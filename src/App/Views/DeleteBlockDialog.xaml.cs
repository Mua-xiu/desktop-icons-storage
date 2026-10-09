using System.Windows;
using System.Windows.Interop;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

public partial class DeleteBlockDialog : Window
{
    public enum DeleteChoice { MoveToDesktop, MoveToOtherBlock, KeepFolderOnly }

    private readonly AppHost _host;
    private readonly List<Block> _others;

    public DeleteChoice Choice { get; private set; } = DeleteChoice.MoveToDesktop;
    public Block? TargetBlock { get; private set; }

    public DeleteBlockDialog(AppHost host, Block block, List<Block> otherBoxes)
    {
        InitializeComponent();
        _host = host;
        _others = otherBoxes;
        PromptText.Text = block.IsLink
            ? $"确定删除快捷方式收纳筐“{block.Name}”吗？请选择快捷图标的处理方式。"
            : $"确定删除收纳盒“{block.Name}”吗？请选择盒内文件的处理方式。";
        if (block.IsLink)
        {
            OptDesktop.Content = "删除收纳筐及所有快捷图标（推荐）";
            OptMove.Content = "快捷图标移入其他收纳盒";
            RiskText.Text = "只处理快捷方式文件，不删除它们指向的原文件；快捷图标不会散落到桌面。";
        }
        TargetCombo.ItemsSource = _others.Select(b => b.Name);
        SourceInitialized += (_, _) => ApplyWindowTheme();
        if (_others.Count > 0)
        {
            TargetCombo.SelectedIndex = 0;
        }
        else
        {
            OptMove.IsEnabled = false;
            TargetCombo.IsEnabled = false;
            NoOtherBoxText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>同步标题栏深浅模式和运行时主题图标。</summary>
    private void ApplyWindowTheme()
    {
        BackdropService.ApplyWindowTheme(new WindowInteropHelper(this).Handle, _host.IsDarkTheme);
        try
        {
            var iconName = _host.IsDarkTheme ? "app-dark.ico" : "app-light.ico";
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri($"pack://application:,,,/Assets/{iconName}", UriKind.Absolute));
        }
        catch { /* 图标加载失败不影响删除流程 */ }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (OptMove.IsChecked == true)
        {
            Choice = DeleteChoice.MoveToOtherBlock;
            if (TargetCombo.SelectedIndex >= 0 && TargetCombo.SelectedIndex < _others.Count)
                TargetBlock = _others[TargetCombo.SelectedIndex];
        }
        else
        {
            Choice = DeleteChoice.MoveToDesktop;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
