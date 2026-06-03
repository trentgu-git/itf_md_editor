using System;
using System.IO;
using System.Text.Json;

namespace ITforceMarkdown.Services;

/// <summary>
/// 把简单值类型 / JSON 对象持久化到 %LOCALAPPDATA%\ITforceMarkdown\
/// 等价 Mac 版 UserDefaults。每个 key 是一个文件, 写时原子替换 (.tmp → rename),
/// 防止 crash 写一半把数据搞坏。
/// </summary>
public static class PersistenceService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ITforceMarkdown");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    static PersistenceService()
    {
        Directory.CreateDirectory(Root);
    }

    private static string PathFor(string key) => Path.Combine(Root, key + ".json");

    public static T? Load<T>(string key) where T : class
    {
        var p = PathFor(key);
        if (!File.Exists(p)) return null;
        try
        {
            var json = File.ReadAllText(p);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            // 文件坏了就当不存在, 下次写新的覆盖。
            return null;
        }
    }

    public static T? LoadValue<T>(string key) where T : struct
    {
        var p = PathFor(key);
        if (!File.Exists(p)) return null;
        try
        {
            var json = File.ReadAllText(p);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static string? LoadString(string key)
    {
        var p = PathFor(key);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    public static void Save<T>(string key, T value)
    {
        var p = PathFor(key);
        var tmp = p + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOpts);
        File.WriteAllText(tmp, json);
        // atomic replace
        if (File.Exists(p)) File.Replace(tmp, p, null);
        else File.Move(tmp, p);
    }

    public static void SaveString(string key, string value)
    {
        var p = PathFor(key);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, value);
        if (File.Exists(p)) File.Replace(tmp, p, null);
        else File.Move(tmp, p);
    }

    public static void Delete(string key)
    {
        var p = PathFor(key);
        if (File.Exists(p)) File.Delete(p);
    }
}
