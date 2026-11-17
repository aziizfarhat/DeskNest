using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskNest.Tests
{
    // Système de fichiers en mémoire : les tests de Storage ne touchent jamais au disque.
    internal sealed class MemoryFileSystem : IFileSystem
    {
        private sealed class Entry
        {
            public byte[] Content = Array.Empty<byte>();
            public DateTime LastWriteUtc = DateTime.UtcNow;
        }

        private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

        private static string Normalize(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return full;
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string Parent(string path)
        {
            var parent = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(parent) ? "" : parent;
        }

        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public string ReadAllText(string path)
        {
            var key = Normalize(path);
            if (!_files.TryGetValue(key, out var entry)) throw new FileNotFoundException("Fichier introuvable.", path);
            return Encoding.UTF8.GetString(entry.Content);
        }

        public void WriteAllText(string path, string contents)
        {
            _files[Normalize(path)] = new Entry
            {
                Content = Encoding.UTF8.GetBytes(contents),
                LastWriteUtc = DateTime.UtcNow
            };
        }

        public void Copy(string sourcePath, string destinationPath, bool overwrite)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            if (!_files.TryGetValue(source, out var entry)) throw new FileNotFoundException("Fichier source introuvable.", sourcePath);
            if (!overwrite && _files.ContainsKey(destination)) throw new IOException("Le fichier de destination existe déjà.");
            _files[destination] = new Entry
            {
                Content = (byte[])entry.Content.Clone(),
                LastWriteUtc = DateTime.UtcNow
            };
        }

        public void Move(string sourcePath, string destinationPath)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            if (!_files.TryGetValue(source, out var entry)) throw new FileNotFoundException("Fichier source introuvable.", sourcePath);
            if (_files.ContainsKey(destination)) throw new IOException("Le fichier de destination existe déjà.");
            _files[destination] = entry;
            _files.Remove(source);
        }

        public void Delete(string path) => _files.Remove(Normalize(path));

        public bool DirectoryExists(string path) => _dirs.Contains(Normalize(path));

        public void CreateDirectory(string path)
        {
            var key = Normalize(path);
            if (_dirs.Contains(key)) return;
            var parent = Parent(key);
            if (!string.IsNullOrEmpty(parent) && !string.Equals(parent, key, StringComparison.OrdinalIgnoreCase))
                CreateDirectory(parent);
            _dirs.Add(key);
        }

        public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
        {
            var directory = Normalize(path);
            return _files.Keys
                .Where(file => Belongs(file, directory, searchOption))
                .Where(file => Matches(Path.GetFileName(file), searchPattern))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            var directory = Normalize(path);
            return _dirs
                .Where(dir => !string.Equals(dir, directory, StringComparison.OrdinalIgnoreCase))
                .Where(dir => Belongs(dir, directory, searchOption))
                .Where(dir => Matches(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)), searchPattern))
                .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            var key = Normalize(path);
            if (!_files.TryGetValue(key, out var entry)) throw new FileNotFoundException("Fichier introuvable.", path);
            return entry.LastWriteUtc;
        }

        public void Replace(string sourcePath, string destinationPath, string destinationBackupPath, bool ignoreMetadataErrors)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            var backup = Normalize(destinationBackupPath);
            if (!_files.TryGetValue(source, out var incoming)) throw new FileNotFoundException("Fichier source introuvable.", sourcePath);
            if (!_files.TryGetValue(destination, out var previous)) throw new FileNotFoundException("Fichier de destination introuvable.", destinationPath);
            _files[backup] = previous;
            _files[destination] = incoming;
            _files.Remove(source);
        }

        private static bool Belongs(string path, string directory, SearchOption option)
        {
            if (string.Equals(Parent(path), directory, StringComparison.OrdinalIgnoreCase)) return true;
            if (option == SearchOption.TopDirectoryOnly) return false;
            var prefix = directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? directory
                : directory + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Matches(string name, string pattern)
        {
            if (pattern == "*") return true;
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
        }
    }
}
