using System;
using System.Collections.Generic;

namespace DeskNest
{
    // Cache LRU borné, propre à une zone. La clé est le chemin complet ; une
    // écriture plus récente invalide l'entrée. Vidé à la fermeture de la zone.
    public sealed class NestMetadataCache
    {
        private const int Capacity = 5000;

        private sealed class Entry
        {
            public ItemMeta Meta = null!;
            public long Stamp;
            public LinkedListNode<string> Node = null!;
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _lru = new();

        public ItemMeta Get(string path)
        {
            long stamp = ItemMeta.StampOf(path);

            lock (_gate)
            {
                if (_entries.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                {
                    _lru.Remove(cached.Node);
                    _lru.AddLast(cached.Node);
                    return cached.Meta;
                }
            }

            var meta = ItemMeta.Read(path);
            var entry = new Entry { Meta = meta, Stamp = stamp };

            lock (_gate)
            {
                if (_entries.TryGetValue(path, out var old))
                {
                    _lru.Remove(old.Node);
                    _entries.Remove(path);
                }

                entry.Node = _lru.AddLast(path);
                _entries[path] = entry;

                while (_entries.Count > Capacity)
                {
                    var oldest = _lru.First;
                    if (oldest == null) break;
                    _lru.RemoveFirst();
                    _entries.Remove(oldest.Value);
                }
            }

            return meta;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _lru.Clear();
            }
        }
    }
}
