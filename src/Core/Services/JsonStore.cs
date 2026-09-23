using System.Text.Json;

namespace DesktopOrganizer.Core.Services;

/// <summary>JSON 配置读写，全部配置集中在 %APPDATA%\DesktopOrganizer。</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopOrganizer");

    public static string LayoutPath => Path.Combine(ConfigDir, "layout.json");
    public static string SettingsPath => Path.Combine(ConfigDir, "settings.json");

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
            }
        }
        catch
        {
            // 配置损坏时回退默认值，不阻塞启动
        }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, true);
        }
        catch
        {
            // 持久化失败不致命，下次保存再试
        }
    }
}
