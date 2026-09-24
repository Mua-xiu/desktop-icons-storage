using System.IO;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Core.Services;
using DesktopIconsStorage.Platform.Services;

// 所有夹具都在被 Git 忽略的 artifacts 下；不枚举或操作真实桌面文件。
var repo = Path.GetFullPath(Environment.CurrentDirectory);
Check(File.Exists(Path.Combine(repo, "src", "App", "Assets", "app.ico")),
    "请从仓库根目录运行烟测。");
var root = Path.Combine(repo, "artifacts", "test-sandbox", "Smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("DESKTOPICONSSTORAGE_TEST_ROOT", root);
var testDesktop = RuntimePaths.DesktopPath;
Directory.CreateDirectory(testDesktop);
var sample = Path.Combine(testDesktop, "测试文件.txt");
File.WriteAllText(sample, "只用于链接筐烟测。");

var manager = new BlockManager(new AppSettings { StorageRoot = Path.Combine(root, "Storage") });
var linkBlock = manager.CreateBlock(10, 10, "链接筐", mode: BlockModes.Link);
var entityBlock = manager.CreateBlock(200, 10, "实体盒");
var largeBlock = manager.CreateBlock(400, 10, "十行十列",
    mode: BlockModes.Link, previewRows: 10, previewColumns: 10);
Check(largeBlock.PreviewRows == 10 && largeBlock.PreviewColumns == 10,
    "预览规格应支持 10 行 10 列");
Check(Throws(() => manager.CreateBlock(400, 10, "越界规格",
        mode: BlockModes.Link, previewRows: 11)),
    "预览规格不能超过 10");
var fitted = LinkBasketSize.Calculate(10, 10, 1, 400, 300);
Check(fitted.Width <= 376 && fitted.Height <= 276,
    "十行十列预览在小工作区内应等比缩小");

var add = LinkBasketService.Add(linkBlock, new[] { sample });
Check(add.Added == 1 && add.Errors.Count == 0, "真实文件应生成快捷方式");
Check(File.Exists(sample), "原文件不应被移动");
var shortcut = manager.GetValidatedLinkFiles(linkBlock).Single();
Check(Path.GetFullPath(ShellLinkService.ReadTarget(shortcut) ?? "") == sample,
    "快捷方式应指向原文件");

var externalLink = Path.Combine(testDesktop, "已有快捷方式.lnk");
ShellLinkService.Create(sample, externalLink);
var copy = LinkBasketService.Add(linkBlock, new[] { externalLink });
Check(copy.Added == 1 && File.Exists(externalLink), "已有快捷方式应复制而非移动");
Check(manager.GetValidatedLinkFiles(linkBlock).Count == 2, "链接筐应有两个快捷方式");

Check(Throws(() => manager.MoveInto(linkBlock, new[] { sample })),
    "链接筐必须拒绝真实移动入口");
Check(manager.RestoreAllToDesktop() == 0 && File.Exists(sample),
    "实体盒还原不能移动链接筐目标");
Check(LinkBasketService.Add(linkBlock, new[] { Path.Combine(repo, "README.md") }).Errors.Count == 1,
    "测试模式必须拒绝沙盒外源路径");

var unknown = Path.Combine(linkBlock.FolderPath, "未知内容.txt");
File.WriteAllText(unknown, "应当保留");
Check(Throws(() => manager.GetValidatedLinkFiles(linkBlock)),
    "未知内容必须阻止整筐删除");
File.Delete(unknown);

manager.TransferLinkBlock(linkBlock, entityBlock);
Check(!manager.Blocks.Contains(linkBlock) && File.Exists(sample),
    "转移链接筐不能删除原文件");
Check(Directory.EnumerateFiles(entityBlock.FolderPath, "*.lnk").Count() == 2,
    "转入实体盒仍只移动快捷方式文件");

var uninstallBlock = manager.CreateBlock(10, 300, "卸载测试", mode: BlockModes.Link);
LinkBasketService.Add(uninstallBlock, new[] { sample });
manager.DeleteLinkFilesForUninstall(uninstallBlock);
manager.RemoveEmptyLinkBlock(uninstallBlock);
Check(File.Exists(sample), "卸载清理不能删除快捷方式目标");

// 历史布局仍指向旧收纳根目录；链接筐应可加载，重命名不迁走真实目录。
var legacyFolder = Path.Combine(root, "LegacyStorage", "旧链接筐");
Directory.CreateDirectory(legacyFolder);
ShellLinkService.Create(sample, Path.Combine(legacyFolder, "测试文件.lnk"));
var legacyBlock = new Block
{
    Name = "旧链接筐",
    Mode = BlockModes.Link,
    FolderPath = legacyFolder
};
JsonStore.Save(JsonStore.LayoutPath, new List<Block> { legacyBlock });
var legacyManager = new BlockManager(new AppSettings
{
    StorageRoot = Path.Combine(root, "Storage")
});
legacyManager.Load();
Check(legacyManager.GetValidatedLinkFiles(legacyManager.Blocks.Single()).Count == 1,
    "历史 DesktopBlocks 布局应能加载快捷方式");
legacyManager.RenameBlock(legacyManager.Blocks.Single(), "旧链接筐已改名");
Check(Path.GetDirectoryName(legacyManager.Blocks.Single().FolderPath) ==
      Path.Combine(root, "LegacyStorage"),
    "历史收纳筐重命名不能改变其收纳根路径");

Console.WriteLine("链接筐隔离烟测通过；真实桌面文件未参与测试。");

/// <summary>断言测试中的安全边界。</summary>
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

/// <summary>断言危险入口会拒绝操作。</summary>
static bool Throws(Action action)
{
    try { action(); return false; }
    catch { return true; }
}
