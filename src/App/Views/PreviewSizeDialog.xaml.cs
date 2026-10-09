using System.Windows;
using System.Windows.Interop;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

/// <summary>链接筐右键规格对话框，分别选择 2 到 10 行和列。</summary>
public partial class PreviewSizeDialog : Window
{
    public int Rows => RowsBox.SelectedItem is int value
        ? value : PreviewGridLimits.Default;
    public int Columns => ColumnsBox.SelectedItem is int value
        ? value : PreviewGridLimits.Default;

    public PreviewSizeDialog(bool dark, int rows, int columns)
    {
        InitializeComponent();
        var values = Enumerable.Range(PreviewGridLimits.Minimum,
            PreviewGridLimits.Maximum - PreviewGridLimits.Minimum + 1).ToArray();
        RowsBox.ItemsSource = values;
        ColumnsBox.ItemsSource = values;
        RowsBox.SelectedItem = Math.Clamp(rows,
            PreviewGridLimits.Minimum, PreviewGridLimits.Maximum);
        ColumnsBox.SelectedItem = Math.Clamp(columns,
            PreviewGridLimits.Minimum, PreviewGridLimits.Maximum);
        SourceInitialized += (_, _) =>
            BackdropService.ApplyWindowTheme(new WindowInteropHelper(this).Handle, dark);
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
