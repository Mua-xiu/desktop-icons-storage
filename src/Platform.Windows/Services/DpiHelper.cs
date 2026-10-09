using System.Runtime.InteropServices;
using DesktopIconsStorage.Platform.Native;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>DPI 缩放辅助（进程为 PerMonitorV2，坐标一律使用物理像素）。</summary>
public static class DpiHelper
{
    /// <summary>系统（主屏）DPI 缩放倍数，如 150% → 1.5。</summary>
    public static double SystemScale => GetScale(NativeMethods.GetDpiForSystem());

    /// <summary>指定窗口所在显示器的 DPI 缩放倍数。</summary>
    public static double WindowScale(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? SystemScale : GetScale(NativeMethods.GetDpiForWindow(hwnd));

    /// <summary>创建窗口前按其位置读取目标显示器 DPI，避免双屏使用主屏缩放倍数。</summary>
    public static double ScaleAt(int x, int y)
    {
        try
        {
            var point = new NativeMethods.POINT { X = x, Y = y };
            var monitor = NativeMethods.MonitorFromPoint(point, 2);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0)
                return GetScale(dpiX);
        }
        catch { /* 旧系统或显示器切换中，继续使用主屏 DPI */ }
        return SystemScale;
    }

    /// <summary>主屏物理像素尺寸。</summary>
    public static (int W, int H) PrimaryScreenSize =>
        (NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1)); // SM_CXSCREEN/SM_CYSCREEN

    private static double GetScale(uint dpi) => dpi == 0 ? 1.0 : dpi / 96.0;

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType,
        out uint dpiX, out uint dpiY);
}
