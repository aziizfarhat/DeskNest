using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DeskNest
{
    public sealed class NestManager : IDisposable
    {
        private AppData _data = new();
        private readonly List<NestWindow> _windows = new();
        private readonly DispatcherTimer _saveTimer;
        private GlobalHotkey? _hotkey;
        private MouseHook? _mouseHook;
        private SettingsWindow? _settings;
        private bool _warnedProtected, _warnedFailure, _warnedHotkey, _hotkeyActive;
        private readonly DispatcherTimer _desktopTimer;
        private readonly DispatcherTimer _desktopWake;
        private DesktopWatcher? _desktopWatcher;
        private int _desktopWakeQueued;

        public bool Hidden { get; private set; }
        public AppSettings Settings => _data.Settings;
        public IList<NestData> Nests => _data.Nests;

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Met à jour toutes les références d'une zone quand un élément change
        // d'emplacement (stockage <-> bureau), pour qu'une zone ne pointe
        // jamais vers un chemin qui n'existe plus.
        private static bool RebaseReferences(IEnumerable<NestData> nests, string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || Same(from, to)) return false;

            bool changed = false;
            foreach (var f in nests)
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], from))
                    {
                        f.Items[i] = to;
                        changed = true;
                    }

            return changed;
        }

        // Répare les dispositions abîmées par une ancienne désinstallation :
        // la zone pointe encore vers %USERPROFILE%\DeskNest\Items alors que
        // le fichier a été remis sur le bureau. On retrouve son homologue par
        // son nom, puis la ré-adoption le reprend normalement en charge.
        private bool RepairMissingStoreReferences()
        {
            bool changed = false;
            var storeRoot = DesktopStore.Dir.TrimEnd('\\') + "\\";

            foreach (var f in _data.Nests)
            {
                if (!string.IsNullOrEmpty(f.PortalPath)) continue;

                for (int i = 0; i < f.Items.Count; i++)
                {
                    var p = f.Items[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (File.Exists(p) || Directory.Exists(p)) continue;

                    var full = p.TrimEnd('\\');
                    if (!full.StartsWith(storeRoot, StringComparison.OrdinalIgnoreCase)) continue;

                    // Un enregistrement de déplacement existe : c'est le
                    // démarrage normal qui remettra le fichier dans le
                    // stockage. Ne pas court-circuiter ce mécanisme.
                    if (_data.Moved.Any(m => Same(m.StorePath, p))) continue;

                    var name = Path.GetFileName(full);
                    if (string.IsNullOrEmpty(name)) continue;

                    var found = FindDesktopEntry(name);
                    if (found == null) continue;

                    f.Items[i] = found;
                    changed = true;
                }
            }

            return changed;
        }

        private static string? FindDesktopEntry(string name)
        {
            foreach (var dir in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        public NestManager()
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Storage.Save(_data); };
            // Le suivi est désormais événementiel (DesktopWatcher) : le timer n'est qu'un filet de sécurité.
            _desktopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _desktopTimer.Tick += (_, _) => DesktopTick();
            // Regroupe les rafales d'événements (Win+D masque/restaure des dizaines de fenêtres à la suite).
            _desktopWake = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _desktopWake.Tick += (_, _) => { _desktopWake.Stop(); DesktopTick(); };
        }

        public void Start()
        {
            _data = Storage.Load();
            Loc.SetLanguage(_data.Settings.Language);
            if (Storage.Issue == Storage.LoadIssue.RestoredBackup)
                Warn(Loc.F("msg.layoutRestored", Storage.RestoredFrom ?? ""));
            else if (Storage.Issue == Storage.LoadIssue.Lost)
                Warn(Loc.T("msg.layoutLost"));

            if (_data.Nests.Count == 0)
            {
                var first = NewNestData(Loc.T("zone.apps"));
                first.Left = 80; first.Top = 80;
                _data.Nests.Add(first);
            }

            // Reprend les fichiers gérés avant d'afficher les Nests.
            RestoreManagedItemsOnStartup();

            foreach (var f in _data.Nests.ToList())
                OpenWindow(f);

            try
            {
                _hotkey = new GlobalHotkey();
                _hotkey.Pressed += ToggleHidden;
            }
            catch (Exception ex) { Diag.Log("raccourci : " + ex.Message); }

            ApplySettings();
            try
            {
                _desktopWatcher = new DesktopWatcher(QueueDesktopTick);
                _desktopWatcher.Start();
            }
            catch (Exception ex) { Diag.Log("suivi bureau : " + ex.Message); }
            _desktopTimer.Start();
        }

        private NestData NewNestData(string title) => new NestData
        {
            Title = title,
            Color = Settings.DefaultColor,
            Opacity = Settings.DefaultOpacity,
            IconSize = Settings.DefaultIconSize
        };

        private bool ShouldShow(NestWindow w) => Settings.Enabled && !Hidden && w.Data.Enabled;

        private void OpenWindow(NestData d)
        {
            var w = new NestWindow(this, d);
            _windows.Add(w);
            // Filet de sécurité : si la fenêtre est réellement fermée (fermeture
            // système, plantage de l'affichage...), on ne garde pas de référence morte.
            w.Closed += (_, _) => _windows.Remove(w);
            if (ShouldShow(w)) w.Show();
        }

        // Vérifie qu'un chemin appartient à un Nest classique.
        // Les Folder Portals sont volontairement exclus.
        private bool IsReferencedByNormalNest(string path)
        {
            return _data.Nests.Any(f =>
                string.IsNullOrEmpty(f.PortalPath) &&
                f.Items.Any(p => Same(p, path)));
        }

        // Vérifie que l'élément est réellement affiché : DeskNest doit être actif
        // et la zone qui le référence doit être visible.
        private bool IsReferencedByEnabledNormalNest(string path)
        {
            return Settings.Enabled && _data.Nests.Any(f =>
                f.Enabled &&
                string.IsNullOrEmpty(f.PortalPath) &&
                f.Items.Any(p => Same(p, path)));
        }

        // Remet sur le bureau les éléments qui ne sont plus affichés par une zone
        // active (DeskNest désactivé ou zone masquée) et met à jour les références.
        // Sans cela, les fichiers resteraient invisibles alors qu'aucune zone ne les montre.
        private bool RestoreItemsNoLongerVisible()
        {
            bool changed = false;

            foreach (var m in _data.Moved.ToList())
            {
                if (!IsReferencedByNormalNest(m.StorePath))
                    continue;
                if (IsReferencedByEnabledNormalNest(m.StorePath))
                    continue;

                // Le fichier a pu être supprimé depuis : on garde la trace,
                // mais on ne pointe pas les zones vers un chemin inexistant.
                if (!File.Exists(m.StorePath) && !Directory.Exists(m.StorePath) &&
                    !File.Exists(m.OriginalPath) && !Directory.Exists(m.OriginalPath))
                    continue;

                var restored = MoveBack(m);
                if (restored == null) continue;

                // Les zones qui référençaient l'élément pointent maintenant
                // vers son emplacement restauré, pour rester utilisables.
                foreach (var f in _data.Nests)
                    for (int i = 0; i < f.Items.Count; i++)
                        if (Same(f.Items[i], m.StorePath)) f.Items[i] = restored;

                _data.Moved.Remove(m);
                changed = true;
            }

            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (!IsReferencedByNormalNest(p))
                    continue;
                if (IsReferencedByEnabledNormalNest(p))
                    continue;

                if (DesktopHelper.SetHidden(p, false) || (!File.Exists(p) && !Directory.Exists(p)))
                {
                    _data.HiddenByApp.Remove(p);
                    changed = true;
                }
            }

            return changed;
        }

        // Au démarrage : replace les éléments dans le dossier de stockage
        // et réapplique le masquage des éléments gérés par DeskNest.
        private void RestoreManagedItemsOnStartup()
        {
            bool changed = RepairMissingStoreReferences();
            if (RestoreItemsNoLongerVisible()) changed = true;

            foreach (var m in _data.Moved.ToList())
            {
                if (!IsReferencedByEnabledNormalNest(m.StorePath))
                    continue;

                // Le fichier est déjà dans le stockage : par sécurité, on
                // s'assure que les zones pointent bien vers cet emplacement.
                if (File.Exists(m.StorePath) || Directory.Exists(m.StorePath))
                {
                    if (RebaseReferences(_data.Nests, m.OriginalPath, m.StorePath))
                        changed = true;
                    continue;
                }

                // Le fichier n'existe plus à son emplacement d'origine.
                if (!File.Exists(m.OriginalPath) && !Directory.Exists(m.OriginalPath))
                    continue;

                bool ok = DesktopStore.TryMove(m.OriginalPath, m.StorePath, out bool denied);

                if (!ok && denied && DesktopHelper.IsOnPublicDesktop(m.OriginalPath))
                {

                    var name = Path.GetFileName(m.OriginalPath.TrimEnd('\\'));

                    var r = MessageBox.Show(
                        Loc.F("msg.publicDesktop", name),
                        "DeskNest",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No,
                        Loc.MsgOptions);

                    if (r == MessageBoxResult.Yes)
                        ok = DesktopStore.TryMoveElevated(m.OriginalPath, m.StorePath);
                }

                if (!ok)
                {
                    Diag.Log($"impossible de reprendre l'élément : {m.OriginalPath}");
                    continue;
                }

                // Le fichier a quitté le bureau : les zones suivent.
                if (RebaseReferences(_data.Nests, m.OriginalPath, m.StorePath))
                    changed = true;
            }

            // Réapplique les attributs caché + système aux éléments concernés.
            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (!IsReferencedByEnabledNormalNest(p))
                    continue;

                if (File.Exists(p) || Directory.Exists(p))
                    DesktopHelper.SetHidden(p, true);
            }

            // Réinstallation, ou zone réactivée : les fichiers remis sur le
            // bureau doivent être repris en charge par leur zone.
            if (AdoptDesktopItemsInEnabledZones())
                changed = true;

            if (changed) RequestSave();
        }

        // À la fermeture : restaure les éléments sur le bureau,
        // mais conserve les associations pour le prochain démarrage.
        private void RestoreManagedItemsOnShutdown()
        {
            foreach (var m in _data.Moved.ToList())
            {
                if (!IsReferencedByNormalNest(m.StorePath))
                    continue;

                var restored = MoveBack(m);

                if (restored != null)
                    m.OriginalPath = restored;
            }

            // Retire le masquage sans supprimer les références sauvegardées.
            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (!IsReferencedByNormalNest(p))
                    continue;

                if (File.Exists(p) || Directory.Exists(p))
                    DesktopHelper.SetHidden(p, false);
            }
        }

        public void RequestSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private static void Warn(string message) =>
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                MessageBox.Show(message, "DeskNest", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, Loc.MsgOptions)));

        // ---------- Paramètres ----------
        public void ShowSettings(NestData? zone = null)
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow(this);
                _settings.Closed += (_, _) => _settings = null;
                _settings.Show();
            }
            else
            {
                if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
                _settings.Activate();
            }
            if (zone != null) _settings.SelectZone(zone);
        }

        public void SettingsChanged()
        {
            ApplySettings();
            RequestSave();
        }

        public void ApplySettings()
        {
            SyncHotkey();
            SyncMouseHook();
            foreach (var w in _windows) w.ApplyExternalChange();
            ApplyVisibility();
        }

        // Le raccourci n'est réenregistré que lorsqu'il change réellement d'état.
        private void SyncHotkey()
        {
            if (_hotkey == null) return;
            try
            {
                if (Settings.HotkeyEnabled)
                {
                    if (_hotkeyActive) return;
                    _hotkeyActive = _hotkey.Register();
                    if (!_hotkeyActive)
                    {
                        Diag.Log("raccourci Ctrl+Alt+F indisponible");
                        if (!_warnedHotkey)
                        {
                            _warnedHotkey = true;
                            Warn(Loc.T("msg.hotkeyFailed"));
                        }
                    }
                }
                else if (_hotkeyActive)
                {
                    _hotkey.Unregister();
                    _hotkeyActive = false;
                }
            }
            catch (Exception ex) { Diag.Log("raccourci : " + ex.Message); }
        }

        // Le hook souris global ne doit être installé que si le double-clic sur le bureau est utile.
        private void SyncMouseHook()
        {
            bool want = Settings.Enabled && Settings.DesktopDoubleClickEnabled;
            if (want && _mouseHook == null)
            {
                try
                {
                    var hook = new MouseHook();
                    hook.DoubleClick += (x, y) =>
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => OnDesktopDoubleClick(x, y)), DispatcherPriority.Background);
                    _mouseHook = hook;
                }
                catch (Exception ex) { Diag.Log("hook souris : " + ex.Message); }
            }
            else if (!want && _mouseHook != null)
            {
                _mouseHook.Dispose();
                _mouseHook = null;
            }
        }

        public void ZoneChanged(NestData d)
        {
            // Masquer une zone ne doit pas faire disparaître ses fichiers du bureau.
            bool restored = !d.Enabled && RestoreItemsNoLongerVisible();
            // Réactiver une zone doit au contraire reprendre les éléments
            // qu'elle référence et qui se trouvent encore sur le bureau.
            bool adopted = d.Enabled && AdoptDesktopItemsInEnabledZones(d);

            _windows.FirstOrDefault(w => w.Data == d)?.ApplyExternalChange();
            if (restored || adopted)
                foreach (var w in _windows) w.ReloadItems();

            ApplyVisibility();
            RequestSave();
        }

        public void SetEnabled(bool on)
        {
            bool was = Settings.Enabled;
            Settings.Enabled = on;
            bool adopted = false;
            if (on)
            {
                Hidden = false;
                adopted = AdoptDesktopItemsInEnabledZones();
            }
            else if (was) RestoreAllHidden(); // aucune zone affichée : les icônes reviennent

            ApplySettings();
            if (adopted)
                foreach (var w in _windows) w.ReloadItems();
            RequestSave();
        }

        public void ApplyVisibility()
        {
            foreach (var w in _windows)
            {
                if (ShouldShow(w)) { if (!w.IsVisible) w.Show(); }
                else if (w.IsVisible) w.Hide();
            }
        }

        // ---------- Instantanés ----------
        public string? CreateSnapshot()
        {
            Storage.Save(_data);
            return Storage.CreateSnapshot();
        }

        public bool RestoreSnapshot(string path)
        {
            var d = Storage.LoadFrom(path);
            if (d == null) return false;
            CreateSnapshot(); // filet de sécurité : l'état actuel est conservé

            foreach (var p in _data.HiddenByApp.ToList()) DesktopHelper.SetHidden(p, false);
            var oldMoved = _data.Moved.ToList();

            foreach (var w in _windows.ToList()) w.CloseForReal();
            _windows.Clear();
            _data = d;
            _data.Settings ??= new AppSettings();
            // On garde la trace des éléments rangés par l'état précédent pour pouvoir les remettre un jour.
            foreach (var m in oldMoved)
                if (!_data.Moved.Any(x => Same(x.StorePath, m.StorePath))) _data.Moved.Add(m);

            foreach (var f in _data.Nests.ToList()) OpenWindow(f);
            foreach (var p in _data.HiddenByApp.ToList())
                if (File.Exists(p) || Directory.Exists(p)) DesktopHelper.SetHidden(p, true);
            ApplySettings();
            RequestSave();
            return true;
        }

        // ---------- Zones ----------
        public void CreateNest() => AddNest(NewNestData(""));

        public void CreatePortal()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("dlg.chooseFolder") };
            if (dlg.ShowDialog() != true) return;
            var name = System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\'));
            var d = NewNestData(string.IsNullOrEmpty(name) ? dlg.FolderName : name);
            d.PortalPath = dlg.FolderName;
            AddNest(d);
        }

        private void AddNest(NestData d)
        {
            int n = _data.Nests.Count;
            if (string.IsNullOrEmpty(d.Title)) d.Title = Loc.F("zone.default", n + 1);
            d.Left = 80 + 30 * (n % 10);
            d.Top = 80 + 30 * (n % 10);
            _data.Nests.Add(d);
            Hidden = false;
            Settings.Enabled = true; // créer une zone la rend forcément visible
            OpenWindow(d);
            ApplyVisibility();
            RequestSave();
        }

        public void Remove(NestWindow w)
        {
            var items = w.Data.Items.ToList();
            _data.Nests.Remove(w.Data);
            _windows.Remove(w);
            w.CloseForReal();
            foreach (var p in items) RestoreOnDesktop(p);
            RequestSave();
        }

        public void RemoveNest(NestData d)
        {
            var w = _windows.FirstOrDefault(x => x.Data == d);
            if (w != null) Remove(w);
        }

        // Déplace plusieurs éléments d'une zone vers une autre (à partir d'un Folder Portal : copie de références).
        public void MoveItems(Guid sourceId, List<string> paths, NestWindow target, int index)
        {
            if (sourceId == target.Data.Id) return;
            var src = _windows.FirstOrDefault(w => w.Data.Id == sourceId);
            if (src == null) return;
            bool fromPortal = !string.IsNullOrEmpty(src.Data.PortalPath);
            int at = index;
            foreach (var p in paths)
            {
                if (!fromPortal && !src.RemoveItem(p, false)) continue;
                target.AddItem(p, at);
                if (at >= 0) at++;
            }
        }

        // ---------- Icônes du bureau ----------
        // Appelé quand un élément est déposé dans une zone. Retourne le chemin à enregistrer dans la zone.
        // Mode 0 : l'élément est déplacé dans le dossier géré (il disparaît réellement du bureau).
        // Mode 1 (ou repli) : l'élément reste en place et reçoit les attributs « caché + système ».
        public string TakeFromDesktop(string path)
        {
            if (!Settings.HideDesktopIcons || !DesktopHelper.IsOnDesktop(path)) return path;
            var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));

            if (Settings.DesktopMode == 0)
            {
                bool isDir = Directory.Exists(path);
                var dst = DesktopStore.UniquePath(DesktopStore.Dir, name, isDir);
                bool ok = DesktopStore.TryMove(path, dst, out bool denied);
                if (!ok && denied && DesktopHelper.IsOnPublicDesktop(path))
                {
                    var r = MessageBox.Show(Loc.F("msg.publicDesktop", name), "DeskNest", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
                    if (r == MessageBoxResult.Yes) ok = DesktopStore.TryMoveElevated(path, dst);
                }
                if (ok)
                {
                    _data.Moved.Add(new MovedItem { StorePath = dst, OriginalPath = path });
                    RequestSave();
                    return dst;
                }
            }

            if (HideByAttributes(path)) return path;
            if (!_warnedFailure)
            {
                _warnedFailure = true;
                Warn(Loc.F("msg.cannotRemove", name));
            }
            return path;
        }

        private bool HideByAttributes(string path)
        {
            if (_data.HiddenByApp.Any(p => Same(p, path))) return true;
            if (DesktopHelper.IsHiddenOrSystem(path)) return true; // déjà caché par l'utilisateur : on n'y touche pas
            if (!DesktopHelper.SetHidden(path, true)) return false;

            _data.HiddenByApp.Add(path);
            RequestSave();
            if (!_warnedProtected && DesktopHelper.ShowsProtectedFiles())
            {
                _warnedProtected = true;
                Warn(Loc.T("msg.protectedWarn"));
            }
            return true;
        }

        // Ré-adopte les éléments référencés par une zone visible mais encore
        // présents sur le bureau. C'est le cas après une réinstallation : la
        // désinstallation les a remis sur le bureau et les zones pointent vers
        // leur nouvel emplacement. Sans cela, le même fichier apparaît à la
        // fois sur le bureau et dans la zone.
        private bool AdoptDesktopItemsInEnabledZones(NestData? only = null)
        {
            if (!Settings.Enabled || !Settings.HideDesktopIcons) return false;

            bool changed = false;
            foreach (var f in _data.Nests.ToList())
            {
                if (only != null && !ReferenceEquals(f, only)) continue;
                if (!f.Enabled || !string.IsNullOrEmpty(f.PortalPath)) continue;

                for (int i = 0; i < f.Items.Count; i++)
                {
                    var p = f.Items[i];
                    if (_data.Moved.Any(m => Same(m.StorePath, p))) continue;
                    if (_data.HiddenByApp.Any(h => Same(h, p))) continue;
                    if (!File.Exists(p) && !Directory.Exists(p)) continue;
                    if (!DesktopHelper.IsOnDesktop(p)) continue;

                    var adopted = TakeFromDesktop(p);

                    // Un même fichier peut être référencé par plusieurs zones :
                    // toutes doivent pointer vers le nouvel emplacement.
                    bool moved = RebaseReferences(_data.Nests, p, adopted);

                    // Ne compter un changement que si l'élément est réellement
                    // géré à présent (déplacé dans le stockage ou masqué).
                    if (moved ||
                        _data.Moved.Any(m => Same(m.StorePath, adopted)) ||
                        _data.HiddenByApp.Any(h => Same(h, adopted)))
                        changed = true;
                }
            }

            return changed;
        }

        // Remet un élément rangé à son emplacement d'origine. Retourne le nouveau chemin, ou null en cas d'échec.
        private string? MoveBack(MovedItem m)
        {
            if (!File.Exists(m.StorePath) && !Directory.Exists(m.StorePath)) return m.OriginalPath; // plus rien à remettre
            var destDir = System.IO.Path.GetDirectoryName(m.OriginalPath.TrimEnd('\\'));
            if (string.IsNullOrEmpty(destDir)) destDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            bool isDir = Directory.Exists(m.StorePath);
            var dst = DesktopStore.UniquePath(destDir, System.IO.Path.GetFileName(m.OriginalPath.TrimEnd('\\')), isDir);
            bool ok = DesktopStore.TryMove(m.StorePath, dst, out bool denied);
            if (!ok && denied && DesktopHelper.IsOnPublicDesktop(m.OriginalPath))
            {
                var r = MessageBox.Show(Loc.T("msg.publicDesktopBack"), "DeskNest", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
                if (r == MessageBoxResult.Yes) ok = DesktopStore.TryMoveElevated(m.StorePath, dst);
            }
            return ok ? dst : null;
        }

        // Appelé quand un élément n'est plus dans aucune zone : il retourne sur le bureau.
        public void RestoreOnDesktop(string path)
        {
            if (_data.Nests.Any(f => f.Items.Any(p => Same(p, path)))) return; // encore référencé ailleurs

            int mi = _data.Moved.FindIndex(m => Same(m.StorePath, path));
            if (mi >= 0)
            {
                if (MoveBack(_data.Moved[mi]) != null) _data.Moved.RemoveAt(mi);
                else Warn(Loc.F("msg.cannotRestore", path));
                RequestSave();
                return;
            }

            int i = _data.HiddenByApp.FindIndex(p => Same(p, path));
            if (i < 0) return;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _data.HiddenByApp.RemoveAt(i); // plus rien à restaurer
            }
            else if (DesktopHelper.SetHidden(path, false)) _data.HiddenByApp.RemoveAt(i);
            else Warn(Loc.F("msg.cannotRestore", path));
            RequestSave();
        }

        // Restauration sans interface, utilisée par le désinstalleur : remet les
        // éléments rangés sur le bureau et met à jour les références des zones.
        public static int RestoreAllForUninstall()
        {
            var data = Storage.Load();
            int n = 0;

            foreach (var m in data.Moved.ToList())
            {
                // Plus rien dans le stockage : le fichier a déjà été restauré
                // (fermeture normale de l'application). Les zones doivent
                // pointer vers cet emplacement avant qu'on oublie la trace.
                if (!File.Exists(m.StorePath) && !Directory.Exists(m.StorePath))
                {
                    if (!string.IsNullOrEmpty(m.OriginalPath) &&
                        (File.Exists(m.OriginalPath) || Directory.Exists(m.OriginalPath)))
                        RebaseReferences(data.Nests, m.StorePath, m.OriginalPath);

                    data.Moved.Remove(m);
                    continue;
                }
                if (string.IsNullOrEmpty(m.OriginalPath))
                    continue;

                var destDir = System.IO.Path.GetDirectoryName(m.OriginalPath.TrimEnd('\\'));
                if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
                    destDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(destDir)) continue;

                bool isDir = Directory.Exists(m.StorePath);
                var name = System.IO.Path.GetFileName(m.OriginalPath.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name))
                    name = System.IO.Path.GetFileName(m.StorePath.TrimEnd('\\'));

                var dst = DesktopStore.UniquePath(destDir, name, isDir);
                if (!DesktopStore.TryMove(m.StorePath, dst, out _))
                {
                    Diag.Log($"désinstallation : impossible de restaurer {m.StorePath}");
                    continue;
                }

                foreach (var f in data.Nests)
                    for (int i = 0; i < f.Items.Count; i++)
                        if (Same(f.Items[i], m.StorePath)) f.Items[i] = dst;

                data.Moved.Remove(m);
                n++;
            }

            foreach (var p in data.HiddenByApp.ToList())
            {
                if (!File.Exists(p) && !Directory.Exists(p))
                {
                    data.HiddenByApp.Remove(p);
                    continue;
                }
                if (DesktopHelper.SetHidden(p, false))
                    data.HiddenByApp.Remove(p);
            }

            Storage.Save(data);

            // Le dossier de stockage ne doit pas rester vide après désinstallation.
            try
            {
                var store = DesktopStore.Dir;
                if (Directory.Exists(store) && !Directory.EnumerateFileSystemEntries(store).Any())
                    Directory.Delete(store);
            }
            catch { }

            return n;
        }

        // Remet toutes les icônes sur le bureau (elles restent aussi dans leurs zones).
        public int RestoreAllHidden()
        {
            int n = 0;
            // On ne perd pas la trace des éléments qui n'ont pas pu être restaurés.
            var stillHidden = new List<string>();
            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (DesktopHelper.SetHidden(p, false)) n++;
                else if (File.Exists(p) || Directory.Exists(p)) stillHidden.Add(p);
                // chemin disparu : on abandonne la trace (il n'y a plus rien à restaurer)
            }
            _data.HiddenByApp = stillHidden;

            foreach (var m in _data.Moved.ToList())
            {
                var restored = MoveBack(m);
                if (restored == null) continue;
                foreach (var f in _data.Nests)
                    for (int i = 0; i < f.Items.Count; i++)
                        if (Same(f.Items[i], m.StorePath)) f.Items[i] = restored; // les zones pointent vers le bureau
                _data.Moved.Remove(m);
                n++;
            }
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
            return n;
        }

        // ---------- Zones et « Afficher le bureau » (Win+D, bouton en bas à droite) ----------
        // Les zones doivent rester visibles : on les restaure si le système les réduit ou les masque,
        // et on les remonte au-dessus du bureau quand celui-ci est affiché par-dessus les fenêtres.
        private void QueueDesktopTick()
        {
            // Un callback WinEvent peut arriver sur le thread qui a installé le hook : on repasse
            // par le dispatcher et on fusionne les demandes en attente.
            if (Interlocked.Exchange(ref _desktopWakeQueued, 1) == 1) return;
            var app = Application.Current;
            if (app == null) { Interlocked.Exchange(ref _desktopWakeQueued, 0); return; }

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _desktopWakeQueued, 0);
                if (!Settings.Enabled || Hidden) return;
                if (!_desktopWake.IsEnabled) _desktopWake.Start();
            }), DispatcherPriority.Background);
        }

        private void DesktopTick()
        {
            if (!Settings.Enabled || Hidden) return;
            var host = DesktopHelper.FindDesktopHost();
            bool desktopForeground = DesktopHelper.IsDesktopForeground(host);
            foreach (var w in _windows)
                if (ShouldShow(w) && w.IsVisible) w.EnsureDesktopVisibility(host, desktopForeground);
        }

        // ---------- Glisser un élément hors d'une zone (vers le bureau, l'Explorateur...) ----------
        // Les éléments masqués par attribut sont démasqués pendant le glisser (sinon ils arriveraient cachés).
        public List<string> UnhideForDrag(List<string> paths)
        {
            var list = new List<string>();
            foreach (var p in paths)
                if (_data.HiddenByApp.Any(h => Same(h, p)) && DesktopHelper.SetHidden(p, false)) list.Add(p);
            return list;
        }

        public void RehideAfterDrag(List<string> paths)
        {
            foreach (var p in paths)
                if (File.Exists(p) || Directory.Exists(p)) DesktopHelper.SetHidden(p, true);
        }

        // L'élément a été déplacé hors de DeskNest par l'utilisateur : on ne garde plus de trace.
        public void ForgetStored(string path)
        {
            _data.Moved.RemoveAll(m => Same(m.StorePath, path));
            _data.HiddenByApp.RemoveAll(p => Same(p, path));
            RequestSave();
        }

        // ---------- Dépôt dans un Folder Portal : les fichiers sont réellement mis dans le dossier ----------
        private void OnPathMoved(string oldPath, string newPath)
        {
            foreach (var f in _data.Nests)
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], oldPath)) f.Items[i] = newPath;
            ForgetStored(oldPath);
        }

        private static void ShellMove(string src, string dst, bool isDir)
        {
            var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
            var cancel = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;
            if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(src, dst, ui, cancel);
            else Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(src, dst, ui, cancel);
        }

        private static void ShellCopy(string src, string dst, bool isDir)
        {
            var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
            var cancel = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;
            if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(src, dst, ui, cancel);
            else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(src, dst, ui, cancel);
        }

        // Déplace (ou copie) les éléments dans le dossier. Utilise les opérations de l'Explorateur
        // (progression, confirmations, autorisation administrateur si nécessaire).
        public void DropIntoFolder(List<string> paths, string folder, bool copy, Guid? sourceNest)
        {
            if (!Directory.Exists(folder)) { Warn(Loc.T("msg.folderNotFound")); return; }
            var src = sourceNest.HasValue ? _windows.FirstOrDefault(w => w.Data.Id == sourceNest.Value) : null;
            bool srcIsReferenceNest = src != null && string.IsNullOrEmpty(src.Data.PortalPath);
            var dest = folder.TrimEnd('\\');

            foreach (var path in paths)
            {
                bool isDir = Directory.Exists(path);
                if (!isDir && !File.Exists(path)) continue;
                var clean = path.TrimEnd('\\');
                var name = System.IO.Path.GetFileName(clean);
                var parent = System.IO.Path.GetDirectoryName(clean);
                if (parent != null && Same(parent.TrimEnd('\\'), dest)) continue; // déjà dans ce dossier
                if (isDir && (Same(clean, dest) || (dest + "\\").StartsWith(clean + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    Warn(Loc.T("msg.folderIntoItself"));
                    continue;
                }

                var dst = System.IO.Path.Combine(folder, name);
                bool wasHidden = _data.HiddenByApp.Any(h => Same(h, path));
                if (wasHidden) DesktopHelper.SetHidden(path, false);
                try
                {
                    if (copy) ShellCopy(path, dst, isDir); else ShellMove(path, dst, isDir);
                }
                catch (Exception ex)
                {
                    Warn(Loc.F("msg.opFailed", name, ex.Message));
                }

                bool sourceGone = !File.Exists(path) && !Directory.Exists(path);
                bool destExists = File.Exists(dst) || Directory.Exists(dst);
                if (!copy && sourceGone && destExists)
                {
                    OnPathMoved(path, dst);
                    if (srcIsReferenceNest) src!.RemoveItem(dst, false); // l'élément quitte la zone d'origine
                }
                else if (wasHidden && !sourceGone) DesktopHelper.SetHidden(path, true); // rien n'a bougé : on remet le masquage
            }
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
        }

        // ---------- Renommer / supprimer de vrais fichiers ----------
        private static void MoveAny(string a, string b)
        {
            if (Directory.Exists(a)) Directory.Move(a, b); else File.Move(a, b);
        }

        // Noms réservés par Windows (y compris avec une extension, ex. « CON.txt »).
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // Un nom saisi par l'utilisateur doit être accepté par Windows sans
        // être normalisé (point/espaces finaux, nom de périphérique réservé...).
        public static bool IsValidItemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (name.EndsWith(' ') || name.EndsWith('.')) return false;

            var stem = Path.GetFileNameWithoutExtension(name).TrimEnd(' ');
            return !ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
        }

        public bool RenameItem(string path, string newName)
        {
            var clean = path.TrimEnd('\\');
            var dir = System.IO.Path.GetDirectoryName(clean);
            if (string.IsNullOrEmpty(dir)) return false;
            if (!IsValidItemName(newName))
            {
                Warn(Loc.T("msg.renameInvalid"));
                return false;
            }
            var dst = System.IO.Path.Combine(dir, newName);
            bool caseOnly = Same(dst, clean);
            if (!caseOnly && (File.Exists(dst) || Directory.Exists(dst)))
            {
                Warn(Loc.T("msg.renameExists"));
                return false;
            }
            try
            {
                if (caseOnly)
                {
                    var tmp = clean + "." + Guid.NewGuid().ToString("N") + ".tmp"; // changement de casse seulement
                    MoveAny(clean, tmp);
                    MoveAny(tmp, dst);
                }
                else MoveAny(clean, dst);
            }
            catch (Exception ex)
            {
                Warn(Loc.F("msg.renameFailed", ex.Message));
                return false;
            }

            RenameTracked(clean, dst);
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
            return true;
        }

        private void RenameTracked(string oldPath, string newPath)
        {
            var dir = System.IO.Path.GetDirectoryName(oldPath) ?? "";
            var oldName = System.IO.Path.GetFileName(oldPath);
            var newName = System.IO.Path.GetFileName(newPath);
            var oldClean = oldPath.TrimEnd('\\');
            string Rebase(string path)
            {
                var clean = path.TrimEnd('\\');
                if (Same(clean, oldClean)) return newPath;
                if (clean.StartsWith(oldClean + "\\", StringComparison.OrdinalIgnoreCase))
                    return newPath + clean.Substring(oldClean.Length); // sous-dossier du dossier renommé
                return path;
            }

            foreach (var f in _data.Nests)
            {
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], oldPath)) f.Items[i] = newPath;

                // Le dossier renommé (ou un de ses sous-dossiers) peut être le Folder Portal affiché.
                if (!string.IsNullOrEmpty(f.PortalPath)) f.PortalPath = Rebase(f.PortalPath);

                if (f.PortalOrder.Count == 0) continue;
                var order = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in f.PortalOrder)
                {
                    var folder = Rebase(kv.Key);
                    if (Same(folder.TrimEnd('\\'), dir.TrimEnd('\\'))) // ordre personnalisé du dossier parent
                        for (int i = 0; i < kv.Value.Count; i++)
                            if (Same(kv.Value[i], oldName)) kv.Value[i] = newName;
                    if (order.TryGetValue(folder, out var existing)) existing.AddRange(kv.Value);
                    else order[folder] = kv.Value;
                }
                f.PortalOrder = order; // les clés suivent le dossier renommé
            }
            foreach (var m in _data.Moved)
                if (Same(m.StorePath, oldPath))
                {
                    m.StorePath = newPath;
                    var od = System.IO.Path.GetDirectoryName(m.OriginalPath.TrimEnd('\\')) ?? "";
                    m.OriginalPath = System.IO.Path.Combine(od, newName);
                }
            for (int i = 0; i < _data.HiddenByApp.Count; i++)
                if (Same(_data.HiddenByApp[i], oldPath)) _data.HiddenByApp[i] = newPath;
        }

        // Envoie les éléments à la Corbeille (la confirmation est demandée par la zone).
        public void DeleteItems(List<string> paths)
        {
            foreach (var p in paths)
            {
                bool isDir = Directory.Exists(p);
                if (!isDir && !File.Exists(p)) continue;
                var name = System.IO.Path.GetFileName(p.TrimEnd('\\'));
                try
                {
                    var ui = Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;
                    var bin = Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin;
                    if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, ui, bin);
                    else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, ui, bin);
                }
                catch (Exception ex)
                {
                    Warn(Loc.F("msg.deleteFailed", name, ex.Message));
                    continue;
                }
                if (!File.Exists(p) && !Directory.Exists(p))
                {
                    foreach (var w in _windows) w.RemoveItem(p, false);
                    ForgetStored(p);
                }
            }
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
        }

        // ---------- Quick-Hide ----------
        public void ToggleHidden()
        {
            if (!Settings.Enabled) return;
            Hidden = !Hidden;
            ApplyVisibility();
        }

        private void OnDesktopDoubleClick(int x, int y)
        {
            if (!Settings.Enabled || !Settings.DesktopDoubleClickEnabled) return;
            if (!DesktopHelper.IsDesktopBackground(x, y)) return;
            Task.Run(() =>
            {
                if (DesktopHelper.IsIconAt(x, y)) return; // double-clic sur une icône : on laisse Windows l'ouvrir
                Application.Current?.Dispatcher.BeginInvoke(new Action(ToggleHidden));
            });
        }

        public void Dispose()
        {
            _saveTimer.Stop();
            _desktopWake.Stop();
            _desktopTimer.Stop();
            _desktopWatcher?.Dispose();
            _desktopWatcher = null;

            // Remet les fichiers sur le Bureau sans perdre leurs références.
            RestoreManagedItemsOnShutdown();

            // Sauvegarde les associations pour le prochain démarrage.
            Storage.Save(_data);

            _mouseHook?.Dispose();
            _hotkey?.Dispose();
            _settings?.Close();

            foreach (var w in _windows.ToList())
                w.CloseForReal();
        }
    }
}
