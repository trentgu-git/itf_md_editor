using System;
using System.Windows;
using Microsoft.Win32;
using ITforceMarkdown.Models;

namespace ITforceMarkdown.Services;

/// <summary>
/// 切换 App 的颜色字典 (Light / Dark)。System 模式下监听
/// Windows 注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme,
/// 通过 SystemEvents.UserPreferenceChanged 收到变化时自动切换。
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppearancePreference _current = AppearancePreference.System;
    private static ResourceDictionary? _activeColors;

    public static void Initialize(AppearancePreference initial)
    {
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        Apply(initial);
    }

    public static void Apply(AppearancePreference pref)
    {
        _current = pref;
        var dark = pref switch
        {
            AppearancePreference.Light => false,
            AppearancePreference.Dark  => true,
            _ => IsSystemDark(),
        };
        SwapDictionary(dark);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // 0 = Dark, 1 = Light (Windows 反着的)
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SwapDictionary(bool dark)
    {
        var app = Application.Current;
        if (app == null) return;

        var uri = dark
            ? new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
        var newDict = new ResourceDictionary { Source = uri };

        if (_activeColors != null)
            app.Resources.MergedDictionaries.Remove(_activeColors);
        app.Resources.MergedDictionaries.Insert(0, newDict);
        _activeColors = newDict;
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_current != AppearancePreference.System) return;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => SwapDictionary(IsSystemDark())));
    }
}
