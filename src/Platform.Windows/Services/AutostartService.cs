using Microsoft.Win32;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>开机自启：HKCU Run 键（免管理员，仅当前用户）。</summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopIconsStorage";
    private const string LegacyValueName = "DesktopOrganizer";
    private const string ProductKeyPath = @"Software\DesktopIconsStorage";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enable)
        {
            var exe = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exe != null) key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    /// <summary>记录当前收纳根目录，供卸载器在“保留数据”时向用户显示准确位置。</summary>
    public static void RecordStorageRoot(string storageRoot)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ProductKeyPath, true);
        key.SetValue("StorageRoot", storageRoot, RegistryValueKind.String);
    }

    /// <summary>正式更名后移除旧版自启项，避免两个名称同时启动。</summary>
    public static void RemoveLegacyRegistration()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        key.DeleteValue(LegacyValueName, false);
    }

    /// <summary>卸载清理时移除新旧自启项和安装元数据。</summary>
    public static void RemoveAllRegistrations()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
        {
            key.DeleteValue(ValueName, false);
            key.DeleteValue(LegacyValueName, false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(ProductKeyPath, false);
    }
}
