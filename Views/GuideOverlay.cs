using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskNest
{
    // Lignes d'alignement affichées pendant le déplacement ou le redimensionnement d'une zone.
    // Deux fenêtres très fines (une verticale, une horizontale) sont positionnées en pixels
    // physiques : aucune conversion DPI n'est nécessaire, le trait reste exact sur tous les écrans.
    // Les fenêtres sont transparentes aux clics et ne prennent jamais le focus.
    public sealed class GuideOverlay : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
        private const uint SWP_NOACTIVATE = 0x10;
        private const int SW_HIDE = 0, SW_SHOWNA = 8;

        private static readonly IntPtr HwndTopmost = new(-1);

        private readonly Window _vertical;
        private readonly Window _horizontal;

        public GuideOverlay()
        {
            _vertical = CreateLine();
            _horizontal = CreateLine();
        }

        private static Window CreateLine()
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x4C, 0x9A, 0xFF)),
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                IsHitTestVisible = false,
                Focusable = false,
                Left = -10000,
                Top = -10000,
                Width = 1,
                Height = 1,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            w.Show();
            var h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
            ShowWindow(h, SW_HIDE);
            return w;
        }

        private static int Thickness(IntPtr h)
        {
            uint dpi = 96;
            try { dpi = GetDpiForWindow(h); } catch { }
            if (dpi < 96) dpi = 96;
            return Math.Max(2, (int)Math.Round(2.0 * dpi / 96.0));
        }

        // verticalX / horizontalY sont en pixels physiques ; area délimite la zone couverte.
        public void Update(int? verticalX, int? horizontalY, Int32Rect area)
        {
            var vh = new WindowInteropHelper(_vertical).Handle;
            var hh = new WindowInteropHelper(_horizontal).Handle;
            if (vh == IntPtr.Zero || hh == IntPtr.Zero) return;

            if (verticalX is int vx && area.Height > 0)
            {
                int t = Thickness(vh);
                SetWindowPos(vh, HwndTopmost, vx - t / 2, area.Y, t, area.Height, SWP_NOACTIVATE);
                ShowWindow(vh, SW_SHOWNA);
            }
            else ShowWindow(vh, SW_HIDE);

            if (horizontalY is int hy && area.Width > 0)
            {
                int t = Thickness(hh);
                SetWindowPos(hh, HwndTopmost, area.X, hy - t / 2, area.Width, t, SWP_NOACTIVATE);
                ShowWindow(hh, SW_SHOWNA);
            }
            else ShowWindow(hh, SW_HIDE);
        }

        public void Hide()
        {
            var vh = new WindowInteropHelper(_vertical).Handle;
            var hh = new WindowInteropHelper(_horizontal).Handle;
            if (vh != IntPtr.Zero) ShowWindow(vh, SW_HIDE);
            if (hh != IntPtr.Zero) ShowWindow(hh, SW_HIDE);
        }

        public void Dispose()
        {
            try { _vertical.Close(); } catch { }
            try { _horizontal.Close(); } catch { }
        }
    }
}
