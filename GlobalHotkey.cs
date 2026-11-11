using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DeskNest
{
    public sealed class GlobalHotkey : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        private readonly HwndSource _source;
        public event Action? Pressed;

        public GlobalHotkey()
        {
            var p = new HwndSourceParameters("DeskNestHotkey") { ParentWindow = new IntPtr(-3) }; // message-only
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        // Ctrl + Alt + F
        public bool Register() => Register(MOD_CONTROL | MOD_ALT, 0x46);

        // Ctrl + Shift + Space : recherche globale.
        public bool RegisterSearch() => Register(MOD_CONTROL | 0x4, 0x20);

        private bool Register(uint mods, uint vk)
        {
            // Réenregistrer sans désenregistrer d'abord échoue : l'ancien raccourci resterait actif sans être mis à jour.
            UnregisterHotKey(_source.Handle, 1);
            return RegisterHotKey(_source.Handle, 1, mods | MOD_NOREPEAT, vk);
        }

        public void Unregister() => UnregisterHotKey(_source.Handle, 1);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 1) { Pressed?.Invoke(); handled = true; }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterHotKey(_source.Handle, 1);
            _source.Dispose();
        }
    }
}
