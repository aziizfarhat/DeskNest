using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DeskNest
{
    // Fond « acrylique » : on découpe le papier peint sous la zone puis on le floute.
    // Aucune dépendance à DWM : le rendu est identique sur Windows 10 et 11, et il suit
    // automatiquement le papier peint courant (y compris les diaporamas).
    public static class Wallpaper
    {
        private const int ImageCacheLimit = 8;

        private sealed class Info
        {
            public string Path = "";
            public long Stamp;
            public int Style;
            public BitmapSource? Image;
        }

        private static readonly object Gate = new();
        private static Info? _info;
        private static readonly Dictionary<string, (long Stamp, BitmapSource Image)> Images =
            new(StringComparer.OrdinalIgnoreCase);

        private static string TranscodedPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

        // Chemin du papier peint affiché par Windows (registre), avec repli sur la copie
        // « transcodée » que Windows maintient pour les diaporamas.
        public static string? CurrentPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var raw = key?.GetValue("WallPaper") as string;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var p = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            try { if (File.Exists(TranscodedPath)) return TranscodedPath; } catch { }
            return null;
        }

        // Découpe du papier peint correspondant à une zone, en pixels de l'image décodée.
        // Retourne false si la zone tombe hors du papier peint (mosaïque, image centrée…).
        public static bool TryGetCrop(Rect dipRect, double dpiScale, out BitmapSource? image, out Rect crop)
        {
            image = null;
            crop = Rect.Empty;
            if (dpiScale <= 0) dpiScale = 1;

            var info = Load(dpiScale);
            var img = info?.Image;
            if (info == null || img == null) return false;

            double wpW = img.PixelWidth, wpH = img.PixelHeight;
            if (wpW < 2 || wpH < 2) return false;

            double vsL = SystemParameters.VirtualScreenLeft * dpiScale;
            double vsT = SystemParameters.VirtualScreenTop * dpiScale;
            double vsW = Math.Max(1, SystemParameters.VirtualScreenWidth * dpiScale);
            double vsH = Math.Max(1, SystemParameters.VirtualScreenHeight * dpiScale);

            double dx, dy, dw, dh;
            switch (info.Style)
            {
                case 2: // étirer
                    dx = vsL; dy = vsT; dw = vsW; dh = vsH;
                    break;
                case 6:   // ajuster
                case 10:  // remplir
                case 22:  // couvrir tous les écrans
                {
                    double s = info.Style == 6
                        ? Math.Min(vsW / wpW, vsH / wpH)
                        : Math.Max(vsW / wpW, vsH / wpH);
                    dw = wpW * s; dh = wpH * s;
                    dx = vsL + (vsW - dw) / 2;
                    dy = vsT + (vsH - dh) / 2;
                    break;
                }
                default: // centrer / mosaïque : taille réelle
                    dw = wpW; dh = wpH;
                    dx = vsL + (vsW - dw) / 2;
                    dy = vsT + (vsH - dh) / 2;
                    break;
            }

            double zx = dipRect.Left * dpiScale, zy = dipRect.Top * dpiScale;
            double zw = Math.Max(1, dipRect.Width * dpiScale), zh = Math.Max(1, dipRect.Height * dpiScale);

            double x = (zx - dx) / dw * wpW;
            double y = (zy - dy) / dh * wpH;
            double w = zw / dw * wpW;
            double h = zh / dh * wpH;

            // La zone doit être entièrement couverte par l'image, sinon on garde la couleur unie.
            if (x < -0.5 || y < -0.5 || x + w > wpW + 0.5 || y + h > wpH + 0.5) return false;

            crop = new Rect(Math.Max(0, x), Math.Max(0, y), Math.Min(w, wpW), Math.Min(h, wpH));
            if (crop.Width < 1 || crop.Height < 1) return false;
            image = img;
            return true;
        }

        // Charge (et met en cache) une image choisie par l'utilisateur, à la taille
        // physique de la zone. Appelé uniquement quand l'apparence change ou qu'un
        // redimensionnement se termine, jamais pendant le déplacement de la souris.
        public static BitmapSource? LoadImage(string? path, int widthPx, int heightPx)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            long stamp;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                stamp = fi.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }

            int w = Math.Clamp(widthPx, 1, 4096), h = Math.Clamp(heightPx, 1, 4096);
            string key = path + "|" + w + "x" + h;

            lock (Gate)
            {
                if (Images.TryGetValue(key, out var entry) && entry.Stamp == stamp) return entry.Image;

                // Le bitmap de l'ancienne taille n'est plus utile : on le retire du cache.
                RemoveImages(path);
                var img = Decode(path, w, h);
                if (img == null) return null;
                if (Images.Count >= ImageCacheLimit)
                {
                    foreach (var old in Images.Keys) { Images.Remove(old); break; }
                }
                Images[key] = (stamp, img);
                return img;
            }
        }

        // Libère les bitmaps mis en cache pour un chemin (changement ou suppression de fond).
        public static void Release(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (Gate) RemoveImages(path);
        }

        private static void RemoveImages(string path)
        {
            var keys = new List<string>();
            foreach (var key in Images.Keys)
                if (key.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)) keys.Add(key);
            foreach (var key in keys) Images.Remove(key);
        }

        // Dimensions source lues sans décoder l'image complète (métadonnées uniquement).
        private static bool TrySourceSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoder = BitmapDecoder.Create(fs,
                    BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                return width > 0 && height > 0;
            }
            catch { return false; }
        }

        private static Info? Load(double dpiScale)
        {
            var candidates = new List<string>();
            int style = 10;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var raw = key?.GetValue("WallPaper") as string;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var p = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                    if (File.Exists(p)) candidates.Add(p);
                }
                if (key?.GetValue("WallpaperStyle") is string ss && int.TryParse(ss, out var v)) style = v;
                if (style == 0 && string.Equals(key?.GetValue("TileWallpaper") as string, "1", StringComparison.Ordinal))
                    style = 1;
            }
            catch { }

            try
            {
                var t = TranscodedPath;
                if (File.Exists(t) && !candidates.Exists(c => string.Equals(c, t, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(t);
            }
            catch { }

            lock (Gate)
            {
                foreach (var path in candidates)
                {
                    long stamp;
                    try { stamp = File.GetLastWriteTimeUtc(path).Ticks; } catch { continue; }

                    if (_info != null && _info.Path == path && _info.Stamp == stamp && _info.Style == style)
                        return _info;

                    int targetW = (int)Math.Ceiling(SystemParameters.VirtualScreenWidth * dpiScale);
                    int targetH = (int)Math.Ceiling(SystemParameters.VirtualScreenHeight * dpiScale);
                    var img = Decode(path, targetW, targetH);
                    if (img == null) continue;

                    _info = new Info { Path = path, Stamp = stamp, Style = style, Image = img };
                    return _info;
                }

                _info = null;
                return null;
            }
        }

        // Décodage à la taille affichée : DecodePixelWidth/Height évite d'allouer
        // l'image en pleine résolution avant de la réduire. OnLoad garantit que le
        // flux est fermé et l'image figée avant le retour.
        private static BitmapSource? Decode(string path, int targetW, int targetH)
        {
            try
            {
                if (!TrySourceSize(path, out int sourceW, out int sourceH))
                {
                    // Format dont les métadonnées ne sont pas lisibles : on se contente
                    // d'une limite de largeur plutôt que de renoncer à afficher le fond.
                    using var raw = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var fallback = new BitmapImage();
                    fallback.BeginInit();
                    fallback.CacheOption = BitmapCacheOption.OnLoad;
                    fallback.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    fallback.DecodePixelWidth = Math.Clamp(targetW, 1, 4096);
                    fallback.StreamSource = raw;
                    fallback.EndInit();
                    fallback.Freeze();
                    return fallback;
                }

                // Couverture sans agrandissement : la copie reste au moins aussi grande
                // que la cible dans les deux axes, sans déformer l'image source.
                double scale = Math.Min(1.0, Math.Max((double)targetW / sourceW, (double)targetH / sourceH));
                int w = Math.Max(1, (int)Math.Round(sourceW * scale));
                int h = Math.Max(1, (int)Math.Round(sourceH * scale));

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (sourceW >= sourceH) image.DecodePixelWidth = w;
                else image.DecodePixelHeight = h;
                image.StreamSource = fs;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }
    }
}
