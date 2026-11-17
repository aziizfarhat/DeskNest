using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskNest
{
    // Boîte de dialogue pour une version plus récente. Les boutons sont traduits,
    // contrairement aux boutons Oui/Non de MessageBox qui suivent la langue de Windows.
    public static class UpdatePrompt
    {
        public static void Show(Window? owner, string installed, string latest, string pageUrl)
        {
            var text = new TextBlock
            {
                Text = Loc.T("upd.title"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var details = new TextBlock
            {
                Text = Loc.F("upd.details", installed, latest),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                TextWrapping = TextWrapping.Wrap
            };

            var later = new Button
            {
                Content = Loc.T("upd.later"),
                MinWidth = 100,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            var open = new Button
            {
                Content = Loc.T("upd.open"),
                MinWidth = 100,
                Padding = new Thickness(12, 6, 12, 6),
                IsDefault = true
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 22, 0, 0)
            };
            buttons.Children.Add(later);
            buttons.Children.Add(open);

            var content = new StackPanel { Margin = new Thickness(24) };
            content.Children.Add(text);
            content.Children.Add(details);
            content.Children.Add(buttons);

            var window = new Window
            {
                Title = "DeskNest",
                Content = content,
                Width = 470,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                FlowDirection = Loc.Flow,
                Background = Brushes.White
            };
            if (owner != null) window.Owner = owner;

            later.Click += (_, _) => window.Close();
            open.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Diag.Log("ouverture de la page de version : " + ex.Message);
                }
                window.Close();
            };

            window.ShowDialog();
        }
    }
}
