using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

/// <summary>链接筐展开窗口：完整快捷方式列表、名称开关和主题化悬浮提示。</summary>
public partial class LinkBasketPopupView : UserControl
{
    private const string ReorderFormat = "DesktopIconsStorage.LinkBasketReorderId";
    private readonly BlockWindow _tile;
    private readonly LinkBasketPopupWindow _popup;
    private readonly AppHost _host;
    private Point _dragStart;

    public BlockItemsCollection Items { get; } = new();

    public static readonly DependencyProperty NamesVisibilityProperty =
        DependencyProperty.Register(nameof(NamesVisibility), typeof(Visibility),
            typeof(LinkBasketPopupView), new PropertyMetadata(Visibility.Collapsed));
    public Visibility NamesVisibility
    {
        get => (Visibility)GetValue(NamesVisibilityProperty);
        set => SetValue(NamesVisibilityProperty, value);
    }

    public static readonly DependencyProperty NamesHiddenProperty =
        DependencyProperty.Register(nameof(NamesHidden), typeof(bool),
            typeof(LinkBasketPopupView), new PropertyMetadata(true));
    public bool NamesHidden
    {
        get => (bool)GetValue(NamesHiddenProperty);
        set => SetValue(NamesHiddenProperty, value);
    }

    public LinkBasketPopupView(BlockWindow tile, LinkBasketPopupWindow popup, AppHost host)
    {
        InitializeComponent();
        _tile = tile;
        _popup = popup;
        _host = host;
        IconList.ItemsSource = Items;
        NamesToggle.Checked += (_, _) => SetShowNames(true);
        NamesToggle.Unchecked += (_, _) => SetShowNames(false);
        IconList.PreviewMouseLeftButtonUp += OnOpenItem;
        IconList.PreviewMouseRightButtonDown += OnRightClick;
        IconList.PreviewMouseLeftButtonDown += OnItemMouseDown;
        IconList.PreviewMouseMove += OnDragStart;
        IconList.DragOver += OnDragOver;
        IconList.Drop += OnDrop;
        IconList.KeyDown += OnKeyDown;
        RefreshItems();
        NamesToggle.IsChecked = _tile.Block.ShowIconNames ?? _host.Settings.ShowIconNames;
        TransitionPreview.Source = _tile.CapturePreview();
        SetTransition(0);
    }

