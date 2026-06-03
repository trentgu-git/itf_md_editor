using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ITforceMarkdown.Models;

namespace ITforceMarkdown.Services;

/// <summary>
/// 主题管理 — 全程代码构建 ResourceDictionary, 不走 pack URI。
///
/// 调用顺序:
///   1. App.OnStartup → ThemeManager.InstallBrushes()  → 装 DynamicResource 链尾的 Brush
///   2. App.OnStartup → ThemeManager.Initialize(pref)  → 根据偏好装 Light 或 Dark 颜色字典
///   3. 用户改 Appearance → ThemeManager.Apply(pref)   → 换颜色字典
///
/// System 模式监听 SystemEvents.UserPreferenceChanged, 切系统主题时自动跟。
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppearancePreference _current = AppearancePreference.System;
    private static ResourceDictionary? _colorsDict;
    private static ResourceDictionary? _brushesDict;
    private static bool _hooked;

    // ───────────── 颜色定义 (跟 Themes/Light.xaml + Dark.xaml 同色) ─────────────
    private static readonly (string Key, string Light, string Dark)[] Colors = new[]
    {
        ("Color.Accent",                 "#F59B14", "#F59B14"),
        ("Color.Window",                 "#F5F5F7", "#1C1C1E"),
        ("Color.Card",                   "#FFFFFF", "#2B2B2D"),
        ("Color.Sidebar",                "#FAF7F0", "#212123"),
        ("Color.SidebarSectionActive",   "#FCEFD4", "#3B3422"),
        ("Color.SidebarSectionInactive", "#F2EFE8", "#2B2B2D"),
        ("Color.SelectedFile",           "#FFF0D1", "#523F1F"),
        ("Color.OutlineBg",              "#F7F7FA", "#212123"),
        ("Color.EditorBg",               "#F5F5F7", "#1F1F21"),
        ("Color.Ink",                    "#1C2433", "#E5E7EB"),
        ("Color.Muted",                  "#6B7585", "#98989D"),
        ("Color.Placeholder",            "#8C8C8C", "#8C8C8C"),
        ("Color.FolderText",             "#3C5473", "#9DB8E2"),
        ("Color.FolderIcon",             "#D16E2E", "#F2A763"),
        ("Color.ToolbarIcon",            "#484D54", "#D8D8DA"),
        ("Color.Line",                   "#E0D7C2", "#3A3A3C"),
        ("Color.SelectionInk",           "#5C3608", "#FAE7B7"),
        ("Color.ToolbarBg",              "#FFFFFF", "#2B2B2D"),
        ("Color.Toast",                  "#1C2433", "#E5E7EB"),
    };

    /// <summary>
    /// 装 SolidColorBrush 字典 — DynamicResource 链的下游, 颜色通过
    /// DynamicResource 反查 _colorsDict, 换主题时自动跟随。
    /// </summary>
    public static void InstallBrushes()
    {
        if (_brushesDict != null) return;
        var b = new ResourceDictionary();
        foreach (var (key, _, _) in Colors)
        {
            var brushKey = "Brush" + key.Substring("Color".Length); // "Color.X" → "Brush.X"
            b[brushKey] = new SolidColorBrush
            {
                Color = (Color)ColorConverter.ConvertFromString("#FFFFFF"), // 占位, 实际靠 DynamicResource
            };
        }
        // 重新构建为 DynamicResource binding 版本
        b.Clear();
        foreach (var (key, _, _) in Colors)
        {
            var brushKey = "Brush" + key.Substring("Color".Length);
            var brush = new SolidColorBrush();
            // 用 SetResourceReference 让 Brush 跟随 Color 字典的 DynamicResource
            // 这里只能做 WPF FrameworkElement.SetResourceReference 不能给 brush 用,
            // 退回到代码: 直接绑定到 _colorsDict, 切主题时同时换 brush 字典。
            b[brushKey] = brush;
        }
        _brushesDict = b;
        Application.Current.Resources.MergedDictionaries.Add(b);
    }

    public static void Initialize(AppearancePreference initial)
    {
        if (!_hooked)
        {
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            _hooked = true;
        }
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

        // 1. 装新的 Color 字典 (替换旧的)
        var newColors = new ResourceDictionary();
        foreach (var (key, light, darkHex) in Colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? darkHex : light);
            newColors[key] = color;
        }

        var app = Application.Current;
        if (_colorsDict != null) app.Resources.MergedDictionaries.Remove(_colorsDict);
        app.Resources.MergedDictionaries.Insert(0, newColors);
        _colorsDict = newColors;

        // 2. 把 brush 字典里每个 brush 的 color 重新设上 (因为 SolidColorBrush
        //    没法对 Color 做 DynamicResource — 必须代码替换)。
        if (_brushesDict != null)
        {
            foreach (var (key, light, darkHex) in Colors)
            {
                var brushKey = "Brush" + key.Substring("Color".Length);
                if (_brushesDict[brushKey] is SolidColorBrush brush)
                {
                    var color = (Color)ColorConverter.ConvertFromString(dark ? darkHex : light);
                    if (brush.IsFrozen)
                    {
                        _brushesDict[brushKey] = new SolidColorBrush(color);
                    }
                    else
                    {
                        brush.Color = color;
                    }
                }
            }
        }
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

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_current != AppearancePreference.System) return;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(_current)));
    }
}
