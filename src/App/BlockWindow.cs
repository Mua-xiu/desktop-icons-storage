using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.App.Views;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Core.Services;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App;

/// <summary>
/// 一个收纳盒窗口：以子窗口形式创建在桌面宿主（Progman/WorkerW）下，
/// 位于壁纸之上、应用窗口之下。Explorer 重启后由看门狗触发重建。
/// </summary>
public class BlockWindow : IDisposable
{
    private const int TitleBarHeightDip = 40;

    /// <summary>标题栏实际高度（物理像素，随 DPI 缩放；XAML 侧 40 DIP 自动同步）。</summary>
    private static int TitleBarHeight => (int)(TitleBarHeightDip * DpiHelper.SystemScale);

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly AppHost _host;
    private HwndSource? _source;
    private BlockView? _view;
    private LinkBasketTileView? _linkView;
    private LinkBasketPopupWindow? _linkPopup;
    private FolderWatchService? _watcher;
    private volatile bool _needsRecreate;
    private long _suppressWatcherUntilUtcTicks;
    private IntPtr _hostHwnd; // 桌面宿主（Progman），z 序钉死在它正上方

    public Block Block { get; }
    public IntPtr Hwnd => _source?.Handle ?? IntPtr.Zero;

    public BlockWindow(AppHost host, Block block)
    {
        _host = host;
        Block = block;
    }

    public void Show()
    {
        CreateSource();
        WatchFolder();
    }

    private void CreateSource()
    {
        _hostHwnd = DesktopEmbedService.GetDesktopHostWindow();
        var height = Block.IsLink ? (int)Block.Height
            : Block.Collapsed ? TitleBarHeight : (int)Block.Height;
        _actualHeight = height;

        // NoFences 同款模型：顶层 WS_POPUP 窗口 + owner = Progman。
        // owner 使窗口始终位于桌面之上、其他应用窗口之下；NOACTIVATE 防止点击抢前台；
        // 顶层窗口可以使用 DWM 毛玻璃（子窗口不行，这是放弃 SetParent 方案的原因）。
        var parms = new HwndSourceParameters("DesktopIconsStorage.Block")
        {
            ParentWindow = _hostHwnd, // 顶层窗口创建时此参数为 owner
            WindowStyle = WS_POPUP | WS_VISIBLE,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            PositionX = (int)Block.X,
            PositionY = (int)Block.Y,
            Width = (int)Block.Width,
            Height = height
        };

        _source = new HwndSource(parms)
        {
            // 透明像素透出 DWM 毛玻璃
            CompositionTarget = { BackgroundColor = Colors.Transparent }
        };
        if (Block.IsLink)
        {
            _linkView = new LinkBasketTileView(this, _host);
            _source.RootVisual = _linkView;
        }
        else
        {
            _view = new BlockView(this, _host);
            _source.RootVisual = _view;
        }
        _source.AddHook(WndProc);

        // HwndSource 首次创建不会经过 SetBounds；必须立即设置窗口区域，
        // 否则毛玻璃底层会从 WPF Border 的四个圆角漏出。
        DesktopEmbedService.ApplyRoundedCorners(_source.Handle, (int)Block.Width, height);

        // 创建后立刻钉到桌面图标视图正上方（否则默认在 z 序顶部，会盖住应用窗口）
        DesktopEmbedService.RaiseAboveDesktopIcons(_source.Handle);

        ApplyBackdrop();
        _view?.OnCollapsedChanged(Block.Collapsed);
        _view?.RefreshItems();
        _linkView?.RefreshItems();
    }

    /// <summary>应用 DWM 亚克力 + 圆角裁剪（半径已修正，与 RootBorder 对齐）。</summary>
    public void ApplyBackdrop()
    {
        if (_source == null) return;
        if (Block.IsLink)
        {
            // 预览小盒固定 10% 磨砂叠色，不读取全局背景不透明度滑块。
            var tint = Color.FromRgb(0x70, 0x70, 0x70);
            var acrylic = BackdropService.Apply(Hwnd, tint, 26,
                blurEnabled: true, dark: true) ==
                BackdropService.BackdropKind.Acrylic;
            _linkView?.ApplyTheme(acrylic);
            _linkPopup?.ApplyTheme();
            DesktopEmbedService.ApplyRoundedCorners(Hwnd,
                (int)Block.Width, (int)Block.Height);
            return;
        }
        if (_view == null) return;
        var effectiveOpacity = EffectiveBackdropOpacity();
        var alpha = (byte)Math.Clamp((int)(effectiveOpacity * 255), 40, 255);
        var palette = _host.ThemePalette;
        var kind = DesktopIconsStorage.Platform.Services.BackdropService.Apply(
            Hwnd, palette.WindowBackground, alpha, _host.Settings.BlurEnabled, _host.IsDarkTheme);
        var liveAcrylic = kind == DesktopIconsStorage.Platform.Services.BackdropService.BackdropKind.Acrylic;

        // 2026-09-23：停用壁纸截图模拟，透明与模糊全部交给 DWM 实时合成。
        _view.SetFrostImage(null);
        _view.SetFrostTint(palette.WindowBackground,
            !_host.Settings.BlurEnabled ? 1.0 : liveAcrylic ? 0.0 : effectiveOpacity);
        _view.ApplyTheme(_host.IsDarkTheme, _host.AccentColor, palette, alpha,
            liveAcrylic);

        // DWM Acrylic 会在窗口区域之后建立底层材质；最后重新施加圆角，
        // 防止透明的 WPF 四角露出矩形 Acrylic 背景。
        var actualHeight = Block.Collapsed ? TitleBarHeight : _actualHeight;
        DesktopEmbedService.ApplyRoundedCorners(Hwnd, (int)Block.Width, actualHeight);

    }

