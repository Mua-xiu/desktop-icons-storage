using DesktopOrganizer.Platform.Native;

namespace DesktopOrganizer.Platform.Services;

/// <summary>DPI 缩放辅助（进程为 PerMonitorV2，坐标一律使用物理像素）。</summary>
public static class DpiHelper
{
    /// <summary>系统（主屏）DPI 缩放倍数，如 150% → 1.5。</summary>
    public static double SystemScale => GetScale(NativeMethods.GetDpiForSystem());

    /// <summary>指定窗口所在显示器的 DPI 缩放倍数。</summary>
    public static double WindowScale(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? SystemScale : GetScale(NativeMethods.GetDpiForWindow(hwnd));

    /// <summary>主屏物理像素尺寸。</summary>
    public static (int W, int H) PrimaryScreenSize =>
        (NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1)); // SM_CXSCREEN/SM_CYSCREEN

    private static double GetScale(uint dpi) => dpi == 0 ? 1.0 : dpi / 96.0;
}
