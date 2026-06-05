using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ITforceMarkdown.Engine;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace ITforceMarkdown.Services;

/// <summary>
/// 文档导出: PDF (用一个 off-screen WebView2 调 PrintToPdfAsync), Word
/// (Markdig 解析 → OpenXml 直接拼 OOXML 段落)。
/// </summary>
public static class Exporter
{
    // ─────────────────── PDF ───────────────────
    public static async Task ExportPdfAsync(Window owner, string markdown, string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export PDF",
            FileName = Path.GetFileNameWithoutExtension(suggestedName) + ".pdf",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = "pdf",
        };
        if (dlg.ShowDialog(owner) != true) return;
        var outPath = dlg.FileName;

        // 创建一个隐藏的 off-screen WebView2 加载 printHTML, 等加载完调
        // PrintToPdfAsync — A4 + 1 inch margin 已经在 printHTML 的 CSS 里写了。
        var hostWindow = new Window
        {
            Width = 1, Height = 1,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden,
            Owner = owner,
        };
        var wv = new WebView2();
        hostWindow.Content = wv;
        hostWindow.Show();

        try
        {
            // 复用主 WebView 同一个 environment, 避免 "different environment" 报错。
            var env = await WebView2Host.GetEnvironmentAsync();
            await wv.EnsureCoreWebView2Async(env);
            // virtual host 映射 mermaid/hljs/KaTeX 库 (跟 MarkdownPreview 一致),
            // 让 print HTML 能 <script src="https://app.local/..."> 引用大文件,
            // 不撑爆 NavigateToString 的 2MB 上限.
            WebView2Host.RegisterVirtualHost(wv.CoreWebView2);

            var html = MarkdownEngine.PrintHtml(markdown, Path.GetFileNameWithoutExtension(suggestedName),
                                                MarkdownEngine.PrintTarget.Pdf);
            var tcs = new TaskCompletionSource();
            wv.NavigationCompleted += (_, _) => tcs.TrySetResult();
            wv.NavigateToString(html);
            await tcs.Task;

            // 给布局一帧, 防止 PDF 渲染时表格还没排完
            await Task.Delay(200);

            var settings = wv.CoreWebView2.Environment.CreatePrintSettings();
            settings.PageWidth = 8.27;   // A4 inches
            settings.PageHeight = 11.69;
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;

            var ok = await wv.CoreWebView2.PrintToPdfAsync(outPath, settings);
            if (ok)
            {
                MessageBox.Show(owner, $"Exported to:\n{outPath}", "Export PDF",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(owner, "PDF export returned false.", "Export PDF",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"PDF export failed:\n{ex.Message}", "Export PDF",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            hostWindow.Close();
        }
    }

    // ─────────────────── Word (.docx) ───────────────────
    public static void ExportWord(Window owner, string markdown, string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Word",
            FileName = Path.GetFileNameWithoutExtension(suggestedName) + ".docx",
            Filter = "Word document (*.docx)|*.docx",
            DefaultExt = "docx",
        };
        if (dlg.ShowDialog(owner) != true) return;
        var outPath = dlg.FileName;

        try
        {
            WriteDocx(outPath, markdown);
            MessageBox.Show(owner, $"Exported to:\n{outPath}", "Export Word",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Word export failed:\n{ex.Message}", "Export Word",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void WriteDocx(string path, string markdown)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        var body = new Body();

        // A4 + 2.54cm margins (跟 print.css @page 一致)
        var sectionProps = new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 },  // A4 in twips
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720, Gutter = 0 }
        );
        body.Append(sectionProps);

        var parsed = Markdig.Markdown.Parse(markdown);
        foreach (var block in parsed)
        {
            AppendBlock(body, block);
        }

        main.Document.Append(body);
    }

    private static void AppendBlock(Body body, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                body.Append(MakeHeading(InlineText(h.Inline), h.Level));
                break;
            case ParagraphBlock p:
                body.Append(MakeParagraph(InlineText(p.Inline)));
                break;
            case QuoteBlock q:
                foreach (var inner in q)
                    body.Append(MakeQuote(InlineText((inner as LeafBlock)?.Inline)));
                break;
            case FencedCodeBlock fenced:
                body.Append(MakeCode(fenced.Lines.ToString()));
                break;
            case CodeBlock code:
                body.Append(MakeCode(code.Lines.ToString()));
                break;
            case ListBlock list:
                foreach (var item in list)
                {
                    if (item is ListItemBlock li)
                    {
                        foreach (var sub in li)
                        {
                            if (sub is LeafBlock lb)
                                body.Append(MakeListItem(InlineText(lb.Inline), list.IsOrdered));
                        }
                    }
                }
                break;
            case ThematicBreakBlock:
                body.Append(MakeHorizontalRule());
                break;
            default:
                body.Append(MakeParagraph(block.ToString() ?? ""));
                break;
        }
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var n in inline)
        {
            switch (n)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case LineBreakInline: sb.Append(' '); break;
                case CodeInline c: sb.Append(c.Content); break;
                case ContainerInline ci: sb.Append(InlineText(ci)); break;
                default: sb.Append(n.ToString()); break;
            }
        }
        return sb.ToString();
    }

    private static Paragraph MakeParagraph(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var props = new RunProperties(new RunFonts { Ascii = "Calibri" }, new FontSize { Val = "20" }); // 10pt
        run.PrependChild(props);
        return new Paragraph(run);
    }

    private static Paragraph MakeHeading(string text, int level)
    {
        var styleSize = level switch
        {
            1 => "28",
            2 => "24",
            3 => "22",
            _ => "20",
        };
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.PrependChild(new RunProperties(
            new Bold(),
            new RunFonts { Ascii = "Calibri" },
            new FontSize { Val = styleSize }
        ));
        var p = new Paragraph(run);
        p.ParagraphProperties = new ParagraphProperties(
            new SpacingBetweenLines { Before = "240", After = "120" }
        );
        return p;
    }

    private static Paragraph MakeQuote(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.PrependChild(new RunProperties(new Italic(), new FontSize { Val = "20" }));
        var p = new Paragraph(run);
        p.ParagraphProperties = new ParagraphProperties(
            new Indentation { Left = "360" }
        );
        return p;
    }

    private static Paragraph MakeCode(string text)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.PrependChild(new RunProperties(
            new RunFonts { Ascii = "Consolas" },
            new FontSize { Val = "18" }
        ));
        var p = new Paragraph(run);
        p.ParagraphProperties = new ParagraphProperties(
            new Shading { Fill = "F3F4F6" }
        );
        return p;
    }

    private static Paragraph MakeListItem(string text, bool ordered)
    {
        var bullet = ordered ? "•" : "•";
        var run = new Run(new Text($"{bullet}  {text}") { Space = SpaceProcessingModeValues.Preserve });
        run.PrependChild(new RunProperties(new RunFonts { Ascii = "Calibri" }, new FontSize { Val = "20" }));
        var p = new Paragraph(run);
        p.ParagraphProperties = new ParagraphProperties(
            new Indentation { Left = "360" }
        );
        return p;
    }

    private static Paragraph MakeHorizontalRule()
    {
        var p = new Paragraph();
        var pp = new ParagraphProperties();
        var border = new ParagraphBorders(
            new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "CCCCCC" }
        );
        pp.Append(border);
        p.ParagraphProperties = pp;
        return p;
    }
}
