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
    // - cache LRU borné (les icônes sont figées, donc utilisables depuis n'importe quel thread) ;
    // - icônes 48 px via SHGetImageList (au lieu du 32 px agrandi de SHGetFileInfo) ;
    // - miniatures (images, vidéos, PDF) via IShellItemImageFactory, avec repli sur l'icône ;
    // - extraction hors du thread UI, avec un nombre limité d'extractions simultanées.
    public static class IconHelper
    {
        private const int CacheLimit = 512;
        private const int ThumbCacheLimit = 256;
        private const int ThumbSize = 128;
        private const int ShilExtraLarge = 0x2;

        private static readonly object Gate = new();
        private static readonly Dictionary<string, LinkedListNode<Entry>> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<Entry> Lru = new();
        private static readonly Dictionary<string, Task<ImageSource?>> Pending = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, LinkedListNode<Entry>> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<Entry> ThumbLru = new();
        private static readonly Dictionary<string, Task<ImageSource?>> ThumbPending = new(StringComparer.OrdinalIgnoreCase);

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
            public Entry(string key, ImageSource icon) { Key = key; Icon = icon; }
            public string Key { get; }
            public ImageSource Icon { get; set; }
        }

        private sealed class Request
        {
            public Request(string path, bool thumbnail, TaskCompletionSource<ImageSource?> done)
            {
                Path = path;
                Thumbnail = thumbnail;
                Done = done;
            }
            public string Path { get; }
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
                var result = request.Thumbnail ? ExtractThumbnail(request.Path) : Extract(request.Path);
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

        // Retourne immédiatement une icône déjà en cache, sans lancer d'extraction.
        public static ImageSource? GetCached(string path)
        {
            lock (Gate)
            {
                if (!Cache.TryGetValue(path, out var node)) return null;
                Lru.Remove(node);
                Lru.AddFirst(node);
                return node.Value.Icon;
            }
        }

        // Extraction asynchrone et dédupliquée (plusieurs demandes du même chemin partagent la tâche).
        public static Task<ImageSource?> GetIconAsync(string path)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(path, out var node))
                {
                    Lru.Remove(node);
                    Lru.AddFirst(node);
                    return Task.FromResult<ImageSource?>(node.Value.Icon);
                }
                if (Pending.TryGetValue(path, out var pending)) return pending;

                var done = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                Pending[path] = done.Task;
                done.Task.ContinueWith(_ =>
                {
                    lock (Gate) Pending.Remove(path);
                }, TaskContinuationOptions.ExecuteSynchronously);
                Queue.Add(new Request(path, false, done));
                return done.Task;
            }
        }

        // Extraction synchrone (conservée pour les appels ponctuels hors interface).
        public static ImageSource? GetIcon(string path)
        {
            var cached = GetCached(path);
            return cached ?? Extract(path);
        }

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

        private static string ThumbKey(string path) => path + "|t" + ThumbSize;

        // Retourne immédiatement une miniature déjà en cache.
        public static ImageSource? GetCachedThumbnail(string path)
        {
            var key = ThumbKey(path);
            lock (Gate)
            {
                if (!ThumbCache.TryGetValue(key, out var node)) return null;
                ThumbLru.Remove(node);
                ThumbLru.AddFirst(node);
                return node.Value.Icon;
            }
        }

        // Miniature asynchrone et dédupliquée (repli sur l'icône géré par l'appelant).
        public static Task<ImageSource?> GetThumbnailAsync(string path)
        {
            var key = ThumbKey(path);
            lock (Gate)
            {
                if (ThumbCache.TryGetValue(key, out var node))
                {
                    ThumbLru.Remove(node);
                    ThumbLru.AddFirst(node);
                    return Task.FromResult<ImageSource?>(node.Value.Icon);
                }
                if (ThumbPending.TryGetValue(key, out var pending)) return pending;

                var done = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                ThumbPending[key] = done.Task;
                done.Task.ContinueWith(_ =>
                {
                    lock (Gate) ThumbPending.Remove(key);
                }, TaskContinuationOptions.ExecuteSynchronously);
                Queue.Add(new Request(path, true, done));
                return done.Task;
            }
        }

        private static void AddToThumbCache(string key, ImageSource image)
        {
            lock (Gate)
            {
                if (ThumbCache.TryGetValue(key, out var existing))
                {
                    existing.Value.Icon = image;
                    ThumbLru.Remove(existing);
                    ThumbLru.AddFirst(existing);
                    return;
                }

                var node = ThumbLru.AddFirst(new Entry(key, image));
                ThumbCache[key] = node;
                while (ThumbCache.Count > ThumbCacheLimit && ThumbLru.Last != null)
                {
                    var last = ThumbLru.Last;
                    ThumbLru.RemoveLast();
                    ThumbCache.Remove(last.Value.Key);
                }
            }
        }

        private static ImageSource? ExtractThumbnail(string path)
        {
            IntPtr hbm = IntPtr.Zero;
            object? item = null;
            try
            {
                if (!File.Exists(path)) return null;

                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out item);
                if (item is not IShellItemImageFactory factory) return null;

                var size = new SIZE { cx = ThumbSize, cy = ThumbSize };
                int hr = factory.GetImage(size, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                AddToThumbCache(ThumbKey(path), src);
                return src;
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (item != null)
                {
                    try { Marshal.ReleaseComObject(item); } catch { }
                }
            }
        }

        private static void AddToCache(string path, ImageSource icon)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(path, out var existing))
                {
                    existing.Value.Icon = icon;
                    Lru.Remove(existing);
                    Lru.AddFirst(existing);
                    return;
                }

                var node = Lru.AddFirst(new Entry(path, icon));
                Cache[path] = node;
                while (Cache.Count > CacheLimit && Lru.Last != null)
                {
                    var last = Lru.Last;
                    Lru.RemoveLast();
                    Cache.Remove(last.Value.Key);
                }
            }
        }

        private static ImageSource? Extract(string path)
        {
            // Les raccourcis Internet (.url) ne sont pas résolus par SHGetFileInfo : il renvoie
            // le globe générique. Explorer, lui, lit IconFile/IconIndex dans le fichier .url.
            var shortcutIcon = ExtractInternetShortcutIcon(path);
            if (shortcutIcon != null)
            {
                AddToCache(path, shortcutIcon);
                return shortcutIcon;
            }

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

                var iid = typeof(IImageList).GUID;
                if (SHGetImageList(ShilExtraLarge, ref iid, out var list) == 0 && list != null)
                {
                    try
                    {
                        if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var icon) == 0) hIcon = icon;
                    }
                    finally { Marshal.ReleaseComObject(list); }
                }

                if (hIcon == IntPtr.Zero)
                {
                    // Repli : icône 32 px du système (éventuellement agrandie par WPF).
                    var fallback = new SHFILEINFO();
                    uint fallbackFlags = SHGFI_ICON;
                    if (!exists) fallbackFlags |= SHGFI_USEFILEATTRIBUTES;
                    if (SHGetFileInfo(path, attrs, ref fallback, (uint)Marshal.SizeOf<SHFILEINFO>(), fallbackFlags) != IntPtr.Zero)
                        hIcon = fallback.hIcon;
                }

                if (hIcon == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                AddToCache(path, src);
                return src;
            }
            catch { return null; }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            }
        }

        // Extrait l'icône déclarée par un raccourci Internet (.url). Sans cela, Windows
        // renvoie l'icône générique « globe » au lieu de l'icône choisie par l'auteur.
        private static ImageSource? ExtractInternetShortcutIcon(string path)
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

                // 48 px comme les autres icônes du shell (ExtractIconEx ne fournit que 32 px,
                // ce qui rendait les icônes de raccourci .url visiblement plus floues).
                var handles = new IntPtr[1];
                var ids = new uint[1];
                if (PrivateExtractIcons(iconFile, iconIndex, 48, 48, handles, ids, 1, 0) > 0 &&
                    handles[0] != IntPtr.Zero)
                {
                    try
                    {
                        var hi = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        hi.Freeze();
                        return hi;
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
                    src.Freeze();
                    return src;
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
