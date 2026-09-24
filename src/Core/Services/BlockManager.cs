using DesktopIconsStorage.Core.Models;

namespace DesktopIconsStorage.Core.Services;

/// <summary>
/// 块的生命周期与文件移动（收纳 = 真实移动，不是复制也不是删除）。
/// 全部文件操作失败时抛出 IOException，由调用方决定如何提示。
/// </summary>
public class BlockManager
{
    private readonly AppSettings _settings;
    private readonly List<Block> _blocks = new();

    public BlockManager(AppSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<Block> Blocks => _blocks;

    public string DesktopPath => RuntimePaths.DesktopPath;

    // ---------- 持久化 ----------

    public void Load()
    {
        _blocks.Clear();
        _blocks.AddRange(JsonStore.Load<List<Block>>(JsonStore.LayoutPath));
        // 测试布局即便被手工改坏，也不能指向沙盒之外的真实桌面目录。
        foreach (var block in _blocks)
        {
            if (!BlockModes.IsValid(block.Mode))
                throw new IOException($"收纳盒“{block.Name}”使用未知模式，请检查布局配置。");
            if (block.IsLink) EnsureManagedFolder(block);
            RuntimePaths.EnsureSandboxPath(block.FolderPath);
        }
    }

    public void Save() => JsonStore.Save(JsonStore.LayoutPath, _blocks.ToList());

    // ---------- 块 CRUD ----------

    public Block CreateBlock(double x, double y, string? name = null, double dpiScale = 1.0,
        string mode = BlockModes.Move,
        int previewRows = PreviewGridLimits.Default,
        int previewColumns = PreviewGridLimits.Default)
    {
        if (!BlockModes.IsValid(mode)) throw new IOException("未知的收纳盒模式。");
        if (!PreviewGridLimits.IsValid(previewRows) ||
            !PreviewGridLimits.IsValid(previewColumns))
            throw new IOException("预览行列必须在 2 到 10 之间。");
        RuntimePaths.EnsureSandboxPath(_settings.StorageRoot);
        Directory.CreateDirectory(_settings.StorageRoot);
        var blockName = UniqueBlockName(name ?? (mode == BlockModes.Link ? "新建收纳筐" : "新建收纳盒"));
        // 默认尺寸 = 5列×3行图标格（单元格尺寸与 BlockView 一致，物理像素随 DPI 缩放）
        var cellW = _settings.IconSize + 34; // 瓦片 + 左右间距
        var cellH = _settings.ShowIconNames ? _settings.IconSize + 60 : _settings.IconSize + 20;
        var block = new Block
        {
            Name = blockName,
            Mode = mode,
            PreviewRows = previewRows,
            PreviewColumns = previewColumns,
            FolderPath = Path.Combine(_settings.StorageRoot, blockName),
            X = x,
            Y = y,
            Width = (mode == BlockModes.Link ? previewColumns * 52 + 24 : 5 * cellW + 16) * dpiScale,
            Height = (mode == BlockModes.Link ? previewRows * 52 + 55 : 40 + 3 * cellH + 12) * dpiScale
        };
        Directory.CreateDirectory(block.FolderPath);
        _blocks.Add(block);
        Save();
        return block;
    }

    public void RenameBlock(Block block, string newName)
    {
        RuntimePaths.EnsureSandboxPath(block.FolderPath);
        newName = newName.Trim();
        if (newName.Length == 0 || newName == block.Name) return;
        ValidateFolderName(newName);

        // 2026-09-24：历史收纳盒仍可能位于 DesktopBlocks，重命名只改变原目录名称。
        var parent = Path.GetDirectoryName(Path.GetFullPath(block.FolderPath))
                     ?? throw new IOException("收纳盒目录无效。");
        var newPath = Path.Combine(parent, newName);
        RuntimePaths.EnsureSandboxPath(newPath);
        if (!string.Equals(block.FolderPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(newPath))
                throw new IOException($"已存在同名收纳目录：{newName}");
            if (Directory.Exists(block.FolderPath))
                Directory.Move(block.FolderPath, newPath);
            block.FolderPath = newPath;
        }
        block.Name = newName;
        Save();
    }

    /// <summary>
    /// 删除块：把块内文件移到桌面（moveContentsTo 为 null）或移入另一个块，
    /// 然后删除块对应的文件夹（仅剩隐藏文件等删除失败时保留）。
    /// </summary>
    public void DeleteBlock(Block block, Block? moveContentsTo)
    {
        RuntimePaths.EnsureSandboxPath(block.FolderPath);
        if (block.IsLink) throw new IOException("快捷方式收纳筐必须使用链接筐删除流程。");
        if (moveContentsTo?.IsLink == true)
            throw new IOException("实体盒删除时不能把真实文件直接移入链接筐。");
        if (Directory.Exists(block.FolderPath))
        {
            foreach (var item in EnumerateItems(block))
            {
                if (moveContentsTo != null)
                    MoveInto(moveContentsTo, new[] { item.FullPath });
                else
                    MoveToDesktop(item.FullPath);
            }
            TryDeleteFolder(block.FolderPath);
        }
        _blocks.Remove(block);
        Save();
    }

    private static void TryDeleteFolder(string path)
    {
        try { Directory.Delete(path, recursive: false); }
        catch { /* 仍有隐藏文件等残留时保留文件夹，不阻塞删除块 */ }
    }

    /// <summary>读取链接筐受管理的快捷方式；未知内容必须保留，阻止整筐删除。</summary>
    public IReadOnlyList<string> GetValidatedLinkFiles(Block block)
    {
        if (!block.IsLink) throw new IOException("目标不是快捷方式收纳筐。");
        EnsureManagedFolder(block);
        if (!Directory.Exists(block.FolderPath)) return Array.Empty<string>();
        RuntimePaths.EnsureSandboxPath(block.FolderPath);
        var result = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(block.FolderPath))
        {
            if (!File.Exists(path) ||
                !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                File.GetAttributes(path).HasFlag(FileAttributes.Hidden) ||
                File.GetAttributes(path).HasFlag(FileAttributes.System))
                throw new IOException($"收纳筐含未知内容，请先检查：{path}");
            result.Add(path);
        }
        return result;
    }

