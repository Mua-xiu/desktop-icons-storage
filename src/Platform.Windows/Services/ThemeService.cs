using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>Win11 设置应用风格的中性色板，供设置窗口与桌面收纳盒共同使用。</summary>
public readonly record struct ThemePalette(
    Color WindowBackground,
    Color NavigationBackground,
    Color CardBackground,
    Color TextPrimary,
    Color TextSecondary,
    Color NavigationHeader,
    Color ControlBackground,
    Color ControlHover,
    Color ControlPressed,
    Color BorderSoft,
    Color TrackOff,
    Color PopupBackground,
    Color MenuBackground,
    Color MenuHover,
    Color MenuBorder);

/// <summary>读取系统主题（明暗 / 主题色）。</summary>
public static class ThemeService
{
    public static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }

    /// <summary>系统主题色（HKCU DWM AccentColor，ABGR）。</summary>
    public static Color GetAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false);
            if (key?.GetValue("AccentColor") is int abgr)
            {
                var u = unchecked((uint)abgr);
                return Color.FromRgb((byte)(u & 0xFF), (byte)((u >> 8) & 0xFF), (byte)((u >> 16) & 0xFF));
            }
        }
        catch { /* fall through */ }
        return Color.FromRgb(0x00, 0x78, 0xD4); // 默认 Windows 蓝
    }

    /// <summary>
    /// 返回统一的 Win11 深浅色板。主题色仅用于开关、滑块与选中态，
    /// 大面积背景保持中性，避免系统强调色把整个界面染色。
    /// </summary>
    public static ThemePalette GetPalette(bool dark) => dark
        ? new ThemePalette(
            WindowBackground: Color.FromRgb(0x20, 0x20, 0x20),
            NavigationBackground: Color.FromRgb(0x20, 0x20, 0x20),
            CardBackground: Color.FromRgb(0x2B, 0x2B, 0x2B),
            TextPrimary: Color.FromRgb(0xFF, 0xFF, 0xFF),
            TextSecondary: Color.FromRgb(0xC5, 0xC5, 0xC5),
            NavigationHeader: Color.FromRgb(0x9D, 0x9D, 0x9D),
            ControlBackground: Color.FromRgb(0x2D, 0x2D, 0x2D),
            ControlHover: Color.FromRgb(0x35, 0x35, 0x35),
            ControlPressed: Color.FromRgb(0x28, 0x28, 0x28),
            BorderSoft: Color.FromRgb(0x3D, 0x3D, 0x3D),
            TrackOff: Color.FromRgb(0x5A, 0x5A, 0x5A),
            PopupBackground: Color.FromRgb(0x2B, 0x2B, 0x2B),
            MenuBackground: Color.FromRgb(0x2B, 0x2B, 0x2E),
            MenuHover: Color.FromRgb(0x3F, 0x4A, 0x5C),
            MenuBorder: Color.FromRgb(0x50, 0x50, 0x55))
        : new ThemePalette(
            WindowBackground: Color.FromRgb(0xF3, 0xF3, 0xF3),
            NavigationBackground: Color.FromRgb(0xF3, 0xF3, 0xF3),
            CardBackground: Color.FromRgb(0xFB, 0xFB, 0xFB),
            TextPrimary: Color.FromRgb(0x1A, 0x1A, 0x1A),
            TextSecondary: Color.FromRgb(0x5D, 0x5D, 0x5D),
            NavigationHeader: Color.FromRgb(0x6B, 0x6B, 0x6B),
            ControlBackground: Color.FromRgb(0xFB, 0xFB, 0xFB),
            ControlHover: Color.FromRgb(0xF6, 0xF6, 0xF6),
            ControlPressed: Color.FromRgb(0xEE, 0xEE, 0xEE),
            BorderSoft: Color.FromRgb(0xE5, 0xE5, 0xE5),
            TrackOff: Color.FromRgb(0x8A, 0x8A, 0x8A),
            PopupBackground: Color.FromRgb(0xFA, 0xFA, 0xFA),
            MenuBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            MenuHover: Color.FromRgb(0xE9, 0xE9, 0xEA),
            MenuBorder: Color.FromRgb(0xDC, 0xDC, 0xDC));
}
