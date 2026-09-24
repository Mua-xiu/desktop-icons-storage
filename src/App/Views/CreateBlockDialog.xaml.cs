using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

/// <summary>创建时收集名称、不可切换的模式及链接筐预览规格。</summary>
public partial class CreateBlockDialog : Window
{
    public string BlockName => NameBox.Text.Trim();
    public string Mode => LinkMode.IsChecked == true ? BlockModes.Link : BlockModes.Move;
    public int PreviewRows => RowsBox.SelectedItem is int value
        ? value : PreviewGridLimits.Default;
    public int PreviewColumns => ColumnsBox.SelectedItem is int value
        ? value : PreviewGridLimits.Default;

    public CreateBlockDialog(bool dark)
    {
        InitializeComponent();
        // 创建与后续修改规格使用同一范围，避免已保存规格无法在界面中重选。
        var values = Enumerable.Range(PreviewGridLimits.Minimum,
            PreviewGridLimits.Maximum - PreviewGridLimits.Minimum + 1).ToArray();
        RowsBox.ItemsSource = values;
        ColumnsBox.ItemsSource = values;
        RowsBox.SelectedItem = PreviewGridLimits.Default;
        ColumnsBox.SelectedItem = PreviewGridLimits.Default;
        LinkMode.Checked += (_, _) =>
        {
            PreviewOptions.Visibility = Visibility.Visible;
            if (NameBox.Text == "新建收纳盒") NameBox.Text = "新建收纳筐";
        };
        MoveMode.Checked += (_, _) =>
        {
            PreviewOptions.Visibility = Visibility.Collapsed;
            if (NameBox.Text == "新建收纳筐") NameBox.Text = "新建收纳盒";
        };
        SourceInitialized += (_, _) =>
            BackdropService.ApplyWindowTheme(new WindowInteropHelper(this).Handle, dark);
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (BlockName.Length == 0)
        {
            MessageBox.Show("请输入收纳盒名称。", "无法创建",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
