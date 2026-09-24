using DesktopIconsStorage.Core.Models;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>按预览行列和显示器工作区计算小盒尺寸，保留网格比例并避免越屏。</summary>
public static class LinkBasketSize
{
    public static (int Width, int Height) Calculate(int rows, int columns,
        double dpiScale, int workWidth, int workHeight)
    {
        rows = Math.Clamp(rows, PreviewGridLimits.Minimum, PreviewGridLimits.Maximum);
        columns = Math.Clamp(columns, PreviewGridLimits.Minimum, PreviewGridLimits.Maximum);
        var desiredWidth = (columns * 52 + 24) * dpiScale;
        var desiredHeight = (rows * 52 + 55) * dpiScale;
        var margin = Math.Max(12 * dpiScale, 12);
        var availableWidth = Math.Max(1, workWidth - 2 * margin);
        var availableHeight = Math.Max(1, workHeight - 2 * margin);
        var fit = Math.Min(1.0,
            Math.Min(availableWidth / desiredWidth, availableHeight / desiredHeight));
        return (Math.Max(1, (int)Math.Round(desiredWidth * fit)),
            Math.Max(1, (int)Math.Round(desiredHeight * fit)));
    }
}
