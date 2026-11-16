using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskNest
{
    // Suivi événementiel minimal du bureau : évite de sonder le z-order plusieurs fois par seconde.
    // Un unique timer de secours (dans NestManager) couvre les cas où Windows n'envoie aucun événement.
    public sealed class DesktopWatcher : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint EVENT_OBJECT_HIDE = 0x8003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc,
            uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

        private readonly Action _onChange;
        private readonly WinEventProc _proc; // référence conservée : sinon le délégué peut être collecté
        private readonly IntPtr[] _hooks = new IntPtr[3];

        public DesktopWatcher(Action onChange)
        {
            _onChange = onChange;
            _proc = OnWinEvent;
        }

        public void Start()
        {
            if (_hooks[0] != IntPtr.Zero) return;
            _hooks[0] = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            _hooks[1] = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE, IntPtr.Zero, _proc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            _hooks[2] = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _proc, 0, 0,
                WINEVENT_OUTOFCONTEXT); // y compris notre propre processus : « Afficher le bureau » nous minimise aussi
        }

        private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            // SHOW/HIDE est émis pour presque chaque fenêtre : ne réveiller que pour le bureau / la barre des tâches.
            if ((evt == EVENT_OBJECT_SHOW || evt == EVENT_OBJECT_HIDE) && !IsShellWindow(hwnd)) return;
            _onChange();
        }

        private static bool IsShellWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            var c = sb.ToString();
            return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
        }

        public void Dispose()
        {
            for (int i = 0; i < _hooks.Length; i++)
            {
                if (_hooks[i] == IntPtr.Zero) continue;
                UnhookWinEvent(_hooks[i]);
                _hooks[i] = IntPtr.Zero;
            }
        }
    }
}
