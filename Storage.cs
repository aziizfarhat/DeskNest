using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeskNest
{
    public static class Storage
    {
        private static readonly string NewDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskNest");
        private static readonly string LegacyDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NestsLite");
        private static string? _resolvedDir;

        // Migre les données existantes sans jamais écraser un fichier déjà présent.
        public static string Dir
        {
            get
            {
                if (_resolvedDir != null) return _resolvedDir;
                try
                {
                    if (!Directory.Exists(NewDir) && Directory.Exists(LegacyDir))
                        Directory.Move(LegacyDir, NewDir);
                    else if (Directory.Exists(NewDir) && Directory.Exists(LegacyDir))
                        CopyMissingFiles(LegacyDir, NewDir);
                    _resolvedDir = NewDir;
                }
                catch
                {
                    // En cas d'échec, continuer à utiliser l'ancien emplacement plutôt que perdre les données.
                    _resolvedDir = Directory.Exists(LegacyDir) ? LegacyDir : NewDir;
                }
                return _resolvedDir;
            }
        }

        private static void CopyMissingFiles(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, file));
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
        }

        private static string FilePath => System.IO.Path.Combine(Dir, "layout.json");
        private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

        // Résultat du dernier chargement, pour prévenir l'utilisateur une fois la langue appliquée.
        public enum LoadIssue { None, RestoredBackup, Lost }
        public static LoadIssue Issue { get; private set; }
        public static string? RestoredFrom { get; private set; }

        public static AppData Load()
        {
            Issue = LoadIssue.None;
            RestoredFrom = null;
            if (!File.Exists(FilePath)) return new AppData();

            try
            {
                var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath));
                if (d != null) return Normalize(d);
                Diag.Log("chargement : layout.json vide ou invalide");
            }
            catch (Exception ex)
            {
                Diag.Log("chargement : " + ex.Message);
            }

            // Le fichier est illisible : on en garde une copie, puis on tente la sauvegarde la plus récente.
            try
            {
                var copy = System.IO.Path.Combine(Dir, $"layout-corrompu-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(FilePath, copy, true);
            }
            catch { }

            var backup = NewestValidBackup();
            if (backup != null)
            {
                Issue = LoadIssue.RestoredBackup;
                RestoredFrom = System.IO.Path.GetFileName(backup.Value.Path);
                Diag.Log($"chargement : sauvegarde restaurée ({RestoredFrom})");
                return backup.Value.Data;
            }

            Issue = LoadIssue.Lost;
            return new AppData();
        }

        public static void Save(AppData data)
        {
            try
            {
                Normalize(data);
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, Opt));
                File.Move(tmp, FilePath, true); // écrire d'abord : une sauvegarde ne doit jamais annuler la sauvegarde
                BackupDaily();
            }
            catch (Exception ex)
            {
                Diag.Log("sauvegarde : " + ex.Message);
            }
        }

        // Les collections peuvent être nulles dans un JSON édité à la main : on ne veut pas planter au démarrage.
        private static AppData Normalize(AppData d)
        {
            d.Nests ??= new List<NestData>();
            d.HiddenByApp ??= new List<string>();
            d.Moved ??= new List<MovedItem>();
            d.Templates ??= new List<NestTemplate>();
            d.Settings ??= new AppSettings();
            foreach (var t in d.Templates)
            {
                t.Name ??= "";
                t.Color ??= "#1E1E2E";
                t.TitleFont ??= "";
                t.ItemFont ??= "";
            }

            foreach (var f in d.Nests)
            {
                f.Items ??= new List<string>();
                f.PortalOrder ??= new Dictionary<string, List<string>>();
                f.Title ??= "";
                f.Color ??= "#1E1E2E";
            }

            // Actualise les chemins enregistrés si l'ancien dossier d'éléments a été renommé.
            if (DesktopStore.MigrateLegacyStore(out bool storeMoved) && storeMoved)
                MigrateStoredPaths(d);

            return d;
        }

        private static void MigrateStoredPaths(AppData data)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var oldRoot = System.IO.Path.Combine(profile, "NestsLite", "Items");
            var newRoot = System.IO.Path.Combine(profile, "DeskNest", "Items");

            foreach (var nest in data.Nests)
            {
                nest.Items = nest.Items.Select(path => ReplacePathPrefix(path, oldRoot, newRoot)).ToList();
                if (!string.IsNullOrWhiteSpace(nest.PortalPath))
                    nest.PortalPath = ReplacePathPrefix(nest.PortalPath, oldRoot, newRoot);

                if (nest.PortalOrder != null)
                {
                    var migratedOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in nest.PortalOrder)
                        migratedOrder[ReplacePathPrefix(entry.Key, oldRoot, newRoot)] = entry.Value;
                    nest.PortalOrder = migratedOrder;
                }
            }

            foreach (var item in data.Moved)
            {
                item.StorePath = ReplacePathPrefix(item.StorePath, oldRoot, newRoot);
                item.OriginalPath = ReplacePathPrefix(item.OriginalPath, oldRoot, newRoot);
            }
        }

        private static string ReplacePathPrefix(string path, string oldRoot, string newRoot)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var oldPrefix = oldRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            if (string.Equals(path, oldPrefix, StringComparison.OrdinalIgnoreCase)) return newRoot;
            if (path.StartsWith(oldPrefix + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(oldPrefix + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return newRoot + path.Substring(oldPrefix.Length);
            return path;
        }

        private static (string Path, AppData Data)? NewestValidBackup()
        {
            try
            {
                var bdir = System.IO.Path.Combine(Dir, "backups");
                if (!Directory.Exists(bdir)) return null;
                foreach (var f in Directory.GetFiles(bdir, "layout-*.json").OrderByDescending(f => f))
                {
                    try
                    {
                        var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(f));
                        if (d != null) return (f, Normalize(d));
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        // ---------- Instantanés (dispositions nommées par date) ----------
        public static string SnapshotDir => System.IO.Path.Combine(Dir, "snapshots");

        public static string? CreateSnapshot()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                Directory.CreateDirectory(SnapshotDir);
                // Millisecondes : deux instantanés créés dans la même seconde ne s'écrasent plus.
                var p = System.IO.Path.Combine(SnapshotDir, $"layout-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                File.Copy(FilePath, p, true);
                PruneSnapshots();
                return p;
            }
            catch (Exception ex)
            {
                Diag.Log("instantané : " + ex.Message);
                return null;
            }
        }

        public static AppData? LoadFrom(string path)
        {
            try
            {
                var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path));
                return d == null ? null : Normalize(d);
            }
            catch (Exception ex)
            {
                Diag.Log("lecture instantané : " + ex.Message);
                return null;
            }
        }

        // Une sauvegarde par jour, on garde les 14 dernières.
        private static void BackupDaily()
        {
            var bdir = System.IO.Path.Combine(Dir, "backups");
            Directory.CreateDirectory(bdir);
            var b = System.IO.Path.Combine(bdir, $"layout-{DateTime.Now:yyyyMMdd}.json");
            if (File.Exists(FilePath) && !File.Exists(b)) File.Copy(FilePath, b);
            foreach (var old in Directory.GetFiles(bdir, "layout-*.json").OrderByDescending(f => f).Skip(14))
            {
                try { File.Delete(old); } catch { }
            }
        }

        // On garde les 30 instantanés les plus récents.
        private static void PruneSnapshots()
        {
            foreach (var old in Directory.GetFiles(SnapshotDir, "layout-*.json").OrderByDescending(f => f).Skip(30))
            {
                try { File.Delete(old); } catch { }
            }
        }
    }
}
