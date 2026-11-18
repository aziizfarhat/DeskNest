using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace DeskNest
{
    // Placement des nouvelles zones dans l'écran (zone utile) qui contient le curseur.
    // Toutes les coordonnées renvoyées sont en DIP WPF, comme Window.Left / Top.
    public static class CursorPlacement
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

        public readonly record struct Context(Point Cursor, Rect WorkArea);

        public static Context Get()
        {
            GetCursorPos(out var p);
            var monitors = DisplayProfiles.CurrentMonitors();
            var monitor = monitors.FirstOrDefault(m => p.X >= m.X && p.X < m.X + m.Width &&
                                                       p.Y >= m.Y && p.Y < m.Y + m.Height)
                          ?? monitors.FirstOrDefault();
            if (monitor == null)
                return new Context(new Point(p.X, p.Y),
                    new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));

            double scale = monitor.DpiScale > 0 ? monitor.DpiScale : 1;
            return new Context(
                new Point(p.X / scale, p.Y / scale),
                new Rect(monitor.WorkX / scale, monitor.WorkY / scale,
                         monitor.WorkWidth / scale, monitor.WorkHeight / scale));
        }

        // Position centrée sur le curseur, clampée dans la zone utile et décalée
        // jusqu'à ne recouvrir aucune fenêtre de zone déjà visible.
        public static Point FindFreeSpot(Context context, double width, double height,
                                         IReadOnlyList<Rect> occupied)
        {
            var work = context.WorkArea;
            double w = Math.Min(Math.Max(width, 120), Math.Max(120, work.Width - 16));
            double h = Math.Min(Math.Max(height, 60), Math.Max(60, work.Height - 16));
            double left = Clamp(context.Cursor.X - w / 2, work.Left + 8, work.Right - w - 8);
            double top = Clamp(context.Cursor.Y - h / 2, work.Top + 8, work.Bottom - h - 8);

            for (int i = 0; i < 60; i++)
            {
                var rect = new Rect(left, top, w, h);
                if (occupied == null || !occupied.Any(r => r.Width > 0 && r.Height > 0 && rect.IntersectsWith(r)))
                    return new Point(left, top);

                left += 30;
                top += 30;
                if (left + w > work.Right - 8) left = work.Left + 8;
                if (top + h > work.Bottom - 8) top = work.Top + 8;
            }
            return new Point(left, top);
        }

        public static double Clamp(double value, double min, double max) =>
            max < min ? min : Math.Min(Math.Max(value, min), max);
    }
}
