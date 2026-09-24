using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconsStorage.App.Tray;
using DesktopIconsStorage.App.Views;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Core.Services;
using DesktopIconsStorage.Platform.Services;
using Microsoft.Win32;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>应用中枢：设置、块管理、窗口集合、托盘、看门狗、主题。</summary>
public class AppHost : IDisposable
{
    public AppSettings Settings { get; private set; } = new();
    public BlockManager Blocks { get; private set; } = null!;
    public List<BlockWindow> Windows { get; } = new();
    public bool IsRunning { get; private set; }

    private TrayManager? _tray;
    private SingleInstanceService? _single;
    private DispatcherTimer? _watchdog;
    private DispatcherTimer? _saveDebounce;
    private SettingsWindow? _settingsWindow;

    public bool IsDarkTheme => Settings.ThemeMode switch
    {
        "light" => false,
        "dark" => true,
        _ => !ThemeService.AppsUseLightTheme()
    };

    public Color AccentColor => ThemeService.GetAccentColor();

    /// <summary>设置窗口与收纳盒共用的 Win11 深浅色板。</summary>
    public ThemePalette ThemePalette => ThemeService.GetPalette(IsDarkTheme);

    public bool Run()
    {
        try
        {
            JsonStore.MigrateLegacyFiles();
            Log("Run: acquiring single instance");
            _single = new SingleInstanceService();
            if (!_single.Acquire()) { Log("Run: second instance, exit"); return false; }
            _single.SecondInstanceDetected += () => InvokeUi(() => _tray?.Balloon("DesktopIconsStorage 已在运行中"));

            Log("Run: loading settings");
            Settings = JsonStore.Load<AppSettings>(JsonStore.SettingsPath);
            // 测试运行强制使用隔离目录，防止旧设置指向真实收纳数据。
            if (RuntimePaths.SandboxStorageRoot is { } sandboxStorage)
                Settings.StorageRoot = sandboxStorage;
            ThemeResourceManager.Apply(ThemePalette, AccentColor);
            AutostartService.RecordStorageRoot(Settings.StorageRoot);
            Blocks = new BlockManager(Settings);
            Blocks.Load();
            Log($"Run: blocks loaded, count={Blocks.Blocks.Count}");

            if (Blocks.Blocks.Count == 0)
                Blocks.CreateBlock(FirstBlockX(), FirstBlockY(), "常用", DpiHelper.SystemScale);

            foreach (var block in Blocks.Blocks.ToList())
                CreateWindow(block);
            Blocks.Save();
            Log($"Run: windows created, count={Windows.Count}");

            SyncAutostart();
            _tray = new TrayManager(this);
            Log("Run: tray created");

            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _watchdog.Tick += (_, _) =>
            {
                foreach (var w in Windows.ToList()) w.ReembedIfNeeded();
            };
            _watchdog.Start();

            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            IsRunning = true;
            Log("Run: completed");
            return true;
        }
        catch (Exception ex)
        {
            Log("Run FATAL: " + ex);
            return false;
        }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(JsonStore.ConfigDir);
            File.AppendAllText(Path.Combine(JsonStore.ConfigDir, "startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        ApplyThemeToAll();

    // ---------- 块窗口管理 ----------

    private void CreateWindow(Block block)
    {
        if (!BlockModes.IsValid(block.Mode))
        {
            Log($"未知收纳盒模式，跳过窗口：{block.Name}");
            return;
        }
        NormalizeBlockBounds(block);
        var w = new BlockWindow(this, block);
        Windows.Add(w);
        try { w.Show(); }
        catch (Exception ex)
        {
            LogError("CreateWindow", ex);
            Windows.Remove(w);
            w.Dispose();
        }
    }

    public static void LogError(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(JsonStore.ConfigDir);
            File.AppendAllText(Path.Combine(JsonStore.ConfigDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
        }
        catch { /* 日志失败不阻塞 */ }
    }

    public void NewBlock()
    {
        var dialog = new CreateBlockDialog(IsDarkTheme);
        if (dialog.ShowDialog() != true) return;
        var offset = (int)(40 * DpiHelper.SystemScale) * (Windows.Count % 8);
        try
        {
            var block = Blocks.CreateBlock(FirstBlockX() + offset, FirstBlockY() + offset,
                dialog.BlockName, DpiHelper.SystemScale, dialog.Mode,
                dialog.PreviewRows, dialog.PreviewColumns);
            CreateWindow(block);
            Blocks.Save();
        }
        catch (Exception ex) { NotifyError($"创建收纳盒失败：{ex.Message}"); }
    }

    private static int FirstBlockX()
    {
        var (vx, _, _, _) = DesktopEmbedService.GetVirtualScreen();
        return vx + (int)(60 * DpiHelper.SystemScale);
    }

    private static int FirstBlockY() => (int)(80 * DpiHelper.SystemScale);

    /// <summary>
    /// 修复历史版本产生的越界/全屏布局，并保证标题栏和缩放边始终可触达。
    /// 仅在宽高同时接近整块工作区时恢复默认尺寸，避免影响用户正常的大尺寸收纳盒。
    /// </summary>
    private void NormalizeBlockBounds(Block b)
    {
        var area = DesktopEmbedService.GetNearestMonitorWorkArea(
            (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height);
        if (area.W <= 0 || area.H <= 0) return;

        var s = DpiHelper.SystemScale;
        if (b.IsLink)
        {
            // 链接筐尺寸只由持久化行列规格决定，旧版八向缩放不能改变小盒。
            b.Collapsed = false;
            b.PreviewRows = Math.Clamp(b.PreviewRows,
                PreviewGridLimits.Minimum, PreviewGridLimits.Maximum);
            b.PreviewColumns = Math.Clamp(b.PreviewColumns,
                PreviewGridLimits.Minimum, PreviewGridLimits.Maximum);
            var monitorScale = DpiHelper.ScaleAt((int)b.X + (int)b.Width / 2,
                (int)b.Y + (int)b.Height / 2);
            var size = LinkBasketSize.Calculate(b.PreviewRows, b.PreviewColumns,
                monitorScale, area.W, area.H);
            b.Width = size.Width;
            b.Height = size.Height;
            var margin = Math.Max(12 * monitorScale, 12);
            b.X = Math.Clamp(b.X, area.X + margin,
                Math.Max(area.X + margin, area.X + area.W - b.Width - margin));
            b.Y = Math.Clamp(b.Y, area.Y + margin,
                Math.Max(area.Y + margin, area.Y + area.H - b.Height - margin));
            return;
        }
        var showNames = b.ShowIconNames ?? Settings.ShowIconNames;
        var cellW = Settings.IconSize + 34;
        var cellH = showNames ? Settings.IconSize + 60 : Settings.IconSize + 20;
        // 仅要求三列宽、一行高；显示名称时不应把窗口高度锁在三行。
        var minW = (3 * cellW + 16) * s;
        var minH = (40 + cellH + 12) * s;

        // 旧版异常会把块保存为整屏尺寸；恢复为 5 列 × 3 行的常用大小。
        if (b.Width >= area.W * 0.95 && b.Height >= area.H * 0.90)
        {
            b.Width = (5 * cellW + 16) * s;
            b.Height = (40 + 3 * cellH + 12) * s;
        }

        var edgeGap = Math.Max(12 * s, 12);
        b.Width = Math.Clamp(b.Width, minW, Math.Max(minW, area.W - edgeGap * 2));
        b.Height = Math.Clamp(b.Height, minH, Math.Max(minH, area.H - edgeGap * 2));
        b.X = Math.Clamp(b.X, area.X + edgeGap, area.X + area.W - b.Width - edgeGap);
        b.Y = Math.Clamp(b.Y, area.Y + edgeGap, area.Y + area.H - b.Height - edgeGap);
    }

    // ---------- 布局持久化（防抖） ----------

    public void PersistLayout()
    {
        // 布局模型由 WPF UI 线程修改；在同一线程防抖保存，避免后台 Timer
        // 与新建/删除收纳盒并发枚举列表，造成布局文件偶发回退或丢项。
        _saveDebounce ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _saveDebounce.Stop();
        _saveDebounce.Tick -= OnLayoutSaveDebounceTick;
        _saveDebounce.Tick += OnLayoutSaveDebounceTick;
        _saveDebounce.Start();
    }

    private void OnLayoutSaveDebounceTick(object? sender, EventArgs e)
    {
        if (_saveDebounce == null) return;
        _saveDebounce.Stop();
        _saveDebounce.Tick -= OnLayoutSaveDebounceTick;
        Blocks.Save();
    }

    // ---------- 块操作 ----------

    public void MoveIntoBlock(BlockWindow w, IEnumerable<string> paths)
    {
        if (w.Block.IsLink)
        {
            var result = LinkBasketService.Add(w.Block, paths);
            if (result.Errors.Count > 0)
                NotifyError($"添加快捷方式时有 {result.Errors.Count} 项失败：{result.Errors[0]}");
            w.RefreshViewAfterInternalChange();
            return;
        }
        try
        {
            var srcList = paths
                .Where(p => !string.Equals(Path.GetDirectoryName(p), w.Block.FolderPath, StringComparison.OrdinalIgnoreCase))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .Select(p => (Path: p, IsDir: Directory.Exists(p)))
                .ToList();
            if (srcList.Count > 0)
            {
                var movedPaths = Blocks.MoveInto(w.Block, srcList.Select(x => x.Path));
                // 主动通知 Shell：桌面上的原图标立即消失（否则会残留"幽灵图标"）
                ShellNotifyService.NotifyMoved(srcList.Zip(movedPaths,
                    (source, destination) => (source.Path, source.IsDir, destination)));
            }
        }
        catch (Exception ex) { NotifyError($"移动失败：{ex.Message}"); }
        w.RefreshViewAfterInternalChange();
    }

    /// <summary>二次确认后，将收纳盒中的项目移动到指定子文件夹。</summary>
    public void MoveItemsIntoFolder(BlockWindow w, IEnumerable<string> paths, string targetFolder)
    {
        try
        {
            Blocks.MoveIntoFolder(paths, targetFolder);
        }
        catch (Exception ex)
        {
            NotifyError($"移动到文件夹失败：{ex.Message}");
        }
        w.RefreshViewAfterInternalChange();
    }

    public void ToggleCollapse(BlockWindow w) => w.SetCollapsed(!w.Block.Collapsed);

    public void SetAllCollapsed(bool collapsed)
    {
        foreach (var w in Windows) w.SetCollapsed(collapsed);
        Blocks.Save();
    }

    public void RenameBlock(BlockWindow w, string newName)
    {
        try
        {
            Blocks.RenameBlock(w.Block, newName);
            w.OnFolderRenamed();
            w.RefreshView();
        }
        catch (Exception ex) { NotifyError($"重命名失败：{ex.Message}"); }
    }

    /// <summary>保存收纳盒内的手动图标顺序。</summary>
    public void SetItemOrder(BlockWindow w, IEnumerable<string> fullPaths) =>
        Blocks.SetItemOrder(w.Block, fullPaths.Select(Path.GetFileName).OfType<string>());

    /// <summary>通过小盒右键菜单修改预览规格，保持尺寸与布局数据一致。</summary>
    public void SetLinkPreviewSize(BlockWindow w, int rows, int columns)
    {
        if (!w.Block.IsLink || !PreviewGridLimits.IsValid(rows) ||
            !PreviewGridLimits.IsValid(columns)) return;
        w.SetLinkPreviewSize(rows, columns);
    }

    public void RequestDeleteBlock(BlockWindow w)
    {
        var others = Blocks.Blocks.Where(b => b != w.Block &&
            (w.Block.IsLink || !b.IsLink)).ToList();
        var dlg = new DeleteBlockDialog(this, w.Block, others);
        if (dlg.ShowDialog() != true) return;
        if (dlg.Choice == DeleteBlockDialog.DeleteChoice.MoveToOtherBlock &&
            dlg.TargetBlock == null)
        {
            NotifyError("请选择要接收快捷方式的收纳盒。");
            return;
        }

        var deleted = false;
        try
        {
            if (w.Block.IsLink)
            {
                if (dlg.Choice == DeleteBlockDialog.DeleteChoice.MoveToOtherBlock &&
                    dlg.TargetBlock != null)
                    Blocks.TransferLinkBlock(w.Block, dlg.TargetBlock);
                else
                {
                    var links = Blocks.GetValidatedLinkFiles(w.Block);
                    if (!ShellFileService.RecycleDelete(w.Hwnd, links))
                        throw new IOException("快捷方式未能全部移入回收站。");
                    Blocks.RemoveEmptyLinkBlock(w.Block);
                }
            }
            else
            {
                if (dlg.Choice == DeleteBlockDialog.DeleteChoice.MoveToOtherBlock &&
                    dlg.TargetBlock != null)
                    Blocks.DeleteBlock(w.Block, dlg.TargetBlock);
                else
                {
                    Blocks.DeleteBlock(w.Block, null);
                    ShellNotifyService.NotifyFolderChanged(Blocks.DesktopPath);
                }
            }
            deleted = true;
        }
        catch (Exception ex) { NotifyError($"删除失败：{ex.Message}"); }

        if (!deleted) return;

        Windows.Remove(w);
        w.Dispose();
        Blocks.Save();
        foreach (var other in Windows) other.RefreshView();
    }

    public void RestoreAllWithConfirm()
    {
        var linkCount = Blocks.Blocks.Where(b => b.IsLink)
            .Sum(b => Blocks.EnumerateItems(b).Count);
        var moveCount = Blocks.Blocks.Where(b => !b.IsLink)
            .Sum(b => Blocks.EnumerateItems(b).Count);
        var result = MessageBox.Show(
            $"实体盒 {moveCount} 项移回桌面；链接筐 {linkCount} 个快捷方式移入回收站。\n" +
            "快捷方式目标保持原位，收纳盒会保留。是否继续？",
            "一键全部还原", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            foreach (var block in Blocks.Blocks.Where(b => b.IsLink))
            {
                var links = Blocks.GetValidatedLinkFiles(block);
                if (!ShellFileService.RecycleDelete(IntPtr.Zero, links))
                    throw new IOException($"清空链接筐失败：{block.Name}");
            }
            Blocks.RestoreAllToDesktop();
            // 通知 Shell 刷新桌面与所有块文件夹
            ShellNotifyService.NotifyFolderChanged(Blocks.DesktopPath);
            foreach (var b in Blocks.Blocks) ShellNotifyService.NotifyFolderChanged(b.FolderPath);
        }
        catch (Exception ex) { NotifyError($"还原失败：{ex.Message}"); }
        foreach (var w in Windows) w.RefreshView();
    }

    // ---------- 设置与主题 ----------

    public void SetAutoStart(bool enabled)
    {
        Settings.AutoStart = enabled;
        try { AutostartService.SetEnabled(enabled); } catch { }
        JsonStore.Save(JsonStore.SettingsPath, Settings);
    }

    private void SyncAutostart()
    {
        try
        {
            AutostartService.RemoveLegacyRegistration();
            if (AutostartService.IsEnabled() != Settings.AutoStart)
                AutostartService.SetEnabled(Settings.AutoStart);
        }
        catch { /* 注册表不可写时忽略 */ }
    }

    public void ApplySettings()
    {
        JsonStore.Save(JsonStore.SettingsPath, Settings);
        try { AutostartService.RecordStorageRoot(Settings.StorageRoot); } catch { }
        ApplyThemeToAll();
    }

    public void ApplyThemeToAll() => InvokeUi(() =>
    {
        ThemeResourceManager.Apply(ThemePalette, AccentColor);
        foreach (var w in Windows.ToList()) w.ApplyBackdrop();
        _tray?.ApplyThemeIcon(IsDarkTheme);
        _settingsWindow?.ApplyThemeIcon();
    });

    public void OpenSettings()
    {
        Log("OpenSettings called, existing=" + (_settingsWindow != null));
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        Log("OpenSettings shown");
    }

    public void OpenStorageRoot()
    {
        Directory.CreateDirectory(Settings.StorageRoot);
        ShellFileService.Open(Settings.StorageRoot);
    }

    // ---------- 杂项 ----------

    public void NotifyError(string message) => _tray?.Balloon(message);

    public void ExitApp() => Application.Current.Shutdown();

    /// <summary>
    /// 卸载器调用：把所有可见收纳项目安全移回桌面，再删除空收纳目录和配置。
    /// 任一步失败都返回非零值，让卸载器保留数据并提示用户手动处理。
    /// </summary>
    public static int CleanupForUninstall()
    {
        try
        {
            JsonStore.MigrateLegacyFiles();
            var settings = JsonStore.Load<AppSettings>(JsonStore.SettingsPath);
            if (RuntimePaths.SandboxStorageRoot is { } sandboxStorage)
                settings.StorageRoot = sandboxStorage;
            var manager = new BlockManager(settings);
            manager.Load();
            var blockFolders = manager.Blocks.Select(block => block.FolderPath).ToList();

            manager.RestoreAllToDesktop();
            foreach (var block in manager.Blocks.Where(b => b.IsLink))
                manager.DeleteLinkFilesForUninstall(block);
            ShellNotifyService.NotifyFolderChanged(manager.DesktopPath);

            foreach (var folder in blockFolders)
                DeleteDirectoryIfEmpty(folder);
            DeleteDirectoryIfEmpty(settings.StorageRoot);
            if (Directory.Exists(settings.StorageRoot) &&
                Directory.EnumerateFileSystemEntries(settings.StorageRoot).Any())
                throw new IOException($"收纳目录仍包含未处理文件：{settings.StorageRoot}");

            AutostartService.RemoveAllRegistrations();
            JsonStore.DeleteConfigurationForUninstall();
            return 0;
        }
        catch (Exception ex)
        {
            LogError("CleanupForUninstall", ex);
            return 1;
        }
    }

    /// <summary>只删除确认为空的目录，绝不递归清除未知或隐藏的用户文件。</summary>
    private static void DeleteDirectoryIfEmpty(string path)
    {
        RuntimePaths.EnsureSandboxPath(path);
        if (!Directory.Exists(path)) return;
        if (!Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path, recursive: false);
    }

    private static void InvokeUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        _watchdog?.Stop();
        _saveDebounce?.Stop();
        if (_saveDebounce != null)
            _saveDebounce.Tick -= OnLayoutSaveDebounceTick;
        Blocks?.Save();
        JsonStore.Save(JsonStore.SettingsPath, Settings);
        foreach (var w in Windows.ToList()) w.Dispose();
        Windows.Clear();
        _tray?.Dispose();
        _single?.Dispose();
    }
}
