using System.Text.Json;

namespace DesktopIconsStorage.Core.Services;

/// <summary>JSON 配置读写，全部配置集中在 %APPDATA%\DesktopIconsStorage。</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string? ConfigOverride =>
        Environment.GetEnvironmentVariable("DESKTOPICONSSTORAGE_CONFIG_DIR");

    public static string ConfigDir => RuntimePaths.SandboxConfigRoot
        ?? (string.IsNullOrWhiteSpace(ConfigOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopIconsStorage")
            : Path.GetFullPath(ConfigOverride));

    /// <summary>0.1.x 版本使用的旧配置目录，仅用于一次性兼容迁移。</summary>
    public static string LegacyConfigDir => RuntimePaths.SandboxRoot is { } sandbox
        ? Path.Combine(sandbox, "LegacyConfig")
        : string.IsNullOrWhiteSpace(ConfigOverride)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopOrganizer")
        : Path.Combine(ConfigDir, "legacy");

    public static string LayoutPath => Path.Combine(ConfigDir, "layout.json");
    public static string SettingsPath => Path.Combine(ConfigDir, "settings.json");

    /// <summary>首次以新名称启动时复制旧版设置和布局，避免用户已有收纳信息丢失。</summary>
    public static void MigrateLegacyFiles()
    {
        try
        {
            if (!Directory.Exists(LegacyConfigDir)) return;
            Directory.CreateDirectory(ConfigDir);
            foreach (var fileName in new[] { "settings.json", "layout.json" })
            {
                var source = Path.Combine(LegacyConfigDir, fileName);
                var destination = Path.Combine(ConfigDir, fileName);
                if (File.Exists(source) && !File.Exists(destination))
                    File.Copy(source, destination);
            }
        }
        catch
        {
            // 迁移失败时仍允许应用用默认设置启动，旧目录保持不变便于手动恢复。
        }
    }

    /// <summary>用户在卸载器中明确选择彻底清理后，删除新旧版本的配置目录。</summary>
    public static void DeleteConfigurationForUninstall()
    {
        if (Directory.Exists(ConfigDir)) Directory.Delete(ConfigDir, recursive: true);
        if (Directory.Exists(LegacyConfigDir)) Directory.Delete(LegacyConfigDir, recursive: true);
    }

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
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(ConfigDir);
            // 唯一临时文件避免快速连续保存时相互覆盖同一个 .tmp 文件。
            tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, true);
        }
        catch
        {
            // 持久化失败不致命，下次保存再试
            try
            {
                if (tmp != null && File.Exists(tmp)) File.Delete(tmp);
            }
            catch { /* 临时文件清理失败不覆盖原始保存错误 */ }
        }
    }
}
