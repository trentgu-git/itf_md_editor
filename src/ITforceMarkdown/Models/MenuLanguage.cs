using System.Globalization;

namespace ITforceMarkdown.Models;

/// <summary>
/// 用户在 View ▸ Language 里选的语言. 对应 Mac 端 MenuLanguage.
/// 只有菜单 + 少量高频 UI 走双语切换, 整个 app 不用 .resx 本地化
/// (.resx 在 WPF 里改一条就得整体重新加载, 太麻烦).
/// </summary>
public enum MenuLanguage
{
    /// <summary>跟随系统 Locale — 中文系统显示中文, 其他显示英文.</summary>
    System,
    English,
    Chinese,
}

public static class MenuLanguageExtensions
{
    /// <summary>选项自己语言下的显示名 (Language 子菜单里给用户看).</summary>
    public static string DisplayName(this MenuLanguage lang) => lang switch
    {
        MenuLanguage.System  => "Follow System / 跟随系统",
        MenuLanguage.English => "English",
        MenuLanguage.Chinese => "中文",
        _ => lang.ToString(),
    };
}

/// <summary>
/// 双语单点查表. 调用: <c>L10n.T(menuLanguage, "Save", "保存")</c>.
/// 比 NSLocalizedString / .resx 简洁: 源代码里两种语言并排, 一眼能对比.
/// </summary>
public static class L10n
{
    /// <summary>把 .System 解析成具体语言, 让下游只看 English/Chinese 两个分支.</summary>
    public static MenuLanguage Effective(MenuLanguage lang)
    {
        if (lang != MenuLanguage.System) return lang;
        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName == "zh" ? MenuLanguage.Chinese : MenuLanguage.English;
    }

    /// <summary>主入口: 根据 lang 返回 en 或 zh 文案.</summary>
    public static string T(MenuLanguage lang, string en, string zh)
        => Effective(lang) == MenuLanguage.Chinese ? zh : en;
}
