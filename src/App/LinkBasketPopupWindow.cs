using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.App.Views;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App;

/// <summary>链接筐的独立桌面弹窗：固定在所属显示器中心，内容轻柔淡入淡出。</summary>
public sealed class LinkBasketPopupWindow : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly BlockWindow _tile;
    private readonly AppHost _host;
    private HwndSource? _source;
    private LinkBasketPopupView? _view;
    private DispatcherTimer? _completionTimer;
    private int _animationVersion;
    private IntPtr _desktopHost;
    private bool _closing;
    private bool _ready;
    private readonly (int X, int Y, int W, int H) _finish;

    public IntPtr Hwnd => _source?.Handle ?? IntPtr.Zero;
    public bool IsOpen => _source != null && !_closing;

    public LinkBasketPopupWindow(BlockWindow tile, AppHost host)
    {
        _tile = tile;
        _host = host;
        var tileBounds = (X: (int)tile.Block.X, Y: (int)tile.Block.Y,
            W: (int)tile.Block.Width, H: (int)tile.Block.Height);
        var area = DesktopEmbedService.GetNearestMonitorWorkArea(
            tileBounds.X, tileBounds.Y, tileBounds.W, tileBounds.H);
        var scale = DpiHelper.WindowScale(tile.Hwnd);
        // 大窗口增加可见图标容量，同时仍限制在所属显示器的工作区内。
        var width = Math.Min(Math.Max((int)(780 * scale), tileBounds.W),
            Math.Max(tileBounds.W, area.W - 40));
        var height = Math.Min(Math.Max((int)(560 * scale), tileBounds.H),
            Math.Max(tileBounds.H, area.H - 40));
        _finish = (area.X + (area.W - width) / 2,
            area.Y + (area.H - height) / 2, width, height);
    }

    /// <summary>2026-09-24：直接在目标位置显示弹窗，避免移动原生毛玻璃产生拖影。</summary>
    public void Show()
    {
        if (_source != null) return;
        _desktopHost = DesktopEmbedService.GetDesktopHostWindow();
        var parameters = new HwndSourceParameters("DesktopIconsStorage.LinkBasketPopup")
        {
            ParentWindow = _desktopHost,
            WindowStyle = WsPopup | WsVisible,
            ExtendedWindowStyle = WsExToolWindow,
            PositionX = _finish.X,
            PositionY = _finish.Y,
            Width = _finish.W,
            Height = _finish.H
        };
        _source = new HwndSource(parameters)
        {
            CompositionTarget = { BackgroundColor = Colors.Transparent }
        };
        _view = new LinkBasketPopupView(_tile, this, _host);
        _source.RootVisual = _view;
        _source.AddHook(WndProc);
        if (!DesktopEmbedService.ShowWithoutActivation(Hwnd))
            throw new IOException("收纳筐展开窗口未能显示。");
        DesktopEmbedService.RaiseAboveDesktopIcons(Hwnd);
        ApplyTheme();
        AnimateTo(1);
    }

    public void RefreshItems() => _view?.RefreshItems();

    /// <summary>2026-09-24：新模式弹窗使用固定深色底板和 20% 毛玻璃，与全局主题无关。</summary>
    public void ApplyTheme()
    {
        if (_source == null || _view == null) return;
        var color = Color.FromRgb(0x20, 0x20, 0x20);
        var acrylic = BackdropService.Apply(Hwnd, color, 51,
            blurEnabled: true, dark: true) == BackdropService.BackdropKind.Acrylic;
        _view.ApplyTheme(acrylic);
        DesktopEmbedService.ApplyRoundedCorners(Hwnd, _finish.W, _finish.H, 8,
            systemCorners: false);
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
        StopAnimation();
        var version = ++_animationVersion;
        if (!SystemParameters.ClientAreaAnimation || _view == null)
        {
            FinishAnimation(target, version);
            return;
        }
        var duration = _view.AnimateTransition(target == 1,
            () => FinishAnimation(target, version));
        // 被遮挡时 WPF 完成回调可能延迟，定时兜底只负责结束过渡。
        _completionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(duration + 80)
        };
        _completionTimer.Tick += (_, _) => FinishAnimation(target, version);
        _completionTimer.Start();
    }

    /// <summary>动画结束时恢复精确圆角，并交还焦点或销毁弹窗。</summary>
    private void FinishAnimation(double target, int version)
    {
        if (_source == null || version != _animationVersion) return;
        ++_animationVersion;
        StopAnimation();
        _view?.SetTransition(target);
        if (target == 0) { Dispose(); return; }
        _ready = true;
        DesktopEmbedService.ActivateWindow(Hwnd);
        _view?.FocusItems();
    }

    private void StopAnimation()
    {
        _completionTimer?.Stop();
        _completionTimer = null;
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
        else if (msg == 0x0100 && Keyboard.Modifiers == ModifierKeys.None &&
                 wParam.ToInt64() is 0x25 or 0x27 &&
                 _view?.TryMovePage(wParam.ToInt64() == 0x25 ? -1 : 1) == true)
        {
            // 方向键在弹窗内全局生效，包括页点或顶部开关当前持有焦点时。
            handled = true;
        }
        if (msg == 0x0006 && wParam == IntPtr.Zero && _ready &&
            !ShellContextMenuService.MenuActive) // WM_ACTIVATE / WA_INACTIVE
            _source?.Dispatcher.BeginInvoke(CloseAnimated);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        ++_animationVersion;
        StopAnimation();
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
