namespace ITforceMarkdown.Models;

/// <summary>
/// Pro 版 GitLab 集成的用户配置。持久化到 %LOCALAPPDATA%\ITforceMarkdown\gitLabSettings.json
///
/// 仅 Pro flavor 编辑 / 使用; Local flavor 不在 UI 暴露入口, 类型本身放在 shared 代码里
/// 不会拖累 Local — 没引用就不参与运行。
/// </summary>
public sealed class GitLabSettings
{
    /// <summary>GitLab 项目克隆 URL, 例如 https://gitlab.com/group/repo.git</summary>
    public string ProjectUrl { get; set; } = "";

    /// <summary>
    /// HTTPS clone/pull 用的认证 token。GitLab Personal Access Token,
    /// 或 deploy token; scope 至少要 read_repository。
    ///
    /// ⚠️ 当前实现明文存到 %LOCALAPPDATA%。生产可改用 DPAPI (CryptProtectData)
    /// 加密, 朋友试用阶段先简化。
    /// </summary>
    public string AccessToken { get; set; } = "";

    /// <summary>用户名 — HTTPS 走 token 时一般填 "oauth2"; 或者真实 GitLab 用户名。</summary>
    public string Username { get; set; } = "oauth2";

    /// <summary>本地 cache 目录, repo 会克隆 / pull 到这里。</summary>
    public string LocalCachePath { get; set; } = "";

    /// <summary>这条配置是否填齐了能 sync 的最少字段。</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectUrl) &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(LocalCachePath);
}
