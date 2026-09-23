namespace DesktopIconsStorage.Core.Services;

/// <summary>监听块文件夹的外部变更，防抖后触发刷新。</summary>
public sealed class FolderWatchService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;

    public event Action? Changed;

    public FolderWatchService(string folder, int debounceMs = 300)
    {
        _debounce = new Timer(_ => Changed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);

        Directory.CreateDirectory(folder);
        _watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Renamed += OnEvent;
        _watcher.Changed += OnEvent;
    }

    private void OnEvent(object sender, FileSystemEventArgs e) =>
        _debounce.Change(300, Timeout.Infinite);

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
