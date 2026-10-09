using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using DesktopIconsStorage.Platform.Native;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>
/// 桌面图标坐标读写（2026-10-09 路线图第 4 项拍板的窄例外：仅 LVM_GET/SETITEMPOSITION，
/// 不注入代码、不接管绘制）。跨进程内存只用于传 LVITEM/POINT 结构。
/// 失败形态良性：读不到就不记录，写不进就由 Explorer 自动排布，不涉及任何用户文件。
/// </summary>
public static class DesktopIconPositionService
{
    private const int TextCapacity = 260;
    private const int SetAttempts = 12;       // 还原后等待 Explorer 把图标加进 ListView
    private const int AttemptDelayMs = 200;

    /// <summary>按文件名读取桌面图标当前坐标（ListView 视图坐标）。</summary>
    public static bool TryGetIconPosition(string fileName, out (int X, int Y) position)
    {
        var lv = FindDesktopListView();
        if (lv == IntPtr.Zero)
        {
            position = default;
            return false;
        }

        // out 参数不能进 lambda，用局部变量中转。
        (int X, int Y) found = default;
        var ok = WithRemoteMemory(lv, (process, remote) =>
        {
            var index = FindItemIndex(lv, process, remote, fileName);
            return index >= 0 && TryGetPositionByIndex(lv, process, remote, index, out found);
        });
        position = found;
        return ok;
    }

    /// <summary>
    /// 还原后批量归位。每项都短重试等待 Explorer 把图标加进 ListView；
    /// 找不到或越出当前虚拟屏幕的项直接跳过（显示器布局可能已变化）。
    /// </summary>
    public static void RestorePositions(IReadOnlyDictionary<string, int[]> positions)
    {
        if (positions.Count == 0) return;
        var lv = FindDesktopListView();
        if (lv == IntPtr.Zero) return;

        WithRemoteMemory(lv, (process, remote) =>
        {
            foreach (var (name, xy) in positions)
            {
                if (xy is not { Length: 2 }) continue;
                if (!IsInsideVirtualScreen(xy[0], xy[1])) continue;
                for (var attempt = 0; attempt < SetAttempts; attempt++)
                {
                    var index = FindItemIndex(lv, process, remote, name);
                    if (index >= 0)
                    {
                        // LVM_SETITEMPOSITION 的坐标打包在 lParam 里，不需要远端内存。
                        SendListViewMessage(lv, NativeMethods.LVM_SETITEMPOSITION,
                            (IntPtr)index, (IntPtr)((xy[1] << 16) | (xy[0] & 0xFFFF)));
                        break;
                    }
                    Thread.Sleep(AttemptDelayMs);
                }
            }
            return true;
        });
    }

    // ---------- 内部 ----------

    /// <summary>桌面图标 ListView（SysListView32）；找不到返回 Zero。</summary>
    private static IntPtr FindDesktopListView()
    {
        var host = DesktopEmbedService.GetDesktopHostWindow();
        if (host == IntPtr.Zero) return IntPtr.Zero;
        var defView = NativeMethods.FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
        return defView == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
    }

    /// <summary>打开 Explorer 进程并分配一块远端内存，执行后自动释放。</summary>
    private static bool WithRemoteMemory(IntPtr listView,
        Func<IntPtr, IntPtr, bool> action)
    {
        NativeMethods.GetWindowThreadProcessId(listView, out var pid);
        if (pid == 0) return false;
        var process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ |
            NativeMethods.PROCESS_VM_WRITE, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            var remote = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)1024,
                NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
            if (remote == IntPtr.Zero) return false;
            try { return action(process, remote); }
            finally
            {
                NativeMethods.VirtualFreeEx(process, remote, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    // 远端内存布局：[0, lvitemSize) LVITEM；[lvitemSize, +520) 文本；[+520, +8) POINT。
    private static int LvItemSize => Marshal.SizeOf<NativeMethods.LVITEM>();
    private static IntPtr RemoteText(IntPtr remote) => remote + LvItemSize;
    private static IntPtr RemotePoint(IntPtr remote) => remote + LvItemSize + TextCapacity * 2;

    /// <summary>按显示名找图标索引；桌面可能隐藏已知扩展名，因此同时匹配去扩展名形式。</summary>
    private static int FindItemIndex(IntPtr lv, IntPtr process, IntPtr remote, string fileName)
    {
        var count = (int)SendListViewMessage(lv, NativeMethods.LVM_GETITEMCOUNT,
            IntPtr.Zero, IntPtr.Zero);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var i = 0; i < count; i++)
        {
            var item = new NativeMethods.LVITEM
            {
                mask = NativeMethods.LVIF_TEXT,
                iItem = i,
                iSubItem = 0,
                pszText = RemoteText(remote),
                cchTextMax = TextCapacity
            };
            if (!WriteStruct(process, remote, item)) continue;
            SendListViewMessage(lv, NativeMethods.LVM_GETITEMTEXTW, (IntPtr)i, remote);
            var text = ReadRemoteString(process, RemoteText(remote));
            if (string.Equals(text, fileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, stem, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static bool TryGetPositionByIndex(IntPtr lv, IntPtr process, IntPtr remote,
        int index, out (int X, int Y) position)
    {
        position = default;
        var result = SendListViewMessage(lv, NativeMethods.LVM_GETITEMPOSITION,
            (IntPtr)index, RemotePoint(remote));
        if (result == IntPtr.Zero) return false;
        var buffer = ReadRemoteBytes(process, RemotePoint(remote), 8);
        if (buffer == null) return false;
        position = (BitConverter.ToInt32(buffer, 0), BitConverter.ToInt32(buffer, 4));
        return true;
    }

    private static IntPtr SendListViewMessage(IntPtr lv, uint msg, IntPtr wParam, IntPtr lParam)
    {
        NativeMethods.SendMessageTimeout(lv, msg, wParam, lParam, 0, 1000, out var result);
        return result;
    }

    private static bool WriteStruct<T>(IntPtr process, IntPtr dest, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];
        var handle = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, handle, false);
            Marshal.Copy(handle, buffer, 0, size);
        }
        finally { Marshal.FreeHGlobal(handle); }
        return NativeMethods.WriteProcessMemory(process, dest, buffer, (UIntPtr)buffer.Length, out _);
    }

    private static string? ReadRemoteString(IntPtr process, IntPtr address)
    {
        var buffer = ReadRemoteBytes(process, address, TextCapacity * 2);
        if (buffer == null) return null;
        var text = Encoding.Unicode.GetString(buffer);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    private static byte[]? ReadRemoteBytes(IntPtr process, IntPtr address, int count)
    {
        var buffer = new byte[count];
        return NativeMethods.ReadProcessMemory(process, address, buffer, (UIntPtr)count, out _)
            ? buffer
            : null;
    }

    /// <summary>显示器布局变化后，旧坐标可能落在虚拟屏幕外；这类项不再主动归位。</summary>
    private static bool IsInsideVirtualScreen(int x, int y)
    {
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        var vx = NativeMethods.GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = NativeMethods.GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = NativeMethods.GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = NativeMethods.GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return x >= vx && y >= vy && x < vx + vw && y < vy + vh;
    }
}
