using System.Windows;
using System.Windows.Interop;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

public partial class CloseAskDialog : Window
{
    public enum CloseChoice { Hide, Exit }

    public CloseChoice Choice { get; private set; } = CloseChoice.Hide;
    public bool Remember => RememberBox.IsChecked == true;

    public CloseAskDialog(bool dark)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            BackdropService.ApplyWindowTheme(new WindowInteropHelper(this).Handle, dark);
            ApplyThemeIcon(dark);
        };
    }

    /// <summary>关闭确认窗口使用与当前主题匹配的运行时图标。</summary>
    private void ApplyThemeIcon(bool dark)
    {
        try
        {
            var iconName = dark ? "app-dark.ico" : "app-light.ico";
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri($"pack://application:,,,/Assets/{iconName}", UriKind.Absolute));
        }
        catch { /* 图标加载失败不影响关闭流程 */ }
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Hide;
        DialogResult = true;
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Exit;
        DialogResult = true;
    }
}
