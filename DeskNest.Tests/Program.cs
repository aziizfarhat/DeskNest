using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

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
            Run("N1 : ancien layout en Files et copie .v3.bak une seule fois", OldLayoutLoadsAsFilesAndCreatesV3Backup);
            Run("N2 : notes aller-retour et échec d'écriture conservé", NotesRoundTripAndWriteFailureKeepsText);
            Run("N3 : ordre des tâches coché en bas et ajout haut/bas", TodoOrderingRules);
            Run("N5 : placement au curseur clampé et sans recouvrement", CursorPlacementAvoidsWindows);
            Run("N6 : anciens réglages chargés avec les nouvelles valeurs par défaut", OldSettingsKeepNewDefaults);
            Run("zone supprimée retirée de tous les profils", DeletedNestIsRemovedFromEveryProfile);
            Run("profil actif préservé si les écrans ont changé", ActiveProfileIsNotOverwrittenOnMonitorChange);
            Run("bascule d'écran : ancien profil capturé puis profil exact appliqué", DisplaySwitchCapturesOldAndAppliesExactProfile);
            Run("configuration inconnue : ancrage droit et hauteur développée cadrée", UnknownConfigurationKeepsRightAnchorAndClampsExpandedSize);
            Run("écran secondaire débranché : zones ramenées sur le principal", RemovedSecondaryMonitorMovesNestsToPrimary);
            Run("profil source : plus grande recouvrance d'écran choisie", MostSimilarProfileIsUsedAsSource);
            Run("sécurité : zone entièrement hors écran ramenée à l'écran", FullyOffScreenNestIsMovedInside);
            Run("sécurité : zone partiellement visible laissée en place", PartiallyVisibleNestIsKept);
            Run("D6 : aller-retour 2560x1440 → 1920x1080 → 2560x1440", RoundTripAcrossMonitorChangeRestoresExactLayout);
            Run("N7 : parité des clés de traduction dans les douze langues", LocalizationKeysAreComplete);
            Run("N7 : profils d'affichage préservent Notes et To-do", NewTypesSurviveDisplayProfiles);
            Run("F1 : titres par défaut localisés et numéro libre le plus bas", F1DefaultTitlesUseLocalizedLowestUnusedNumber);
            Run("F2/F3 : ajout, édition et refus de tâche vide", F2F3TodoAddEditAndEmptyRules);
            Run("F5 : cycle de vie des brouillons de note", F5NoteDraftLifecycle);

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

        private static string SampleV3Json() => @"{
  ""Version"": 3,
  ""ZonesCleared"": false,
  ""Nests"": [
    {
      ""Id"": ""aaaaaaaa-1111-1111-1111-111111111111"",
      ""Title"": ""Bureau"",
      ""Left"": 40.0,
      ""Top"": 60.0,
      ""Width"": 320.0,
      ""Height"": 240.0,
      ""Items"": [ ""C:\\Users\\example\\Desktop\\notes.txt"" ]
    },
    {
      ""Id"": ""bbbbbbbb-2222-2222-2222-222222222222"",
      ""Title"": ""Portail"",
      ""PortalPath"": ""C:\\Users\\example\\Documents"",
      ""Left"": 500.0,
      ""Top"": 60.0,
      ""Width"": 320.0,
      ""Height"": 240.0
    }
  ],
  ""Settings"": { ""Language"": ""fr"" }
}";

        private static void OldLayoutLoadsAsFilesAndCreatesV3Backup()
        {
            var (fs, root) = Sandbox();
            var json = SampleV3Json();
            var layoutPath = Path.Combine(root, "layout.json");
            var v3Path = Path.Combine(root, "layout.json.v3.bak");
            fs.WriteAllText(layoutPath, json);

            var loaded = Storage.Load();

            AssertEqual(3, loaded.Version, "version laissée inchangée au chargement");
            AssertEqual(2, loaded.Nests.Count, "nombre de zones conservées");
            Assert(loaded.Nests.All(n => n.Type == NestTypes.Files), "anciennes zones toutes de type Files");
            AssertEqual("fr", loaded.Settings.Language, "réglages conservés");
            Assert(!fs.FileExists(v3Path), "pas de copie avant la première écriture");
            AssertEqual(json, fs.ReadAllText(layoutPath), "layout non réécrit au chargement");

            Storage.SaveNow(loaded);
            Assert(fs.FileExists(v3Path), "copie layout.json.v3.bak créée");
            AssertEqual(json, fs.ReadAllText(v3Path), "contenu original conservé");

            Storage.SaveNow(loaded);
            AssertEqual(json, fs.ReadAllText(v3Path), "copie non écrasée par la suite");
        }

        private static void LocalizationKeysAreComplete()
        {
            var assembly = typeof(Loc).Assembly;
            Dictionary<string, string>? english = null;
            foreach (var code in new[] { "en", "fr", "it", "de", "es", "pt", "ar", "zh", "ja", "ko", "ru", "uk" })
            {
                using var stream = assembly.GetManifestResourceStream("DeskNest.Localization." + code + ".json");
                Assert(stream != null, "table de traduction " + code);
                var table = JsonSerializer.Deserialize<Dictionary<string, string>>(stream!)!;
                if (english == null) english = table;
                else AssertEqual(english.Count, table.Count, "nombre de clés en " + code);
                foreach (var key in english.Keys)
                    Assert(table.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
                        "clé « " + key + " » absente de " + code);
            }

            foreach (var key in new[]
            {
                "tab.notesTodo", "nt.todoTitle", "nt.todoStrike", "nt.todoBottom", "nt.todoAddTop",
                "nt.notesTitle", "nt.notesFontSize", "nt.notesWrap", "nt.notesSaveDelay", "nt.quickSearch",
                "todo.empty", "todo.count", "todo.drag", "tray.newTodo", "tray.newNote",
                "todo.default", "note.default", "todo.add", "todo.edit", "todo.validate", "nt.todoFontSize",
                "note.edit", "note.validate", "note.discard", "note.discardTitle" })
                Assert(english!.ContainsKey(key), "nouvelle clé absente : " + key);
        }

        private static void NewTypesSurviveDisplayProfiles()
        {
            var (_, root) = Sandbox();
            var big = new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true, 0, 0, 2560, 1380);
            var small = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var bigKey = DisplayProfiles.ComputeKey(new[] { big });
            var smallKey = DisplayProfiles.ComputeKey(new[] { small });

            DisplayProfiles.UseProvider(new FakeDisplayProvider(big));
            var data = Sample("Notes");
            var notes = data.Nests[0];
            notes.Type = NestTypes.Notes;
            notes.Left = 2000; notes.Top = 200; notes.Width = 420; notes.Height = 300;
            double notesLeft = notes.Left, notesTop = notes.Top;
            var todo = new NestData
            {
                Type = NestTypes.Todo, Title = "Tâches",
                Left = 2100, Top = 600, Width = 380, Height = 300
            };
            todo.Tasks.Add(new TodoTask { Text = "Acheter du lait", Done = true });
            data.Nests.Add(todo);
            Storage.SaveNow(data);

            var loaded = Storage.Load();
            AssertEqual(bigKey, loaded.ActiveProfileKey, "profil initial");
            AssertEqual(NestTypes.Notes, loaded.Nests[0].Type, "type Notes conservé");
            AssertEqual(NestTypes.Todo, loaded.Nests[1].Type, "type Todo conservé");
            AssertEqual(1, loaded.Nests[1].Tasks.Count, "tâches conservées");
            AssertEqual("Acheter du lait", loaded.Nests[1].Tasks[0].Text, "texte de tâche conservé");
            Assert(loaded.Nests[1].Tasks[0].Done, "état coché conservé");

            DisplayProfileSwitcher.Switch(loaded, new[] { small });
            Storage.SaveNow(loaded);
            DisplayProfiles.UseProvider(new FakeDisplayProvider(small));
            var adapted = Storage.Load();
            AssertEqual(smallKey, adapted.ActiveProfileKey, "profil portable");
            AssertEqual(NestTypes.Notes, adapted.Nests[0].Type, "type Notes après adaptation");
            AssertEqual(NestTypes.Todo, adapted.Nests[1].Type, "type Todo après adaptation");
            Assert(adapted.Nests.All(n => n.Left >= 0 && n.Left + n.Width <= 1920), "zones dans le portable");
            AssertEqual("Acheter du lait", adapted.Nests[1].Tasks[0].Text, "tâches après adaptation");

            var restored = DisplayProfileSwitcher.Switch(adapted, new[] { big });
            Assert(restored.ProfileFound, "profil exact retrouvé");
            AssertEqual(bigKey, adapted.ActiveProfileKey, "profil 2560x1440 restauré");
            AssertEqual(notesLeft, adapted.Nests[0].Left, "position Notes restaurée");
            AssertEqual(notesTop, adapted.Nests[0].Top, "hauteur Notes restaurée");
            AssertEqual(NestTypes.Todo, adapted.Nests[1].Type, "type Todo restauré");
        }

        private static void OldSettingsKeepNewDefaults()
        {
            var (fs, root) = Sandbox();
            var json = @"{
  ""Version"": 3,
  ""ZonesCleared"": false,
  ""Nests"": [ { ""Id"": ""cccccccc-3333-3333-3333-333333333333"", ""Title"": ""Zone"" } ],
  ""Settings"": { ""Language"": ""fr"", ""ShowSearchBar"": false }
}";
            fs.WriteAllText(Path.Combine(root, "layout.json"), json);

            var s = Storage.Load().Settings;

            AssertEqual("fr", s.Language, "ancien réglage de langue conservé");
            Assert(!s.ShowSearchBar, "ancien réglage de barre de recherche conservé");
            AssertEqual(13, s.NotesFontSize, "taille des notes par défaut");
            Assert(s.NotesWrap, "retour à la ligne des notes par défaut");
            AssertEqual(800, s.NotesSaveDelayMs, "délai de sauvegarde par défaut");
            AssertEqual(12, s.TodoFontSize, "taille des tâches par défaut");
            Assert(s.TodoStrikeThrough, "barré des tâches cochées par défaut");
            Assert(!s.TodoCheckedToBottom, "tâches cochées en bas désactivé par défaut");
            Assert(!s.TodoAddAtTop, "ajout en haut désactivé par défaut");
            Assert(s.ShowQuickActionsInSearch, "actions rapides dans la recherche actives par défaut");
        }

        private static void CursorPlacementAvoidsWindows()
        {
            var work = new Rect(0, 0, 1920, 1040);
            var ctx = new CursorPlacement.Context(new Point(1000, 600), work);
            var existing = new List<Rect> { new Rect(950, 550, 320, 240) };
            var spot = CursorPlacement.FindFreeSpot(ctx, 320, 240, existing);
            var rect = new Rect(spot.X, spot.Y, 320, 240);

            Assert(!rect.IntersectsWith(existing[0]), "nouvelle zone sans recouvrement");
            Assert(rect.Left >= work.Left + 8 && rect.Right <= work.Right - 8 &&
                   rect.Top >= work.Top + 8 && rect.Bottom <= work.Bottom - 8, "dans la zone utile");

            var corner = new CursorPlacement.Context(new Point(1910, 1030), work);
            var edge = CursorPlacement.FindFreeSpot(corner, 320, 240, new List<Rect>());
            var edgeRect = new Rect(edge.X, edge.Y, 320, 240);
            Assert(edgeRect.Right <= work.Right - 8 && edgeRect.Bottom <= work.Bottom - 8,
                "zone clampée près du bord");
        }

        private static void TodoOrderingRules()
        {
            var a = new TodoTask { Text = "A" };
            var b = new TodoTask { Text = "B" };
            var c = new TodoTask { Text = "C" };
            var tasks = new List<TodoTask> { a, b, c };
            static string Text(IEnumerable<TodoTask> list) => string.Join(",", list.Select(t => t.Text));

            AssertEqual("A,B,C", Text(TodoOrder.Sort(tasks, false)), "ordre manuscrit");

            b.Done = true;
            AssertEqual("A,C,B", Text(TodoOrder.Sort(tasks, true)), "tâche cochée en bas");
            AssertEqual("A,B,C", Text(TodoOrder.Sort(tasks, false)), "ordre manuscrit inchangé");

            b.Done = false;
            AssertEqual("A,B,C", Text(TodoOrder.Sort(tasks, true)), "position restaurée après décochage");

            var d = new TodoTask { Text = "D" };
            TodoOrder.Insert(tasks, d, false);
            AssertEqual("A,B,C,D", Text(tasks), "ajout en bas");
            var e = new TodoTask { Text = "E" };
            TodoOrder.Insert(tasks, e, true);
            AssertEqual("E,A,B,C,D", Text(tasks), "ajout en haut");

            TodoOrder.Move(tasks, d, null, b);
            AssertEqual("E,A,D,B,C", Text(tasks), "déplacement avant une tâche");
            AssertEqual("E,A,D,B,C", Text(TodoOrder.Sort(tasks, false)), "ordre conservé après lecture");
        }

        private static void NotesRoundTripAndWriteFailureKeepsText()
        {
            var (fs, root) = Sandbox();
            var id = Guid.NewGuid();
            var path = Path.Combine(root, "Notes", id.ToString("N") + ".txt");

            AssertEqual("", NotesStore.Load(id), "note absente = texte vide");
            Assert(NotesStore.Save(id, "Première ligne\nDeuxième ligne"), "première sauvegarde");
            AssertEqual("Première ligne\nDeuxième ligne", NotesStore.Load(id), "aller-retour de la note");
            Assert(fs.FileExists(path), "fichier de note créé");
            Assert(!File.Exists(path), "aucune écriture sur le disque réel");

            var data = Sample("Notes");
            data.Nests[0].Type = NestTypes.Notes;
            Storage.SaveNow(data);
            var layout = fs.ReadAllText(Path.Combine(root, "layout.json"));
            Assert(!layout.Contains("Première ligne"), "le texte d'une note n'est pas dans layout.json");

            Storage.UseFileSystem(new FailingFileSystem(fs), root);
            Assert(!NotesStore.Save(id, "Nouveau texte"), "échec d'écriture signalé");
            AssertEqual("Première ligne\nDeuxième ligne", NotesStore.Load(id), "ancien contenu intact après échec");

            Storage.UseFileSystem(fs, root);
            NotesStore.RetryPending();
            AssertEqual("Nouveau texte", NotesStore.Load(id), "texte réécrit après réussite");
        }

        private static void F1DefaultTitlesUseLocalizedLowestUnusedNumber()
        {
            Loc.SetLanguage("en");

            // La clé dépend du type ; les zones classiques gardent « Zone N ».
            AssertEqual("zone.default", DefaultTitles.KeyFor(NestTypes.Files), "clé Files");
            AssertEqual("todo.default", DefaultTitles.KeyFor(NestTypes.Todo), "clé Todo");
            AssertEqual("note.default", DefaultTitles.KeyFor(NestTypes.Notes), "clé Notes");
            AssertEqual("zone.default", DefaultTitles.KeyFor(null), "clé type inconnu");
            AssertEqual("Todo 1", Loc.F("todo.default", 1), "préfixe Todo anglais");
            AssertEqual("Note 1", Loc.F("note.default", 1), "préfixe Note anglais");

            // Numéro libre le plus bas, y compris avec un trou.
            var todos = new[] { Loc.F("todo.default", 1), Loc.F("todo.default", 3), Loc.F("todo.default", 4) };
            AssertEqual(2, DefaultTitles.NextNumber("todo.default", todos), "trou comblé pour Todo");
            var notes = new[] { Loc.F("note.default", 2), Loc.F("note.default", 5) };
            AssertEqual(1, DefaultTitles.NextNumber("note.default", notes), "premier numéro libre pour Note");

            // Après suppression du numéro 2, on le réutilise.
            var remaining = new List<string>
            {
                Loc.F("todo.default", 1), Loc.F("todo.default", 2), Loc.F("todo.default", 3)
            };
            remaining.RemoveAt(1);
            AssertEqual(2, DefaultTitles.NextNumber("todo.default", remaining), "numéro réutilisé après suppression");

            // Un titre d'un autre type n'entre pas en compte.
            var mixed = new[] { Loc.F("zone.default", 1), Loc.F("zone.default", 2), Loc.F("todo.default", 1) };
            AssertEqual(2, DefaultTitles.NextNumber("todo.default", mixed), "numérotation par type");

            // Préfixe localisé : la numérotation suit la langue active.
            Loc.SetLanguage("fr");
            AssertEqual("Tâches 1", Loc.F("todo.default", 1), "préfixe Todo français");
            var french = new[] { Loc.F("todo.default", 1), Loc.F("todo.default", 2) };
            AssertEqual(3, DefaultTitles.NextNumber("todo.default", french), "numérotation en français");
            Loc.SetLanguage("en");
        }

        private static void F2F3TodoAddEditAndEmptyRules()
        {
            // La touche Entrée (principale/pavé) et le bouton « + » utilisent la même voie.
            Assert(TodoOrder.TryCreate("  Acheter du lait  ", out var created), "saisie non vide acceptée");
            AssertEqual("Acheter du lait", created!.Text, "texte coupé autour");
            Assert(!TodoOrder.TryCreate("   ", out var blank), "saisie blanche refusée");
            Assert(blank == null, "aucune tâche créée pour une saisie blanche");
            Assert(!TodoOrder.TryCreate("", out var empty), "saisie vide refusée");
            Assert(!TodoOrder.TryCreate(null, out var none), "saisie nulle refusée");

            // Ajout en haut / en bas, identique pour la touche et le bouton.
            var tasks = new List<TodoTask>();
            TodoOrder.Insert(tasks, new TodoTask { Text = "Bas" }, false);
            TodoOrder.Insert(tasks, new TodoTask { Text = "Haut" }, true);
            AssertEqual("Haut,Bas", string.Join(",", tasks.Select(t => t.Text)), "ajout haut et bas");

            // Édition en place : validation et refus du vide.
            var task = new TodoTask { Text = "Ancien texte" };
            Assert(!TodoOrder.CommitEdit(task, "   "), "édition vide refusée");
            AssertEqual("Ancien texte", task.Text, "texte conservé après refus");
            Assert(TodoOrder.CommitEdit(task, "  Nouveau texte  "), "édition validée");
            AssertEqual("Nouveau texte", task.Text, "texte mis à jour");
        }

        private static void F5NoteDraftLifecycle()
        {
            var (fs, _) = Sandbox();
            var id = Guid.NewGuid();
            var notePath = NotesStore.PathFor(id);
            var draftPath = NotesStore.DraftPathFor(id);

            AssertEqual(id.ToString("N") + ".draft.txt", Path.GetFileName(draftPath), "nom du brouillon");
            Assert(!NotesStore.HasDraft(id), "aucun brouillon au départ");
            AssertEqual("", NotesStore.LoadDraft(id), "brouillon absent = vide");

            // Création : seule la saisie est écrite, jamais la note réelle.
            Assert(NotesStore.SaveDraft(id, "Brouillon en cours"), "brouillon créé");
            Assert(NotesStore.HasDraft(id), "brouillon détecté");
            Assert(fs.FileExists(draftPath), "fichier .draft.txt présent");
            Assert(!fs.FileExists(draftPath + ".tmp"), "aucun temporaire laissé par l'écriture atomique");
            AssertEqual("Brouillon en cours", NotesStore.LoadDraft(id), "brouillon relu");
            AssertEqual("", NotesStore.Load(id), "note réelle intacte pendant l'édition");
            Assert(!fs.FileExists(notePath), "aucun fichier de note avant validation");

            // Validation : le brouillon devient la note et disparaît.
            Assert(NotesStore.PromoteDraft(id, "Texte validé"), "brouillon promu");
            AssertEqual("Texte validé", NotesStore.Load(id), "note enregistrée");
            Assert(!NotesStore.HasDraft(id), "brouillon supprimé après validation");
            AssertEqual("", NotesStore.LoadDraft(id), "plus de brouillon");

            // Annulation : on garde la note, on jette le brouillon.
            Assert(NotesStore.SaveDraft(id, "À annuler"), "nouveau brouillon");
            NotesStore.DeleteDraft(id);
            Assert(!NotesStore.HasDraft(id), "brouillon annulé");
            AssertEqual("Texte validé", NotesStore.Load(id), "note conservée après annulation");

            // Reprise au démarrage : un brouillon existant est relu sans écraser la note.
            Assert(NotesStore.SaveDraft(id, "Récupéré au démarrage"), "brouillon avant redémarrage");
            Assert(NotesStore.HasDraft(id), "brouillon à récupérer");
            AssertEqual("Récupéré au démarrage", NotesStore.LoadDraft(id), "texte récupéré");
            AssertEqual("Texte validé", NotesStore.Load(id), "note non écrasée par la récupération");

            // Longueur bornée, comme la note.
            Assert(NotesStore.SaveDraft(id, new string('x', NotesStore.MaxChars + 1)), "brouillon long accepté");
            AssertEqual(NotesStore.MaxChars, NotesStore.LoadDraft(id).Length, "brouillon tronqué à la limite");

            // Suppression de la zone : note et brouillon disparaissent.
            NotesStore.Delete(id);
            Assert(!NotesStore.HasDraft(id), "brouillon supprimé avec la zone");
            AssertEqual("", NotesStore.Load(id), "note supprimée avec la zone");
            Assert(!fs.FileExists(notePath), "fichier de note supprimé");
            Assert(!fs.FileExists(draftPath), "fichier de brouillon supprimé");
        }

        private sealed class FailingFileSystem : IFileSystem
        {
            private readonly IFileSystem _inner;
            public FailingFileSystem(IFileSystem inner) => _inner = inner;

            public bool FileExists(string path) => _inner.FileExists(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public void WriteAllText(string path, string contents) => throw new IOException("écriture simulée en échec");
            public void Copy(string sourcePath, string destinationPath, bool overwrite) => throw new IOException("copie simulée en échec");
            public void Move(string sourcePath, string destinationPath) => throw new IOException("déplacement simulé en échec");
            public void Delete(string path) => _inner.Delete(path);
            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => _inner.GetFiles(path, searchPattern, searchOption);
            public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) => _inner.GetDirectories(path, searchPattern, searchOption);
            public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
            public void Replace(string sourcePath, string destinationPath, string destinationBackupPath, bool ignoreMetadataErrors) => throw new IOException("remplacement simulé en échec");
        }

        private static void MigrateV2LayoutCreatesProfileAndBackup()
        {
            var (fs, root) = Sandbox();
            var monitors = new[] { new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true) };
            DisplayProfiles.UseProvider(new FakeDisplayProvider(monitors));

            var json = SampleV2Json();
            var layoutPath = Path.Combine(root, "layout.json");
            var v2Path = Path.Combine(root, "layout.json.v2.bak");
            fs.WriteAllText(layoutPath, json);

            var loaded = Storage.Load();
            var expectedKey = DisplayProfiles.ComputeKey(monitors);
            var expected = SampleV2Expectations();

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

        private static void DisplaySwitchCapturesOldAndAppliesExactProfile()
        {
            var big = new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true);
            var small = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true);
            var bigKey = DisplayProfiles.ComputeKey(new[] { big });
            var smallKey = DisplayProfiles.ComputeKey(new[] { small });

            var nest = new NestData
            {
                Title = "Ancré à droite",
                Left = 2040, Top = 80, Width = 420, Height = 320, RolledUp = true
            };
            var data = new AppData { ActiveProfileKey = bigKey };
            data.Nests.Add(nest);
            data.Profiles.Add(new DisplayProfile
            {
                Key = bigKey,
                Description = "Grand écran",
                Nests = { [nest.Id.ToString()] = new NestGeometry { Left = 2040, Top = 80, Width = 420, Height = 320 } }
            });
            data.Profiles.Add(new DisplayProfile
            {
                Key = smallKey,
                Description = "Portable",
                Nests = { [nest.Id.ToString()] = new NestGeometry { Left = 100, Top = 40, Width = 380, Height = 280 } }
            });

            nest.Left = 2050; // position réellement atteinte avant le débranchement

            var result = DisplayProfileSwitcher.Switch(data, new[] { small });

            Assert(result.Changed, "la configuration a changé");
            Assert(result.ProfileFound, "le profil du portable existe");
            Assert(!result.Adapted, "aucune adaptation nécessaire");
            AssertEqual(smallKey, data.ActiveProfileKey, "nouveau profil actif");
            AssertEqual(100d, nest.Left, "Left appliqué exactement");
            AssertEqual(40d, nest.Top, "Top appliqué exactement");
            AssertEqual(380d, nest.Width, "Width appliqué exactement");
            AssertEqual(280d, nest.Height, "Height appliqué exactement");

            var oldProfile = data.Profiles.First(p => p.Key == bigKey);
            AssertEqual(2050d, oldProfile.Nests[nest.Id.ToString()].Left,
                "ancienne position capturée avant la bascule");
            var target = data.Profiles.First(p => p.Key == smallKey);
            AssertEqual(100d, target.Nests[nest.Id.ToString()].Left,
                "profil cible non écrasé par l'ancienne position");

            nest.Left = 777; // l'utilisateur a déplacé la zone sur le portable
            var same = DisplayProfileSwitcher.Switch(data, new[] { small });
            Assert(!same.Changed, "un événement sans changement de clé ne bascule pas");
            AssertEqual(777d, nest.Left, "position inchangée sans bascule");
        }

        private static void UnknownConfigurationKeepsRightAnchorAndClampsExpandedSize()
        {
            var big = new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true, 0, 0, 2560, 1380);
            var small = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var bigKey = DisplayProfiles.ComputeKey(new[] { big });
            var smallKey = DisplayProfiles.ComputeKey(new[] { small });

            var right = new NestData
            {
                Title = "Droite", Left = 2041, Top = 60, Width = 420, Height = 320, RolledUp = true
            };
            var tall = new NestData
            {
                Title = "Développé", Left = 100, Top = 100, Width = 500, Height = 1200, RolledUp = true
            };
            var data = new AppData { ActiveProfileKey = bigKey };
            data.Nests.Add(right);
            data.Nests.Add(tall);
            data.Profiles.Add(new DisplayProfile
            {
                Key = bigKey,
                Description = "Grand écran",
                Nests = DisplayProfiles.CaptureGeometry(data.Nests)
            });
            DisplayProfiles.RememberMonitors(data, bigKey, new[] { big });

            var result = DisplayProfileSwitcher.Switch(data, new[] { small });

            Assert(result.Changed, "changement de configuration");
            Assert(result.Adapted, "profil inconnu adapté");
            AssertEqual(smallKey, data.ActiveProfileKey, "nouveau profil actif");
            // 2560 - (2041 + 420) = 99 DIP du bord droit ; 1920 - 420 - 99 = 1401.
            AssertEqual(1401d, right.Left, "ancrage droit conservé");
            AssertEqual(1821d, right.Left + right.Width, "bord droit après adaptation");
            // Hauteur développée 1200 > 1020 : elle est réduite et le haut reste atteignable.
            AssertEqual(1020d, tall.Height, "hauteur développée cadrée");
            AssertEqual(0d, tall.Top, "haut de la zone atteignable");
            AssertEqual(1020d, tall.Top + tall.Height, "zone développée entière dans le travail");

            var created = data.Profiles.First(p => p.Key == smallKey);
            AssertEqual(right.Left, created.Nests[right.Id.ToString()].Left,
                "adaptation enregistrée dans le nouveau profil");
            AssertEqual(tall.Height, created.Nests[tall.Id.ToString()].Height,
                "hauteur cadrée enregistrée dans le nouveau profil");
            var old = data.Profiles.First(p => p.Key == bigKey);
            AssertEqual(2041d, old.Nests[right.Id.ToString()].Left,
                "ancien profil non écrasé");
        }

        private static void RemovedSecondaryMonitorMovesNestsToPrimary()
        {
            var primary = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var secondary = new MonitorDescriptor(1920, 0, 1280, 1024, 1.0, false, 1920, 0, 1280, 974);
            var sourceKey = DisplayProfiles.ComputeKey(new[] { primary, secondary });
            var targetKey = DisplayProfiles.ComputeKey(new[] { primary });

            var nest = new NestData
            {
                Title = "Sur le secondaire", Left = 2020, Top = 50, Width = 400, Height = 300
            };
            var data = new AppData { ActiveProfileKey = sourceKey };
            data.Nests.Add(nest);
            data.Profiles.Add(new DisplayProfile
            {
                Key = sourceKey,
                Description = "Deux écrans",
                Nests = DisplayProfiles.CaptureGeometry(data.Nests)
            });
            DisplayProfiles.RememberMonitors(data, sourceKey, new[] { primary, secondary });

            var result = DisplayProfileSwitcher.Switch(data, new[] { primary });

            Assert(result.Changed && result.Adapted, "adaptation après retrait du secondaire");
            AssertEqual(targetKey, data.ActiveProfileKey, "profil du principal");
            AssertEqual(100d, nest.Left, "distance au bord gauche conservée sur le principal");
            AssertEqual(50d, nest.Top, "distance au bord haut conservée sur le principal");
            Assert(nest.Left >= 0 && nest.Top >= 0, "zone dans le principal");
            Assert(nest.Left + nest.Width <= primary.WorkRight, "bord droit dans le principal");
            Assert(nest.Top + nest.Height <= primary.WorkBottom, "bord bas dans le principal");
        }

        private static void MostSimilarProfileIsUsedAsSource()
        {
            var target = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var far = new MonitorDescriptor(5000, 0, 1000, 800, 1.0, true, 5000, 0, 1000, 740);
            var near = new MonitorDescriptor(0, 0, 2000, 1000, 1.0, true, 0, 0, 2000, 940);
            var farKey = DisplayProfiles.ComputeKey(new[] { far });
            var nearKey = DisplayProfiles.ComputeKey(new[] { near });
            var targetKey = DisplayProfiles.ComputeKey(new[] { target });

            var nest = new NestData { Title = "Commune", Left = 5010, Top = 10, Width = 200, Height = 100 };
            var data = new AppData { ActiveProfileKey = farKey };
            data.Nests.Add(nest);
            data.Profiles.Add(new DisplayProfile
            {
                Key = farKey,
                Description = "Écran éloigné",
                Nests = { [nest.Id.ToString()] = new NestGeometry { Left = 5010, Top = 10, Width = 200, Height = 100 } }
            });
            data.Profiles.Add(new DisplayProfile
            {
                Key = nearKey,
                Description = "Écran proche",
                Nests = { [nest.Id.ToString()] = new NestGeometry { Left = 1900, Top = 20, Width = 80, Height = 100 } }
            });
            DisplayProfiles.RememberMonitors(data, farKey, new[] { far });
            DisplayProfiles.RememberMonitors(data, nearKey, new[] { near });

            DisplayProfileSwitcher.Switch(data, new[] { target });

            AssertEqual(targetKey, data.ActiveProfileKey, "nouveau profil actif");
            // Depuis le profil proche : largeur portée au minimum 120, puis 1920 - 120 - 20 = 1780.
            AssertEqual(1780d, nest.Left, "source la plus similaire utilisée");
            AssertEqual(20d, nest.Top, "distance haute de la source similaire conservée");
            AssertEqual(120d, nest.Width, "largeur minimale conservée");
        }

        private static void FullyOffScreenNestIsMovedInside()
        {
            var monitor = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var nest = new NestData
            {
                Title = "Hors écran", Left = 3000, Top = 500, Width = 300, Height = 200
            };
            var data = new AppData();
            data.Nests.Add(nest);

            var adjusted = DisplaySafety.EnsureVisible(data, new[] { monitor });

            AssertEqual(1, adjusted, "une zone déplacée");
            AssertEqual(1620d, nest.Left, "bord droit ramené dans le travail");
            AssertEqual(500d, nest.Top, "haut conservé");
            AssertEqual(300d, nest.Width, "largeur conservée");
            AssertEqual(200d, nest.Height, "hauteur conservée");
            Assert(nest.Left + nest.Width <= monitor.WorkRight, "zone entière dans le travail");
            Assert(nest.Top + nest.Height <= monitor.WorkBottom, "bas dans le travail");
        }

        private static void PartiallyVisibleNestIsKept()
        {
            var monitor = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var nest = new NestData
            {
                Title = "Partiellement visible", Left = 1800, Top = 100, Width = 300, Height = 200
            };
            var data = new AppData();
            data.Nests.Add(nest);

            var adjusted = DisplaySafety.EnsureVisible(data, new[] { monitor });

            AssertEqual(0, adjusted, "aucun déplacement");
            AssertEqual(1800d, nest.Left, "position laissée en place");
            AssertEqual(100d, nest.Top, "hauteur laissée en place");
        }

        private static void RoundTripAcrossMonitorChangeRestoresExactLayout()
        {
            var (fs, root) = Sandbox();
            var big = new MonitorDescriptor(0, 0, 2560, 1440, 1.0, true, 0, 0, 2560, 1380);
            var small = new MonitorDescriptor(0, 0, 1920, 1080, 1.0, true, 0, 0, 1920, 1020);
            var bigKey = DisplayProfiles.ComputeKey(new[] { big });
            var smallKey = DisplayProfiles.ComputeKey(new[] { small });
            var expected = SampleV2Expectations();

            DisplayProfiles.UseProvider(new FakeDisplayProvider(big));
            fs.WriteAllText(Path.Combine(root, "layout.json"), SampleV2Json());
            var data = Storage.Load();
            AssertEqual(3, data.Version, "migration V3");
            AssertEqual(bigKey, data.ActiveProfileKey, "profil 2560x1440");
            Assert(fs.FileExists(Path.Combine(root, "layout.json.v2.bak")), "copie V2 créée");

            var toSmall = DisplayProfileSwitcher.Switch(data, new[] { small });
            Assert(toSmall.Changed && toSmall.Adapted, "adaptation vers 1920x1080");
            AssertEqual(smallKey, data.ActiveProfileKey, "profil 1920x1080");

            foreach (var item in expected.Where(e => e.RolledUp))
            {
                var nest = data.Nests.First(n => n.Id.ToString() == item.Id);
                double sourceDistance = big.WorkRight - (item.Left + item.Width);
                double targetDistance = small.WorkRight - (nest.Left + nest.Width);
                AssertEqual(sourceDistance, targetDistance, "ancrage droit " + item.Title);
            }
            foreach (var nest in data.Nests)
            {
                Assert(nest.Left >= small.WorkX && nest.Top >= small.WorkY, "dans le travail du portable");
                Assert(nest.Left + nest.Width <= small.WorkRight, "bord droit dans le portable");
                Assert(nest.Top + Math.Max(60, nest.Height) <= small.WorkBottom,
                    "taille développée dans le portable");
            }

            DisplayProfiles.UseProvider(new FakeDisplayProvider(small));
            Storage.SaveNow(data);
            var reloaded = Storage.Load();
            AssertEqual(smallKey, reloaded.ActiveProfileKey, "profil du portable persisté");

            var toBig = DisplayProfileSwitcher.Switch(reloaded, new[] { big });
            Assert(toBig.Changed && toBig.ProfileFound && !toBig.Adapted, "retour au profil exact");
            AssertEqual(bigKey, reloaded.ActiveProfileKey, "profil 2560x1440 restauré");
            AssertExactLayout(reloaded, expected, "après retour");

            Storage.SaveNow(reloaded);
            DisplayProfiles.UseProvider(new FakeDisplayProvider(big));
            var final = Storage.Load();
            AssertExactLayout(final, expected, "après rechargement final");
        }

        private static void AssertExactLayout(AppData data, SampleNest[] expected, string stage)
        {
            AssertEqual(6, data.Nests.Count, "nombre de zones " + stage);
            foreach (var item in expected)
            {
                var nest = data.Nests.FirstOrDefault(n => n.Id.ToString() == item.Id);
                Assert(nest != null, "zone " + item.Title + " " + stage);
                AssertEqual(item.Title, nest!.Title, "titre " + stage);
                AssertEqual(item.Left, nest.Left, "Left " + item.Title + " " + stage);
                AssertEqual(item.Top, nest.Top, "Top " + item.Title + " " + stage);
                AssertEqual(item.Width, nest.Width, "Width " + item.Title + " " + stage);
                AssertEqual(item.Height, nest.Height, "Height " + item.Title + " " + stage);
                AssertEqual(item.RolledUp, nest.RolledUp, "RolledUp " + item.Title + " " + stage);
            }
        }

        private static string SampleV2Json() => @"{
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

        private static SampleNest[] SampleV2Expectations() => new[]
        {
            new SampleNest { Id = "11111111-1111-1111-1111-111111111111", Title = "Bureau", Left = 40, Top = 40, Width = 720, Height = 560, RolledUp = false },
            new SampleNest { Id = "22222222-2222-2222-2222-222222222222", Title = "Notes", Left = 2041, Top = 60, Width = 420, Height = 320, RolledUp = true },
            new SampleNest { Id = "33333333-3333-3333-3333-333333333333", Title = "Photos", Left = 1995, Top = 400, Width = 470, Height = 300, RolledUp = true },
            new SampleNest { Id = "44444444-4444-4444-4444-444444444444", Title = "Projets", Left = 2100, Top = 720, Width = 360, Height = 280, RolledUp = true },
            new SampleNest { Id = "55555555-5555-5555-5555-555555555555", Title = "Musique", Left = 2060, Top = 1020, Width = 405, Height = 240, RolledUp = true },
            new SampleNest { Id = "66666666-6666-6666-6666-666666666666", Title = "Outils", Left = 2080, Top = 1260, Width = 390, Height = 180, RolledUp = true }
        };

        private sealed class SampleNest
        {
            public string Id = "";
            public string Title = "";
            public double Left;
            public double Top;
            public double Width;
            public double Height;
            public bool RolledUp;
        }

        private sealed class FakeDisplayProvider : IDisplayLayoutProvider
        {
            private readonly IReadOnlyList<MonitorDescriptor> _monitors;

            public FakeDisplayProvider(params MonitorDescriptor[] monitors) => _monitors = monitors;

            public IReadOnlyList<MonitorDescriptor> GetMonitors() => _monitors;
        }
    }
}
