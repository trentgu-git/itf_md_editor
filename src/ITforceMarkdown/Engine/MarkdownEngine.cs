using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ITforceMarkdown.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ITforceMarkdown.Engine;

/// <summary>
/// Markdown 引擎 — 对应 Mac 版 MarkdownEngine.swift。
/// 用 Markdig 解析 CommonMark + GFM, 然后包一层 HTML 模板 (light/dark CSS).
/// </summary>
public static class MarkdownEngine
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()  // tables, task lists, autolinks, strikethrough, etc.
        .UseAutoIdentifiers()      // h2/h3 自动生成 id, 用于锚点跳转
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    // ─────────────────── 公开 API ───────────────────

    /// <summary>
    /// 解析 markdown 字符串里的所有标题, 用于 outline 面板。
    /// </summary>
    public static List<HeadingItem> ParseHeadings(string markdown)
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        var result = new List<HeadingItem>();
        var slugCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in doc)
        {
            if (block is HeadingBlock h)
            {
                var title = ExtractText(h.Inline);
                var baseSlug = Slug(title);
                slugCounts.TryGetValue(baseSlug, out int n);
                slugCounts[baseSlug] = n + 1;
                var id = n == 0 ? baseSlug : $"{baseSlug}-{n + 1}";

                result.Add(new HeadingItem
                {
                    Id = id,
                    Title = title,
                    Level = h.Level,
                    Line = h.Line + 1,
                });
            }
        }
        return result;
    }

    /// <summary>Pure HTML body (无 wrapper), 用于 export PDF / Word, 也作为 documentHTML 的内容部分。</summary>
    public static string RenderInnerHtml(string markdown)
        => Markdown.ToHtml(markdown ?? "", Pipeline);

    /// <summary>
    /// 给 WebView 用的完整 HTML — 嵌好暗黑 / 亮色 CSS, 支持 contenteditable。
    /// 跟 Mac 版 documentHTML 同名同行为。
    /// </summary>
    public static string DocumentHtml(string markdown, bool editable, AppearancePreference appearance)
    {
        var inner = RenderInnerHtml(markdown);
        var editableAttr = editable ? "true" : "false";
        var htmlClass = appearance switch
        {
            AppearancePreference.Light => " class=\"force-light\"",
            AppearancePreference.Dark  => " class=\"force-dark\"",
            _ => "",
        };
        var css = LoadEmbedded("document.css");
        var js  = LoadEmbedded("document.js");
        var escaped = EscapeForJsTemplate(inner);

        return $"""
<!doctype html>
<html{htmlClass}>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>{css}</style>
</head>
<body>
<article id="doc" class="page" contenteditable="{editableAttr}" spellcheck="true"></article>
<script>
const doc = document.getElementById('doc');
doc.innerHTML = `{escaped}`;
{js}
</script>
</body>
</html>
""";
    }

    public enum PrintTarget { Pdf, Word }

    /// <summary>
    /// 给 PDF / Word 导出用的打印友好版 HTML — 不含 contenteditable / JS。
    /// PDF: 用 pt 单位, body padding 控制页边距 (WebView2 PrintToPdfAsync 不识 @page margin)。
    /// Word: 用 px 单位, body padding=0, 让 @page margin 接管。
    /// </summary>
    public static string PrintHtml(string markdown, string title, PrintTarget target)
    {
        var inner = RenderInnerHtml(markdown);
        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrEmpty(title) ? "Document" : title);
        string unit, bodyPadding;
        switch (target)
        {
            case PrintTarget.Pdf:
                unit = "pt";
                bodyPadding = "body { margin: 0; padding: 96px; }";
                break;
            case PrintTarget.Word:
            default:
                unit = "px";
                bodyPadding = "body { margin: 0; padding: 0; }";
                break;
        }

        var css = LoadEmbedded("print.css").Replace("__UNIT__", unit);

        return $"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>{safeTitle}</title>
<style>
{css}
{bodyPadding}
</style>
</head>
<body>
{inner}
</body>
</html>
""";
    }

    // ─────────────────── 内部工具 ───────────────────

    /// <summary>读取嵌入式资源 (Engine/Resources/*.css|*.js)。</summary>
    private static string LoadEmbedded(string filename)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = $"ITforceMarkdown.Engine.Resources.{filename}";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else sb.Append('-');
        }
        var collapsed = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return string.IsNullOrEmpty(collapsed) ? "section" : collapsed;
    }

    /// <summary>把 Markdig 解析过的 inline 串里的 literal 文本拼起来 (去掉 markup)。</summary>
    private static string ExtractText(ContainerInline? inline)
    {
        if (inline == null) return "";
        var sb = new StringBuilder();
        foreach (var i in inline)
        {
            switch (i)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content.ToString());
                    break;
                case ContainerInline container:
                    sb.Append(ExtractText(container));
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>转义内容塞进 JS template literal — 反斜杠 / 反引号 / $ 都要 escape。</summary>
    private static string EscapeForJsTemplate(string s)
        => s.Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("$", "\\$");
}
