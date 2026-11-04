using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DeskNest
{
    // Propriété attachée pour traduire les textes d'un fichier XAML : l:L.Key="clé" et l:L.TipKey="clé".
    // Les textes se mettent à jour tout seuls quand la langue change.
    public static class L
    {
        public static readonly DependencyProperty KeyProperty =
            DependencyProperty.RegisterAttached("Key", typeof(string), typeof(L), new PropertyMetadata(null, OnChanged));
        public static readonly DependencyProperty TipKeyProperty =
            DependencyProperty.RegisterAttached("TipKey", typeof(string), typeof(L), new PropertyMetadata(null, OnChanged));

        public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
        public static void SetKey(DependencyObject d, string? v) => d.SetValue(KeyProperty, v);
        public static string? GetTipKey(DependencyObject d) => (string?)d.GetValue(TipKeyProperty);
        public static void SetTipKey(DependencyObject d, string? v) => d.SetValue(TipKeyProperty, v);

        private static readonly List<WeakReference<DependencyObject>> Targets = new();

        static L() { Loc.Changed += RefreshAll; }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            Targets.Add(new WeakReference<DependencyObject>(d));
            Apply(d);
        }

        private static void RefreshAll()
        {
            for (int i = Targets.Count - 1; i >= 0; i--)
            {
                if (Targets[i].TryGetTarget(out var d)) Apply(d);
                else Targets.RemoveAt(i);
            }
        }

        private static void Apply(DependencyObject d)
        {
            var key = GetKey(d);
            if (key != null)
            {
                var text = Loc.T(key);
                switch (d)
                {
                    case TextBlock tb: tb.Text = text; break;
                    case Window w: w.Title = text; break;
                    case HeaderedContentControl hc: hc.Header = text; break;  // TabItem
                    case HeaderedItemsControl hi: hi.Header = text; break;    // MenuItem
                    case ContentControl cc: cc.Content = text; break;         // Button, CheckBox...
                }
            }
            var tip = GetTipKey(d);
            if (tip != null && d is FrameworkElement fe) fe.ToolTip = Loc.T(tip);
        }
    }
}
