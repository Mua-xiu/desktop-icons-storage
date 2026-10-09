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
        RuntimePaths.EnsureSandboxPath(block.FolderPath);
        Directory.CreateDirectory(block.FolderPath);
        var errors = new List<string>();
        var added = 0;
        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            try
            {
                var fullPath = Path.GetFullPath(source);
                RuntimePaths.EnsureSandboxPath(fullPath);
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
}
