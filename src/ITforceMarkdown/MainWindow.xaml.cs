using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ITforceMarkdown.Models;
using ITforceMarkdown.Services;
using ITforceMarkdown.Stores;
using Microsoft.Win32;

namespace ITforceMarkdown;

/// <summary>
/// 主窗口: 三栏布局 (Sidebar | TopActionBar+DocumentWorkspace) + 全菜单。
///
/// 职责:
///   - 同步标题 (含 dirty marker · 文档名)
///   - 拦截 ⌘W / Alt+F4 关闭, 有 dirty 时弹 Save/Don't/Cancel
///   - 拖拽 .md 文件打开
///   - Open Recent 菜单根据 store 动态刷新
///   - View ▸ Appearance 菜单项打勾跟 store.Appearance 同步
/// </summary>
public partial class MainWindow : Window
{
    private WorkspaceStore Store => App.Store;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 按 flavor 装图标 (Local / Pro)。XAML 里设 pack URI 在 single-file
        // 自解压场景偶发解析失败, 代码里设更稳。
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(App.IconPackUri));
        }
        catch { /* 图标加载失败不影响功能 */ }

        Store.PropertyChanged += OnStoreChanged;
        UpdateTitle();
        UpdateAppearanceCheck();
        BuildRecentMenu();

        Store.RecentDocuments.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(BuildRecentMenu));
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceStore.SelectedFile):
            case nameof(WorkspaceStore.IsDirty):
                Dispatcher.BeginInvoke(new Action(UpdateTitle));
                break;
            case nameof(WorkspaceStore.IsSidebarHidden):
                Dispatcher.BeginInvoke(new Action(ApplySidebarVisibility));
                break;
            case nameof(WorkspaceStore.Appearance):
                Dispatcher.BeginInvoke(new Action(UpdateAppearanceCheck));
                break;
            case nameof(WorkspaceStore.StatusMessage):
                Dispatcher.BeginInvoke(new Action(() => StatusBar.Text = Store.StatusMessage));
                break;
        }
    }

    private void UpdateTitle()
    {
        var dirty = Store.IsDirty ? " — Edited" : "";
        var doc = Store.SelectedFile != null
            ? Path.GetFileNameWithoutExtension(Store.SelectedFile.Name)
            : "Untitled";
        Title = $"{doc}{dirty} — {App.ProductName}";
    }

    private void UpdateAppearanceCheck()
    {
        MenuApSystem.IsChecked = Store.Appearance == AppearancePreference.System;
        MenuApLight.IsChecked  = Store.Appearance == AppearancePreference.Light;
        MenuApDark.IsChecked   = Store.Appearance == AppearancePreference.Dark;
    }

    private void ApplySidebarVisibility()
    {
        SidebarCol.Width = Store.IsSidebarHidden ? new GridLength(0) : new GridLength(280);
    }

    // ─────────────────── Open Recent dynamic menu ───────────────────
    private void BuildRecentMenu()
    {
        MenuRecent.Items.Clear();
        if (Store.RecentDocuments.Count == 0)
        {
            var empty = new MenuItem { Header = "(empty)", IsEnabled = false };
            MenuRecent.Items.Add(empty);
            return;
        }
        foreach (var p in Store.RecentDocuments)
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(p),
                ToolTip = p,
            };
            var capturedPath = p;
            item.Click += (_, _) => Store.OpenExternalFile(capturedPath);
            MenuRecent.Items.Add(item);
        }
        MenuRecent.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear Menu" };
        clear.Click += (_, _) => Store.ClearRecentDocuments();
        MenuRecent.Items.Add(clear);
    }

    // ─────────────────── Drag & Drop ───────────────────
    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".md" or ".markdown" && File.Exists(f))
                Store.OpenExternalFile(f);
        }
    }

    // ─────────────────── Close confirm ───────────────────
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!Store.IsDirty) return;
        var r = MessageBox.Show(
            this,
            "You have unsaved changes. Save before closing?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (r == MessageBoxResult.Yes && !Store.SaveCurrent()) e.Cancel = true;
    }

    // ─────────────────── File 菜单 ───────────────────
    private void MenuNew_Click(object sender, RoutedEventArgs e) => Store.CreateDocument();
    private void MenuOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Markdown file",
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true) Store.OpenExternalFile(dlg.FileName);
    }
    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a workspace folder" };
        if (dlg.ShowDialog(this) == true) Store.AddWorkspace(dlg.FolderName);
    }
    private void MenuSave_Click(object sender, RoutedEventArgs e) => Store.SaveCurrent();
    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        if (Store.IsDirty)
        {
            var r = MessageBox.Show(this, "You have unsaved changes. Save before closing?",
                "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !Store.SaveCurrent()) return;
        }
        Store.CloseDocument();
    }
    private async void MenuExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (Store.SelectedFile == null) return;
        await Exporter.ExportPdfAsync(this, Store.SourceDraft, Store.SelectedFile.Name);
    }
    private void MenuExportWord_Click(object sender, RoutedEventArgs e)
    {
        if (Store.SelectedFile == null) return;
        Exporter.ExportWord(this, Store.SourceDraft, Store.SelectedFile.Name);
    }
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // ─────────────────── View 菜单 ───────────────────
    private void MenuModeRead_Click(object sender, RoutedEventArgs e)   => Store.EditorMode = EditorMode.Read;
    private void MenuModeRich_Click(object sender, RoutedEventArgs e)   => Store.EditorMode = EditorMode.Rich;
    private void MenuModeSource_Click(object sender, RoutedEventArgs e) => Store.EditorMode = EditorMode.Source;

    private void MenuApSystem_Click(object sender, RoutedEventArgs e) => Store.Appearance = AppearancePreference.System;
    private void MenuApLight_Click(object sender, RoutedEventArgs e)  => Store.Appearance = AppearancePreference.Light;
    private void MenuApDark_Click(object sender, RoutedEventArgs e)   => Store.Appearance = AppearancePreference.Dark;

    private void MenuToggleSidebar_Click(object sender, RoutedEventArgs e)
        => Store.IsSidebarHidden = !Store.IsSidebarHidden;

    private void MenuBack_Click(object sender, RoutedEventArgs e) => Store.GoBack();
    private void MenuForward_Click(object sender, RoutedEventArgs e) => Store.GoForward();

    // ─────────────────── Window 菜单 ───────────────────
    private void MenuMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MenuMaxRestore_Click(object sender, RoutedEventArgs e)
        => WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;

    // ─────────────────── Help 菜单 ───────────────────
    private void MenuHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.fieldsone.com",
                UseShellExecute = true,
            });
        }
        catch { /* ignore */ }
    }
    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var asmVer = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        MessageBox.Show(this,
            $"{App.ProductName} — Windows\n\nv{asmVer}\n.NET {Environment.Version}\n{Environment.OSVersion}\n\n© ITforce",
            $"About {App.ProductName}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
