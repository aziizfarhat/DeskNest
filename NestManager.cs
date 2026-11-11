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
        private GlobalHotkey? _searchHotkey;
        private MouseHook? _mouseHook;
        private SettingsWindow? _settings;
        private SearchPaletteWindow? _searchPalette;
        private GuideOverlay? _guides;
        private DispatcherTimer? _updateTimer;
        private bool _updateBusy;

        // Annuler / rétablir (disposition uniquement : les fichiers ne sont jamais restaurés).
        private readonly Dictionary<Guid, NestData> _baseline = new();
        private bool _applyingHistory;
        private DelegateUndo? _pendingZoneCmd;
        private Guid _pendingZoneId;
        private DateTime _pendingZoneAt;
        private bool _warnedProtected, _warnedFailure, _warnedHotkey, _hotkeyActive;
        private readonly DispatcherTimer _desktopTimer;
        private readonly DispatcherTimer _desktopWake;
        private DesktopWatcher? _desktopWatcher;
        private int _desktopWakeQueued;

        public bool Hidden { get; private set; }
        public AppSettings Settings => _data.Settings;
        public IList<NestData> Nests => _data.Nests;
        public IEnumerable<NestWindow> Windows => _windows;

        // ---------- Annuler / rétablir ----------
        public UndoStack History { get; } = new();

        public void Undo()
        {
            _pendingZoneCmd = null;
            if (!History.Undo()) return;
            ApplyVisibility();
            RequestSave();
        }

        public void Redo()
        {
            _pendingZoneCmd = null;
            if (!History.Redo()) return;
            ApplyVisibility();
            RequestSave();
        }

        private static NestData CloneNest(NestData d) => new()
        {
            Id = d.Id,
            Title = d.Title,
            Left = d.Left,
            Top = d.Top,
            Width = d.Width,
            Height = d.Height,
            Locked = d.Locked,
            TitleVisible = d.TitleVisible,
            RolledUp = d.RolledUp,
            Color = d.Color,
            Opacity = d.Opacity,
            IconSize = d.IconSize,
            ListView = d.ListView,
            TitleFont = d.TitleFont,
            TitleFontSize = d.TitleFontSize,
            ItemFont = d.ItemFont,
            ItemFontSize = d.ItemFontSize,
            Enabled = d.Enabled,
            HoverExpand = d.HoverExpand,
            Items = new List<string>(d.Items),
            PortalPath = d.PortalPath,
            PortalOrder = d.PortalOrder.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value))
        };

        private static bool SameLayout(NestData a, NestData b)
        {
            if (a.Title != b.Title || a.Locked != b.Locked || a.TitleVisible != b.TitleVisible ||
                a.RolledUp != b.RolledUp || a.Color != b.Color || a.Opacity != b.Opacity ||
                a.IconSize != b.IconSize || a.ListView != b.ListView ||
                a.TitleFont != b.TitleFont || a.TitleFontSize != b.TitleFontSize ||
                a.ItemFont != b.ItemFont || a.ItemFontSize != b.ItemFontSize ||
                a.Enabled != b.Enabled || a.HoverExpand != b.HoverExpand ||
                a.PortalPath != b.PortalPath ||
                Math.Abs(a.Left - b.Left) > 0.01 || Math.Abs(a.Top - b.Top) > 0.01 ||
                Math.Abs(a.Width - b.Width) > 0.01 || Math.Abs(a.Height - b.Height) > 0.01)
                return false;
            // Les références d'éléments ont leurs propres commandes d'annulation : une
            // modification d'apparence ne doit jamais les inclure.
            return true;
        }

        private void InitBaselines()
        {
            _baseline.Clear();
            foreach (var d in _data.Nests) _baseline[d.Id] = CloneNest(d);
        }

        private void PushUndo(IUndoable action)
        {
            _pendingZoneCmd = null;
            History.Push(action);
        }

        // Regroupe les modifications rapprochées d'une même zone (curseur d'opacité, saisie du titre…).
        private void PushZoneDiff(Guid id, string label, NestData before, NestData after)
        {
            if (_applyingHistory) return;
            var now = DateTime.UtcNow;
            if (!History.IsBatching && _pendingZoneCmd != null && _pendingZoneId == id &&
                (now - _pendingZoneAt).TotalMilliseconds < 900)
            {
                _pendingZoneCmd.ReplaceRedo(() => ApplyZoneStateById(id, after));
                _pendingZoneAt = now;
                return;
            }
            var command = new DelegateUndo(label,
                () => ApplyZoneStateById(id, before),
                () => ApplyZoneStateById(id, after));
            if (!History.IsBatching)
            {
                _pendingZoneCmd = command;
                _pendingZoneId = id;
                _pendingZoneAt = now;
            }
            History.Push(command);
        }

        private void ApplyZoneStateById(Guid id, NestData state)
        {
            var live = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (live == null) return;
            _applyingHistory = true;
            try
            {
                bool boundsChanged =
                    Math.Abs(live.Left - state.Left) > 0.01 || Math.Abs(live.Top - state.Top) > 0.01 ||
                    Math.Abs(live.Width - state.Width) > 0.01 || Math.Abs(live.Height - state.Height) > 0.01;

                live.Title = state.Title;
                live.Left = state.Left;
                live.Top = state.Top;
                live.Width = state.Width;
                live.Height = state.Height;
                live.Locked = state.Locked;
                live.TitleVisible = state.TitleVisible;
                live.RolledUp = state.RolledUp;
                live.Color = state.Color;
                live.Opacity = state.Opacity;
                live.IconSize = state.IconSize;
                live.ListView = state.ListView;
                live.TitleFont = state.TitleFont;
                live.TitleFontSize = state.TitleFontSize;
                live.ItemFont = state.ItemFont;
                live.ItemFontSize = state.ItemFontSize;
                live.Enabled = state.Enabled;
                live.HoverExpand = state.HoverExpand;
                live.PortalPath = state.PortalPath;

                var w = _windows.FirstOrDefault(x => x.Data == live);
                if (w != null)
                {
                    w.ApplyExternalChange();
                    if (boundsChanged) w.SetBounds(live.Left, live.Top, live.Width, live.Height);
                    w.ReloadItems();
                }
                _baseline[id] = CloneNest(live);
            }
            finally { _applyingHistory = false; }
        }

        private void ApplyBoundsById(Guid id, double left, double top, double width, double height)
        {
            var live = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (live == null) return;
            _applyingHistory = true;
            try
            {
                live.Left = left;
                live.Top = top;
                live.Width = width;
                live.Height = height;
                _windows.FirstOrDefault(x => x.Data == live)?.SetBounds(left, top, width, height);
                _baseline[id] = CloneNest(live);
            }
            finally { _applyingHistory = false; }
        }

        // À appeler après toute modification de zone qui ne passe pas par ZoneChanged (menu contextuel).
        public void EditNest(NestData d, string label, Action change)
        {
            if (_applyingHistory) { change(); return; }
            var before = _baseline.TryGetValue(d.Id, out var b) ? b : CloneNest(d);
            change();
            var after = CloneNest(d);
            if (!SameLayout(before, after)) PushZoneDiff(d.Id, label, before, after);
            _baseline[d.Id] = after;
            RequestSave();
        }

        public void RecordBounds(NestData d, double left, double top, double width, double height, string label)
        {
            if (_applyingHistory) return;
            if (Math.Abs(d.Left - left) < 0.5 && Math.Abs(d.Top - top) < 0.5 &&
                Math.Abs(d.Width - width) < 0.5 && Math.Abs(d.Height - height) < 0.5) return;
            var id = d.Id;
            double nl = d.Left, nt = d.Top, nw = d.Width, nh = d.Height;
            PushUndo(new DelegateUndo(label,
                () => ApplyBoundsById(id, left, top, width, height),
                () => ApplyBoundsById(id, nl, nt, nw, nh)));
            _baseline[id] = CloneNest(d);
        }

        // ---------- Références d'éléments ----------
        private void RefreshZone(NestData d) =>
            _windows.FirstOrDefault(w => w.Data == d)?.ReloadItems();

        // Retrouve le chemin actuel d'un élément : son emplacement d'origine, une copie
        // déplacée dans le stockage, une autre zone qui le référence, ou le bureau
        // (après un « Del » ou un glisser vers le bureau).
        public string ResolveItemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (File.Exists(path) || Directory.Exists(path)) return path;

            var moved = _data.Moved.FirstOrDefault(m =>
                Same(m.OriginalPath, path) && (File.Exists(m.StorePath) || Directory.Exists(m.StorePath)));
            if (moved != null) return moved.StorePath;

            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) return path;

            foreach (var f in _data.Nests)
                foreach (var p in f.Items)
                    if (string.Equals(Path.GetFileName(p.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase) &&
                        (File.Exists(p) || Directory.Exists(p)))
                        return p;

            return FindDesktopEntry(name) ?? path;
        }

        // Retire une référence et retourne le chemin réellement retiré.
        private string? RemoveItemRef(Guid id, string path)
        {
            var d = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (d == null) return null;
            int i = d.Items.FindIndex(p => Same(p, path));
            if (i < 0) return null;
            var removed = d.Items[i];
            d.Items.RemoveAt(i);
            RefreshZone(d);
            RequestSave();
            return removed;
        }

        // Insère une référence, ré-adopte l'élément s'il est sur le bureau et retourne
        // le chemin finalement référencé (il peut changer après adoption).
        private string? InsertItemRef(Guid id, string path, int index)
        {
            var d = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (d == null) return null;
            if (!d.Items.Any(p => Same(p, path)))
            {
                int at = Math.Clamp(index, 0, d.Items.Count);
                d.Items.Insert(at, path);
            }
            _applyingHistory = true;
            try { AdoptDesktopItemsInEnabledZones(d); } // l'élément avait pu être remis sur le bureau
            finally { _applyingHistory = false; }
            RefreshZone(d);
            RequestSave();

            var exact = d.Items.FirstOrDefault(p => Same(p, path));
            if (exact != null) return exact;
            var name = Path.GetFileName(path.TrimEnd('\\'));
            return d.Items.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase)) ?? path;
        }

        // `fromDesktop` : l'élément vient du bureau. Annuler l'ajout le remet sur le bureau.
        public void RecordItemAdded(NestData d, string path, int index, bool fromDesktop = false)
        {
            if (_applyingHistory) return;
            var id = d.Id;
            string current = path;
            PushUndo(new DelegateUndo(Loc.T("undo.addItem"),
                () =>
                {
                    var removed = RemoveItemRef(id, current);
                    if (removed == null) return;
                    current = removed;
                    if (fromDesktop) current = RestoreOnDesktop(removed) ?? removed;
                },
                () =>
                {
                    var target = fromDesktop ? ResolveItemPath(current) : current;
                    current = InsertItemRef(id, target, index) ?? target;
                }));
        }

        // `restoreOnDesktop` : le retrait remet l'élément sur le bureau (Del, glisser
        // vers le bureau). Annuler le retrait le ré-adopte dans la zone ; rétablir le
        // retire à nouveau et le remet sur le bureau.
        public void RecordItemRemoved(NestData d, string path, int index, bool restoreOnDesktop = false)
        {
            if (_applyingHistory) return;
            var id = d.Id;
            string current = path;
            PushUndo(new DelegateUndo(Loc.T("undo.removeItem"),
                () =>
                {
                    var target = restoreOnDesktop ? ResolveItemPath(current) : current;
                    current = InsertItemRef(id, target, index) ?? target;
                },
                () =>
                {
                    var removed = RemoveItemRef(id, current);
                    if (removed == null) return;
                    current = removed;
                    if (restoreOnDesktop) current = RestoreOnDesktop(removed) ?? removed;
                }));
        }

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
            InitBaselines();

            try
            {
                _hotkey = new GlobalHotkey();
                _hotkey.Pressed += ToggleHidden;
            }
            catch (Exception ex) { Diag.Log("raccourci : " + ex.Message); }

            try
            {
                _searchHotkey = new GlobalHotkey();
                _searchHotkey.Pressed += ShowSearchPalette;
                if (!_searchHotkey.RegisterSearch())
                    Diag.Log("raccourci Ctrl+Shift+Espace indisponible");
            }
            catch (Exception ex) { Diag.Log("raccourci recherche : " + ex.Message); }

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

        public void ShowSearchPalette()
        {
            if (_searchPalette != null && !_searchPalette.IsClosing)
            {
                _searchPalette.FocusQuery();
                return;
            }

            var palette = new SearchPaletteWindow(this);
            palette.Closed += (_, _) =>
            {
                if (ReferenceEquals(_searchPalette, palette)) _searchPalette = null;
            };
            _searchPalette = palette;
            palette.Show();
        }

        // Met une zone au premier plan et, si demandé, y sélectionne un élément.
        public void ActivateZone(Guid id, string? revealPath = null)
        {
            var w = _windows.FirstOrDefault(x => x.Data.Id == id);
            if (w == null) return;
            if (!w.IsVisible) w.Show();
            w.Activate();
            if (revealPath != null) w.RevealItem(revealPath);
        }

        public void SettingsChanged()
        {
            ApplySettings();
            RequestSave();
        }

        // ---------- Mises à jour ----------
        // Vérification différée : le démarrage des zones reste instantané.
        public void ScheduleUpdateCheck()
        {
            if (!Settings.AutoUpdate) return;
            _updateTimer?.Stop();
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _updateTimer.Tick += async (_, _) =>
            {
                _updateTimer?.Stop();
                await CheckForUpdatesAsync(silent: true);
            };
            _updateTimer.Start();
        }

        public async Task CheckForUpdatesAsync(bool silent)
        {
            if (_updateBusy) return;
            _updateBusy = true;
            try
            {
                var info = await UpdateChecker.CheckAsync();
                if (info == null)
                {
                    if (!silent)
                        MessageBox.Show(Loc.T("upd.none"), "DeskNest", MessageBoxButton.OK,
                            MessageBoxImage.Information, MessageBoxResult.OK, Loc.MsgOptions);
                    return;
                }

                var current = UpdateChecker.CurrentVersion();
                var answer = MessageBox.Show(
                    Loc.F("upd.available", info.Version.ToString(3), current.ToString(3)),
                    "DeskNest", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.No, Loc.MsgOptions);
                if (answer != MessageBoxResult.Yes) return;

                string file;
                try { file = await UpdateChecker.DownloadAsync(info); }
                catch (Exception ex)
                {
                    Diag.Log("téléchargement de la mise à jour : " + ex.Message);
                    MessageBox.Show(Loc.F("upd.failed", ex.Message), "DeskNest", MessageBoxButton.OK,
                        MessageBoxImage.Error, MessageBoxResult.OK, Loc.MsgOptions);
                    return;
                }

                // L'installateur doit être signé : c'est ce qui évite l'avertissement SmartScreen.
                if (!UpdateChecker.IsSignatureValid(file))
                {
                    MessageBox.Show(Loc.T("upd.invalid"), "DeskNest", MessageBoxButton.OK,
                        MessageBoxImage.Warning, MessageBoxResult.OK, Loc.MsgOptions);
                    return;
                }

                try
                {
                    UpdateChecker.Install(file);
                    Application.Current?.Shutdown();
                }
                catch (Exception ex)
                {
                    Diag.Log("installation de la mise à jour : " + ex.Message);
                    MessageBox.Show(Loc.F("upd.installFail", ex.Message), "DeskNest", MessageBoxButton.OK,
                        MessageBoxImage.Error, MessageBoxResult.OK, Loc.MsgOptions);
                }
            }
            catch (Exception ex)
            {
                Diag.Log("vérification de la mise à jour : " + ex.Message);
                if (!silent)
                    MessageBox.Show(Loc.F("upd.failed", ex.Message), "DeskNest", MessageBoxButton.OK,
                        MessageBoxImage.Error, MessageBoxResult.OK, Loc.MsgOptions);
            }
            finally { _updateBusy = false; }
        }

        // ---------- Aimantation et guides ----------
        public void ShowGuides(int? verticalX, int? horizontalY, Int32Rect area)
        {
            if (!Settings.SnapEnabled) return;
            _guides ??= new GuideOverlay();
            _guides.Update(verticalX, horizontalY, area);
        }

        public void HideGuides() => _guides?.Hide();

        // Répartit les zones visibles en grille sur l'écran principal.
        public void DistributeNests()
        {
            var windows = _windows.Where(w => w.IsVisible).ToList();
            if (windows.Count == 0) return;

            var area = SystemParameters.WorkArea;
            const double margin = 24, gap = 16;
            int count = windows.Count;
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);
            double cellW = Math.Max(160, (area.Width - margin * 2 - gap * (cols - 1)) / cols);
            double cellH = Math.Max(120, (area.Height - margin * 2 - gap * (rows - 1)) / rows);

            History.BeginBatch();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int row = i / cols, col = i % cols;
                    var d = windows[i].Data;
                    double l = d.Left, t = d.Top, w = d.Width, h = d.Height;
                    windows[i].SetBounds(
                        area.Left + margin + col * (cellW + gap),
                        area.Top + margin + row * (cellH + gap),
                        cellW,
                        cellH);
                    RecordBounds(d, l, t, w, h, Loc.T("undo.move"));
                }
            }
            finally { History.EndBatch(Loc.T("undo.move")); }
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
                    {
                        // Le clic est évalué tout de suite, pendant que la palette de
                        // recherche (ou une zone) est encore sous le curseur. Le traitement
                        // différé arrive après sa fermeture : le point semblerait alors
                        // être le bureau et masquerait les zones par erreur.
                        if (DesktopHelper.IsOwnWindowAt(x, y)) return;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => OnDesktopDoubleClick(x, y)), DispatcherPriority.Background);
                    };
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
            RecordZoneBaseline(d);
        }

        private void RecordZoneBaseline(NestData d)
        {
            if (_applyingHistory) return;
            var after = CloneNest(d);
            if (_baseline.TryGetValue(d.Id, out var before) && !SameLayout(before, after))
                PushZoneDiff(d.Id, Loc.T("undo.zoneEdit"), before, after);
            _baseline[d.Id] = after;
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
            History.Clear();
            _pendingZoneCmd = null;
            InitBaselines();
            ApplySettings();
            RequestSave();
            return true;
        }

        // ---------- Zones ----------
        public IList<NestTemplate> Templates => _data.Templates;

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

        // Duplique une zone complète : apparence, taille et références (les fichiers ne sont pas copiés).
        public void DuplicateNest(NestData source)
        {
            var d = new NestData
            {
                Title = Loc.F("zone.copyName", source.Title),
                Color = source.Color,
                Opacity = source.Opacity,
                IconSize = source.IconSize,
                ListView = source.ListView,
                Width = source.Width,
                Height = source.Height,
                TitleVisible = source.TitleVisible,
                TitleFont = source.TitleFont,
                TitleFontSize = source.TitleFontSize,
                ItemFont = source.ItemFont,
                ItemFontSize = source.ItemFontSize,
                HoverExpand = source.HoverExpand,
                PortalPath = source.PortalPath,
                Items = new List<string>(source.Items)
            };
            foreach (var kv in source.PortalOrder)
                d.PortalOrder[kv.Key] = new List<string>(kv.Value);
            d.Left = source.Left + 30;
            d.Top = source.Top + 30;
            AddNest(d, false);
        }

        // Enregistre l'apparence d'une zone comme modèle. Un nom existant met le modèle à jour.
        public void SaveTemplate(NestData source, string name)
        {
            name = name.Trim();
            if (name.Length == 0) return;

            var t = _data.Templates.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (t == null)
            {
                t = new NestTemplate();
                _data.Templates.Add(t);
            }
            t.Name = name;
            t.Color = source.Color;
            t.Opacity = source.Opacity;
            t.IconSize = source.IconSize;
            t.ListView = source.ListView;
            t.Width = source.Width;
            t.Height = source.Height;
            t.TitleVisible = source.TitleVisible;
            t.TitleFont = source.TitleFont;
            t.TitleFontSize = source.TitleFontSize;
            t.ItemFont = source.ItemFont;
            t.ItemFontSize = source.ItemFontSize;
            RequestSave();
        }

        public void CreateNestFromTemplate(NestTemplate t)
        {
            var d = new NestData
            {
                Title = t.Name,
                Color = t.Color,
                Opacity = t.Opacity,
                IconSize = t.IconSize,
                ListView = t.ListView,
                Width = t.Width,
                Height = t.Height,
                TitleVisible = t.TitleVisible,
                TitleFont = t.TitleFont,
                TitleFontSize = t.TitleFontSize,
                ItemFont = t.ItemFont,
                ItemFontSize = t.ItemFontSize
            };
            AddNest(d);
        }

        public void DeleteTemplate(NestTemplate t)
        {
            _data.Templates.Remove(t);
            RequestSave();
        }

        // Applique l'apparence et la taille d'un modèle à une zone existante (sans toucher aux éléments).
        public void ApplyTemplate(NestData target, NestTemplate t)
        {
            EditNest(target, Loc.T("undo.applyTemplate"), () =>
            {
                target.Color = t.Color;
                target.Opacity = t.Opacity;
                target.IconSize = t.IconSize;
                target.ListView = t.ListView;
                target.Width = t.Width;
                target.Height = t.Height;
                target.TitleVisible = t.TitleVisible;
                target.TitleFont = t.TitleFont;
                target.TitleFontSize = t.TitleFontSize;
                target.ItemFont = t.ItemFont;
                target.ItemFontSize = t.ItemFontSize;
                if (!target.TitleVisible) target.RolledUp = false;
            });

            var w = _windows.FirstOrDefault(x => x.Data == target);
            if (w != null)
            {
                w.ApplyExternalChange();
                w.SetBounds(target.Left, target.Top, target.Width, target.Height);
            }
        }

        public void ShowTemplates()
        {
            ShowSettings();
            _settings?.SelectTemplates();
        }

        private void AddNest(NestData d, bool place = true)
        {
            int n = _data.Nests.Count;
            if (string.IsNullOrEmpty(d.Title)) d.Title = Loc.F("zone.default", n + 1);
            if (place)
            {
                d.Left = 80 + 30 * (n % 10);
                d.Top = 80 + 30 * (n % 10);
            }
            int index = _data.Nests.Count;
            _data.Nests.Add(d);
            Hidden = false;
            Settings.Enabled = true; // créer une zone la rend forcément visible
            OpenWindow(d);
            ApplyVisibility();
            _baseline[d.Id] = CloneNest(d);
            PushUndo(new DelegateUndo(Loc.T("undo.newZone"),
                () => RemoveNestSilent(d),
                () => ReopenNestAt(d, index)));
            RequestSave();
        }

        // Retire une zone sans toucher aux fichiers (annulation d'une création).
        private void RemoveNestSilent(NestData d)
        {
            _data.Nests.Remove(d);
            _baseline.Remove(d.Id);
            var w = _windows.FirstOrDefault(x => x.Data == d);
            if (w != null)
            {
                _windows.Remove(w);
                w.CloseForReal();
            }
            ApplyVisibility();
            RequestSave();
        }

        // Remet une zone supprimée (ou annulée) à sa place, avec ses références.
        private void ReopenNestAt(NestData d, int index)
        {
            if (_data.Nests.Contains(d)) return;
            int at = Math.Clamp(index, 0, _data.Nests.Count);
            _data.Nests.Insert(at, d);
            OpenWindow(d);
            _baseline[d.Id] = CloneNest(d);
            _applyingHistory = true;
            try { AdoptDesktopItemsInEnabledZones(d); } // les fichiers avaient été remis sur le bureau
            finally { _applyingHistory = false; }
            ApplyVisibility();
            RequestSave();
        }

        public void Remove(NestWindow w)
        {
            var items = w.Data.Items.ToList();
            var data = w.Data;
            int index = _data.Nests.IndexOf(data);
            _data.Nests.Remove(data);
            _baseline.Remove(data.Id);
            _windows.Remove(w);
            w.CloseForReal();
            foreach (var p in items) RestoreOnDesktop(p);
            PushUndo(new DelegateUndo(Loc.T("undo.deleteZone"),
                () => ReopenNestAt(data, index),
                () => RemoveNestSilent(data)));
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
            History.BeginBatch();
            try
            {
                foreach (var p in paths)
                {
                    if (!fromPortal && !src.RemoveItem(p, false)) continue;
                    target.AddItem(p, at);
                    if (at >= 0) at++;
                }
            }
            finally { History.EndBatch(Loc.T("undo.moveItems")); }
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
        // Retourne le chemin actuel (sur le bureau si le déplacement a réussi).
        public string? RestoreOnDesktop(string path)
        {
            if (_data.Nests.Any(f => f.Items.Any(p => Same(p, path)))) return path; // encore référencé ailleurs

            int mi = _data.Moved.FindIndex(m => Same(m.StorePath, path));
            if (mi >= 0)
            {
                var restored = MoveBack(_data.Moved[mi]);
                if (restored != null)
                {
                    _data.Moved.RemoveAt(mi);
                    RequestSave();
                    return restored;
                }
                Warn(Loc.F("msg.cannotRestore", path));
                RequestSave();
                return path;
            }

            int i = _data.HiddenByApp.FindIndex(p => Same(p, path));
            if (i < 0) return path;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _data.HiddenByApp.RemoveAt(i); // plus rien à restaurer
            }
            else if (DesktopHelper.SetHidden(path, false)) _data.HiddenByApp.RemoveAt(i);
            else Warn(Loc.F("msg.cannotRestore", path));
            RequestSave();
            return path;
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
                if (ShouldShow(w)) w.EnsureDesktopVisibility(host, desktopForeground);
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
                    // Suppression réelle : les références disparaissent sans étape d'annulation.
                    foreach (var w in _windows) w.RemoveItem(p, false, record: false);
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
            _updateTimer?.Stop();
            _desktopWatcher?.Dispose();
            _desktopWatcher = null;

            // Remet les fichiers sur le Bureau sans perdre leurs références.
            RestoreManagedItemsOnShutdown();

            // Sauvegarde les associations pour le prochain démarrage.
            Storage.Save(_data);

            _mouseHook?.Dispose();
            _hotkey?.Dispose();
            _searchHotkey?.Dispose();
            _guides?.Dispose();
            _settings?.Close();

            foreach (var w in _windows.ToList())
                w.CloseForReal();
        }
    }
}
