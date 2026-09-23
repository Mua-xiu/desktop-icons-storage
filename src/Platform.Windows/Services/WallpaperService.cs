using System.Windows;
using System.Windows.Media.Imaging;
using DesktopOrganizer.Platform.Native;
using Microsoft.Win32;

namespace DesktopOrganizer.Platform.Services;

/// <summary>
/// 自绘毛玻璃的壁纸取样：读取当前壁纸图，按块所在屏幕区域做"填充"模式裁剪。
/// 相比 DWM 亚克力，自绘可以完美配合 WPF 圆角裁剪，且透明度完全可调。
/// </summary>
public static class WallpaperService
{
    /// <summary>当前壁纸文件路径（注册表），取不到返回 null。</summary>
    public static string? GetWallpaperPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false);
            var path = key?.GetValue("Wallpaper") as string;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch { return null; }
    }

    /// <summary>
    /// 裁剪出覆盖屏幕矩形（物理像素）的壁纸区域。
    /// Windows 默认"填充"模式：等比放大取 max 缩放、居中裁切。
    /// </summary>
    public static BitmapSource? CropForRect(Rect screenRectPhysical)
    {
        try
        {
            var path = GetWallpaperPath();
            if (path == null || !System.IO.File.Exists(path)) return null;

            // 块所在显示器的物理范围
            var monitorRect = GetMonitorRect(screenRectPhysical);

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            var iw = image.PixelWidth;
            var ih = image.PixelHeight;
            if (iw <= 0 || ih <= 0) return null;

            var mw = monitorRect.Width;
            var mh = monitorRect.Height;
            if (mw <= 0 || mh <= 0) return null;

            // 填充模式：scale = max(mw/iw, mh/ih)，居中裁切
            var scale = Math.Max(mw / iw, mh / ih);
            var scaledW = iw * scale;
            var scaledH = ih * scale;
            var offX = (scaledW - mw) / 2;
            var offY = (scaledH - mh) / 2;

            // 块矩形相对显示器左上角 → 换算回壁纸图像像素
            var relX = screenRectPhysical.X - monitorRect.X;
            var relY = screenRectPhysical.Y - monitorRect.Y;
            var cropX = (int)Math.Round((relX + offX) / scale);
            var cropY = (int)Math.Round((relY + offY) / scale);
            var cropW = (int)Math.Round(screenRectPhysical.Width / scale);
            var cropH = (int)Math.Round(screenRectPhysical.Height / scale);

            cropX = Math.Clamp(cropX, 0, Math.Max(0, iw - 1));
            cropY = Math.Clamp(cropY, 0, Math.Max(0, ih - 1));
            cropW = Math.Clamp(cropW, 1, iw - cropX);
            cropH = Math.Clamp(cropH, 1, ih - cropY);

            var cropped = new CroppedBitmap(image, new Int32Rect(cropX, cropY, cropW, cropH));
            cropped.Freeze();

            // 降采样到 ~48px 宽：放大回填时线性插值即天然模糊，
            // 避免 BlurEffect 在逐像素透明窗口上的渲染问题，且性能更好
            var k = 48.0 / Math.Max(1, cropped.PixelWidth);
            var small = new TransformedBitmap(cropped, new System.Windows.Media.ScaleTransform(k, k));
            small.Freeze();
            return small;
        }
        catch { return null; }
    }

    private static Rect GetMonitorRect(Rect screenRect)
    {
        try
        {
            var pt = new NativeMethods.POINT
            {
                X = (int)(screenRect.X + screenRect.Width / 2),
                Y = (int)(screenRect.Y + screenRect.Height / 2)
            };
            var mon = NativeMethods.MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (mon != IntPtr.Zero)
            {
                var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(mon, ref info))
                {
                    var r = info.rcMonitor;
                    return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                }
            }
        }
        catch { /* 回退主屏 */ }

        var w = NativeMethods.GetSystemMetrics(0);
        var h = NativeMethods.GetSystemMetrics(1);
        return new Rect(0, 0, w, h);
    }
}
