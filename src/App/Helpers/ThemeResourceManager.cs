using System.Windows;
using System.Windows.Media;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>把统一主题色板写入应用级动态资源，供所有窗口和对话框共享。</summary>
public static class ThemeResourceManager
{
    public static void Apply(ThemePalette palette, Color accent)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        void Set(string key, Color color) => resources[key] = new SolidColorBrush(color);

        Set("WindowBg", palette.WindowBackground);
        Set("NavBg", palette.NavigationBackground);
        Set("CardBg", palette.CardBackground);
        Set("TextPrimary", palette.TextPrimary);
        Set("TextSecondary", palette.TextSecondary);
        Set("NavHeader", palette.NavigationHeader);
        Set("ControlBg", palette.ControlBackground);
        Set("ControlBgHover", palette.ControlHover);
        Set("ControlBgPress", palette.ControlPressed);
        Set("BorderSoft", palette.BorderSoft);
        Set("TrackOff", palette.TrackOff);
        Set("PopupBg", palette.PopupBackground);
        Set("Accent", accent);
    }
}