    /// <summary>
    /// 保留原调用入口供移动/缩放完成事件使用；实时 Acrylic 无需重新截取桌面背景。
    /// </summary>
    public void RefreshFrostBackground()
    {
        if (_view == null) return;
        _view.SetFrostImage(null);
    }

    /// <summary>
    /// 浅色材质比深色更容易被白色 tint 覆盖，因此使用保留 0%/100% 端点的柔和曲线。
    /// 例如设置为 55% 时浅色实际约为 43%，既更通透又不改变用户可调范围。
    /// </summary>
    private double EffectiveBackdropOpacity()
    {
        var opacity = Math.Clamp(_host.Settings.BackdropOpacity, 0.0, 1.0);
        return _host.IsDarkTheme ? opacity : opacity * (0.5 + 0.5 * opacity);
    }

    /// <summary>实时 DWM 材质无需监听壁纸文件变化，保留入口兼容现有看门狗。</summary>
    public void CheckWallpaperChanged() { }

    private void WatchFolder()
    {
        _watcher?.Dispose();
        try
        {
            _watcher = new FolderWatchService(Block.FolderPath);
            _watcher.Changed += () =>
            {
                // 应用内部移动已经主动刷新过列表，跳过随后到达的监听回调，避免图标整组闪烁。
                if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _suppressWatcherUntilUtcTicks)) return;
                _source?.Dispatcher.BeginInvoke(() =>
                {
                    _view?.RefreshItems();
                    _linkView?.RefreshItems();
                    _linkPopup?.RefreshItems();
                });
            };
        }
        catch { /* 文件夹暂不可用时忽略，由用户操作触发刷新 */ }
    }

    public void OnFolderRenamed() => WatchFolder();

    public void RefreshView()
    {
        try { _source?.Dispatcher.Invoke(() =>
        {
            _view?.RefreshItems();
            _linkView?.RefreshItems();
            _linkPopup?.RefreshItems();
        }); } catch { }
    }

    /// <summary>内部文件操作后立即刷新一次，并暂时抑制 FileSystemWatcher 的重复刷新。</summary>
    public void RefreshViewAfterInternalChange()
    {
        Interlocked.Exchange(ref _suppressWatcherUntilUtcTicks, DateTime.UtcNow.AddSeconds(1).Ticks);
        RefreshView();
    }

    // ---------- 几何 ----------

    /// <summary>移动（屏幕坐标），不改变展开高度。</summary>
    public void SetPositionScreen(int x, int y)
    {
        if (_source == null) return;
        var h = Block.Collapsed ? TitleBarHeight : (int)Block.Height;
        if (!DesktopEmbedService.SetPosition(Hwnd, x, y))
        {
            Helpers.AppHost.Log($"SetPositionScreen FAILED: hwnd={Hwnd}, x={x}, y={y}");
            return;
        }
        _actualHeight = h;
        Block.X = x;
        Block.Y = y;
    }

    /// <summary>调整尺寸（h 为展开高度，折叠时实际窗口高度保持标题栏高）。</summary>
    public void SetSizeScreen(int w, int h)
    {
        if (_source == null) return;
        var actual = Block.Collapsed ? TitleBarHeight : h;
        if (!DesktopEmbedService.SetBounds(Hwnd, (int)Block.X, (int)Block.Y, w, actual))
        {
            Helpers.AppHost.Log($"SetSizeScreen FAILED: hwnd={Hwnd}, w={w}, h={actual}");
            return;
        }
        Block.Width = w;
        Block.Height = h;
        _actualHeight = actual;
    }

    /// <summary>同时设置位置与尺寸（八向拉伸用；h 为展开高度）。</summary>
    public void SetBoundsScreen(int x, int y, int w, int h)
    {
        if (_source == null) return;
        var actual = Block.Collapsed ? TitleBarHeight : h;
        if (!DesktopEmbedService.SetBounds(Hwnd, x, y, w, actual))
        {
            Helpers.AppHost.Log($"SetBoundsScreen FAILED: hwnd={Hwnd}, x={x}, y={y}, w={w}, h={actual}");
            return;
        }
        Block.X = x;
        Block.Y = y;
        Block.Width = w;
        Block.Height = h;
        _actualHeight = actual;
    }

    public void SetCollapsed(bool collapsed)
    {
        if (Block.IsLink) return;
        Block.Collapsed = collapsed;
        // 滚动条已隐藏，无需处理溢出闪烁；直接动画到目标高度
        AnimateHeightTo(collapsed ? TitleBarHeight : (int)Block.Height);
        _view?.OnCollapsedChanged(collapsed);
        _host.PersistLayout();
    }

    /// <summary>链接筐点击时打开独立居中窗口，重复点击不创建第二个弹窗。</summary>
    public void OpenLinkPopup()
    {
        if (!Block.IsLink || _linkPopup != null) return;
        _linkPopup = new LinkBasketPopupWindow(this, _host);
        try { _linkPopup.Show(); }
        catch (Exception ex)
        {
            Helpers.AppHost.LogError("OpenLinkPopup", ex);
            _linkPopup.Dispose();
            _linkPopup = null;
            _host.NotifyError("打开收纳筐失败，请查看错误日志。");
        }
    }

    public void OnLinkPopupClosed(LinkBasketPopupWindow popup)
    {
        if (_linkPopup == popup) _linkPopup = null;
    }

    public System.Windows.Media.Imaging.BitmapSource? CapturePreview() =>
        _linkView?.CapturePreview();

    /// <summary>按行列重新计算小盒尺寸；只能由右键规格菜单调用。</summary>
    public void SetLinkPreviewSize(int rows, int columns)
    {
        if (!Block.IsLink) return;
        Block.PreviewRows = rows;
        Block.PreviewColumns = columns;
        var scale = DpiHelper.SystemScale;
        SetSizeScreen((int)((columns * 52 + 24) * scale),
            (int)((rows * 52 + 55) * scale));
        _linkView?.RefreshItems();
        _host.PersistLayout();
    }

    // ---------- 折叠/展开动画（167ms easeOutQuad，Fluent 动效基准） ----------

    private DispatcherTimer? _animTimer;
    private int _actualHeight;

    private void AnimateHeightTo(int target)
    {
        if (_source == null) return;
        var start = _actualHeight > 0 ? _actualHeight : target;
        var delta = target - start;
        if (delta == 0) return;

        _animTimer?.Stop();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int durationMs = 167;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _animTimer = timer;
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)durationMs);
            var eased = 1 - (1 - t) * (1 - t); // easeOutQuad
            var h = (int)(start + delta * eased);
            if (!DesktopEmbedService.SetBounds(Hwnd, (int)Block.X, (int)Block.Y, (int)Block.Width, h))
            {
                timer.Stop();
                Helpers.AppHost.Log($"AnimateHeightTo FAILED: hwnd={Hwnd}, h={h}");
                return;
            }
            _actualHeight = h;
            if (t >= 1.0) timer.Stop();
        };
        timer.Start();
    }

    // ---------- 嵌入存活检测与重建 ----------

    public void ReembedIfNeeded()
    {
        var host = DesktopEmbedService.GetDesktopHostWindow();
        if (_needsRecreate || !DesktopEmbedService.IsEmbedded(Hwnd, host))
        {
            _needsRecreate = false;
            if (_source == null || !DesktopEmbedService.IsWindowAlive(Hwnd))
            {
                Helpers.AppHost.Log("ReembedIfNeeded: window dead, recreating");
                Recreate();
            }
            else
            {
                Helpers.AppHost.Log("ReembedIfNeeded: re-owning");
                _hostHwnd = host;
                DesktopEmbedService.EmbedAsOwned(Hwnd, host);
            }
        }
    }

    private void Recreate()
    {
        DisposeSource();
        try { CreateSource(); }
        catch (Exception ex)
        {
            Helpers.AppHost.Log("Recreate FAILED: " + ex);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (ShellContextMenuService.MenuActive &&
            ShellContextMenuService.ForwardMessage((uint)msg, wParam, lParam))
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == DesktopEmbedService.TaskbarCreatedMessage)
        {
            _needsRecreate = true; // Explorer 重启 → 由看门狗统一重建
        }
        else if (msg == 0x0046 /* WM_WINDOWPOSCHANGING */ && _hostHwnd != IntPtr.Zero)
        {
            // z 序钉死：任何来源（系统/激活/自身误用）想把块抬离桌面宿主正上方时强制拉回
            DesktopEmbedService.PinZOrderAboveHost(lParam, _hostHwnd);
        }
        return IntPtr.Zero;
    }

    private void DisposeSource()
    {
        _linkPopup?.Dispose();
        _linkPopup = null;
        if (_source != null)
        {
            BackdropService.Clear(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
            _view = null;
            _linkView = null;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        DisposeSource();
    }
}
