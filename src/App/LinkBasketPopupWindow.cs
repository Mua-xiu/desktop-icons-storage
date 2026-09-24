using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.App.Views;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App;

/// <summary>链接筐的独立桌面弹窗：在所属显示器居中并与小盒做双向几何过渡。</summary>
public sealed class LinkBasketPopupWindow : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsExToolWindow = 0x00000080;
    private const int AnimationDurationMs = 220;

    private readonly BlockWindow _tile;
    private readonly AppHost _host;
    private HwndSource? _source;
    private LinkBasketPopupView? _view;
    private DispatcherTimer? _timer;
    private IntPtr _desktopHost;
    private bool _closing;
    private bool _ready;
    private double _progress;
    private readonly (int X, int Y, int W, int H) _start;
    private readonly (int X, int Y, int W, int H) _finish;

    public IntPtr Hwnd => _source?.Handle ?? IntPtr.Zero;
    public bool IsOpen => _source != null && !_closing;

    public LinkBasketPopupWindow(BlockWindow tile, AppHost host)
    {
        _tile = tile;
        _host = host;
        _start = ((int)tile.Block.X, (int)tile.Block.Y,
            (int)tile.Block.Width, (int)tile.Block.Height);
        var area = DesktopEmbedService.GetNearestMonitorWorkArea(
            _start.X, _start.Y, _start.W, _start.H);
        var scale = DpiHelper.WindowScale(tile.Hwnd);
        // 大窗口增加可见图标容量，同时仍限制在所属显示器的工作区内。
        var width = Math.Min((int)(780 * scale), Math.Max(_start.W, area.W - 40));
        var height = Math.Min((int)(560 * scale), Math.Max(_start.H, area.H - 40));
        _finish = (area.X + (area.W - width) / 2,
            area.Y + (area.H - height) / 2, width, height);
    }

    /// <summary>先显示与小盒重合的快照，再向工作区中心连续展开。</summary>
    public void Show()
    {
        if (_source != null) return;
        _desktopHost = DesktopEmbedService.GetDesktopHostWindow();
        var parameters = new HwndSourceParameters("DesktopIconsStorage.LinkBasketPopup")
        {
            ParentWindow = _desktopHost,
            WindowStyle = WsPopup | WsVisible,
            ExtendedWindowStyle = WsExToolWindow,
            PositionX = _start.X,
            PositionY = _start.Y,
            Width = _start.W,
            Height = _start.H
        };
        _source = new HwndSource(parameters)
        {
            CompositionTarget = { BackgroundColor = Colors.Transparent }
        };
        _view = new LinkBasketPopupView(_tile, this, _host);
        _source.RootVisual = _view;
        _source.AddHook(WndProc);
        DesktopEmbedService.RaiseAboveDesktopIcons(Hwnd);
        ApplyTheme();
        AnimateTo(1);
    }

    public void RefreshItems() => _view?.RefreshItems();

    /// <summary>展开窗口固定 20% 背景叠色，主题颜色与应用全局设置同步。</summary>
    public void ApplyTheme()
    {
        if (_source == null || _view == null) return;
        var color = _host.ThemePalette.WindowBackground;
        var acrylic = BackdropService.Apply(Hwnd, color, 51,
            blurEnabled: true, _host.IsDarkTheme) == BackdropService.BackdropKind.Acrylic;
        _view.ApplyTheme(_host.IsDarkTheme, acrylic);
        var bounds = Interpolate(_progress);
        DesktopEmbedService.ApplyRoundedCorners(Hwnd, bounds.W, bounds.H);
    }

    public void CloseAnimated()
    {
        if (_source == null || _closing) return;
        _closing = true;
        _ready = false;
        AnimateTo(0);
    }

    private void AnimateTo(double target)
    {
        _timer?.Stop();
        if (!SystemParameters.ClientAreaAnimation)
        {
            _progress = target;
            var instant = Interpolate(target);
            DesktopEmbedService.SetBounds(Hwnd, instant.X, instant.Y,
                instant.W, instant.H);
            _view?.SetTransition(target);
            if (target == 0) Dispose();
            else
            {
                _ready = true;
                DesktopEmbedService.ActivateWindow(Hwnd);
                _view?.FocusItems();
            }
            return;
        }
        var initial = _progress;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / AnimationDurationMs);
            var eased = 1 - Math.Pow(1 - t, 3);
            _progress = initial + (target - initial) * eased;
            var bounds = Interpolate(_progress);
            if (!DesktopEmbedService.SetBoundsAnimated(Hwnd,
                    bounds.X, bounds.Y, bounds.W, bounds.H))
            {
                _timer?.Stop();
                Dispose();
                return;
            }
            _view?.SetTransition(_progress);
            if (t < 1) return;
            _timer?.Stop();
            _progress = target;
            DesktopEmbedService.ApplyRoundedCorners(Hwnd, bounds.W, bounds.H);
            if (target == 0) Dispose();
            else
            {
                _ready = true;
                DesktopEmbedService.ActivateWindow(Hwnd);
                _view?.FocusItems();
            }
        };
        _timer.Start();
    }

    private (int X, int Y, int W, int H) Interpolate(double progress)
    {
        var x = _start.X + (_finish.X - _start.X) * progress;
        var y = _start.Y + (_finish.Y - _start.Y) * progress;
        var w = _start.W + (_finish.W - _start.W) * progress;
        var h = _start.H + (_finish.H - _start.H) * progress;
        return ((int)Math.Round(x), (int)Math.Round(y),
            Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
        IntPtr lParam, ref bool handled)
    {
        if (ShellContextMenuService.MenuActive &&
            ShellContextMenuService.ForwardMessage((uint)msg, wParam, lParam))
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == 0x0046 && _desktopHost != IntPtr.Zero) // WM_WINDOWPOSCHANGING
            DesktopEmbedService.PinZOrderAboveHost(lParam, _desktopHost);
        if (msg == 0x0100 && wParam.ToInt64() == 0x1B) // WM_KEYDOWN / Esc
        {
            CloseAnimated();
            handled = true;
        }
        if (msg == 0x0006 && wParam == IntPtr.Zero && _ready &&
            !ShellContextMenuService.MenuActive) // WM_ACTIVATE / WA_INACTIVE
            _source?.Dispatcher.BeginInvoke(CloseAnimated);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        if (_source != null)
        {
            BackdropService.Clear(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
        _view = null;
        _tile.OnLinkPopupClosed(this);
    }
}
