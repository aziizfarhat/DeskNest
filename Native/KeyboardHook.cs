using System;
using System.Runtime.InteropServices;

namespace DeskNest
{
    // Hook clavier global (bas niveau). La palette de recherche l'utilise pour
    // conserver la frappe même si Windows lui a retiré le premier plan : les
    // touches destinées à une autre application sont avalées puis la palette
    // reprend le focus.
    public sealed class KeyboardHook : IDisposable
    {
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

        private readonly HookProc _proc;
        private IntPtr _hook;

        // Retourne true pour avaler la touche (elle ne doit pas atteindre une autre application).
        public event Func<int, bool>? KeyDown;

        public KeyboardHook()
        {
            _proc = Callback; // garder la référence pour éviter le garbage collector
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool swallow = false;
                    try { swallow = KeyDown?.Invoke((int)info.vkCode) == true; }
                    catch { }
                    if (swallow) return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
