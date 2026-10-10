namespace DesktopIconsStorage.Core.Services;

/// <summary>
/// 桌面条目来源：Explorer 的桌面是用户桌面与公共桌面两个物理目录的合并视图，
/// 来源决定允许的操作（公共桌面对标准用户只读）。
/// </summary>
public enum DesktopOrigin
{
    Other,
    UserDesktop,
    PublicDesktop,
}

/// <summary>
/// 桌面目录解析（2026-10-09 竞品分析结论，详见 docs/改进路线图-双桌面目录与体验增强.md）。
/// 两条硬规则：
/// 一、解析永远走 known-folder API 且实时进行，不缓存、不手拼 %USERPROFILE%\Desktop——
///     OneDrive Known Folder Move 可能在应用安装后把桌面改指到 %USERPROFILE%\OneDrive\Desktop；
/// 二、凡是"放回/写入桌面"的操作统一落用户桌面（可写、免管理员），
///     公共桌面来源的项目归还时也落到用户桌面。
/// </summary>
public static class DesktopLocations
{
    /// <summary>用户桌面（跟随 OneDrive KFM 重定向）。</summary>
    public static string UserDesktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>公共桌面（所有用户共享，标准用户只读）；取不到时返回 null。</summary>
    public static string? PublicDesktop
    {
        get
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
    }

    /// <summary>判断路径位于用户桌面、公共桌面还是其他位置。</summary>
    public static DesktopOrigin GetOrigin(string path)
    {
        if (PublicDesktop is { } pub && IsUnder(path, pub)) return DesktopOrigin.PublicDesktop;
        if (IsUnder(path, UserDesktop)) return DesktopOrigin.UserDesktop;
        return DesktopOrigin.Other;
    }

    /// <summary>路径是否位于公共桌面内（移动/删除其中的项目需要管理员权限）。</summary>
    public static bool IsUnderPublicDesktop(string path) =>
        GetOrigin(path) == DesktopOrigin.PublicDesktop;

    private static bool IsUnder(string path, string directory)
    {
        string full;
        string dir;
        try
        {
            full = Path.GetFullPath(path);
            dir = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return false; }

        return string.Equals(full, dir, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
