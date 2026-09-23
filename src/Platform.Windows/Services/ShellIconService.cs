using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DesktopOrganizer.Platform.Native;

namespace DesktopOrganizer.Platform.Services;

/// <summary>Shell 图标提取（IShellItemImageFactory，高质量，支持缩略图缓存）。</summary>
public static class ShellIconService
{
    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// 提取文件/文件夹图标。可能阻塞（缩略图生成），请在后台线程调用。
    /// 返回的 BitmapSource 已 Freeze，可跨线程使用。
    /// </summary>
    public static BitmapSource? GetIcon(string path, int size = 48)
    {
        IntPtr hbm = IntPtr.Zero;
        object? obj = null;
        try
        {
            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItemImageFactory, out obj);
            var factory = (IShellItemImageFactory)obj;
            factory.GetImage(
                new NativeMethods.SIZE { cx = size, cy = size },
                SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK,
                out hbm);
            if (hbm == IntPtr.Zero) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) NativeMethods.DeleteObject(hbm);
            if (obj != null) Marshal.ReleaseComObject(obj);
        }
    }
}
