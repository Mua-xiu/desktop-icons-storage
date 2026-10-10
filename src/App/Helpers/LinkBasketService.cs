using System.IO;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Core.Services;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>链接筐统一输入入口：拖放与粘贴都只创建或复制快捷方式。</summary>
public static class LinkBasketService
{
    public static (int Added, IReadOnlyList<string> Errors) Add(Block block,
        IEnumerable<string> sourcePaths)
    {
        if (!block.IsLink) throw new IOException("目标不是快捷方式收纳筐。");
        Directory.CreateDirectory(block.FolderPath);
        var errors = new List<string>();
        var added = 0;
        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            try
            {
                var fullPath = Path.GetFullPath(source);
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    throw new IOException("源项目不存在。");
                if (IsInOrContainsBlock(fullPath, block.FolderPath))
                    throw new IOException("不能引用收纳筐自身目录或其上级目录。");
                if (string.Equals(Path.GetDirectoryName(fullPath), block.FolderPath,
                    StringComparison.OrdinalIgnoreCase)) continue;

                var existingShortcut = File.Exists(fullPath) &&
                    Path.GetExtension(fullPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                var name = existingShortcut
                    ? Path.GetFileName(fullPath)
                    : Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)) + ".lnk";
                var destination = BlockManager.UniqueDestination(block.FolderPath, name);
                if (existingShortcut) File.Copy(fullPath, destination);
                else ShellLinkService.Create(fullPath, destination);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(source)}：{ex.Message}");
            }
        }
        return (added, errors);
    }

    private static bool IsInOrContainsBlock(string source, string blockFolder)
    {
        var current = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        var block = Path.GetFullPath(blockFolder).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(current, block, StringComparison.OrdinalIgnoreCase) ||
               block.StartsWith(current + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 体检单个快捷方式（2026-10-09 路线图第 6 项）。
    /// 判定原则：网络盘离线、盘符不在、Shell 虚拟目标和无法解析一律标 Unreachable（不判死），
    /// 只有"盘在、路径不在"的本地目标才算 Missing；File.Exists == false 不是永久失效的唯一依据。
    /// </summary>
    public static LinkHealth EvaluateHealth(string linkPath)
    {
        string? target;
        try { target = ShellLinkService.ReadTarget(linkPath); }
        catch { return LinkHealth.Unreachable; }
        if (string.IsNullOrWhiteSpace(target)) return LinkHealth.Unreachable;
        if (File.Exists(target) || Directory.Exists(target)) return LinkHealth.Ok;
        if (target.StartsWith(@"\\", StringComparison.Ordinal)) return LinkHealth.Unreachable;
        var root = Path.GetPathRoot(target);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return LinkHealth.Unreachable;
        return LinkHealth.Missing;
    }
}

/// <summary>快捷方式体检结果：Missing = 本地目标已删除（可清理）；Unreachable = 暂时不可达（不判死）。</summary>
public enum LinkHealth
{
    Ok,
    Missing,
    Unreachable,
}
