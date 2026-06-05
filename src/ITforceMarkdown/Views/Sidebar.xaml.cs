using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ITforceMarkdown.Models;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

/// <summary>
/// Sidebar: 多工作区文件树 + 搜索框。
/// 这里手动构建可视化树 (StackPanel + Button) 而不是 TreeView, 因为
/// TreeView 的样式覆盖太繁琐, 且我们需要紧凑的暖色高亮 / 折叠 chevron。
/// </summary>
public partial class Sidebar : UserControl
{
    private WorkspaceStore Store => App.Store;

    /// <summary>
    /// 搜索的 CancellationToken — 用户继续打字时取消上一次的搜索, 防止后台
    /// 任务堆积; 用户清空搜索框时也取消, 立刻回 workspace 树.
    /// </summary>
    private CancellationTokenSource? _searchCts;

    public Sidebar()
    {
        InitializeComponent();
        Loaded += (_, _) => HookStore();
    }

    private void HookStore()
    {
        Store.PropertyChanged += Store_PropertyChanged;
        Store.LocalWorkspaces.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private void Store_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceStore.SelectedFile):
            case nameof(WorkspaceStore.SearchText):
            // ToggleWorkspaceExpanded / ToggleWorkspaceHideEmpty 改了 ws 内部状态
            // 后会 OnPropertyChanged(nameof(LocalWorkspaces)) — 我们必须在这里
            // 重建, 否则灯泡 / chevron / 过滤后的文件列表都不更新。
            case nameof(WorkspaceStore.LocalWorkspaces):
            case nameof(WorkspaceStore.WorkspaceTrees):
                Dispatcher.BeginInvoke(new Action(Rebuild));
                break;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Store.SearchText = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─────────────────── 渲染 ───────────────────
    private void Rebuild()
    {
        // 任何 rebuild 触发都先取消还在跑的搜索 — 旧搜索完成后回 UI 时
        // 会发现 token cancelled 直接 return, 不会污染新视图.
        _searchCts?.Cancel();

        ContentHost.Children.Clear();

        var query = Store.SearchText?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            // 显示一个 loading 提示, 让用户知道在搜.
            ContentHost.Children.Add(new TextBlock
            {
                Text = "Searching…",
                Margin = new Thickness(16),
                Foreground = (Brush)FindResource("Brush.Muted"),
                FontSize = 12,
            });
            // 启动后台搜索. RenderSearchResultsAsync 在 ContinueWith 里 marshal
            // 回 UI 线程更新 ContentHost (不能在后台线程 touch UI).
            _searchCts = new CancellationTokenSource();
            _ = RenderSearchResultsAsync(query, _searchCts.Token);
        }
        else
        {
            RenderWorkspaces();
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (Store.LocalWorkspaces.Count == 0)
            StatusLabel.Text = "No workspaces yet";
        else
        {
            var totalFiles = Store.LocalWorkspaces.Sum(w =>
                Store.WorkspaceTrees.TryGetValue(w.Id, out var t)
                    ? CountFiles(t) : 0);
            StatusLabel.Text = $"{Store.LocalWorkspaces.Count} workspace(s) · {totalFiles} files";
        }
    }

    private static int CountFiles(List<FileNode> nodes)
    {
        int n = 0;
        foreach (var node in nodes)
        {
            if (node.IsDirectory) n += CountFiles(node.Children);
            else n++;
        }
        return n;
    }

