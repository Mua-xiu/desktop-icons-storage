using DesktopIconsStorage.Platform.Native;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>
/// 桌面层嵌入：把块窗口挂到"桌面图标视图"所在的宿主窗口下，
/// 使块位于壁纸之上、应用窗口之下（永远不会盖住其他应用）。
/// 参考：Lively SetupDesktop.cs / NoFences DesktopUtil（0x052C 为未文档化消息，业界通用）。
/// </summary>
public static class DesktopEmbedService
{
    /// <summary>返回桌面图标视图（SHELLDLL_DefView）所在的宿主窗口句柄。</summary>
    public static IntPtr GetDesktopHostWindow()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);

        // 让系统在图标层之后生成 WorkerW（未文档化消息，壁纸软件同款用法）
        NativeMethods.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        // 找到直接包含 SHELLDLL_DefView 的顶层窗口（通常是 Progman，部分系统是某个 WorkerW）
        IntPtr host = IntPtr.Zero;
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                host = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return host != IntPtr.Zero ? host : progman;
    }

    /// <summary>窗口句柄是否仍然有效。</summary>
    public static bool IsWindowAlive(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);

    public static bool IsShown(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);

    /// <summary>2026-09-24：模态创建后显式显示桌面窗，避免新盒已保存却不可见。</summary>
    public static bool ShowWithoutActivation(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, 4 /* SW_SHOWNOACTIVATE */);
        return NativeMethods.IsWindowVisible(hwnd);
    }

    /// <summary>块窗口是否仍挂接在桌面宿主下（以 owner 方式挂接）。</summary>
    public static bool IsEmbedded(IntPtr hwnd, IntPtr host) =>
        hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd) &&
        NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_HWNDPARENT) == host;

    /// <summary>把顶层窗口的 owner 设为桌面宿主（NoFences 同款：窗口粘附在桌面层、随 Win+D 保留）。</summary>
    public static void EmbedAsOwned(IntPtr hwnd, IntPtr host)
    {
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_HWNDPARENT, host);
        RaiseAboveDesktopIcons(hwnd); // 重挂接后 z 序也要回到图标层之上
    }

    /// <summary>桌面图标视图（SHELLDLL_DefView）句柄：z 序插入点必须用它，
    /// 否则块会被排在图标/壁纸层之下而不可见。</summary>
    public static IntPtr GetDesktopIconView()
    {
        var host = GetDesktopHostWindow();
        var defView = NativeMethods.FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
        return defView != IntPtr.Zero ? defView : host;
    }

    /// <summary>
    /// 只移动窗口，不参与 z 序重排。拖动期间复用现有窗口层级，避免无效插入点
    /// 使整次 SetWindowPos 失败，同时避免重复创建圆角区域造成拖动卡顿。
    /// </summary>
    public static bool SetPosition(IntPtr hwnd, int screenX, int screenY) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, screenX, screenY, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    /// <summary>
    /// 以屏幕坐标设置窗口位置与尺寸，但保留创建时已经确定的桌面 z 序。
    /// 几何更新不能再混入桌面图标子窗口作为插入点，否则失败时移动、折叠和缩放会同时失效。
    /// </summary>
    public static bool SetBounds(IntPtr hwnd, int screenX, int screenY, int w, int h)
    {
        var updated = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, screenX, screenY, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        if (!updated) return false;
        ApplyRoundedCorners(hwnd, w, h);
        return true;
    }

    /// <summary>2026-09-24：动画开始时移除旧裁剪，避免窗口放大时被原尺寸区域截断。</summary>
    public static void BeginBoundsAnimation(IntPtr hwnd) =>
        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, false);

    /// <summary>2026-09-24：动画帧只更新窗口几何，圆角区域在结束时一次性恢复。</summary>
    public static bool SetBoundsAnimated(IntPtr hwnd, int screenX, int screenY, int w, int h)
        => NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, screenX, screenY, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    /// <summary>把块窗口钉在桌面图标视图正上方（创建/重挂接后调用）。</summary>
    public static void RaiseAboveDesktopIcons(IntPtr hwnd) =>
        NativeMethods.SetWindowPos(hwnd, GetDesktopIconView(), 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>
    /// 用窗口区域裁剪出圆角（半径与 RootBorder 的 8 DIP 对齐）。
    /// </summary>
    public static void ApplyRoundedCorners(IntPtr hwnd, int w, int h)
    {
        if (w <= 0 || h <= 0) return;

        // Acrylic 由 DWM 在 WPF 内容下方绘制，仅裁剪前景内容无法挡住四个方角；
        // 先声明系统圆角偏好，再用窗口区域作旧系统与无边框弹窗的可靠兜底。
        try
        {
            int preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            // DWM Acrylic 会为无边框弹窗额外绘制一圈矩形系统描边；关闭它，
            // 统一使用 BlockView 自己的圆角描边，避免四角出现亮色遮挡块。
            int borderColor = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch { /* Windows 10 等旧系统继续使用窗口区域裁剪 */ }

        var ellipse = (int)(16 * DpiHelper.WindowScale(hwnd)); // 8 DIP 半径 → 16 DIP 直径
        var rgn = NativeMethods.CreateRoundRectRgn(0, 0, w, h, ellipse, ellipse);
        // SetWindowRgn 后区域归系统所有，不需要也不能 DeleteObject
        NativeMethods.SetWindowRgn(hwnd, rgn, true);
    }

    /// <summary>NOACTIVATE 窗口需要程序化聚焦才能接收键盘输入。</summary>
    public static void FocusWindow(IntPtr hwnd) => NativeMethods.SetFocus(hwnd);

    /// <summary>
    /// 程序化激活块窗口（NOACTIVATE 仅阻止鼠标激活）。重命名/键盘操作前必须激活，
    /// 否则键盘输入不会路由到窗口。z 序由 WM_WINDOWPOSCHANGING 钉住，不会被抬高。
    /// </summary>
    public static void ActivateWindow(IntPtr hwnd)
    {
        if (!NativeMethods.SetForegroundWindow(hwnd))
            NativeMethods.SetActiveWindow(hwnd);
    }

    /// <summary>交互结束（如重命名完成）后把前台焦点还给桌面。</summary>
    public static void DeactivateToDesktop()
    {
        var host = GetDesktopHostWindow();
        if (host != IntPtr.Zero) NativeMethods.SetForegroundWindow(host);
    }

    /// <summary>
    /// WM_WINDOWPOSCHANGING 处理：把 z 序强制钉在桌面宿主正上方。
    /// 任何来源（系统激活、自身误用 HWND_TOP）想把块抬高时都会被拉回。
    /// </summary>
    public static void PinZOrderAboveHost(IntPtr lParam, IntPtr host)
    {
        if (host == IntPtr.Zero) return;
        var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
        if ((pos.flags & NativeMethods.SWP_NOZORDER) != 0) return;
        if (pos.hwndInsertAfter == host) return;
        pos.hwndInsertAfter = host;
        System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
    }

    /// <summary>虚拟桌面范围（物理像素，可跨多显示器）。</summary>
    public static (int X, int Y, int W, int H) GetVirtualScreen() =>
        (NativeMethods.GetSystemMetrics(76),  // SM_XVIRTUALSCREEN
         NativeMethods.GetSystemMetrics(77),  // SM_YVIRTUALSCREEN
         NativeMethods.GetSystemMetrics(78),  // SM_CXVIRTUALSCREEN
         NativeMethods.GetSystemMetrics(79)); // SM_CYVIRTUALSCREEN

    /// <summary>取得最接近指定窗口中心点的显示器工作区，排除任务栏以保证缩放边可操作。</summary>
    public static (int X, int Y, int W, int H) GetNearestMonitorWorkArea(int x, int y, int w, int h)
    {
        var point = new NativeMethods.POINT
        {
            X = x + Math.Max(0, w) / 2,
            Y = y + Math.Max(0, h) / 2
        };
        var monitor = NativeMethods.MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor != IntPtr.Zero)
        {
            var info = new NativeMethods.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var r = info.rcWork;
                return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
        }

        var (vx, vy, vw, vh) = GetVirtualScreen();
        return (vx, vy, vw, vh);
    }

    private static uint? _taskbarCreated;

    /// <summary>Explorer 重建任务栏时广播的消息 ID（用于触发重新嵌入）。</summary>
    public static uint TaskbarCreatedMessage =>
        _taskbarCreated ??= NativeMethods.RegisterWindowMessage("TaskbarCreated");
}
