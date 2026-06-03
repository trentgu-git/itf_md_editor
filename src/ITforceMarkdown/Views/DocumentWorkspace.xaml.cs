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
        }
    }

    private void Sync()
    {
        // 标题 + dirty
        if (Store.SelectedFile == null)
        {
            TitleLabel.Text = "No document";
            DirtyDot.Visibility = Visibility.Collapsed;
            BtnSave.IsEnabled = BtnExportPdf.IsEnabled = BtnExportWord.IsEnabled = BtnClose.IsEnabled = false;
        }
        else
        {
            TitleLabel.Text = System.IO.Path.GetFileNameWithoutExtension(Store.SelectedFile.Name);
            DirtyDot.Visibility = Store.IsDirty ? Visibility.Visible : Visibility.Collapsed;
            BtnSave.IsEnabled = Store.IsDirty;
            BtnExportPdf.IsEnabled = BtnExportWord.IsEnabled = BtnClose.IsEnabled = true;
        }

        // mode 按钮高亮
        SetActiveMode(Store.EditorMode);

        // 主体
        ContentHost.Children.Clear();
        if (Store.SelectedFile == null)
        {
            ContentHost.Children.Add(BuildWelcome());
            return;
        }
        switch (Store.EditorMode)
        {
            case EditorMode.Read:
                _readView ??= new MarkdownPreview { IsEditable = false };
                ContentHost.Children.Add(_readView);
                break;
            case EditorMode.Rich:
                _richView ??= new MarkdownPreview { IsEditable = true };
                ContentHost.Children.Add(_richView);
                break;
            case EditorMode.Source:
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
}
