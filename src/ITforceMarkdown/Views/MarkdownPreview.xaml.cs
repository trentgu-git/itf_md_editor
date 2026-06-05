using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ITforceMarkdown.Engine;
using ITforceMarkdown.Services;
using ITforceMarkdown.Stores;
using Microsoft.Web.WebView2.Core;

namespace ITforceMarkdown.Views;

/// <summary>
/// WebView2 wrapper, 渲染 MarkdownEngine.DocumentHtml。
///
/// Read 模式 (IsEditable=false):  外部 SourceDraft 变化 → 重 load HTML
/// Rich 模式 (IsEditable=true):   WebView 是真理源, SourceDraft 变化 不 reload,
///                                否则用户每打一个字就闪屏一次 (死循环)
///
/// 切换 SelectedFile / Appearance 时无论哪种模式都 reload。
/// </summary>
public partial class MarkdownPreview : UserControl
{
    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(MarkdownPreview),
            new PropertyMetadata(false, OnEditableChanged));

    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    private WorkspaceStore Store => App.Store;
    private bool _wv2Ready;
    private string _lastReloadKey = "";
    private Guid _lastScrollToken;
    private Guid _lastRichCommandToken;

    public MarkdownPreview()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
    }

    private static void OnEditableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownPreview mp && mp._wv2Ready)
            _ = mp.ReloadAsync();
    }

    private async Task InitAsync()
    {
        // 防止 Loaded 多次触发 (UserControl 在父容器中被 remove/add 时会重复触发).
        if (_wv2Ready) return;

        try
        {
            var env = await WebView2Host.GetEnvironmentAsync();
            await WebView.EnsureCoreWebView2Async(env);

            // 注册 virtual host — HTML 里走 https://app.local/mermaid.min.js
            // 等 URL, 避免把 3.3MB mermaid.js inline 进 NavigateToString
            // (2MB 上限会撑爆, 报 "Value does not fall within the expected range").
            WebView2Host.RegisterVirtualHost(WebView.CoreWebView2);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            // 暂时开 DevTools 让用户能右键 → Inspect 排查渲染问题 (mermaid 没渲染等).
            // 稳定后可以改回 false.
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessage;

            _wv2Ready = true;
            Store.PropertyChanged += OnStoreChanged;
            ApplyZoom();  // 启动就把上次保存的缩放级别应用上
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"WebView2 init failed.\n\n{ex.Message}\n\n" +
                              "Please install Microsoft Edge WebView2 Runtime:\n" +
                              "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            ErrorLabel.Visibility = Visibility.Visible;
            WebView.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceStore.SelectedFile):
            case nameof(WorkspaceStore.Appearance):
                // 文件/主题切换: 两种模式都 reload
                Dispatcher.BeginInvoke(new Action(async () => await ReloadAsync()));
                break;

            case nameof(WorkspaceStore.SourceDraft):
                // ⚠️ Rich 模式下不能 reload! webview 是真理源, 自己的输入会
                // 通过 postMessage 回写 SourceDraft, 如果再 reload 整页就
                // 覆盖光标位置 → 用户感觉是"闪屏 / 输入卡死"。
                if (!IsEditable)
                    Dispatcher.BeginInvoke(new Action(async () => await ReloadAsync()));
                break;

            case nameof(WorkspaceStore.ScrollToken):
                Dispatcher.BeginInvoke(new Action(async () => await ScrollToTargetAsync()));
                break;

            case nameof(WorkspaceStore.RichCommandToken):
                if (IsEditable)
                    Dispatcher.BeginInvoke(new Action(async () => await ExecuteRichCommandAsync()));
                break;

            case nameof(WorkspaceStore.DocumentZoomLevel):
                Dispatcher.BeginInvoke(new Action(ApplyZoom));
                break;

            case nameof(WorkspaceStore.ReloadFromDiskToken):
                Dispatcher.BeginInvoke(new Action(async () => await ForceReloadAsync()));
                break;
        }
    }

    private void ApplyZoom()
    {
        if (!_wv2Ready) return;
        try { WebView.ZoomFactor = Store.DocumentZoomLevel; }
        catch { /* WebView 还没初始化好就忽略 */ }
    }

    /// <summary>外部点了 Reload from disk — 强制重新 NavigateToString, 不管 IsEditable.</summary>
    private async Task ForceReloadAsync()
    {
        _lastReloadKey = "";  // 失效 cache 让 ReloadAsync 真的重 navigate
        await ReloadAsync();
    }

    /// <summary>
    /// 触发查找: 走 JS prompt + window.find(). WebView2 原生 Find API 在
    /// 1.0.2792 上还是 experimental, 没暴露在 stable .NET 接口, 用 JS 兜底
    /// 最稳. 用户也可以直接 Ctrl+F (WebView2 默认开启浏览器 Find UI).
    /// </summary>
    public async Task ShowFindAsync()
    {
        if (!_wv2Ready) return;
        WebView.Focus();
        await WebView.CoreWebView2.ExecuteScriptAsync(@"
            (function(){
                var q = prompt('Find in document');
                if (!q) return;
                // window.find 在 Chromium/WebView2 上原生支持, 高亮第一个匹配并滚动到位.
                // 第二个 arg=false 不区分大小写, 第三个 arg=true 循环回开头.
                if (typeof window.find === 'function') window.find(q, false, false, true);
            })();");
    }

    private async Task ExecuteRichCommandAsync()
    {
        if (!_wv2Ready) return;
        if (Store.RichCommandToken == _lastRichCommandToken) return;
        _lastRichCommandToken = Store.RichCommandToken;
        var js = Store.RichCommandJs;
        if (string.IsNullOrEmpty(js)) return;
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { /* webview not ready, ignore */ }
    }

    private async Task ReloadAsync()
    {
        if (!_wv2Ready) return;

        // reload key:
        //   - 文件 / 模式 / 主题 共同决定
        //   - Read 模式才加 SourceDraft hash, 让外部 (Source 模式编辑后) 改动被捕获
        //   - Rich 模式不带 hash, 不会因自身输入触发 reload
        var key = IsEditable
            ? $"{Store.SelectedFile?.Path}|rich|{Store.Appearance}"
            : $"{Store.SelectedFile?.Path}|read|{Store.Appearance}|{Store.SourceDraft.GetHashCode()}";

        if (key == _lastReloadKey) return;
        _lastReloadKey = key;

        var html = MarkdownEngine.DocumentHtml(Store.SourceDraft, IsEditable, Store.Appearance);
        WebView.NavigateToString(html);
    }

    private async Task ScrollToTargetAsync()
    {
        if (!_wv2Ready) return;
        if (Store.ScrollToken == _lastScrollToken) return;
        _lastScrollToken = Store.ScrollToken;
        var id = Store.ScrollTargetId;
        if (string.IsNullOrEmpty(id)) return;

        await Task.Delay(80);
        var escaped = id.Replace("\\", "\\\\").Replace("'", "\\'");
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync($"__scrollToHeading('{escaped}')");
        }
        catch { /* webview not ready, ignore */ }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsEditable) return;
        try
        {
            // postMessage 发来的是 {type: 'editorChanged', markdown: '...'}
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t) &&
                t.GetString() == "editorChanged" &&
                doc.RootElement.TryGetProperty("markdown", out var md))
            {
                var markdown = md.GetString() ?? "";
                // 直接写, 不需要 _isApplyingFromStore 标志 — 因为 Rich 模式
                // 我们已经在 OnStoreChanged 里彻底忽略了 SourceDraft 变化。
                Store.SourceDraft = markdown;
            }
        }
        catch { /* ignore parse errors */ }
    }
}
