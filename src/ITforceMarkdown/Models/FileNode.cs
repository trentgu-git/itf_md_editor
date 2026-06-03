using System.Collections.Generic;
using System.IO;

namespace ITforceMarkdown.Models;

/// <summary>
/// Sidebar 文件树节点 — 对应 Mac 版 FileNode struct。
/// 一个 FileNode 要么是文件 (IsDirectory=false), 要么是目录 + 子节点。
/// </summary>
public sealed class FileNode
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public List<FileNode> Children { get; init; } = new();
    public int MarkdownCount { get; init; }

    /// <summary>Path 作为 ID, 一个工作区内唯一。</summary>
    public string Id => Path;

    public string DisplayCount => MarkdownCount == 0 ? "" : MarkdownCount.ToString();

    /// <summary>方便 binding 调用的全路径转 FileInfo (lazy).</summary>
    public FileInfo? AsFileInfo() => IsDirectory ? null : new FileInfo(Path);
}
