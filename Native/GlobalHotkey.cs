using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DeskNest
{
    public sealed class GlobalHotkey : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;
        private readonly HwndSource _source;
        public event Action? Pressed;

        public GlobalHotkey()
        {
            var p = new HwndSourceParameters("DeskNestHotkey") { ParentWindow = new IntPtr(-3) }; // message-only
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        // Enregistre une combinaison au format « Ctrl+Alt+F ». Réenregistrer sans
        // désenregistrer d'abord échoue : l'ancien raccourci resterait actif.
        public bool Register(string spec)
        {
            if (!HotkeyText.TryParse(spec, out uint mods, out uint vk)) return false;
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

    // Conversion entre une combinaison Win32 (modificateurs + touche virtuelle)
    // et le texte stocké dans les paramètres.
    public static class HotkeyText
    {
        public const uint ModAlt = 0x1;
        public const uint ModControl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;

        public const string DefaultToggle = "Ctrl+Alt+F";
        public const string DefaultSearch = "Ctrl+Shift+Space";

        public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            foreach (var part in parts)
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        continue;
                    case "alt":
                        modifiers |= ModAlt;
                        continue;
                    case "shift":
                        modifiers |= ModShift;
                        continue;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        continue;
                }

                if (virtualKey != 0) return false; // une seule touche finale
                if (!TryParseKey(part, out virtualKey)) return false;
            }

            return virtualKey != 0;
        }

        public static string Format(uint modifiers, uint virtualKey)
        {
            var parts = new List<string>();
            if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & ModShift) != 0) parts.Add("Shift");
            if ((modifiers & ModWin) != 0) parts.Add("Win");
            parts.Add(KeyName(virtualKey));
            return string.Join("+", parts);
        }

        public static string Format(ModifierKeys modifiers, Key key) =>
            Format(ToWin32(modifiers), (uint)KeyInterop.VirtualKeyFromKey(key));

        public static uint ToWin32(ModifierKeys modifiers)
        {
            uint result = 0;
            if ((modifiers & ModifierKeys.Control) != 0) result |= ModControl;
            if ((modifiers & ModifierKeys.Alt) != 0) result |= ModAlt;
            if ((modifiers & ModifierKeys.Shift) != 0) result |= ModShift;
            if ((modifiers & ModifierKeys.Windows) != 0) result |= ModWin;
            return result;
        }

        // Texte canonique, ou la valeur d'origine si elle est illisible.
        public static string Normalize(string? text, string fallback) =>
            TryParse(text, out uint mods, out uint vk) ? Format(mods, vk) : fallback;

        public static string Display(string? text, string fallback) =>
            TryParse(text, out uint mods, out uint vk) ? Format(mods, vk) : text ?? fallback;

        private static bool TryParseKey(string text, out uint virtualKey)
        {
            virtualKey = 0;
            if (text.Length == 1)
            {
                char c = char.ToUpperInvariant(text[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    virtualKey = c;
                    return true;
                }
            }

            string name = text.Replace(" ", "");
            switch (name.ToLowerInvariant())
            {
                case "space": virtualKey = 0x20; return true;
                case "tab": virtualKey = 0x09; return true;
                case "enter":
                case "return": virtualKey = 0x0D; return true;
                case "esc":
                case "escape": virtualKey = 0x1B; return true;
                case "backspace": virtualKey = 0x08; return true;
                case "delete":
                case "del": virtualKey = 0x2E; return true;
                case "insert":
                case "ins": virtualKey = 0x2D; return true;
                case "home": virtualKey = 0x24; return true;
                case "end": virtualKey = 0x23; return true;
                case "pageup":
                case "pgup": virtualKey = 0x21; return true;
                case "pagedown":
                case "pgdn": virtualKey = 0x22; return true;
                case "left":
                case "arrowleft": virtualKey = 0x25; return true;
                case "up":
                case "arrowup": virtualKey = 0x26; return true;
                case "right":
                case "arrowright": virtualKey = 0x27; return true;
                case "down":
                case "arrowdown": virtualKey = 0x28; return true;
            }

            if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f') &&
                int.TryParse(name[1..], out int f) && f >= 1 && f <= 24)
            {
                virtualKey = (uint)(0x70 + f - 1);
                return true;
            }

            if (Enum.TryParse<Key>(name, true, out var key))
            {
                int vk = KeyInterop.VirtualKeyFromKey(key);
                if (vk > 0)
                {
                    virtualKey = (uint)vk;
                    return true;
                }
            }

            return false;
        }

        private static string KeyName(uint virtualKey)
        {
            if (virtualKey >= 'A' && virtualKey <= 'Z') return ((char)virtualKey).ToString();
            if (virtualKey >= '0' && virtualKey <= '9') return ((char)virtualKey).ToString();
            if (virtualKey >= 0x70 && virtualKey <= 0x87) return "F" + (virtualKey - 0x70 + 1);

            switch (virtualKey)
            {
                case 0x20: return "Space";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x08: return "Backspace";
                case 0x2E: return "Delete";
                case 0x2D: return "Insert";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
            }

            var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
            return key == Key.None ? virtualKey.ToString("X2") : key.ToString();
        }
    }
}
