using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskNest
{
    // Filet de sécurité D5 : au démarrage et après D3/D4, aucune zone ne doit être
    // entièrement en dehors des écrans. Les déplacements automatiques sont journalisés.
    public static class DisplaySafety
    {
        private const double MinWidth = 120;
        private const double MinHeight = 60;

        private readonly struct DipRect
        {
            public readonly double Left;
            public readonly double Top;
            public readonly double Width;
            public readonly double Height;

            public DipRect(double left, double top, double width, double height)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

            public double Right => Left + Width;
            public double Bottom => Top + Height;
        }

        // Retourne le nombre de zones déplacées.
        public static int EnsureVisible(AppData data, IReadOnlyList<MonitorDescriptor> monitors)
        {
            if (data?.Nests == null || monitors == null || monitors.Count == 0) return 0;
            var areas = monitors.Select(WorkAreaDips).ToList();
            int adjusted = 0;

            foreach (var nest in data.Nests)
            {
                if (nest == null) continue;
                var rect = new DipRect(nest.Left, nest.Top,
                    Math.Max(MinWidth, nest.Width),
                    Math.Max(MinHeight, nest.Height)); // hauteur développée, même si la zone est réduite
                if (IntersectsAny(rect, areas)) continue;

                var target = Nearest(rect, monitors);
                if (target == null) continue;
                var work = WorkAreaDips(target);
                double width = ClampSize(rect.Width, work.Width, MinWidth);
                double height = ClampSize(rect.Height, work.Height, MinHeight);
                double left = Clamp(rect.Left, work.Left, work.Right - width);
                double top = Clamp(rect.Top, work.Top, work.Bottom - height);

                Diag.Log($"profils : zone '{nest.Title}' hors écran, déplacée de " +
                         $"({rect.Left:0},{rect.Top:0} {rect.Width:0}×{rect.Height:0}) vers " +
                         $"({left:0},{top:0} {width:0}×{height:0})");
                DisplayProfiles.ApplyGeometry(nest, new NestGeometry
                {
                    Left = left, Top = top, Width = width, Height = height
                });
                adjusted++;
            }
            return adjusted;
        }

        private static DipRect WorkAreaDips(MonitorDescriptor monitor)
        {
            double scale = monitor.DpiScale > 0 ? monitor.DpiScale : 1;
            return new DipRect(monitor.WorkX / scale, monitor.WorkY / scale,
                monitor.WorkWidth / scale, monitor.WorkHeight / scale);
        }

        private static bool IntersectsAny(DipRect rect, IReadOnlyList<DipRect> areas)
        {
            foreach (var area in areas)
            {
                if (rect.Right > area.Left && rect.Left < area.Right &&
                    rect.Bottom > area.Top && rect.Top < area.Bottom)
                    return true;
            }
            return false;
        }

        private static MonitorDescriptor? Nearest(DipRect rect, IReadOnlyList<MonitorDescriptor> monitors)
        {
            MonitorDescriptor? best = null;
            double bestOverlap = 0;
            foreach (var monitor in monitors)
            {
                var area = WorkAreaDips(monitor);
                double width = Math.Min(rect.Right, area.Right) - Math.Max(rect.Left, area.Left);
                double height = Math.Min(rect.Bottom, area.Bottom) - Math.Max(rect.Top, area.Top);
                double overlap = width > 0 && height > 0 ? width * height : 0;
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = monitor;
                }
            }
            if (best != null) return best;

            best = monitors[0];
            double bestDistance = double.MaxValue;
            foreach (var monitor in monitors)
            {
                var area = WorkAreaDips(monitor);
                double dx = (rect.Left + rect.Width / 2) - (area.Left + area.Width / 2);
                double dy = (rect.Top + rect.Height / 2) - (area.Top + area.Height / 2);
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = monitor;
                }
            }
            return best;
        }

        private static double ClampSize(double value, double available, double minimum)
        {
            if (available <= 0) return minimum;
            double floor = Math.Min(minimum, available);
            return Math.Min(Math.Max(value, floor), available);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min) max = min;
            return Math.Min(Math.Max(value, min), max);
        }
    }
}
