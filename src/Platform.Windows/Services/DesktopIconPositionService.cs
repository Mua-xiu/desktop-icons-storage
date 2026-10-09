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
///
/// 性能约束（2026-10-09 黑屏卡顿复盘）：禁止"每个图标 × 每次重试都全量枚举"的写法——
/// 还原高峰期 Explorer 正忙于添加图标，成百上千次跨进程消息会直接卡死桌面。
/// 所有批量操作必须：先一次性枚举整表建立 名称→索引 映射，再按映射批量处理。
/// </summary>
public static class DesktopIconPositionService
{
    private const int TextCapacity = 260;
    // 大批量还原时 Explorer 逐个把图标加回视图，间隔递增地等待，上限约 15 秒。
    private const int MaxRestoreRounds = 12;

    /// <summary>批量读取若干文件名对应的桌面图标坐标；单次全量枚举，找不到的不出现在结果里。</summary>
    public static Dictionary<string, int[]> GetIconPositions(IReadOnlyCollection<string> fileNames)
    {
        var result = new Dictionary<string, int[]>();
        if (fileNames.Count == 0) return result;
        var lv = FindDesktopListView();
        if (lv == IntPtr.Zero) return result;

        WithRemoteMemory(lv, (process, remote) =>
        {
            var indexByName = EnumerateIconIndices(lv, process, remote);
            foreach (var name in fileNames)
            {
                if (!TryFindIndex(indexByName, name, out var index)) continue;
                if (TryGetPositionByIndex(lv, process, remote, index, out var pt))
                    result[name] = new[] { pt.X, pt.Y };
            }
            return true;
        });
        return result;
    }

    /// <summary>
    /// 还原后批量归位，返回 (matched, total)。
    /// 自适应轮次：大批量还原时 Explorer 把图标逐个加回视图可能耗时很久，
    /// 只要还有未归位的项就继续等（间隔递增），直到全部归位或达到上限；
    /// 越出当前虚拟屏幕或坐标非法的项立即放弃（显示器布局可能已变化）。
    /// </summary>
    public static (int Matched, int Total) RestorePositions(IReadOnlyDictionary<string, int[]> positions)
    {
        if (positions.Count == 0) return (0, 0);
        var lv = FindDesktopListView();
        if (lv == IntPtr.Zero) return (0, positions.Count);

        var matched = 0;
        WithRemoteMemory(lv, (process, remote) =>
        {
            var pending = new Dictionary<string, int[]>(positions, StringComparer.OrdinalIgnoreCase);
            var delay = 800;
            for (var round = 0; round < MaxRestoreRounds && pending.Count > 0; round++)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay + 400, 2000);
                var indexByName = EnumerateIconIndices(lv, process, remote);
                foreach (var name in pending.Keys.ToList())
                {
                    var xy = pending[name];
                    var usable = xy is { Length: 2 } && IsInsideVirtualScreen(xy[0], xy[1]);
                    if (!usable)
                    {
                        pending.Remove(name);
                        continue;
                    }
                    if (!TryFindIndex(indexByName, name, out var index)) continue;
                    SendListViewMessage(lv, NativeMethods.LVM_SETITEMPOSITION,
                        (IntPtr)index, MakeLParam(xy[0], xy[1]));
                    pending.Remove(name);
                    matched++;
                }
            }
            return true;
        });
        return (matched, positions.Count);
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

    /// <summary>一次性全量枚举，建立 显示名→索引 映射（同名项只保留第一个）。</summary>
    private static Dictionary<string, int> EnumerateIconIndices(
        IntPtr lv, IntPtr process, IntPtr remote)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var count = (int)SendListViewMessage(lv, NativeMethods.LVM_GETITEMCOUNT,
            IntPtr.Zero, IntPtr.Zero);
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
            if (!string.IsNullOrEmpty(text)) map.TryAdd(text, i);
        }
        return map;
    }

    /// <summary>桌面可能隐藏已知扩展名，查找时先按全名、再按去扩展名匹配。</summary>
    private static bool TryFindIndex(Dictionary<string, int> indexByName,
        string fileName, out int index)
    {
        if (indexByName.TryGetValue(fileName, out index)) return true;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem != fileName && indexByName.TryGetValue(stem, out index);
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

    /// <summary>MAKELPARAM：LOWORD=x、HIWORD=y，按 16 位有符号打包（多屏坐标可为负）。</summary>
    private static IntPtr MakeLParam(int x, int y) =>
        (IntPtr)(int)((ushort)x | ((uint)(ushort)y << 16));

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
