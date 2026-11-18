using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskNest
{
    // Fenêtre d'actions rapides ouverte par double-clic sur l'icône de notification.
    // Même habillage que la palette de recherche, mais sans champ de saisie.
    public partial class QuickActionsWindow : Window
    {
        private readonly NestManager _manager;
        private QuickActionsBar? _bar;
        private bool _closing;
        private bool _ready;

        public bool IsClosing => _closing;

        public QuickActionsWindow(NestManager manager)
        {
            InitializeComponent();
            _manager = manager;
            FlowDirection = Loc.Flow;
            _bar = new QuickActionsBar(Invoke, compact: false);
            ActionsHost.Content = _bar;

            Loaded += OnLoaded;
            Closing += (_, _) => _closing = true;
            Closed += (_, _) =>
            {
                _bar?.Dispose();
                _bar = null;
                Loc.Changed -= OnLanguageChanged;
            };
            Loc.Changed += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            FlowDirection = Loc.Flow;
            _bar?.RefreshLabels();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PlaceAtCursor();
            Focus();
            Deactivated += (_, _) => { if (_ready && !_closing) CloseWindow(); };
            _ready = true;
        }

        private void PlaceAtCursor()
        {
            var ctx = CursorPlacement.Get();
            var work = ctx.WorkArea;
            Left = CursorPlacement.Clamp(ctx.Cursor.X - ActualWidth / 2,
                work.Left + 8, work.Right - ActualWidth - 8);
            Top = CursorPlacement.Clamp(ctx.Cursor.Y - ActualHeight / 2,
                work.Top + 8, work.Bottom - ActualHeight - 8);
        }

        public void CloseWindow()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private void Invoke(QuickActionKind kind)
        {
            if (_closing) return;
            CloseWindow();
            // L'action crée une fenêtre : on attend la fin de la fermeture pour éviter
            // les erreurs WPF « window is closing » et rendre le focus au bon endroit.
            Dispatcher.BeginInvoke(new Action(() => Execute(kind)), DispatcherPriority.Background);
        }

        private void Execute(QuickActionKind kind) => _manager.ExecuteQuickAction(kind);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.ImeProcessed ? e.SystemKey : e.Key;
            switch (key)
            {
                case Key.Escape:
                    CloseWindow();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _bar?.InvokeSelected();
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Up:
                    _bar?.Move(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Down:
                    _bar?.Move(1);
                    e.Handled = true;
                    break;
                default:
                    int number = key switch
                    {
                        Key.D1 or Key.NumPad1 => 1,
                        Key.D2 or Key.NumPad2 => 2,
                        Key.D3 or Key.NumPad3 => 3,
                        Key.D4 or Key.NumPad4 => 4,
                        Key.D5 or Key.NumPad5 => 5,
                        _ => 0
                    };
                    if (number > 0)
                    {
                        _bar?.Invoke(number - 1);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsWithin(d, RootBorder)) return;
            CloseWindow();
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
    }
}
