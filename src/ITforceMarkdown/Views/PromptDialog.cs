using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ITforceMarkdown.Views;

/// <summary>
/// 一个轻量级输入对话框 — WPF 没有 InputBox 这样的内置控件 (Win Forms 才有)。
/// 用 Insert Link / Insert Image 让用户输 URL。
/// </summary>
internal static class PromptDialog
{
    /// <summary>
    /// 弹一个带 OK/Cancel 的输入框。返回用户输入, Cancel 或空字符串则返回 null。
    /// </summary>
    public static string? Show(Window owner, string title, string label, string defaultText = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = owner.Background,
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12,
        };
        Grid.SetRow(lbl, 0);
        grid.Children.Add(lbl);

        var tb = new TextBox
        {
            Text = defaultText,
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(tb, 1);
        grid.Children.Add(tb);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button { Content = "OK", MinWidth = 80, Padding = new Thickness(12, 4, 12, 4) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        ok.IsDefault = true;
        cancel.IsCancel = true;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        Grid.SetRow(btnRow, 2);
        grid.Children.Add(btnRow);

        win.Content = grid;

        string? result = null;
        ok.Click += (_, _) =>
        {
            var t = tb.Text?.Trim();
            result = string.IsNullOrEmpty(t) ? null : t;
            win.DialogResult = true;
            win.Close();
        };

        win.Loaded += (_, _) =>
        {
            tb.Focus();
            tb.SelectAll();
        };

        win.ShowDialog();
        return result;
    }
}
