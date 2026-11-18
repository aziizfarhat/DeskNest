using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskNest
{
    public enum QuickActionKind { NewZone, NewPortal, NewTodo, NewNote, Settings }

    // Composant unique partagé par la fenêtre d'actions rapides et la palette de
    // recherche. Les boutons ne prennent jamais le focus et ne figurent pas dans
    // l'ordre de tabulation : la saisie de la recherche reste inchangée.
    public sealed class QuickActionsBar : StackPanel, IDisposable
    {
        private static readonly QuickActionKind[] Kinds =
        {
            QuickActionKind.NewZone, QuickActionKind.NewPortal, QuickActionKind.NewTodo,
            QuickActionKind.NewNote, QuickActionKind.Settings
        };

        private static readonly string[] LabelKeys =
        {
            "tray.newZone", "tray.newPortal", "tray.newTodo", "tray.newNote", "tray.settings"
        };

        // Segoe MDL2 Assets : plus, dossier, liste, presse-papiers, engrenage.
        private static readonly string[] Glyphs = { "\uE710", "\uE8B7", "\uE8FD", "\uE8A5", "\uE713" };

        private readonly Action<QuickActionKind> _invoke;
        private readonly List<Button> _buttons = new();
        private readonly List<Border> _visuals = new();
        private readonly List<TextBlock> _labels = new();
        private int _selected;

        public IReadOnlyList<QuickActionKind> Actions => Kinds;
        public QuickActionKind SelectedAction => Kinds[_selected];
        public int Count => Kinds.Length;

        public QuickActionsBar(Action<QuickActionKind> invoke, bool compact)
        {
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            FlowDirection = Loc.Flow;

            var iconBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            var textBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

            for (int i = 0; i < Kinds.Length; i++)
            {
                int index = i;
                var content = new StackPanel
                {
                    Orientation = compact ? Orientation.Horizontal : Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
                content.Children.Add(new TextBlock
                {
                    Text = Glyphs[i],
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = compact ? 17 : 28,
                    Foreground = iconBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (!compact)
                {
                    var label = new TextBlock
                    {
                        Text = Loc.T(LabelKeys[i]),
                        FontSize = 12,
                        Foreground = textBrush,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0),
                        MaxWidth = 104,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    content.Children.Add(label);
                    _labels.Add(label);
                }

                var visual = new Border
                {
                    CornerRadius = new CornerRadius(compact ? 7 : 10),
                    Background = NormalBrush(),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = compact ? new Thickness(8, 4, 8, 4) : new Thickness(10, 14, 10, 12),
                    Width = compact ? double.NaN : 122,
                    Height = compact ? double.NaN : 96,
                    Child = content
                };

                var button = new Button
                {
                    Content = visual,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Margin = compact ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 12, 0),
                    Focusable = false,
                    Cursor = Cursors.Hand,
                    ToolTip = Loc.T(LabelKeys[i])
                };
                KeyboardNavigation.SetIsTabStop(button, false);
                button.Click += (_, _) => Invoke(index);
                button.MouseEnter += (_, _) => Select(index);

                _buttons.Add(button);
                _visuals.Add(visual);
                Children.Add(button);
            }
            // La dernière marge est inutile et déscentre la rangée.
            if (_buttons.Count > 0) _buttons[_buttons.Count - 1].Margin = new Thickness(0);

            Select(0);
            Loc.Changed += OnLanguageChanged;
        }

        private static SolidColorBrush NormalBrush() => new(Color.FromArgb(24, 255, 255, 255));
        private static SolidColorBrush SelectedBrush() => new(Color.FromArgb(70, 90, 150, 230));

        private void OnLanguageChanged()
        {
            FlowDirection = Loc.Flow;
            for (int i = 0; i < _labels.Count; i++) _labels[i].Text = Loc.T(LabelKeys[i]);
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].ToolTip = Loc.T(LabelKeys[i]);
        }

        public void RefreshLabels() => OnLanguageChanged();

        public void Select(int index)
        {
            if (_buttons.Count == 0) return;
            _selected = Math.Clamp(index, 0, _buttons.Count - 1);
            for (int i = 0; i < _visuals.Count; i++)
                _visuals[i].Background = i == _selected ? SelectedBrush() : NormalBrush();
        }

        public void Move(int delta)
        {
            if (_buttons.Count > 0) Select(_selected + delta);
        }

        public void InvokeSelected()
        {
            if (_buttons.Count > 0) Invoke(_selected);
        }

        public void Invoke(int index)
        {
            if (index < 0 || index >= Kinds.Length) return;
            Select(index);
            _invoke(Kinds[index]);
        }

        public void Dispose() => Loc.Changed -= OnLanguageChanged;
    }
}
