using System.IO;

namespace ITforceMarkdown.Models;

/// <summary>
/// 当前打开的 Markdown 文档 — 对应 Mac 版 MarkdownDocument。
/// 不持有内容, 内容由 WorkspaceStore.SourceDraft 单一来源。
/// </summary>
public sealed class MarkdownDocument
{
    public required string Path { get; init; }

    public string Id => Path;
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Filename => System.IO.Path.GetFileName(Path);

    public bool Exists() => File.Exists(Path);
}
