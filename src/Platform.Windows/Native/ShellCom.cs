using System.Runtime.InteropServices;
using static DesktopIconsStorage.Platform.Native.NativeMethods;

namespace DesktopIconsStorage.Platform.Native;

// ---- Shell COM 接口（按 vtable 顺序完整声明，缺失方法会导致调用错位）----

/// <summary>IShellItemImageFactory：高质量图标/缩略图提取。</summary>
[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    void GetImage(SIZE size, uint flags, out IntPtr phbm);
}

internal static class SIIGBF
{
    internal const uint RESIZETOFIT = 0x00;
    internal const uint BIGGERSIZEOK = 0x01;
    internal const uint MEMORYONLY = 0x02;
    internal const uint ICONONLY = 0x04;
    internal const uint THUMBNAILONLY = 0x08;
    internal const uint INCACHEONLY = 0x10;
}

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    void ParseDisplayName(IntPtr hwnd, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

    void EnumObjects(IntPtr hwnd, uint grfFlags, [MarshalAs(UnmanagedType.Interface)] out object ppenumIDList);

    void BindToObject(IntPtr pidl, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void BindToStorage(IntPtr pidl, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [PreserveSig]
    int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

    void CreateViewObject(IntPtr hwndOwner,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

    void GetUIObjectOf(IntPtr hwndOwner, uint cidl,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr rgfReserved,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

    void SetNameOf(IntPtr hwnd, IntPtr pidl,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    void QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    void InvokeCommand(IntPtr pici);
    void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
}

[ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    void QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    void InvokeCommand(IntPtr pici);
    void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    void HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
}

[ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    void QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    void InvokeCommand(IntPtr pici);
    void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    void HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    [PreserveSig]
    int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr plResult);
}

internal static class ShellMenuMessages
{
    internal const uint WM_INITMENUPOPUP = 0x0117;
    internal const uint WM_DRAWITEM = 0x002B;
    internal const uint WM_MEASUREITEM = 0x002C;
    internal const uint WM_MENUCHAR = 0x0120;
    internal const uint CMF_NORMAL = 0x00000000;
    internal const uint CMF_EXTENDEDVERBS = 0x00000100;
    internal const uint CMIC_MASK_UNICODE = 0x00004000;
}
