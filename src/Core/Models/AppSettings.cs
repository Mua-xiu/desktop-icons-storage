namespace DesktopOrganizer.Core.Models;

/// <summary>应用设置，持久化到 settings.json。</summary>
public class AppSettings
{
    /// <summary>开机自启。</summary>
    public bool AutoStart { get; set; }

    /// <summary>主题模式：system / light / dark。</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>收纳根目录，默认 %USERPROFILE%\DesktopBlocks。</summary>
    public string StorageRoot { get; set; } = DefaultStorageRoot();

    /// <summary>块背景不透明度 0.2 - 1.0（作用于毛玻璃 tint）。</summary>
    public double BackdropOpacity { get; set; } = 0.55;

    /// <summary>是否启用毛玻璃（关闭则使用纯色）。</summary>
    public bool BlurEnabled { get; set; } = true;

    /// <summary>着色模式：accent（系统主题色）/ none。</summary>
    public string TintMode { get; set; } = "accent";

    /// <summary>关闭设置窗口时的行为：ask（每次询问）/ hide（隐藏到托盘）/ exit（退出应用）。</summary>
    public string CloseBehavior { get; set; } = "ask";

    /// <summary>是否在图标下显示文件名（默认关闭：仅图标，悬停浮出名称）。</summary>
    public bool ShowIconNames { get; set; }

    /// <summary>图标背景框（瓦片）不透明度 0.1 - 0.8。</summary>
    public double IconTileOpacity { get; set; } = 0.28;

    /// <summary>图标尺寸（32/40/48）。</summary>
    public int IconSize { get; set; } = 40;

    /// <summary>鼠标移开后自动淡出块内容。</summary>
    public bool AutoFadeEnabled { get; set; }

    /// <summary>自动淡出时的不透明度 0.05 - 0.5。</summary>
    public double AutoFadeOpacity { get; set; } = 0.15;

    public static string DefaultStorageRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DesktopBlocks");
}
