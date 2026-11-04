using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using IOPath = System.IO.Path;

namespace DeskNest
{
    // Dossier géré par DeskNest : les éléments du bureau glissés dans une zone y sont rangés
    // (et remis à leur place d'origine quand on les retire de la zone).
    public static class DesktopStore
    {
        private static bool _migrationResolved;
        private static bool _useLegacyStore;
        private static bool _legacyStoreWasMoved;

        public static bool MigrateLegacyStore(out bool moved)
        {
            moved = false;
            if (_migrationResolved)
            {
                moved = _legacyStoreWasMoved;
                return !_useLegacyStore;
            }

            if (!Directory.Exists(LegacyStoreDir))
            {
                _migrationResolved = true;
                return true;
            }

            if (Directory.Exists(NewStoreDir))
            {
                // Les deux emplacements existent : conserver l'ancien pour ne pas casser les chemins enregistrés.
                _useLegacyStore = true;
                _migrationResolved = true;
                return false;
            }

            try
            {
                Directory.Move(LegacyStoreDir, NewStoreDir);
                _legacyStoreWasMoved = true;
                moved = true;
                _migrationResolved = true;
                return true;
            }
            catch
            {
                // En cas d'échec, continuer à utiliser l'ancien dossier plutôt que perdre les éléments.
                _useLegacyStore = true;
                _migrationResolved = true;
                return false;
            }
        }

        private static string ProfileDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static string LegacyStoreDir => IOPath.Combine(ProfileDir, "NestsLite");
        private static string NewStoreDir => IOPath.Combine(ProfileDir, "DeskNest");

        public static string Dir
        {
            get
            {
                MigrateLegacyStore(out _);
                var root = _useLegacyStore ? LegacyStoreDir : NewStoreDir;
                return IOPath.Combine(root, "Items");
            }
        }

        public static string UniquePath(string dir, string name, bool isDir)
        {
            var target = IOPath.Combine(dir, name);
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
            string stem = isDir ? name : IOPath.GetFileNameWithoutExtension(name);
            string ext = isDir ? "" : IOPath.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                var t = IOPath.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(t) && !Directory.Exists(t)) return t;
            }
            return IOPath.Combine(dir, $"{stem}-{Guid.NewGuid():N}{ext}");
        }

        // Déplace src vers dst (renommage atomique sur le même volume, copie + suppression sinon).
        // denied = droits insuffisants.
        public static bool TryMove(string src, string dst, out bool denied)
        {
            denied = false;
            try
            {
                var dir = IOPath.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (Directory.Exists(src)) Directory.Move(src, dst);
                else File.Move(src, dst);
                return true;
            }
            catch (UnauthorizedAccessException) { denied = true; return false; }
            catch (IOException)
            {
                // Volume différent (ou autre erreur Win32) : SHFileOperation sait copier puis supprimer.
                return ShellMove(src, dst);
            }
            catch { return false; }
        }

        // Même opération avec une demande d'autorisation administrateur (UAC), pour le Bureau public.
        // On relance notre propre exécutable en mode « --move » : pas de cmd.exe, donc pas d'expansion
        // des caractères comme % ou & dans les noms de fichiers.
        public static bool TryMoveElevated(string src, string dst)
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;

                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = $"--move {Quote(src)} {Quote(dst)}"
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(30_000);
                if (!p.HasExited)
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                return p.ExitCode == 0 && (File.Exists(dst) || Directory.Exists(dst));
            }
            catch (Win32Exception) { return false; } // autorisation refusée
            catch { return false; }
        }

        // Appelé au démarrage : true si ce processus est le déplacement élevé demandé par TryMoveElevated.
        public static bool TryRunElevatedMove(string[] args)
        {
            if (args.Length < 3 || !string.Equals(args[0], "--move", StringComparison.OrdinalIgnoreCase)) return false;
            bool ok = false;
            try
            {
                var dir = IOPath.GetDirectoryName(args[2]);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                ok = ShellMove(args[1], args[2]);
            }
            catch { }
            Environment.ExitCode = ok ? 0 : 1;
            return true;
        }

        // Échappement des barres obliques finales pour la ligne de commande Windows.
        private static string Quote(string path)
        {
            int n = path.Length;
            int trailing = 0;
            while (trailing < n && path[n - 1 - trailing] == '\\') trailing++;
            return "\"" + path + new string('\\', trailing) + "\"";
        }

        // ---------- SHFileOperation (déplacement fiable, y compris entre volumes) ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        private const uint FO_MOVE = 0x0001;
        private const ushort FOF_NO_UI = 0x0004 | 0x0010 | 0x0200 | 0x0400; // SILENT | NOCONFIRMATION | NOCONFIRMMKDIR | NOERRORUI

        private static bool ShellMove(string src, string dst)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_MOVE,
                pFrom = src + "\0", // liste à double zéro terminal
                pTo = dst + "\0",
                fFlags = FOF_NO_UI
            };
            try
            {
                int r = SHFileOperation(ref op);
                return r == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }
    }
}
