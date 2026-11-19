using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskNest
{
    // Métadonnées lues une seule fois par élément, hors du thread UI, puis
    // comparées sans aucun accès disque. Les entrées manquantes ou illisibles
    // sont marquées Available=false et repoussent en fin de liste.
    public sealed class ItemMeta
    {
        public string Path { get; }
        public string Name { get; }
        public bool IsFolder { get; }
        public long Size { get; }
        public DateTime CreatedUtc { get; }
        public DateTime ModifiedUtc { get; }
        public string Extension { get; }
        public bool Available { get; }

        public ItemMeta(string path, string name, bool isFolder, long size,
                        DateTime createdUtc, DateTime modifiedUtc, bool available = true)
        {
            Path = path ?? "";
            Name = name ?? "";
            IsFolder = isFolder;
            Size = Math.Max(0, size);
            CreatedUtc = createdUtc;
            ModifiedUtc = modifiedUtc;
            Extension = IsFolder ? "" : GetExtension(Name);
            Available = available;
        }

        public static ItemMeta Read(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var d = new DirectoryInfo(path);
                    return new ItemMeta(path, d.Name, true, 0, d.CreationTimeUtc, d.LastWriteTimeUtc);
                }

                if (File.Exists(path))
                {
                    var f = new FileInfo(path);
                    f.Refresh();
                    return new ItemMeta(path, f.Name, false, f.Length,
                                        f.CreationTimeUtc, f.LastWriteTimeUtc);
                }
            }
            catch
            {
                // Accès refusé, chemin trop long, lecteur démonté : entrée indisponible.
            }

            return new ItemMeta(path, GetFileName(path), false, 0,
                                DateTime.MinValue, DateTime.MinValue, false);
        }

        // Horodatage utilisé pour invalider le cache (une seule lecture légère).
        public static long StampOf(string path)
        {
            try { return File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return 0; }
        }

        private static string GetFileName(string path)
        {
            try { return System.IO.Path.GetFileName(path.TrimEnd('\\', '/')); }
            catch { return path; }
        }

        private static string GetExtension(string name)
        {
            try { return System.IO.Path.GetExtension(name); }
            catch { return ""; }
        }
    }

    // Tri pur et testable. Ne touche jamais à l'ordre manuscrit stocké :
    // une copie triée est retournée, les données restent intactes.
    public static class NestItemSorter
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        // Tri naturel insensible à la casse ET aux accents. StrCmpLogicalW gère
        // les nombres et la casse, mais est sensible aux accents : on retire
        // donc les diacritiques avant la comparaison (NFD + marques non espacées).
        public static int NaturalCompare(string? a, string? b)
        {
            a ??= "";
            b ??= "";
            if (ReferenceEquals(a, b)) return 0;
            try
            {
                int r = StrCmpLogicalW(Fold(a), Fold(b));
                return r < 0 ? -1 : r > 0 ? 1 : 0;
            }
            catch
            {
                return Math.Sign(string.Compare(a, b, CultureInfo.CurrentCulture,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace));
            }
        }

        private static string Fold(string value)
        {
            if (value.Length == 0) return value;
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public static List<ItemMeta> Sort(IEnumerable<ItemMeta>? source, string sortBy, bool descending)
        {
            var items = source?.ToList() ?? new List<ItemMeta>();
            string mode = NestSort.Normalize(sortBy);
            if (!NestSort.IsActive(mode) || items.Count < 2) return items;

            // OrderBy est stable ; l'index garantit en plus l'ordre relatif des
            // entrées manquantes, qui se comparent toutes à zéro.
            return items.Select((item, index) => (item, index))
                        .OrderBy(x => x.item, new MetaComparer(mode, descending))
                        .ThenBy(x => x.index)
                        .Select(x => x.item)
                        .ToList();
        }

        private sealed class MetaComparer : IComparer<ItemMeta>
        {
            private readonly string _mode;
            private readonly bool _descending;

            public MetaComparer(string mode, bool descending)
            {
                _mode = mode;
                _descending = descending;
            }

            public int Compare(ItemMeta? x, ItemMeta? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return 1;
                if (y == null) return -1;

                // Indisponibles toujours en dernier, dans leur ordre relatif.
                int ax = x.Available ? 0 : 1, ay = y.Available ? 0 : 1;
                if (ax != ay) return ax - ay;
                if (ax == 1) return 0;

                // Dossiers toujours avant les fichiers, quel que soit le critère
                // et quel que soit le sens.
                int fx = x.IsFolder ? 0 : 1, fy = y.IsFolder ? 0 : 1;
                if (fx != fy) return fx - fy;

                int c;
                if (fx == 0)
                {
                    // Les dossiers n'ont ni taille de contenu ni type : ils gardent
                    // un tri par nom (ascending pour Taille/Type, sens choisi sinon).
                    c = _mode == NestSort.Type || _mode == NestSort.Size
                        ? NaturalCompare(x.Name, y.Name)
                        : _mode == NestSort.DateCreated
                            ? x.CreatedUtc.CompareTo(y.CreatedUtc)
                            : _mode == NestSort.DateModified
                                ? x.ModifiedUtc.CompareTo(y.ModifiedUtc)
                                : NaturalCompare(x.Name, y.Name);
                    if (_mode != NestSort.Type && _mode != NestSort.Size && c != 0 && _descending)
                        c = -c;
                    return c == 0 ? NaturalCompare(x.Name, y.Name) : Math.Sign(c);
                }

                switch (_mode)
                {
                    case NestSort.Type:
                        c = string.Compare(x.Extension, y.Extension, StringComparison.OrdinalIgnoreCase);
                        break;
                    case NestSort.Size:
                        c = x.Size.CompareTo(y.Size);
                        break;
                    case NestSort.DateCreated:
                        c = x.CreatedUtc.CompareTo(y.CreatedUtc);
                        break;
                    case NestSort.DateModified:
                        c = x.ModifiedUtc.CompareTo(y.ModifiedUtc);
                        break;
                    default:
                        c = NaturalCompare(x.Name, y.Name);
                        break;
                }

                if (c != 0) return _descending ? -Math.Sign(c) : Math.Sign(c);

                // Égalité : départage stable par nom naturel croissant.
                return NaturalCompare(x.Name, y.Name);
            }
        }
    }
}