    /// <summary>快捷方式已清空后删除空目录与布局记录；失败时保持模型不变。</summary>
    public void RemoveEmptyLinkBlock(Block block)
    {
        if (!block.IsLink) throw new IOException("目标不是快捷方式收纳筐。");
        GetValidatedLinkFiles(block);
        if (Directory.Exists(block.FolderPath))
        {
            if (Directory.EnumerateFileSystemEntries(block.FolderPath).Any())
                throw new IOException("收纳筐内仍有快捷方式，不能删除。");
            Directory.Delete(block.FolderPath);
        }
        _blocks.Remove(block);
        Save();
    }

    /// <summary>删除链接筐时只搬迁 .lnk 本身，目标盒的模式保持不变。</summary>
    public void TransferLinkBlock(Block block, Block target)
    {
        if (!block.IsLink || block == target)
            throw new IOException("请选择其他收纳盒作为目标。");
        var files = GetValidatedLinkFiles(block);
        EnsureManagedFolder(target);
        RuntimePaths.EnsureSandboxPath(target.FolderPath);
        Directory.CreateDirectory(target.FolderPath);
        foreach (var file in files)
        {
            var destination = UniqueDestination(target.FolderPath, Path.GetFileName(file));
            MoveEntry(file, destination);
        }
        RemoveEmptyLinkBlock(block);
    }

    /// <summary>卸载时永久删除链接筐中的 .lnk，不将快捷图标移到桌面。</summary>
    public void DeleteLinkFilesForUninstall(Block block)
    {
        foreach (var file in GetValidatedLinkFiles(block)) File.Delete(file);
    }

