namespace ITforceMarkdown.Models;

/// <summary>
/// Sidebar 搜索结果项 — 对应 Mac 版 DocumentSearchResult。
/// 一行匹配 = 一个结果, 同一文档多行命中会有多条。
/// </summary>
public sealed class DocumentSearchResult
{
    public required string Path { get; init; }         // 文件绝对路径
    public required string Title { get; init; }        // 显示用文件名 (无扩展名)
    public required string RelativePath { get; init; } // 相对 workspace 根的路径
    public required string Snippet { get; init; }      // 命中的那行内容预览
    public required int Line { get; init; }

    public string Id => $"{Path}#{Line}#{Snippet}";
}
