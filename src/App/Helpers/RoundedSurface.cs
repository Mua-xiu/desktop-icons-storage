using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopIconsStorage.Platform.Services;
using Size = System.Windows.Size;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>收纳窗口共用外框：统一圆角、单层描边，并裁剪内部背景与内容。</summary>
public sealed class RoundedSurface : Border
{
    public RoundedSurface()
    {
        CornerRadius = new CornerRadius(WindowChromeService.SurfaceRadiusDip);
        BorderThickness = new Thickness(WindowChromeService.SurfaceBorderDip);
    }

    /// <summary>布局后以内容实际尺寸裁剪，内圆角扣除描边宽度，避免双层弧线。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (Child != null)
        {
            var radius = WindowChromeService.SurfaceRadiusDip - WindowChromeService.SurfaceBorderDip;
            var clip = new RectangleGeometry(new Rect(Child.RenderSize), radius, radius);
            clip.Freeze();
            Child.Clip = clip;
        }
        return arranged;
    }
}
