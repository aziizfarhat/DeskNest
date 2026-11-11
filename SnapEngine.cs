using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DeskNest
{
    // Calcul de l'aimantation : bords et centres de l'écran et des autres zones.
    // Tout se fait en pixels physiques (comme GetWindowRect / SetWindowPos),
    // ce qui évite les conversions DPI approximatives.
    public static class SnapEngine
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
        private const uint MonitorDefaultToNearest = 2;

        public static Int32Rect? WindowRect(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return null;
            return GetWindowRect(h, out var r)
                ? new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
                : null;
        }

        // Zone utile (hors barre des tâches) de l'écran qui contient la fenêtre.
        public static Int32Rect WorkArea(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            var monitor = MonitorFromWindow(h, MonitorDefaultToNearest);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref mi))
                return new Int32Rect(mi.rcWork.Left, mi.rcWork.Top,
                    mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
            return new Int32Rect(0, 0,
                (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
        }

        public static List<Int32Rect> OtherRects(NestManager manager, Window except)
        {
            var list = new List<Int32Rect>();
            foreach (var w in manager.Windows)
            {
                if (ReferenceEquals(w, except) || !w.IsVisible) continue;
                var r = WindowRect(w);
                if (r is Int32Rect rr && rr.Width > 0 && rr.Height > 0) list.Add(rr);
            }
            return list;
        }

        public readonly record struct MoveSnap(int X, int Y, int? GuideX, int? GuideY);
        public readonly record struct ResizeSnap(int X, int Y, int W, int H, int? GuideX, int? GuideY);

        private static IEnumerable<int> VerticalLines(Int32Rect screen, IReadOnlyList<Int32Rect> targets)
        {
            yield return screen.X;
            yield return screen.X + screen.Width / 2;
            yield return screen.X + screen.Width;
            foreach (var t in targets)
            {
                yield return t.X;
                yield return t.X + t.Width / 2;
                yield return t.X + t.Width;
            }
        }

        private static IEnumerable<int> HorizontalLines(Int32Rect screen, IReadOnlyList<Int32Rect> targets)
        {
            yield return screen.Y;
            yield return screen.Y + screen.Height / 2;
            yield return screen.Y + screen.Height;
            foreach (var t in targets)
            {
                yield return t.Y;
                yield return t.Y + t.Height / 2;
                yield return t.Y + t.Height;
            }
        }

        public static MoveSnap SnapMove(int x, int y, int w, int h,
            IReadOnlyList<Int32Rect> targets, Int32Rect screen, int threshold)
        {
            int bestAbsX = int.MaxValue, bestDx = 0;
            int? guideX = null;
            foreach (int line in VerticalLines(screen, targets))
            {
                foreach (int anchor in new[] { x, x + w / 2, x + w })
                {
                    int delta = line - anchor;
                    int abs = Math.Abs(delta);
                    // La zone doit rester entièrement dans la zone utile de l'écran après l'aimantation :
                    // sinon son bord supérieur « s'aimanterait » au bas de l'écran, par exemple.
                    if (x + delta < screen.X || x + delta + w > screen.X + screen.Width) continue;
                    if (abs <= threshold && abs < bestAbsX)
                    {
                        bestAbsX = abs;
                        bestDx = delta;
                        guideX = line;
                    }
                }
            }

            int bestAbsY = int.MaxValue, bestDy = 0;
            int? guideY = null;
            foreach (int line in HorizontalLines(screen, targets))
            {
                foreach (int anchor in new[] { y, y + h / 2, y + h })
                {
                    int delta = line - anchor;
                    int abs = Math.Abs(delta);
                    if (y + delta < screen.Y || y + delta + h > screen.Y + screen.Height) continue;
                    if (abs <= threshold && abs < bestAbsY)
                    {
                        bestAbsY = abs;
                        bestDy = delta;
                        guideY = line;
                    }
                }
            }

            return new MoveSnap(x + bestDx, y + bestDy, guideX, guideY);
        }

        // edge : 1 = gauche, 2 = haut, 4 = droite, 8 = bas (un coin combine deux valeurs).
        public static ResizeSnap SnapResize(int x, int y, int w, int h, int edge,
            IReadOnlyList<Int32Rect> targets, Int32Rect screen, int threshold, int minW, int minH)
        {
            int? guideX = null, guideY = null;

            if ((edge & 1) != 0)
            {
                if (Nearest(VerticalLines(screen, targets), x, threshold, out int line))
                {
                    int newW = (x + w) - line;
                    if (newW >= minW) { x = line; w = newW; guideX = line; }
                }
            }
            else if ((edge & 4) != 0)
            {
                if (Nearest(VerticalLines(screen, targets), x + w, threshold, out int line))
                {
                    int newW = line - x;
                    if (newW >= minW) { w = newW; guideX = line; }
                }
            }

            if ((edge & 2) != 0)
            {
                if (Nearest(HorizontalLines(screen, targets), y, threshold, out int line))
                {
                    int newH = (y + h) - line;
                    if (newH >= minH) { y = line; h = newH; guideY = line; }
                }
            }
            else if ((edge & 8) != 0)
            {
                if (Nearest(HorizontalLines(screen, targets), y + h, threshold, out int line))
                {
                    int newH = line - y;
                    if (newH >= minH) { h = newH; guideY = line; }
                }
            }

            return new ResizeSnap(x, y, w, h, guideX, guideY);
        }

        private static bool Nearest(IEnumerable<int> lines, int anchor, int threshold, out int best)
        {
            best = 0;
            int bestAbs = int.MaxValue;
            foreach (int line in lines)
            {
                int abs = Math.Abs(line - anchor);
                if (abs <= threshold && abs < bestAbs)
                {
                    bestAbs = abs;
                    best = line;
                }
            }
            return bestAbs != int.MaxValue;
        }
    }
}
