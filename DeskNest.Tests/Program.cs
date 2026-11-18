using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeskNest.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("aller-retour SaveNow/Load", RoundTripThroughSaveNow);
            Run("sauvegarde asynchrone suivie de Flush", AsyncSaveWithFlush);
            Run("layout.json corrompu restauré depuis layout.json.bak", CorruptLayoutFallsBackToBackup);
            Run("layout vide restauré depuis la sauvegarde", EmptyLayoutFallsBackToBackup);
            Run("layout vide volontairement conservé", ClearedLayoutIsKept);
            Run("identifiants de zone dupliqués filtrés", DuplicateZoneIdsAreRemoved);
            Run("instantané créé puis relu", SnapshotRoundTrip);
            Run("copie du fichier corrompu conservée", CorruptFileIsCopied);
            Run("dossier de stockage surchargé", StorageDirectoryIsOverridden);
            Run("traductions intégrées chargées à la demande", LocalizationLoadsActiveLanguageOnly);
            Run("profondeur d'annulation bornée à 50", UndoDepthIsBounded);
            Run("migration Version 2 vers 3 : profil, géométrie et copie .v2.bak", MigrateV2LayoutCreatesProfileAndBackup);
            Run("zone supprimée retirée de tous les profils", DeletedNestIsRemovedFromEveryProfile);
            Run("profil actif préservé si les écrans ont changé", ActiveProfileIsNotOverwrittenOnMonitorChange);

            Console.WriteLine($"{_passed} test(s) réussi(s), {_failed} échec(s).");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("OK   " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + " : " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"{what} : attendu « {expected} », obtenu « {actual} ».");
        }

        private static (MemoryFileSystem Fs, string Root) Sandbox()
        {
            var fs = new MemoryFileSystem();
            var root = Path.Combine(Path.GetTempPath(), "DeskNest.Tests", Guid.NewGuid().ToString("N"));
            Storage.UseFileSystem(fs, root);
            DisplayProfiles.UseDefaultProvider();
            return (fs, root);
        }

        private static AppData Sample(string title)
        {
            var data = new AppData();
            var zone = new NestData { Title = title };
            zone.Items.Add(@"C:\Users\example\Desktop\notes.txt");
            data.Nests.Add(zone);
            data.Settings.Language = "fr";
            data.Templates.Add(new NestTemplate { Name = "Modèle" });
            return data;
        }

        private static void RoundTripThroughSaveNow()
        {
            var (_, root) = Sandbox();
            Storage.SaveNow(Sample("Travail"));

            var loaded = Storage.Load();

            AssertEqual(Storage.LoadIssue.None, Storage.Issue, "état du chargement");
            AssertEqual(1, loaded.Nests.Count, "nombre de zones");
            AssertEqual("Travail", loaded.Nests[0].Title, "titre de la zone");
            AssertEqual(@"C:\Users\example\Desktop\notes.txt", loaded.Nests[0].Items[0], "chemin de l'élément");
            AssertEqual("fr", loaded.Settings.Language, "langue");
            AssertEqual(1, loaded.Templates.Count, "nombre de modèles");
            Assert(File.Exists(Path.Combine(root, "layout.json")) == false, "le disque réel ne doit pas être touché");
        }

        private static void AsyncSaveWithFlush()
        {
            Sandbox();
            Storage.Save(Sample("Asynchrone"));
            Storage.Flush(5000);

            var loaded = Storage.Load();

            AssertEqual(1, loaded.Nests.Count, "nombre de zones");
            AssertEqual("Asynchrone", loaded.Nests[0].Title, "titre de la zone");
        }

        private static void CorruptLayoutFallsBackToBackup()
        {
            var (fs, root) = Sandbox();
            Storage.SaveNow(Sample("Premier"));
            Storage.SaveNow(Sample("Deuxième"));

            fs.WriteAllText(Path.Combine(root, "layout.json"), "{ ceci n'est pas du JSON");

            var loaded = Storage.Load();

            AssertEqual(Storage.LoadIssue.RestoredBackup, Storage.Issue, "état du chargement");
            AssertEqual("layout.json.bak", Storage.RestoredFrom, "fichier restauré");
            AssertEqual("Premier", loaded.Nests[0].Title, "titre restauré");
        }

        private static void EmptyLayoutFallsBackToBackup()
        {
            Sandbox();
            Storage.SaveNow(Sample("Zone conservée"));
            Storage.SaveNow(new AppData()); // plus de zone, mais pas de suppression volontaire

            var loaded = Storage.Load();

            AssertEqual(Storage.LoadIssue.RestoredBackup, Storage.Issue, "état du chargement");
            AssertEqual(1, loaded.Nests.Count, "nombre de zones restaurées");
            AssertEqual("Zone conservée", loaded.Nests[0].Title, "titre restauré");
        }

        private static void ClearedLayoutIsKept()
        {
            Sandbox();
            Storage.SaveNow(new AppData { ZonesCleared = true });

            var loaded = Storage.Load();

            AssertEqual(Storage.LoadIssue.None, Storage.Issue, "état du chargement");
            AssertEqual(0, loaded.Nests.Count, "nombre de zones");
        }

        private static void DuplicateZoneIdsAreRemoved()
        {
            Sandbox();
            var id = Guid.NewGuid();
            var data = new AppData();
            data.Nests.Add(new NestData { Id = id, Title = "Première" });
            data.Nests.Add(new NestData { Id = id, Title = "Deuxième" });
            Storage.SaveNow(data);

            var loaded = Storage.Load();

            AssertEqual(1, loaded.Nests.Count, "nombre de zones après filtrage");
            AssertEqual("Première", loaded.Nests[0].Title, "zone conservée");
        }

        private static void SnapshotRoundTrip()
        {
            Sandbox();
            Storage.SaveNow(Sample("Instantané"));

            var path = Storage.CreateSnapshot();
            Assert(path != null, "l'instantané doit être créé");

            var snapshot = Storage.LoadFrom(path!);
            Assert(snapshot != null, "l'instantané doit être relisible");
            AssertEqual(1, snapshot!.Nests.Count, "nombre de zones dans l'instantané");
            AssertEqual("Instantané", snapshot.Nests[0].Title, "titre dans l'instantané");
        }

        private static void CorruptFileIsCopied()
        {
            var (fs, root) = Sandbox();
            fs.WriteAllText(Path.Combine(root, "layout.json"), "{");

            Storage.Load();

            var copies = fs.GetFiles(root, "layout-corrompu-*.json", SearchOption.TopDirectoryOnly);
            AssertEqual(1, copies.Length, "nombre de copies du fichier corrompu");
        }

        private static void StorageDirectoryIsOverridden()
        {
            var (_, root) = Sandbox();
            AssertEqual(root, Storage.Dir, "dossier de stockage");
            Assert(Storage.Dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
                "le dossier de test doit rester dans le dossier temporaire");
        }

        private static void LocalizationLoadsActiveLanguageOnly()
        {
            Loc.SetLanguage("en");
            AssertEqual("New zone", Loc.T("tray.newZone"), "textes anglais");
            Loc.SetLanguage("fr");
            AssertEqual("Nouvelle zone", Loc.T("tray.newZone"), "textes français");
            Loc.SetLanguage("de");
            AssertEqual("Neuer Bereich", Loc.T("tray.newZone"), "textes allemands");
            Assert(Loc.IsDefaultZoneTitle("Bereich 3"), "titre de zone par défaut en allemand");
            Assert(Loc.IsDefaultZoneTitle("Nouvelle zone"), "titre de zone par défaut historique");
            AssertEqual("cle.inconnue", Loc.T("cle.inconnue"), "clé absente des tables");
            Loc.SetLanguage("en");
        }

        private static void UndoDepthIsBounded()
        {
            var stack = new UndoStack();
            for (int i = 0; i < 60; i++)
                stack.Push(new DelegateUndo("étape " + i, () => { }, () => { }));

            int undone = 0;
            while (stack.Undo()) undone++;

            AssertEqual(50, undone, "nombre maximum d'étapes annulables");
            Assert(!stack.CanUndo, "aucune étape ne doit rester après 50 annulations");
        }

        private static void MigrateV2LayoutCreatesProfileAndBackup()
        {
            var (fs, root) = Sandbox();
            var monitors = new[] { new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true) };
            DisplayProfiles.UseProvider(new FakeDisplayProvider(monitors));

            var json = @"{
  ""Version"": 2,
  ""ZonesCleared"": false,
  ""Nests"": [
    {
      ""Id"": ""11111111-1111-1111-1111-111111111111"",
      ""Title"": ""Bureau"",
      ""Left"": 40.0,
      ""Top"": 40.0,
      ""Width"": 720.0,
      ""Height"": 560.0,
      ""RolledUp"": false,
      ""Items"": [ ""C:\\Users\\example\\Desktop\\notes.txt"" ]
    },
    {
      ""Id"": ""22222222-2222-2222-2222-222222222222"",
      ""Title"": ""Notes"",
      ""Left"": 2041.0,
      ""Top"": 60.0,
      ""Width"": 420.0,
      ""Height"": 320.0,
      ""RolledUp"": true
    },
    {
      ""Id"": ""33333333-3333-3333-3333-333333333333"",
      ""Title"": ""Photos"",
      ""Left"": 1995.0,
      ""Top"": 400.0,
      ""Width"": 470.0,
      ""Height"": 300.0,
      ""RolledUp"": true
    },
    {
      ""Id"": ""44444444-4444-4444-4444-444444444444"",
      ""Title"": ""Projets"",
      ""Left"": 2100.0,
      ""Top"": 720.0,
      ""Width"": 360.0,
      ""Height"": 280.0,
      ""RolledUp"": true
    },
    {
      ""Id"": ""55555555-5555-5555-5555-555555555555"",
      ""Title"": ""Musique"",
      ""Left"": 2060.0,
      ""Top"": 1020.0,
      ""Width"": 405.0,
      ""Height"": 240.0,
      ""RolledUp"": true
    },
    {
      ""Id"": ""66666666-6666-6666-6666-666666666666"",
      ""Title"": ""Outils"",
      ""Left"": 2080.0,
      ""Top"": 1260.0,
      ""Width"": 390.0,
      ""Height"": 180.0,
      ""RolledUp"": true
    }
  ]
}";
            var layoutPath = Path.Combine(root, "layout.json");
            var v2Path = Path.Combine(root, "layout.json.v2.bak");
            fs.WriteAllText(layoutPath, json);

            var loaded = Storage.Load();
            var expectedKey = DisplayProfiles.ComputeKey(monitors);
            var expected = new[]
            {
                new { Id = "11111111-1111-1111-1111-111111111111", Title = "Bureau", Left = 40d, Top = 40d, Width = 720d, Height = 560d, RolledUp = false },
                new { Id = "22222222-2222-2222-2222-222222222222", Title = "Notes", Left = 2041d, Top = 60d, Width = 420d, Height = 320d, RolledUp = true },
                new { Id = "33333333-3333-3333-3333-333333333333", Title = "Photos", Left = 1995d, Top = 400d, Width = 470d, Height = 300d, RolledUp = true },
                new { Id = "44444444-4444-4444-4444-444444444444", Title = "Projets", Left = 2100d, Top = 720d, Width = 360d, Height = 280d, RolledUp = true },
                new { Id = "55555555-5555-5555-5555-555555555555", Title = "Musique", Left = 2060d, Top = 1020d, Width = 405d, Height = 240d, RolledUp = true },
                new { Id = "66666666-6666-6666-6666-666666666666", Title = "Outils", Left = 2080d, Top = 1260d, Width = 390d, Height = 180d, RolledUp = true }
            };

            AssertEqual(3, loaded.Version, "version migrée");
            AssertEqual(expectedKey, loaded.ActiveProfileKey, "profil actif");
            AssertEqual(1, loaded.Profiles.Count, "nombre de profils créés");
            Assert(fs.FileExists(v2Path), "copie layout.json.v2.bak créée");
            AssertEqual(json, fs.ReadAllText(v2Path), "contenu de la copie V2");
            AssertEqual(json, fs.ReadAllText(layoutPath), "layout V2 non réécrit pendant la migration");
            AssertEqual(6, loaded.Nests.Count, "nombre de zones conservées");

            var profile = loaded.Profiles[0];
            AssertEqual(expectedKey, profile.Key, "clé du profil");
            Assert(profile.Nests.All(e => e.Value.GetType().GetProperties().Length == 4),
                "un profil ne stocke que Left/Top/Width/Height");

            foreach (var item in expected)
            {
                var nest = loaded.Nests.FirstOrDefault(n => n.Id.ToString() == item.Id);
                Assert(nest != null, "zone conservée " + item.Title);
                AssertEqual(item.Title, nest!.Title, "titre " + item.Title);
                AssertEqual(item.Left, nest.Left, "Left vivant " + item.Title);
                AssertEqual(item.Top, nest.Top, "Top vivant " + item.Title);
                AssertEqual(item.Width, nest.Width, "Width vivant " + item.Title);
                AssertEqual(item.Height, nest.Height, "Height vivant " + item.Title);
                AssertEqual(item.RolledUp, nest.RolledUp, "RolledUp " + item.Title);

                Assert(profile.Nests.TryGetValue(item.Id, out var g), "entrée de profil " + item.Title);
                Assert(g != null, "géométrie de profil " + item.Title);
                AssertEqual(item.Left, g!.Left, "Left du profil " + item.Title);
                AssertEqual(item.Top, g.Top, "Top du profil " + item.Title);
                AssertEqual(item.Width, g.Width, "Width du profil " + item.Title);
                AssertEqual(item.Height, g.Height, "Height du profil " + item.Title);
            }

            AssertEqual(@"C:\Users\example\Desktop\notes.txt",
                loaded.Nests[0].Items[0], "élément de la zone conservé");
            AssertEqual(6, profile.Nests.Count, "nombre d'entrées géométriques");

            fs.WriteAllText(v2Path, "copie existante");
            Storage.Load();
            AssertEqual("copie existante", fs.ReadAllText(v2Path),
                "la copie V2 ne doit être faite qu'une fois");
        }

        private static void DeletedNestIsRemovedFromEveryProfile()
        {
            Sandbox();
            DisplayProfiles.UseProvider(new FakeDisplayProvider(
                new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true)));

            var data = new AppData();
            var kept = new NestData { Title = "Gardé", Left = 10, Top = 20, Width = 300, Height = 200 };
            var removed = new NestData { Title = "Supprimé", Left = 50, Top = 60 };
            data.Nests.Add(kept);
            data.Nests.Add(removed);
            Storage.SaveNow(data);
            AssertEqual(1, data.Profiles.Count, "profil actif créé");

            var stale = Guid.NewGuid().ToString();
            foreach (var p in data.Profiles)
                p.Nests[stale] = new NestGeometry { Left = 1, Top = 2, Width = 3, Height = 4 };
            data.Nests.Remove(removed);
            Storage.SaveNow(data);

            var loaded = Storage.Load();
            AssertEqual(1, loaded.Nests.Count, "zone restante");
            Assert(!loaded.Profiles.Any(p => p.Nests.ContainsKey(removed.Id.ToString())),
                "zone supprimée retirée du profil");
            Assert(!loaded.Profiles.Any(p => p.Nests.ContainsKey(stale)),
                "entrée orpheline retirée du profil");
            Assert(loaded.Profiles[0].Nests.ContainsKey(kept.Id.ToString()),
                "zone conservée présente dans le profil");
        }

        private static void ActiveProfileIsNotOverwrittenOnMonitorChange()
        {
            Sandbox();
            var big = new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true);
            DisplayProfiles.UseProvider(new FakeDisplayProvider(big));

            var data = new AppData();
            var nest = new NestData { Title = "Ancré à droite", Left = 2040, Top = 80, Width = 420, Height = 320 };
            data.Nests.Add(nest);
            Storage.SaveNow(data);

            var key = DisplayProfiles.ComputeKey(new[] { big });
            AssertEqual(key, data.ActiveProfileKey, "profil initial");
            var saved = data.Profiles[0].Nests[nest.Id.ToString()];
            var (left, top, width, height) = (saved.Left, saved.Top, saved.Width, saved.Height);

            DisplayProfiles.UseProvider(new FakeDisplayProvider(
                new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true)));
            nest.Left = 100; // valeur vivante déjà modifiée avant la bascule D3
            Storage.SaveNow(data);

            var loaded = Storage.Load();
            AssertEqual(key, loaded.ActiveProfileKey, "clé active inchangée");
            var profile = loaded.Profiles.First(p => p.Key == key);
            var kept = profile.Nests[nest.Id.ToString()];
            AssertEqual(left, kept.Left, "Left du profil préservé");
            AssertEqual(top, kept.Top, "Top du profil préservé");
            AssertEqual(width, kept.Width, "Width du profil préservé");
            AssertEqual(height, kept.Height, "Height du profil préservé");
        }

        private sealed class FakeDisplayProvider : IDisplayLayoutProvider
        {
            private readonly IReadOnlyList<MonitorDescriptor> _monitors;

            public FakeDisplayProvider(params MonitorDescriptor[] monitors) => _monitors = monitors;

            public IReadOnlyList<MonitorDescriptor> GetMonitors() => _monitors;
        }
    }
}
