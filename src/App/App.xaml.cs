using System.Windows;
using DesktopIconsStorage.App.Helpers;

namespace DesktopIconsStorage.App;

public partial class App : Application
{
    private AppHost? _host;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Contains("--uninstall-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(AppHost.CleanupForUninstall());
            return;
        }

        _host = new AppHost();
        if (!_host.Run())
        {
            // 已有实例在运行：它已被唤醒，本实例直接退出
            Shutdown();
            return;
        }
        // 调试参数：启动后直接打开设置窗口（便于界面验证）
        if (System.Linq.Enumerable.Contains(e.Args, "--settings"))
            _host.OpenSettings();
    }

    private void OnExit(object sender, ExitEventArgs e) => _host?.Dispose();
}
