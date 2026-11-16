using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DeskNest
{
    public sealed partial class NestManager
    {
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
            var watch = Stopwatch.StartNew();
            ApplySettings();
            RequestSave();
            if (watch.ElapsedMilliseconds > 200)
                Diag.Log($"paramètres : application lente ({watch.ElapsedMilliseconds} ms)");
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
                    Loc.F("upd.available", UpdateChecker.FormatVersion(info.Version),
                        UpdateChecker.FormatVersion(current)),
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

                // L'installateur est vérifié par son empreinte SHA-256 publiée avec la
                // release. La signature Authenticode n'est contrôlée que si un
                // certificat est disponible (UpdateChecker.RequireSignature).
                var verification = await UpdateChecker.VerifyAsync(info, file);
                if (verification != UpdateChecker.VerificationResult.Ok)
                {
                    string key = verification switch
                    {
                        UpdateChecker.VerificationResult.SignatureInvalid => "upd.invalid",
                        UpdateChecker.VerificationResult.MissingChecksum => "upd.noChecksum",
                        _ => "upd.badChecksum"
                    };
                    MessageBox.Show(Loc.T(key), "DeskNest", MessageBoxButton.OK,
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
            SyncSearchHotkey();
            SyncMouseHook();
            foreach (var w in _windows) w.ApplyExternalChange();
            ApplyVisibility();
        }

        // Le raccourci n'est réenregistré que lorsqu'il change réellement.
        private void SyncHotkey()
        {
            if (_hotkey == null) return;
            try
            {
                if (Settings.HotkeyEnabled)
                {
                    string spec = HotkeyText.Normalize(Settings.ToggleHotkey, HotkeyText.DefaultToggle);
                    if (_hotkeyActive && spec == _activeToggleHotkey) return;

                    _hotkeyActive = _hotkey.Register(spec);
                    _activeToggleHotkey = _hotkeyActive ? spec : "";
                    if (!_hotkeyActive)
                    {
                        string display = HotkeyText.Display(spec, HotkeyText.DefaultToggle);
                        Diag.Log($"raccourci {display} indisponible");
                        if (!_warnedHotkey)
                        {
                            _warnedHotkey = true;
                            Warn(Loc.F("msg.hotkeyFailed", display));
                        }
                    }
                }
                else if (_hotkeyActive)
                {
                    _hotkey.Unregister();
                    _hotkeyActive = false;
                    _activeToggleHotkey = "";
                }
            }
            catch (Exception ex) { Diag.Log("raccourci : " + ex.Message); }
        }

        // La palette de recherche a son propre raccourci, configurable séparément.
        private void SyncSearchHotkey()
        {
            if (_searchHotkey == null) return;
            try
            {
                string spec = HotkeyText.Normalize(Settings.SearchHotkey, HotkeyText.DefaultSearch);
                if (_searchHotkeyActive && spec == _activeSearchHotkey) return;

                _searchHotkeyActive = _searchHotkey.Register(spec);
                _activeSearchHotkey = _searchHotkeyActive ? spec : "";
                if (!_searchHotkeyActive)
                {
                    string display = HotkeyText.Display(spec, HotkeyText.DefaultSearch);
                    Diag.Log($"raccourci {display} indisponible");
                    if (!_warnedSearchHotkey)
                    {
                        _warnedSearchHotkey = true;
                        Warn(Loc.F("msg.hotkeyFailed", display));
                    }
                }
            }
            catch (Exception ex) { Diag.Log("raccourci recherche : " + ex.Message); }
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
                    hook.DoubleClick += (x, y, window) =>
                    {
                        // Le handle a été capturé pendant le clic : le traitement
                        // différé peut donc se faire sur le dispatcher sans risquer
                        // de confondre une fenêtre DeskNest déjà fermée avec le bureau.
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (DesktopHelper.IsOwnWindow(window)) return;
                            OnDesktopDoubleClick(x, y);
                        }), DispatcherPriority.Background);
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
            var watch = Stopwatch.StartNew();
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
            if (watch.ElapsedMilliseconds > 200)
                Diag.Log($"zone : modification lente ({watch.ElapsedMilliseconds} ms)");
        }

        // Modification d'apparence (couleur, police, fond, taille…). Aucun impact sur les
        // fichiers du bureau : on évite ainsi les vérifications de dossier à chaque
        // mouvement de curseur dans la fenêtre de paramètres.
        public void ZoneAppearanceChanged(NestData d)
        {
            var watch = Stopwatch.StartNew();
            _windows.FirstOrDefault(w => w.Data == d)?.ApplyExternalChange();
            ApplyVisibility();
            RequestSave();
            RecordZoneBaseline(d);
            if (watch.ElapsedMilliseconds > 200)
                Diag.Log($"zone : apparence lente ({watch.ElapsedMilliseconds} ms)");
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

    }
}
