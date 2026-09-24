using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopIconsStorage.Core.Models;

/// <summary>
/// 收纳盒：桌面上的一个区域，对应磁盘上的一个真实文件夹。
/// 坐标/尺寸使用屏幕物理坐标（相对虚拟桌面原点）。
/// </summary>
public class Block : INotifyPropertyChanged
{
    private string _name = "新建收纳盒";
    private bool _collapsed;
    private double _x, _y, _width = 372, _height = 300;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>创建时固定的收纳语义；旧布局缺失此字段时仍按实体盒处理。</summary>
    public string Mode { get; set; } = BlockModes.Move;

    /// <summary>链接筐预览网格行列，创建后只能通过右键菜单修改。</summary>
    public int PreviewRows { get; set; } = 2;
    public int PreviewColumns { get; set; } = 2;

    [JsonIgnore]
    public bool IsLink => Mode == BlockModes.Link;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>块对应的文件夹完整路径。</summary>
    public string FolderPath { get; set; } = "";

    /// <summary>本块是否显示图标名称；null = 跟随全局设置。</summary>
    public bool? ShowIconNames { get; set; }

    /// <summary>块内项目的手动顺序，保存文件名而非完整路径，避免块重命名后顺序失效。</summary>
    public List<string> ItemOrder { get; set; } = new();

    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public double Width { get => _width; set => SetField(ref _width, value); }
    public double Height { get => _height; set => SetField(ref _height, value); }

    public bool Collapsed
    {
        get => _collapsed;
        set => SetField(ref _collapsed, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>两种收纳盒模式的持久化取值。</summary>
public static class BlockModes
{
    public const string Move = "move";
    public const string Link = "link";

    public static bool IsValid(string? mode) => mode is Move or Link;
}
