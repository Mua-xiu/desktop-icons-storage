using System.IO;
using System.Runtime.InteropServices;
using DesktopOrganizer.Platform.Native;

namespace DesktopOrganizer.Platform.Services;

/// <summary>
/// SHChangeNotify 主动通知：我们用 File.Move 移动文件时 Shell 不会感知，
/// 必须显式通知，否则桌面/资源管理器会一直显示已移走的"幽灵图标"。
/// </summary>
public static class ShellNotifyService
{
    private const uint SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;

    /// <summary>一组文件/文件夹从 Src 移动到 Dest 后，通知 Shell 双侧刷新。</summary>
    public static void NotifyMoved(IEnumerable<(string Src, bool IsDir, string Dest)> items)
    {
        // 2026-09-23：精确删除/创建事件并未降低 Explorer 的可见延迟，反而会造成额外刷新。
        // 回到稳定的目录级通知，只保留正确的 PATHW 参数以避免历史 AccessViolation。
        var folders = items
            .SelectMany(item => new[]
            {
                Path.GetDirectoryName(item.Src),
                Path.GetDirectoryName(item.Dest)
            })
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
            Notify(SHCNE_UPDATEDIR, folder!);
    }

    /// <summary>强制刷新某个文件夹的视图（桌面图标消失/出现就靠它）。</summary>
    public static void NotifyFolderChanged(string folder) => Notify(SHCNE_UPDATEDIR, folder);

    private static void Notify(uint eventId, string path)
    {
        var ptr = Marshal.StringToHGlobalUni(path);
        try
        {
            NativeMethods.SHChangeNotify(eventId, SHCNF_PATHW, ptr, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
