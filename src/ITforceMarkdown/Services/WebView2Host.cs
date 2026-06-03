using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace ITforceMarkdown.Services;

/// <summary>
/// 同进程内必须共用一个 CoreWebView2Environment, 否则:
///   "WebView2 was already initialized with a different CoreWebView2Environment.
///    Check if the source property was already set or EnsureCoreWebView2Async
///    was previously called with different values"
///
/// MarkdownPreview (Read / Edit 两个实例) + Exporter 离屏 WebView2 都从这里
/// 拿同一个 env, 互不冲突。
/// </summary>
public static class WebView2Host
{
    private static CoreWebView2Environment? _env;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_env != null) return _env;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_env != null) return _env;

            var userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ITforceMarkdown", "WebView2Cache");
            Directory.CreateDirectory(userDataDir);

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
}
