using System;
using System.IO;

namespace ITforceMarkdown.Models;

/// <summary>
/// 用户在 sidebar 里挂载的一个本地工作区。多工作区模型 —
/// sidebar 同时显示零个或多个 Workspace, 每个有自己的根目录、折叠状态、
/// "隐藏空文件夹" 偏好。
///
/// 持久化: 整个数组 JSON 编码到 %LOCALAPPDATA%\ITforceMarkdown\localWorkspaces.json
///
/// Windows 没有 sandbox, 不需要 macOS 那种 security-scoped bookmark — 普通
/// 文件路径就足够长期访问。
/// </summary>
public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>根目录绝对路径。</summary>
    public string Path { get; set; } = "";

    /// <summary>是否隐藏空文件夹 (不含 .md 的目录)。每个 workspace 独立设置。</summary>
    public bool HideEmptyFolders { get; set; } = true;

    /// <summary>sidebar 里这个 workspace section 当前是展开还是折叠。</summary>
    public bool IsExpanded { get; set; } = true;

    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    public bool Exists() => Directory.Exists(Path);
}
