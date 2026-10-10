using DesktopIconsStorage.Platform.Native;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>统一窗口外轮廓：收纳窗口自行裁剪，标准窗口由系统绘制标题栏与圆角。</summary>
public static class WindowChromeService
{
    /// <summary>沿用实体盒的外圆角半径，单位 DIP；原生区域按窗口 DPI 换算。</summary>
    public const int SurfaceRadiusDip = 8;

    /// <summary>收纳窗口唯一一层 WPF 描边的宽度，单位 DIP。</summary>
    public const int SurfaceBorderDip = 1;

    /// <summary>裁剪无边框收纳窗口的原生材质，宽高为物理像素。</summary>
    public static void ApplySurface(IntPtr hwnd, int width, int height)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        try
        {
            // 2026-10-10：保留旧实体盒的 DWM 圆角偏好；禁用它会让 Acrylic 底层露出矩形角。
            int preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            int borderColor = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(hwnd,
                NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch { /* 旧系统继续使用原生区域裁剪。 */ }

        var diameter = (int)(2 * SurfaceRadiusDip * DpiHelper.WindowScale(hwnd));
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width, height, diameter, diameter);
        // 设置成功后区域归系统所有；失败时由调用方释放，避免 GDI 资源泄漏。
        if (region != IntPtr.Zero && NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
            NativeMethods.DeleteObject(region);
    }

    /// <summary>标准设置窗口使用系统圆角，保留系统标题栏、边框和阴影。</summary>
    public static void ApplyStandardWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
        try
        {
            int preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch { /* 旧系统保留默认系统外观。 */ }
    }
}
