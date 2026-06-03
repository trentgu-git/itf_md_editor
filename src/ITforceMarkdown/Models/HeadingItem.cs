namespace ITforceMarkdown.Models;

/// <summary>
/// Outline 面板里的一个标题项 — 对应 Mac 版 HeadingItem struct。
/// </summary>
public sealed class HeadingItem
{
    public required string Id { get; init; }     // slug, 用于 webview scrollIntoView 锚点
    public required string Title { get; init; }
    public required int Level { get; init; }     // 1..6
    public required int Line { get; init; }      // 源文件中的行号
}
