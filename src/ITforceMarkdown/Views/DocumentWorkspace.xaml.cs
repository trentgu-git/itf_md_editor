using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ITforceMarkdown.Models;
using ITforceMarkdown.Services;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

public partial class DocumentWorkspace : UserControl
{
    private WorkspaceStore Store => App.Store;
    private MarkdownPreview? _readView;
    private MarkdownPreview? _richView;
    private SourceEditor? _sourceView;

    public DocumentWorkspace()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Store.PropertyChanged += OnStoreChanged;
            Sync();
        };
        Unloaded += (_, _) => Store.PropertyChanged -= OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceStore.SelectedFile):
            case nameof(WorkspaceStore.EditorMode):
            case nameof(WorkspaceStore.IsDirty):
                Dispatcher.BeginInvoke(new Action(Sync));
                break;
            case nameof(WorkspaceStore.IsSidebarHidden):
                Dispatcher.BeginInvoke(new Action(ApplyFullscreen));
                break;
        }
    }

    /// <summary>
    /// "全屏阅读"模式: Mac 版的 IsSidebarHidden 不光收 sidebar, 还把 Outline 列
    /// 也收起来, 整个文档独占, 减少干扰。我们这边同步处理。
    /// </summary>
    private void ApplyFullscreen()
    {
        if (Store.IsSidebarHidden)
        {
            OutlineCol.Width = new GridLength(0);
            FullscreenIcon.Text = "⛗"; // 进入全屏后图标换"退出"样式
            BtnFullscreen.ToolTip = "Exit distraction-free mode (Ctrl+\\)";
        }
        else
        {
            OutlineCol.Width = new GridLength(240);
            FullscreenIcon.Text = "⛶";
            BtnFullscreen.ToolTip = "Toggle distraction-free mode (Ctrl+\\)";
        }
    }

    private void Sync()
    {
        // 标题 + dirty
        if (Store.SelectedFile == null)
        {
            TitleLabel.Text = "No document";
            DirtyDot.Visibility = Visibility.Collapsed;
            BtnSave.IsEnabled = BtnExportPdf.IsEnabled = BtnExportWord.IsEnabled =
                BtnClose.IsEnabled = BtnDelete.IsEnabled = BtnDuplicate.IsEnabled = false;
        }
        else
        {
            TitleLabel.Text = System.IO.Path.GetFileNameWithoutExtension(Store.SelectedFile.Name);
            DirtyDot.Visibility = Store.IsDirty ? Visibility.Visible : Visibility.Collapsed;
            BtnSave.IsEnabled = Store.IsDirty;
            BtnExportPdf.IsEnabled = BtnExportWord.IsEnabled = BtnClose.IsEnabled =
                BtnDelete.IsEnabled = BtnDuplicate.IsEnabled = true;
        }

        // mode 按钮高亮
        SetActiveMode(Store.EditorMode);

        // 主体
        ContentHost.Children.Clear();
        if (Store.SelectedFile == null)
        {
            EditorToolbarHost.Visibility = Visibility.Collapsed;
            ContentHost.Children.Add(BuildWelcome());
            return;
        }
        switch (Store.EditorMode)
        {
            case EditorMode.Read:
                EditorToolbarHost.Visibility = Visibility.Collapsed;
                _readView ??= new MarkdownPreview { IsEditable = false };
                ContentHost.Children.Add(_readView);
                break;
            case EditorMode.Rich:
                EditorToolbarHost.Visibility = Visibility.Visible;
                _richView ??= new MarkdownPreview { IsEditable = true };
                ContentHost.Children.Add(_richView);
                break;
            case EditorMode.Source:
                EditorToolbarHost.Visibility = Visibility.Collapsed;
                _sourceView ??= new SourceEditor();
                ContentHost.Children.Add(_sourceView);
                break;
        }
    }

    private FrameworkElement BuildWelcome()
    {
        var sp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sp.Children.Add(new TextBlock
        {
            Text = "ITforce Markdown",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Brush.Ink"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Open a folder on the left to begin, or drag-and-drop a .md file here.",
            FontSize = 13,
            Foreground = (Brush)FindResource("Brush.Muted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return sp;
    }

    private void SetActiveMode(EditorMode m)
    {
        BtnRead.FontWeight = m == EditorMode.Read ? FontWeights.Bold : FontWeights.Normal;
        BtnRich.FontWeight = m == EditorMode.Rich ? FontWeights.Bold : FontWeights.Normal;
        BtnSource.FontWeight = m == EditorMode.Source ? FontWeights.Bold : FontWeights.Normal;
        BtnRead.Foreground = m == EditorMode.Read ? (Brush)FindResource("Brush.Accent") : (Brush)FindResource("Brush.Muted");
        BtnRich.Foreground = m == EditorMode.Rich ? (Brush)FindResource("Brush.Accent") : (Brush)FindResource("Brush.Muted");
        BtnSource.Foreground = m == EditorMode.Source ? (Brush)FindResource("Brush.Accent") : (Brush)FindResource("Brush.Muted");
    }

    // ─────────────────── 按钮 handlers ───────────────────
    private void ModeRead_Click(object sender, RoutedEventArgs e)   => Store.EditorMode = EditorMode.Read;
    private void ModeRich_Click(object sender, RoutedEventArgs e)   => Store.EditorMode = EditorMode.Rich;
    private void ModeSource_Click(object sender, RoutedEventArgs e) => Store.EditorMode = EditorMode.Source;

    private void Save_Click(object sender, RoutedEventArgs e) => Store.SaveCurrent();
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Store.IsDirty)
        {
            var r = MessageBox.Show(
                "You have unsaved changes. Save before closing?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !Store.SaveCurrent()) return;
        }
        Store.CloseDocument();
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (Store.SelectedFile == null) return;
        await Exporter.ExportPdfAsync(Window.GetWindow(this)!, Store.SourceDraft, Store.SelectedFile.Name);
    }

    private void ExportWord_Click(object sender, RoutedEventArgs e)
    {
        if (Store.SelectedFile == null) return;
        Exporter.ExportWord(Window.GetWindow(this)!, Store.SourceDraft, Store.SelectedFile.Name);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Store.SelectedFile == null) return;
        var name = Store.SelectedFile.Name;
        var r = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Move \"{name}\" to the Recycle Bin?\n\nYou can restore it from the Recycle Bin later.",
            "Delete document",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        Store.DeleteCurrent();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Store.SelectedFile == null) return;
        // 有未保存改动时先 save, 不然副本是磁盘上的旧版本, 用户会困惑
        if (Store.IsDirty)
        {
            var r = MessageBox.Show(
                Window.GetWindow(this)!,
                "Save current changes before duplicating?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) Store.SaveCurrent();
        }
        Store.DuplicateCurrent();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
        => Store.IsSidebarHidden = !Store.IsSidebarHidden;
}