    /// <summary>按空间进度交接小盒快照与展开内容，避免跨窗口瞬间跳变。</summary>
    public void SetTransition(double progress)
    {
        var content = Math.Clamp((progress - 0.28) / 0.50, 0, 1);
        ContentGrid.Opacity = content;
        ContentGrid.IsHitTestVisible = progress >= 1;
        TransitionPreview.Opacity = 1 - content;
        TransitionPreview.Visibility = content >= 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>刷新仅加载 .lnk；普通文件属于异常内容，不作为链接目标展示。</summary>
    public void RefreshItems()
    {
        TitleText.Text = _tile.Block.Name;
        IReadOnlyList<IconItem> files;
        try { files = _host.Blocks.EnumerateItems(_tile.Block).Where(i => i.IsShortcut).ToList(); }
        catch { files = Array.Empty<IconItem>(); }
        Items.Clear();
        foreach (var item in files)
        {
            var model = new IconGridItem
            {
                Name = Path.GetFileNameWithoutExtension(item.Name),
                FullPath = item.FullPath,
                IsFolder = false
            };
            Items.Add(model);
            _ = LoadIconAsync(model);
        }
    }

    public void FocusItems() => IconList.Focus();

    private async Task LoadIconAsync(IconGridItem item)
    {
        var icon = await Task.Run(() => ShellIconService.GetIcon(item.FullPath, 48));
        if (icon != null && Items.Contains(item)) item.Icon = icon;
    }

    /// <summary>展开窗口固定 20% 磨砂叠色，文字与提示层保持主题对比度。</summary>
    public void ApplyTheme(bool dark, bool acrylic)
    {
        var palette = _host.ThemePalette;
        TintLayer.Background = acrylic ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(51, palette.WindowBackground.R,
                palette.WindowBackground.G, palette.WindowBackground.B));
        TitleText.Foreground = new SolidColorBrush(palette.TextPrimary);
        IconList.Foreground = new SolidColorBrush(palette.TextPrimary);
        RootBorder.BorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(105, 255, 255, 255)
            : Color.FromArgb(95, 0, 0, 0));
    }

    private void SetShowNames(bool show)
    {
        // 每次打开弹窗都要根据已保存状态重建可见性，不能只在状态发生变化时设置。
        NamesVisibility = show ? Visibility.Visible : Visibility.Collapsed;
        NamesHidden = !show;
        if (_tile.Block.ShowIconNames == show) return;
        _tile.Block.ShowIconNames = show;
        _host.PersistLayout();
    }

    private List<string> SelectedPaths() => IconList.SelectedItems.Cast<IconGridItem>()
        .Select(i => i.FullPath).Where(IsOwnedLink).ToList();

    private bool IsOwnedLink(string path) =>
        Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetDirectoryName(path), _tile.Block.FolderPath,
            StringComparison.OrdinalIgnoreCase);

    private void OnOpenItem(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers is not ModifierKeys.None) return;
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(IconList, source) is not ListBoxItem container ||
            container.DataContext is not IconGridItem item) return;
        ShellFileService.Open(item.FullPath);
        e.Handled = true;
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(IconList, source) is not ListBoxItem container ||
            container.DataContext is not IconGridItem item) return;
        if (!container.IsSelected)
        {
            IconList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        var point = PointToScreen(e.GetPosition(this));
        ShellContextMenuService.Show(_popup.Hwnd, SelectedPaths(), (int)point.X, (int)point.Y);
        RefreshItems();
        e.Handled = true;
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(IconList);
        if (Keyboard.Modifiers != ModifierKeys.None ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(IconList, source) is not ListBoxItem container ||
            container.IsSelected) return;
        IconList.SelectedItems.Clear();
        container.IsSelected = true;
    }

    private void OnDragStart(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(IconList);
        if (Math.Abs(position.X - _dragStart.X) < 7 &&
            Math.Abs(position.Y - _dragStart.Y) < 7) return;
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths.ToArray());
        data.SetData(ReorderFormat, _tile.Block.Id.ToString("D"));
        DragDrop.DoDragDrop(IconList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.None;
        else e.Effects = IsOwnDrag(e.Data) ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private bool IsOwnDrag(System.Windows.IDataObject data) =>
        data.GetData(ReorderFormat) is string id &&
        string.Equals(id, _tile.Block.Id.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
        data.GetData(DataFormats.FileDrop) is string[] paths &&
        paths.Length > 0 && paths.All(IsOwnedLink);

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (IsOwnDrag(e.Data))
        {
            Reorder(paths, e);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            _host.MoveIntoBlock(_tile, paths);
            e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void Reorder(string[] paths, DragEventArgs e)
    {
        var moving = Items.Where(i => paths.Contains(i.FullPath,
            StringComparer.OrdinalIgnoreCase)).ToList();
        if (moving.Count == 0) return;
        var remaining = Items.Except(moving).ToList();
        var target = e.OriginalSource is DependencyObject source
            ? (ItemsControl.ContainerFromElement(IconList, source) as ListBoxItem)?.DataContext as IconGridItem
            : null;
        var index = target == null ? remaining.Count : remaining.IndexOf(target);
        if (index < 0) index = remaining.Count;
        remaining.InsertRange(index, moving);
        Items.Clear();
        foreach (var item in remaining) Items.Add(item);
        _host.SetItemOrder(_tile, Items.Select(i => i.FullPath));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape) { _popup.CloseAnimated(); e.Handled = true; }
        else if (e.Key == Key.Enter)
        {
            foreach (var path in SelectedPaths()) ShellFileService.Open(path);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (!ShellFileService.RecycleDelete(_popup.Hwnd, SelectedPaths()))
                _host.NotifyError("删除快捷方式失败，请检查文件是否仍存在。");
            RefreshItems();
            e.Handled = true;
        }
        else if (e.Key == Key.A && ctrl) { IconList.SelectAll(); e.Handled = true; }
        else if ((e.Key == Key.C || e.Key == Key.X) && ctrl)
        {
            var paths = SelectedPaths();
            var collection = new StringCollection();
            collection.AddRange(paths.ToArray());
            var data = new DataObject();
            data.SetFileDropList(collection);
            if (e.Key == Key.X)
                data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 2, 0, 0, 0 }));
            Clipboard.SetDataObject(data, true);
            e.Handled = true;
        }
        else if (e.Key == Key.V && ctrl)
        {
            if (Clipboard.ContainsFileDropList())
                _host.MoveIntoBlock(_tile, Clipboard.GetFileDropList().Cast<string>());
            e.Handled = true;
        }
    }
}
