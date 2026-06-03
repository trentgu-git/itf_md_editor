using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ITforceMarkdown.Models;
using ITforceMarkdown.Services;
using ITforceMarkdown.Stores;
using Microsoft.Win32;

namespace ITforceMarkdown.Views;

/// <summary>
/// GitLab Settings + Sync 对话框。Pro 版菜单入口: File ▸ GitLab ▸ Settings…
/// Sync 成功后, cache 目录会被自动 add 成 sidebar 里的一个 workspace。
///
/// 实际 sync 逻辑在 #if PRO 包住的 GitLabService 里, Local 编译时不存在 —
/// 这个 dialog 文件本身始终编译, 但 Sync_Click 内部对 SyncAsync 的调用也用 #if 包了。
/// </summary>
public partial class GitLabSettingsDialog : Window
{
    private WorkspaceStore Store => App.Store;

    public GitLabSettingsDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = Store.GitLabSettings;
        UrlBox.Text   = s.ProjectUrl;
        TokenBox.Password = s.AccessToken;
        UserBox.Text  = string.IsNullOrEmpty(s.Username) ? "oauth2" : s.Username;
        FolderBox.Text = string.IsNullOrEmpty(s.LocalCachePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ITforceMarkdown",
                "GitLabCache")
            : s.LocalCachePath;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose local cache folder",
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null!,
        };
        if (dlg.ShowDialog(this) == true)
            FolderBox.Text = dlg.FolderName;
    }

    private GitLabSettings ReadFields() => new()
    {
        ProjectUrl    = UrlBox.Text.Trim(),
        AccessToken   = TokenBox.Password,
        Username      = string.IsNullOrWhiteSpace(UserBox.Text) ? "oauth2" : UserBox.Text.Trim(),
        LocalCachePath = FolderBox.Text.Trim(),
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = ReadFields();
        Store.SaveGitLabSettings(s);
        DialogResult = true;
        Close();
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        var s = ReadFields();
        if (!s.IsConfigured)
        {
            StatusText.Text = "Please fill in URL, Access Token and cache folder.";
            return;
        }

        // 保存设置 (即便 sync 失败下次打开也能续上)
        Store.SaveGitLabSettings(s);

        SetBusy(true, "Connecting to GitLab… (this may take a while for large repos)");
        try
        {
#if PRO
            var result = await GitLabService.SyncAsync(s);
            if (result.Success)
            {
                StatusText.Text = "✓ " + result.Message;
                // sync 成功后自动添加为 workspace
                Store.AddWorkspace(s.LocalCachePath);
            }
            else
            {
                StatusText.Text = "✗ " + result.Message;
            }
#else
            await Task.Delay(50);
            StatusText.Text = "GitLab sync is only available in the Pro edition.";
#endif
        }
        catch (Exception ex)
        {
            StatusText.Text = "✗ " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        BtnSync.IsEnabled = !busy;
        BtnSave.IsEnabled = !busy;
        IsEnabled = !busy || (busy && true); // keep window enabled so Cancel still works
        if (status != null) StatusText.Text = status;
        Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }
}
