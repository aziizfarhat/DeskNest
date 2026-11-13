using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

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
        private static string LastGoodPath => System.IO.Path.Combine(Dir, "layout-last-good.json");
        private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

        // Résultat du dernier chargement, pour prévenir l'utilisateur une fois la langue appliquée.
        public enum LoadIssue { None, RestoredBackup, Lost }
        public static LoadIssue Issue { get; private set; }
        public static string? RestoredFrom { get; private set; }

        public static AppData Load()
        {
            Issue = LoadIssue.None;
            RestoredFrom = null;

            AppData? d = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath));
                    if (d == null) Diag.Log("chargement : layout.json vide ou invalide");
                }
                catch (Exception ex)
                {
                    Diag.Log("chargement : " + ex.Message);
                    // Le fichier est illisible : on en garde une copie avant toute réécriture.
                    try
                    {
                        var copy = System.IO.Path.Combine(Dir, $"layout-corrompu-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                        File.Copy(FilePath, copy, true);
                    }
                    catch { }
                }
            }

            if (d != null)
            {
                d = Normalize(d);
                // Un fichier sans zone n'est légitime que si l'utilisateur a supprimé
                // la dernière zone. Sinon (mise à jour, écriture interrompue), on
                // restaure la sauvegarde la plus récente au lieu de perdre les zones.
                if (d.Nests.Count > 0 || d.ZonesCleared) return d;
                Diag.Log("chargement : layout.json sans zone, restauration d'une sauvegarde");
            }

            var backup = NewestValidBackup();
            if (backup != null)
            {
                Issue = LoadIssue.RestoredBackup;
                RestoredFrom = System.IO.Path.GetFileName(backup.Value.Path);
                Diag.Log($"chargement : sauvegarde restaurée ({RestoredFrom})");
                return backup.Value.Data;
            }

            // Aucune sauvegarde exploitable : on garde les réglages du fichier s'il était lisible.
            if (d != null) return d;

            Issue = LoadIssue.Lost;
            return new AppData();
        }

        // ---------- Sauvegarde non bloquante ----------
        // La sérialisation se fait sur le thread appelant (les données ne sont pas thread-safe),
        // puis l'écriture disque et les sauvegardes quotidiennes sont faites en arrière-plan.
        // Une rafale de modifications (curseur d'opacité, glissement…) ne produit qu'une écriture.
        private static readonly object WriteGate = new();
        private static string? _pendingJson;
        private static bool _pendingHasZones;
        private static bool _writerActive;

        public static void Save(AppData data)
        {
            string json;
            bool hasZones;
            var watch = Stopwatch.StartNew();
            try
            {
                Normalize(data);
                hasZones = data.Nests.Count > 0;
                json = JsonSerializer.Serialize(data, Opt);
            }
            catch (Exception ex)
            {
                Diag.Log("sauvegarde : " + ex.Message);
                return;
            }
            if (watch.ElapsedMilliseconds > 200)
                Diag.Log($"sauvegarde : sérialisation lente ({watch.ElapsedMilliseconds} ms)");

            lock (WriteGate)
            {
                _pendingJson = json;
                _pendingHasZones = hasZones;
                if (_writerActive) return;
                _writerActive = true;
            }

            var thread = new Thread(WriterLoop) { IsBackground = true, Name = "DeskNest.Save" };
            thread.Start();
        }

        private static void WriterLoop()
        {
            while (true)
            {
                string? json;
                bool hasZones;
                lock (WriteGate)
                {
                    json = _pendingJson;
                    hasZones = _pendingHasZones;
                    _pendingJson = null;
                    if (json == null) { _writerActive = false; return; }
                }
                WriteJson(json, hasZones);
            }
        }

        private static void WriteJson(string json, bool hasZones)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true); // écrire d'abord : une sauvegarde ne doit jamais annuler la sauvegarde

                // Copie « dernier état valide » : utilisée au démarrage si layout.json est vide.
                if (hasZones)
                {
                    try { File.Copy(FilePath, LastGoodPath, true); } catch { }
                }

                BackupDaily(hasZones);
            }
            catch (Exception ex)
            {
                Diag.Log("sauvegarde : " + ex.Message);
            }
            if (watch.ElapsedMilliseconds > 200)
                Diag.Log($"sauvegarde : écriture lente ({watch.ElapsedMilliseconds} ms)");
        }

        // Attend la fin des écritures en attente (fermeture de l'application, instantané).
        public static void Flush(int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                lock (WriteGate)
                    if (!_writerActive && _pendingJson == null) return;
                Thread.Sleep(10);
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

            // Un import ou une ancienne sauvegarde peut contenir deux fois la même zone
            // (même Id) : sans ce filtre, deux fenêtres se superposeraient et les
            // modifications seraient enregistrées sur la mauvaise.
            if (d.Nests.Count > 0)
            {
                var seen = new HashSet<Guid>();
                d.Nests.RemoveAll(n => n == null || !seen.Add(n.Id));
            }
            foreach (var t in d.Templates)
            {
                t.Name ??= "";
                t.Color ??= "#1E1E2E";
                t.GradientStart ??= "#1E1E2E";
                t.GradientEnd ??= "#4A3426";
                t.BackgroundImage ??= "";
                t.TitleFont ??= "";
                t.ItemFont ??= "";
                t.BackgroundMode = Math.Clamp(t.BackgroundMode, 0, 3);
                t.GradientAngle %= 360;
                if (t.GradientAngle < 0) t.GradientAngle += 360;
            }

            foreach (var f in d.Nests)
            {
                f.Items ??= new List<string>();
                f.PortalOrder ??= new Dictionary<string, List<string>>();
                f.Title ??= "";
                f.Color ??= "#1E1E2E";
                f.GradientStart ??= "#1E1E2E";
                f.GradientEnd ??= "#4A3426";
                f.BackgroundImage ??= "";
                f.BackgroundMode = Math.Clamp(f.BackgroundMode, 0, 3);
                f.GradientAngle %= 360;
                if (f.GradientAngle < 0) f.GradientAngle += 360;
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
                var candidates = new List<string>();
                var bdir = System.IO.Path.Combine(Dir, "backups");
                if (Directory.Exists(bdir)) candidates.AddRange(Directory.GetFiles(bdir, "layout-*.json"));
                if (File.Exists(LastGoodPath)) candidates.Add(LastGoodPath);

                foreach (var f in candidates.OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    try
                    {
                        var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(f));
                        if (d == null) continue;
                        d = Normalize(d);
                        // Une sauvegarde vide n'est jamais une restauration utile.
                        if (d.Nests.Count == 0) continue;
                        return (f, d);
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
                Flush(); // l'instantané doit contenir la dernière sauvegarde, pas l'état précédent
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
        private static void BackupDaily(bool hasZones)
        {
            var bdir = System.IO.Path.Combine(Dir, "backups");
            Directory.CreateDirectory(bdir);
            var b = System.IO.Path.Combine(bdir, $"layout-{DateTime.Now:yyyyMMdd}.json");
            // Ne jamais remplacer une sauvegarde non vide par un état vide.
            bool exists = File.Exists(b);
            if (!exists || (hasZones && IsEmptyLayoutFile(b)))
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, b, true);
            }
            foreach (var old in Directory.GetFiles(bdir, "layout-*.json").OrderByDescending(f => f).Skip(14))
            {
                try { File.Delete(old); } catch { }
            }
        }

        private static bool IsEmptyLayoutFile(string path)
        {
            try
            {
                var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path));
                return d == null || d.Nests == null || d.Nests.Count == 0;
            }
            catch { return true; } // illisible : peut être remplacé
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
