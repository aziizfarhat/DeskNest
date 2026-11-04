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
    // - extraction hors du thread UI, avec un nombre limité d'extractions simultanées.
    public static class IconHelper
    {
        private const int CacheLimit = 512;
        private const int ShilExtraLarge = 0x2;

        private static readonly object Gate = new();
        private static readonly Dictionary<string, LinkedListNode<Entry>> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<Entry> Lru = new();
        private static readonly Dictionary<string, Task<ImageSource?>> Pending = new(StringComparer.OrdinalIgnoreCase);

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
            public Request(string path, TaskCompletionSource<ImageSource?> done)
            {
                Path = path;
                Done = done;
            }
            public string Path { get; }
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
                request.Done.TrySetResult(Extract(request.Path));
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

        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

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
                Queue.Add(new Request(path, done));
                return done.Task;
            }
        }

        // Extraction synchrone (conservée pour les appels ponctuels hors interface).
        public static ImageSource? GetIcon(string path)
        {
            var cached = GetCached(path);
            return cached ?? Extract(path);
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
    }
}
