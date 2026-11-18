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
        private static IFileSystem _fs = new PhysicalFileSystem();
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
                    if (!_fs.DirectoryExists(NewDir) && _fs.DirectoryExists(LegacyDir))
                        _fs.Move(LegacyDir, NewDir);
                    else if (_fs.DirectoryExists(NewDir) && _fs.DirectoryExists(LegacyDir))
                        CopyMissingFiles(LegacyDir, NewDir);
                    _resolvedDir = NewDir;
                }
                catch
                {
                    // En cas d'échec, continuer à utiliser l'ancien emplacement plutôt que perdre les données.
                    _resolvedDir = _fs.DirectoryExists(LegacyDir) ? LegacyDir : NewDir;
                }
                return _resolvedDir;
            }
        }

        private static void CopyMissingFiles(string source, string destination)
        {
            _fs.CreateDirectory(destination);
            foreach (var directory in _fs.GetDirectories(source, "*", SearchOption.AllDirectories))
                _fs.CreateDirectory(System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, directory)));
            foreach (var file in _fs.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, file));
                if (!_fs.FileExists(target))
                {
                    _fs.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    _fs.Copy(file, target, false);
                }
            }
        }

        // Remplace le système de fichiers pour les tests. L'application utilise
        // toujours PhysicalFileSystem ; aucun appel de production ne passe par ici.
        public static void UseFileSystem(IFileSystem fileSystem, string directory)
        {
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Un dossier est requis.", nameof(directory));
            Flush();
            lock (IoGate)
            {
                lock (WriteGate)
                {
                    _fs = fileSystem;
                    _resolvedDir = directory;
                    _v3BackupPending = false;
                }
            }
        }

        public static void UsePhysicalFileSystem()
        {
            Flush();
            lock (IoGate)
            {
                lock (WriteGate)
                {
                    _fs = new PhysicalFileSystem();
                    _resolvedDir = null;
                    _v3BackupPending = false;
                }
            }
        }

        private static string FilePath => System.IO.Path.Combine(Dir, "layout.json");
        private static string BakPath => System.IO.Path.Combine(Dir, "layout.json.bak");
        private static string V2BakPath => System.IO.Path.Combine(Dir, "layout.json.v2.bak");
        private static string V3BakPath => System.IO.Path.Combine(Dir, "layout.json.v3.bak");
        private static string LastGoodPath => System.IO.Path.Combine(Dir, "layout-last-good.json");
        // Vrai tant qu'un layout lu au format V3 n'a pas encore été réécrit : la
        // première écriture par cette version conserve l'original dans .v3.bak.
        private static bool _v3BackupPending;
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
            if (_fs.FileExists(FilePath))
            {
                try
                {
                    d = JsonSerializer.Deserialize<AppData>(_fs.ReadAllText(FilePath));
                    if (d == null) Diag.Log("chargement : layout.json vide ou invalide");
                }
                catch (Exception ex)
                {
                    Diag.Log("chargement : " + ex.Message);
                    // Le fichier est illisible : on en garde une copie avant toute réécriture.
                    try
                    {
                        var copy = System.IO.Path.Combine(Dir, $"layout-corrompu-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                        _fs.Copy(FilePath, copy, true);
                    }
                    catch { }
                }
            }

            if (d != null)
            {
                d = PrepareLoaded(d, FilePath);
                // Un fichier sans zone n'est légitime que si l'utilisateur a supprimé
                // la dernière zone. Sinon (mise à jour, écriture interrompue), on
                // restaure la sauvegarde la plus récente au lieu de perdre les zones.
                if (d.Nests.Count > 0 || d.ZonesCleared) return d;
                Diag.Log("chargement : layout.json sans zone, restauration d'une sauvegarde");
            }

            // Le fichier principal est illisible ou vide : on essaie d'abord la copie
            // de l'état précédent écrite atomiquement à côté de layout.json.
            if (_fs.FileExists(BakPath))
            {
                var bak = TryReadLayout(BakPath);
                if (bak != null)
                {
                    Issue = LoadIssue.RestoredBackup;
                    RestoredFrom = System.IO.Path.GetFileName(BakPath);
                    Diag.Log($"chargement : sauvegarde restaurée ({RestoredFrom})");
                    return bak;
                }
                Diag.Log("chargement : layout.json.bak illisible ou vide");
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

        private static AppData? TryReadLayout(string path)
        {
            try
            {
                var d = JsonSerializer.Deserialize<AppData>(_fs.ReadAllText(path));
                if (d == null) return null;
                d = PrepareLoaded(d, path);
                // Une sauvegarde vide n'est jamais une restauration utile.
                return d.Nests.Count > 0 ? d : null;
            }
            catch { return null; }
        }

        // ---------- Sauvegarde non bloquante ----------
        // La sérialisation se fait sur le thread appelant (les données ne sont pas thread-safe),
        // puis l'écriture disque et les sauvegardes quotidiennes sont faites en arrière-plan.
        // Une rafale de modifications (curseur d'opacité, glissement…) ne produit qu'une écriture.
        private static readonly object WriteGate = new();
        private static readonly object IoGate = new();
        private static string? _pendingJson;
        private static bool _pendingHasZones;
        private static bool _writerActive;
        private static int _syncWrites;

        public static void Save(AppData data)
        {
            string json;
            bool hasZones;
            var watch = Stopwatch.StartNew();
            try
            {
                EnsureV3Backup();
                Normalize(data);
                DisplayProfiles.EnsureActiveProfile(data);
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

        // Écrit immédiatement l'état sur disque. Utilisé quand un fichier vient d'être
        // déplacé dans le stockage : même si l'application se termine brutalement,
        // la correspondance « élément rangé / emplacement d'origine » est déjà enregistrée.
        public static void SaveNow(AppData data)
        {
            string json;
            bool hasZones;
            var watch = Stopwatch.StartNew();
            try
            {
                EnsureV3Backup();
                Normalize(data);
                DisplayProfiles.EnsureActiveProfile(data);
                hasZones = data.Nests.Count > 0;
                json = JsonSerializer.Serialize(data, Opt);
            }
            catch (Exception ex)
            {
                Diag.Log("sauvegarde immédiate : " + ex.Message);
                return;
            }

            Interlocked.Increment(ref _syncWrites);
            try
            {
                // IoGate sérialise les écritures : une sauvegarde asynchrone déjà
                // partie ne peut pas écraser cet état plus récent.
                lock (IoGate)
                {
                    lock (WriteGate) _pendingJson = null;
                    WriteJsonCore(json, hasZones);
                }
            }
            finally { Interlocked.Decrement(ref _syncWrites); }

            if (watch.ElapsedMilliseconds > 200)
                Diag.Log($"sauvegarde immédiate : écriture lente ({watch.ElapsedMilliseconds} ms)");
        }

        private static void WriterLoop()
        {
            while (true)
            {
                string? json;
                bool hasZones;
                // IoGate reste tenu pendant la capture et l'écriture : SaveNow ne
                // peut pas s'intercaler entre les deux.
                lock (IoGate)
                {
                    lock (WriteGate)
                    {
                        json = _pendingJson;
                        hasZones = _pendingHasZones;
                        _pendingJson = null;
                        if (json == null) { _writerActive = false; return; }
                    }
                    WriteJsonCore(json, hasZones);
                }
            }
        }

        private static void WriteJsonCore(string json, bool hasZones)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                _fs.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                _fs.WriteAllText(tmp, json);

                // Écriture atomique : le fichier principal n'est jamais partiellement
                // remplacé. L'ancien état reste disponible dans layout.json.bak.
                if (_fs.FileExists(FilePath))
                {
                    try { _fs.Replace(tmp, FilePath, BakPath, true); }
                    catch
                    {
                        // Dernier recours (système de fichiers exotique) : ne jamais
                        // perdre la nouvelle sauvegarde.
                        _fs.Copy(tmp, FilePath, true);
                        try { _fs.Delete(tmp); } catch { }
                    }
                }
                else
                {
                    _fs.Move(tmp, FilePath);
                }

                // Copie « dernier état valide » : utilisée au démarrage si layout.json est vide.
                if (hasZones)
                {
                    try { _fs.Copy(FilePath, LastGoodPath, true); } catch { }
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
                    if (!_writerActive && _pendingJson == null && Volatile.Read(ref _syncWrites) == 0) return;
                Thread.Sleep(10);
            }
        }

        // Prépare un layout lu sur disque : migration V2 -> V3 (avec copie unique du
        // fichier source) puis profils normalisés. Les positions vivantes ne bougent pas.
        private static AppData PrepareLoaded(AppData d, string? v2SourcePath)
        {
            d = Normalize(d);
            if (d.Version < 3 && v2SourcePath != null)
                CopyV2Backup(v2SourcePath);
            if (d.Version < 3)
                Diag.Log("migration : layout Version 2 vers Version 3");
            // Un layout au format V3 doit être conservé une fois avant la première
            // écriture faite par cette version (qui ajoute Type / Tasks / réglages).
            if (d.Version <= 3) _v3BackupPending = true;
            DisplayProfiles.EnsureActiveProfile(d);
            return d;
        }

        // Copie unique du layout d'origine avant toute réécriture au format 0.8.6.
        // Le fichier n'est jamais écrasé : les versions précédentes restent restaurables.
        private static void EnsureV3Backup()
        {
            if (!_v3BackupPending) return;
            _v3BackupPending = false;
            try
            {
                if (_fs.FileExists(V3BakPath))
                {
                    Diag.Log("migration : layout.json.v3.bak déjà présent");
                    return;
                }
                if (!_fs.FileExists(FilePath)) return;
                _fs.Copy(FilePath, V3BakPath, false);
                Diag.Log("migration : copie conservée dans layout.json.v3.bak");
            }
            catch (Exception ex)
            {
                Diag.Log("migration : copie V3 impossible : " + ex.Message);
            }
        }

        // Une seule copie du layout d'origine, avant toute réécriture au format V3.
        private static void CopyV2Backup(string sourcePath)
        {
            try
            {
                if (string.Equals(sourcePath, V2BakPath, StringComparison.OrdinalIgnoreCase)) return;
                if (_fs.FileExists(V2BakPath))
                {
                    Diag.Log("migration : layout.json.v2.bak déjà présent");
                    return;
                }
                if (!_fs.FileExists(sourcePath)) return;
                _fs.Copy(sourcePath, V2BakPath, false);
                Diag.Log("migration : copie conservée dans layout.json.v2.bak");
            }
            catch (Exception ex)
            {
                Diag.Log("migration : copie V2 impossible : " + ex.Message);
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
            DisplayProfiles.NormalizeProfiles(d);

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
                f.Tasks ??= new List<TodoTask>();
                f.PortalOrder ??= new Dictionary<string, List<string>>();
                f.Title ??= "";
                f.Color ??= "#1E1E2E";
                f.GradientStart ??= "#1E1E2E";
                f.GradientEnd ??= "#4A3426";
                f.BackgroundImage ??= "";
                // Un layout sans Type (ou un Type inconnu) reste une zone de fichiers.
                if (!NestTypes.IsKnown(f.Type)) f.Type = NestTypes.Files;
                foreach (var task in f.Tasks)
                {
                    if (task == null) continue;
                    task.Text ??= "";
                    if (task.Id == Guid.Empty) task.Id = Guid.NewGuid();
                }
                f.Tasks.RemoveAll(t => t == null);
                f.BackgroundMode = Math.Clamp(f.BackgroundMode, 0, 3);
                f.GradientAngle %= 360;
                if (f.GradientAngle < 0) f.GradientAngle += 360;
            }

            // Actualise les chemins enregistrés si l'ancien dossier d'éléments a été renommé.
            // Avec un système de fichiers factice (tests), on ne touche jamais au vrai profil.
            if (_fs is PhysicalFileSystem && DesktopStore.MigrateLegacyStore(out bool storeMoved) && storeMoved)
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
                if (_fs.DirectoryExists(bdir)) candidates.AddRange(_fs.GetFiles(bdir, "layout-*.json", SearchOption.TopDirectoryOnly));
                // Le fichier principal est corrompu ou vide : l'état précédent est
                // dans layout.json.bak (écriture atomique via File.Replace).
                if (_fs.FileExists(BakPath)) candidates.Add(BakPath);
                if (_fs.FileExists(LastGoodPath)) candidates.Add(LastGoodPath);

                foreach (var f in candidates.OrderByDescending(_fs.GetLastWriteTimeUtc))
                {
                    var d = TryReadLayout(f);
                    if (d != null) return (f, d);
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
                if (!_fs.FileExists(FilePath)) return null;
                _fs.CreateDirectory(SnapshotDir);
                // Millisecondes : deux instantanés créés dans la même seconde ne s'écrasent plus.
                var p = System.IO.Path.Combine(SnapshotDir, $"layout-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                _fs.Copy(FilePath, p, true);
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
                var d = JsonSerializer.Deserialize<AppData>(_fs.ReadAllText(path));
                return d == null ? null : PrepareLoaded(d, null);
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
            _fs.CreateDirectory(bdir);
            var b = System.IO.Path.Combine(bdir, $"layout-{DateTime.Now:yyyyMMdd}.json");
            // Ne jamais remplacer une sauvegarde non vide par un état vide.
            bool exists = _fs.FileExists(b);
            if (!exists || (hasZones && IsEmptyLayoutFile(b)))
            {
                if (_fs.FileExists(FilePath)) _fs.Copy(FilePath, b, true);
            }
            foreach (var old in _fs.GetFiles(bdir, "layout-*.json", SearchOption.TopDirectoryOnly).OrderByDescending(f => f).Skip(14))
            {
                try { _fs.Delete(old); } catch { }
            }
        }

        private static bool IsEmptyLayoutFile(string path)
        {
            try
            {
                var d = JsonSerializer.Deserialize<AppData>(_fs.ReadAllText(path));
                return d == null || d.Nests == null || d.Nests.Count == 0;
            }
            catch { return true; } // illisible : peut être remplacé
        }

        // On garde les 30 instantanés les plus récents.
        private static void PruneSnapshots()
        {
            foreach (var old in _fs.GetFiles(SnapshotDir, "layout-*.json", SearchOption.TopDirectoryOnly).OrderByDescending(f => f).Skip(30))
            {
                try { _fs.Delete(old); } catch { }
            }
        }
    }
}
