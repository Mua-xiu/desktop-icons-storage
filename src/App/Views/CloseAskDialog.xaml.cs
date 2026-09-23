using System.Windows;

namespace DesktopIconsStorage.App.Views;

public partial class CloseAskDialog : Window
{
    public enum CloseChoice { Hide, Exit }

    public CloseChoice Choice { get; private set; } = CloseChoice.Hide;
    public bool Remember => RememberBox.IsChecked == true;

    public CloseAskDialog()
    {
        InitializeComponent();
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
