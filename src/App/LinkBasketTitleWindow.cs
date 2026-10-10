using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;
using FontFamily = System.Windows.Media.FontFamily;

namespace DesktopIconsStorage.App;

/// <summary>链接筐外部标题：透明独立窗口，不让盒子的 Acrylic 材质延伸到名称区域。</summary>
public sealed class LinkBasketTitleWindow : IDisposable
{
    private const int TitleHeightDip = 24; // 标题行高度，包含文字阴影留白。
    private const int TitleGapDip = 4; // 标题与盒子外框的间距。
    private readonly BlockWindow _tile;
    private HwndSource? _source;
    private readonly TextBlock _text = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI"),
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 2, 0),
        Effect = new DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.95
        }
    };

    /// <summary>绑定盒子名称和几何变化，跟随拖动、规格调整及名称开关。</summary>
    public LinkBasketTitleWindow(BlockWindow tile)
    {
        _tile = tile;
        tile.Block.PropertyChanged += OnBlockChanged;
        Update();
    }

    /// <summary>只同步影响外部标题的属性，避免文件列表刷新重建标题窗口。</summary>
    private void OnBlockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Block.Name) or nameof(Block.ShowBlockName) or
            nameof(Block.X) or nameof(Block.Y) or nameof(Block.Width))
            Update();
    }

    /// <summary>按盒子所在屏幕 DPI 定位透明标题；隐藏时销毁窗口以释放资源。</summary>
    private void Update()
    {
        if (!_tile.Block.ShowBlockName)
        {
            CloseSource();
            return;
        }
        var scale = DpiHelper.WindowScale(_tile.Hwnd);
        var width = Math.Max(1, (int)_tile.Block.Width);
        var height = Math.Max(1, (int)(TitleHeightDip * scale));
        var x = (int)_tile.Block.X;
        var y = (int)_tile.Block.Y - height - (int)(TitleGapDip * scale);
        if (_source == null)
        {
            var parameters = new HwndSourceParameters("DesktopIconsStorage.LinkBasketTitle")
            {
                ParentWindow = _tile.Hwnd,
                WindowStyle = unchecked((int)0x80000000) | 0x10000000, // POPUP、VISIBLE。
                ExtendedWindowStyle = 0x80 | 0x08000000 | 0x20, // 工具窗、不抢焦点、鼠标穿透。
                UsesPerPixelOpacity = true,
                PositionX = x, PositionY = y, Width = width, Height = height
            };
            _source = new HwndSource(parameters)
            {
                CompositionTarget = { BackgroundColor = Colors.Transparent },
                RootVisual = _text
            };
        }
        _text.Text = _tile.Block.Name;
        DesktopEmbedService.SetOverlayBounds(_source.Handle, x, y, width, height);
    }

    /// <summary>盒子删除或 Explorer 重建时解除模型订阅，关闭伴随标题。</summary>
    public void Dispose()
    {
        _tile.Block.PropertyChanged -= OnBlockChanged;
        CloseSource();
    }

    /// <summary>解除文字与窗口的关联，使反复隐藏、显示时可以安全复用文字控件。</summary>
    private void CloseSource()
    {
        if (_source == null) return;
        _source.RootVisual = null;
        _source.Dispose();
        _source = null;
    }
}
