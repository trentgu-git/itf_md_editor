using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ITforceMarkdown.Models;
using ITforceMarkdown.Services;

namespace ITforceMarkdown.Stores;

/// <summary>
/// 全局 App 状态 — 对应 Mac 版 WorkspaceStore (ObservableObject)。
///
/// 单例, 通过 App.Store 全局访问 (跟 Mac @StateObject 等价)。
/// 持有:
///   - 用户挂载的所有 workspace 列表 + 每个 workspace 的文件树
///   - 当前打开的文档 (路径 + 源码草稿 + dirty 状态)
///   - 浏览历史 (Back/Forward 栈)
///   - 用户偏好 (EditorMode, Appearance, HideEmptyFolders, RecentDocuments)
///   - 搜索文本 + 结果
///
/// 所有 @Published 字段用 [ObservableProperty] source generator 生成,
/// XAML 双向绑定到对应 PascalCase 公开属性。
/// </summary>
public partial class WorkspaceStore : ObservableObject
{
    // ─────────────────── 常量 / persistence key ───────────────────
    private const string KeyLocalWorkspaces = "localWorkspaces";
    private const string KeyRecentDocuments = "recentDocuments";
    private const string KeyAppearance      = "appearance";
    private const string KeyHideEmpty       = "hideEmptyFolders";
    private const string KeyLastFile        = "lastSelectedFile";
    private const int    RecentDocumentsLimit = 15;

    // ─────────────────── 可观察状态 ───────────────────

    /// <summary>所有挂载的本地 workspace, sidebar 按这个顺序显示。</summary>
    public ObservableCollection<Workspace> LocalWorkspaces { get; } = new();

    /// <summary>每个 workspace 当前的文件树, 按 workspace.Id 索引。</summary>
    public Dictionary<Guid, List<FileNode>> WorkspaceTrees { get; } = new();

    [ObservableProperty]
    private FileNode? selectedFile;

    /// <summary>正在编辑的源码草稿 (单一来源)。</summary>
    [ObservableProperty]
    private string sourceDraft = "";

    /// <summary>磁盘上的最后保存版本, 用来判断 dirty。</summary>
    [ObservableProperty]
    private string lastSavedSource = "";

    /// <summary>当前编辑模式 (Read / Rich / Source)。</summary>
    [ObservableProperty]
    private EditorMode editorMode = EditorMode.Read;

    /// <summary>外观偏好 (System / Light / Dark)。</summary>
    [ObservableProperty]
    private AppearancePreference appearance = AppearancePreference.System;

    /// <summary>全局 sidebar 是否完全隐藏 (全屏阅读)。</summary>
    [ObservableProperty]
    private bool isSidebarHidden = false;

    /// <summary>统一的"隐藏空文件夹"开关 (跨 workspace 全局兜底)。</summary>
    [ObservableProperty]
    private bool hideEmptyFoldersGlobal = true;

    /// <summary>顶部状态栏文字。</summary>
    [ObservableProperty]
    private string statusMessage = "Ready";

    /// <summary>错误 toast 文字, 非空时 UI 上显示一个浮层。</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>搜索框文字。</summary>
    [ObservableProperty]
    private string searchText = "";

    public ObservableCollection<DocumentSearchResult> SearchResults { get; } = new();

    /// <summary>Open Recent 列表 (15 上限, 最新在前)。</summary>
    public ObservableCollection<string> RecentDocuments { get; } = new();

    /// <summary>浏览历史: ⌘[ 回退栈。</summary>
    public Stack<string> BackStack { get; } = new();

    /// <summary>浏览历史: ⌘] 前进栈。</summary>
    public Stack<string> ForwardStack { get; } = new();

    public bool CanGoBack    => BackStack.Count    > 0;
    public bool CanGoForward => ForwardStack.Count > 0;

    /// <summary>当前文档是否有未保存修改。</summary>
    public bool IsDirty => SelectedFile != null && SourceDraft != LastSavedSource;

    // ─────────────────── 内部 ───────────────────
    private bool _isNavigatingHistory;

