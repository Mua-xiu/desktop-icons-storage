using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace DesktopIconsStorage.App.Helpers;

/// <summary>图标网格中的一个条目（绑定用）。</summary>
public class IconGridItem : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private double _opacity = 1.0;
    private string? _healthTip;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }

    /// <summary>链接筐体检结果（不参与绑定；默认 Ok）。</summary>
    public LinkHealth Health { get; set; } = LinkHealth.Ok;

    /// <summary>失效项目降低不透明度以示区别。</summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
        }
    }

    /// <summary>悬浮提示：名称 + 失效原因（无失效时只有名称）。</summary>
    public string ToolTipText => HealthTip == null ? Name : $"{Name}\n{HealthTip}";

    public string? HealthTip
    {
        get => _healthTip;
        set
        {
            _healthTip = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HealthTip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTipText)));
        }
    }

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