    /// <summary>2026-09-24：兼容历史 DesktopBlocks，并限制链接筐清理范围。</summary>
    private void EnsureManagedFolder(Block block)
    {
        if (!_blocks.Contains(block))
            throw new IOException("收纳盒不属于当前布局，不能执行文件清理。");
        var root = Path.GetFullPath(_settings.StorageRoot).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var legacy = RuntimePaths.SandboxRoot is { } sandbox
            ? Path.Combine(sandbox, "LegacyStorage")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "DesktopBlocks");
        legacy = Path.GetFullPath(legacy).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folder = Path.GetFullPath(block.FolderPath).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(folder);
        if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parent, legacy, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"收纳盒目录不在受管理的收纳根目录内：{folder}");
    }

    // ---------- 图标移动 ----------

    public IReadOnlyList<IconItem> EnumerateItems(Block block)
    {
        var result = new List<IconItem>();
        if (!Directory.Exists(block.FolderPath)) return result;

        foreach (var dir in Directory.EnumerateDirectories(block.FolderPath))
        {
            if (IsHiddenOrSystem(dir)) continue;
            result.Add(new IconItem { Name = Path.GetFileName(dir), FullPath = dir, IsFolder = true });
        }
        foreach (var file in Directory.EnumerateFiles(block.FolderPath))
        {
            if (IsHiddenOrSystem(file)) continue;
            result.Add(new IconItem
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsFolder = false,
                IsShortcut = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            });
        }
        // 已保存的手动顺序优先；新出现的项目保持文件系统枚举顺序并追加到末尾。
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < block.ItemOrder.Count; index++)
            positions.TryAdd(block.ItemOrder[index], index);

        return result
            .OrderBy(item => positions.TryGetValue(Path.GetFileName(item.FullPath), out var index)
                ? index
                : int.MaxValue)
            .ToList();
    }

    /// <summary>保存块内文件名顺序；过滤重复项，确保布局数据稳定。</summary>
    public void SetItemOrder(Block block, IEnumerable<string> fileNames)
    {
        block.ItemOrder = fileNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Save();
    }

    /// <summary>把若干文件/文件夹移动进块（桌面图标随之消失）。</summary>
    public List<string> MoveInto(Block block, IEnumerable<string> paths)
    {
        if (block.IsLink) throw new IOException("不能向快捷方式收纳筐移动真实文件。");
        RuntimePaths.EnsureSandboxPath(block.FolderPath);
        var moved = new List<string>();
        Directory.CreateDirectory(block.FolderPath);
        foreach (var src in paths)
        {
            if (string.IsNullOrWhiteSpace(src)) continue;
            if (!File.Exists(src) && !Directory.Exists(src)) continue;
            var dest = UniqueDestination(block.FolderPath, Path.GetFileName(src));
            MoveEntry(src, dest);
            moved.Add(dest);
        }
        return moved;
    }

    /// <summary>
    /// 把收纳盒根目录中的项目移动到其子文件夹；调用前应由界面完成二次确认。
    /// 会阻止把文件夹移动到自身或自身的后代目录。
    /// </summary>
    public List<string> MoveIntoFolder(IEnumerable<string> paths, string targetFolder)
    {
        var targetFullPath = Path.GetFullPath(targetFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(targetFullPath))
            throw new IOException("目标文件夹不存在或已被移动。");

        var moved = new List<string>();
        foreach (var source in paths)
        {
            if (!File.Exists(source) && !Directory.Exists(source)) continue;
            var sourceFullPath = Path.GetFullPath(source)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetDirectoryName(sourceFullPath), targetFullPath,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (Directory.Exists(sourceFullPath) &&
                (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase) ||
                 targetFullPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar,
                     StringComparison.OrdinalIgnoreCase)))
                throw new IOException("不能把文件夹移动到自身或它的子文件夹中。");

            var destination = UniqueDestination(targetFullPath, Path.GetFileName(sourceFullPath));
            MoveEntry(sourceFullPath, destination);
            moved.Add(destination);
        }
        return moved;
    }

    /// <summary>把一个条目移回桌面。</summary>
    public string MoveToDesktop(string sourcePath)
    {
        RuntimePaths.EnsureSandboxPath(sourcePath);
        var dest = UniqueDestination(DesktopPath, Path.GetFileName(sourcePath));
        MoveEntry(sourcePath, dest);
        return dest;
    }

    /// <summary>把若干文件/文件夹复制进块（用于 Ctrl+V 复制语义）。</summary>
    public List<string> CopyInto(Block block, IEnumerable<string> paths)
    {
        if (block.IsLink) throw new IOException("快捷方式收纳筐只接受快捷方式引用。");
        RuntimePaths.EnsureSandboxPath(block.FolderPath);
        var copied = new List<string>();
        Directory.CreateDirectory(block.FolderPath);
        foreach (var src in paths)
        {
            if (string.IsNullOrWhiteSpace(src)) continue;
            if (!File.Exists(src) && !Directory.Exists(src)) continue;
            RuntimePaths.EnsureSandboxPath(src);
            var dest = UniqueDestination(block.FolderPath, Path.GetFileName(src));
            if (File.Exists(src)) File.Copy(src, dest);
            else CopyDirectory(src, dest);
            copied.Add(dest);
        }
        return copied;
    }

    /// <summary>一键全部还原：所有块内文件移回桌面（块保留）。</summary>
    public int RestoreAllToDesktop()
    {
        var count = 0;
        foreach (var block in _blocks.ToList())
        {
            if (block.IsLink) continue;
            if (!Directory.Exists(block.FolderPath)) continue;
            foreach (var item in EnumerateItems(block))
            {
                MoveToDesktop(item.FullPath);
                count++;
            }
        }
        return count;
    }

    // ---------- 内部 ----------

    private static void MoveEntry(string src, string dest)
    {
        RuntimePaths.EnsureSandboxPath(src);
        RuntimePaths.EnsureSandboxPath(dest);
        var sameVolume = string.Equals(
            Path.GetPathRoot(Path.GetFullPath(src)),
            Path.GetPathRoot(Path.GetFullPath(dest)),
            StringComparison.OrdinalIgnoreCase);

        if (File.Exists(src))
        {
            if (sameVolume) File.Move(src, dest);
            else { File.Copy(src, dest); File.Delete(src); }
        }
        else
        {
            if (sameVolume) Directory.Move(src, dest);
            else { CopyDirectory(src, dest); Directory.Delete(src, true); }
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    /// <summary>同名冲突时自动加序号：文件 (2).txt / 文件夹 (2)。绝不覆盖。</summary>
    public static string UniqueDestination(string dir, string name)
    {
        var candidate = Path.Combine(dir, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var newName = $"{stem} ({i}){ext}";
            candidate = Path.Combine(dir, newName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private string UniqueBlockName(string baseName)
    {
        ValidateFolderName(baseName);
        var name = baseName;
        for (var i = 2; ; i++)
        {
            var clash = _blocks.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                        || Directory.Exists(Path.Combine(_settings.StorageRoot, name));
            if (!clash) return name;
            name = $"{baseName} ({i})";
        }
    }

    private static void ValidateFolderName(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("名称包含文件夹不允许的字符：\\ / : * ? \" < > |");
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return attr.HasFlag(FileAttributes.Hidden) || attr.HasFlag(FileAttributes.System);
        }
        catch { return true; }
    }
}
