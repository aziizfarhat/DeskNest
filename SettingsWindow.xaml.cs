using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskNest
{
    public partial class SettingsWindow : Window
    {
        private static readonly int[] Sizes = { 32, 40, 48 };
        private static readonly string[] SizeKeys = { "m.small", "m.medium", "m.large" };
        private readonly NestManager _m;
        private bool _loading = true; // vrai pendant le chargement : les gestionnaires d'événements ne font rien

        private NestData? Current => ZoneList.SelectedItem as NestData;

        public SettingsWindow(NestManager manager)
        {
            InitializeComponent();
            _m = manager;
            FlowDirection = Loc.Flow;

            foreach (var l in Loc.Languages) CmbLanguage.Items.Add(l.Name); // chaque langue s'écrit dans sa propre langue

            // Toutes les polices installées dans Windows (chaque nom est affiché dans sa propre police).
            var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct()
                             .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            FillFontCombo(ZTitleFont, fonts);
            FillFontCombo(ZItemFont, fonts);

            FillStaticCombos();
            Loc.Changed += OnLanguageChanged;
            Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
            LoadAll();
        }

        // Listes dont le texte dépend de la langue.
        private void FillStaticCombos()
        {
            _loading = true;
            foreach (var cb in new[] { ZColor, DColor })
            {
                cb.Items.Clear();
                foreach (var (name, _) in Palette.Presets) cb.Items.Add(Loc.T(name));
            }
            foreach (var cb in new[] { ZIcon, DIcon })
            {
                cb.Items.Clear();
                for (int i = 0; i < Sizes.Length; i++) cb.Items.Add($"{Loc.T(SizeKeys[i])} ({Sizes[i]})");
            }
            CmbDesktopMode.Items.Clear();
            CmbDesktopMode.Items.Add(Loc.T("g.modeMove"));
            CmbDesktopMode.Items.Add(Loc.T("g.modeHide"));
            ZView.Items.Clear();
            ZView.Items.Add(Loc.T("view.grid"));
            ZView.Items.Add(Loc.T("view.list"));
        }

        private void OnLanguageChanged()
        {
            FlowDirection = Loc.Flow;
            var cur = Current;
            FillStaticCombos();
            ((ComboBoxItem)ZTitleFont.Items[0]).Content = Loc.T("z.defaultFont");
            ((ComboBoxItem)ZItemFont.Items[0]).Content = Loc.T("z.defaultFont");
            LoadAll();
            if (cur != null) ZoneList.SelectedItem = cur;
        }

        private static int IndexOfPreset(string hex)
        {
            for (int i = 0; i < Palette.Presets.Length; i++)
                if (string.Equals(Palette.Presets[i].Hex, hex, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // Textes qui contiennent une valeur (curseurs, version...).
        private void UpdateLabels()
        {
            var s = _m.Settings;
            HoverDelayLabel.Text = Loc.F("b.expandDelay", s.HoverExpandDelayMs);
            CollapseDelayLabel.Text = Loc.F("b.collapseDelay", s.HoverCollapseDelayMs);
            AnimMsLabel.Text = Loc.F("b.animMs", s.RollAnimationMs);
            DOpacityLabel.Text = Loc.F("z.opacity", (int)(s.DefaultOpacity * 100));
            RadiusLabel.Text = Loc.F("a.radius", s.CornerRadius);
            DataPath.Text = Loc.F("k.path", Storage.Dir);
            AboutVersion.Text = Loc.F("ab.version", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?");
            if (Current is NestData d) ZOpacityLabel.Text = Loc.F("z.opacity", (int)(d.Opacity * 100));
        }

        // ---------- Chargement ----------
        private void LoadAll()
        {
            _loading = true;
            var s = _m.Settings;

            CmbLanguage.SelectedIndex = Array.FindIndex(Loc.Languages, l => l.Code == Loc.Current);
            ChkEnabled.IsChecked = s.Enabled;
            ChkAutostart.IsChecked = TrayIcon.IsAutostart();
            ChkHotkey.IsChecked = s.HotkeyEnabled;
            ChkDesktopDbl.IsChecked = s.DesktopDoubleClickEnabled;
            ChkHideIcons.IsChecked = s.HideDesktopIcons;
            CmbDesktopMode.SelectedIndex = s.DesktopMode == 1 ? 1 : 0;

            ChkHover.IsChecked = s.HoverExpand;
            HoverDelay.Value = s.HoverExpandDelayMs;
            CollapseDelay.Value = s.HoverCollapseDelayMs;
            ChkAnimate.IsChecked = s.AnimateRoll;
            AnimMs.Value = s.RollAnimationMs;

            DColor.SelectedIndex = IndexOfPreset(s.DefaultColor);
            DOpacity.Value = s.DefaultOpacity * 100;
            DIcon.SelectedIndex = Array.IndexOf(Sizes, s.DefaultIconSize);
            Radius.Value = s.CornerRadius;
            ChkBorder.IsChecked = s.ShowBorder;

            ZoneList.ItemsSource = null;
            ZoneList.ItemsSource = _m.Nests;
            UpdateLabels();
            _loading = false;
            if (_m.Nests.Count > 0) ZoneList.SelectedIndex = 0;
            else ZonePanel.IsEnabled = false;
        }

        // ---------- Général ----------
        private void CmbLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbLanguage.SelectedIndex < 0) return;
            var code = Loc.Languages[CmbLanguage.SelectedIndex].Code;
            _m.Settings.Language = code;
            Loc.SetLanguage(code); // met à jour tous les textes (fenêtres, menus, icône de notification)
            _m.SettingsChanged();
        }

        private void ChkEnabled_Click(object sender, RoutedEventArgs e) { if (!_loading) _m.SetEnabled(ChkEnabled.IsChecked == true); }
        private void ChkAutostart_Click(object sender, RoutedEventArgs e) { if (!_loading) TrayIcon.SetAutostart(ChkAutostart.IsChecked == true); }
        private void ChkHotkey_Click(object sender, RoutedEventArgs e) { if (_loading) return; _m.Settings.HotkeyEnabled = ChkHotkey.IsChecked == true; _m.SettingsChanged(); }
        private void ChkDesktopDbl_Click(object sender, RoutedEventArgs e) { if (_loading) return; _m.Settings.DesktopDoubleClickEnabled = ChkDesktopDbl.IsChecked == true; _m.SettingsChanged(); }
        private void ChkHideIcons_Click(object sender, RoutedEventArgs e) { if (_loading) return; _m.Settings.HideDesktopIcons = ChkHideIcons.IsChecked == true; _m.SettingsChanged(); }

        private void CmbDesktopMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CmbDesktopMode.SelectedIndex < 0) return;
            _m.Settings.DesktopMode = CmbDesktopMode.SelectedIndex;
            _m.SettingsChanged();
        }

        private void RestoreIcons_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this, Loc.T("tray.restoreConfirm"), "DeskNest", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
            if (r != MessageBoxResult.Yes) return;
            int n = _m.RestoreAllHidden();
            MessageBox.Show(this, Loc.F("g.restoreResult", n), "DeskNest", MessageBoxButton.OK,
                MessageBoxImage.Information, MessageBoxResult.OK, Loc.MsgOptions);
        }

        // ---------- Zones ----------
        private void ZoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var d = Current;
            ZonePanel.IsEnabled = d != null;
            if (d == null) return;

            _loading = true;
            ZVisible.IsChecked = d.Enabled;
            ZName.Text = d.Title;
            ZColor.SelectedIndex = IndexOfPreset(d.Color);
            ZHex.Text = d.Color;
            ZOpacity.Value = d.Opacity * 100;
            ZOpacityLabel.Text = Loc.F("z.opacity", (int)(d.Opacity * 100));
            ZIcon.SelectedIndex = Array.IndexOf(Sizes, d.IconSize);
            ZView.SelectedIndex = d.ListView ? 1 : 0;
            SelectFont(ZTitleFont, d.TitleFont);
            SelectFont(ZItemFont, d.ItemFont);
            ZTitleFontSize.Value = d.TitleFontSize;
            ZTitleFontSizeValue.Text = d.TitleFontSize.ToString();
            ZItemFontSize.Value = d.ItemFontSize;
            ZItemFontSizeValue.Text = d.ItemFontSize.ToString();
            ZTitle.IsChecked = d.TitleVisible;
            ZLocked.IsChecked = d.Locked;
            ZRolled.IsChecked = d.RolledUp;
            ZHover.IsChecked = d.HoverExpand;
            ZInfo.Text = string.IsNullOrEmpty(d.PortalPath)
                ? Loc.F("z.infoClassic", d.Items.Count)
                : Loc.F("z.infoPortal", d.PortalPath);
            _loading = false;
        }

        private void Zone_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || Current is not NestData d) return;
            d.Enabled = ZVisible.IsChecked == true;
            d.TitleVisible = ZTitle.IsChecked == true;
            d.Locked = ZLocked.IsChecked == true;
            d.RolledUp = ZRolled.IsChecked == true;
            d.HoverExpand = ZHover.IsChecked == true;
            if (!d.TitleVisible) { d.RolledUp = false; ZRolled.IsChecked = false; }
            _m.ZoneChanged(d);
            ZoneList.Items.Refresh();
        }

        private void ZName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || Current is not NestData d || string.IsNullOrWhiteSpace(ZName.Text)) return;
            d.Title = ZName.Text.Trim();
            _m.ZoneChanged(d);
            ZoneList.Items.Refresh();
        }

        private void ZColor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Current is not NestData d || ZColor.SelectedIndex < 0) return;
            d.Color = Palette.Presets[ZColor.SelectedIndex].Hex;
            _loading = true; ZHex.Text = d.Color; _loading = false;
            _m.ZoneChanged(d);
        }

        private void ZHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || Current is not NestData d) return;
            if (!Regex.IsMatch(ZHex.Text, "^#[0-9A-Fa-f]{6}$")) return;
            d.Color = ZHex.Text;
            _loading = true; ZColor.SelectedIndex = IndexOfPreset(d.Color); _loading = false;
            _m.ZoneChanged(d);
        }

        private void ZOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            ZOpacityLabel.Text = Loc.F("z.opacity", (int)ZOpacity.Value);
            if (Current is NestData d) { d.Opacity = ZOpacity.Value / 100.0; _m.ZoneChanged(d); }
        }

        private void ZIcon_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Current is not NestData d || ZIcon.SelectedIndex < 0) return;
            d.IconSize = Sizes[ZIcon.SelectedIndex];
            _m.ZoneChanged(d);
        }

        private void ZView_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Current is not NestData d || ZView.SelectedIndex < 0) return;
            d.ListView = ZView.SelectedIndex == 1;
            _m.ZoneChanged(d);
        }

        private static void FillFontCombo(ComboBox cb, List<string> fonts)
        {
            cb.Items.Add(new ComboBoxItem { Content = Loc.T("z.defaultFont") });
            foreach (var n in fonts)
                cb.Items.Add(new ComboBoxItem { Content = n, FontFamily = new FontFamily(n), FontSize = 14 });
        }

        private static void SelectFont(ComboBox cb, string? name)
        {
            if (string.IsNullOrEmpty(name)) { cb.SelectedIndex = 0; return; }
            for (int i = 1; i < cb.Items.Count; i++)
                if (cb.Items[i] is ComboBoxItem it && string.Equals(it.Content as string, name, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            cb.SelectedIndex = -1; // police non installée sur ce PC : elle est conservée telle quelle
        }

        private static string FontName(ComboBox cb) =>
            cb.SelectedIndex <= 0 ? "" : ((cb.SelectedItem as ComboBoxItem)?.Content as string ?? "");

        private void ZTitleFont_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Current is not NestData d || ZTitleFont.SelectedIndex < 0) return;
            d.TitleFont = FontName(ZTitleFont);
            _m.ZoneChanged(d);
        }

        private void ZItemFont_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Current is not NestData d || ZItemFont.SelectedIndex < 0) return;
            d.ItemFont = FontName(ZItemFont);
            _m.ZoneChanged(d);
        }

        private void ZTitleFontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || Current is not NestData d) return;

            d.TitleFontSize = (int)Math.Round(ZTitleFontSize.Value);
            ZTitleFontSizeValue.Text = d.TitleFontSize.ToString();

            _m.ZoneChanged(d);
        }

        private void ZItemFontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || Current is not NestData d) return;

            d.ItemFontSize = (int)Math.Round(ZItemFontSize.Value);
            ZItemFontSizeValue.Text = d.ItemFontSize.ToString();

            _m.ZoneChanged(d);
        }

        // Ouvre l'onglet Zones sur la zone demandée (menu « Polices… » d'une zone).
        public void SelectZone(NestData zone)
        {
            MainTabs.SelectedItem = ZonesTab;
            ZoneList.SelectedItem = zone;
            ZoneList.ScrollIntoView(zone);
        }

        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (Current is not NestData src) return;
            foreach (var f in _m.Nests)
            {
                f.Color = src.Color; f.Opacity = src.Opacity; f.IconSize = src.IconSize;
                f.TitleFont = src.TitleFont; f.TitleFontSize = src.TitleFontSize;
                f.ItemFont = src.ItemFont; f.ItemFontSize = src.ItemFontSize;
                _m.ZoneChanged(f);
            }
        }

        private void NewZone_Click(object sender, RoutedEventArgs e)
        {
            _m.CreateNest();
            LoadAll();
            ZoneList.SelectedIndex = _m.Nests.Count - 1;
        }

        private void NewPortal_Click(object sender, RoutedEventArgs e)
        {
            int before = _m.Nests.Count;
            _m.CreatePortal();
            if (_m.Nests.Count == before) return;
            LoadAll();
            ZoneList.SelectedIndex = _m.Nests.Count - 1;
        }

        private void DeleteZone_Click(object sender, RoutedEventArgs e)
        {
            if (Current is not NestData d) return;
            var r = MessageBox.Show(this, Loc.F("msg.deleteZone", d.Title), "DeskNest", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
            if (r != MessageBoxResult.Yes) return;
            _m.RemoveNest(d);
            LoadAll();
        }

        // ---------- Comportement ----------
        private void ChkHover_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _m.Settings.HoverExpand = ChkHover.IsChecked == true;
            _m.SettingsChanged();
        }

        private void HoverDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _m.Settings.HoverExpandDelayMs = (int)HoverDelay.Value;
            HoverDelayLabel.Text = Loc.F("b.expandDelay", (int)HoverDelay.Value);
            _m.RequestSave();
        }

        private void CollapseDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _m.Settings.HoverCollapseDelayMs = (int)CollapseDelay.Value;
            CollapseDelayLabel.Text = Loc.F("b.collapseDelay", (int)CollapseDelay.Value);
            _m.RequestSave();
        }

        private void ChkAnimate_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _m.Settings.AnimateRoll = ChkAnimate.IsChecked == true;
            _m.SettingsChanged();
        }

        private void AnimMs_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _m.Settings.RollAnimationMs = (int)AnimMs.Value;
            AnimMsLabel.Text = Loc.F("b.animMs", (int)AnimMs.Value);
            _m.RequestSave();
        }

        // ---------- Apparence ----------
        private void DColor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || DColor.SelectedIndex < 0) return;
            _m.Settings.DefaultColor = Palette.Presets[DColor.SelectedIndex].Hex;
            _m.RequestSave();
        }

        private void DOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _m.Settings.DefaultOpacity = DOpacity.Value / 100.0;
            DOpacityLabel.Text = Loc.F("z.opacity", (int)DOpacity.Value);
            _m.RequestSave();
        }

        private void DIcon_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || DIcon.SelectedIndex < 0) return;
            _m.Settings.DefaultIconSize = Sizes[DIcon.SelectedIndex];
            _m.RequestSave();
        }

        private void ApplyDefaults_Click(object sender, RoutedEventArgs e)
        {
            var s = _m.Settings;
            foreach (var f in _m.Nests)
            {
                f.Color = s.DefaultColor; f.Opacity = s.DefaultOpacity; f.IconSize = s.DefaultIconSize;
                _m.ZoneChanged(f);
            }
            ZoneList_SelectionChanged(this, null!);
        }

        private void Radius_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _m.Settings.CornerRadius = (int)Radius.Value;
            RadiusLabel.Text = Loc.F("a.radius", (int)Radius.Value);
            _m.SettingsChanged();
        }

        private void ChkBorder_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _m.Settings.ShowBorder = ChkBorder.IsChecked == true;
            _m.SettingsChanged();
        }

        // ---------- Sauvegardes ----------
        private void Snapshot_Click(object sender, RoutedEventArgs e)
        {
            var p = _m.CreateSnapshot();
            MessageBox.Show(this, p == null ? Loc.T("k.snapshotFail") : Loc.F("k.snapshotOk", p), "DeskNest",
                MessageBoxButton.OK, p == null ? MessageBoxImage.Warning : MessageBoxImage.Information,
                MessageBoxResult.OK, Loc.MsgOptions);
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            System.IO.Directory.CreateDirectory(Storage.SnapshotDir);
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("k.restoreTitle"),
                InitialDirectory = Storage.SnapshotDir,
                Filter = Loc.T("k.filter")
            };
            if (dlg.ShowDialog(this) != true) return;
            var r = MessageBox.Show(this, Loc.T("k.restoreConfirm"), "DeskNest", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
            if (r != MessageBoxResult.Yes) return;
            if (!_m.RestoreSnapshot(dlg.FileName))
                MessageBox.Show(this, Loc.T("k.restoreInvalid"), "DeskNest", MessageBoxButton.OK,
                    MessageBoxImage.Warning, MessageBoxResult.OK, Loc.MsgOptions);
            else
            {
                Loc.SetLanguage(_m.Settings.Language); // le snapshot peut contenir une autre langue
                LoadAll();
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(Storage.Dir) { UseShellExecute = true }); } catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
