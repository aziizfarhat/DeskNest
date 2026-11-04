using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskNest
{
    // Actions de l'Explorateur Windows : Propriétés, Ouvrir avec, Afficher dans l'Explorateur.
    public static class ShellActions
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string? lpVerb;
            public string? lpFile;
            public string? lpParameters;
            public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIconOrMonitor;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        // Fenêtre « Propriétés » de l'Explorateur.
        public static void Properties(string path)
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
                lpVerb = "properties",
                lpFile = path,
                nShow = 5
            };
            ShellExecuteEx(ref info);
        }

        public static void OpenWith(string path)
        {
            try { Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"") { UseShellExecute = false }); }
            catch { }
        }

        public static void ShowInExplorer(string path)
        {
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
        }
    }
}
