using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ITforceMarkdown.Models;

namespace ITforceMarkdown.Services;

/// <summary>
/// 扫描一个目录, 构建 FileNode 树, 只收 .md/.markdown 文件。
/// 跟 Mac 版 WorkspaceStore.buildTree 行为等价。
/// </summary>
public static class WorkspaceScanner
{
    private static readonly HashSet<string> MarkdownExts =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };

    // 跳过的目录 (噪音, 几乎不可能存有意义的 .md)
    private static readonly HashSet<string> IgnoredDirs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", "node_modules", "bin", "obj",
            ".vs", ".idea", ".DS_Store", "__pycache__",
        };

    public static List<FileNode> Scan(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return new List<FileNode>();
        try
        {
            return ScanDirectory(rootPath).Children;
        }
        catch (UnauthorizedAccessException)
        {
            return new List<FileNode>();
        }
    }

    private static FileNode ScanDirectory(string path)
    {
        var children = new List<FileNode>();
        int mdCount = 0;

        IEnumerable<string> dirs = Array.Empty<string>();
        IEnumerable<string> files = Array.Empty<string>();
        try
        {
            dirs = Directory.EnumerateDirectories(path);
            files = Directory.EnumerateFiles(path);
        }
        catch (UnauthorizedAccessException) { /* skip */ }
        catch (DirectoryNotFoundException) { /* skip */ }
        catch (PathTooLongException) { /* skip */ }

        // Directories first
        foreach (var d in dirs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(d);
            if (IgnoredDirs.Contains(name)) continue;
            if (name.StartsWith('.')) continue; // hidden dirs

            var childNode = ScanDirectory(d);
            children.Add(childNode);
            mdCount += childNode.MarkdownCount;
        }

        // Files second
        foreach (var f in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(f);
            if (!MarkdownExts.Contains(ext)) continue;
            var name = Path.GetFileName(f);
            if (name.StartsWith('.')) continue;

            children.Add(new FileNode
            {
                Path = f,
                Name = name,
                IsDirectory = false,
                MarkdownCount = 0,
            });
            mdCount++;
        }

        return new FileNode
        {
            Path = path,
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
            IsDirectory = true,
            Children = children,
            MarkdownCount = mdCount,
        };
    }

    /// <summary>
    /// 过滤掉所有 markdownCount == 0 的目录 (递归)。
    /// 对应 Mac 版 hideEmptyFolders 模式。
    /// </summary>
    public static List<FileNode> FilterEmptyFolders(List<FileNode> nodes)
    {
        var result = new List<FileNode>();
        foreach (var n in nodes)
        {
            if (n.IsDirectory)
            {
                if (n.MarkdownCount == 0) continue;
                var filtered = FilterEmptyFolders(n.Children);
                result.Add(new FileNode
                {
                    Path = n.Path,
                    Name = n.Name,
                    IsDirectory = true,
                    Children = filtered,
                    MarkdownCount = n.MarkdownCount,
                });
            }
            else
            {
                result.Add(n);
            }
        }
        return result;
    }
}
