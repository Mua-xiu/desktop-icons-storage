using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;
using Button = System.Windows.Controls.Button;

namespace DesktopIconsStorage.App.Views;

/// <summary>链接筐展开窗口：完整快捷方式列表、名称开关和主题化悬浮提示。</summary>
public partial class LinkBasketPopupView : UserControl
{
    private const string ReorderFormat = "DesktopIconsStorage.LinkBasketReorderId";
    private readonly BlockWindow _tile;
    private readonly LinkBasketPopupWindow _popup;
    private readonly AppHost _host;
    private readonly ScaleTransform _transitionScale = new();
    private readonly List<IconGridItem> _allItems = new();
    private Point _dragStart;
    private int _pageSize = 32;
    private int _currentPage;

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
        IconList.SizeChanged += (_, _) => UpdatePageCapacity();
        Loaded += (_, _) => UpdatePageCapacity();
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
        ContentGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        ContentGrid.RenderTransform = _transitionScale;
        SetTransition(0);
    }

    /// <summary>2026-09-24：静止的弹窗只动画内容透明度和轻微缩放，避免毛玻璃移动拖影。</summary>
    public void SetTransition(double progress)
    {
        ContentGrid.BeginAnimation(UIElement.OpacityProperty, null);
        _transitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _transitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ContentGrid.Opacity = progress;
        ContentGrid.IsHitTestVisible = progress >= 1;
        _transitionScale.ScaleX = 0.97 + 0.03 * progress;
        _transitionScale.ScaleY = 0.97 + 0.03 * progress;
    }

    /// <summary>让 WPF 组合器完成开合动效，返回时长供窗口生命周期兜底使用。</summary>
    public int AnimateTransition(bool opening, Action completed)
    {
        var duration = opening ? 180 : 140;
        var easing = new CubicEase
        {
            EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn
        };
        ContentGrid.IsHitTestVisible = false;
        var opacity = new DoubleAnimation(opening ? 1 : 0,
            TimeSpan.FromMilliseconds(duration)) { EasingFunction = easing };
        opacity.Completed += (_, _) => completed();
        ContentGrid.BeginAnimation(UIElement.OpacityProperty, opacity);
        var scale = new DoubleAnimation(opening ? 1 : 0.98,
            TimeSpan.FromMilliseconds(duration)) { EasingFunction = easing };
        _transitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _transitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        return duration;
    }

    /// <summary>刷新仅加载 .lnk；普通文件属于异常内容，不作为链接目标展示。</summary>
    public void RefreshItems()
    {
        TitleText.Text = _tile.Block.Name;
        IReadOnlyList<IconItem> files;
        try { files = _host.Blocks.EnumerateItems(_tile.Block).Where(i => i.IsShortcut).ToList(); }
        catch { files = Array.Empty<IconItem>(); }
        _allItems.Clear();
        foreach (var item in files)
        {
            var model = new IconGridItem
            {
                Name = Path.GetFileNameWithoutExtension(item.Name),
                FullPath = item.FullPath,
                IsFolder = false
            };
            _allItems.Add(model);
            _ = LoadIconAsync(model);
        }
        RefreshVisiblePage();
    }

    public void FocusItems() => IconList.Focus();

    private async Task LoadIconAsync(IconGridItem item)
    {
        var icon = await Task.Run(() => ShellIconService.GetIcon(item.FullPath, 48));
        if (icon != null && _allItems.Contains(item)) item.Icon = icon;
    }

    /// <summary>根据窗口实际内容区计算每页容量，所有快捷方式只在分页间切换，不启用滚动条。</summary>
    private void UpdatePageCapacity()
    {
        if (IconList.ActualWidth <= 0 || IconList.ActualHeight <= 0) return;
        var columns = Math.Max(1, (int)Math.Floor(IconList.ActualWidth / 92));
        var rows = Math.Max(1, (int)Math.Floor(IconList.ActualHeight / 104));
        var pageSize = columns * rows;
        if (pageSize == _pageSize) return;
        _pageSize = pageSize;
        RefreshVisiblePage();
    }

    private int PageCount => Math.Max(1,
        (int)Math.Ceiling(_allItems.Count / (double)Math.Max(1, _pageSize)));

    /// <summary>只把当前页条目放入 ListBox，同时刷新底部圆点导航。</summary>
    private void RefreshVisiblePage()
    {
        _currentPage = Math.Clamp(_currentPage, 0, PageCount - 1);
        Items.Clear();
        foreach (var item in _allItems.Skip(_currentPage * _pageSize).Take(_pageSize))
            Items.Add(item);
        RefreshPageDots();
    }

    private void RefreshPageDots()
    {
        PageDots.Children.Clear();
        var count = PageCount;
        PageDots.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        for (var page = 0; page < count; page++)
        {
            var index = page;
            var dot = new Button
            {
                Style = (Style)FindResource("PageDot"),
                Background = new SolidColorBrush(page == _currentPage
                    ? _host.AccentColor : _host.ThemePalette.TrackOff),
                ToolTip = $"第 {page + 1} 页"
            };
            System.Windows.Automation.AutomationProperties.SetName(dot,
                $"第 {page + 1} 页");
            dot.Click += (_, _) => ChangePage(index);
            PageDots.Children.Add(dot);
        }
    }

    private void ChangePage(int page)
    {
        if (page < 0 || page >= PageCount || page == _currentPage) return;
        _currentPage = page;
        RefreshVisiblePage();
    }

    /// <summary>供弹窗级键盘处理调用，使焦点在页点或开关上时也能翻页。</summary>
    public bool TryMovePage(int delta)
    {
        if (PageCount <= 1) return false;
        ChangePage(Math.Clamp(_currentPage + delta, 0, PageCount - 1));
        return true;
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
        var pageStart = _currentPage * _pageSize;
        var remaining = Items.Except(moving).ToList();
        var target = e.OriginalSource is DependencyObject source
            ? (ItemsControl.ContainerFromElement(IconList, source) as ListBoxItem)?.DataContext as IconGridItem
            : null;
        var index = target == null ? remaining.Count : remaining.IndexOf(target);
        if (index < 0) index = remaining.Count;
        remaining.InsertRange(index, moving);
        for (var offset = 0; offset < remaining.Count; offset++)
            _allItems[pageStart + offset] = remaining[offset];
        _host.SetItemOrder(_tile, _allItems.Select(i => i.FullPath));
        RefreshVisiblePage();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Left)
        {
            ChangePage(_currentPage - 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Right)
        {
            ChangePage(_currentPage + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { _popup.CloseAnimated(); e.Handled = true; }
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
