using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DeskNest
{
    public partial class NestWindow : Window
    {
        public const string ItemFormat = "DeskNest.Item";
        private const double TitleHeight = 28;

        private static Geometry MakeGeometry(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }
        // Icône de zone normale (grille) et de Folder Portal (dossier)
        private static readonly Geometry NestIcon = MakeGeometry("M1,1 H7 V7 H1 Z M9,1 H15 V7 H9 Z M1,9 H7 V15 H1 Z M9,9 H15 V15 H9 Z");
        private static readonly Geometry PortalIcon = MakeGeometry("M1,3 H6 L8,5 H15 V13 H1 Z");

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

        private readonly NestManager _manager;
        public NestData Data { get; }

        private IntPtr Hwnd => new WindowInteropHelper(this).Handle;
        private List<ItemVM> _vms = new();
        private Point _dragStart;
        private bool _loaded, _suspendSize;
        private bool _moving, _resizing;
        private bool _allowClose, _closed;
        private int _refreshVersion;
        private int _winX, _winY, _edge, _animId, _lastIconSize, _lastItemFontSize;
        private POINT _curStart;
        private RECT _startRect;
        private string? _portalCurrent;
        private FileSystemWatcher? _watcher;
        private readonly DispatcherTimer _portalTimer;

        // Pagination des Folder Portals : un dossier de milliers de fichiers ne doit pas figer l'interface.
        private const int PortalPageSize = 200;
        private int _portalPage, _portalPageCount = 1, _portalPageStart;
        private string? _portalPageFolder;
        private List<string>? _portalPageNames;
        private MenuItem _miTitle = null!, _miRoll = null!, _miLock = null!, _miHover = null!, _miPaste = null!;
        private MenuItem _miViewGrid = null!, _miViewList = null!;
        private MenuItem _miPortal = null!, _miPortalChange = null!, _miPortalOpen = null!, _miPortalDetach = null!;

        // Menu des éléments (menu contextuel façon Windows)
        private ContextMenu _itemMenu = null!;
        private MenuItem _imOpen = null!, _imOpenWith = null!, _imLocation = null!, _imCut = null!, _imCopy = null!, _imCopyPath = null!;
        private MenuItem _imPaste = null!, _imRename = null!, _imDelete = null!, _imRemove = null!, _imProps = null!;

        // Glisser-déposer interne (vrai quand une zone de DeskNest a accepté le dépôt)
        private static bool s_internalDrop;

        // Édition du titre et du nom des éléments
        private bool _editingTitle;
        private bool _refreshPending; // une actualisation a été demandée pendant une édition : on la diffère
        private ItemVM? _editingItem, _renameCandidate, _pendingRename;
        private DispatcherTimer? _renameTimer;

        // « Afficher le bureau » : zone remontée au-dessus du bureau
        private bool _raisedForDesktop;

        // Sélection multiple
        private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
        private ItemVM? _anchor, _pendingCollapse, _dragCandidate;
        private bool _marquee;
        private Point _marqueeStart;
        private HashSet<string> _marqueeBase = new(StringComparer.OrdinalIgnoreCase);

        // Folder Portal : historique des dossiers visités (bouton Retour).
        private readonly List<string> _portalHistory = new();

        // Affichage Grille / Liste de cette zone.
        private bool _lastListView;

        // Recherche : filtre temporaire (non enregistré dans la configuration).
        private string _searchText = "";
        private bool _updatingSearch;
        private bool _searchReady;
        private bool HasFilter => !string.IsNullOrWhiteSpace(_searchText);

        // Développement au survol
        private enum HoverMode { None, Expand, Collapse }
        private HoverMode _hoverMode;
        private bool _hoverExpanded, _menuOpen, _dragging, _dragOver;
        private readonly DispatcherTimer _hoverTimer;

        private bool IsRolled => Data.RolledUp && Data.TitleVisible && !_hoverExpanded;
        private bool IsPortal => !string.IsNullOrEmpty(Data.PortalPath);
        private string CurrentFolder => _portalCurrent ?? Data.PortalPath ?? "";
        private string PortalName
        {
            get
            {
                var n = IOPath.GetFileName(CurrentFolder.TrimEnd('\\'));
                return string.IsNullOrEmpty(n) ? CurrentFolder : n;
            }
        }

        public NestWindow(NestManager manager, NestData data)
        {
            InitializeComponent();
            _manager = manager;
            Data = data;
            FlowDirection = Loc.Flow;

            _portalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _portalTimer.Tick += (_, _) => { _portalTimer.Stop(); RefreshItems(); };
            _hoverTimer = new DispatcherTimer();
            _hoverTimer.Tick += (_, _) => OnHoverTick();

            // Garder la zone dans la partie visible des écrans.
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            Left = Math.Min(Math.Max(data.Left, vl - data.Width + 80), vl + vw - 80);
            Top = Math.Min(Math.Max(data.Top, vt), vt + vh - 40);
            Width = Math.Max(data.Width, MinWidth);

            Root.ContextMenu = BuildNestMenu();
            _itemMenu = BuildItemMenu();
            Loc.Changed += OnLanguageChanged;
            ApplyState();
            ApplyFonts();
            ApplyViewMode();
            UpdateSearchUi();
            ResetWatcher();
            RefreshItems();

            SourceInitialized += (_, _) => MakeToolWindow();
            Loaded += (_, _) => { _loaded = true; SendToBottom(); };
            Activated += (_, _) =>
            {
                if (!_raisedForDesktop && !_hoverExpanded) SendToBottom();
                // Les fichiers d'une zone classique peuvent avoir été renommés/supprimés depuis l'Explorateur.
                Dispatcher.BeginInvoke(new Action(RevalidateClassicItems), DispatcherPriority.Background);
            };
            Deactivated += (_, _) =>
            {
                EndTitleEdit(true);
                EndItemEdit(true);
                if (!_menuOpen && !_dragging) ClearSelection();
            };
            // Une zone n'est jamais minimisée / agrandie : elle reste indépendante des fenêtres Windows.
            // « Afficher le bureau » peut néanmoins minimiser la fenêtre par le biais de l'API ShowWindow.
            StateChanged += (_, _) =>
            {
                if (WindowState != WindowState.Minimized) return;
                Dispatcher.BeginInvoke(new Action(RestoreSystemMinimize), DispatcherPriority.Send);
            };
            MouseEnter += (_, _) => OnHoverEnter();
            MouseLeave += (_, _) => OnHoverLeave();
            ContextMenuOpening += (_, _) => _menuOpen = true;
            ContextMenuClosing += (_, _) =>
            {
                _menuOpen = false;
                if (_hoverExpanded && !IsMouseOver) StartHover(HoverMode.Collapse, _manager.Settings.HoverCollapseDelayMs);
            };
            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewMouseDown += Window_PreviewMouseDown;
            LocationChanged += (_, _) =>
            {
                if (!_loaded) return;
                Data.Left = Left; Data.Top = Top;
                _manager.RequestSave();
            };
            SizeChanged += (_, e) =>
            {
                if (!_loaded || _suspendSize) return;
                Data.Width = e.NewSize.Width;
                if (!IsRolled) Data.Height = e.NewSize.Height; // la hauteur "développée" n'est jamais écrasée par la hauteur réduite
                _manager.RequestSave();
            };

            _searchReady = true;
        }

        // Une zone ne doit pas pouvoir être fermée par Alt+F4 ou par le système :
        // elle serait perdue (plus aucune fenêtre à réafficher) jusqu'au redémarrage.
        // La fermeture réelle passe par CloseForReal(), réservée au gestionnaire.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        internal void CloseForReal()
        {
            _allowClose = true;
            if (!_closed) Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _refreshVersion++;
            Loc.Changed -= OnLanguageChanged;
            _watcher?.Dispose();
            _portalTimer.Stop();
            _hoverTimer.Stop();
            _renameTimer?.Stop();
            base.OnClosed(e);
        }

        // Changement de langue : on reconstruit les menus et le titre.
        private void OnLanguageChanged()
        {
            FlowDirection = Loc.Flow;
            Root.ContextMenu = BuildNestMenu();
            _itemMenu = BuildItemMenu();
            UpdateTitle();
        }

        // ---------- Fenêtre : style et z-order ----------
        private void MakeToolWindow()
        {
            var h = Hwnd;
            const int GWL_STYLE = -16, GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
            // Pas de bouton réduire / agrandir, pas de bordure redimensionnable, pas de menu système :
            // Windows (Aero Snap, "Afficher le bureau", Win+M...) ne les traite plus comme des fenêtres normales.
            const int WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000, WS_THICKFRAME = 0x40000, WS_SYSMENU = 0x80000;
            SetWindowLong(h, GWL_STYLE, GetWindowLong(h, GWL_STYLE) & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_THICKFRAME | WS_SYSMENU));
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW); // hors Alt+Tab
            HwndSource.FromHwnd(h)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int WM_SIZE = 0x0005;
            const int SIZE_MINIMIZED = 1;
            if (msg == WM_SYSCOMMAND)
            {
                int cmd = (int)(wParam.ToInt64() & 0xFFF0);
                // SC_SIZE, SC_MOVE, SC_MINIMIZE, SC_MAXIMIZE : ignorés
                if (cmd == 0xF000 || cmd == 0xF010 || cmd == 0xF020 || cmd == 0xF030) handled = true;
            }
            else if (msg == WM_SIZE && (int)(wParam.ToInt64() & 0xFFFF) == SIZE_MINIMIZED)
            {
                // « Afficher le bureau » / Win+D minimise les fenêtres via ShowWindow : on se rétablit aussitôt.
                Diag.Log($"zone « {Data.Title} » réduite par Windows (WM_SIZE) -> restauration");
                Dispatcher.BeginInvoke(new Action(RestoreSystemMinimize), DispatcherPriority.Send);
            }
            return IntPtr.Zero;
        }

        private void RestoreSystemMinimize()
        {
            if (_closed) return;
            var h = Hwnd;
            if (h == IntPtr.Zero) return;

            // Restauration sans activation : la zone ne doit pas voler le focus quand Windows affiche le bureau.
            if (IsIconic(h)) ShowWindow(h, 4); // SW_SHOWNOACTIVATE
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; // repli WPF
            if (IsIconic(h)) ShowWindow(h, 8); // SW_SHOWNA : dernier repli, toujours sans activation
        }

        private void SendToBottom()
        {
            var h = Hwnd;
            if (h == IntPtr.Zero) return;
            // HWND_BOTTOM retire aussi l'état « toujours au-dessus » s'il était actif.
            SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
        }

        // « Afficher le bureau » ne minimise pas les fenêtres sur Windows 10/11 : le shell remonte
        // le bureau au-dessus de tout le reste. Un simple HWND_TOP est annulé immédiatement par
        // Explorer ; il faut donc passer dans la bande topmost tant que le bureau est affiché.
        private void RaiseAboveDesktop(IntPtr h)
        {
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        }

        private void ClearTopmost(IntPtr h)
        {
            SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); // HWND_NOTOPMOST
        }

        private void BringToTopForHover()
        {
            var h = Hwnd;
            if (h == IntPtr.Zero) return;

            // HWND_TOP : remonte la zone sans lui donner le focus.
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }

        // Appelé régulièrement par le gestionnaire : la zone doit rester visible même quand Windows affiche le bureau
        // (bouton « Afficher le bureau » en bas à droite, Win+D, Win+M).
        public void EnsureDesktopVisibility(IntPtr desktopHost, bool desktopForeground)
        {
            var h = Hwnd;
            if (h == IntPtr.Zero) return;

            if (IsIconic(h) || WindowState == WindowState.Minimized)
            {
                Diag.Log($"zone « {Data.Title} » réduite par Windows -> restauration");
                // Restauration sans activation (pas de vol de focus), avec repli si Windows insiste.
                if (IsIconic(h)) ShowWindow(h, 4); // SW_SHOWNOACTIVATE
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (IsIconic(h)) ShowWindow(h, 8); // SW_SHOWNA
            }
            else if (!IsWindowVisible(h))
            {
                Diag.Log($"zone « {Data.Title} » masquée par Windows -> réaffichage");
                ShowWindow(h, 4); // SW_SHOWNOACTIVATE
            }

            if (desktopHost != IntPtr.Zero && DesktopHelper.IsAbove(desktopHost, h))
            {
                // Le bureau a été remonté au-dessus de nos zones (« Afficher le bureau ») : on passe
                // dans la bande topmost, sinon le shell nous replace sous le bureau à chaque passe.
                RaiseAboveDesktop(h);
                if (!_raisedForDesktop) Diag.Log($"bureau au-dessus de la zone « {Data.Title} » -> zone remontée au-dessus du bureau");
                _raisedForDesktop = true;
            }
            else if (_raisedForDesktop && !desktopForeground)
            {
                // Une application a repris le focus : la zone quitte la bande topmost et retourne à l'arrière-plan.
                _raisedForDesktop = false;
                Diag.Log($"retour à l'arrière-plan de la zone « {Data.Title} »");
                ClearTopmost(h);
                SendToBottom();
            }
        }

        // ---------- Apparence / état (avec animation du Roll) ----------
        private void ApplyState(bool animate = false)
        {
            _suspendSize = true;
            int id = ++_animId;
            TitleBar.Visibility = Data.TitleVisible ? Visibility.Visible : Visibility.Collapsed;
            bool rolled = IsRolled;
            SearchBar.Visibility = !rolled && _manager.Settings.ShowSearchBar ? Visibility.Visible : Visibility.Collapsed;
            Handles.Visibility = (Data.Locked || rolled) ? Visibility.Collapsed : Visibility.Visible;
            double target = rolled ? TitleHeight + 2 : Math.Max(Data.Height, 60);

            BeginAnimation(HeightProperty, null);
            var s = _manager.Settings;
            double from = ActualHeight;
            if (animate && s.AnimateRoll && IsLoaded && IsVisible && from > 0 && Math.Abs(from - target) > 1)
            {
                ContentScroll.Visibility = Visibility.Visible; // le contenu reste visible pendant l'animation
                MinHeight = 0;
                Height = from;
                var anim = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(Math.Clamp(s.RollAnimationMs, 50, 1000)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (_, _) => { if (id == _animId) FinishState(rolled, target); };
                BeginAnimation(HeightProperty, anim);
            }
            else FinishState(rolled, target);
        }

        private void FinishState(bool rolled, double target)
        {
            ContentScroll.Visibility = rolled ? Visibility.Collapsed : Visibility.Visible;
            if (rolled) { MinHeight = 0; Height = target; }
            else { Height = target; MinHeight = 60; }
            BeginAnimation(HeightProperty, null);
            UpdateTitle();
            UpdatePager();
            ApplyColors();
            // Les SizeChanged provoqués par ce changement d'état sont ignorés (ils ne doivent pas écraser la taille mémorisée).
            Dispatcher.BeginInvoke(new Action(() => _suspendSize = false), DispatcherPriority.ContextIdle);
        }

        private void UpdateTitle()
        {
            var t = Data.Title;
            if (IsPortal && _portalCurrent != null && !SamePath(_portalCurrent, Data.PortalPath!))
                t += " › " + IOPath.GetFileName(_portalCurrent.TrimEnd('\\'));
            TitleText.Text = t;
            TitleIcon.Data = IsPortal ? PortalIcon : NestIcon;
            BackButton.Visibility = IsPortal && _portalHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyColors()
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(Data.Color); }
            catch { c = Color.FromRgb(30, 30, 46); }
            byte a = (byte)Math.Round(Math.Clamp(Data.Opacity, 0.1, 1.0) * 255);
            Root.Background = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));

            var s = _manager.Settings;
            double r = Math.Clamp(s.CornerRadius, 0, 24), ri = Math.Max(0, r - 1);
            Root.CornerRadius = new CornerRadius(r);
            Root.BorderThickness = new Thickness(s.ShowBorder ? 1 : 0);
            TitleBar.CornerRadius = IsRolled ? new CornerRadius(ri) : new CornerRadius(ri, ri, 0, 0);
        }

        // Polices propres à chaque zone (vide = police par défaut de Windows).
        private void ApplyFonts()
        {
            // Police et taille du titre
            if (string.IsNullOrWhiteSpace(Data.TitleFont))
            {
                TitleText.ClearValue(TextBlock.FontFamilyProperty);
                TitleEdit.ClearValue(Control.FontFamilyProperty);
            }
            else
            {
                var ff = new FontFamily(Data.TitleFont);
                TitleText.FontFamily = ff;
                TitleEdit.FontFamily = ff;
            }

            TitleText.FontSize = Data.TitleFontSize;
            TitleEdit.FontSize = Data.TitleFontSize;

            // Police et taille des noms des icônes
            if (string.IsNullOrWhiteSpace(Data.ItemFont))
            {
                ItemsHost.ClearValue(Control.FontFamilyProperty);
            }
            else
            {
                ItemsHost.FontFamily = new FontFamily(Data.ItemFont);
            }

            ItemsHost.FontSize = Data.ItemFontSize;
        }

        // Affichage Grille / Liste, propre à chaque zone.
        private void ApplyViewMode()
        {
            _lastListView = Data.ListView;
            ItemsHost.ItemsPanel = (ItemsPanelTemplate)FindResource(Data.ListView ? "ListItemsPanel" : "GridItemsPanel");
            ItemsHost.ItemTemplate = (DataTemplate)FindResource(Data.ListView ? "ListItemTemplate" : "GridItemTemplate");
            Marker.Width = Data.ListView ? Math.Max(ItemsHost.ActualWidth, 40) : 2;
            Marker.Height = Data.ListView ? 2 : Math.Max(Data.IconSize + 10, 12);
        }

        private void SetListView(bool list)
        {
            if (Data.ListView == list) return;
            Data.ListView = list;
            ApplyViewMode();
            _manager.RequestSave();
        }

        // ---------- Recherche : filtre temporaire des éléments affichés ----------
        private bool NameMatches(string name)
        {
            var query = _searchText.Trim();
            if (query.Length == 0) return true;
            // Ignore la casse et les accents (« ecole » trouve « école »).
            return CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, query,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        private void UpdateSearchUi()
        {
            bool empty = string.IsNullOrEmpty(SearchBox.Text);
            SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        // Vide la recherche sans déclencher d'actualisation (l'appelant en lance une).
        private void ResetSearch()
        {
            if (SearchBox.Text.Length == 0 && _searchText.Length == 0) return;
            _updatingSearch = true;
            SearchBox.Clear();
            _updatingSearch = false;
            _searchText = "";
            UpdateSearchUi();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_searchReady || _updatingSearch) return;
            _searchText = SearchBox.Text;
            UpdateSearchUi();
            _portalPage = 0;
            RefreshItems();
            ContentScroll.ScrollToTop();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else ClearSelection();
            e.Handled = true;
        }

        private void SearchClear_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        // Appelé quand les paramètres ou la zone ont été modifiés depuis la fenêtre de paramètres.
        public void ApplyExternalChange()
        {
            StopHover();
            _hoverExpanded = false;
            // Masquer la barre de recherche ne doit pas laisser un filtre invisible actif.
            if (!_manager.Settings.ShowSearchBar && HasFilter)
            {
                ResetSearch();
                RefreshItems();
            }
            ApplyState();
            ApplyFonts();
            bool viewChanged = _lastListView != Data.ListView;
            if (viewChanged) ApplyViewMode();
            if (_lastIconSize != Data.IconSize || _lastItemFontSize != Data.ItemFontSize || viewChanged)
                RefreshItems();
        }

        public void ReloadItems() => RefreshItems();

        // ---------- Contenu : zone classique ou Folder Portal ----------
        private static bool SamePath(string a, string b) =>
            string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        private List<string>? GetOrder(string folder)
        {
            foreach (var kv in Data.PortalOrder) if (SamePath(kv.Key, folder)) return kv.Value;
            return null;
        }

        private void SetOrder(string folder, List<string> names)
        {
            foreach (var k in Data.PortalOrder.Keys.Where(k => SamePath(k, folder)).ToList()) Data.PortalOrder.Remove(k);
            Data.PortalOrder[folder] = names;
        }

        private List<ItemVM> BuildClassicItems()
        {
            var list = new List<ItemVM>(Data.Items.Count);
            foreach (var p in Data.Items)
            {
                if (HasFilter && !NameMatches(ItemVM.GetName(p))) continue;
                list.Add(new ItemVM(p, Data.IconSize));
            }
            return list;
        }

        // Énumération d'un dossier : exécutée hors du thread UI (un dossier
        // volumineux ou un lecteur réseau ne doit pas figer la zone).
        private static List<string>? ScanPortal(string folder, List<string>? custom)
        {
            try
            {
                var entries = Directory.EnumerateFileSystemEntries(folder).Where(p =>
                {
                    try { return (File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                    catch { return false; }
                }).ToList();

                int Rank(string p)
                {
                    if (custom == null) return int.MaxValue;
                    int i = custom.FindIndex(n => string.Equals(n, IOPath.GetFileName(p), StringComparison.OrdinalIgnoreCase));
                    return i < 0 ? int.MaxValue : i;
                }

                return entries.OrderBy(Rank)
                              .ThenBy(p => Directory.Exists(p) ? 0 : 1)
                              .ThenBy(p => IOPath.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                              .ToList();
            }
            catch (Exception ex)
            {
                Diag.Log("portail : " + ex.Message);
                return null;
            }
        }

        private void RefreshItems()
        {
            // Reconstruire la liste pendant une saisie ferait perdre le champ de saisie et le texte en cours.
            if (_editingTitle || _editingItem != null)
            {
                _refreshPending = true;
                return;
            }
            _refreshPending = false;

            int version = ++_refreshVersion;

            if (!IsPortal)
            {
                ApplyItems(BuildClassicItems(), 1, 0, null);
                return;
            }

            _ = RefreshPortalItemsAsync(version);
        }

        private async Task RefreshPortalItemsAsync(int version)
        {
            var cur = CurrentFolder;
            if (!SamePath(cur, _portalPageFolder ?? "")) _portalPage = 0; // nouveau dossier : retour en première page
            _portalPageFolder = cur;
            int page = _portalPage;
            var custom = GetOrder(cur)?.ToList();
            bool filtering = HasFilter;
            bool includeParent = !filtering && !SamePath(cur, Data.PortalPath!);

            var ordered = await Task.Run(() => ScanPortal(cur, custom));
            if (_closed || version != _refreshVersion || !SamePath(CurrentFolder, cur)) return;

            if (ordered == null)
            {
                ApplyItems(new List<ItemVM>(), 1, 0, null);
                return;
            }

            // Le filtre porte sur le dossier complet avant la pagination : toutes les correspondances
            // restent visibles, même si elles se trouvent au-delà de la première page.
            if (filtering)
                ordered = ordered.Where(p => NameMatches(ItemVM.GetName(p))).ToList();

            int pageCount = Math.Max(1, (ordered.Count + PortalPageSize - 1) / PortalPageSize);
            int currentPage = Math.Clamp(page, 0, pageCount - 1);
            _portalPage = currentPage;
            int start = currentPage * PortalPageSize;

            var list = new List<ItemVM>();
            if (includeParent)
            {
                var parent = Directory.GetParent(cur.TrimEnd('\\'));
                if (parent != null) list.Add(new ItemVM(parent.FullName, Data.IconSize, true, true));
            }

            int count = Math.Min(PortalPageSize, ordered.Count - start);
            for (int i = 0; i < count; i++)
                list.Add(new ItemVM(ordered[start + i], Data.IconSize, true));

            // Une saisie a pu commencer pendant la lecture du dossier.
            if (_editingTitle || _editingItem != null)
            {
                _refreshPending = true;
                return;
            }

            ApplyItems(list, pageCount, start, ordered);
        }

        private void ApplyItems(List<ItemVM> items, int pageCount, int pageStart, List<string>? pageNames)
        {
            _portalPageCount = pageCount;
            _portalPageStart = pageStart;
            _portalPageNames = pageNames;
            _vms = items;
            _lastIconSize = Data.IconSize;
            _lastItemFontSize = Data.ItemFontSize;
            _selected.RemoveWhere(p => !_vms.Any(v => SamePath(v.FullPath, p)));
            foreach (var v in _vms) v.IsSelected = _selected.Contains(v.FullPath);
            _anchor = null; _pendingCollapse = null; _dragCandidate = null; _renameCandidate = null;
            ItemsHost.ItemsSource = _vms;
            UpdatePager();
        }

        private void UpdatePager()
        {
            bool show = IsPortal && _portalPageCount > 1 && !IsRolled;
            PagerBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            PagerText.Text = Loc.F("portal.page", _portalPage + 1, _portalPageCount);
            PagerPrev.IsEnabled = _portalPage > 0;
            PagerNext.IsEnabled = _portalPage < _portalPageCount - 1;
        }

        private void PagerPrev_Click(object sender, RoutedEventArgs e) => ChangePage(-1);
        private void PagerNext_Click(object sender, RoutedEventArgs e) => ChangePage(1);

        private void ChangePage(int delta)
        {
            int target = Math.Clamp(_portalPage + delta, 0, _portalPageCount - 1);
            if (target == _portalPage) return;
            _portalPage = target;
            RefreshItems();
            ContentScroll.ScrollToTop();
        }

        private void FlushPendingRefresh()
        {
            if (!_refreshPending) return;
            _refreshPending = false;
            RefreshItems();
        }

        // Une zone classique ne surveille pas ses fichiers : on vérifie ses références à l'activation.
        // Les vérifications se font hors du thread UI (un lecteur réseau lent ne doit pas figer la zone).
        // Si le dossier parent a disparu (lecteur indisponible...), on ne touche à rien.
        private async void RevalidateClassicItems()
        {
            if (IsPortal || _editingTitle || _editingItem != null || _dragging || _menuOpen) return;

            var snapshot = Data.Items.ToList();
            if (snapshot.Count == 0) return;

            var missing = await Task.Run(() => snapshot.Where(IsMissingLocally).ToList());
            if (missing.Count == 0) return;
            if (IsPortal || _editingTitle || _editingItem != null || _dragging || _menuOpen) return;

            bool changed = false;
            foreach (var p in missing)
            {
                if (!Data.Items.Any(q => SamePath(q, p))) continue;
                Data.Items.RemoveAll(q => SamePath(q, p));
                _selected.Remove(p);
                changed = true;
            }

            if (!changed) return;
            Diag.Log($"zone « {Data.Title} » : références disparues retirées");
            _manager.RequestSave();
            RefreshItems();
        }

        private static bool IsMissingLocally(string path)
        {
            var parent = IOPath.GetDirectoryName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return false;
            return !File.Exists(path) && !Directory.Exists(path);
        }

        private void ResetWatcher()
        {
            _watcher?.Dispose();
            _watcher = null;
            if (!IsPortal) return;
            var cur = CurrentFolder;
            if (!Directory.Exists(cur)) return;
            try
            {
                _watcher = new FileSystemWatcher(cur)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                    EnableRaisingEvents = true
                };
                FileSystemEventHandler h = (_, _) => ScheduleRefresh();
                _watcher.Created += h;
                _watcher.Deleted += h;
                _watcher.Changed += h;
                _watcher.Renamed += (_, _) => ScheduleRefresh();
                _watcher.Error += (_, e) =>
                {
                    // Débordement du tampon ou dossier inaccessible : on recrée le surveillant et on relit le dossier.
                    Diag.Log("portail : " + e.GetException().Message);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ResetWatcher();
                        ScheduleRefresh();
                    }), DispatcherPriority.Background);
                };
            }
            catch (Exception ex) { Diag.Log("portail : " + ex.Message); }
        }

        private void ScheduleRefresh() =>
            Dispatcher.BeginInvoke(new Action(() => { _portalTimer.Stop(); _portalTimer.Start(); }));

        private void NavigateTo(string folder, bool addHistory = true)
        {
            if (!IsPortal || !Directory.Exists(folder) || SamePath(folder, CurrentFolder)) return;
            if (addHistory && !string.IsNullOrEmpty(CurrentFolder)) _portalHistory.Add(CurrentFolder);
            _portalCurrent = SamePath(folder, Data.PortalPath!) ? null : folder;
            ResetSearch();
            _portalPage = 0;
            ResetWatcher();
            UpdateTitle();
            RefreshItems();
        }

        // Retour au dossier précédent ; les dossiers supprimés entre-temps sont ignorés.
        private void NavigateBack()
        {
            if (!IsPortal) return;
            while (_portalHistory.Count > 0)
            {
                int last = _portalHistory.Count - 1;
                var folder = _portalHistory[last];
                _portalHistory.RemoveAt(last);
                if (!Directory.Exists(folder)) continue;
                _portalCurrent = SamePath(folder, Data.PortalPath!) ? null : folder;
                ResetSearch();
                _portalPage = 0;
                ResetWatcher();
                UpdateTitle();
                RefreshItems();
                return;
            }
            UpdateTitle();
        }

        // ---------- Éléments ----------
        public void AddItem(string path, int index = -1)
        {
            if (IsPortal) return;
            if (Data.Items.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return;
            if (index < 0 || index > Data.Items.Count) Data.Items.Add(path);
            else Data.Items.Insert(index, path);
            Dispatcher.BeginInvoke(new Action(RefreshItems));
            _manager.RequestSave();
        }

        public bool RemoveItem(string path, bool restoreOnDesktop)
        {
            int i = Data.Items.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            Data.Items.RemoveAt(i);
            _selected.Remove(path);
            Dispatcher.BeginInvoke(new Action(RefreshItems));
            if (restoreOnDesktop) _manager.RestoreOnDesktop(path);
            _manager.RequestSave();
            return true;
        }

        // Déplace un ou plusieurs éléments (ordre relatif conservé) à la position donnée.
        private void ReorderTo(List<string> paths, int index)
        {
            // Pendant une recherche, la liste affichée est partielle : réenregistrer son ordre
            // ferait perdre les éléments masqués. Le tri reste donc désactivé tant que le filtre est actif.
            if (HasFilter) return;

            var moving = _vms.Where(v => !v.IsUp && paths.Any(p => SamePath(p, v.FullPath))).ToList();
            if (moving.Count == 0) return;
            bool hasUp = _vms.Count > 0 && _vms[0].IsUp;
            if (hasUp && index < 1) index = 1;
            index = Math.Clamp(index, 0, _vms.Count);

            int before = 0;
            for (int i = 0; i < index && i < _vms.Count; i++) if (moving.Contains(_vms[i])) before++;
            foreach (var v in moving) _vms.Remove(v);
            int at = Math.Clamp(index - before, 0, _vms.Count);
            _vms.InsertRange(at, moving);

            if (IsPortal)
            {
                var pageNames = _vms.Where(v => !v.IsUp).Select(v => IOPath.GetFileName(v.FullPath)).ToList();
                var full = _portalPageNames?.ToList();
                // Remplace uniquement la tranche de la page courante : les autres pages gardent leur ordre.
                if (full != null && _portalPageStart >= 0 && _portalPageStart + pageNames.Count <= full.Count)
                {
                    for (int i = 0; i < pageNames.Count; i++) full[_portalPageStart + i] = pageNames[i];
                    SetOrder(CurrentFolder, full);
                }
                else SetOrder(CurrentFolder, pageNames);
            }
            else
                Data.Items = _vms.Select(v => v.FullPath).ToList();

            _manager.RequestSave();
            Dispatcher.BeginInvoke(new Action(RefreshItems));
        }

        private void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Directory.Exists(path) ? path : (IOPath.GetDirectoryName(path) ?? "")
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("msg.cannotOpen", path, ex.Message), "DeskNest",
                    MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, Loc.MsgOptions);
            }
        }

        private void ActivateItem(ItemVM vm)
        {
            if (IsPortal && vm.IsFolder)
            {
                var target = vm.FullPath;
                Dispatcher.BeginInvoke(new Action(() => NavigateTo(target)));
                return;
            }
            OpenPath(vm.FullPath);
        }

        // ---------- Actions façon Windows sur la sélection : copier, couper, coller, renommer, supprimer, propriétés ----------
        private void OpenSelected()
        {
            foreach (var v in SelectedVms()) OpenPath(v.FullPath);
        }

        private void CopySelected(bool cut)
        {
            var paths = SelectedVms().Select(v => v.FullPath).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (paths.Count == 0) return;
            FileClipboard.Write(paths, cut, cut ? Data.Id : null);
        }

        private void CopyPathSelected()
        {
            var paths = SelectedVms().Select(v => "\"" + v.FullPath + "\"").ToList();
            if (paths.Count > 0) FileClipboard.WriteText(string.Join("\r\n", paths));
        }

        // Coller : dans un Folder Portal les fichiers sont réellement copiés / déplacés dans le dossier ;
        // dans une zone classique on ajoute des références (un « Couper » de DeskNest déplace les références).
        private void PasteHere(string? intoFolder)
        {
            var clip = FileClipboard.Read();
            if (clip == null) return;
            if (IsPortal)
            {
                _manager.DropIntoFolder(clip.Paths, intoFolder ?? CurrentFolder, !clip.Cut, clip.SourceNest);
                if (clip.Cut) FileClipboard.Clear();
            }
            else if (clip.Cut && clip.SourceNest.HasValue)
            {
                _manager.MoveItems(clip.SourceNest.Value, clip.Paths, this, -1);
                FileClipboard.Clear();
            }
            else AddDroppedFiles(clip.Paths, -1);
        }

        private string? PasteTargetFromSelection()
        {
            var sel = SelectedVms();
            return IsPortal && sel.Count == 1 && sel[0].IsFolder ? sel[0].FullPath : null;
        }

        private void RenameSelected()
        {
            var sel = SelectedVms();
            if (sel.Count == 1) BeginItemEdit(sel[0]);
        }

        private void DeleteSelected()
        {
            var targets = SelectedVms();
            if (targets.Count == 0) return;
            var msg = targets.Count == 1 ? Loc.F("msg.delete1", targets[0].Name) : Loc.F("msg.deleteN", targets.Count);
            var r = MessageBox.Show(this, msg, "DeskNest", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No, Loc.MsgOptions);
            if (r != MessageBoxResult.Yes) return;
            _manager.DeleteItems(targets.Select(t => t.FullPath).ToList());
        }

        private void PropertiesSelected()
        {
            var first = SelectedVms().FirstOrDefault();
            if (first != null) ShellActions.Properties(first.FullPath);
        }

        // ---------- Renommer un élément sur place (clic sur le nom, F2, menu) ----------
        private static bool IsInsideTextBox(object? source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is TextBox) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void CancelRenameTimer()
        {
            _renameTimer?.Stop();
            _pendingRename = null;
        }

        // Comme dans l'Explorateur : un clic sur le nom d'un élément déjà sélectionné lance le renommage après le délai de double-clic.
        private void ScheduleRename(ItemVM vm)
        {
            _renameTimer ??= new DispatcherTimer();
            _renameTimer.Stop();
            _renameTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime() + 80);
            _renameTimer.Tick -= RenameTimerTick;
            _renameTimer.Tick += RenameTimerTick;
            _pendingRename = vm;
            _renameTimer.Start();
        }

        private void RenameTimerTick(object? sender, EventArgs e)
        {
            _renameTimer?.Stop();
            var vm = _pendingRename;
            _pendingRename = null;
            if (vm != null && vm.IsSelected && SelectedVms().Count == 1 && _editingItem == null) BeginItemEdit(vm);
        }

        private void BeginItemEdit(ItemVM vm)
        {
            if (vm.IsUp || _editingItem != null) return;
            CancelRenameTimer();
            EndTitleEdit(true);
            // Une actualisation différée peut avoir reconstruit la liste : on retrouve l'élément par son chemin.
            var current = _vms.FirstOrDefault(v => !v.IsUp && SamePath(v.FullPath, vm.FullPath));
            if (current == null) return;
            vm = current;
            _editingItem = vm;
            vm.EditText = vm.Name; // sans l'extension (comme l'Explorateur quand les extensions sont masquées)
            vm.IsEditing = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ItemsHost.ItemContainerGenerator.ContainerFromItem(vm) is ContentPresenter cp)
                {
                    cp.ApplyTemplate();
                    if (cp.ContentTemplate?.FindName("NameEdit", cp) is TextBox tb)
                    {
                        tb.Focus();
                        Keyboard.Focus(tb);
                        tb.SelectAll();
                    }
                }
            }), DispatcherPriority.Input);
        }

        private void EndItemEdit(bool commit)
        {
            var vm = _editingItem;
            if (vm == null) return;
            _editingItem = null;
            var text = (vm.EditText ?? "").Trim();
            vm.IsEditing = false;
            try
            {
                if (!commit || text.Length == 0 || text == vm.Name) return;

                // Comme l'Explorateur : si l'utilisateur a saisi l'extension, on ne la rajoute pas une deuxième fois.
                var ext = vm.IsFolder ? "" : IOPath.GetExtension(vm.FullPath);
                var newName = vm.IsFolder || string.IsNullOrEmpty(ext) || text.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                    ? text
                    : text + ext;
                if (string.Equals(newName, IOPath.GetFileName(vm.FullPath), StringComparison.Ordinal)) return;
                if (!NestManager.IsValidItemName(newName))
                {
                    MessageBox.Show(this, Loc.T("msg.renameInvalid"), "DeskNest", MessageBoxButton.OK, MessageBoxImage.Warning,
                        MessageBoxResult.OK, Loc.MsgOptions);
                    return;
                }
                _manager.RenameItem(vm.FullPath, newName);
            }
            finally
            {
                FlushPendingRefresh();
            }
        }

        private void NameEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { EndItemEdit(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndItemEdit(false); e.Handled = true; }
        }

        private void NameEdit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndItemEdit(true);

        // ---------- Titre : bouton menu, modification par double-clic ----------
        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var m = Root.ContextMenu;
            if (m == null) return;
            m.PlacementTarget = MenuButton;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBack();

        private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return; // simple clic : déplacement de la zone (géré par Root)
            e.Handled = true;
            BeginTitleEdit();
        }

        private void BeginTitleEdit()
        {
            if (!Data.TitleVisible || _editingTitle) return;
            EndItemEdit(true);
            _editingTitle = true;
            TitleEdit.Text = Data.Title;
            TitleText.Visibility = Visibility.Collapsed;
            TitleEdit.Visibility = Visibility.Visible;
            TitleEdit.Focus();
            Keyboard.Focus(TitleEdit);
            TitleEdit.SelectAll();
        }

        private void EndTitleEdit(bool commit)
        {
            if (!_editingTitle) return;
            _editingTitle = false;
            if (commit && !string.IsNullOrWhiteSpace(TitleEdit.Text) && TitleEdit.Text.Trim() != Data.Title)
            {
                Data.Title = TitleEdit.Text.Trim();
                _manager.RequestSave();
            }
            TitleEdit.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
            UpdateTitle();
            FlushPendingRefresh();
        }

        private void TitleEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { EndTitleEdit(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndTitleEdit(false); e.Handled = true; }
        }

        private void TitleEdit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndTitleEdit(true);

        // ---------- Sélection multiple ----------
        private void SetSelected(ItemVM vm, bool on)
        {
            vm.IsSelected = on;
            if (on) _selected.Add(vm.FullPath); else _selected.Remove(vm.FullPath);
        }

        private void ClearSelection()
        {
            foreach (var v in _vms) v.IsSelected = false;
            _selected.Clear();
            _anchor = null;
        }

        private void SelectOnly(ItemVM vm)
        {
            ClearSelection();
            SetSelected(vm, true);
            _anchor = vm;
        }

        private void SelectAll()
        {
            foreach (var v in _vms) if (!v.IsUp) SetSelected(v, true);
        }

        private List<ItemVM> SelectedVms() => _vms.Where(v => v.IsSelected && !v.IsUp).ToList();

        private void RangeSelect(ItemVM a, ItemVM b, bool additive)
        {
            int i1 = _vms.IndexOf(a), i2 = _vms.IndexOf(b);
            if (i1 < 0 || i2 < 0) { SelectOnly(b); return; }
            if (!additive) { var keep = _anchor; ClearSelection(); _anchor = keep; }
            for (int i = Math.Min(i1, i2); i <= Math.Max(i1, i2); i++)
                if (!_vms[i].IsUp) SetSelected(_vms[i], true);
        }

        private void RemoveSelected()
        {
            foreach (var v in SelectedVms().Where(v => v.CanRemove).ToList()) RemoveItem(v.FullPath, true);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_editingTitle || _editingItem != null) return; // les touches appartiennent au champ de saisie
            if (SearchBox.IsKeyboardFocusWithin) return;        // frappe dans la barre de recherche
            CancelRenameTimer();
            var mods = Keyboard.Modifiers;

            // Folder Portal : Alt+← ou Retour arrière pour revenir au dossier précédent.
            if (IsPortal && mods == ModifierKeys.None && e.Key == Key.Back) { NavigateBack(); e.Handled = true; return; }
            if (IsPortal && mods == ModifierKeys.Alt && (e.Key == Key.Left || e.SystemKey == Key.Left)) { NavigateBack(); e.Handled = true; return; }

            if (mods == ModifierKeys.Control && e.Key == Key.A) { SelectAll(); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.C) { CopySelected(false); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.X) { CopySelected(true); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.V) { PasteHere(PasteTargetFromSelection()); e.Handled = true; }
            else if (e.Key == Key.F2) { RenameSelected(); e.Handled = true; }
            else if (e.Key == Key.Delete)
            {
                // Portail ou Maj+Suppr : suppression réelle (Corbeille). Zone classique : on retire seulement de la zone.
                if (IsPortal || mods == ModifierKeys.Shift) DeleteSelected(); else RemoveSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { ClearSelection(); e.Handled = true; }
            else if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        }

        // Bouton « précédent » de la souris (XButton1) : retour dans un Folder Portal.
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.XButton1 || !IsPortal) return;
            NavigateBack();
            e.Handled = true;
        }

        private List<Rect> GetItemRects()
        {
            var rects = new List<Rect>(_vms.Count);
            for (int i = 0; i < _vms.Count; i++)
            {
                if (ItemsHost.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe && fe.ActualWidth > 0)
                {
                    var tl = fe.TranslatePoint(new Point(0, 0), ItemsHost);
                    rects.Add(new Rect(tl.X, tl.Y, fe.ActualWidth, fe.ActualHeight));
                }
                else rects.Add(Rect.Empty);
            }
            return rects;
        }

        // Sélection par rectangle (glisser dans une zone vide du contenu).
        private void StartMarquee(MouseButtonEventArgs e)
        {
            Focus();
            _marquee = true;
            _marqueeStart = e.GetPosition(ItemsHost);
            bool additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _marqueeBase = additive
                ? new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!additive) ClearSelection();
            Root.CaptureMouse();
        }

        private void UpdateMarquee(Point p)
        {
            var r = new Rect(_marqueeStart, p);
            if (SelBox.Visibility != Visibility.Visible && r.Width < 4 && r.Height < 4) return;
            ShowBox(r);

            var rects = GetItemRects();
            for (int i = 0; i < _vms.Count; i++)
            {
                var vm = _vms[i];
                if (vm.IsUp) continue;
                bool on = _marqueeBase.Contains(vm.FullPath) || (!rects[i].IsEmpty && r.IntersectsWith(rects[i]));
                if (vm.IsSelected != on) SetSelected(vm, on);
            }
        }

        private void EndMarquee()
        {
            if (!_marquee) return;
            _marquee = false;
            SelBox.Visibility = Visibility.Collapsed;
            Root.ReleaseMouseCapture();
        }

        // Rectangle (coordonnées de ItemsHost) dessiné dans la couche de superposition.
        private void ShowBox(Rect r)
        {
            var tl = ItemsHost.TranslatePoint(r.TopLeft, Overlay);
            Canvas.SetLeft(SelBox, tl.X);
            Canvas.SetTop(SelBox, tl.Y);
            SelBox.Width = r.Width;
            SelBox.Height = r.Height;
            SelBox.Visibility = Visibility.Visible;
        }

        // ---------- Souris : déplacement de la zone, sélection, éléments ----------
        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            if (IsInsideTextBox(e.OriginalSource)) return; // clic dans un champ de saisie (titre ou nom d'élément)
            CancelRenameTimer();
            if (_editingTitle) EndTitleEdit(true);
            if (_editingItem != null) EndItemEdit(true);
            bool onTitle = TitleBar.IsVisible && TitleBar.IsMouseOver;
            if (e.ClickCount == 2 && onTitle) { ToggleRollUp(); e.Handled = true; return; } // double-clic hors du texte du titre
            if (e.ClickCount != 1) return;

            // Zone de contenu (titre visible) : sélection rectangulaire. Sinon : déplacement de la zone.
            if (Data.TitleVisible && ContentScroll.IsVisible && ContentScroll.IsMouseOver && !onTitle)
            {
                StartMarquee(e);
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) ClearSelection();
            if (Data.Locked) return;
            var h = Hwnd;
            if (h == IntPtr.Zero) return;
            // Déplacement manuel (pas de DragMove) : pas d'aimantation aux bords, pas de maximisation.
            GetCursorPos(out _curStart);
            GetWindowRect(h, out var rect);
            _winX = rect.Left; _winY = rect.Top;
            _moving = true;
            Root.CaptureMouse();
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (_marquee)
            {
                if (e.LeftButton != MouseButtonState.Pressed) { EndMarquee(); return; }
                UpdateMarquee(e.GetPosition(ItemsHost));
                return;
            }
            if (!_moving) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndMove(); return; }
            GetCursorPos(out var c);
            // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
            SetWindowPos(Hwnd, IntPtr.Zero, _winX + c.X - _curStart.X, _winY + c.Y - _curStart.Y, 0, 0, 0x1 | 0x4 | 0x10);
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndMarquee();
            EndMove();
        }

        private void Root_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _moving = false;
            if (_marquee) { _marquee = false; SelBox.Visibility = Visibility.Collapsed; }
        }

        private void EndMove()
        {
            if (!_moving) return;
            _moving = false;
            Root.ReleaseMouseCapture();
        }

        // ---------- Redimensionnement manuel (tous les côtés et coins) ----------
        // Tag : 1 = gauche, 2 = haut, 4 = droite, 8 = bas (les coins combinent deux valeurs).
        private void Handle_Down(object sender, MouseButtonEventArgs e)
        {
            if (Data.Locked || IsRolled || sender is not FrameworkElement fe) return;
            _edge = int.Parse((string)fe.Tag);
            GetCursorPos(out _curStart);
            GetWindowRect(Hwnd, out _startRect);
            _resizing = true;
            fe.CaptureMouse();
            e.Handled = true;
        }

        private void Handle_Move(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndResize(sender); return; }
            GetCursorPos(out var c);
            int dx = c.X - _curStart.X, dy = c.Y - _curStart.Y;
            var dpi = VisualTreeHelper.GetDpi(this);
            int minW = (int)Math.Ceiling(120 * dpi.DpiScaleX), minH = (int)Math.Ceiling(60 * dpi.DpiScaleY);
            int x = _startRect.Left, y = _startRect.Top;
            int w = _startRect.Right - x, h = _startRect.Bottom - y;
            if ((_edge & 1) != 0) { int nw = Math.Max(minW, w - dx); x += w - nw; w = nw; }
            if ((_edge & 4) != 0) w = Math.Max(minW, w + dx);
            if ((_edge & 2) != 0) { int nh = Math.Max(minH, h - dy); y += h - nh; h = nh; }
            if ((_edge & 8) != 0) h = Math.Max(minH, h + dy);
            SetWindowPos(Hwnd, IntPtr.Zero, x, y, w, h, 0x4 | 0x10); // SWP_NOZORDER | SWP_NOACTIVATE
        }

        private void Handle_Up(object sender, MouseButtonEventArgs e) => EndResize(sender);
        private void Handle_LostCapture(object sender, MouseEventArgs e) => _resizing = false;

        private void EndResize(object sender)
        {
            if (!_resizing) return;
            _resizing = false;
            (sender as UIElement)?.ReleaseMouseCapture();
        }

        // ---------- Clic sur les éléments ----------
        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideTextBox(e.OriginalSource)) return; // le champ de renommage gère lui-même ses clics
            e.Handled = true; // empêche le déplacement de la zone et la sélection rectangulaire
            if (sender is not FrameworkElement fe || fe.DataContext is not ItemVM vm) return;
            if (_editingTitle) EndTitleEdit(true);
            if (_editingItem != null) EndItemEdit(true);
            CancelRenameTimer();
            Focus();
            _dragStart = e.GetPosition(null);
            _dragCandidate = null;
            _pendingCollapse = null;
            _renameCandidate = null;

            if (e.ClickCount == 2) { ActivateItem(vm); return; }
            if (vm.IsUp) { ClearSelection(); return; }

            var mods = Keyboard.Modifiers;
            bool wasSoleSelected = vm.IsSelected && SelectedVms().Count == 1;
            bool onName = e.OriginalSource is TextBlock tb && (tb.Tag as string) == "name";

            if ((mods & ModifierKeys.Shift) != 0 && _anchor != null) RangeSelect(_anchor, vm, (mods & ModifierKeys.Control) != 0);
            else if ((mods & ModifierKeys.Control) != 0) { SetSelected(vm, !vm.IsSelected); _anchor = vm; }
            else if (vm.IsSelected) _pendingCollapse = vm; // peut être le début d'un glisser de plusieurs éléments
            else SelectOnly(vm);

            if (vm.IsSelected) _dragCandidate = vm;
            if (wasSoleSelected && onName && mods == ModifierKeys.None) _renameCandidate = vm; // clic sur le nom : renommage possible
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var vm = (sender as FrameworkElement)?.DataContext as ItemVM;
            var renameCandidate = _renameCandidate;
            _renameCandidate = null;

            // Simple clic sur un élément déjà sélectionné (sans glisser) : la sélection se réduit à cet élément.
            if (_pendingCollapse != null && vm == _pendingCollapse) SelectOnly(_pendingCollapse);
            _pendingCollapse = null;
            _dragCandidate = null;

            if (renameCandidate != null && vm == renameCandidate) ScheduleRename(renameCandidate);
        }

        private void Item_RightDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ItemVM vm) return;
            if (IsInsideTextBox(e.OriginalSource)) return; // menu standard du champ de saisie
            CancelRenameTimer();
            if (!vm.IsUp && !vm.IsSelected) SelectOnly(vm);
            fe.ContextMenu = _itemMenu; // un seul menu partagé par tous les éléments
        }

        private void Item_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var vms = SelectedVms();
            if (vms.Count == 0 || sender is not FrameworkElement fe) return;
            _pendingCollapse = null;
            _dragCandidate = null;
            _renameCandidate = null;
            CancelRenameTimer();

            var paths = vms.Select(v => v.FullPath).ToList();
            var d = new DataObject();
            d.SetData(ItemFormat, $"{Data.Id}|{string.Join("\n", paths)}");
            // Les vrais chemins permettent aussi de déposer sur le bureau ou dans une fenêtre de l'Explorateur (déplacement).
            var real = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            if (real.Length > 0) d.SetData(DataFormats.FileDrop, real);

            var unhidden = _manager.UnhideForDrag(paths);
            s_internalDrop = false;
            _dragging = true;
            var effect = DragDropEffects.None;
            try { effect = DragDrop.DoDragDrop(fe, d, DragDropEffects.Move | DragDropEffects.Copy); }
            finally
            {
                _dragging = false;
                if (_hoverExpanded && !IsMouseOver) StartHover(HoverMode.Collapse, _manager.Settings.HoverCollapseDelayMs);
            }
            FinishDrag(paths, unhidden, effect);
        }

        // Fin d'un glisser : si l'élément a été déposé hors de DeskNest (bureau, Explorateur), on suit son déplacement réel.
        private void FinishDrag(List<string> paths, List<string> unhidden, DragDropEffects effect)
        {
            if (s_internalDrop || effect == DragDropEffects.None)
            {
                _manager.RehideAfterDrag(unhidden); // glisser annulé ou déposé dans une zone : rien n'a quitté DeskNest
                return;
            }

            bool moved = (effect & DragDropEffects.Move) != 0;
            var pending = paths.ToList();
            int ticks = 0;
            // L'Explorateur peut terminer l'opération bien après le glisser (gros fichier, volume lent) : 30 s de surveillance.
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (_, _) =>
            {
                ticks++;
                foreach (var p in pending.ToList())
                {
                    if (File.Exists(p) || Directory.Exists(p)) continue;
                    pending.Remove(p);               // le fichier a quitté son emplacement : il a été déplacé
                    _manager.ForgetStored(p);
                    if (!IsPortal) RemoveItem(p, false); // il n'est plus dans la zone
                }
                if (pending.Count == 0) { t.Stop(); return; }
                if (ticks < 60) return;

                t.Stop();
                // Après un déplacement, re-masquer un chemin encore présent peut masquer la copie déplacée
                // (Windows conserve les attributs) : on ne le fait que pour une copie ou une annulation.
                if (!moved)
                    _manager.RehideAfterDrag(unhidden.Where(p => pending.Any(q => SamePath(p, q))).ToList());
            };
            t.Start();
        }

        // ---------- Menu contextuel des éléments (façon Windows, agit sur toute la sélection) ----------
        private static MenuItem Mi(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            return mi;
        }

        private ContextMenu BuildItemMenu()
        {
            var m = new ContextMenu { FlowDirection = Loc.Flow };
            _imOpen = Mi(Loc.T("i.open"), OpenSelected);
            _imOpenWith = Mi(Loc.T("i.openWith"), () => { var v = SelectedVms().FirstOrDefault(); if (v != null) ShellActions.OpenWith(v.FullPath); });
            _imLocation = Mi(Loc.T("i.location"), () => { var v = SelectedVms().FirstOrDefault(); if (v != null) ShellActions.ShowInExplorer(v.FullPath); });
            _imCut = Mi(Loc.T("i.cut"), () => CopySelected(true));
            _imCopy = Mi(Loc.T("i.copy"), () => CopySelected(false));
            _imCopyPath = Mi(Loc.T("i.copyPath"), CopyPathSelected);
            _imPaste = Mi(Loc.T("paste"), () => PasteHere(PasteTargetFromSelection()));
            // Après la fermeture du menu, pour que le champ de saisie garde le focus.
            _imRename = Mi(Loc.T("i.rename"), () => Dispatcher.BeginInvoke(new Action(RenameSelected), DispatcherPriority.ContextIdle));
            _imDelete = Mi(Loc.T("i.delete"), DeleteSelected);
            _imRemove = Mi(Loc.T("i.remove"), RemoveSelected);
            _imProps = Mi(Loc.T("i.properties"), PropertiesSelected);

            m.Items.Add(_imOpen);
            m.Items.Add(_imOpenWith);
            m.Items.Add(_imLocation);
            m.Items.Add(new Separator());
            m.Items.Add(_imCut);
            m.Items.Add(_imCopy);
            m.Items.Add(_imCopyPath);
            m.Items.Add(_imPaste);
            m.Items.Add(new Separator());
            m.Items.Add(_imRename);
            m.Items.Add(_imDelete);
            m.Items.Add(_imRemove);
            m.Items.Add(new Separator());
            m.Items.Add(_imProps);

            m.Opened += (_, _) =>
            {
                _menuOpen = true;
                int n = SelectedVms().Count;
                bool any = n > 0;
                _imOpen.Header = n > 1 ? Loc.F("i.openN", n) : Loc.T("i.open");
                _imDelete.Header = n > 1 ? Loc.F("i.deleteN", n) : Loc.T("i.delete");
                _imRemove.Header = n > 1 ? Loc.F("i.removeN", n) : Loc.T("i.remove");
                _imRemove.Visibility = IsPortal ? Visibility.Collapsed : Visibility.Visible;
                foreach (var mi in new[] { _imOpen, _imOpenWith, _imLocation, _imCut, _imCopy, _imCopyPath, _imDelete, _imRemove, _imProps })
                    mi.IsEnabled = any;
                _imRename.IsEnabled = n == 1;
                _imPaste.IsEnabled = FileClipboard.HasFiles();
            };
            m.Closed += (_, _) =>
            {
                _menuOpen = false;
                if (_hoverExpanded && !IsMouseOver) StartHover(HoverMode.Collapse, _manager.Settings.HoverCollapseDelayMs);
            };
            return m;
        }

        // ---------- Glisser-déposer + tri personnalisé + dépôt dans un Folder Portal ----------
        private bool TryParseItems(DragEventArgs e, out Guid src, out List<string> paths)
        {
            src = Guid.Empty;
            paths = new List<string>();
            if (e.Data.GetData(ItemFormat) is not string s) return false;
            int i = s.IndexOf('|');
            if (i <= 0 || !Guid.TryParse(s.AsSpan(0, i), out src)) return false;
            paths = s.Substring(i + 1).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            return paths.Count > 0;
        }

        // Position d'insertion (indice dans la liste affichée) et rectangle du repère, en coordonnées de ItemsHost.
        private int GetInsertIndex(Point p, out Rect marker)
        {
            int n = _vms.Count;
            var rects = GetItemRects();

            if (n == 0 || rects.All(r => r.IsEmpty))
            {
                marker = Data.ListView
                    ? new Rect(0, 0, Math.Max(ItemsHost.ActualWidth, 40), 2)
                    : new Rect(2, 2, 2, Data.IconSize + 10);
                return 0;
            }

            // Liste : les éléments sont empilés verticalement, le repère est une ligne horizontale.
            if (Data.ListView)
            {
                double width = Math.Max(ItemsHost.ActualWidth, 40);
                for (int i = 0; i < n; i++)
                {
                    var r = rects[i];
                    if (r.IsEmpty) continue;
                    if (p.Y < r.Top + r.Height / 2)
                    {
                        marker = new Rect(0, r.Top, width, 2);
                        return i;
                    }
                }
                int last = rects.FindLastIndex(r => !r.IsEmpty);
                marker = new Rect(0, rects[last].Bottom - 2, width, 2);
                return last + 1;
            }

            int rowStart = -1, rowEnd = -1;
            for (int i = 0; i < n; i++)
            {
                var r = rects[i];
                if (r.IsEmpty) continue;
                if (p.Y >= r.Top && p.Y < r.Bottom) { if (rowStart < 0) rowStart = i; rowEnd = i; }
            }

            if (rowStart < 0)
            {
                var first = rects.First(r => !r.IsEmpty);
                var last = rects.Last(r => !r.IsEmpty);
                if (p.Y < first.Top) { marker = new Rect(first.Left, first.Top, 2, first.Height); return 0; }
                marker = new Rect(last.Right - 2, last.Top, 2, last.Height);
                return n;
            }

            for (int i = rowStart; i <= rowEnd; i++)
            {
                var r = rects[i];
                if (r.IsEmpty) continue;
                if (p.X < r.Left + r.Width / 2) { marker = new Rect(r.Left, r.Top, 2, r.Height); return i; }
            }
            var re = rects[rowEnd];
            marker = new Rect(re.Right - 2, re.Top, 2, re.Height);
            return rowEnd + 1;
        }

        // Dans un Folder Portal : dossier visé quand on dépose au centre d'une icône de dossier (ou de « .. »).
        private ItemVM? FolderTargetAt(Point p, out Rect rect)
        {
            rect = Rect.Empty;
            if (!IsPortal) return null;
            var rects = GetItemRects();
            for (int i = 0; i < _vms.Count; i++)
            {
                var r = rects[i];
                var vm = _vms[i];
                if (r.IsEmpty || !vm.IsFolder || !r.Contains(p)) continue;
                double band = r.Width * 0.3;
                if (p.X >= r.Left + band && p.X <= r.Right - band) { rect = r; return vm; }
            }
            return null;
        }

        private string FolderHint(ItemVM vm, bool copy) =>
            Loc.F(copy ? "hint.copyInto" : "hint.moveInto", vm.IsUp ? Loc.T("hint.parent") : vm.Name);

        private string PortalHint(bool copy) => Loc.F(copy ? "hint.copyInto" : "hint.moveInto", PortalName);

        private void ShowMarker(Rect r)
        {
            var tl = ItemsHost.TranslatePoint(r.TopLeft, Overlay);
            Canvas.SetLeft(Marker, tl.X);
            Canvas.SetTop(Marker, tl.Y);
            Marker.Width = Math.Max(r.Width, 2);
            Marker.Height = Math.Max(r.Height, 2);
            Marker.Visibility = Visibility.Visible;
        }

        private void HideMarker() => Marker.Visibility = Visibility.Collapsed;

        private void HideDragVisuals()
        {
            HideMarker();
            DropHint.Visibility = Visibility.Collapsed;
            if (!_marquee) SelBox.Visibility = Visibility.Collapsed;
        }

        private void Root_DragEnter(object sender, DragEventArgs e)
        {
            _dragOver = true;
            OnHoverEnter(); // une zone réduite s'ouvre quand on y amène un élément
        }

        private void Root_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_hoverMode == HoverMode.Collapse) StopHover();

            var pos = e.GetPosition(ItemsHost);
            bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            var folderTarget = FolderTargetAt(pos, out var folderRect);
            var fx = DragDropEffects.None;
            string? hint = null;
            bool intoFolder = false, insertMarker = false;

            if (e.Data.GetDataPresent(ItemFormat))
            {
                TryParseItems(e, out var src, out var paths);
                bool same = src == Data.Id;
                fx = DragDropEffects.Move;
                if (IsPortal)
                {
                    if (folderTarget != null && !paths.Any(p => SamePath(p, folderTarget.FullPath)))
                    {
                        intoFolder = true;
                        hint = FolderHint(folderTarget, ctrl);
                    }
                    else if (same && !HasFilter) insertMarker = true;
                    else hint = PortalHint(ctrl); // venu d'une autre zone : le fichier est mis dans le dossier du portail
                }
                else insertMarker = !HasFilter;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (IsPortal)
                {
                    // Copy ou Link vers la source (jamais Move) : c'est DeskNest qui déplace réellement les fichiers.
                    if (e.AllowedEffects.HasFlag(DragDropEffects.Copy)) fx = DragDropEffects.Copy;
                    else if (e.AllowedEffects.HasFlag(DragDropEffects.Link)) fx = DragDropEffects.Link;
                    if (fx != DragDropEffects.None)
                    {
                        if (folderTarget != null) { intoFolder = true; hint = FolderHint(folderTarget, ctrl); }
                        else hint = PortalHint(ctrl);
                    }
                }
                else
                {
                    // Zone classique : simple référence, jamais Move (l'Explorateur ne doit rien supprimer).
                    if (e.AllowedEffects.HasFlag(DragDropEffects.Link)) fx = DragDropEffects.Link;
                    else if (e.AllowedEffects.HasFlag(DragDropEffects.Copy)) fx = DragDropEffects.Copy;
                    insertMarker = fx != DragDropEffects.None && !HasFilter;
                }
            }
            e.Effects = fx;

            if (fx == DragDropEffects.None) { HideDragVisuals(); return; }

            if (hint != null) { DropHintText.Text = hint; DropHint.Visibility = Visibility.Visible; }
            else DropHint.Visibility = Visibility.Collapsed;

            if (intoFolder) { HideMarker(); ShowBox(folderRect); }
            else
            {
                if (!_marquee) SelBox.Visibility = Visibility.Collapsed;
                if (insertMarker) { GetInsertIndex(pos, out var m); ShowMarker(m); }
                else HideMarker();
            }
        }

        private void Root_DragLeave(object sender, DragEventArgs e)
        {
            HideDragVisuals();
            _dragOver = false;
            OnHoverLeave();
        }

        private void Root_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            _dragOver = false;
            HideDragVisuals();

            var pos = e.GetPosition(ItemsHost);
            int idx = HasFilter ? -1 : GetInsertIndex(pos, out _);
            var folderTarget = FolderTargetAt(pos, out _);
            bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

            if (TryParseItems(e, out var src, out var paths))
            {
                s_internalDrop = true;
                if (IsPortal)
                {
                    if (folderTarget != null && !paths.Any(p => SamePath(p, folderTarget.FullPath)))
                        DropIntoFolderLater(paths, folderTarget.FullPath, ctrl, src);
                    else if (src == Data.Id) ReorderTo(paths, idx);
                    else DropIntoFolderLater(paths, CurrentFolder, ctrl, src);
                }
                else if (src == Data.Id) ReorderTo(paths, idx);
                else _manager.MoveItems(src, paths, this, idx);
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var list = files.ToList();
                if (IsPortal)
                    DropIntoFolderLater(list, folderTarget?.FullPath ?? CurrentFolder, ctrl, null);
                else
                    // On attend la fin du glisser-déposer avant de toucher aux fichiers (rangement des icônes du bureau).
                    Dispatcher.BeginInvoke(new Action(() => AddDroppedFiles(list, idx)), DispatcherPriority.Background);
            }
        }

        private void DropIntoFolderLater(List<string> paths, string folder, bool copy, Guid? sourceNest) =>
            Dispatcher.BeginInvoke(new Action(() => _manager.DropIntoFolder(paths, folder, copy, sourceNest)), DispatcherPriority.Background);

        private void AddDroppedFiles(List<string> files, int idx)
        {
            foreach (var f in files)
            {
                if (Data.Items.Any(p => string.Equals(p, f, StringComparison.OrdinalIgnoreCase))) continue;
                var effective = _manager.TakeFromDesktop(f); // l'élément quitte le bureau
                AddItem(effective, idx);
                if (idx >= 0) idx++;
            }
        }

        // ---------- Menu contextuel de la zone ----------
        private ContextMenu BuildNestMenu()
        {
            var m = new ContextMenu { FlowDirection = Loc.Flow };
            m.Items.Add(Mi(Loc.T("tray.newZone"), () => _manager.CreateNest()));
            m.Items.Add(Mi(Loc.T("tray.newPortal"), () => _manager.CreatePortal()));
            m.Items.Add(Mi(Loc.T("m.renameZone"), RenameZone));
            m.Items.Add(Mi(Loc.T("m.selectAll"), SelectAll));
            _miPaste = Mi(Loc.T("paste"), () => PasteHere(null));
            m.Items.Add(_miPaste);
            m.Items.Add(new Separator());

            _miPortal = Mi(Loc.T("m.linkFolder"), ChoosePortalFolder);
            _miPortalChange = Mi(Loc.T("m.changeFolder"), ChoosePortalFolder);
            _miPortalOpen = Mi(Loc.T("m.openFolder"), () => OpenPath(CurrentFolder));
            _miPortalDetach = Mi(Loc.T("m.detach"), DetachPortal);
            m.Items.Add(_miPortal);
            m.Items.Add(_miPortalChange);
            m.Items.Add(_miPortalOpen);
            m.Items.Add(_miPortalDetach);
            m.Items.Add(new Separator());

            var colors = new MenuItem { Header = Loc.T("m.color") };
            foreach (var (name, hex) in Palette.Presets)
                colors.Items.Add(Mi(Loc.T(name), () => { Data.Color = hex; ApplyColors(); _manager.RequestSave(); }));
            m.Items.Add(colors);

            var opacity = new MenuItem { Header = Loc.T("m.opacity") };
            foreach (var pct in new[] { 30, 50, 75, 90 })
                opacity.Items.Add(Mi($"{pct} %", () => { Data.Opacity = pct / 100.0; ApplyColors(); _manager.RequestSave(); }));
            m.Items.Add(opacity);

            var sizes = new MenuItem { Header = Loc.T("m.iconSize") };
            foreach (var (key, px) in new[] { ("m.small", 32), ("m.medium", 40), ("m.large", 48) })
                sizes.Items.Add(Mi(Loc.T(key), () => { Data.IconSize = px; RefreshItems(); _manager.RequestSave(); }));
            m.Items.Add(sizes);

            var view = new MenuItem { Header = Loc.T("m.view") };
            _miViewGrid = Mi(Loc.T("view.grid"), () => SetListView(false));
            _miViewList = Mi(Loc.T("view.list"), () => SetListView(true));
            view.Items.Add(_miViewGrid);
            view.Items.Add(_miViewList);
            m.Items.Add(view);

            m.Items.Add(Mi(Loc.T("m.fonts"), () => _manager.ShowSettings(Data)));

            m.Items.Add(new Separator());
            _miTitle = Mi("", () =>
            {
                Data.TitleVisible = !Data.TitleVisible;
                if (!Data.TitleVisible) Data.RolledUp = false;
                ApplyState();
                _manager.RequestSave();
            });
            _miRoll = Mi("", ToggleRollUp);
            _miLock = Mi("", () => { Data.Locked = !Data.Locked; ApplyState(); _manager.RequestSave(); });
            _miHover = Mi("", () => { Data.HoverExpand = !Data.HoverExpand; _manager.RequestSave(); });
            m.Items.Add(_miTitle);
            m.Items.Add(_miRoll);
            m.Items.Add(_miLock);
            m.Items.Add(_miHover);
            m.Items.Add(new Separator());
            m.Items.Add(Mi(Loc.T("tray.settings"), () => _manager.ShowSettings()));
            m.Items.Add(Mi(Loc.T("m.deleteZone"), DeleteNest));

            m.Opened += (_, _) =>
            {
                _menuOpen = true;
                _miTitle.Header = Data.TitleVisible ? Loc.T("m.hideTitle") : Loc.T("m.showTitle");
                _miRoll.Header = _hoverExpanded ? Loc.T("m.keepExpanded") : Data.RolledUp ? Loc.T("m.expand") : Loc.T("m.rollUp");
                _miRoll.IsEnabled = Data.TitleVisible;
                _miLock.Header = Data.Locked ? Loc.T("m.unlock") : Loc.T("m.lock");
                _miHover.Header = Data.HoverExpand ? Loc.T("m.hoverOn") : Loc.T("m.hoverOff");
                _miViewGrid.IsChecked = !Data.ListView;
                _miViewList.IsChecked = Data.ListView;
                _miPaste.IsEnabled = FileClipboard.HasFiles();
                _miPortal.Visibility = IsPortal ? Visibility.Collapsed : Visibility.Visible;
                _miPortalChange.Visibility = IsPortal ? Visibility.Visible : Visibility.Collapsed;
                _miPortalOpen.Visibility = IsPortal ? Visibility.Visible : Visibility.Collapsed;
                _miPortalDetach.Visibility = IsPortal ? Visibility.Visible : Visibility.Collapsed;
            };
            m.Closed += (_, _) =>
            {
                // Après une ouverture par le bouton du titre, on rend au menu son comportement normal (clic droit).
                m.ClearValue(ContextMenu.PlacementProperty);
                m.ClearValue(ContextMenu.PlacementTargetProperty);
                _menuOpen = false;
                if (_hoverExpanded && !IsMouseOver) StartHover(HoverMode.Collapse, _manager.Settings.HoverCollapseDelayMs);
            };
            return m;
        }

        private void ToggleRollUp()
        {
            if (!Data.TitleVisible) return;
            StopHover();
            if (_hoverExpanded) { _hoverExpanded = false; Data.RolledUp = false; } // zone ouverte par survol : on la garde développée
            else Data.RolledUp = !Data.RolledUp;
            ApplyState(true);
            _manager.RequestSave();
        }

        private void RenameZone()
        {
            if (Data.TitleVisible)
            {
                // Modification directement dans le titre (après la fermeture du menu, pour garder le focus).
                Dispatcher.BeginInvoke(new Action(BeginTitleEdit), DispatcherPriority.ContextIdle);
                return;
            }
            var t = InputDialog.Ask(this, Loc.T("m.renameZone"), Data.Title);
            if (string.IsNullOrWhiteSpace(t)) return;
            Data.Title = t.Trim();
            UpdateTitle();
            _manager.RequestSave();
        }

        // ---------- Développement au survol ----------
        private bool HoverEnabled =>
            _manager.Settings.HoverExpand && Data.HoverExpand && Data.RolledUp && Data.TitleVisible;

        private bool IsBusy =>
            _moving || _resizing || _marquee || _menuOpen || _dragging || _dragOver || _editingTitle || _editingItem != null ||
            (Hwnd != IntPtr.Zero && !IsWindowEnabled(Hwnd)); // fenêtre de dialogue ouverte

        private void StartHover(HoverMode mode, int ms)
        {
            _hoverTimer.Stop();
            _hoverMode = mode;
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
            _hoverTimer.Start();
        }

        private void StopHover()
        {
            _hoverTimer.Stop();
            _hoverMode = HoverMode.None;
        }

        private void OnHoverEnter()
        {
            if (_hoverExpanded) { StopHover(); return; } // annule la réduction prévue
            if (HoverEnabled) StartHover(HoverMode.Expand, _manager.Settings.HoverExpandDelayMs);
        }

        private void OnHoverLeave()
        {
            if (_hoverExpanded) StartHover(HoverMode.Collapse, _manager.Settings.HoverCollapseDelayMs);
            else if (_hoverMode == HoverMode.Expand) StopHover();
        }

        private void OnHoverTick()
        {
            _hoverTimer.Stop();
            var mode = _hoverMode;
            _hoverMode = HoverMode.None;

            if (mode == HoverMode.Expand)
            {
                if ((IsMouseOver || _dragOver) && HoverEnabled && !_hoverExpanded)
                {
                    _hoverExpanded = true;
                    BringToTopForHover();
                    ApplyState(true);
                }
            }
            else if (mode == HoverMode.Collapse)
            {
                if (!_hoverExpanded || IsMouseOver) return;
                if (IsBusy)
                {
                    StartHover(HoverMode.Collapse, 400);
                    return;
                }

                _hoverExpanded = false;

                if (!_raisedForDesktop)
                    SendToBottom();

                ApplyState(true);
            }
        }

        // ---------- Folder Portal ----------
        private void ChoosePortalFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("dlg.chooseFolder") };
            if (IsPortal && Directory.Exists(Data.PortalPath)) dlg.InitialDirectory = Data.PortalPath;
            if (dlg.ShowDialog(this) != true) return;
            SetPortal(dlg.FolderName);
        }

        private void SetPortal(string folder)
        {
            if (!IsPortal && Data.Items.Count > 0)
            {
                var r = MessageBox.Show(this, Loc.T("msg.portalReplace"), "DeskNest", MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
                if (r != MessageBoxResult.Yes) return;
                var old = Data.Items.ToList();
                Data.Items.Clear();
                _selected.Clear();
                foreach (var p in old) _manager.RestoreOnDesktop(p);
            }

            Data.PortalPath = folder;
            _portalCurrent = null;
            _portalHistory.Clear();
            Data.PortalOrder.Clear();
            ResetSearch();
            if (Loc.IsDefaultZoneTitle(Data.Title))
            {
                var n = IOPath.GetFileName(folder.TrimEnd('\\'));
                Data.Title = string.IsNullOrEmpty(n) ? folder : n;
            }
            ResetWatcher();
            UpdateTitle();
            RefreshItems();
            _manager.RequestSave();
        }

        private void DetachPortal()
        {
            Data.PortalPath = null;
            _portalCurrent = null;
            _portalHistory.Clear();
            Data.PortalOrder.Clear();
            _selected.Clear();
            ResetSearch();
            ResetWatcher();
            UpdateTitle();
            RefreshItems();
            _manager.RequestSave();
        }

        private void DeleteNest()
        {
            var r = MessageBox.Show(this, Loc.F("msg.deleteZone", Data.Title), "DeskNest", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
            if (r == MessageBoxResult.Yes) _manager.Remove(this);
        }
    }
}
