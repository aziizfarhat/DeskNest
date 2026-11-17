using System;
using System.Runtime.InteropServices;

namespace DeskNest
{
    // Fonctions Windows utilisées par le trimming à l'inactivité.
    // Aucune dépendance externe : uniquement kernel32/user32.
    public static class ProcessMemory
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr minimum, IntPtr maximum);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        // -1 comme minimum et maximum demande à Windows de libérer les pages
        // qui ne sont pas actuellement utilisées par le processus courant.
        // Le pseudo-handle -1 est équivalent à GetCurrentProcess().
        public static bool TrimWorkingSet()
        {
            try
            {
                return SetProcessWorkingSetSize(new IntPtr(-1), new IntPtr(-1), new IntPtr(-1));
            }
            catch { return false; }
        }

        // Temps écoulé depuis la dernière entrée utilisateur (souris ou clavier),
        // sur toutes les sessions de l'utilisateur courant. La soustraction non
        // signée gère le rebouclage de Environment.TickCount (~49 jours).
        public static TimeSpan IdleTime()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
                uint now = unchecked((uint)Environment.TickCount);
                return TimeSpan.FromMilliseconds(unchecked(now - info.dwTime));
            }
            catch { return TimeSpan.Zero; }
        }
    }
}
