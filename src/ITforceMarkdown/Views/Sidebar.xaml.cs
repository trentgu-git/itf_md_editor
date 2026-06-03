using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        ContentHost.Children.Clear();

        var query = Store.SearchText?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            RenderSearchResults(query);
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

    // ─────────────────── 搜索结果渲染 ───────────────────
    private void RenderSearchResults(string query)
    {
        var results = new List<(Workspace ws, string filePath, string title, string rel, string snippet, int line)>();
        foreach (var ws in Store.LocalWorkspaces)
        {
            if (!Store.WorkspaceTrees.TryGetValue(ws.Id, out var tree)) continue;
            CollectFiles(tree, files => { });
            foreach (var f in EnumerateFilesFlat(tree))
            {
                try
                {
                    var lines = File.ReadAllLines(f.Path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(ws.Path, f.Path);
                            results.Add((ws, f.Path, Path.GetFileNameWithoutExtension(f.Path),
                                rel, lines[i].Trim(), i + 1));
                            if (results.Count > 200) goto done;
                        }
                    }
                }
                catch { /* skip unreadable */ }
            }
        }
        done:
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
        if (results.Count == 0)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = "No results.",
                Margin = new Thickness(16),
                Foreground = (Brush)FindResource("Brush.Muted"),
                FontSize = 12,
            });
        }
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
