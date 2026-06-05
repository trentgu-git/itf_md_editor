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

        // Pro flavor 才显示 GitLab 子菜单. 用 #if PRO 而不是
        // `if (App.IsProEdition)` — 后者 IsProEdition 是 const bool,
        // Local build 时编译器直接判定 body unreachable, 报 CS0162 warning.
#if PRO
        MenuGitLab.Visibility = Visibility.Visible;
        MenuGitLabSep1.Visibility = Visibility.Visible;
#endif

        Store.PropertyChanged += OnStoreChanged;
        UpdateTitle();
        UpdateAppearanceCheck();
        UpdateLanguageCheck();
        ApplyLocalization();
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
            case nameof(WorkspaceStore.MenuLanguage):
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateLanguageCheck();
                    ApplyLocalization();
                    BuildRecentMenu();  // "Clear Menu" label 也跟着切
                }));
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

    private void UpdateLanguageCheck()
    {
        MenuLangSystem.IsChecked  = Store.MenuLanguage == Models.MenuLanguage.System;
        MenuLangEnglish.IsChecked = Store.MenuLanguage == Models.MenuLanguage.English;
        MenuLangChinese.IsChecked = Store.MenuLanguage == Models.MenuLanguage.Chinese;
    }

    /// <summary>
    /// 根据 Store.MenuLanguage 重设所有 MenuItem.Header. 加下划线表示
    /// Alt+letter 快捷键. Appearance / Language 子选项的标签也一并切.
    /// </summary>
    private void ApplyLocalization()
    {
        var lang = Store.MenuLanguage;
        string T(string en, string zh) => L10n.T(lang, en, zh);

        // ── File ──
        MenuFile.Header         = T("_File", "文件(_F)");
        MenuNew.Header          = T("_New", "新建(_N)");
        MenuOpenFile.Header     = T("_Open File…", "打开文件…(_O)");
        MenuOpenFolder.Header   = T("Open _Folder…", "打开文件夹…(_F)");
        MenuRecent.Header       = T("Open _Recent", "最近打开(_R)");
        MenuSave.Header         = T("_Save", "保存(_S)");
        MenuClose.Header        = T("_Close Document", "关闭文档(_C)");
        MenuExportPdf.Header    = T("Export _PDF…", "导出为 PDF…(_P)");
        MenuExportWord.Header   = T("Export _Word…", "导出为 Word…(_W)");
        MenuExit.Header         = T("E_xit", "退出(_X)");

        // Pro only — GitLab. flavor 关闭时这俩还在但 visibility=collapsed.
        MenuGitLab.Header         = T("_GitLab", "GitLab(_G)");
        MenuGitLabSettings.Header = T("GitLab _Settings…", "GitLab 设置…(_S)");
        MenuGitLabSync.Header     = T("Sync _Now", "立即同步(_N)");

        // ── Edit ──
        MenuEdit.Header            = T("_Edit", "编辑(_E)");
        MenuEditUndo.Header        = T("Undo",    "撤销");
        MenuEditRedo.Header        = T("Redo",    "重做");
        MenuEditCut.Header         = T("Cut",     "剪切");
        MenuEditCopy.Header        = T("Copy",    "复制");
        MenuEditPaste.Header       = T("Paste",   "粘贴");
        MenuEditSelectAll.Header   = T("Select All", "全选");

        // ── View ──
        MenuView.Header           = T("_View", "视图(_V)");
        MenuModeRead.Header       = T("_Read",   "阅读(_R)");
        MenuModeRich.Header       = T("_Edit",   "编辑(_E)");
        MenuModeSource.Header     = T("_Source", "源码(_S)");
        MenuAppearance.Header     = T("_Appearance", "外观(_A)");
        MenuApSystem.Header       = T("Follow System", "跟随系统");
        MenuApLight.Header        = T("Light",         "浅色");
        MenuApDark.Header         = T("Dark",          "深色");
        MenuLanguage.Header       = T("_Language", "语言(_L)");
        // Language 三个子项的 label 保持双语并排 (用户能从两种语言下都认出),
        // 不调用 T(...).
        MenuToggleSidebar.Header  = T("Toggle _Sidebar", "切换侧栏(_S)");
        MenuBack.Header           = T("_Back",    "后退(_B)");
        MenuForward.Header        = T("_Forward", "前进(_F)");

        // ── Window ──
        MenuWindow.Header              = T("_Window",              "窗口(_W)");
        MenuWindowMin.Header           = T("_Minimize",            "最小化(_M)");
        MenuWindowMaxRestore.Header    = T("_Maximize / Restore",  "最大化 / 还原(_M)");

        // ── Help ──
        MenuHelp.Header       = T("_Help",                    "帮助(_H)");
        MenuHelpDocs.Header   = T("ITforce Markdown _Help",   "ITforce Markdown 帮助(_H)");
        MenuHelpAbout.Header  = T("_About",                   "关于(_A)");
    }

    private void MenuLangSystem_Click(object sender, RoutedEventArgs e)  => Store.MenuLanguage = Models.MenuLanguage.System;
    private void MenuLangEnglish_Click(object sender, RoutedEventArgs e) => Store.MenuLanguage = Models.MenuLanguage.English;
    private void MenuLangChinese_Click(object sender, RoutedEventArgs e) => Store.MenuLanguage = Models.MenuLanguage.Chinese;

    private void ApplySidebarVisibility()
    {
        SidebarCol.Width = Store.IsSidebarHidden ? new GridLength(0) : new GridLength(280);
    }

    // ─────────────────── Open Recent dynamic menu ───────────────────
    private void BuildRecentMenu()
    {
        var lang = Store.MenuLanguage;
        MenuRecent.Items.Clear();
        if (Store.RecentDocuments.Count == 0)
        {
            var empty = new MenuItem { Header = L10n.T(lang, "(empty)", "(无)"), IsEnabled = false };
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
        var clear = new MenuItem { Header = L10n.T(lang, "Clear Menu", "清除列表") };
        clear.Click += (_, _) => Store.ClearRecentDocuments();
        MenuRecent.Items.Add(clear);
    }

    // ─────────────────── 键盘快捷键 ───────────────────
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!ctrl) return;
        // 当 WebView2 / TextBox 在 focus 里时, 它们自己会处理 Ctrl+S/Z 等;
        // 但我们的快捷键是 app 级别的, 让它们也生效.
        switch (e.Key)
        {
            case Key.S:   Store.SaveCurrent(); e.Handled = true; break;
            case Key.N:   Store.CreateDocument(); e.Handled = true; break;
            case Key.O:
                // 跟 MenuOpenFile_Click 一样的逻辑, 直接复用
                MenuOpenFile_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.OemOpenBrackets:   /* 历史栈预留 */ break;
            case Key.OemCloseBrackets:  /* 历史栈预留 */ break;
            case Key.OemBackslash:
                Store.IsSidebarHidden = !Store.IsSidebarHidden;
                e.Handled = true;
                break;
            case Key.OemPlus:   Store.ZoomIn();    e.Handled = true; break;
            case Key.OemMinus:  Store.ZoomOut();   e.Handled = true; break;
            case Key.D0:        Store.ZoomReset(); e.Handled = true; break;
            case Key.R:         Store.ReloadFromDisk(); e.Handled = true; break;
            // Ctrl+F 不拦 — 让 WebView2 自家的 Find UI 接管 (browser default)
        }
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

    // ─────────────────── GitLab (Pro only) ───────────────────
    private void MenuGitLabSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.GitLabSettingsDialog { Owner = this };
        dlg.ShowDialog();
    }

    private async void MenuGitLabSync_Click(object sender, RoutedEventArgs e)
    {
        // 快速 sync — 不弹完整对话框, 直接拿当前设置跑。如果没配置过, 引导去 Settings。
        if (!Store.GitLabSettings.IsConfigured)
        {
            var r = MessageBox.Show(this,
                "GitLab is not configured yet. Open settings now?",
                "GitLab Sync",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (r == MessageBoxResult.OK)
                new Views.GitLabSettingsDialog { Owner = this }.ShowDialog();
            return;
        }

        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        Store.StatusMessage = "GitLab: syncing…";
        try
        {
#if PRO
            var result = await Services.GitLabService.SyncAsync(Store.GitLabSettings);
            Store.StatusMessage = (result.Success ? "GitLab: " : "GitLab error: ") + result.Message;
            if (result.Success)
                Store.AddWorkspace(Store.GitLabSettings.LocalCachePath);
#else
            await System.Threading.Tasks.Task.Delay(50);
            Store.StatusMessage = "GitLab is only available in the Pro edition.";
#endif
        }
        catch (Exception ex)
        {
            Store.StatusMessage = "GitLab error: " + ex.Message;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

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