    // ─────────────────── 构造 ───────────────────
    public WorkspaceStore()
    {
        // 1. 加载持久化的偏好 (早一点加载, 让窗口一出来就是正确主题)
        var appearanceRaw = PersistenceService.LoadString(KeyAppearance);
        if (Enum.TryParse<AppearancePreference>(appearanceRaw, ignoreCase: true, out var ap))
            Appearance = ap;

        var hideEmpty = PersistenceService.LoadValue<bool>(KeyHideEmpty);
        if (hideEmpty.HasValue) HideEmptyFoldersGlobal = hideEmpty.Value;

        // 2. 加载 workspace 列表
        var ws = PersistenceService.Load<List<Workspace>>(KeyLocalWorkspaces);
        if (ws != null)
        {
            foreach (var w in ws.Where(w => w.Exists()))
            {
                LocalWorkspaces.Add(w);
                WorkspaceTrees[w.Id] = WorkspaceScanner.Scan(w.Path);
            }
        }

        // 3. 加载 recent
        var recents = PersistenceService.Load<List<string>>(KeyRecentDocuments);
        if (recents != null)
        {
            foreach (var p in recents.Where(File.Exists))
                RecentDocuments.Add(p);
        }

        // 4. 恢复上次打开的文件
        var lastFile = PersistenceService.LoadString(KeyLastFile);
        if (lastFile != null && File.Exists(lastFile))
            OpenExternalFile(lastFile);
    }

    // ─────────────────── persistence reactions ───────────────────
    partial void OnAppearanceChanged(AppearancePreference value)
        => PersistenceService.SaveString(KeyAppearance, value.ToString());

    partial void OnHideEmptyFoldersGlobalChanged(bool value)
        => PersistenceService.Save(KeyHideEmpty, value);

    partial void OnSelectedFileChanged(FileNode? oldValue, FileNode? newValue)
    {
        // 触发 IsDirty 重新计算
        OnPropertyChanged(nameof(IsDirty));
        if (newValue != null)
            PersistenceService.SaveString(KeyLastFile, newValue.Path);
        else
            PersistenceService.Delete(KeyLastFile);
    }

    partial void OnSourceDraftChanged(string value)
        => OnPropertyChanged(nameof(IsDirty));

    partial void OnLastSavedSourceChanged(string value)
        => OnPropertyChanged(nameof(IsDirty));

    // ─────────────────── workspace 管理 ───────────────────

    public void AddWorkspace(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        if (LocalWorkspaces.Any(w => string.Equals(w.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
            return; // 已存在

        var ws = new Workspace { Path = folderPath };
        LocalWorkspaces.Add(ws);
        WorkspaceTrees[ws.Id] = WorkspaceScanner.Scan(folderPath);
        PersistWorkspaces();
        StatusMessage = $"Added workspace: {ws.Name}";
    }

    public void RemoveWorkspace(Workspace ws)
    {
        LocalWorkspaces.Remove(ws);
        WorkspaceTrees.Remove(ws.Id);
        PersistWorkspaces();
    }

    public void ToggleWorkspaceExpanded(Workspace ws)
    {
        ws.IsExpanded = !ws.IsExpanded;
        PersistWorkspaces();
        OnPropertyChanged(nameof(LocalWorkspaces));
    }

    public void ToggleWorkspaceHideEmpty(Workspace ws)
    {
        ws.HideEmptyFolders = !ws.HideEmptyFolders;
        PersistWorkspaces();
        OnPropertyChanged(nameof(LocalWorkspaces));
    }

    public void RefreshAllTrees()
    {
        foreach (var ws in LocalWorkspaces)
            WorkspaceTrees[ws.Id] = WorkspaceScanner.Scan(ws.Path);
        OnPropertyChanged(nameof(WorkspaceTrees));
        OnPropertyChanged(nameof(LocalWorkspaces));
    }

    public List<FileNode> VisibleTreeFor(Workspace ws)
    {
        if (!WorkspaceTrees.TryGetValue(ws.Id, out var raw)) return new();
        return ws.HideEmptyFolders ? WorkspaceScanner.FilterEmptyFolders(raw) : raw;
    }

    private void PersistWorkspaces()
        => PersistenceService.Save(KeyLocalWorkspaces, LocalWorkspaces.ToList());

    // ─────────────────── 文档打开 / 关闭 / 保存 ───────────────────

    /// <summary>
    /// 选中某个 FileNode 打开文档。如果当前文档 dirty, 弹出 Save/Discard/Cancel 由调用方处理 (UI 层)。
    /// 这里只做 "已经决定切换" 后的状态变更。
    /// </summary>
    public void SelectFile(FileNode file)
    {
        if (file.IsDirectory) return;
        if (SelectedFile != null && !_isNavigatingHistory)
        {
            // 当前文档 push 进 back stack, 清空 forward
            BackStack.Push(SelectedFile.Path);
            ForwardStack.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        SelectedFile = file;
        LoadFileIntoDraft(file.Path);
        AddRecentDocument(file.Path);
    }

    /// <summary>打开一个不在任何 workspace 内的外部 .md 文件。</summary>
    public void OpenExternalFile(string path)
    {
        if (!File.Exists(path)) return;

        // 自动把其父目录变成新 workspace (跟 Mac 版一致)
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) &&
            !LocalWorkspaces.Any(w => string.Equals(w.Path, parent, StringComparison.OrdinalIgnoreCase)))
        {
            AddWorkspace(parent);
        }

        var node = new FileNode
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsDirectory = false,
        };
        SelectFile(node);
    }

    public bool SaveCurrent()
    {
        if (SelectedFile == null) return false;
        try
        {
            File.WriteAllText(SelectedFile.Path, SourceDraft);
            LastSavedSource = SourceDraft;
            StatusMessage = $"Saved {SelectedFile.Name}";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Save failed: {ex.Message}";
            return false;
        }
    }

    public void CloseDocument()
    {
        SelectedFile = null;
        SourceDraft = "";
        LastSavedSource = "";
    }

    /// <summary>新建空 markdown 文件 — 在当前选中文件所在目录, 或第一个 workspace 根。</summary>
    public string? CreateDocument()
    {
        string? dir = null;
        if (SelectedFile != null)
            dir = Path.GetDirectoryName(SelectedFile.Path);
        if (string.IsNullOrEmpty(dir) && LocalWorkspaces.Count > 0)
            dir = LocalWorkspaces[0].Path;
        if (string.IsNullOrEmpty(dir)) return null;

        var idx = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, idx == 1 ? "Untitled.md" : $"Untitled {idx}.md");
            idx++;
        } while (File.Exists(candidate));

