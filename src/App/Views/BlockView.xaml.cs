using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

/// <summary>收纳盒的视觉与交互：标题栏、图标网格、拖拽、折叠、重命名。</summary>
public partial class BlockView : UserControl
{
    private const string InternalReorderFormat = "DesktopIconsStorage.InternalReorderBlockId";

    private readonly BlockWindow _window;
    private readonly AppHost _host;

    public BlockItemsCollection Items { get; } = new();

    // ---- 供 ItemTemplate 绑定的外观依赖属性（ApplyTheme 时更新）----
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(BlockView), new PropertyMetadata(40.0));
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

    public static readonly DependencyProperty CellWidthProperty =
        DependencyProperty.Register(nameof(CellWidth), typeof(double), typeof(BlockView), new PropertyMetadata(74.0));
    public double CellWidth { get => (double)GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }

    public static readonly DependencyProperty NamesVisibilityProperty =
        DependencyProperty.Register(nameof(NamesVisibility), typeof(Visibility), typeof(BlockView), new PropertyMetadata(Visibility.Visible));
    public Visibility NamesVisibility { get => (Visibility)GetValue(NamesVisibilityProperty); set => SetValue(NamesVisibilityProperty, value); }

    public static readonly DependencyProperty TileBrushProperty =
        DependencyProperty.Register(nameof(TileBrush), typeof(Brush), typeof(BlockView));
    public Brush? TileBrush { get => (Brush?)GetValue(TileBrushProperty); set => SetValue(TileBrushProperty, value); }

    public static readonly DependencyProperty TileBorderBrushProperty =
        DependencyProperty.Register(nameof(TileBorderBrush), typeof(Brush), typeof(BlockView));
    public Brush? TileBorderBrush { get => (Brush?)GetValue(TileBorderBrushProperty); set => SetValue(TileBorderBrushProperty, value); }

    private bool _dark = true;
    private Point _dragStart;
    private bool _dragArmed;
    private bool _moving;
    private Point _moveStartCursor;
    private double _moveStartX, _moveStartY;
    private DispatcherTimer? _fadeOutTimer;

    public BlockView(BlockWindow window, AppHost host)
    {
        InitializeComponent();
        _window = window;
        _host = host;
        IconList.ItemsSource = Items;
        WireEvents();
        RefreshTitle();
        IconList.Loaded += (_, _) => UpdateCellSize();
        SurfaceRoot.SizeChanged += (_, _) => UpdateRoundedClip();

        // NOACTIVATE 窗口：点击不会自动激活/聚焦，需要程序化处理以支持快捷键与重命名
        RootBorder.PreviewMouseDown += (_, _) =>
        {
            DesktopEmbedService.ActivateWindow(_window.Hwnd);
            if (Mouse.DirectlyOver is DependencyObject d && IsDescendantOfIconList(d))
                IconList.Focus();
        };
    }

    /// <summary>
    /// WPF 的 Border 不会按 CornerRadius 裁剪子元素，因此给背景层补充真正的圆角几何裁剪。
    /// 原生窗口区域负责最外层命中范围，这里负责消除毛玻璃图层的四角漏色。
    /// </summary>
    private void UpdateRoundedClip()
    {
        if (SurfaceRoot.ActualWidth <= 0 || SurfaceRoot.ActualHeight <= 0) return;
        SurfaceRoot.Clip = new RectangleGeometry(
            new Rect(0, 0, SurfaceRoot.ActualWidth, SurfaceRoot.ActualHeight), 7, 7);
    }

    /// <summary>直接设置 WrapPanel 单元格尺寸（模板内绑定不可靠，改走代码）。</summary>
    private void UpdateCellSize()
    {
        var s = _host.Settings;
        var panel = FindVisualChild<WrapPanel>(IconList);
        if (panel != null)
        {
            panel.ItemWidth = s.IconSize + 34;
            // 名称隐藏时单元格收紧：上下左右间距一致，hover 不再有预留大空白
            panel.ItemHeight = EffectiveShowNames ? s.IconSize + 60 : s.IconSize + 20;
        }
    }

    /// <summary>最小宽度保留三列；高度允许缩到一行，名称开启后也能向上收紧。</summary>
    private (int Width, int Height) MinimumGridSize(double scale)
    {
        var settings = _host.Settings;
        var cellWidth = settings.IconSize + 34;
        var cellHeight = EffectiveShowNames ? settings.IconSize + 60 : settings.IconSize + 20;
        return (
            (int)((3 * cellWidth + 16) * scale),
            (int)((40 + cellHeight + 12) * scale));
    }

    /// <summary>图标规格或名称显示方式变化后，仅保证一行图标可见。</summary>
    private void EnsureMinimumGridSize()
    {
        var minimum = MinimumGridSize(DpiScale);
        var width = Math.Max((int)_window.Block.Width, minimum.Width);
        var height = Math.Max((int)_window.Block.Height, minimum.Height);
        if (width == (int)_window.Block.Width && height == (int)_window.Block.Height) return;
        _window.SetSizeScreen(width, height);
        _host.PersistLayout();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private bool IsDescendantOfIconList(DependencyObject d)
    {
        while (d != null)
        {
            if (d == IconList) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>本块名称显示的有效值：块级覆盖优先，未设置时跟随全局。</summary>
    private bool EffectiveShowNames =>
        _window.Block.ShowIconNames ?? _host.Settings.ShowIconNames;

    // ---------- 主题 ----------

    // ---------- 自绘毛玻璃 ----------

    /// <summary>设置模糊壁纸底层（null 时隐藏，退回纯色 tint）。</summary>
    public void SetFrostImage(System.Windows.Media.Imaging.BitmapSource? src)
    {
        if (src == null)
        {
            FrostImage.Source = null;
            FrostImage.Visibility = Visibility.Collapsed;
            return;
        }
        FrostImage.Source = src; // 源已是降采样图，放大即模糊
        FrostImage.Visibility = Visibility.Visible;
    }

    /// <summary>主题色叠加层；不透明度即"毛玻璃透明度"设计点。</summary>
    public void SetFrostTint(Color color, double alpha01)
    {
        var a = (byte)Math.Clamp((int)(alpha01 * 255), 0, 255);
        FrostTint.Background = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
    }

    // ---------- 主题 ----------

    public void ApplyTheme(bool dark, Color accent, ThemePalette palette, byte backdropAlpha, bool glassEnabled)
    {
        _dark = dark;
        var fgBrush = new SolidColorBrush(palette.TextPrimary);

        // 窗口本身全透明：视觉完全由 FrostImage + FrostTint 提供，圆角由 RootBorder 干净裁剪
        RootBorder.Background = Brushes.Transparent;
        TitleText.Foreground = fgBrush;
        IconList.Foreground = fgBrush;
        CountText.Foreground = new SolidColorBrush(
            dark ? Color.FromArgb(0xCC, 255, 255, 255) : Color.FromArgb(0xCC, 0, 0, 0));
        CountBadge.Background = new SolidColorBrush(dark
            ? Color.FromArgb(0x66, palette.ControlBackground.R, palette.ControlBackground.G, palette.ControlBackground.B)
            : Color.FromArgb(0x55, 0, 0, 0));
        TitleBar.Background = new SolidColorBrush(Color.FromArgb(
            dark ? (byte)0x70 : (byte)0x58,
            palette.CardBackground.R, palette.CardBackground.G, palette.CardBackground.B));
        RootBorder.BorderBrush = new SolidColorBrush(
            Color.FromArgb(glassEnabled
                    ? (dark ? (byte)0x3A : (byte)0x28)
                    : (dark ? (byte)0x72 : (byte)0xA0),
                palette.BorderSoft.R, palette.BorderSoft.G, palette.BorderSoft.B));

        // 图标瓦片 / 名称显隐 / 图标尺寸（参考 WitchDrawer 的可调处理）
        var s = _host.Settings;
        var ta = (byte)Math.Clamp((int)(s.IconTileOpacity * 255), 10, 220);
        TileBrush = new SolidColorBrush(dark
            ? Color.FromArgb(ta, 255, 255, 255)
            : Color.FromArgb((byte)(ta * 0.55), 0, 0, 0));
        TileBorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb((byte)(ta * 0.7), 255, 255, 255)
            : Color.FromArgb((byte)(ta * 0.4), 0, 0, 0));
        IconSize = s.IconSize;
        CellWidth = s.IconSize + 34;
        NamesVisibility = EffectiveShowNames ? Visibility.Visible : Visibility.Collapsed;
        UpdateCellSize();
        EnsureMinimumGridSize();

        // 毛玻璃的着色统一由 FrostTint 完成；根背景保持透明，避免圆角边缘形成亮色光晕。
        RootBorder.Background = Brushes.Transparent;
    }

    public void OnCollapsedChanged(bool collapsed)
    {
        // 折叠后禁止竖直方向缩放（横向仍可调整名称条宽度）
        RzS.IsEnabled = !collapsed;
        RzSE.IsEnabled = !collapsed;
        RzSW.IsEnabled = !collapsed;
        RzN.IsEnabled = !collapsed;
        RzNE.IsEnabled = !collapsed;
        RzNW.IsEnabled = !collapsed;
    }

    // ---------- 数据刷新 ----------

    public void RefreshTitle() => TitleText.Text = _window.Block.Name;

    public void RefreshItems()
    {
        List<Core.Models.IconItem> items;
        try { items = _host.Blocks.EnumerateItems(_window.Block).ToList(); }
        catch { items = new List<Core.Models.IconItem>(); }

        Items.Clear();
        foreach (var it in items)
        {
            Items.Add(new IconGridItem
            {
                Name = DisplayName(it.Name, it.IsShortcut),
                FullPath = it.FullPath,
                IsFolder = it.IsFolder
            });
        }
        CountText.Text = Items.Count.ToString();
        PersistNormalizedItemOrder();
        _ = LoadIconsAsync(Items.ToList());
    }

    /// <summary>移除已不存在的顺序项，并把新项目按当前显示顺序追加到布局数据。</summary>
    private void PersistNormalizedItemOrder()
    {
        var current = Items.Select(item => Path.GetFileName(item.FullPath)).ToList();
        if (_window.Block.ItemOrder.SequenceEqual(current, StringComparer.OrdinalIgnoreCase)) return;
        _host.SetItemOrder(_window, Items.Select(item => item.FullPath));
    }

    private static string DisplayName(string fileName, bool isShortcut) =>
        isShortcut ? Path.GetFileNameWithoutExtension(fileName) : fileName;

    private async Task LoadIconsAsync(List<IconGridItem> snapshot)
    {
        foreach (var g in snapshot)
        {
            var icon = await Task.Run(() => ShellIconService.GetIcon(g.FullPath, 48));
            if (icon != null)
            {
                try { Dispatcher.Invoke(() => g.Icon = icon); } catch { /* 窗口已销毁 */ }
            }
        }
    }

    // ---------- 事件接线 ----------

    private void WireEvents()
    {
        TitleBar.MouseLeftButtonDown += OnTitleBarMouseDown;
        TitleBar.MouseRightButtonDown += (_, _) =>
        {
            var menu = BuildBlockMenu();
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        };
        TitleEditor.KeyDown += OnTitleEditorKeyDown;
        TitleEditor.LostFocus += (_, _) => CommitRename();

        // 鼠标离开后启动自动淡出倒计时（WitchDrawer 式自动隐藏）
        RootBorder.MouseEnter += (_, _) => CancelAutoFade();
        RootBorder.MouseLeave += (_, _) => BeginAutoFadeCountdown();

        IconList.MouseDoubleClick += OnItemDoubleClick;
        IconList.PreviewMouseLeftButtonDown += OnItemMouseDown;
        IconList.PreviewMouseLeftButtonUp += OnItemMouseUp;
        IconList.PreviewMouseMove += OnItemMouseMove;
        IconList.DragOver += OnDragOver;
        IconList.Drop += OnDrop;
        IconList.PreviewMouseRightButtonDown += OnRightClick;
        IconList.KeyDown += OnKeyDown;
    }

    // ---------- 悬停按钮与自动淡出 ----------

    private static void FadeElement(UIElement element, double target)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(
            target, new Duration(TimeSpan.FromMilliseconds(140)));
        element.BeginAnimation(OpacityProperty, anim);
    }

    private void BeginAutoFadeCountdown()
    {
        if (!_host.Settings.AutoFadeEnabled || _window.Block.Collapsed) return;
        _fadeOutTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _fadeOutTimer.Stop();
        _fadeOutTimer.Tick += OnFadeOutTick;
        _fadeOutTimer.Start();
    }

    private void OnFadeOutTick(object? sender, EventArgs e)
    {
        _fadeOutTimer?.Stop();
        _fadeOutTimer!.Tick -= OnFadeOutTick;
        var target = Math.Clamp(_host.Settings.AutoFadeOpacity, 0.05, 0.5);
        FadeElement(IconList, target);
        FadeElement(TitleText, Math.Max(target, 0.4)); // 名称保留一定可读性
        FadeElement(CountBadge, target);
    }

    private void CancelAutoFade()
    {
        _fadeOutTimer?.Stop();
        FadeElement(IconList, 1);
        FadeElement(TitleText, 1);
        FadeElement(CountBadge, 1);
    }

    private static ListBoxItem? ItemFromEvent(RoutedEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return d as ListBoxItem;
    }

    private List<string> SelectedPaths() =>
        IconList.SelectedItems.Cast<IconGridItem>().Select(i => i.FullPath).ToList();

    // ---------- 标题栏：移动 / 双击折叠 ----------

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _host.ToggleCollapse(_window);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        _moving = true;
        _moveStartCursor = PointToScreen(e.GetPosition(this));
        _moveStartX = _window.Block.X;
        _moveStartY = _window.Block.Y;
        TitleBar.CaptureMouse();
        TitleBar.MouseMove += OnTitleBarMouseMove;
        TitleBar.MouseLeftButtonUp += OnTitleBarMouseUp;
        e.Handled = true;
    }

    private void OnTitleBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_moving) return;
        var cursor = PointToScreen(e.GetPosition(this));
        _window.SetPositionScreen(
            (int)(_moveStartX + cursor.X - _moveStartCursor.X),
            (int)(_moveStartY + cursor.Y - _moveStartCursor.Y));
    }

    private void OnTitleBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        _moving = false;
        TitleBar.ReleaseMouseCapture();
        TitleBar.MouseMove -= OnTitleBarMouseMove;
        TitleBar.MouseLeftButtonUp -= OnTitleBarMouseUp;
        _host.PersistLayout();
        _window.RefreshFrostBackground();
    }

    // ---------- 重命名（右键菜单入口；双击名称行 = 折叠/展开，走 TitleBar 处理） ----------

    private void BeginRename()
    {
        TitleEditor.Text = _window.Block.Name;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        // NOACTIVATE 窗口必须先程序化激活，键盘输入才会路由进来
        DesktopEmbedService.ActivateWindow(_window.Hwnd);
        TitleEditor.Focus();
        Keyboard.Focus(TitleEditor);
        TitleEditor.SelectAll();
    }

    private void OnTitleEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
    }

    private void CommitRename()
    {
        if (TitleEditor.Visibility != Visibility.Visible) return;
        var name = TitleEditor.Text.Trim();
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        if (name.Length > 0 && name != _window.Block.Name)
            _host.RenameBlock(_window, name);
        RefreshTitle();
        DesktopEmbedService.DeactivateToDesktop(); // 编辑结束，焦点还给桌面
    }

    private void CancelRename()
    {
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        DesktopEmbedService.DeactivateToDesktop();
    }

    // ---------- 块右键菜单 ----------

    private ContextMenu BuildBlockMenu()
    {
        var menu = new ContextMenu();

        // 名称显示：勾选状态为有效值；点击后写入块级覆盖（优先于全局设置）
        var showNames = new MenuItem
        {
            Header = "显示图标名称",
            IsCheckable = true,
            IsChecked = EffectiveShowNames
        };
        showNames.Click += (_, _) =>
        {
            _window.Block.ShowIconNames = !EffectiveShowNames;
            _host.PersistLayout();
            _window.ApplyBackdrop(); // 重新应用主题以刷新名称显隐与单元格高度
        };

        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) => BeginRename();
        var openFolder = new MenuItem { Header = "打开文件夹" };
        openFolder.Click += (_, _) => ShellFileService.Open(_window.Block.FolderPath);
        var delete = new MenuItem { Header = "删除收纳盒…" };
        delete.Click += (_, _) => _host.RequestDeleteBlock(_window);

        menu.Items.Add(showNames);
        menu.Items.Add(new Separator());
        menu.Items.Add(rename);
        menu.Items.Add(openFolder);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        return menu;
    }

    // ---------- 图标：打开 / 右键系统菜单 ----------

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemFromEvent(e) == null) return;
        foreach (var path in SelectedPaths())
            ShellFileService.Open(path);
        e.Handled = true;
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = ItemFromEvent(e);
        if (item != null)
        {
            if (!item.IsSelected)
            {
                IconList.SelectedItems.Clear();
                item.IsSelected = true;
            }
            var pt = PointToScreen(Mouse.GetPosition(this));
            ShellContextMenuService.Show(_window.Hwnd, SelectedPaths(), (int)pt.X, (int)pt.Y);
            RefreshItems();
        }
        else
        {
            var menu = BuildBlockMenu();
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
        e.Handled = true;
    }

    // ---------- 拖出到桌面 ----------

    private bool _preserveMultiSelect;
    private IconGridItem? _preserveItem;
    private bool _marqueeActive;
    private Point _marqueeStart;
    private readonly HashSet<IconGridItem> _marqueeBase = new();

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(IconList);
        var lbi = ItemFromEvent(e);
        _dragArmed = lbi != null;

        // 空白处按下左键 → 开始框选（滚动条区域除外）
        if (e.ChangedButton == MouseButton.Left && lbi == null && !IsOnScrollBar(e))
        {
            BeginMarquee(e);
            e.Handled = true;
            return;
        }

        // 按下已在多选中的项时抑制选择重置，保留多选用于整组拖出
        if (_dragArmed && lbi!.IsSelected && IconList.SelectedItems.Count > 1)
        {
            e.Handled = true;
            _preserveMultiSelect = true;
            _preserveItem = lbi.DataContext as IconGridItem;
        }
    }

    private static bool IsOnScrollBar(RoutedEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is ScrollBar) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    // ---------- 框选（资源管理器式 rubber-band 选择） ----------

    private void BeginMarquee(MouseButtonEventArgs e)
    {
        _marqueeActive = true;
        _marqueeStart = e.GetPosition(MarqueeLayer);
        _marqueeBase.Clear();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl + 框选 = 在现有选择上追加
            foreach (var item in IconList.SelectedItems.Cast<IconGridItem>())
                _marqueeBase.Add(item);
        }
        else
        {
            IconList.SelectedItems.Clear();
        }

        MarqueeRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(MarqueeRect, _marqueeStart.X);
        Canvas.SetTop(MarqueeRect, _marqueeStart.Y);
        MarqueeRect.Width = 0;
        MarqueeRect.Height = 0;

        IconList.CaptureMouse();
        IconList.MouseMove += OnMarqueeMove;
        IconList.MouseLeftButtonUp += OnMarqueeUp;
    }

    private void OnMarqueeMove(object sender, MouseEventArgs e)
    {
        if (!_marqueeActive) return;
        var pos = e.GetPosition(MarqueeLayer);
        var rect = new Rect(
            Math.Min(pos.X, _marqueeStart.X), Math.Min(pos.Y, _marqueeStart.Y),
            Math.Abs(pos.X - _marqueeStart.X), Math.Abs(pos.Y - _marqueeStart.Y));

        Canvas.SetLeft(MarqueeRect, rect.X);
        Canvas.SetTop(MarqueeRect, rect.Y);
        MarqueeRect.Width = rect.Width;
        MarqueeRect.Height = rect.Height;

        UpdateMarqueeSelection(rect);
    }

    private void UpdateMarqueeSelection(Rect rect)
    {
        var hit = new List<IconGridItem>();
        foreach (var item in Items)
        {
            if (IconList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                continue;
            var topLeft = container.TranslatePoint(new Point(0, 0), MarqueeLayer);
            var itemRect = new Rect(topLeft, container.RenderSize);
            if (rect.IntersectsWith(itemRect)) hit.Add(item);
        }

        IconList.SelectedItems.Clear();
        foreach (var item in _marqueeBase.Concat(hit).Distinct())
            IconList.SelectedItems.Add(item);
    }

    private void OnMarqueeUp(object sender, MouseButtonEventArgs e)
    {
        _marqueeActive = false;
        MarqueeRect.Visibility = Visibility.Collapsed;
        IconList.ReleaseMouseCapture();
        IconList.MouseMove -= OnMarqueeMove;
        IconList.MouseLeftButtonUp -= OnMarqueeUp;
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        // 未发生拖拽的纯点击：把多选收拢为当前项（常规列表行为）
        if (_preserveMultiSelect)
        {
            if (_preserveItem != null)
            {
                IconList.SelectedItems.Clear();
                IconList.SelectedItems.Add(_preserveItem);
            }
            _preserveMultiSelect = false;
            _preserveItem = null;
        }
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(IconList);
        if (Math.Abs(pos.X - _dragStart.X) < 7 && Math.Abs(pos.Y - _dragStart.Y) < 7) return;

        _dragArmed = false;
        _preserveMultiSelect = false;
        _preserveItem = null;
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths.ToArray());
        data.SetData(InternalReorderFormat, _window.Block.Id.ToString("D"));
        DragDrop.DoDragDrop(IconList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    // ---------- 从桌面拖入 ----------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (IsInternalReorder(e.Data))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (IsInternalReorder(e.Data))
        {
            ReorderItems(paths, e);
            e.Handled = true;
            return;
        }
        _host.MoveIntoBlock(_window, paths);
        e.Handled = true;
    }

    /// <summary>判断拖放是否来自当前收纳盒，防止外部数据伪装成内部排序。</summary>
    private bool IsInternalReorder(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent(InternalReorderFormat) || !data.GetDataPresent(DataFormats.FileDrop))
            return false;
        if (data.GetData(InternalReorderFormat) is not string id ||
            !string.Equals(id, _window.Block.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
            return false;

        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths is { Length: > 0 } && paths.All(path =>
            string.Equals(Path.GetDirectoryName(path), _window.Block.FolderPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把选中的一个或多个项目移动到落点前后，仅调整显示顺序，不改动磁盘文件。</summary>
    private void ReorderItems(IEnumerable<string> paths, DragEventArgs e)
    {
        var pathSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var movingItems = Items.Where(item => pathSet.Contains(item.FullPath)).ToList();
        if (movingItems.Count == 0) return;

        var targetContainer = ItemFromEvent(e);
        var targetItem = targetContainer?.DataContext as IconGridItem;
        if (targetItem != null && pathSet.Contains(targetItem.FullPath)) return;

        // 文件夹单元格中央区域代表“移入文件夹”；左右两侧仍用于手动排序。
        if (targetItem is { IsFolder: true } && targetContainer != null &&
            IsFolderDropZone(targetContainer, e))
        {
            ConfirmMoveIntoFolder(movingItems, targetItem);
            return;
        }

        var desired = Items.Where(item => !pathSet.Contains(item.FullPath)).ToList();
        var insertIndex = desired.Count;
        if (targetItem != null)
        {
            insertIndex = desired.IndexOf(targetItem);
            if (insertIndex < 0) insertIndex = desired.Count;
            else if (targetContainer != null &&
                     e.GetPosition(targetContainer).X > targetContainer.ActualWidth / 2)
                insertIndex++;
        }
        desired.InsertRange(insertIndex, movingItems);

        // 使用 Move 而不是清空重建，保留已加载图标和选择状态，避免排序时闪烁。
        for (var index = 0; index < desired.Count; index++)
        {
            var currentIndex = Items.IndexOf(desired[index]);
            if (currentIndex != index) Items.Move(currentIndex, index);
        }
        _host.SetItemOrder(_window, Items.Select(item => item.FullPath));
    }

    /// <summary>文件夹单元格中央 50% 为移入热区，两侧保留为前后排序热区。</summary>
    private static bool IsFolderDropZone(ListBoxItem container, DragEventArgs e)
    {
        var position = e.GetPosition(container);
        return position.X >= container.ActualWidth * 0.25 &&
               position.X <= container.ActualWidth * 0.75;
    }

    /// <summary>实际改变磁盘目录前进行二次确认，默认选择“否”避免误操作。</summary>
    private void ConfirmMoveIntoFolder(IReadOnlyCollection<IconGridItem> movingItems, IconGridItem targetFolder)
    {
        var result = MessageBox.Show(
            $"确定将选中的 {movingItems.Count} 个项目移动到文件夹“{targetFolder.Name}”中吗？\n\n" +
            "此操作会改变这些文件在磁盘上的实际位置。",
            "确认移动到文件夹",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        _host.MoveItemsIntoFolder(
            _window,
            movingItems.Select(item => item.FullPath),
            targetFolder.FullPath);
    }

    // ---------- 键盘与剪贴板 ----------

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Enter:
                foreach (var path in SelectedPaths()) ShellFileService.Open(path);
                e.Handled = true;
                break;
            case Key.Delete:
                ShellFileService.RecycleDelete(_window.Hwnd, SelectedPaths());
                RefreshItems();
                e.Handled = true;
                break;
            case Key.A when ctrl:
                IconList.SelectAll();
                e.Handled = true;
                break;
            case Key.C when ctrl:
                CopySelection(cut: false);
                e.Handled = true;
                break;
            case Key.X when ctrl:
                CopySelection(cut: true);
                e.Handled = true;
                break;
            case Key.V when ctrl:
                PasteIntoBlock();
                e.Handled = true;
                break;
        }
    }

    private void CopySelection(bool cut)
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var collection = new System.Collections.Specialized.StringCollection();
        collection.AddRange(paths.ToArray());
        var data = new DataObject();
        data.SetFileDropList(collection);
        if (cut)
            data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 2, 0, 0, 0 }));
        Clipboard.SetDataObject(data, true);
    }

    private void PasteIntoBlock()
    {
        if (!Clipboard.ContainsFileDropList()) return;
        var paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
        if (paths.Length == 0) return;

        var cut = false;
        try
        {
            if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length > 0 && ms.ReadByte() == 2)
                cut = true;
        }
        catch { /* 无 DropEffect 视为复制 */ }

        try
        {
            if (cut)
            {
                var srcList = paths
                    .Where(p => File.Exists(p) || Directory.Exists(p))
                    .Select(p => (Path: p, IsDir: Directory.Exists(p)))
                    .ToList();
                var movedPaths = _host.Blocks.MoveInto(_window.Block, srcList.Select(x => x.Path));
                ShellNotifyService.NotifyMoved(srcList.Zip(movedPaths,
                    (source, destination) => (source.Path, source.IsDir, destination)));
            }
            else
            {
                _host.Blocks.CopyInto(_window.Block, paths);
            }
        }
        catch (Exception ex)
        {
            _host.NotifyError($"粘贴失败：{ex.Message}");
        }
        _window.RefreshViewAfterInternalChange();
    }

    // ---------- 缩放（四边 + 四角，八向自由拉伸） ----------

    private double DpiScale =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

    private void OnResizeThumb(object sender, DragDeltaEventArgs e)
    {
        var dir = ((Thumb)sender).Tag as string ?? "";
        var scale = DpiScale;
        var dx = (int)(e.HorizontalChange * scale);
        var dy = (int)(e.VerticalChange * scale);

        var x = (int)_window.Block.X;
        var y = (int)_window.Block.Y;
        var w = (int)_window.Block.Width;
        var h = (int)_window.Block.Height;

        switch (dir)
        {
            case "E": w += dx; break;
            case "S": h += dy; break;
            case "W": x += dx; w -= dx; break;
            case "N": y += dy; h -= dy; break;
            case "SE": w += dx; h += dy; break;
            case "SW": x += dx; w -= dx; h += dy; break;
            case "NE": y += dy; h -= dy; w += dx; break;
            case "NW": x += dx; w -= dx; y += dy; h -= dy; break;
        }

        var minimum = MinimumGridSize(scale);
        var minW = minimum.Width;
        var minH = minimum.Height;
        // 左边/上边拉伸触碰最小尺寸时保持对角位置不动
        if (w < minW) { if (dir.Contains('W')) x -= minW - w; w = minW; }
        if (h < minH) { if (dir.Contains('N')) y -= minH - h; h = minH; }

        // 限制到当前显示器工作区内，避免再次把缩放边拖到屏幕外后无法收回。
        var area = DesktopEmbedService.GetNearestMonitorWorkArea(x, y, w, h);
        var edgeGap = Math.Max((int)(12 * scale), 12);
        var maxW = Math.Max(minW, area.W - edgeGap * 2);
        var maxH = Math.Max(minH, area.H - edgeGap * 2);
        if (w > maxW) { if (dir.Contains('W')) x += w - maxW; w = maxW; }
        if (h > maxH) { if (dir.Contains('N')) y += h - maxH; h = maxH; }

        _window.SetBoundsScreen(x, y, w, h);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        _host.PersistLayout();
        _window.RefreshFrostBackground();
    }
}
