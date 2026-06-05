using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ITforceMarkdown.Models;

namespace ITforceMarkdown.Services;

/// <summary>
/// 扫描一个目录, 构建 FileNode 树, 只收 .md/.markdown 文件。
/// 跟 Mac 版 WorkspaceStore.buildTree 行为等价。
///
/// 鲁棒性 (用户反馈过的崩溃 / 闪退几乎都是 scan 引起的, 全部在这里兜底):
///   - 深度上限 (MaxDepth) — 防止符号链接 / junction 循环把栈或递归打爆
///   - 文件总数上限 (MaxFiles) — 防止 OOM (用户开 C:\ 这种几十万文件的根)
///   - 跳过 reparse point (symlink / junction) — 避免循环 + 跨卷扫描的卡顿
///   - catch *所有* IOException 系 — Directory.Enumerate 在没权限 / 网络
///     盘断了 / 长路径 / 锁定文件夹时会抛 5 种不同异常, 一个不漏才不会 crash
/// </summary>
public static class WorkspaceScanner
{
    /// <summary>最大递归深度. 工作区里超过这个的目录直接跳过.</summary>
    public const int MaxDepth = 20;
    /// <summary>整个 workspace 最多收的文件数. 超过就停, 避免 OOM.</summary>
    public const int MaxFiles = 50_000;

    private static readonly HashSet<string> MarkdownExts =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };

    // 跳过的目录 (噪音, 几乎不可能存有意义的 .md)
    private static readonly HashSet<string> IgnoredDirs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", "node_modules", "bin", "obj",
            ".vs", ".idea", ".DS_Store", "__pycache__",
            // Windows 大坑目录 — 用户开 C:\Users\xxx 时会扫这些, 慢且容易没权限
            "AppData", "$Recycle.Bin", "System Volume Information",
        };

    public static List<FileNode> Scan(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            return new List<FileNode>();
        try
        {
            int totalFiles = 0;
            var root = ScanDirectory(rootPath, depth: 0, ref totalFiles);
            return root.Children;
        }
        catch
        {
            // 最后兜底: 任何意外异常都不能把 app 干掉, 给空树.
            return new List<FileNode>();
        }
    }

    private static FileNode ScanDirectory(string path, int depth, ref int totalFiles)
    {
        var children = new List<FileNode>();
        int mdCount = 0;

        // 到达深度 / 文件上限就停, 不再向下递归.
        if (depth >= MaxDepth || totalFiles >= MaxFiles)
            return EmptyNode(path);

        // Reparse point (symlink / junction): 直接跳过 — 避免循环, 也避免
        // 跨卷扫描卡半天.
        try
        {
            var di = new DirectoryInfo(path);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                return EmptyNode(path);
        }
        catch { /* attribute 读不到就当普通目录 */ }

        // Directory.Enumerate* 在异常情况下抛多种异常, 全部 catch (吞掉而非崩).
        IEnumerable<string> dirs = Array.Empty<string>();
        IEnumerable<string> files = Array.Empty<string>();
        try { dirs = Directory.EnumerateDirectories(path); }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (PathTooLongException) { }
        catch (IOException) { }                // 网络盘断开 / lock
        catch (System.Security.SecurityException) { }

        try { files = Directory.EnumerateFiles(path); }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (PathTooLongException) { }
        catch (IOException) { }
        catch (System.Security.SecurityException) { }

        // Directories first
        // ToList 是为了把惰性枚举的异常一次性 fail-fast 而不是中途崩.
        List<string> dirList;
        try { dirList = dirs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { dirList = new List<string>(); }
        foreach (var d in dirList)
        {
            string name;
            try { name = Path.GetFileName(d); }
            catch { continue; }
            if (string.IsNullOrEmpty(name)) continue;
            if (IgnoredDirs.Contains(name)) continue;
            if (name.StartsWith('.')) continue;

            var childNode = ScanDirectory(d, depth + 1, ref totalFiles);
            children.Add(childNode);
            mdCount += childNode.MarkdownCount;
            if (totalFiles >= MaxFiles) break;  // 全局上限, 中途也能停
        }

        // Files second
        List<string> fileList;
        try { fileList = files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { fileList = new List<string>(); }
        foreach (var f in fileList)
        {
            string ext, name;
            try { ext = Path.GetExtension(f); name = Path.GetFileName(f); }
            catch { continue; }
            if (!MarkdownExts.Contains(ext)) continue;
            if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
            if (totalFiles >= MaxFiles) break;

            children.Add(new FileNode
            {
                Path = f,
                Name = name,
                IsDirectory = false,
                MarkdownCount = 0,
            });
            mdCount++;
            totalFiles++;
        }

        return new FileNode
        {
            Path = path,
            Name = SafeName(path),
            IsDirectory = true,
            Children = children,
            MarkdownCount = mdCount,
        };
    }

    private static FileNode EmptyNode(string path) => new()
    {
        Path = path,
        Name = SafeName(path),
        IsDirectory = true,
        Children = new List<FileNode>(),
        MarkdownCount = 0,
    };

    private static string SafeName(string path)
    {
        try { return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? path; }
        catch { return path; }
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
