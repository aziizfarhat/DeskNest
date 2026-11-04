using System;
using System.Runtime.InteropServices;

namespace DeskNest
{
    // Hook souris global (bas niveau) pour détecter un double-clic gauche.
    public sealed class MouseHook : IDisposable
    {
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201;
        private readonly HookProc _proc;
        private IntPtr _hook;
        private bool _hasLast;
        private uint _lastTime;
        private POINT _lastPt;

        public event Action<int, int>? DoubleClick;

        public MouseHook()
        {
            _proc = Callback; // garder la référence pour éviter le garbage collector
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
            {
                var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int dx = GetSystemMetrics(36) / 2, dy = GetSystemMetrics(37) / 2; // SM_CXDOUBLECLK / SM_CYDOUBLECLK
                if (_hasLast && s.time - _lastTime <= GetDoubleClickTime()
                    && Math.Abs(s.pt.X - _lastPt.X) <= dx && Math.Abs(s.pt.Y - _lastPt.Y) <= dy)
                {
                    _hasLast = false;
                    DoubleClick?.Invoke(s.pt.X, s.pt.Y);
                }
                else { _hasLast = true; _lastTime = s.time; _lastPt = s.pt; }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
