using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using System.Windows.Automation;

namespace DeskNest
{
    public static class DesktopHelper
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr l);

        private const int SHCNE_ATTRIBUTES = 0x800, SHCNE_UPDATEITEM = 0x2000;
        private const uint SHCNF_PATHW = 0x5;

        private static readonly string[] Roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        // ---------- Éléments du bureau ----------
        public static bool IsOnDesktop(string path)
        {
            try
            {
                var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path).TrimEnd('\\'));
                if (parent == null) return false;
                foreach (var r in Roots)
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    if (string.Equals(System.IO.Path.GetFullPath(r).TrimEnd('\\'), parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        // « Caché + Système » : l'Explorateur masque ces éléments même quand « Afficher les fichiers cachés » est activé
        // (sauf si « Afficher les fichiers protégés du système d'exploitation » est aussi activé).
        private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

        public static bool IsOnPublicDesktop(string path)
        {
            try
            {
                var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path).TrimEnd('\\'));
                var pub = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                return parent != null && !string.IsNullOrEmpty(pub) &&
                       string.Equals(System.IO.Path.GetFullPath(pub).TrimEnd('\\'), parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool IsHiddenOrSystem(string path)
        {
            try { return (File.GetAttributes(path) & HiddenSystem) != 0; } catch { return false; }
        }

        public static bool ShowsProtectedFiles()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                return k?.GetValue("ShowSuperHidden") is int v && v == 1;
            }
            catch { return false; }
        }

        public static bool SetHidden(string path, bool hidden)
        {
            try
            {
                var a = File.GetAttributes(path);
                var n = hidden ? a | HiddenSystem : a & ~HiddenSystem;
                if (n != a) File.SetAttributes(path, n);
                SHChangeNotify(SHCNE_ATTRIBUTES, SHCNF_PATHW, path, IntPtr.Zero);
                SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, path, IntPtr.Zero);
                return true;
            }
            catch { return false; }
        }

        // ---------- Fenêtre « bureau » (pour « Afficher le bureau » / Win+D) ----------
        // Le handle est mis en cache : l'énumération de toutes les fenêtres n'est faite que si le cache
        // n'est plus valide (Explorateur redémarré, bureau recréé...).
        private static readonly object HostLock = new();
        private static IntPtr _cachedHost;

        private static bool HasDefView(IntPtr h) =>
            h != IntPtr.Zero && FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

        // Fenêtre qui héberge les icônes du bureau (Progman ou un WorkerW selon l'état de Windows).
        public static IntPtr FindDesktopHost()
        {
            lock (HostLock)
            {
                if (_cachedHost != IntPtr.Zero && IsWindow(_cachedHost) && HasDefView(_cachedHost))
                    return _cachedHost;

                _cachedHost = FindDesktopHostCore();
                return _cachedHost;
            }
        }

        public static void InvalidateDesktopHost()
        {
            lock (HostLock) _cachedHost = IntPtr.Zero;
        }

        private static IntPtr FindDesktopHostCore()
        {
            var progman = FindWindow("Progman", null);
            if (HasDefView(progman)) return progman;

            IntPtr found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                if (Cls(h) == "WorkerW" && HasDefView(h))
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // Vrai si la fenêtre a est au-dessus de la fenêtre b dans l'ordre d'empilement.
        public static bool IsAbove(IntPtr a, IntPtr b)
        {
            const uint GW_HWNDPREV = 3;
            int guard = 0;
            for (var w = GetWindow(b, GW_HWNDPREV); w != IntPtr.Zero && guard < 2000; w = GetWindow(w, GW_HWNDPREV), guard++)
                if (w == a) return true;
            return false;
        }

        // Vrai si le bureau, la barre des tâches ou une de nos fenêtres a le focus.
        public static bool IsDesktopForeground(IntPtr host)
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (fg == host) return true;
            var c = Cls(fg);
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return true;
            GetWindowThreadProcessId(fg, out var pid);
            return pid == (uint)Environment.ProcessId;
        }

        // ---------- Double-clic sur le bureau ----------
        private static string Cls(IntPtr h)
        {
            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // Vrai si le point est sur le bureau lui-même (et pas sur une fenêtre ou une fenêtre Explorateur).
        public static bool IsDesktopBackground(int x, int y)
        {
            var h = WindowFromPoint(new POINT { X = x, Y = y });
            if (h == IntPtr.Zero) return false;
            var c = Cls(h);
            if (c == "SysListView32") { h = GetParent(h); c = Cls(h); }
            if (c == "SHELLDLL_DefView") { h = GetParent(h); c = Cls(h); }
            return c == "Progman" || c == "WorkerW";
        }

        // Vrai si une icône du bureau se trouve sous le point (double-clic pour ouvrir : on ne masque pas).
        public static bool IsIconAt(int x, int y)
        {
            try
            {
                var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                return el != null && el.Current.ControlType == ControlType.ListItem;
            }
            catch { return false; }
        }
    }
}
