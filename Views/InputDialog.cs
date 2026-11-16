using System.Windows;
using System.Windows.Controls;

namespace DeskNest
{
    public sealed class InputDialog : Window
    {
        private readonly TextBox _box;
        private string? _result;

        private InputDialog(string title, string initial)
        {
            Title = title;
            Width = 340;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FlowDirection = Loc.Flow;

            var panel = new StackPanel { Margin = new Thickness(14) };
            _box = new TextBox { Text = initial, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 12) };
            var ok = new Button { Content = Loc.T("dlg.ok"), Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = Loc.T("dlg.cancel"), Width = 80, IsCancel = true };
            ok.Click += (_, _) => { _result = _box.Text; DialogResult = true; };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(ok);
            row.Children.Add(cancel);
            panel.Children.Add(_box);
            panel.Children.Add(row);
            Content = panel;
            Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        }

        public static string? Ask(Window owner, string title, string initial)
        {
            var d = new InputDialog(title, initial) { Owner = owner };
            return d.ShowDialog() == true ? d._result : null;
        }
    }
}
