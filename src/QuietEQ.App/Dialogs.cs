using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuietEQ.App;

internal static class Dialogs
{
    public static Window Preview(Window owner, string message, string text, string action)
    {
        var window = new Window { Owner = owner, Title = "应用配置 · 预览", Width = 850, Height = 650, MinWidth = 650, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new Grid { Margin = new Thickness(26) }; window.Content = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 24, Margin = new Thickness(0, 0, 0, 18) });
        var code = new TextBox { Text = text, IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(code, 1); grid.Children.Add(code);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var apply = new Button { Content = action, Style = (Style)owner.FindResource("Primary") }; buttons.Children.Add(apply);
        apply.Click += (_, _) => window.DialogResult = true;
        return window;
    }
}
