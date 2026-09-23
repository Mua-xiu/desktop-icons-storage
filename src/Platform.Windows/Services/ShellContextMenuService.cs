using System.Runtime.InteropServices;
using DesktopOrganizer.Platform.Native;

namespace DesktopOrganizer.Platform.Services;

/// <summary>
/// 完整系统右键菜单托管（IContextMenu/2/3），与资源管理器一致，含第三方外壳扩展。
/// 菜单弹出期间需要宿主窗口把 WM_INITMENUPOPUP/WM_DRAWITEM/WM_MEASUREITEM/WM_MENUCHAR
/// 转发给 <see cref="ForwardMessage"/>（Old New Thing "How to host an IContextMenu" 同款流程）。
/// </summary>
public static class ShellContextMenuService
{
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    public static bool MenuActive => _menu2 != null || _menu3 != null;

    /// <summary>宿主窗口 WndProc 转发入口。返回 true 表示消息已被菜单处理。</summary>
    public static bool ForwardMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == ShellMenuMessages.WM_MENUCHAR && _menu3 != null)
                return _menu3.HandleMenuMsg2(msg, wParam, lParam, IntPtr.Zero) == 0;

            if (msg is ShellMenuMessages.WM_INITMENUPOPUP or ShellMenuMessages.WM_DRAWITEM or ShellMenuMessages.WM_MEASUREITEM)
            {
                if (_menu3 != null) return _menu3.HandleMenuMsg2(msg, wParam, lParam, IntPtr.Zero) == 0;
                if (_menu2 != null) { _menu2.HandleMenuMsg(msg, wParam, lParam); return true; }
            }
        }
        catch { /* 菜单扩展异常不应导致崩溃 */ }
        return false;
    }

    /// <summary>为一组同文件夹路径弹出系统右键菜单并执行所选命令。</summary>
    public static void Show(IntPtr ownerHwnd, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        if (paths.Count == 0) return;

        var fullPidls = new List<IntPtr>();
        var relPidls = new List<IntPtr>();
        object? parentObj = null;
        object? ctxObj = null;
        IntPtr hMenu = IntPtr.Zero;

        try
        {
            var iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
            IShellFolder? parentFolder = null;

            foreach (var path in paths)
            {
                NativeMethods.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
                fullPidls.Add(pidl);

                NativeMethods.SHBindToParent(pidl, iidFolder, out var parent, out var relPidl);
                if (parentFolder == null)
                {
                    parentFolder = (IShellFolder)parent;
                    parentObj = parent;
                }
                else
                {
                    Marshal.ReleaseComObject(parent); // ppidlLast 指向完整 PIDL 内部，不能单独释放
                }
                relPidls.Add(relPidl);
            }

            var iidCtx = new Guid("000214e4-0000-0000-c000-000000000046");
            parentFolder!.GetUIObjectOf(ownerHwnd, (uint)relPidls.Count, relPidls.ToArray(),
                iidCtx, IntPtr.Zero, out ctxObj);
            var cm = (IContextMenu)ctxObj;
            _menu2 = ctxObj as IContextMenu2;
            _menu3 = ctxObj as IContextMenu3;

            hMenu = NativeMethods.CreatePopupMenu();
            cm.QueryContextMenu(hMenu, 0, 1, 0x7FFF, ShellMenuMessages.CMF_NORMAL);

            int cmd = NativeMethods.TrackPopupMenuEx(
                hMenu, NativeMethods.TPM_RETURNCMD, screenX, screenY, ownerHwnd, IntPtr.Zero);

            if (cmd > 0)
            {
                var ici = new NativeMethods.CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<NativeMethods.CMINVOKECOMMANDINFOEX>(),
                    fMask = ShellMenuMessages.CMIC_MASK_UNICODE,
                    hwnd = ownerHwnd,
                    lpVerb = (IntPtr)(cmd - 1),
                    lpVerbW = (IntPtr)(cmd - 1),
                    nShow = NativeMethods.SW_SHOWNORMAL
                };
                var ptr = Marshal.AllocHGlobal(ici.cbSize);
                try
                {
                    Marshal.StructureToPtr(ici, ptr, false);
                    cm.InvokeCommand(ptr);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }
        catch
        {
            // 菜单弹出失败（如文件刚好被删）静默处理
        }
        finally
        {
            if (hMenu != IntPtr.Zero) NativeMethods.DestroyMenu(hMenu);
            _menu2 = null;
            _menu3 = null;
            if (ctxObj != null) Marshal.ReleaseComObject(ctxObj);
            if (parentObj != null) Marshal.ReleaseComObject(parentObj);
            foreach (var pidl in fullPidls) NativeMethods.CoTaskMemFree(pidl);
        }
    }
}
