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
            UpdateContent();
            UpdateHeader();
        };
        Unloaded += (_, _) => Store.PropertyChanged -= OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // ⚠️ 只有"文件切换"或"显示模式切换"这两种情况才能动 ContentHost.
            // 如果在 IsDirty 也走 UpdateContent, 每个 keystroke 都会 detach 再 attach
            // WebView2 控件, 光标位置丢失, 用户感觉是"body 编辑不了"。
            case nameof(WorkspaceStore.SelectedFile):
            case nameof(WorkspaceStore.EditorMode):
                Dispatcher.BeginInvoke(new Action(() => { UpdateContent(); UpdateHeader(); }));
                break;
            // IsDirty 只更新标题里的圆点 + Save 按钮, 不重建视图
            case nameof(WorkspaceStore.IsDirty):
                Dispatcher.BeginInvoke(new Action(UpdateHeader));
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

    /// <summary>
    /// 只更新 header (title / dirty dot / button enabled / mode 高亮)。
    /// 不触碰 ContentHost — 安全到每个 keystroke 都可以调。
    /// </summary>
    private void UpdateHeader()
    {
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
        SetActiveMode(Store.EditorMode);
    }

    /// <summary>
    /// 重建 ContentHost 内容。**只在 SelectedFile / EditorMode 变化时调**,
    /// 千万不要因为 IsDirty (每个 keystroke 都变) 来调, 否则 WebView 被反复
    /// detach/attach, 光标位置丢失 → 用户感觉是"打字闪屏 / body 编辑不了"。
    /// </summary>
    private void UpdateContent()
    {
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
                AttachOnce(_readView);
                ContentHost.Children.Add(_readView);
                break;
            case EditorMode.Rich:
                EditorToolbarHost.Visibility = Visibility.Visible;
                _richView ??= new MarkdownPreview { IsEditable = true };
                AttachOnce(_richView);
                ContentHost.Children.Add(_richView);
                break;
            case EditorMode.Source:
                EditorToolbarHost.Visibility = Visibility.Collapsed;
                _sourceView ??= new SourceEditor();
                AttachOnce(_sourceView);
                ContentHost.Children.Add(_sourceView);
                break;
        }
    }

    /// <summary>
    /// 从之前的父容器里摘下来 (如果有) — 一个 UIElement 只能有一个 parent,
    /// 我们 cache 了 _readView/_richView/_sourceView 三个实例反复用, 切换 mode 时
    /// 必须先 detach 才能 attach 到新容器, 不然 InvalidOperationException。
    /// </summary>
    private static void AttachOnce(UIElement element)
    {
        if (element is FrameworkElement fe && fe.Parent is Panel parent)
            parent.Children.Remove(element);
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
            Text = App.ProductName,
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
