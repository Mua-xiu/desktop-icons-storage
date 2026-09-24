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
    public int PreviewRows => int.Parse((string)((ComboBoxItem)RowsBox.SelectedItem).Tag);
    public int PreviewColumns => int.Parse((string)((ComboBoxItem)ColumnsBox.SelectedItem).Tag);

    public CreateBlockDialog(bool dark)
    {
        InitializeComponent();
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
