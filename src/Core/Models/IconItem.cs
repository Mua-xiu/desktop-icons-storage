namespace DesktopIconsStorage.Core.Models;

/// <summary>块内的一个条目（文件或文件夹）。</summary>
public class IconItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }
    public bool IsShortcut { get; init; }
}
