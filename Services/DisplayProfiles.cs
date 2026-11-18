using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DeskNest
{
    // Description d'un écran. Les bounds et la zone de travail sont en pixels physiques ;
    // DpiScale est le facteur appliqué à cet écran (1.0 = 100 %).
    public sealed class MonitorDescriptor
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int WorkX { get; }
        public int WorkY { get; }
        public int WorkWidth { get; }
        public int WorkHeight { get; }
        public double DpiScale { get; }
        public bool IsPrimary { get; }

        public int Right => X + Width;
        public int Bottom => Y + Height;
        public int WorkRight => WorkX + WorkWidth;
        public int WorkBottom => WorkY + WorkHeight;

        public MonitorDescriptor(int x, int y, int width, int height, double dpiScale, bool isPrimary,
            int? workX = null, int? workY = null, int? workWidth = null, int? workHeight = null)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            DpiScale = Math.Round(dpiScale > 0 ? dpiScale : 1, 4);
            IsPrimary = isPrimary;
            WorkX = workX ?? x;
            WorkY = workY ?? y;
            WorkWidth = workWidth > 0 ? workWidth!.Value : width;
            WorkHeight = workHeight > 0 ? workHeight!.Value : height;
        }
    }

    // Source des écrans. L'application utilise le provider Win32 ; les tests en injectent un faux.
    public interface IDisplayLayoutProvider
    {
        IReadOnlyList<MonitorDescriptor> GetMonitors();
    }

    // Clés de profils, capture de géométrie et helpers partagés par D2 à D5.
    public static class DisplayProfiles
    {
        private static readonly object ProviderGate = new();
        private static IDisplayLayoutProvider _provider = new Win32DisplayLayoutProvider();

        public static void UseProvider(IDisplayLayoutProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (ProviderGate) _provider = provider;
        }

        public static void UseDefaultProvider()
        {
            lock (ProviderGate) _provider = new Win32DisplayLayoutProvider();
        }

        public static IReadOnlyList<MonitorDescriptor> CurrentMonitors()
        {
            IDisplayLayoutProvider provider;
            lock (ProviderGate) provider = _provider;
            try
            {
                var monitors = provider.GetMonitors();
                if (monitors != null && monitors.Count > 0) return monitors;
            }
            catch (Exception ex)
            {
                Diag.Log("écrans : " + ex.Message);
            }
            return Win32DisplayLayoutProvider.Fallback();
        }

        // La clé est indépendante de l'ordre de énumération de Windows : bounds, DPI et
        // indicateur principal sont triés puis hachés. Les bounds restent en pixels physiques,
        // le DPI étant stocké séparément (la géométrie, elle, reste en DIPs).
        public static string ComputeKey(IReadOnlyList<MonitorDescriptor> monitors)
        {
            var parts = new List<string>();
            if (monitors != null)
            {
                foreach (var m in monitors
                    .OrderBy(m => m.X).ThenBy(m => m.Y).ThenBy(m => m.Width).ThenBy(m => m.Height)
                    .ThenBy(m => m.DpiScale).ThenBy(m => m.IsPrimary ? 0 : 1))
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4:0.####},{5}",
                        m.X, m.Y, m.Width, m.Height, m.DpiScale, m.IsPrimary ? 1 : 0));
                }
            }

            var canonical = parts.Count.ToString(CultureInfo.InvariantCulture) + ":" + string.Join("|", parts);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexString(hash).Substring(0, 16);
        }

        public static string BuildDescription(IReadOnlyList<MonitorDescriptor> monitors)
        {
            if (monitors == null || monitors.Count == 0) return "Aucun écran détecté";
            return string.Join(" + ", monitors
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.X).ThenBy(m => m.Y)
                .Select(m => string.Format(CultureInfo.InvariantCulture,
                    "{0}×{1} @ {2}%{3}", m.Width, m.Height,
                    (int)Math.Round(m.DpiScale * 100), m.IsPrimary ? " (principal)" : "")));
        }

        public static DisplayProfile? FindProfile(AppData data, string key)
        {
            if (data?.Profiles == null || string.IsNullOrEmpty(key)) return null;
            return data.Profiles.FirstOrDefault(p =>
                p != null && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public static NestGeometry GeometryOf(NestData nest) => new()
        {
            Left = nest.Left,
            Top = nest.Top,
            Width = nest.Width,
            Height = nest.Height
        };

        public static void ApplyGeometry(NestData nest, NestGeometry geometry)
        {
            if (nest == null || geometry == null) return;
            nest.Left = geometry.Left;
            nest.Top = geometry.Top;
            nest.Width = geometry.Width;
            nest.Height = geometry.Height;
        }

        public static Dictionary<string, NestGeometry> CaptureGeometry(IEnumerable<NestData> nests)
        {
            var result = new Dictionary<string, NestGeometry>(StringComparer.OrdinalIgnoreCase);
            if (nests == null) return result;
            foreach (var nest in nests)
                if (nest != null) result[nest.Id.ToString()] = GeometryOf(nest);
            return result;
        }

        // Normalise les collections qui peuvent être nulles dans un JSON édité à la main.
        public static void NormalizeProfiles(AppData data)
        {
            data.Profiles ??= new List<DisplayProfile>();
            data.ActiveProfileKey ??= "";

            data.Profiles.RemoveAll(p => p == null);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in data.Profiles.ToList())
            {
                profile.Key ??= "";
                profile.Description ??= "";
                if (!seen.Add(profile.Key))
                {
                    data.Profiles.Remove(profile);
                    continue;
                }

                var nests = new Dictionary<string, NestGeometry>(StringComparer.OrdinalIgnoreCase);
                if (profile.Nests != null)
                {
                    foreach (var entry in profile.Nests)
                    {
                        var id = NormalizeId(entry.Key);
                        if (id == null || entry.Value == null) continue;
                        nests[id] = entry.Value;
                    }
                }
                profile.Nests = nests;
            }
        }

        // Retire d TOUS les profils les zones supprimées (y compris les profils inactifs).
        public static void PruneProfiles(AppData data)
        {
            NormalizeProfiles(data);
            var liveIds = new HashSet<string>(
                data.Nests.Where(n => n != null).Select(n => n.Id.ToString()),
                StringComparer.OrdinalIgnoreCase);
            foreach (var profile in data.Profiles)
                foreach (var id in profile.Nests.Keys.ToList())
                    if (!liveIds.Contains(id)) profile.Nests.Remove(id);
        }

        // Capture les positions vivantes dans le profil actif. C'est le seul endroit qui
        // écrit dans les profils : il ne touche jamais aux autres configurations.
        public static void EnsureActiveProfile(AppData data, IReadOnlyList<MonitorDescriptor>? monitors = null)
        {
            PruneProfiles(data);
            var current = monitors ?? CurrentMonitors();
            var currentKey = ComputeKey(current);
            data.Version = 3;

            if (string.IsNullOrWhiteSpace(data.ActiveProfileKey)) data.ActiveProfileKey = currentKey;
            // Si les écrans ont changé (par exemple avant le démarrage de D3), les valeurs
            // vivantes ne décrivent plus ce profil : ne jamais l'écraser avec elles.
            if (!string.Equals(data.ActiveProfileKey, currentKey, StringComparison.OrdinalIgnoreCase))
            {
                Diag.Log("profils : configuration actuelle différente du profil actif, capture différée");
                return;
            }

            var profile = FindProfile(data, data.ActiveProfileKey);
            if (profile == null)
            {
                profile = new DisplayProfile
                {
                    Key = data.ActiveProfileKey,
                    Description = BuildDescription(current)
                };
                data.Profiles.Add(profile);
            }
            else if (string.IsNullOrWhiteSpace(profile.Description))
            {
                profile.Description = BuildDescription(current);
            }

            profile.Nests = CaptureGeometry(data.Nests);
        }

        public static DisplayProfile CreateProfile(IReadOnlyList<MonitorDescriptor> monitors, IEnumerable<NestData> nests)
        {
            return new DisplayProfile
            {
                Key = ComputeKey(monitors),
                Description = BuildDescription(monitors),
                Nests = CaptureGeometry(nests)
            };
        }

        private static string? NormalizeId(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var value = key.Trim();
            return Guid.TryParse(value, out var id) ? id.ToString() : value;
        }
    }

    internal sealed class Win32DisplayLayoutProvider : IDisplayLayoutProvider
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool EnumMonitorsProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        public IReadOnlyList<MonitorDescriptor> GetMonitors()
        {
            try
            {
                var result = new List<MonitorDescriptor>();
                EnumMonitorsProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                    if (GetMonitorInfo(hMonitor, ref info))
                    {
                        var scale = ReadScale(hMonitor);
                        result.Add(new MonitorDescriptor(
                            info.rcMonitor.Left, info.rcMonitor.Top,
                            info.rcMonitor.Right - info.rcMonitor.Left,
                            info.rcMonitor.Bottom - info.rcMonitor.Top,
                            scale,
                            (info.dwFlags & 1) != 0,
                            info.rcWork.Left, info.rcWork.Top,
                            info.rcWork.Right - info.rcWork.Left,
                            info.rcWork.Bottom - info.rcWork.Top));
                    }
                    return true;
                };

                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
                GC.KeepAlive(callback);
                if (result.Count > 0)
                    return result.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.X).ThenBy(m => m.Y).ToList();
            }
            catch (Exception ex)
            {
                Diag.Log("écrans : " + ex.Message);
            }
            return Fallback();
        }

        private static double ReadScale(IntPtr hMonitor)
        {
            try
            {
                // MDT_EFFECTIVE_DPI = 0
                if (GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
                    return dpiX / 96.0;
            }
            catch { }
            return 1.0;
        }

        internal static IReadOnlyList<MonitorDescriptor> Fallback()
        {
            int width = 0, height = 0;
            try
            {
                width = GetSystemMetrics(0); // SM_CXSCREEN
                height = GetSystemMetrics(1); // SM_CYSCREEN
            }
            catch { }
            if (width <= 0) width = 1920;
            if (height <= 0) height = 1080;
            return new[] { new MonitorDescriptor(0, 0, width, height, 1.0, true) };
        }
    }
}
