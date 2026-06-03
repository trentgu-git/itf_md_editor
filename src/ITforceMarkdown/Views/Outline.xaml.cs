using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ITforceMarkdown.Models;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

/// <summary>
/// 文档大纲 (TOC) — 对应 Mac 版 OutlinePanel。
/// 显示当前文档的所有标题, 按层级缩进, 点击调 Store.RequestScrollToHeading,
/// MarkdownPreview 监听 ScrollToken 变化, ExecuteScriptAsync 调 webview 里
/// document.js 注入的 __scrollToHeading 函数。
/// </summary>
public partial class Outline : UserControl
{
    private WorkspaceStore Store => App.Store;

    public Outline()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Store.PropertyChanged += OnStoreChanged;
        Store.Headings.CollectionChanged += OnHeadingsChanged;
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Store.PropertyChanged -= OnStoreChanged;
        Store.Headings.CollectionChanged -= OnHeadingsChanged;
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceStore.OutlineFilter))
        {
            Dispatcher.BeginInvoke(new Action(Rebuild));
            // 同步 placeholder 显示
            if (FilterBox.Text != Store.OutlineFilter)
                FilterBox.Text = Store.OutlineFilter;
        }
    }

    private void OnHeadingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(Rebuild));

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Store.OutlineFilter = FilterBox.Text;
        FilterPlaceholder.Visibility = string.IsNullOrEmpty(FilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Rebuild()
    {
        HeadingsHost.Children.Clear();

        var headings = Store.Headings.AsEnumerable();
        var filter = Store.OutlineFilter?.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            headings = headings.Where(h =>
                h.Title.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var list = headings.ToList();
        if (list.Count == 0)
        {
            HeadingsHost.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(Store.SelectedFile?.Path)
                    ? "Open a document to see its outline."
                    : (string.IsNullOrEmpty(filter)
                        ? "No headings."
                        : "No matches."),
                Margin = new Thickness(16, 8, 16, 8),
                FontSize = 11,
                Foreground = (Brush)FindResource("Brush.Muted"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var h in list)
            HeadingsHost.Children.Add(BuildItem(h));
    }

    private FrameworkElement BuildItem(HeadingItem h)
    {
        var b = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16 + (h.Level - 1) * 12, 4, 12, 4),
            Cursor = Cursors.Hand,
            Foreground = (Brush)FindResource("Brush.Ink"),
            FontSize = h.Level == 1 ? 12.5 : 11.5,
            FontWeight = h.Level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
            Content = new TextBlock
            {
                Text = h.Title,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        var capturedId = h.Id;
        b.Click += (_, _) => Store.RequestScrollToHeading(capturedId);
        return b;
    }
}
