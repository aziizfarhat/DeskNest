using System;
using System.Collections.Generic;
using System.IO;

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
    }
}
