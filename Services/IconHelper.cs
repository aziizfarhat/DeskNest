using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskNest
{
    // Extraction et cache des icônes du shell.
    // - un seul cache LRU borné, clé par chemin + taille physique + date de modification ;
    // - extraction à la taille affichée (pas plus grande), images figées (thread-safe) ;
    // - extraction hors du thread UI, avec un seul thread STA dédié ;
    // - tous les handles natifs sont libérés dès la copie en bitmap managé.
    public static class IconHelper
    {
        private const int CacheLimit = 512;
        private const int MinSize = 16;
        private const int MaxSize = 256;
        private const int ShilLarge = 0x0;
        private const int ShilSmall = 0x1;
        private const int ShilExtraLarge = 0x2;
        private const int ShilJumbo = 0x4;

        private static readonly object Gate = new();
        private static readonly Dictionary<string, LinkedListNode<Entry>> Cache = new(StringComparer.Ordinal);
        private static readonly LinkedList<Entry> Lru = new();
        private static readonly Dictionary<string, Task<ImageSource?>> Pending = new(StringComparer.Ordinal);

        // Extensions qui savent produire une miniature dans l'Explorateur.
        private static readonly HashSet<string> ThumbExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif",
            ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".flv", ".ts", ".m2ts", ".3gp", ".ogv",
            ".pdf"
        };

        // Un seul thread STA dédié : le shell n'aime pas les extractions concurrentes
        // (les premières demandes peuvent échouer quand plusieurs threads initialisent COM en même temps).
        private static readonly System.Collections.Concurrent.BlockingCollection<Request> Queue = new();

        private sealed class Entry
        {
            public Entry(string key, string path, int size, long stamp, ImageSource icon)
            {
                Key = key; Path = path; Size = size; Stamp = stamp; Icon = icon;
            }
            public string Key { get; }
            public string Path { get; }
            public int Size { get; }
            public long Stamp { get; }
            public ImageSource Icon { get; set; }
        }

        private sealed class Request
        {
            public Request(string path, int size, long stamp, bool thumbnail, TaskCompletionSource<ImageSource?> done)
            {
                Path = path; Size = size; Stamp = stamp; Thumbnail = thumbnail; Done = done;
            }
            public string Path { get; }
            public int Size { get; }
            public long Stamp { get; }
            public bool Thumbnail { get; }
            public TaskCompletionSource<ImageSource?> Done { get; }
        }

        static IconHelper()
        {
            var worker = new System.Threading.Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "DeskNest.Icons"
            };
            worker.SetApartmentState(System.Threading.ApartmentState.STA);
            worker.Start();
        }

        private static void WorkerLoop()
        {
            foreach (var request in Queue.GetConsumingEnumerable())
            {
                var result = request.Thumbnail
                    ? ExtractThumbnail(request.Path, request.Size)
                    : Extract(request.Path, request.Size);
                if (result != null) AddToCache(request.Path, request.Size, request.Stamp, result);
                request.Done.TrySetResult(result);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        // IImageList (shell32) : seules les méthodes précédant GetIcon sont déclarées,
        // dans l'ordre de la vtable.
        [ComImport]
        [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, int nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
            [Out] IntPtr[] phicon, [Out] uint[] piconid, uint nIcons, uint flags);

        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00,
            BiggerSizeOk = 0x01,
            MemoryOnly = 0x02,
            IconOnly = 0x04,
            ThumbnailOnly = 0x08,
            InCacheOnly = 0x10,
            CropToSquare = 0x20,
            WideThumbnails = 0x40,
            IconBackground = 0x80,
            ScaleUp = 0x100
        }

        // IShellItemImageFactory : le même fournisseur que l'Explorateur, donc les
        // miniatures des images, vidéos et PDF (quand un fournisseur est installé).
        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const int ILD_TRANSPARENT = 0x1;
        private const uint FileAttributeDirectory = 0x10;
        private const uint FileAttributeNormal = 0x80;

        // Taille physique (pixels) à demander pour une taille DIP et un facteur DPI.
        public static int PixelSize(double dipSize, double dpiScale)
        {
            if (dpiScale <= 0) dpiScale = 1;
            return ClampSize((int)Math.Ceiling(dipSize * dpiScale));
        }

        private static int ClampSize(int size) => Math.Clamp(size, MinSize, MaxSize);

        private static long StampOf(string path)
        {
            try { return File.Exists(path) || Directory.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0; }
            catch { return 0; }
        }

        // La clé inclut la date : un fichier modifié produit une nouvelle entrée et l'ancienne est évincée.
        private static string KeyOf(string path, int size, long stamp) =>
            path.ToUpperInvariant() + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
            stamp.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Appelé sous Gate : retire toutes les tailles d'un chemin dont la date a changé
        // (fichier modifié ou supprimé).
        private static void Prune(string path, long stamp)
        {
            var node = Lru.First;
            while (node != null)
            {
                var next = node.Next;
                if (string.Equals(node.Value.Path, path, StringComparison.OrdinalIgnoreCase) && node.Value.Stamp != stamp)
                {
                    Lru.Remove(node);
                    Cache.Remove(node.Value.Key);
                }
                node = next;
            }
        }

        // Retourne immédiatement une icône déjà en cache, sans lancer d'extraction.
        public static ImageSource? GetCached(string path, int size)
        {
            if (string.IsNullOrEmpty(path)) return null;
            size = ClampSize(size);
            long stamp = StampOf(path);
            lock (Gate)
            {
                Prune(path, stamp);
                if (!Cache.TryGetValue(KeyOf(path, size, stamp), out var node)) return null;
                Lru.Remove(node);
                Lru.AddFirst(node);
                return node.Value.Icon;
            }
        }

        // Extraction asynchrone et dédupliquée (plusieurs demandes du même chemin partagent la tâche).
        public static Task<ImageSource?> GetIconAsync(string path, int size)
            => GetAsync(path, size, thumbnail: false);

        public static Task<ImageSource?> GetThumbnailAsync(string path, int size)
            => GetAsync(path, size, thumbnail: true);

        private static Task<ImageSource?> GetAsync(string path, int size, bool thumbnail)
        {
            if (string.IsNullOrEmpty(path)) return Task.FromResult<ImageSource?>(null);
            size = ClampSize(size);
            long stamp = StampOf(path);
            string key = KeyOf(path, size, stamp);

            lock (Gate)
            {
                Prune(path, stamp);
                if (Cache.TryGetValue(key, out var node))
                {
                    Lru.Remove(node);
                    Lru.AddFirst(node);
                    return Task.FromResult<ImageSource?>(node.Value.Icon);
                }
                if (Pending.TryGetValue(key, out var pending)) return pending;

                var done = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                Pending[key] = done.Task;
                done.Task.ContinueWith(_ =>
                {
                    lock (Gate) Pending.Remove(key);
                }, TaskContinuationOptions.ExecuteSynchronously);
                Queue.Add(new Request(path, size, stamp, thumbnail, done));
                return done.Task;
            }
        }

        private static void AddToCache(string path, int size, long stamp, ImageSource image)
        {
            string key = KeyOf(path, size, stamp);
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var existing))
                {
                    existing.Value.Icon = image;
                    Lru.Remove(existing);
                    Lru.AddFirst(existing);
                    return;
                }

                var node = Lru.AddFirst(new Entry(key, path, size, stamp, image));
                Cache[key] = node;
                while (Cache.Count > CacheLimit && Lru.Last != null)
                {
                    var last = Lru.Last;
                    Lru.RemoveLast();
                    Cache.Remove(last.Value.Key);
                }
            }
        }

        // Même cache pour les icônes et les miniatures (la source est choisie par l'appelant).
        public static ImageSource? GetCachedThumbnail(string path, int size) => GetCached(path, size);

        // Vrai si le fichier peut fournir une miniature (image, vidéo, PDF).
        public static bool CanThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (Directory.Exists(path)) return false;
                return ThumbExtensions.Contains(System.IO.Path.GetExtension(path));
            }
            catch { return false; }
        }

        private static ImageSource? ExtractThumbnail(string path, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            object? item = null;
            try
            {
                if (!File.Exists(path)) return null;

                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out item);
                if (item is not IShellItemImageFactory factory) return null;

                var wanted = new SIZE { cx = size, cy = size };
                int hr = factory.GetImage(wanted, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                return Fit(src, size);
            }
            catch { return null; }
            finally
            {
                // Le bitmap natif n'est plus nécessaire une fois copié dans le BitmapSource.
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (item != null)
                {
                    try { Marshal.ReleaseComObject(item); } catch { }
                }
            }
        }

        // Réduit (ou agrandit) uniquement si la copie managée ne correspond pas à la taille affichée.
        private static BitmapSource Fit(BitmapSource source, int size)
        {
            if (source.PixelWidth < 1 || source.PixelHeight < 1 ||
                (source.PixelWidth == size && source.PixelHeight == size))
            {
                source.Freeze();
                return source;
            }
            double scale = Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
            var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            resized.Freeze();
            return resized;
        }

        private static ImageSource? Extract(string path, int size)
        {
            // Les raccourcis Internet (.url) ne sont pas résolus par SHGetFileInfo : il renvoie
            // le globe générique. Explorer, lui, lit IconFile/IconIndex dans le fichier .url.
            var shortcutIcon = ExtractInternetShortcutIcon(path, size);
            if (shortcutIcon != null) return shortcutIcon;

            IntPtr hIcon = IntPtr.Zero;
            try
            {
                bool exists = File.Exists(path) || Directory.Exists(path);
                uint attrs = 0;
                if (!exists)
                    attrs = System.IO.Path.HasExtension(path) ? FileAttributeNormal : FileAttributeDirectory;

                var info = new SHFILEINFO();
                uint flags = SHGFI_SYSICONINDEX;
                if (!exists) flags |= SHGFI_USEFILEATTRIBUTES;
                SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

                // La liste système la plus proche de la taille affichée évite de décoder
                // systématiquement l'icône 48 px (ou 256 px) pour une icône de 20 px.
                int listIndex = size <= 16 ? ShilSmall : size <= 32 ? ShilLarge : size <= 48 ? ShilExtraLarge : ShilJumbo;
                var iid = typeof(IImageList).GUID;
                if (SHGetImageList(listIndex, ref iid, out var list) == 0 && list != null)
                {
                    try
                    {
                        if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var icon) == 0) hIcon = icon;
                    }
                    finally { Marshal.ReleaseComObject(list); }
                }

                if (hIcon == IntPtr.Zero)
                {
                    // Repli : icône 32 px du système.
                    var fallback = new SHFILEINFO();
                    uint fallbackFlags = SHGFI_ICON;
                    if (!exists) fallbackFlags |= SHGFI_USEFILEATTRIBUTES;
                    if (SHGetFileInfo(path, attrs, ref fallback, (uint)Marshal.SizeOf<SHFILEINFO>(), fallbackFlags) != IntPtr.Zero)
                        hIcon = fallback.hIcon;
                }

                if (hIcon == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return Fit(src, size);
            }
            catch { return null; }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            }
        }

        // Extrait l'icône déclarée par un raccourci Internet (.url). Sans cela, Windows
        // renvoie l'icône générique « globe » au lieu de l'icône choisie par l'auteur.
        private static ImageSource? ExtractInternetShortcutIcon(string path, int size)
        {
            try
            {
                if (!path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return null;
                if (!File.Exists(path)) return null;

                string? iconFile = null;
                int iconIndex = 0;
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconFile = line.Substring("IconFile=".Length).Trim().Trim('"');
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.Substring("IconIndex=".Length).Trim(), out iconIndex);
                }

                if (string.IsNullOrWhiteSpace(iconFile)) return null;
                iconFile = Environment.ExpandEnvironmentVariables(iconFile);
                if (!File.Exists(iconFile)) return null;

                var handles = new IntPtr[1];
                var ids = new uint[1];
                if (PrivateExtractIcons(iconFile, iconIndex, size, size, handles, ids, 1, 0) > 0 &&
                    handles[0] != IntPtr.Zero)
                {
                    try
                    {
                        var hi = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        return Fit(hi, size);
                    }
                    finally { DestroyIcon(handles[0]); }
                }

                IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
                try
                {
                    if (ExtractIconEx(iconFile, iconIndex, out large, out small, 1) <= 0) return null;
                    var handle = large != IntPtr.Zero ? large : small;
                    if (handle == IntPtr.Zero) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    return Fit(src, size);
                }
                finally
                {
                    if (large != IntPtr.Zero) DestroyIcon(large);
                    if (small != IntPtr.Zero) DestroyIcon(small);
                }
            }
            catch { return null; }
        }
    }
}
