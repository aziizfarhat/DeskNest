using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskNest
{
    // Contenu d'une zone To-do. Construit en code pour rester léger : aucune fenêtre
    // ni ressource supplémentaire n'est créée tant que la zone n'est pas développée.
    public sealed class TodoContent : Grid
    {
        private const string TodoFormat = "DeskNest.TodoTask";

        private readonly NestManager _manager;
        private readonly NestData _data;
        private readonly StackPanel _rows = new();
        private readonly TextBox _input;
        private readonly Button _add;
        private readonly TextBlock _count;
        private readonly TextBlock _empty;
        private readonly Border _indicator;
        private TodoTask? _editing;
        private TextBox? _editBox;
        private TodoTask? _drag;
        private TodoTask? _selected;
        private int _dropIndex;
        private bool _refreshing;

        public TodoContent(NestManager manager, NestData data)
        {
            _manager = manager;
            _data = data;
            FlowDirection = Loc.Flow;
            Focusable = true;
            AllowDrop = true;
            Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _input = new TextBox
            {
                Margin = new Thickness(4, 4, 4, 6),
                Padding = new Thickness(6, 3, 6, 3),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                MaxLength = 500
            };
            _input.KeyDown += Input_KeyDown;

            _add = new Button
            {
                Content = "+",
                ToolTip = Loc.T("todo.add"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(60, 90, 150, 230)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Width = 30,
                Height = 28,
                Margin = new Thickness(0, 4, 4, 6),
                Padding = new Thickness(0),
                Focusable = false,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            KeyboardNavigation.SetIsTabStop(_add, false);
            _add.Click += (_, _) => AddFromInput();

            var inputRow = new Grid();
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputRow.Children.Add(_input);
            inputRow.Children.Add(_add);
            Grid.SetColumn(_input, 0);
            Grid.SetColumn(_add, 1);
            Children.Add(inputRow);
            Grid.SetRow(inputRow, 0);

            _empty = new TextBlock
            {
                Text = Loc.T("todo.empty"),
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            var listGrid = new Grid();
            listGrid.Children.Add(_rows);
            listGrid.Children.Add(_empty);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = listGrid
            };
            Children.Add(scroll);
            Grid.SetRow(scroll, 1);

            _indicator = new Border
            {
                Height = 2,
                Margin = new Thickness(4, 0, 4, 0),
                Background = Brushes.White,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            _count = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(4, 2, 6, 2)
            };
            Children.Add(_count);
            Grid.SetRow(_count, 2);

            DragOver += Todo_DragOver;
            Drop += Todo_Drop;
            DragLeave += (_, _) => HideIndicator();
            PreviewKeyDown += Todo_PreviewKeyDown;
            MouseLeftButtonDown += (_, _) => Focus();
            Loc.Changed += OnLanguageChanged;
            Unloaded += (_, _) => Loc.Changed -= OnLanguageChanged;

            ApplyFonts();
            Refresh();
        }

        public void ApplyFonts()
        {
            if (string.IsNullOrWhiteSpace(_data.ItemFont)) ClearValue(TextElement.FontFamilyProperty);
            else SetValue(TextElement.FontFamilyProperty, new FontFamily(_data.ItemFont));
            // Taille globale des listes (réglage « Notes & Todo List »), comme pour les notes.
            SetValue(TextElement.FontSizeProperty, Math.Clamp(_manager.Settings.TodoFontSize, 8, 28));
            Refresh();
        }

        private void OnLanguageChanged()
        {
            FlowDirection = Loc.Flow;
            _empty.Text = Loc.T("todo.empty");
            _add.ToolTip = Loc.T("todo.add");
            Refresh();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            // Key.Enter = touche principale, Key.Return = pavé numérique.
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            e.Handled = true;
            AddFromInput();
        }

        private void AddFromInput()
        {
            if (!TodoOrder.TryCreate(_input.Text, out var task) || task == null) return;
            TodoOrder.Insert(_data.Tasks, task, _manager.Settings.TodoAddAtTop);
            _input.Clear();
            _manager.RequestSave();
            Refresh();
            FocusInput();
        }

        private void FocusInput()
        {
            _input.Focus();
            Keyboard.Focus(_input);
            _input.CaretIndex = _input.Text.Length;
        }

        private void Todo_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_editing != null) return; // la touche appartient au champ d'édition
            if (e.Key == Key.Delete && _selected != null)
            {
                e.Handled = true;
                var task = _selected;
                _selected = null;
                _data.Tasks.Remove(task);
                _manager.RequestSave();
                Refresh();
                Focus();
            }
        }

        private void Toggle(TodoTask task, bool done)
        {
            if (task.Done == done) return;
            task.Done = done;
            _manager.RequestSave();
            Refresh();
        }

        private List<TodoTask> Ordered() =>
            TodoOrder.Sort(_data.Tasks, _manager.Settings.TodoCheckedToBottom);

        // Un clic sur un contrôle interactif ne doit pas reconstruire la ligne
        // (sinon le bouton qui reçoit le clic disparaît avant son événement Click).
        private static bool IsInteractiveSource(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is ButtonBase || d is TextBoxBase || d is TextBlock) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        // Reconstruit les lignes sans perdre une édition en cours : le texte du
        // champ est conservé et le focus lui est rendu si nécessaire.
        private void Refresh(string? editTextOverride = null)
        {
            if (_refreshing) return;
            _refreshing = true;
            string? editText = editTextOverride ??
                (_editing != null && _editBox != null ? _editBox.Text : null);
            int caret = _editBox?.CaretIndex ?? 0;
            bool editFocused = _editBox != null && _editBox.IsKeyboardFocused;
            _editBox = null;

            _rows.Children.Clear();
            var tasks = Ordered();
            foreach (var task in tasks) _rows.Children.Add(BuildRow(task, editText));
            _empty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int done = tasks.Count(t => t.Done);
            _count.Text = Loc.F("todo.count", done, tasks.Count);
            HideIndicator();

            if (editFocused && _editBox != null)
            {
                _editBox.Focus();
                Keyboard.Focus(_editBox);
                _editBox.CaretIndex = Math.Min(caret, _editBox.Text.Length);
            }
            _refreshing = false;
        }

        private static Button SmallButton(string glyph, string tooltip, Action click)
        {
            var button = new Button
            {
                Content = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            KeyboardNavigation.SetIsTabStop(button, false);
            button.Click += (_, _) => click();
            return button;
        }

        private UIElement BuildRow(TodoTask task, string? editText)
        {
            bool editing = ReferenceEquals(task, _editing);
            var row = new Border
            {
                Padding = new Thickness(2, 3, 4, 3),
                CornerRadius = new CornerRadius(4),
                Background = ReferenceEquals(task, _selected)
                    ? new SolidColorBrush(Color.FromArgb(60, 90, 160, 255))
                    : Brushes.Transparent
            };
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (editing || IsInteractiveSource(e.OriginalSource as DependencyObject)) return;
                _selected = task;
                e.Handled = true;
                Focus();
                Refresh();
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. Poignée de glisser (désactivée en mode édition).
            var grip = new Border
            {
                Width = 10,
                Height = 18,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                ToolTip = Loc.T("todo.drag"),
                IsEnabled = !editing,
                Opacity = editing ? 0.35 : 1.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "⠿",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            grip.MouseLeftButtonDown += (sender, e) => BeginDrag(task, grip, e);
            grid.Children.Add(grip);
            Grid.SetColumn(grip, 0);

            // 2. Case « terminé », toujours visible et cliquable.
            var check = new CheckBox
            {
                IsChecked = task.Done,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0)
            };
            check.Click += (_, _) => Toggle(task, check.IsChecked == true);
            grid.Children.Add(check);
            Grid.SetColumn(check, 1);

            // 3. Texte ou champ d'édition.
            UIElement textControl;
            if (editing)
            {
                var box = new TextBox
                {
                    Text = editText ?? task.Text,
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxLength = 1000,
                    AcceptsReturn = false
                };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter || e.Key == Key.Return) { e.Handled = true; EndEdit(true); }
                    else if (e.Key == Key.Escape) { e.Handled = true; EndEdit(false); }
                };
                _editBox = box;
                textControl = box;
            }
            else
            {
                var label = new TextBlock
                {
                    Text = task.Text,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextDecorations = task.Done && _manager.Settings.TodoStrikeThrough ? TextDecorations.Strikethrough : null,
                    Opacity = task.Done && _manager.Settings.TodoStrikeThrough ? 0.55 : 1.0,
                    Foreground = Brushes.White,
                    Cursor = Cursors.IBeam
                };
                label.MouseLeftButtonUp += (_, e) =>
                {
                    if (_editing == task) return;
                    e.Handled = true;
                    BeginEdit(task);
                };
                textControl = label;
            }
            grid.Children.Add(textControl);
            Grid.SetColumn(textControl, 2);

            // 4 et 5. Crayon + poubelle, ou croix + X en mode édition.
            var action = editing
                ? SmallButton("\uE73E", Loc.T("todo.validate"), () => EndEdit(true))
                : SmallButton("\uE712", Loc.T("todo.edit"), () => BeginEdit(task));
            grid.Children.Add(action);
            Grid.SetColumn(action, 3);

            var remove = editing
                ? SmallButton("\uE711", Loc.T("dlg.cancel"), () => EndEdit(false))
                : SmallButton("\uE74D", Loc.T("i.delete"), () => DeleteTask(task));
            grid.Children.Add(remove);
            Grid.SetColumn(remove, 4);

            row.Child = grid;
            return row;
        }

        private void DeleteTask(TodoTask task)
        {
            if (_editing == task) return;
            if (_selected == task) _selected = null;
            _data.Tasks.Remove(task);
            _manager.RequestSave();
            Refresh();
            Focus();
        }

        private void BeginEdit(TodoTask task)
        {
            if (_editing != null && !ReferenceEquals(_editing, task)) EndEdit(true);
            if (_editing == task)
            {
                if (_editBox != null) { _editBox.Focus(); Keyboard.Focus(_editBox); }
                return;
            }
            _selected = task;
            _editing = task;
            _editBox = null;
            Refresh();
            if (_editBox != null)
            {
                _editBox.Focus();
                Keyboard.Focus(_editBox);
                _editBox.SelectAll();
            }
        }

        private void EndEdit(bool commit)
        {
            if (_editing == null) return;
            var task = _editing;
            string text = _editBox?.Text ?? "";

            // Texte vide : refus, on reste en édition avec le champ vide.
            if (commit && string.IsNullOrWhiteSpace(text))
            {
                _editBox = null;
                Refresh(text);
                if (_editBox != null) { _editBox.Focus(); Keyboard.Focus(_editBox); }
                return;
            }

            _editing = null;
            _editBox = null;
            if (commit && TodoOrder.CommitEdit(task, text)) _manager.RequestSave();
            Refresh();
        }

        private void BeginDrag(TodoTask task, UIElement source, MouseButtonEventArgs e)
        {
            if (_editing != null) return; // une ligne en édition n'est pas déplaçable
            if (e.LeftButton != MouseButtonState.Pressed) return;
            e.Handled = true;
            _drag = task;
            var data = new DataObject(TodoFormat, task.Id.ToString("N"));
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
            _drag = null;
            HideIndicator();
            Refresh();
        }

        private void Todo_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true; // empêche le Root de la zone d'appliquer ses règles de dépôt de fichiers
            if (_drag == null || !e.Data.GetDataPresent(TodoFormat))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            e.Effects = DragDropEffects.Move;
            ShowIndicator(IndexAt(e.GetPosition(_rows).Y));
        }

        private void Todo_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_drag == null || !e.Data.GetDataPresent(TodoFormat)) { HideIndicator(); return; }
            var ordered = Ordered();
            var dragged = ordered.FirstOrDefault(t => t.Id == _drag.Id);
            int index = _dropIndex;
            if (dragged != null)
            {
                ordered.Remove(dragged);
                index = Math.Clamp(index, 0, ordered.Count);
                var before = index > 0 ? ordered[index - 1] : null;
                var after = index < ordered.Count ? ordered[index] : null;
                TodoOrder.Move(_data.Tasks, dragged, before, after);
                _manager.RequestSave();
            }
            _drag = null;
            HideIndicator();
            Refresh();
        }

        private int IndexAt(double y)
        {
            int index = 0;
            foreach (UIElement child in _rows.Children)
            {
                if (ReferenceEquals(child, _indicator)) continue;
                var top = child.TransformToVisual(_rows).Transform(new Point(0, 0)).Y;
                if (y < top + child.RenderSize.Height / 2) return index;
                index++;
            }
            return index;
        }

        private void ShowIndicator(int index)
        {
            _dropIndex = Math.Max(0, index);
            if (_rows.Children.Contains(_indicator)) _rows.Children.Remove(_indicator);
            _rows.Children.Insert(Math.Min(_dropIndex, _rows.Children.Count), _indicator);
            _indicator.Visibility = Visibility.Visible;
        }

        private void HideIndicator()
        {
            _dropIndex = -1;
            if (_rows.Children.Contains(_indicator)) _rows.Children.Remove(_indicator);
            _indicator.Visibility = Visibility.Collapsed;
        }
    }
}
