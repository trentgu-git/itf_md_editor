using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ITforceMarkdown.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ITforceMarkdown.Engine;

/// <summary>
/// Markdown 引擎 — 对应 Mac 版 MarkdownEngine.swift。
/// 用 Markdig 解析 CommonMark + GFM, 然后包一层 HTML 模板 (light/dark CSS).
///
/// 增强渲染管线 (Tier 1, 跟 Mac 端一致):
///   - Mermaid: ```mermaid 代码块在 JS 端用 mermaid.run() 转 SVG
///   - highlight.js: 给 <pre><code class="language-xxx"> 着色
///   - KaTeX: 自动渲染 $...$ / $$...$$
///   - GitHub callouts: > [!NOTE/TIP/IMPORTANT/WARNING/CAUTION] 渲染成彩色框
///   - YAML front matter: 文件首 `---...---` 折成 &lt;details&gt;
///   - 图片 / mermaid 点击放大 (event delegation in document.js)
/// </summary>
public static class MarkdownEngine
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()  // tables, task lists, autolinks, strikethrough 等
        .UseAutoIdentifiers()      // h2/h3 自动生成 id, 用于锚点跳转
        .UseSoftlineBreakAsHardlineBreak()
        .UseYamlFrontMatter()      // 文件首 --- 块识别为 YAML, 不当 hr
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
    {
        var (frontMatterHtml, body) = ExtractFrontMatter(markdown ?? "");
        var bodyHtml = ConvertMermaidCodeBlocks(body);
        bodyHtml = Markdown.ToHtml(bodyHtml, Pipeline);
        bodyHtml = TransformCallouts(bodyHtml);
        return frontMatterHtml + bodyHtml;
    }

    /// <summary>
    /// 给 WebView 用的完整 HTML — 嵌好暗黑 / 亮色 CSS, 支持 contenteditable。
    /// 跟 Mac 版 documentHTML 同名同行为。
    ///
    /// 把 mermaid / highlight.js / KaTeX 的 JS+CSS 全部以 inline &lt;script&gt;/&lt;style&gt;
    /// 嵌入, document.js 里调用对应 API. 这样 WebView2 完全离线可渲染.
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

        var libsHead = LibraryAssetsHead();

        return $"""
<!doctype html>
<html{htmlClass}>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
{libsHead}
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
    /// 给 PDF / Word 导出用的打印友好版 HTML。
    /// PDF: 用 pt 单位 + body padding 控制页边距, 同时也嵌渲染库 (mermaid/hljs/KaTeX),
    ///      WebView2.PrintToPdfAsync 加载后 JS 会跑, mermaid 会变 SVG.
    /// Word: 用 px 单位, body padding=0; *不* 嵌库 (textutil 不跑 JS),
    ///       mermaid 源码块被换成 [Mermaid diagram] 占位符 (Mac 端同样做法).
    /// </summary>
    public static string PrintHtml(string markdown, string title, PrintTarget target)
    {
        var (frontMatterHtml, body) = ExtractFrontMatter(markdown ?? "");
        var bodyForRender = ConvertMermaidCodeBlocks(body);
        var inner = Markdown.ToHtml(bodyForRender, Pipeline);
        inner = TransformCallouts(inner);
        inner = frontMatterHtml + inner;

        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrEmpty(title) ? "Document" : title);
        string unit, bodyPadding, libsHead, initScript;
        switch (target)
        {
            case PrintTarget.Pdf:
                unit = "pt";
                bodyPadding = "body { margin: 0; padding: 96px; }";
                libsHead = LibraryAssetsHead();
                initScript = PrintInitScript;
                break;
            case PrintTarget.Word:
            default:
                unit = "px";
                bodyPadding = "body { margin: 0; padding: 0; }";
                libsHead = "";
                initScript = "";
                // Word 不能渲染 mermaid (textutil/OpenXml 不跑 JS), 换占位符.
                inner = StripMermaidForWord(inner);
                break;
        }

        var css = LoadEmbedded("print.css").Replace("__UNIT__", unit);

        return $"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>{safeTitle}</title>
{libsHead}
<style>
{css}
{bodyPadding}
</style>
</head>
<body>
{inner}
{initScript}
</body>
</html>
""";
    }

    // ─────────────────── 内部: 资源加载 ───────────────────

    /// <summary>
    /// Mermaid + highlight.js + KaTeX 的 &lt;script&gt;/&lt;link&gt; 头.
    ///
    /// 走 virtual host (https://app.local/...) 而不是 inline — mermaid.min.js
    /// 单文件 3.3MB, 全 inline 进 HTML 会超过 WebView2.NavigateToString 的
    /// 2MB 上限, 直接 "Value does not fall within the expected range" 崩.
    /// 文件在应用启动时由 WebView2Host.ExtractEmbeddedAssets 解压到
    /// %LOCALAPPDATA%\ITforceMarkdown\Assets\, 通过 SetVirtualHostNameTo
    /// FolderMapping 暴露成 https URL.
    /// </summary>
    private static string LibraryAssetsHead()
    {
        // WebView2Host.AssetsUrl(filename) 拼出 https://app.local/<filename>
        var hljsCss   = Services.WebView2Host.AssetsUrl("github.min.css");
        var katexCss  = Services.WebView2Host.AssetsUrl("katex.min.css");
        var mermaid   = Services.WebView2Host.AssetsUrl("mermaid.min.js");
        var hljs      = Services.WebView2Host.AssetsUrl("highlight.min.js");
        var katex     = Services.WebView2Host.AssetsUrl("katex.min.js");
        var katexAuto = Services.WebView2Host.AssetsUrl("katex-auto-render.min.js");

        return $"""
<link rel="stylesheet" href="{hljsCss}">
<link rel="stylesheet" href="{katexCss}">
<script src="{mermaid}"></script>
<script src="{hljs}"></script>
<script src="{katex}"></script>
<script src="{katexAuto}"></script>
""";
    }

    /// <summary>
    /// PDF 导出时塞进 body 末尾的脚本: mermaid.run + hljs + KaTeX, 完成后置
    /// window.__exportReady = true. Exporter 那边可以等这个 flag 再 PrintToPdfAsync.
    /// </summary>
    private const string PrintInitScript = """
<script>
(async function () {
  try {
    if (window.mermaid && window.mermaid.run) {
      window.mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'loose' });
      const nodes = document.querySelectorAll('div.mermaid:not([data-processed="true"])');
      if (nodes.length) await window.mermaid.run({ nodes });
    }
    if (window.hljs && window.hljs.highlightElement) {
      document.querySelectorAll('pre code').forEach(function (el) {
        try { window.hljs.highlightElement(el); } catch (e) {}
      });
    }
    if (window.renderMathInElement) {
      try {
        window.renderMathInElement(document.body, {
          delimiters: [
            { left: '$$', right: '$$', display: true  },
            { left: '\\[', right: '\\]', display: true  },
            { left: '$',  right: '$',  display: false },
            { left: '\\(', right: '\\)', display: false }
          ],
          throwOnError: false,
          ignoredTags: ['script', 'noscript', 'style', 'textarea', 'pre', 'code']
        });
      } catch (e) {}
    }
  } catch (e) {
    console.error('print render error:', e);
  } finally {
    window.__exportReady = true;
  }
})();
</script>
""";

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

    // ─────────────────── 内部: Markdown 预处理 / HTML 后处理 ───────────────────

    /// <summary>
    /// 提取 YAML front matter (文件首 ---...---), 转成 &lt;details&gt; 块, 返回
    /// (frontMatterHtml, 剩余 markdown 文本). 没有 front matter 时返回 ("", original).
    /// </summary>
    private static (string frontMatterHtml, string remainingMarkdown) ExtractFrontMatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return ("", markdown);
        var lines = markdown.Split('\n');
        if (lines.Length < 3) return ("", markdown);
        if (lines[0].TrimEnd('\r').Trim() != "---") return ("", markdown);
        int endIdx = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').Trim() == "---") { endIdx = i; break; }
        }
        if (endIdx <= 1) return ("", markdown);
        var yaml = string.Join("\n", lines, 1, endIdx - 1);
        var escapedYaml = System.Net.WebUtility.HtmlEncode(yaml);
        var html = $"<details class=\"front-matter\"><summary>front matter</summary><pre>{escapedYaml}</pre></details>\n";
        var rest = string.Join("\n", lines, endIdx + 1, lines.Length - endIdx - 1);
        return (html, rest);
    }

    /// <summary>
    /// 把 ```mermaid 代码块替换成 &lt;div class="mermaid"&gt; 容器, mermaid.js 才能渲染.
    /// Markdig 默认把它当代码块, 我们要在解析前 hijack.
    /// </summary>
    private static readonly Regex MermaidCodeFence = new(
        @"```mermaid\s*\n(.*?)\n```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static string ConvertMermaidCodeBlocks(string markdown)
    {
        return MermaidCodeFence.Replace(markdown, m =>
        {
            var inner = System.Net.WebUtility.HtmlEncode(m.Groups[1].Value);
            // 用 HTML block 形式塞回 markdown, Markdig 会原样保留 (pass-through).
            return $"\n\n<div class=\"mermaid\">{inner}</div>\n\n";
        });
    }

    /// <summary>
    /// GitHub callout 后处理: blockquote 第一行是 [!NOTE]/[!TIP]/[!IMPORTANT]/
    /// [!WARNING]/[!CAUTION] 时, 加 callout-xxx class + 标题图标.
    /// </summary>
    private static readonly Regex CalloutPattern = new(
        @"<blockquote>\s*<p>\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*(?:<br\s*/?>\s*)?(.*?)</p>(.*?)</blockquote>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string TransformCallouts(string html)
    {
        return CalloutPattern.Replace(html, m =>
        {
            var kind = m.Groups[1].Value.ToLowerInvariant();
            var firstLine = m.Groups[2].Value.Trim();
            var rest = m.Groups[3].Value.Trim();
            var title = char.ToUpper(kind[0]) + kind.Substring(1);
            var icon = kind switch
            {
                "note"      => "ℹ",
                "tip"       => "💡",
                "important" => "❗",
                "warning"   => "⚠",
                "caution"   => "⛔",
                _ => ""
            };
            var body = string.IsNullOrEmpty(firstLine)
                ? rest
                : $"<p>{firstLine}</p>{rest}";
            return $"<blockquote class=\"callout callout-{kind}\"><div class=\"callout-title\">{icon} {title}</div>{body}</blockquote>";
        });
    }

    /// <summary>
    /// Word 导出: 把 &lt;div class="mermaid"&gt; 整段换成 [Mermaid diagram] 占位符.
    /// textutil / OpenXml 都不能渲染 inline SVG, 留 mermaid 块只会变成乱码文本.
    /// </summary>
    private static readonly Regex MermaidDivPattern = new(
        @"<div\s+class=""mermaid""[^>]*>.*?</div>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string StripMermaidForWord(string html)
    {
        return MermaidDivPattern.Replace(html,
            "<p style=\"color:#999;font-style:italic\">[Mermaid diagram]</p>");
    }

    // ─────────────────── 内部: 工具 ───────────────────

    private static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else sb.Append('-');
        }
        var collapsed = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
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
