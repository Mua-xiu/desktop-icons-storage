using System.Windows;
using DesktopIconsStorage.Core.Models;

namespace DesktopIconsStorage.App.Views;

public partial class DeleteBlockDialog : Window
{
    public enum DeleteChoice { MoveToDesktop, MoveToOtherBlock, KeepFolderOnly }

    private readonly List<Block> _others;

    public DeleteChoice Choice { get; private set; } = DeleteChoice.MoveToDesktop;
    public Block? TargetBlock { get; private set; }

    public DeleteBlockDialog(Block block, List<Block> otherBlocks)
    {
        InitializeComponent();
        _others = otherBlocks;
        PromptText.Text = $"确定删除收纳块“{block.Name}”吗？块内文件如何处理：";
        TargetCombo.ItemsSource = _others.Select(b => b.Name);
        if (_others.Count > 0)
        {
            TargetCombo.SelectedIndex = 0;
        }
        else
        {
            OptMove.IsEnabled = false;
            TargetCombo.IsEnabled = false;
        }
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
