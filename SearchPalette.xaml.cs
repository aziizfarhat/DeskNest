using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskNest
{
    // Palette de recherche globale : zones classiques, dossiers des Folder Portals
    // et bureau. Le filtrage est instantané ; les icônes se chargent en arrière-plan.
    public partial class SearchPaletteWindow : Window
    {
        private const int MaxResults = 250;
        private const int MaxPortalEntries = 5000;

        private readonly NestManager _manager;
        private readonly List<SearchEntry> _all = new();
        private readonly ObservableCollection<SearchEntry> _view = new();
        private bool _ready;
        private bool _closing;

        public bool IsClosing => _closing;

        public SearchPaletteWindow(NestManager manager)
        {
            InitializeComponent();
            _manager = manager;
            ResultsList.ItemsSource = _view;
            QueryBox.TextChanged += (_, _) =>
            {
                HintText.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Filter();
            };
            ResultsList.MouseDoubleClick += (_, _) => OpenSelected();
            Loaded += OnLoaded;
            Closing += (_, _) => _closing = true;
            Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
            Loc.Changed += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            // Les noms des zones et du bureau doivent suivre la langue choisie.
            if (_ready) BuildEntries();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildEntries();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
            _ready = true;
            Deactivated += (_, _) => { if (_ready && !_closing) ClosePalette(); };
        }

        // Ferme la palette une seule fois : le double-clic et la perte de focus
        // peuvent demander la fermeture pendant que la fenêtre se ferme déjà.
        public void ClosePalette()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        public void FocusQuery()
        {
            Activate();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
            QueryBox.SelectAll();
        }

        private void BuildEntries()
        {
            _all.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string desktop = Loc.T("search.desktop");

            foreach (var zone in _manager.Nests)
            {
                if (!string.IsNullOrEmpty(zone.PortalPath))
                {
                    if (!Directory.Exists(zone.PortalPath)) continue;
                    try
                    {
                        int n = 0;
                        foreach (var path in Directory.EnumerateFileSystemEntries(zone.PortalPath))
                        {
                            if (n++ >= MaxPortalEntries) break;
                            AddEntry(path, zone.Title, zone.Id, seen);
                        }
                    }
                    catch (Exception ex) { Diag.Log("recherche portail : " + ex.Message); }
                }
                else
                {
                    foreach (var path in zone.Items)
                        AddEntry(path, zone.Title, zone.Id, seen);
                }
            }

            foreach (var dir in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(dir))
                        AddEntry(path, desktop, null, seen);
                }
                catch (Exception ex) { Diag.Log("recherche bureau : " + ex.Message); }
            }

            Filter();
        }

        private void AddEntry(string path, string source, Guid? zoneId, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            if (!seen.Add(path.TrimEnd('\\'))) return;
            _all.Add(new SearchEntry(path, source, zoneId));
        }

        private void Filter()
        {
            string q = QueryBox.Text.Trim();
            IEnumerable<SearchEntry> matches;

            if (q.Length == 0)
            {
                matches = _all
                    .OrderBy(e => e.Source, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
            }
            else
            {
                matches = _all
                    .Select(e => (Entry: e, Rank: Rank(e, q)))
                    .Where(x => x.Rank >= 0)
                    .OrderBy(x => x.Rank)
                    .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(x => x.Entry);
            }

            var list = matches.ToList();
            _view.Clear();
            foreach (var e in list.Take(MaxResults)) _view.Add(e);

            EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CountText.Text = Loc.F("search.results", list.Count);
            ResultsList.SelectedIndex = _view.Count > 0 ? 0 : -1;
        }

        private static int Rank(SearchEntry e, string query)
        {
            if (e.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase)) return 0;
            if (e.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 1;
            if (e.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 2;
            if (e.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 3;
            if (e.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 4;
            return -1;
        }

        private void OpenSelected()
        {
            if (_closing) return;
            if (ResultsList.SelectedItem is not SearchEntry entry) return;
            ClosePalette();

            // Laisse la fenêtre terminer sa fermeture avant d'activer la zone ou
            // d'ouvrir l'élément : sinon WPF lève « objet Window en cours de fermeture ».
            Dispatcher.BeginInvoke(new Action(() => OpenEntry(entry)), DispatcherPriority.Background);
        }

        private void OpenEntry(SearchEntry entry)
        {
            if (entry.ZoneId is Guid id) _manager.ActivateZone(id, entry.Path);

            try
            {
                Process.Start(new ProcessStartInfo(entry.Path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Directory.Exists(entry.Path)
                        ? entry.Path
                        : (Path.GetDirectoryName(entry.Path) ?? "")
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("msg.cannotOpen", entry.Path, ex.Message), "DeskNest",
                    MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, Loc.MsgOptions);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Les IME peuvent signaler la touche via SystemKey : Échap doit toujours fermer.
            var key = e.Key == Key.ImeProcessed ? e.SystemKey : e.Key;
            switch (key)
            {
                case Key.Escape:
                    ClosePalette();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    OpenSelected();
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (_view.Count > 0)
                        ResultsList.SelectedIndex = Math.Min(_view.Count - 1, ResultsList.SelectedIndex + 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (_view.Count > 0)
                        ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    if (_view.Count > 0)
                        ResultsList.SelectedIndex = Math.Min(_view.Count - 1, ResultsList.SelectedIndex + 8);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    if (_view.Count > 0)
                        ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 8);
                    e.Handled = true;
                    break;
            }
        }

    }

    public sealed class SearchEntry : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public string Name { get; }
        public string Path { get; }
        public string Detail => Path;
        public string Source { get; }
        public Guid? ZoneId { get; }

        public ImageSource? Icon
        {
            get => _icon;
            private set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SearchEntry(string path, string source, Guid? zoneId)
        {
            Name = ItemVM.GetName(path);
            Path = path;
            Source = source;
            ZoneId = zoneId;
            Icon = IconHelper.GetCached(path);
            if (Icon == null) LoadIconAsync(path);
        }

        private async void LoadIconAsync(string path)
        {
            try
            {
                var icon = await IconHelper.GetIconAsync(path);
                if (icon != null && _icon == null) Icon = icon;
            }
            catch { }
        }
    }
}
