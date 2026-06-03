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

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessage;

            _wv2Ready = true;
            Store.PropertyChanged += OnStoreChanged;
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
        }
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
