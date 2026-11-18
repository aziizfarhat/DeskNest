using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
        private KeyboardHook? _keyboardHook;
        private QuickActionsBar? _quickActions;
        private IntPtr _hwnd;

        public bool IsClosing => _closing;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        public SearchPaletteWindow(NestManager manager)
        {
            InitializeComponent();
            _manager = manager;
            _quickActions = new QuickActionsBar(QuickInvoke, compact: true);
            QuickActionsHost.Content = _quickActions;
            ApplyQuickActionsVisibility();
            ResultsList.ItemsSource = _view;
            QueryBox.TextChanged += (_, _) =>
            {
                HintText.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Filter();
            };
            ResultsList.MouseDoubleClick += (_, _) => OpenSelected();
            Loaded += OnLoaded;
            Closing += (_, _) => _closing = true;
            Closed += (_, _) =>
            {
                _keyboardHook?.Dispose();
                _keyboardHook = null;
                _quickActions?.Dispose();
                _quickActions = null;
                Loc.Changed -= OnLanguageChanged;
            };
            Loc.Changed += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            // Les noms des zones et du bureau doivent suivre la langue choisie.
            if (_ready) BuildEntries();
            _quickActions?.RefreshLabels();
            ApplyQuickActionsVisibility();
        }

        private void ApplyQuickActionsVisibility()
        {
            QuickActionsHost.Visibility = _manager.Settings.ShowQuickActionsInSearch
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // Un clic exécute l'action et ferme la palette. Aucun raccourci clavier
        // n'est attaché ici : les chiffres et les flèches appartiennent à la recherche.
        private void QuickInvoke(QuickActionKind kind)
        {
            if (_closing) return;
            ClosePalette();
            Dispatcher.BeginInvoke(new Action(() => _manager.ExecuteQuickAction(kind)),
                DispatcherPriority.Background);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _ready = true;

            // La palette prend le clavier immédiatement, avant même de construire
            // la liste : aucune frappe ne doit partir vers le bureau.
            _keyboardHook = new KeyboardHook();
            _keyboardHook.KeyDown += OnGlobalKeyDown;
            FocusQuery();

            BuildEntries();
            Deactivated += (_, _) => { if (_ready && !_closing) ClosePalette(); };
        }

        // Ferme la palette une seule fois : le double-clic, un clic à l'extérieur
        // et la perte de focus peuvent demander la fermeture pendant qu'elle se ferme.
        public void ClosePalette()
        {
            if (_closing) return;
            _closing = true;
            _keyboardHook?.Dispose();
            _keyboardHook = null;
            Close();
        }

        public void FocusQuery()
        {
            Activate();
            if (!IsPaletteForeground()) ForceForeground();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
            QueryBox.SelectAll();
        }

        private bool IsPaletteForeground()
        {
            if (_hwnd == IntPtr.Zero) return false;
            var fg = GetForegroundWindow();
            if (fg == _hwnd || (fg != IntPtr.Zero && IsChild(_hwnd, fg))) return true;
            if (fg == IntPtr.Zero) return false;
            GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)Environment.ProcessId; // fenêtre de composition IME, par exemple
        }

        // Les raccourcis système restent disponibles : sinon la palette
        // emprisonnerait l'utilisateur (Alt+Tab, touche Windows, Alt+F4...).
        private static bool IsSystemShortcut(int vk)
        {
            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;  // VK_MENU
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
            if (vk == 0x5B || vk == 0x5C) return true;          // touches Windows
            if (alt && (vk == 0x09 || vk == 0x73 || vk == 0x1B)) return true; // Alt+Tab / Alt+F4 / Alt+Esc
            if (ctrl && vk == 0x1B) return true;                // Ctrl+Esc, Ctrl+Maj+Échap
            return false;
        }

        private bool OnGlobalKeyDown(int vk)
        {
            if (_closing) return false;
            if (IsPaletteForeground()) return false; // WPF reçoit la frappe normalement

            // Les raccourcis système passent avant Échap : Alt+Échap et Ctrl+Échap
            // doivent continuer d'ouvrir le menu Démarrer ou changer de fenêtre.
            if (IsSystemShortcut(vk)) return false;

            if (vk == 0x1B) // Échap ferme la palette même si elle a perdu le premier plan
            {
                Dispatcher.BeginInvoke(new Action(ClosePalette));
                return true;
            }

            ForceForeground();
            return true; // la touche ne doit pas atteindre l'application au premier plan
        }

        // Reprend le premier plan sans dépendre du bon vouloir de Windows :
        // on partage temporairement la file de messages de la fenêtre active.
        private void ForceForeground()
        {
            if (_hwnd == IntPtr.Zero) return;
            var fg = GetForegroundWindow();
            uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
            uint ourThread = GetWindowThreadProcessId(_hwnd, out _);
            bool attached = fgThread != 0 && ourThread != 0 && fgThread != ourThread &&
                            AttachThreadInput(fgThread, ourThread, true);
            try
            {
                SetForegroundWindow(_hwnd);
                SetFocus(_hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(fgThread, ourThread, false);
            }

            Activate();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
        }

        // Un clic dans la marge transparente de la fenêtre doit fermer la palette
        // comme Échap ; les clics à l'extérieur passent par Deactivated.
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsWithin(d, PaletteBorder)) return;
            ClosePalette();
        }

        private static bool IsWithin(DependencyObject? d, DependencyObject root)
        {
            while (d != null)
            {
                if (ReferenceEquals(d, root)) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
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
            _all.Add(new SearchEntry(path, source, zoneId,
                IconHelper.PixelSize(20, VisualTreeHelper.GetDpi(this).DpiScaleX)));
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

        private readonly int _iconPixels;

        public SearchEntry(string path, string source, Guid? zoneId, int iconPixels)
        {
            Name = ItemVM.GetName(path);
            Path = path;
            Source = source;
            ZoneId = zoneId;
            _iconPixels = iconPixels;
            Icon = IconHelper.GetCached(path, iconPixels);
            if (Icon == null) LoadIconAsync(path);
        }

        private async void LoadIconAsync(string path)
        {
            try
            {
                var icon = await IconHelper.GetIconAsync(path, _iconPixels);
                if (icon != null && _icon == null) Icon = icon;
            }
            catch { }
        }
    }
}
