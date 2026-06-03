using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ITforceMarkdown.Engine;
using ITforceMarkdown.Stores;
using Microsoft.Web.WebView2.Core;

namespace ITforceMarkdown.Views;

/// <summary>
/// WebView2 wrapper, 渲染 MarkdownEngine.DocumentHtml。
/// Read 模式: editable=false; Rich 模式: editable=true, 监听 postMessage 把
/// 富文本编辑回吐的 markdown 同步进 Store.SourceDraft。
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
    private bool _isApplyingFromStore;
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
        try
        {
            // WebView2 用户数据目录放 LocalAppData, 避免污染默认浏览器配置
            var userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ITforceMarkdown", "WebView2Cache");
            Directory.CreateDirectory(userDataDir);
            var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
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
            case nameof(WorkspaceStore.SourceDraft):
            case nameof(WorkspaceStore.Appearance):
                if (!_isApplyingFromStore)
                    Dispatcher.BeginInvoke(new Action(async () => await ReloadAsync()));
                break;
            case nameof(WorkspaceStore.ScrollToken):
                Dispatcher.BeginInvoke(new Action(async () => await ScrollToTargetAsync()));
                break;
        }
    }

    private async Task ScrollToTargetAsync()
    {
        if (!_wv2Ready) return;
        if (Store.ScrollToken == _lastScrollToken) return;
        _lastScrollToken = Store.ScrollToken;
        var id = Store.ScrollTargetId;
        if (string.IsNullOrEmpty(id)) return;

        // 内容可能还没渲染完, 给一点缓冲, 否则 scrollIntoView 找不到目标
        await Task.Delay(80);
        var escaped = id.Replace("\\", "\\\\").Replace("'", "\\'");
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync($"__scrollToHeading('{escaped}')");
        }
        catch { /* webview 还没 ready 时静默忽略 */ }
    }

    private async Task ReloadAsync()
    {
        if (!_wv2Ready) return;
        var key = IsEditable
            ? Store.RichEditorReloadKey + (Store.SourceDraft.GetHashCode().ToString())
            : Store.RenderedHtmlReloadKey;
        if (key == _lastReloadKey) return;
        _lastReloadKey = key;

        var html = MarkdownEngine.DocumentHtml(Store.SourceDraft, IsEditable, Store.Appearance);
        WebView.NavigateToString(html);
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
                _isApplyingFromStore = true;
                try { Store.SourceDraft = markdown; }
                finally { _isApplyingFromStore = false; }
            }
        }
        catch { /* ignore parse errors */ }
    }
}
