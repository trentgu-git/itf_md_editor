namespace ITforceMarkdown.Models;

/// <summary>
/// 文档查看模式 — 对应 Mac 版 EditorMode enum。
/// Read   = 只读预览 (WebView2, contenteditable=false)
/// Rich   = 富文本编辑 (WebView2, contenteditable=true)
/// Source = Markdown 源码编辑 (普通 TextBox)
/// </summary>
public enum EditorMode
{
    Read,
    Rich,
    Source,
}

public static class EditorModeExtensions
{
    public static string DisplayName(this EditorMode mode) => mode switch
    {
        EditorMode.Read   => "Read",
        EditorMode.Rich   => "Edit",
        EditorMode.Source => "Source",
        _ => mode.ToString(),
    };
}
