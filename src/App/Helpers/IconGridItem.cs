using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>图标网格中的一个条目（绑定用）。</summary>
public class IconGridItem : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class BlockItemsCollection : ObservableCollection<IconGridItem> { }
