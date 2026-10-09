namespace DesktopIconsStorage.Core.Services;

/// <summary>运行时路径隔离：测试模式只能读写独立沙盒，不能触及真实桌面文件。</summary>
public static class RuntimePaths
{
    private const string SandboxVariable = "DESKTOPICONSSTORAGE_TEST_ROOT";

    /// <summary>测试沙盒根目录；未设置时为正常用户环境。</summary>
    public static string? SandboxRoot
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(SandboxVariable);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
    }

    public static bool IsSandbox => SandboxRoot != null;

    /// <summary>测试运行使用沙盒桌面目录，实体盒还原也不会接触真实桌面。</summary>
    public static string DesktopPath => SandboxRoot is { } root
        ? Path.Combine(root, "Desktop")
        : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>测试运行的收纳根目录与配置目录均强制落在沙盒下。</summary>
    public static string? SandboxStorageRoot => SandboxRoot is { } root
        ? Path.Combine(root, "Storage")
        : null;

    public static string? SandboxConfigRoot => SandboxRoot is { } root
        ? Path.Combine(root, "Config")
        : null;

    /// <summary>测试模式拒绝沙盒外文件，避免开发验证误操作真实桌面。</summary>
    public static void EnsureSandboxPath(string path)
    {
        if (SandboxRoot is not { } root) return;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("测试模式只允许访问隔离目录中的文件。");
    }
}