    // ─────────────────── 搜索结果渲染 (异步, 可取消) ───────────────────
    /// <summary>
    /// 后台扫描所有 workspace 里的 .md 文件, 全文找子串. 大 workspace 也
    /// 不会卡 UI 因为整个 IO + 字符串匹配都在 Task.Run 里跑.
    ///
    /// 鲁棒性 (修过的崩溃模式):
    ///   - 取消 token 频繁 check, 用户继续打字 → 立刻 bail 不浪费 IO
    ///   - 大文件读取上限 (跳过 &gt; 2MB 的, 避免误读到压缩包 / SQLite 等)
    ///   - 每个文件 try/catch, 单个文件读失败不影响整个搜索
    ///   - 结果上限 200, 取到就停 (不再像老版扫完所有匹配再 Take(80))
    /// </summary>
    private async Task RenderSearchResultsAsync(string query, CancellationToken ct)
    {
        // 取所有 workspace + 树的快照 (后续在后台线程读, 不能再 touch Store).
        var snapshots = new List<(Workspace ws, List<FileNode> tree)>();
        foreach (var ws in Store.LocalWorkspaces)
        {
            if (Store.WorkspaceTrees.TryGetValue(ws.Id, out var t))
                snapshots.Add((ws, t));
        }

        // debounce 短暂等待 — 用户连续打字时, 取消 propagate 过来就直接 return.
        // 同时也让 UI 显示 "Searching…" 一段时间, 别一字一闪.
        try { await Task.Delay(180, ct); }
        catch (OperationCanceledException) { return; }

        // 后台扫.
        var results = await Task.Run(() =>
        {
            var hits = new List<(string ws, string filePath, string title, string rel, string snippet, int line)>();
            foreach (var (ws, tree) in snapshots)
            {
                if (ct.IsCancellationRequested) return hits;
                foreach (var f in EnumerateFilesFlat(tree))
                {
                    if (ct.IsCancellationRequested) return hits;
                    try
                    {
                        // 跳过过大的文件 (>2MB) — 用户拖 PDF/zip 进 workspace 时不卡住
                        var info = new FileInfo(f.Path);
                        if (!info.Exists || info.Length > 2 * 1024 * 1024) continue;

                        var lines = File.ReadAllLines(f.Path);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (ct.IsCancellationRequested) return hits;
                            if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                string rel;
                                try { rel = Path.GetRelativePath(ws.Path, f.Path); }
                                catch { rel = f.Path; }
                                hits.Add((ws.Path, f.Path,
                                    Path.GetFileNameWithoutExtension(f.Path),
                                    rel, lines[i].Trim(), i + 1));
                                if (hits.Count >= 200) return hits;
                            }
                        }
                    }
                    catch { /* skip unreadable */ }
                }
            }
            return hits;
        }, ct).ConfigureAwait(false);

        // 回 UI 线程渲染结果. 期间用户可能继续打字 → token cancelled → 不动 UI.
        if (ct.IsCancellationRequested) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            ContentHost.Children.Clear();
            if (results.Count == 0)
            {
                ContentHost.Children.Add(new TextBlock
                {
                    Text = "No results.",
                    Margin = new Thickness(16),
                    Foreground = (Brush)FindResource("Brush.Muted"),
                    FontSize = 12,
                });
                return;
            }
            foreach (var r in results.Take(80))
            {
                var b = new Button
                {
                    Margin = new Thickness(8, 2, 8, 2),
                    Padding = new Thickness(10, 8, 10, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    BorderThickness = new Thickness(0),
                    Background = (Brush)FindResource("Brush.Card"),
                    Cursor = Cursors.Hand,
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = r.title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("Brush.Ink"),
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"line {r.line} · {r.rel}",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("Brush.Muted"),
                    Margin = new Thickness(0, 2, 0, 4),
                });
                sp.Children.Add(new TextBlock
                {
                    Text = r.snippet,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("Brush.Muted"),
                    TextWrapping = TextWrapping.Wrap,
                });
                b.Content = sp;
                var capturedPath = r.filePath;
                b.Click += (_, _) => Store.OpenExternalFile(capturedPath);
                ContentHost.Children.Add(b);
            }
        });
    }

    private static IEnumerable<FileNode> EnumerateFilesFlat(IEnumerable<FileNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.IsDirectory)
            {
                foreach (var x in EnumerateFilesFlat(n.Children)) yield return x;
            }
            else yield return n;
        }
    }

    private static void CollectFiles(List<FileNode> nodes, Action<FileNode> visit)
    {
        foreach (var n in nodes)
        {
            if (n.IsDirectory) CollectFiles(n.Children, visit);
            else visit(n);
        }
    }

    // ─────────────────── workspace 列表渲染 ───────────────────
    private void RenderWorkspaces()
    {
        if (Store.LocalWorkspaces.Count == 0)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = "No workspaces yet.\nClick '+ Open Folder' above to add one.",
                Margin = new Thickness(16),
                Foreground = (Brush)FindResource("Brush.Muted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var ws in Store.LocalWorkspaces)
        {
            var isCurrent = Store.SelectedFile != null &&
                            Store.SelectedFile.Path.StartsWith(ws.Path, StringComparison.OrdinalIgnoreCase);

            // header
            var header = BuildWorkspaceHeader(ws, isCurrent);
            ContentHost.Children.Add(header);

            if (ws.IsExpanded)
            {
                foreach (var node in Store.VisibleTreeFor(ws))
                    ContentHost.Children.Add(BuildNode(node, indent: 1));
            }
        }
    }

    private Border BuildWorkspaceHeader(Workspace ws, bool isCurrent)
    {
        var bg = isCurrent
            ? (Brush)FindResource("Brush.SidebarSectionActive")
            : (Brush)FindResource("Brush.SidebarSectionInactive");

        var sp = new DockPanel { LastChildFill = true };

        // chevron
        var chev = new TextBlock
        {
            Text = ws.IsExpanded ? "▾" : "▸",
            Margin = new Thickness(4, 0, 6, 0),
            FontSize = 11,
            Foreground = (Brush)FindResource("Brush.Muted"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        chev.MouseDown += (_, _) => Store.ToggleWorkspaceExpanded(ws);
        DockPanel.SetDock(chev, Dock.Left);
        sp.Children.Add(chev);

        // 灯泡 (hide empty)
        var bulb = new TextBlock
        {
            Text = ws.HideEmptyFolders ? "💡" : "💡",
            Opacity = ws.HideEmptyFolders ? 1.0 : 0.35,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Hide empty folders",
        };
        bulb.MouseDown += (_, _) => Store.ToggleWorkspaceHideEmpty(ws);
        DockPanel.SetDock(bulb, Dock.Right);
        sp.Children.Add(bulb);

        // 删除
        var del = new TextBlock
        {
            Text = "×",
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Remove workspace",
            Foreground = (Brush)FindResource("Brush.Muted"),
        };
        del.MouseDown += (_, _) => Store.RemoveWorkspace(ws);
        DockPanel.SetDock(del, Dock.Right);
        sp.Children.Add(del);

        // 名字
        var name = new TextBlock
        {
            Text = ws.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Brush.Ink"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        sp.Children.Add(name);

        var b = new Border
        {
            Background = bg,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 0),
            Child = sp,
            Cursor = Cursors.Hand,
        };
        b.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) Store.ToggleWorkspaceExpanded(ws);
        };
        return b;
    }

    private FrameworkElement BuildNode(FileNode node, int indent)
    {
        if (node.IsDirectory)
        {
            return BuildDirectoryNode(node, indent);
        }
        return BuildFileNode(node, indent);
    }

    private FrameworkElement BuildDirectoryNode(FileNode dir, int indent)
    {
        // 简化: 目录默认展开, 不存折叠状态 (workspace 级别已经有 chevron)
        var stack = new StackPanel();

        var header = new Border
        {
            Padding = new Thickness(8 + indent * 14, 4, 8, 4),
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = "📁",
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = (Brush)FindResource("Brush.FolderIcon"),
        });
        sp.Children.Add(new TextBlock
        {
            Text = dir.Name,
            FontSize = 12,
            Foreground = (Brush)FindResource("Brush.FolderText"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (dir.MarkdownCount > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"  ({dir.MarkdownCount})",
                FontSize = 10,
                Foreground = (Brush)FindResource("Brush.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        header.Child = sp;
        stack.Children.Add(header);

        foreach (var c in dir.Children)
            stack.Children.Add(BuildNode(c, indent + 1));

        return stack;
    }

    private FrameworkElement BuildFileNode(FileNode file, int indent)
    {
        var selected = Store.SelectedFile != null &&
                       string.Equals(Store.SelectedFile.Path, file.Path, StringComparison.OrdinalIgnoreCase);

        var bg = selected
            ? (Brush)FindResource("Brush.SelectedFile")
            : Brushes.Transparent;
        var fg = selected
            ? (Brush)FindResource("Brush.SelectionInk")
            : (Brush)FindResource("Brush.Ink");

        var displayName = Path.GetFileNameWithoutExtension(file.Name);
        var tb = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            Foreground = fg,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var b = new Border
        {
            Background = bg,
            Padding = new Thickness(8 + indent * 14, 4, 8, 4),
            Cursor = Cursors.Hand,
            Child = tb,
        };
        b.MouseDown += (_, _) => Store.SelectFile(file);
        return b;
    }
}
