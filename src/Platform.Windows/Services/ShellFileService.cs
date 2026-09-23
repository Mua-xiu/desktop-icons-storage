using DesktopIconsStorage.Platform.Native;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>Shell 文件操作：打开、回收站删除。</summary>
public static class ShellFileService
{
    /// <summary>以系统默认方式打开（程序运行 / 文档打开 / 文件夹在资源管理器中打开）。</summary>
    public static bool Open(string path)
    {
        var info = new NativeMethods.SHELLEXECUTEINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
            fMask = 0,
            hwnd = IntPtr.Zero,
            lpVerb = null, // 默认动词
            lpFile = path,
            lpParameters = null,
            lpDirectory = null,
            nShow = NativeMethods.SW_SHOWNORMAL
        };
        return NativeMethods.ShellExecuteEx(ref info);
    }

    /// <summary>删除到回收站（FOF_ALLOWUNDO，可撤销）。返回是否成功。</summary>
    public static bool RecycleDelete(IntPtr ownerHwnd, IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return true;

        var from = string.Join('\0', list) + "\0\0"; // 双 null 结尾
        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            hwnd = ownerHwnd,
            wFunc = NativeMethods.FO_DELETE,
            pFrom = from,
            pTo = null,
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOERRORUI,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };
        var result = NativeMethods.SHFileOperation(ref op);
        return result == 0 && !op.fAnyOperationsAborted;
    }
}
