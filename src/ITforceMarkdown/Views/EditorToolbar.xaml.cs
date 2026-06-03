using System.Windows;
using System.Windows.Controls;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

/// <summary>
/// Edit (Rich) 模式上方的格式化工具栏。每个按钮把一段 JS 经
/// Store.SendRichCommand → MarkdownPreview.ExecuteScriptAsync 注入到 WebView,
/// 由 document.js 里的 __runCommand / __formatBlock / __insertHTML 等实现。
/// </summary>
public partial class EditorToolbar : UserControl
{
    private WorkspaceStore Store => App.Store;

    public EditorToolbar()
    {
        InitializeComponent();
    }

    // ─── Headings ───
    private void H1_Click(object sender, RoutedEventArgs e) => Store.SendRichCommand("window.__formatBlock('H1')");
    private void H2_Click(object sender, RoutedEventArgs e) => Store.SendRichCommand("window.__formatBlock('H2')");
    private void H3_Click(object sender, RoutedEventArgs e) => Store.SendRichCommand("window.__formatBlock('H3')");
    private void P_Click(object sender, RoutedEventArgs e)  => Store.SendRichCommand("window.__formatBlock('P')");

    // ─── Inline ───
    private void Bold_Click(object sender, RoutedEventArgs e)        => Store.SendRichCommand("window.__runCommand('bold')");
    private void Italic_Click(object sender, RoutedEventArgs e)      => Store.SendRichCommand("window.__runCommand('italic')");
    private void Strike_Click(object sender, RoutedEventArgs e)      => Store.SendRichCommand("window.__runCommand('strikeThrough')");
    private void InlineCode_Click(object sender, RoutedEventArgs e)
    {
        // execCommand 没有原生 inline code, 用 insertHTML 包一层 <code>
        Store.SendRichCommand(
            "var s = window.getSelection(); var t = s && s.toString() || 'code';" +
            "window.__insertHTML('<code>' + t.replace(/[<>&]/g, function(c){return c==='<'?'&lt;':c==='>'?'&gt;':'&amp;';}) + '</code>');");
    }

    // ─── Lists / Blocks ───
    private void UL_Click(object sender, RoutedEventArgs e)        => Store.SendRichCommand("window.__runCommand('insertUnorderedList')");
    private void OL_Click(object sender, RoutedEventArgs e)        => Store.SendRichCommand("window.__runCommand('insertOrderedList')");
    private void Quote_Click(object sender, RoutedEventArgs e)     => Store.SendRichCommand("window.__formatBlock('BLOCKQUOTE')");
    private void CodeBlock_Click(object sender, RoutedEventArgs e) => Store.SendRichCommand("window.__insertCodeBlock()");

    // ─── Insert ───
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        var url = PromptDialog.Show(Window.GetWindow(this)!,
            "Insert link", "URL:", "https://");
        if (string.IsNullOrEmpty(url)) return;
        var escaped = url.Replace("\\", "\\\\").Replace("'", "\\'");
        Store.SendRichCommand($"window.__insertLink('{escaped}')");
    }
    private void Image_Click(object sender, RoutedEventArgs e)
    {
        var url = PromptDialog.Show(Window.GetWindow(this)!,
            "Insert image", "Image URL or file:// path:", "https://");
        if (string.IsNullOrEmpty(url)) return;
        var escaped = url.Replace("\\", "\\\\").Replace("'", "\\'");
        Store.SendRichCommand($"window.__insertImage('{escaped}')");
    }
    private void Table_Click(object sender, RoutedEventArgs e) => Store.SendRichCommand("window.__insertTable()");
    private void HR_Click(object sender, RoutedEventArgs e)    => Store.SendRichCommand("window.__insertHR()");
}
