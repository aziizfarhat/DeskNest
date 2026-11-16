using System;
using System.Runtime.InteropServices;

namespace DeskNest
{
    // Hook souris global (bas niveau) pour détecter un double-clic gauche.
    // Le callback est appelé sur le thread du hook : il doit rester minimal.
    // Les métriques système sont donc lues une seule fois, dans le constructeur,
    // et le handle de la fenêtre sous le curseur est capturé au moment du clic
    // pour que le traitement différé ne dépende pas de l'état de l'écran ensuite.
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
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);

        private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201;
        private const int SM_CXDOUBLECLK = 36, SM_CYDOUBLECLK = 37;

        private readonly HookProc _proc;
        private readonly uint _doubleClickTime;
        private readonly int _toleranceX;
        private readonly int _toleranceY;
        private IntPtr _hook;
        private bool _hasLast;
        private uint _lastTime;
        private POINT _lastPt;

        // X, Y et fenêtre sous le curseur au moment du double-clic.
        public event Action<int, int, IntPtr>? DoubleClick;

        public MouseHook()
        {
            // Lu une fois : GetSystemMetrics/GetDoubleClickTime à chaque clic
            // ralentiraient inutilement le callback du hook bas niveau.
            _doubleClickTime = GetDoubleClickTime();
            _toleranceX = GetSystemMetrics(SM_CXDOUBLECLK) / 2;
            _toleranceY = GetSystemMetrics(SM_CYDOUBLECLK) / 2;

            _proc = Callback; // garder la référence pour éviter le garbage collector
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
            {
                var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (_hasLast && s.time - _lastTime <= _doubleClickTime
                    && Math.Abs(s.pt.X - _lastPt.X) <= _toleranceX
                    && Math.Abs(s.pt.Y - _lastPt.Y) <= _toleranceY)
                {
                    _hasLast = false;
                    // Un seul appel Win32 supplémentaire : le handle est figé
                    // maintenant, le reste du travail est fait hors du hook.
                    DoubleClick?.Invoke(s.pt.X, s.pt.Y, WindowFromPoint(s.pt));
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
