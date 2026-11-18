using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskNest
{
    // Règle D4 : on repart du profil le plus proche, on conserve l'ancrage aux bords
    // de la zone de travail d'origine, puis on cadre dans la zone de travail cible.
    public static class DisplayAdaptation
    {
        private const double MinWidth = 120;   // NestWindow.xaml MinWidth
        private const double MinHeight = 60;   // NestWindow.xaml MinHeight (taille développée)
        private const double TitleHeight = 30; // NestWindow.xaml.cs:399 (28 + 2)

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

        // Le profil de départ est celui dont la configuration d'écrans recouvre le plus
        // la configuration cible ; à défaut de recouvrement, on garde le profil actif.
        public static DisplayProfile? SelectSourceProfile(
            AppData data, IReadOnlyList<MonitorDescriptor> targets, DisplayProfile? active)
        {
            if (data?.Profiles == null) return active;
            var best = active;
            var bestScore = Score(active, data, targets);
            foreach (var profile in data.Profiles)
            {
                if (profile == null || profile == active) continue;
                var score = Score(profile, data, targets);
                if (score > bestScore + 0.5)
                {
                    best = profile;
                    bestScore = score;
                }
            }
            return best;
        }

        public static void Adapt(AppData data, IReadOnlyList<MonitorDescriptor> targets,
            DisplayProfile? source, IReadOnlyList<MonitorDescriptor> sourceMonitors)
        {
            var sources = sourceMonitors != null && sourceMonitors.Count > 0
                ? sourceMonitors
                : DisplayProfiles.GetMonitorsFor(data, source?.Key);
            if (sources.Count == 0) sources = targets;
            if (targets == null || targets.Count == 0) return;

            foreach (var nest in data.Nests)
            {
                var geometry = SourceGeometry(source, nest);
                var from = FindSourceMonitor(geometry, sources);
                var to = FindTargetMonitor(from, targets);
                if (from == null || to == null) continue;

                var adapted = AdaptGeometry(geometry, from, to, nest.RolledUp);
                DisplayProfiles.ApplyGeometry(nest, adapted);
                Diag.Log($"profils : adaptation de '{nest.Title}' de {Describe(geometry)} vers {Describe(adapted)}");
            }
        }

        public static void AdaptMissing(NestData nest, IReadOnlyList<MonitorDescriptor> targets,
            DisplayProfile? source, IReadOnlyList<MonitorDescriptor> sourceMonitors)
        {
            if (nest == null || targets == null || targets.Count == 0) return;
            var geometry = SourceGeometry(source, nest);
            var from = FindSourceMonitor(geometry, sourceMonitors);
            var to = FindTargetMonitor(from, targets);
            if (from == null || to == null) return;

            var adapted = AdaptGeometry(geometry, from, to, nest.RolledUp);
            DisplayProfiles.ApplyGeometry(nest, adapted);
            Diag.Log($"profils : zone absente '{nest.Title}' placée en {Describe(adapted)}");
        }

        private static NestGeometry SourceGeometry(DisplayProfile? source, NestData nest)
        {
            if (source != null && source.Nests.TryGetValue(nest.Id.ToString(), out var saved) && saved != null)
                return new NestGeometry { Left = saved.Left, Top = saved.Top, Width = saved.Width, Height = saved.Height };
            return DisplayProfiles.GeometryOf(nest);
        }

        private static NestGeometry AdaptGeometry(NestGeometry geometry, MonitorDescriptor from,
            MonitorDescriptor to, bool rolledUp)
        {
            var source = WorkAreaDips(from);
            var target = WorkAreaDips(to);

            // Distance aux bords les plus proches : le côté choisi (gauche/droite,
            // haut/bas) est conservé, y compris pour les zones right-aligned.
            double distanceLeft = geometry.Left - source.Left;
            double distanceRight = source.Right - (geometry.Left + geometry.Width);
            bool anchorRight = distanceRight < distanceLeft;
            double horizontal = anchorRight ? distanceRight : distanceLeft;

            double effectiveHeight = rolledUp ? Math.Max(geometry.Height, TitleHeight) : geometry.Height;
            double distanceTop = geometry.Top - source.Top;
            double distanceBottom = source.Bottom - (geometry.Top + effectiveHeight);
            bool anchorBottom = distanceBottom < distanceTop;
            double vertical = anchorBottom ? distanceBottom : distanceTop;

            double width = ClampSize(geometry.Width, target.Width, MinWidth);
            double height = ClampSize(effectiveHeight, target.Height, MinHeight);

            double left = anchorRight ? target.Right - width - horizontal : target.Left + horizontal;
            double top = anchorBottom ? target.Bottom - height - vertical : target.Top + vertical;

            // Le rectangle entier (et la taille développée d'une zone réduite) reste dans la zone de travail.
            left = Clamp(left, target.Left, target.Right - width);
            top = Clamp(top, target.Top, target.Bottom - height);

            return new NestGeometry { Left = left, Top = top, Width = width, Height = height };
        }

        private static DipRect WorkAreaDips(MonitorDescriptor monitor)
        {
            double scale = monitor.DpiScale > 0 ? monitor.DpiScale : 1;
            return new DipRect(monitor.WorkX / scale, monitor.WorkY / scale,
                monitor.WorkWidth / scale, monitor.WorkHeight / scale);
        }

        private static MonitorDescriptor? FindSourceMonitor(NestGeometry geometry, IReadOnlyList<MonitorDescriptor>? monitors)
        {
            if (monitors == null || monitors.Count == 0) return null;
            var rect = new DipRect(geometry.Left, geometry.Top,
                Math.Max(1, geometry.Width), Math.Max(1, geometry.Height));

            MonitorDescriptor? best = null;
            double bestOverlap = 0;
            foreach (var monitor in monitors)
            {
                double overlap = OverlapArea(rect, WorkAreaDips(monitor));
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = monitor;
                }
            }
            if (best != null) return best;

            // Aucune intersection (positions obsolètes) : écran le plus proche.
            best = monitors[0];
            double bestDistance = double.MaxValue;
            foreach (var monitor in monitors)
            {
                var work = WorkAreaDips(monitor);
                double dx = (rect.Left + rect.Width / 2) - (work.Left + work.Width / 2);
                double dy = (rect.Top + rect.Height / 2) - (work.Top + work.Height / 2);
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = monitor;
                }
            }
            return best;
        }

        private static MonitorDescriptor? FindTargetMonitor(MonitorDescriptor? source, IReadOnlyList<MonitorDescriptor> targets)
        {
            if (targets.Count == 0) return null;
            if (source == null) return targets.FirstOrDefault(m => m.IsPrimary) ?? targets[0];

            // Même écran physique : on garde sa zone de travail actuelle.
            var same = targets.FirstOrDefault(m =>
                m.X == source.X && m.Y == source.Y && m.Width == source.Width && m.Height == source.Height &&
                Math.Abs(m.DpiScale - source.DpiScale) < 0.001 && m.IsPrimary == source.IsPrimary);
            if (same != null) return same;

            // Écran débranché : plus grand recouvrement, sinon l'écran principal.
            MonitorDescriptor? best = null;
            double bestOverlap = 0;
            foreach (var target in targets)
            {
                double overlap = PhysicalOverlap(source, target);
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = target;
                }
            }
            return best ?? targets.FirstOrDefault(m => m.IsPrimary) ?? targets[0];
        }

        private static double Score(DisplayProfile? profile, AppData data, IReadOnlyList<MonitorDescriptor> targets)
        {
            if (profile == null || targets == null || targets.Count == 0) return 0;
            var sources = DisplayProfiles.GetMonitorsFor(data, profile.Key);
            double total = 0;
            foreach (var source in sources)
            {
                double best = 0;
                foreach (var target in targets)
                    best = Math.Max(best, PhysicalOverlap(source, target));
                total += best;
            }
            return total;
        }

        private static double PhysicalOverlap(MonitorDescriptor a, MonitorDescriptor b)
        {
            double width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            double height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return width > 0 && height > 0 ? width * height : 0;
        }

        private static double OverlapArea(DipRect a, DipRect b)
        {
            double width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            double height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            return width > 0 && height > 0 ? width * height : 0;
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

        private static string Describe(NestGeometry g) =>
            $"({g.Left:0},{g.Top:0} {g.Width:0}×{g.Height:0})";
    }
}
