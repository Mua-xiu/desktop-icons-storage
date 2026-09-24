using System.Security.Cryptography;
using System.Text;
using DesktopIconsStorage.Core.Services;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>单实例：命名 Mutex + 命名事件（二次启动时唤醒已有实例）。</summary>
public sealed class SingleInstanceService : IDisposable
{
    private static string InstanceSuffix => RuntimePaths.SandboxRoot is { } root
        ? ".Test." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..12]
        : "";

    private static string MutexName => @"Local\DesktopIconsStorage.SingleInstance" + InstanceSuffix;
    private static string WakeupEventName => @"Local\DesktopIconsStorage.Wakeup" + InstanceSuffix;

    private Mutex? _mutex;
    private EventWaitHandle? _wakeup;
    private Thread? _listener;
    private volatile bool _stopped;

    public event Action? SecondInstanceDetected;

    /// <summary>尝试成为唯一实例；返回 false 表示已有实例在运行（已通知其唤醒）。</summary>
    public bool Acquire()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            try { EventWaitHandle.OpenExisting(WakeupEventName).Set(); } catch { }
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _wakeup = new EventWaitHandle(false, EventResetMode.AutoReset, WakeupEventName);
        _listener = new Thread(ListenLoop) { IsBackground = true, Name = "SingleInstanceListener" };
        _listener.Start();
        return true;
    }

    private void ListenLoop()
    {
        while (!_stopped)
        {
            try
            {
                if (_wakeup!.WaitOne() && !_stopped)
                    SecondInstanceDetected?.Invoke();
            }
            catch (ObjectDisposedException) { break; }
            catch { break; }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        try { _wakeup?.Set(); } catch { }
        _wakeup?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
    }
}
