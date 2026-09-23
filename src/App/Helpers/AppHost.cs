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
    private System.Threading.Timer? _saveDebounce;
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
        var offset = (int)(40 * DpiHelper.SystemScale) * (Windows.Count % 8);
        var block = Blocks.CreateBlock(FirstBlockX() + offset, FirstBlockY() + offset,
            dpiScale: DpiHelper.SystemScale);
        CreateWindow(block);
        Blocks.Save();
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
        var showNames = b.ShowIconNames ?? Settings.ShowIconNames;
        var cellW = Settings.IconSize + 34;
        var cellH = showNames ? Settings.IconSize + 60 : Settings.IconSize + 20;
        // 收纳筐最小为完整 3×3 网格，避免缩小后图标区域无法使用。
        var minW = (3 * cellW + 16) * s;
        var minH = (40 + 3 * cellH + 12) * s;

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
        _saveDebounce ??= new System.Threading.Timer(_ => Blocks.Save(), null, Timeout.Infinite, Timeout.Infinite);
        _saveDebounce.Change(800, Timeout.Infinite);
    }

    // ---------- 块操作 ----------

    public void MoveIntoBlock(BlockWindow w, IEnumerable<string> paths)
    {
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

    /// <summary>二次确认后，将收纳筐中的项目移动到指定子文件夹。</summary>
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

    /// <summary>保存收纳筐内的手动图标顺序。</summary>
    public void SetItemOrder(BlockWindow w, IEnumerable<string> fullPaths) =>
        Blocks.SetItemOrder(w.Block, fullPaths.Select(Path.GetFileName).OfType<string>());

    public void RequestDeleteBlock(BlockWindow w)
    {
        var others = Blocks.Blocks.Where(b => b != w.Block).ToList();
        var dlg = new DeleteBlockDialog(w.Block, others);
        if (dlg.ShowDialog() != true) return;

        try
        {
            var items = Blocks.EnumerateItems(w.Block).Select(i => i.FullPath).ToList();
            if (dlg.Choice == DeleteBlockDialog.DeleteChoice.MoveToOtherBlock && dlg.TargetBlock != null)
            {
                Blocks.DeleteBlock(w.Block, dlg.TargetBlock);
                // 块间移动与桌面图标无关，文件夹监听会自动刷新两个块的视图
            }
            else
            {
                // 移回桌面并删除块文件夹
                Blocks.DeleteBlock(w.Block, null);
                ShellNotifyService.NotifyFolderChanged(Blocks.DesktopPath);
            }
        }
        catch (Exception ex) { NotifyError($"删除失败：{ex.Message}"); }

        Windows.Remove(w);
        w.Dispose();
        Blocks.Save();
        foreach (var other in Windows) other.RefreshView();
    }

    public void RestoreAllWithConfirm()
    {
        var result = MessageBox.Show(
            "将所有收纳块中的文件移回桌面？\n（块会保留，内容清空）",
            "一键全部还原", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
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

    public void ApplyThemeToAll() =>
        InvokeUi(() => { foreach (var w in Windows.ToList()) w.ApplyBackdrop(); });

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
            var manager = new BlockManager(settings);
            manager.Load();
            var blockFolders = manager.Blocks.Select(block => block.FolderPath).ToList();

            manager.RestoreAllToDesktop();
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
        _saveDebounce?.Dispose();
        Blocks?.Save();
        JsonStore.Save(JsonStore.SettingsPath, Settings);
        foreach (var w in Windows.ToList()) w.Dispose();
        Windows.Clear();
        _tray?.Dispose();
        _single?.Dispose();
    }
}
