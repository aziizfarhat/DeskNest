using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskNest.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        [STAThread]
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
            Run("police : TodoFontSize entier/décimal chargé et appliqué", FontSizeSettingsAcceptNumbersAndApply);
            Run("police : TodoFontSize absent utilise le défaut", FontSizeSettingsAbsentUsesDefault);
            Run("police : valeurs invalides ramenées au défaut ou bornées", FontSizeSettingsInvalidValuesAreSafe);
            Run("S3 : filtre Todo (casse, accents, kana, mots, aucun résultat)", S3TodoFilterMatchingRules);
            Run("S3 : filtre Todo sans effet sur les données et glisser désactivé", S3TodoFilterIsDisplayOnlyAndDisablesDrag);
            Run("S3 : recherche des zones Fichiers inchangée", S3FilesSearchBehaviorUnchanged);
            Run("S3 : aucune barre de recherche créée pour Notes", S3NotesHasNoSearchControl);
            Run("S3 : ShowSearchBar=false masque la barre des To-do", S3ShowSearchBarFalseHidesTheBox);
            Run("V5 : critères et sens de tri", SortCriteriaAndOrder);
            Run("V5 : dossiers avant les fichiers, quel que soit le critère et le sens", SortFoldersFirstInEveryMode);
            Run("V5 : nombres naturels, casse et accents ignorés", SortNaturalCaseAndAccents);
            Run("V5 : départage par nom et ordre stable", SortTiesByNameAndStability);
            Run("V5 : éléments manquants en fin dans leur ordre relatif", SortMissingItemsLast);
            Run("V5 : tri manuel ou inconnu sans effet sur l'ordre d'origine", SortManualIsNoOp);
            Run("V5 : tri persisté, anciens layouts en manuel", SortPersistsAndOldLayoutsLoadManual);
            Run("V5 : profils d'affichage sans métadonnées de tri", SortExcludedFromGeometryProfiles);
            Run("V5 : portail (sous-dossier, retour, changement de contenu)", SortAppliesToPortalLevelsAndChanges);
            Run("V5 : zones indépendantes, glisser désactivé, bouton de tri", SortIsPerNestAndDisablesDrag);
            Run("V5 : menu de zone Todo/Note utilise la palette", NestMenuEntriesUseQuickActions);
            Run("V5 : routage des verbes de ligne de commande", CommandVerbsRouting);
            Run("B1 : le bouton de tri ouvre le menu et n'agit que sur sa zone", SortMenuOpenAndPerNestCommands);
            Run("B2 : liste triée en cache avant le survol et rafraîchie sur changement", () => WithWpfContext(B2ClassicOrderIsCachedBeforeExpand));
            Run("B2 : portail racine/sous-dossier affiché dès la première image", () => WithWpfContext(B2PortalOrderIsCachedBeforeExpand));
            Run("B2 : cache froid : ancien ordre affiché puis retri sans vidage", () => WithWpfContext(B2ColdCacheKeepsLastKnownOrder));
            Run("B2 : des centaines d'éléments restent en cache au repli", () => WithWpfContext(B2ManyItemsStayCachedWhileRolled));
            Run("B2 : portail de centaines d'éléments : page en cache au dépliage", () => WithWpfContext(B2PortalManyItemsUseCachedPage));

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
                "todo.empty", "todo.count", "todo.noMatch", "todo.filterCount", "todo.drag", "tray.newTodo", "tray.newNote",
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

        private static void FontSizeSettingsAcceptNumbersAndApply()
        {
            var (fs, root) = Sandbox();
            var path = Path.Combine(root, "layout.json");
            fs.WriteAllText(path, @"{
  ""Version"": 3,
  ""ZonesCleared"": true,
  ""Nests"": [],
  ""Settings"": { ""Language"": ""en"", ""TodoFontSize"": 12, ""NotesFontSize"": 18.0 }
}");

            var s = Storage.Load().Settings;
            AssertEqual(12.0, s.TodoFontSize, "TodoFontSize entier lu comme double");
            AssertEqual(18.0, s.NotesFontSize, "NotesFontSize décimal lu comme double");

            // Le chemin qui plantait au démarrage : SetValue(TextElement.FontSizeProperty, ...).
            var block = new TextBlock();
            FontSizeHelper.Apply(block, TextElement.FontSizeProperty, s.TodoFontSize, 12);
            AssertEqual(12.0, block.FontSize, "taille appliquée au DependencyProperty");

            // La forme 12.0 est acceptée sans traiter le layout comme corrompu.
            fs.WriteAllText(path, @"{
  ""Version"": 3,
  ""ZonesCleared"": true,
  ""Nests"": [],
  ""Settings"": { ""Language"": ""en"", ""TodoFontSize"": 12.0, ""NotesFontSize"": 18 }
}");
            var again = Storage.Load().Settings;
            AssertEqual(12.0, again.TodoFontSize, "TodoFontSize décimal relu");
            AssertEqual(18.0, again.NotesFontSize, "NotesFontSize entier relu");
        }

        private static void FontSizeSettingsAbsentUsesDefault()
        {
            var (fs, root) = Sandbox();
            fs.WriteAllText(Path.Combine(root, "layout.json"), @"{
  ""Version"": 3,
  ""ZonesCleared"": true,
  ""Nests"": [],
  ""Settings"": { ""Language"": ""en"" }
}");

            var s = Storage.Load().Settings;
            AssertEqual(12.0, s.TodoFontSize, "TodoFontSize par défaut sans la clé");
            AssertEqual(13.0, s.NotesFontSize, "NotesFontSize par défaut sans la clé");

            var block = new TextBlock();
            FontSizeHelper.Apply(block, TextElement.FontSizeProperty, s.TodoFontSize, 12);
            AssertEqual(12.0, block.FontSize, "taille par défaut appliquée");
        }

        private static void FontSizeSettingsInvalidValuesAreSafe()
        {
            var (fs, root) = Sandbox();
            fs.WriteAllText(Path.Combine(root, "layout.json"), @"{
  ""Version"": 3,
  ""ZonesCleared"": true,
  ""Nests"": [],
  ""Settings"": { ""Language"": ""en"", ""TodoFontSize"": "" invalide "", ""NotesFontSize"": -4 }
}");

            var s = Storage.Load().Settings;
            AssertEqual(12.0, s.TodoFontSize, "valeur non numérique remplacée par le défaut");
            AssertEqual(8.0, s.NotesFontSize, "valeur hors bornes ramenée au minimum");

            // Même un objet non convertible ne doit pas empêcher l'application.
            var block = new TextBlock();
            FontSizeHelper.Apply(block, TextElement.FontSizeProperty, new object(), 12);
            AssertEqual(12.0, block.FontSize, "repli appliqué sans exception");
        }

        // ---------- S3 : recherche et filtres ----------
        private static DependencyObject? FindByName(DependencyObject root, string name)
        {
            if (root is FrameworkElement rootElement) rootElement.ApplyTemplate();
            if (root is FrameworkElement named && named.Name == name) return named;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindByName(VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is FrameworkElement rootElement) rootElement.ApplyTemplate();
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }

        private static int CountItems(ItemsControl host)
        {
            if (host.ItemsSource is not System.Collections.IEnumerable source) return host.Items.Count;
            int n = 0;
            foreach (var _ in source) n++;
            return n;
        }

        private static void S3TodoFilterMatchingRules()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            var data = new NestData { Type = NestTypes.Todo };
            data.Tasks.Add(new TodoTask { Text = "École" });
            data.Tasks.Add(new TodoTask { Text = "Acheter du lait" });
            data.Tasks.Add(new TodoTask { Text = "Acheter du pain" });
            data.Tasks.Add(new TodoTask { Text = "カタカナ" });
            var content = new TodoContent(manager, data);

            AssertEqual(4, content.DisplayedTasks.Count, "toutes les tâches au départ");

            content.ApplyFilter("ecole");
            AssertEqual(1, content.DisplayedTasks.Count, "casse et accent ignorés");
            AssertEqual("École", content.DisplayedTasks[0].Text, "seule la tâche correspondante");
            AssertEqual("1/4", content.CountText, "footer en mode filtre");

            content.ApplyFilter("ECOLE");
            AssertEqual(1, content.DisplayedTasks.Count, "majuscules ignorées");

            content.ApplyFilter("かたかな");
            AssertEqual(1, content.DisplayedTasks.Count, "type de kana ignoré");

            content.ApplyFilter("acheter lait");
            AssertEqual(1, content.DisplayedTasks.Count, "plusieurs mots = ET");
            AssertEqual("Acheter du lait", content.DisplayedTasks[0].Text, "tous les mots doivent correspondre");

            content.ApplyFilter("acheter sucre");
            AssertEqual(0, content.DisplayedTasks.Count, "un mot absent exclut la tâche");

            content.ApplyFilter("introuvable");
            AssertEqual(0, content.DisplayedTasks.Count, "aucune correspondance");
            AssertEqual(Loc.T("todo.noMatch"), content.EmptyText, "message « aucun résultat »");
            AssertEqual("0/4", content.CountText, "compte à zéro");

            content.ApplyFilter("");
            AssertEqual(4, content.DisplayedTasks.Count, "filtre vidé : toutes les tâches");
            AssertEqual(Loc.T("todo.empty"), content.EmptyText, "message vide habituel");
        }

        private static void S3TodoFilterIsDisplayOnlyAndDisablesDrag()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            manager.Settings.TodoCheckedToBottom = true;
            var data = new NestData { Type = NestTypes.Todo };
            var first = new TodoTask { Text = "Lait" };
            var done = new TodoTask { Text = "Lait fait", Done = true };
            var last = new TodoTask { Text = "Pain" };
            data.Tasks.Add(first);
            data.Tasks.Add(done);
            data.Tasks.Add(last);
            var content = new TodoContent(manager, data);

            var storedBefore = data.Tasks.ToList();
            Assert(content.ReorderEnabled, "réordonnement autorisé sans filtre");

            content.ApplyFilter("lait");
            Assert(!content.ReorderEnabled, "réordonnement interdit pendant le filtre");
            AssertEqual(2, content.DisplayedTasks.Count, "deux correspondances");
            AssertEqual("Lait", content.DisplayedTasks[0].Text, "ordre préservé");
            AssertEqual("Lait fait", content.DisplayedTasks[1].Text, "tâche cochée à sa place");
            Assert(done.Done, "état Done conservé");
            AssertEqual("2/3", content.CountText, "footer compte les correspondances");

            bool dimmed = FindVisualChildren<Border>(content)
                .Any(b => !b.IsEnabled && b.Opacity < 1.0 && b.Cursor == Cursors.Arrow);
            Assert(dimmed, "poignée désactivée et grisée");

            Assert(storedBefore.SequenceEqual(data.Tasks), "ordre et références stockés inchangés");
            Assert(!first.Done && done.Done && !last.Done, "états Done stockés inchangés");

            int cleared = 0;
            content.FilterCleared += () => cleared++;
            content.AddTask("Nouvelle tâche");
            AssertEqual(1, cleared, "ajout signale la remise à zéro du filtre");
            AssertEqual("", content.FilterText, "texte du filtre vidé");
            Assert(!content.IsFiltering, "plus aucun filtre actif");
            Assert(content.ReorderEnabled, "réordonnement réactivé");
            Assert(data.Tasks.Any(t => t.Text == "Nouvelle tâche"), "tâche ajoutée au stockage");
            Assert(content.DisplayedTasks.Any(t => t.Text == "Nouvelle tâche"), "nouvelle tâche visible");
        }

        private static void S3FilesSearchBehaviorUnchanged()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            manager.Settings.ShowSearchBar = true;
            var data = new NestData { Title = "Files" };
            data.Items.Add(@"C:\Users\example\Desktop\École.txt");
            data.Items.Add(@"C:\Users\example\Desktop\Lait.txt");
            var window = new NestWindow(manager, data);

            Assert(window.HasSearchControl, "la zone Fichiers possède une barre");
            var box = FindByName(window, "SearchBox") as TextBox;
            Assert(box != null, "SearchBox instanciée pour Files");
            var host = FindByName(window, "ItemsHost") as ItemsControl;
            Assert(host != null, "ItemsHost présent");
            AssertEqual(2, CountItems(host!), "tous les éléments au départ");

            box!.Text = "ecole";
            AssertEqual(1, CountItems(host!), "recherche Files : sous-chaîne insensible aux accents");

            box.Text = "ecole lait";
            AssertEqual(0, CountItems(host!), "recherche Files : une seule sous-chaîne, pas de ET sur les mots");

            box.Text = "";
            AssertEqual(2, CountItems(host!), "recherche vidée : éléments restaurés");
            window.Close();
        }

        private static void S3NotesHasNoSearchControl()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            manager.Settings.ShowSearchBar = true;
            var data = new NestData { Title = "Note", Type = NestTypes.Notes };
            var window = new NestWindow(manager, data);

            Assert(!window.HasSearchControl, "aucune barre instanciée pour Notes");
            Assert(FindByName(window, "SearchBox") == null, "aucun SearchBox créé");
            Assert(FindByName(window, "SearchHint") == null, "aucun SearchHint créé");
            Assert(FindByName(window, "SearchClear") == null, "aucun SearchClear créé");
            var host = FindByName(window, "SearchHost") as ContentControl;
            Assert(host != null && host.Visibility == Visibility.Collapsed, "hôte de recherche replié");
            window.Close();
        }

        private static void S3ShowSearchBarFalseHidesTheBox()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            manager.Settings.ShowSearchBar = false;
            var data = new NestData { Title = "Todo", Type = NestTypes.Todo };
            var window = new NestWindow(manager, data);

            Assert(window.HasSearchControl, "le contrôle existe bien pour Todo");
            var host = FindByName(window, "SearchHost") as ContentControl;
            Assert(host != null, "hôte de recherche présent");
            AssertEqual(Visibility.Collapsed, host!.Visibility, "ShowSearchBar=false masque la barre");

            manager.Settings.ShowSearchBar = true;
            window.ApplyExternalChange();
            AssertEqual(Visibility.Visible, host.Visibility, "barre affichée après réactivation");
            window.Close();
        }

        // ---------- V5 : tri (comparateur pur) ----------
        private static ItemMeta Meta(
            string name,
            bool folder = false,
            long size = 0,
            DateTime? created = null,
            DateTime? modified = null,
            bool available = true,
            string? path = null) =>
            new(path ?? @"C:\Data\" + name, name, folder, size,
                created ?? DateTime.MinValue, modified ?? DateTime.MinValue, available);

        private static string Names(IEnumerable<ItemMeta> items) =>
            string.Join(",", items.Select(i => i.Name));

        private static void SortCriteriaAndOrder()
        {
            var d = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var a = Meta("alpha.txt", size: 300, created: d, modified: d.AddDays(3), path: "a");
            var b = Meta("beta.md", size: 100, created: d.AddDays(2), modified: d.AddDays(1), path: "b");
            var c = Meta("gamma.txt", size: 200, created: d.AddDays(1), modified: d.AddDays(2), path: "c");
            var input = new[] { a, b, c };

            AssertEqual("alpha.txt,beta.md,gamma.txt", Names(NestItemSorter.Sort(input, NestSort.Name, false)), "Nom croissant");
            AssertEqual("gamma.txt,beta.md,alpha.txt", Names(NestItemSorter.Sort(input, NestSort.Name, true)), "Nom décroissant");
            AssertEqual("beta.md,alpha.txt,gamma.txt", Names(NestItemSorter.Sort(input, NestSort.Type, false)), "Type croissant (extension puis nom)");
            AssertEqual("alpha.txt,gamma.txt,beta.md", Names(NestItemSorter.Sort(input, NestSort.Type, true)), "Type décroissant (extension puis nom)");
            AssertEqual("beta.md,gamma.txt,alpha.txt", Names(NestItemSorter.Sort(input, NestSort.Size, false)), "Taille croissante");
            AssertEqual("alpha.txt,gamma.txt,beta.md", Names(NestItemSorter.Sort(input, NestSort.Size, true)), "Taille décroissante");
            AssertEqual("alpha.txt,gamma.txt,beta.md", Names(NestItemSorter.Sort(input, NestSort.DateCreated, false)), "Date de création croissante");
            AssertEqual("beta.md,gamma.txt,alpha.txt", Names(NestItemSorter.Sort(input, NestSort.DateCreated, true)), "Date de création décroissante");
            AssertEqual("beta.md,gamma.txt,alpha.txt", Names(NestItemSorter.Sort(input, NestSort.DateModified, false)), "Date de modification croissante");
            AssertEqual("alpha.txt,gamma.txt,beta.md", Names(NestItemSorter.Sort(input, NestSort.DateModified, true)), "Date de modification décroissante");
        }

        private static void SortFoldersFirstInEveryMode()
        {
            var d = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var zDir = Meta("zeta", folder: true, created: d.AddDays(1), modified: d.AddDays(1), path: "dz");
            var aDir = Meta("alpha", folder: true, created: d, modified: d.AddDays(2), path: "da");
            var zFile = Meta("z.txt", size: 1, path: "fz");
            var aFile = Meta("a.txt", size: 999, path: "fa");
            var input = new[] { zFile, zDir, aFile, aDir };

            foreach (var mode in new[] { NestSort.Name, NestSort.DateCreated })
            {
                AssertEqual("alpha,zeta", string.Join(",", NestItemSorter.Sort(input, mode, false).Take(2).Select(i => i.Name)), $"dossiers en tête ({mode} croissant)");
                AssertEqual("zeta,alpha", string.Join(",", NestItemSorter.Sort(input, mode, true).Take(2).Select(i => i.Name)), $"dossiers en tête ({mode} décroissant)");
            }
            AssertEqual("zeta,alpha", string.Join(",", NestItemSorter.Sort(input, NestSort.DateModified, false).Take(2).Select(i => i.Name)), "dossiers en tête (DateModified croissant)");
            AssertEqual("alpha,zeta", string.Join(",", NestItemSorter.Sort(input, NestSort.DateModified, true).Take(2).Select(i => i.Name)), "dossiers en tête (DateModified décroissant)");

            // Type et Taille n'ont pas de sens pour un dossier : il garde un tri par nom
            // croissant, même quand les fichiers sont triés en sens inverse.
            foreach (var mode in new[] { NestSort.Type, NestSort.Size })
            {
                AssertEqual("alpha,zeta", string.Join(",", NestItemSorter.Sort(input, mode, false).Take(2).Select(i => i.Name)), $"dossiers par nom ({mode} croissant)");
                AssertEqual("alpha,zeta", string.Join(",", NestItemSorter.Sort(input, mode, true).Take(2).Select(i => i.Name)), $"dossiers par nom ({mode} décroissant)");
            }

            Assert(NestItemSorter.Sort(input, NestSort.Size, false).Skip(2).All(i => !i.IsFolder), "fichiers toujours après les dossiers");
            Assert(NestItemSorter.Sort(input, NestSort.Name, true).Skip(2).All(i => !i.IsFolder), "fichiers toujours après les dossiers en sens inverse");
        }

        private static void SortNaturalCaseAndAccents()
        {
            Assert(NestItemSorter.NaturalCompare("item2.txt", "item10.txt") < 0, "tri naturel : 2 avant 10");
            Assert(NestItemSorter.NaturalCompare("Item10.TXT", "item2.txt") > 0, "casse ignorée, tri naturel conservé");
            AssertEqual(0, NestItemSorter.NaturalCompare("école.txt", "ecole.txt"), "accents ignorés");
            AssertEqual(0, NestItemSorter.NaturalCompare("ÉCOLE.TXT", "ecole.txt"), "casse et accents ignorés");

            var input = new[]
            {
                Meta("item10.txt", path: "i10"),
                Meta("école.txt", path: "e1"),
                Meta("item2.txt", path: "i2"),
                Meta("ecole.TXT", path: "e2")
            };
            AssertEqual("école.txt,ecole.TXT,item2.txt,item10.txt",
                Names(NestItemSorter.Sort(input, NestSort.Name, false)),
                "tri naturel insensible à la casse et aux accents");
        }

        private static void SortTiesByNameAndStability()
        {
            var b = Meta("beta.txt", size: 100, path: "b");
            var a = Meta("alpha.txt", size: 100, path: "a");
            foreach (var desc in new[] { false, true })
                AssertEqual("alpha.txt,beta.txt",
                    Names(NestItemSorter.Sort(new[] { b, a }, NestSort.Size, desc)),
                    "égalité sur le critère : départage par nom, même en sens inverse");

            var first = Meta("same.txt", size: 5, path: "p1");
            var second = Meta("same.txt", size: 5, path: "p2");
            AssertEqual("p1,p2", string.Join(",", NestItemSorter.Sort(new[] { first, second }, NestSort.Size, false).Select(i => i.Path)),
                "égalité complète : ordre d'entrée conservé");
            AssertEqual("p2,p1", string.Join(",", NestItemSorter.Sort(new[] { second, first }, NestSort.Size, true).Select(i => i.Path)),
                "égalité complète en sens inverse : ordre d'entrée conservé");
        }

        private static void SortMissingItemsLast()
        {
            var missingB = Meta("missing-b.txt", available: false, path: "mb");
            var ok = Meta("ok.txt", path: "ok");
            var missingA = Meta("missing-a.txt", available: false, path: "ma");
            var input = new[] { missingB, ok, missingA };

            foreach (var desc in new[] { false, true })
                AssertEqual("ok.txt,missing-b.txt,missing-a.txt",
                    Names(NestItemSorter.Sort(input, NestSort.Name, desc)),
                    "éléments manquants repoussés en fin dans leur ordre relatif");

            var absent = Path.Combine(Path.GetTempPath(), "desknest-v5-" + Guid.NewGuid().ToString("N") + ".txt");
            var read = ItemMeta.Read(absent);
            Assert(!read.Available, "chemin absent marqué indisponible");
            AssertEqual(Path.GetFileName(absent), read.Name, "nom conservé pour un élément absent");
        }

        private static void SortManualIsNoOp()
        {
            var source = new List<ItemMeta>
            {
                Meta("z.txt", path: "z"), Meta("a.txt", path: "a"), Meta("m.txt", path: "m")
            };
            foreach (var mode in new[] { NestSort.Manual, "Inconnu", "", null })
                foreach (var desc in new[] { false, true })
                    AssertEqual("z.txt,a.txt,m.txt",
                        Names(NestItemSorter.Sort(source, mode!, desc)),
                        "mode inactif : ordre manuscrit intact");

            Assert(!NestSort.IsActive(NestSort.Manual) && !NestSort.IsActive("Inconnu"), "seuls les modes connus et non manuels sont actifs");
            AssertEqual(NestSort.Manual, NestSort.Normalize("Inconnu"), "valeur inconnue ramenée à Manuel");
            AssertEqual("z.txt,a.txt,m.txt", Names(source), "la source n'est jamais réécrite par un tri");
        }

        private static void SortPersistsAndOldLayoutsLoadManual()
        {
            var (_, _) = Sandbox();
            var data = Sample("Tri");
            data.Nests[0].SortBy = NestSort.DateCreated;
            data.Nests[0].SortDescending = true;
            Storage.SaveNow(data);
            var loaded = Storage.Load();
            AssertEqual(NestSort.DateCreated, loaded.Nests[0].SortBy, "critère de tri persisté");
            Assert(loaded.Nests[0].SortDescending, "sens de tri persisté");

            var (fs, root) = Sandbox();
            fs.WriteAllText(Path.Combine(root, "layout.json"), SampleV3Json());
            var old = Storage.Load();
            AssertEqual(2, old.Nests.Count, "ancien layout conservé");
            Assert(old.Nests.All(n => n.SortBy == NestSort.Manual), "ancien layout : tri manuel par défaut");
            Assert(old.Nests.All(n => !n.SortDescending), "ancien layout : sens croissant par défaut");
        }

        private static void SortExcludedFromGeometryProfiles()
        {
            var (_, _) = Sandbox();
            var data = Sample("Profil");
            var nest = data.Nests[0];
            nest.SortBy = NestSort.Type;
            nest.SortDescending = true;

            var props = typeof(NestGeometry).GetProperties();
            AssertEqual(4, props.Length, "un profil ne contient que Left/Top/Width/Height");
            Assert(props.All(p => p.Name != "SortBy" && p.Name != "SortDescending"), "aucun champ de tri dans un profil");

            var before = DisplayProfiles.GeometryOf(nest);
            var captured = DisplayProfiles.CaptureGeometry(data.Nests);
            nest.SortBy = NestSort.Size;
            nest.SortDescending = false;
            var after = DisplayProfiles.CaptureGeometry(data.Nests);

            AssertEqual(4, after[nest.Id.ToString()].GetType().GetProperties().Length, "capture sans métadonnées de tri");
            AssertEqual(before.Left, after[nest.Id.ToString()].Left, "changer le tri n'altère pas la géométrie capturée");
            DisplayProfiles.ApplyGeometry(nest, before);
            AssertEqual(NestSort.Size, nest.SortBy, "appliquer un profil ne touche pas au tri");
            Assert(!nest.SortDescending, "appliquer un profil ne touche pas au sens du tri");
        }

        private static void SortAppliesToPortalLevelsAndChanges()
        {
            var rootEntries = new[]
            {
                Meta("10.txt", path: @"C:\portal\10.txt"),
                Meta("2.txt", path: @"C:\portal\2.txt")
            };
            var subEntries = new[]
            {
                Meta("b.txt", path: @"C:\portal\Sub\b.txt"),
                Meta("a.txt", path: @"C:\portal\Sub\a.txt")
            };

            AssertEqual("2.txt,10.txt", Names(NestItemSorter.Sort(rootEntries, NestSort.Name, false)), "racine du portail triée");
            AssertEqual("a.txt,b.txt", Names(NestItemSorter.Sort(subEntries, NestSort.Name, false)), "sous-dossier trié avec le même critère");
            AssertEqual("10.txt,2.txt", Names(NestItemSorter.Sort(rootEntries, NestSort.Name, true)), "retour à la racine : tri réappliqué");

            var changed = rootEntries.Concat(new[] { Meta("1.txt", path: @"C:\portal\1.txt") }).ToList();
            AssertEqual("1.txt,2.txt,10.txt", Names(NestItemSorter.Sort(changed, NestSort.Name, false)), "ajout : tri réappliqué");
            changed.RemoveAll(i => i.Path == @"C:\portal\1.txt");
            AssertEqual("2.txt,10.txt", Names(NestItemSorter.Sort(changed, NestSort.Name, false)), "retrait : tri réappliqué");
        }

        // ---------- V5 : tri dans la fenêtre ----------
        private static void SortIsPerNestAndDisablesDrag()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            var a = new NestData { Title = "A", Type = NestTypes.Files, SortBy = NestSort.Name };
            var b = new NestData { Title = "B", Type = NestTypes.Files, SortBy = NestSort.Manual };
            var wa = new NestWindow(manager, a);
            var wb = new NestWindow(manager, b);

            Assert(!wa.ReorderEnabled, "tri actif : réordonnancement manuel désactivé");
            Assert(wb.ReorderEnabled, "tri manuel : réordonnancement autorisé");

            b.SortBy = NestSort.Type;
            wb.ApplyExternalChange();
            Assert(!wb.ReorderEnabled && !wa.ReorderEnabled, "chaque zone porte son propre état");

            a.SortBy = NestSort.Manual;
            wa.ApplyExternalChange();
            Assert(wa.ReorderEnabled && !wb.ReorderEnabled, "retour au manuscrit dans une zone seulement");

            // ItemVM porte le même état que la fenêtre.
            var withItem = new NestData { Title = "C", Type = NestTypes.Files };
            withItem.Items.Add(@"C:\Data\fichier.txt");
            var wc = new NestWindow(manager, withItem);
            var host = FindByName(wc, "ItemsHost") as ItemsControl;
            var vm = (host?.ItemsSource as IEnumerable<ItemVM>)?.FirstOrDefault();
            Assert(vm != null, "élément affiché pour tester le glisser");
            Assert(vm!.ReorderEnabled, "manuel : glisser autorisé sur l'élément");
            withItem.SortBy = NestSort.Size;
            wc.ApplyExternalChange();
            Assert(!vm.ReorderEnabled, "tri actif : glisser désactivé sur l'élément");
            AssertEqual(Loc.T("sort.reorderHint"), vm.ItemToolTip, "infobulle « glisser désactivé »");
            withItem.SortBy = NestSort.Manual;
            wc.ApplyExternalChange();
            Assert(vm.ReorderEnabled, "retour au manuscrit : glisser réautorisé");

            // Le bouton de tri est dans la barre de titre : il suit exactement le
            // bouton menu (masqué avec la barre, disponible quand la zone est repliée).
            var sortA = FindByName(wa, "SortButton") as Button;
            var barA = FindByName(wa, "TitleBar") as Border;
            Assert(sortA != null && barA != null, "bouton de tri et barre de titre présents");
            Assert(ReferenceEquals(sortA, FindByName(barA!, "SortButton")), "bouton de tri placé dans la barre de titre");
            AssertEqual(Visibility.Visible, barA!.Visibility, "barre de titre visible");
            AssertEqual(Visibility.Visible, sortA!.Visibility, "tri visible pour une zone Fichiers");

            a.TitleVisible = false;
            wa.ApplyExternalChange();
            AssertEqual(Visibility.Collapsed, barA.Visibility, "titre masqué : barre entière masquée");
            AssertEqual(Visibility.Visible, sortA.Visibility, "bouton de tri masqué avec le menu par la barre");

            a.TitleVisible = true;
            a.RolledUp = true;
            wa.ApplyExternalChange();
            AssertEqual(Visibility.Visible, barA.Visibility, "zone repliée : barre conservée");
            AssertEqual(Visibility.Visible, sortA.Visibility, "tri disponible dans la barre repliée comme le menu");

            var note = new NestData { Title = "Note", Type = NestTypes.Notes };
            var wn = new NestWindow(manager, note);
            AssertEqual(Visibility.Collapsed, (FindByName(wn, "SortButton") as Button)!.Visibility, "pas de bouton de tri pour Notes");

            wa.Close();
            wb.Close();
            wc.Close();
            wn.Close();
        }

        // ---------- V5 : menu et ligne de commande ----------
        private static MenuItem? MenuEntry(ContextMenu menu, string header) =>
            menu.Items.OfType<MenuItem>().FirstOrDefault(i => string.Equals(i.Header as string, header, StringComparison.Ordinal));

        private static void ClickEntry(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        private static void NestMenuEntriesUseQuickActions()
        {
            Loc.SetLanguage("en");
            var (_, _) = Sandbox();
            var manager = new NestManager();
            manager.Settings.Enabled = false; // pas de fenêtre affichée pendant le test
            var data = new NestData { Title = "Menu", Type = NestTypes.Files };
            var window = new NestWindow(manager, data);
            var root = FindByName(window, "Root") as Border;
            Assert(root?.ContextMenu != null, "menu de zone présent");

            var todo = MenuEntry(root!.ContextMenu!, Loc.T("tray.newTodo"));
            var note = MenuEntry(root.ContextMenu!, Loc.T("tray.newNote"));
            Assert(todo != null, "entrée « New to-do list » dans le menu de la zone");
            Assert(note != null, "entrée « New note » dans le menu de la zone");

            int before = manager.Nests.Count;
            ClickEntry(todo!);
            AssertEqual(before + 1, manager.Nests.Count, "Todo créée depuis le menu");
            AssertEqual(NestTypes.Todo, manager.Nests.Last().Type, "la Todo suit le chemin de la palette");

            manager.Settings.Enabled = false;
            ClickEntry(note!);
            AssertEqual(before + 2, manager.Nests.Count, "Note créée depuis le menu");
            AssertEqual(NestTypes.Notes, manager.Nests.Last().Type, "la Note suit le chemin de la palette");

            window.Close();
            foreach (var w in manager.Windows.ToList()) w.Close();
        }

        private static void CommandVerbsRouting()
        {
            Assert(DeskNestCommands.IsKnown(DeskNestCommands.NewZone), "--new-zone reconnu");
            Assert(DeskNestCommands.IsKnown(DeskNestCommands.NewPortal), "--new-portal reconnu");
            Assert(DeskNestCommands.IsKnown(DeskNestCommands.NewTodo), "--new-todo reconnu");
            Assert(DeskNestCommands.IsKnown(DeskNestCommands.NewNote), "--new-note reconnu");
            Assert(!DeskNestCommands.IsKnown("--inconnu"), "verbe inconnu refusé");
            Assert(!DeskNestCommands.IsKnown(null), "verbe nul refusé");

            AssertEqual(QuickActionKind.NewZone, DeskNestCommands.QuickAction(DeskNestCommands.NewZone), "correspondance NewZone");
            AssertEqual(QuickActionKind.NewPortal, DeskNestCommands.QuickAction(DeskNestCommands.NewPortal), "correspondance NewPortal");
            AssertEqual(QuickActionKind.NewTodo, DeskNestCommands.QuickAction(DeskNestCommands.NewTodo), "correspondance NewTodo");
            AssertEqual(QuickActionKind.NewNote, DeskNestCommands.QuickAction(DeskNestCommands.NewNote), "correspondance NewNote");
            Assert(DeskNestCommands.QuickAction("--inconnu") == null, "aucune action pour un verbe inconnu");
            Assert(!CommandServer.SendAsync("--inconnu", 50).GetAwaiter().GetResult(), "verbe inconnu rejeté sans ouvrir le pipe");

            var (_, _) = Sandbox();
            var manager = new NestManager();
            foreach (var verb in new[] { DeskNestCommands.NewTodo, DeskNestCommands.NewNote })
            {
                manager.Settings.Enabled = false;
                var kind = DeskNestCommands.QuickAction(verb)!.Value;
                manager.ExecuteQuickAction(kind);
                AssertEqual(kind == QuickActionKind.NewTodo ? NestTypes.Todo : NestTypes.Notes,
                    manager.Nests.Last().Type, verb + " distribué à la palette");
            }
            foreach (var w in manager.Windows.ToList()) w.Close();
        }

        private static void SortMenuOpenAndPerNestCommands()
        {
            var (_, _) = Sandbox();
            var manager = new NestManager();
            var a = new NestData { Title = "A", Type = NestTypes.Files };
            var b = new NestData { Title = "B", Type = NestTypes.Files };
            var wa = new NestWindow(manager, a);
            var wb = new NestWindow(manager, b);

            var field = typeof(NestWindow).GetField("_sortMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert(field != null, "menu de tri présent");
            var menu = (ContextMenu)field!.GetValue(wa)!;
            var menuB = (ContextMenu)field.GetValue(wb)!;
            Assert(!ReferenceEquals(menu, menuB), "chaque zone a son propre menu");

            // Le clic sur le bouton doit ouvrir le menu, exactement comme le bouton menu.
            var button = FindByName(wa, "SortButton") as Button;
            Assert(button != null, "bouton de tri présent");
            Assert(!menu.IsOpen, "menu fermé au départ");
            button!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(menu.IsOpen, "le clic sur le bouton ouvre le menu de tri");
            menu.IsOpen = false;

            MenuItem Criterion(ContextMenu m, string mode) =>
                m.Items.OfType<MenuItem>().First(i => string.Equals(i.Tag as string, mode, StringComparison.Ordinal));
            MenuItem Order(ContextMenu m, string key) =>
                m.Items.OfType<MenuItem>().First(i => string.Equals(i.Header as string, Loc.T(key), StringComparison.Ordinal));

            // L'ordre n'a pas de sens en tri manuel.
            menuB.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert(!Order(menuB, "sort.ascending").IsEnabled && !Order(menuB, "sort.descending").IsEnabled,
                "ordre désactivé en tri manuel");

            Criterion(menu, NestSort.Name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            AssertEqual(NestSort.Name, a.SortBy, "critère choisi dans la zone A");
            AssertEqual(NestSort.Manual, b.SortBy, "la zone B n'est pas touchée");
            Assert(!wa.ReorderEnabled && wb.ReorderEnabled, "glisser désactivé seulement dans A");

            Order(menu, "sort.descending").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(a.SortDescending, "ordre décroissant appliqué à A");
            Assert(!b.SortDescending, "ordre de B inchangé");

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert(Criterion(menu, NestSort.Name).IsChecked, "coche sur le critère courant");
            Assert(Order(menu, "sort.descending").IsChecked, "coche sur l'ordre courant");
            Assert(!Criterion(menu, NestSort.Manual).IsChecked, "aucune coche sur Manuel");
            Assert(Order(menu, "sort.ascending").IsEnabled && Order(menu, "sort.descending").IsEnabled,
                "ordre activé dès qu'un tri est choisi");

            var saved = new AppData();
            saved.Nests.Add(a);
            saved.Nests.Add(b);
            Storage.SaveNow(saved);
            var loaded = Storage.Load();
            AssertEqual(NestSort.Name, loaded.Nests[0].SortBy, "critère persisté");
            Assert(loaded.Nests[0].SortDescending, "ordre persisté");
            AssertEqual(NestSort.Manual, loaded.Nests[1].SortBy, "zone B toujours manuelle après rechargement");

            wa.Close();
            wb.Close();
        }

        // ---------- B2 : ordre d'affichage disponible avant le dépliage ----------
        private static object? ReadField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        private static void InvokePrivate(object target, string name, params object?[] args) =>
            target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

        // Les rafraîchissements asynchrones reprennent sur le contexte de synchronisation :
        // sans lui, ils reprendraient sur un threadpool et toucheraient la UI depuis le mauvais thread.
        private sealed class DispatcherSyncContext : SynchronizationContext
        {
            private readonly Dispatcher _dispatcher;
            public DispatcherSyncContext(Dispatcher dispatcher) => _dispatcher = dispatcher;

            public override void Post(SendOrPostCallback d, object? state) =>
                _dispatcher.BeginInvoke(new Action(() => d(state)), DispatcherPriority.Normal);

            public override void Send(SendOrPostCallback d, object? state) =>
                _dispatcher.Invoke(() => d(state));
        }

        private static void WithWpfContext(Action test)
        {
            var d = Dispatcher.CurrentDispatcher;
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSyncContext(d));
            try { test(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        // Les rafraîchissements asynchrones finissent par publier leur résultat sur le
        // dispatcher. On pompe jusqu'à ce que l'état attendu soit observé (avec un délai
        // de sécurité) plutôt que de compter sur un nombre d'itérations arbitraire.
        private static void PumpUntil(Func<bool> condition, string label, int timeoutMs = 5000)
        {
            var d = Dispatcher.CurrentDispatcher;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
                d.Invoke(() => { }, DispatcherPriority.Background);
            Assert(condition(), "délai dépassé en attendant : " + label);
        }

        private static List<string> CachedPaths(NestWindow window)
        {
            var display = ReadField(window, "_display")!;
            return (List<string>)display.GetType().GetField("Paths")!.GetValue(display)!;
        }

        private static List<string> CachedNames(NestWindow window) =>
            CachedPaths(window).Select(p => Path.GetFileName(p)!).ToList();

        private static List<string> DisplayedNames(NestWindow window)
        {
            var host = (ItemsControl)FindByName(window, "ItemsHost")!;
            return (host.ItemsSource as IEnumerable<ItemVM>)?.Where(v => !v.IsUp)
                       .Select(v => Path.GetFileName(v.FullPath)).ToList()
                   ?? new List<string>();
        }

        private static string Joined(IEnumerable<string> names) => string.Join(",", names);

        private static void B2ClassicOrderIsCachedBeforeExpand()
        {
            var (_, _) = Sandbox();
            var dir = Path.Combine(Path.GetTempPath(), "desknest-b2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var paths = new[] { "c.txt", "a.txt", "b.txt" }.Select(n => Path.Combine(dir, n)).ToList();
                foreach (var p in paths) File.WriteAllText(p, "x");

                var data = new NestData { Title = "Tri", Type = NestTypes.Files, RolledUp = true, SortBy = NestSort.Name };
                data.Items.AddRange(paths);
                var window = new NestWindow(new NestManager(), data);
                PumpUntil(() => Joined(CachedNames(window)) == "a.txt,b.txt,c.txt", "tri initial");

                AssertEqual("a.txt,b.txt,c.txt", Joined(CachedNames(window)), "tri calculé avant le survol");
                AssertEqual(0, ((List<ItemVM>)ReadField(window, "_vms")!).Count, "repli : aucun ItemVM en mémoire");
                var host = (ItemsControl)FindByName(window, "ItemsHost")!;
                Assert(host.ItemsSource == null, "repli : source d'éléments libérée");

                // Première image du dépliage : la liste est déjà construite depuis le cache.
                InvokePrivate(window, "RestoreFilesContent");
                AssertEqual("a.txt,b.txt,c.txt", Joined(DisplayedNames(window)), "liste visible dès la première image");
                AssertEqual(3, CountItems(host), "trois éléments affichés sans attendre le tri");

                data.RolledUp = false;
                window.ApplyExternalChange();
                AssertEqual("a.txt,b.txt,c.txt", Joined(DisplayedNames(window)), "dépliage complet conserve l'ordre");

                // Ajout d'élément : le cache et l'affichage suivent.
                var added = Path.Combine(dir, "d.txt");
                File.WriteAllText(added, "x");
                data.Items.Add(added);
                window.ReloadItems();
                PumpUntil(() => Joined(CachedNames(window)) == "a.txt,b.txt,c.txt,d.txt", "ajout trié");
                AssertEqual("a.txt,b.txt,c.txt,d.txt", Joined(CachedNames(window)), "ajout répercuté dans le cache");
                AssertEqual("a.txt,b.txt,c.txt,d.txt", Joined(DisplayedNames(window)), "ajout répercuté à l'affichage");

                // Sens du tri : nouveau cache sans repasser par un dépliage.
                data.SortDescending = true;
                window.ApplyExternalChange();
                PumpUntil(() => Joined(CachedNames(window)) == "d.txt,c.txt,b.txt,a.txt", "tri inversé");
                AssertEqual("d.txt,c.txt,b.txt,a.txt", Joined(CachedNames(window)), "sens inversé en cache");
                AssertEqual("d.txt,c.txt,b.txt,a.txt", Joined(DisplayedNames(window)), "sens inversé à l'affichage");

                // Retour au tri manuel : l'ordre manuscrit est immédiat.
                data.SortBy = NestSort.Manual;
                data.SortDescending = false;
                window.ApplyExternalChange();
                PumpUntil(() => Joined(CachedNames(window)) == "c.txt,a.txt,b.txt,d.txt", "retour au manuscrit");
                AssertEqual("c.txt,a.txt,b.txt,d.txt", Joined(CachedNames(window)), "ordre manuscrit restauré");

                window.Close();
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void B2PortalOrderIsCachedBeforeExpand()
        {
            var (_, _) = Sandbox();
            var root = Path.Combine(Path.GetTempPath(), "desknest-b2p-" + Guid.NewGuid().ToString("N"));
            var sub = Path.Combine(root, "Sub");
            Directory.CreateDirectory(sub);
            try
            {
                foreach (var n in new[] { "e.txt", "c.txt", "a.txt", "d.txt", "b.txt" })
                    File.WriteAllText(Path.Combine(root, n), "x");
                foreach (var n in new[] { "z.txt", "y.txt", "x.txt" })
                    File.WriteAllText(Path.Combine(sub, n), "x");

                var data = new NestData
                {
                    Title = "Portail", Type = NestTypes.Files, PortalPath = root,
                    RolledUp = true, SortBy = NestSort.Name
                };
                var window = new NestWindow(new NestManager(), data);
                PumpUntil(() => Joined(CachedNames(window)) == "Sub,a.txt,b.txt,c.txt,d.txt,e.txt", "racine du portail");

                AssertEqual("Sub,a.txt,b.txt,c.txt,d.txt,e.txt", Joined(CachedNames(window)), "racine du portail triée en cache");
                AssertEqual(0, ((List<ItemVM>)ReadField(window, "_vms")!).Count, "portail replié : aucun ItemVM");

                InvokePrivate(window, "RestoreFilesContent");
                AssertEqual("Sub,a.txt,b.txt,c.txt,d.txt,e.txt", Joined(DisplayedNames(window)), "racine affichée dès la première image");

                data.RolledUp = false;
                window.ApplyExternalChange();

                // Sous-dossier : le cache suit le dossier courant.
                InvokePrivate(window, "NavigateTo", sub, true);
                PumpUntil(() => Joined(CachedNames(window)) == "x.txt,y.txt,z.txt", "sous-dossier du portail");
                AssertEqual("x.txt,y.txt,z.txt", Joined(CachedNames(window)), "sous-dossier trié en cache");
                AssertEqual("x.txt,y.txt,z.txt", Joined(DisplayedNames(window)), "sous-dossier affiché");

                // Retour à la racine : le tri est réappliqué sans passer par le dépliage.
                InvokePrivate(window, "NavigateTo", root, true);
                PumpUntil(() => Joined(CachedNames(window)) == "Sub,a.txt,b.txt,c.txt,d.txt,e.txt", "retour racine portail");
                AssertEqual("Sub,a.txt,b.txt,c.txt,d.txt,e.txt", Joined(CachedNames(window)), "retour à la racine trié");

                window.Close();
            }
            finally { Directory.Delete(root, true); }
        }

        private static void B2ColdCacheKeepsLastKnownOrder()
        {
            var (_, _) = Sandbox();
            var dir = Path.Combine(Path.GetTempPath(), "desknest-b2f-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var paths = new[] { "c.txt", "a.txt", "b.txt" }.Select(n => Path.Combine(dir, n)).ToList();
                foreach (var p in paths) File.WriteAllText(p, "x");

                var data = new NestData
                {
                    Title = "Froid", Type = NestTypes.Files,
                    SortBy = NestSort.Name, SortDescending = true
                };
                data.Items.AddRange(paths);
                var window = new NestWindow(new NestManager(), data);

                // Le tri asynchrone n'a pas encore abouti : l'ordre connu est déjà affiché.
                AssertEqual("c.txt,a.txt,b.txt", Joined(DisplayedNames(window)), "ordre connu affiché immédiatement");
                var host = (ItemsControl)FindByName(window, "ItemsHost")!;
                AssertEqual(3, CountItems(host), "aucune case vide pendant le tri");

                PumpUntil(() => Joined(DisplayedNames(window)) == "c.txt,b.txt,a.txt", "tri à froid");
                AssertEqual("c.txt,b.txt,a.txt", Joined(DisplayedNames(window)), "retri sans vider ni cacher la liste");
                AssertEqual(3, CountItems(host), "même nombre d'éléments après le tri");
                window.Close();
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void B2ManyItemsStayCachedWhileRolled()
        {
            var (_, _) = Sandbox();
            var dir = Path.Combine(Path.GetTempPath(), "desknest-b2n-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var paths = new List<string>();
                for (int i = 0; i < 300; i++)
                {
                    var p = Path.Combine(dir, $"Fichier{i:000}.txt");
                    File.WriteAllText(p, "x");
                    paths.Add(p);
                }
                paths.Reverse(); // ordre manuscrit volontairement inverse de l'ordre naturel

                var data = new NestData { Title = "Gros", Type = NestTypes.Files, RolledUp = true, SortBy = NestSort.Manual };
                data.Items.AddRange(paths);
                var window = new NestWindow(new NestManager(), data);
                PumpUntil(() => CachedPaths(window).Count == 300, "cache 300 fichiers");

                AssertEqual(300, CachedPaths(window).Count, "300 chemins conservés en cache");
                AssertEqual("Fichier299.txt", CachedNames(window).First(), "ordre manuscrit conservé");
                AssertEqual(0, ((List<ItemVM>)ReadField(window, "_vms")!).Count, "aucun ItemVM en mémoire au repli");

                data.RolledUp = false;
                window.ApplyExternalChange();
                AssertEqual(300, CountItems((ItemsControl)FindByName(window, "ItemsHost")!), "300 éléments affichés au dépliage");
                window.Close();
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void B2PortalManyItemsUseCachedPage()
        {
            var (_, _) = Sandbox();
            var dir = Path.Combine(Path.GetTempPath(), "desknest-b2m-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                for (int i = 0; i < 300; i++) File.WriteAllText(Path.Combine(dir, $"Fichier{i:000}.txt"), "x");

                var data = new NestData
                {
                    Title = "Gros portail", Type = NestTypes.Files, PortalPath = dir,
                    RolledUp = true, SortBy = NestSort.Name
                };
                var window = new NestWindow(new NestManager(), data);
                PumpUntil(() => CachedPaths(window).Count == 300, "cache 300 fichiers portail");

                AssertEqual(300, CachedPaths(window).Count, "300 chemins triés en cache");
                AssertEqual("Fichier000.txt", CachedNames(window).First(), "premier du cache");

                data.RolledUp = false;
                window.ApplyExternalChange();
                var host = (ItemsControl)FindByName(window, "ItemsHost")!;
                AssertEqual(200, CountItems(host), "première page affichée au dépliage");

                InvokePrivate(window, "ChangePage", 1);
                PumpUntil(() => CountItems(host) == 100, "page 2 du portail");
                AssertEqual(100, CountItems(host), "deuxième page affichée");
                AssertEqual("Fichier200.txt", DisplayedNames(window).First(), "page 2 servie depuis le cache trié");
                window.Close();
            }
            finally { Directory.Delete(dir, true); }
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