        try
        {
            File.WriteAllText(candidate, "# New document\n\n");
            RefreshAllTrees();
            var node = new FileNode
            {
                Path = candidate,
                Name = Path.GetFileName(candidate),
                IsDirectory = false,
            };
            SelectFile(node);
            return candidate;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Create failed: {ex.Message}";
            return null;
        }
    }

    private void LoadFileIntoDraft(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            SourceDraft = content;
            LastSavedSource = content;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Open failed: {ex.Message}";
        }
    }

    // ─────────────────── Recent ───────────────────

    private void AddRecentDocument(string path)
    {
        var idx = -1;
        for (int i = 0; i < RecentDocuments.Count; i++)
        {
            if (string.Equals(RecentDocuments[i], path, StringComparison.OrdinalIgnoreCase))
            { idx = i; break; }
        }
        if (idx >= 0) RecentDocuments.RemoveAt(idx);
        RecentDocuments.Insert(0, path);
        while (RecentDocuments.Count > RecentDocumentsLimit)
            RecentDocuments.RemoveAt(RecentDocuments.Count - 1);
        PersistenceService.Save(KeyRecentDocuments, RecentDocuments.ToList());
    }

    public void ClearRecentDocuments()
    {
        RecentDocuments.Clear();
        PersistenceService.Save(KeyRecentDocuments, new List<string>());
    }

    // ─────────────────── 浏览历史 ───────────────────

    public void GoBack()
    {
        if (BackStack.Count == 0) return;
        var target = BackStack.Pop();
        if (SelectedFile != null) ForwardStack.Push(SelectedFile.Path);

        _isNavigatingHistory = true;
        try { OpenExternalFile(target); }
        finally { _isNavigatingHistory = false; }
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    public void GoForward()
    {
        if (ForwardStack.Count == 0) return;
        var target = ForwardStack.Pop();
        if (SelectedFile != null) BackStack.Push(SelectedFile.Path);

        _isNavigatingHistory = true;
        try { OpenExternalFile(target); }
        finally { _isNavigatingHistory = false; }
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    // ─────────────────── reload key (跟 Mac 版对位) ───────────────────

    /// <summary>预览 WebView 的 reload key, 包含 appearance + 内容 hash。
    /// 变化时 MarkdownPreview 重新 load。</summary>
    public string RenderedHtmlReloadKey
        => $"{SelectedFile?.Path ?? "none"}|preview|{Appearance}|{SourceDraft.GetHashCode()}";

    public string RichEditorReloadKey
        => $"{SelectedFile?.Path ?? "none"}|rich|{Appearance}";
}
