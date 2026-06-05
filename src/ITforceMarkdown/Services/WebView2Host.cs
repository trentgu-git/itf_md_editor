using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace ITforceMarkdown.Services;

/// <summary>
/// 同进程内共用一个 CoreWebView2Environment, 也共用 virtual-host 映射的
/// 资产目录 (mermaid/hljs/KaTeX 等大型 JS/CSS).
///
/// 为什么要 virtual host:
///   WebView2.NavigateToString 的 HTML 字符串硬上限是 2MB. mermaid.min.js
///   单文件就 3.3MB, 全 inline 会让 EnsureCoreWebView2Async 报
///   "Value does not fall within the expected range".
///
/// 方案: 应用启动时把所有嵌入的 JS/CSS resource 解压到
///   %LOCALAPPDATA%\ITforceMarkdown\Assets\
/// 然后 WebView2 用 SetVirtualHostNameToFolderMapping 把它映射成
///   https://app.local/mermaid.min.js  等 URL.
/// HTML 里就走 &lt;script src="https://app.local/mermaid.min.js"&gt;,
/// HTML 字符串小到几十 KB, 远低于 2MB 上限.
///
/// MarkdownPreview (Read / Edit 两个实例) + Exporter 离屏 WebView2 都从这里
/// 拿同一个 env, 互不冲突.
/// </summary>
public static class WebView2Host
{
    /// <summary>Virtual host 名 — HTML 里走 https://&lt;HostName&gt;/xxx.js .</summary>
    public const string VirtualHostName = "app.local";

    private static CoreWebView2Environment? _env;
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static string? _assetsDir;

    /// <summary>给 MarkdownEngine 用 — 知道虚拟主机, 拼资源 URL.</summary>
    public static string AssetsUrl(string filename) => $"https://{VirtualHostName}/{filename}";

    /// <summary>
    /// 给每个 CoreWebView2 实例调用一次 — 注册虚拟主机映射.
    /// 同一个 env 下的所有 WebView2 都会共享这个映射, 但 API 要在每个
    /// CoreWebView2 上分别调.
    /// </summary>
    public static void RegisterVirtualHost(CoreWebView2 cwv)
    {
        if (_assetsDir == null) return;
        cwv.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            _assetsDir,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_env != null) return _env;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_env != null) return _env;

            var appLocal = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ITforceMarkdown");
            var userDataDir = Path.Combine(appLocal, "WebView2Cache");
            _assetsDir       = Path.Combine(appLocal, "Assets");
            Directory.CreateDirectory(userDataDir);
            Directory.CreateDirectory(_assetsDir);

            ExtractEmbeddedAssets(_assetsDir);

            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDir,
                options: null);
            return _env;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 把 Engine.Resources 里所有 .min.js / .min.css 文件 dump 到磁盘.
    /// 先把 stream 拷成 byte[], 再用 byte[].Length 跟磁盘 size 比较 — 比直接读
    /// stream.Length 更稳 (某些 ManifestResourceStream 实现 Length 行为不一致).
    /// 已存在且 size 一致就跳过, 改了就覆盖.
    /// </summary>
    private static void ExtractEmbeddedAssets(string targetDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        const string prefix = "ITforceMarkdown.Engine.Resources.";
        foreach (var resName in asm.GetManifestResourceNames())
        {
            if (!resName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var filename = resName.Substring(prefix.Length);
            // 只 extract 大型库 (Mermaid/hljs/KaTeX). document.css/js + print.css
            // 还是 inline 进 HTML — 它们 < 10KB, 不值得走 HTTP, 而且 document.js
            // 依赖 host 注入的 doc 变量, 必须在 inline 上下文里.
            if (!filename.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) &&
                !filename.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
                continue;

            using var src = asm.GetManifestResourceStream(resName);
            if (src == null) continue;
            using var ms = new MemoryStream();
            src.CopyTo(ms);
            var bytes = ms.ToArray();

            var dstPath = Path.Combine(targetDir, filename);
            // 已存在 + 大小一致就跳过 (避免每次启动重写几 MB).
            if (File.Exists(dstPath) && new FileInfo(dstPath).Length == bytes.Length)
                continue;
            File.WriteAllBytes(dstPath, bytes);
        }
    }
}
