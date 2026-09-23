using System.Runtime.InteropServices;
using System.Windows.Media;
using DesktopOrganizer.Platform.Native;

namespace DesktopOrganizer.Platform.Services;

/// <summary>
/// 毛玻璃背景。降级链：
/// 可调透明度 Acrylic → Win11 官方 DWM backdrop → 调用方退化为纯色。
/// </summary>
public static class BackdropService
{
    public enum BackdropKind { None, Solid, Acrylic }

    /// <summary>
    /// 尝试为窗口应用毛玻璃。返回实际生效的材质；未生效时调用方应画半透明纯色。
    /// </summary>
    /// <param name="tint">材质底色（使用统一主题色板中的窗口背景色）。</param>
    /// <param name="alpha">tint 的不透明度。</param>
    /// <param name="dark">深色模式（控制 DWM 材质的明暗变体）。</param>
    public static BackdropKind Apply(IntPtr hwnd, Color tint, byte alpha, bool blurEnabled, bool dark)
    {
        // 先设定明暗：Desktop Acrylic 默认是浅色变体，深色主题必须显式打开 dark mode
        ApplyWindowTheme(hwnd, dark);

        // 每次重设材质前先撤销旧策略和历史全客户区 Frame，防止白色矩形边框残留。
        Clear(hwnd);
        ResetExtendedFrame(hwnd);

        if (!blurEnabled)
        {
            return BackdropKind.Solid;
        }

        // 使用实时 DWM Acrylic，不再截取桌面壁纸。AccentFlags 保持为 0，
        // 避免无边框窗口被系统额外绘制矩形边框。
        if (TryAccentAcrylic(hwnd, tint, alpha)) return BackdropKind.Acrylic;
        if (TryDwmBackdrop(hwnd)) return BackdropKind.Acrylic;
        return BackdropKind.None;
    }

    /// <summary>同步标准窗口标题栏的深浅模式，用于设置窗口与收纳盒保持一致。</summary>
    public static void ApplyWindowTheme(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int useDark = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>
    /// 为自绘壁纸毛玻璃准备透明宿主，不启用会产生矩形系统描边的原生 Acrylic。
    /// </summary>
    public static void PrepareCustomBackdrop(IntPtr hwnd, bool dark)
    {
        ApplyWindowTheme(hwnd, dark);
        Clear(hwnd);
        try
        {
            // 2026-09-22：撤销历史 DwmExtendFrame(-1)，避免 Acrylic 的白色非客户区边框残留。
            var margins = new NativeMethods.MARGINS();
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>撤销 DwmExtendFrameIntoClientArea(-1)，避免实时 Acrylic 周围出现白色非客户区。</summary>
    private static void ResetExtendedFrame(IntPtr hwnd)
    {
        try
        {
            var margins = new NativeMethods.MARGINS();
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>Win11 22621+ 官方材质：DWMSBT_TRANSIENTWINDOW = Desktop Acrylic。</summary>
    private static bool TryDwmBackdrop(IntPtr hwnd)
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22621) return false;
            int type = NativeMethods.DWMSBT_TRANSIENTWINDOW;
            var hr = NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
            return hr == 0 && type != NativeMethods.DWMSBT_NONE;
        }
        catch { return false; }
    }

    /// <summary>未文档化 SetWindowCompositionAttribute（Win10 起社区通用，NoFences 同款）。</summary>
    private static bool TryAccentAcrylic(IntPtr hwnd, Color tint, byte alpha)
    {
        try
        {
            var policy = new NativeMethods.ACCENT_POLICY
            {
                AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                AccentFlags = 0,
                GradientColor = (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R
            };
            var size = Marshal.SizeOf(policy);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = size
                };
                return NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { return false; }
    }

    /// <summary>清除材质（用于主题切换或关闭模糊时）。</summary>
    public static void Clear(IntPtr hwnd)
    {
        try
        {
            var policy = new NativeMethods.ACCENT_POLICY { AccentState = 0 };
            var size = Marshal.SizeOf(policy);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = 19,
                    Data = ptr,
                    SizeOfData = size
                };
                NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }

            if (Environment.OSVersion.Version.Build >= 22621)
            {
                int none = NativeMethods.DWMSBT_NONE;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            }
        }
        catch { /* 清理失败无碍 */ }
    }
}
