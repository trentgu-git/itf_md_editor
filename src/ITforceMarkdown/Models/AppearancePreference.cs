namespace ITforceMarkdown.Models;

/// <summary>
/// 应用外观选择 — 对应 Mac 版 AppearancePreference。
/// System = 跟随 Windows 系统主题 (Win10 19H1+ / Win11)
/// Light  = 强制浅色
/// Dark   = 强制深色
/// </summary>
public enum AppearancePreference
{
    System,
    Light,
    Dark,
}

public static class AppearancePreferenceExtensions
{
    public static string DisplayName(this AppearancePreference p) => p switch
    {
        AppearancePreference.System => "Follow System",
        AppearancePreference.Light  => "Light",
        AppearancePreference.Dark   => "Dark",
        _ => p.ToString(),
    };
}
