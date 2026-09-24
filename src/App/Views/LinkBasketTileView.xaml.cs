using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Core.Models;
using DesktopIconsStorage.Platform.Services;
using Image = System.Windows.Controls.Image;

namespace DesktopIconsStorage.App.Views;

/// <summary>链接筐桌面小盒：只读图标预览、移动、规格菜单与打开弹窗。</summary>
public partial class LinkBasketTileView : UserControl
{
    private readonly BlockWindow _window;
    private readonly AppHost _host;
    private Point _dragStart;
    private double _windowStartX;
    private double _windowStartY;
    private bool _pointerDown;
    private bool _moved;

    public LinkBasketTileView(BlockWindow window, AppHost host)
    {
        InitializeComponent();
        _window = window;
        _host = host;
        TileRoot.PreviewMouseLeftButtonDown += OnPointerDown;
        TileRoot.PreviewMouseMove += OnPointerMove;
        TileRoot.PreviewMouseLeftButtonUp += OnPointerUp;
        TileRoot.LostMouseCapture += (_, _) => _pointerDown = false;
        TileRoot.MouseRightButtonUp += OnRightClick;
        TileRoot.DragOver += OnDragOver;
        TileRoot.Drop += OnDrop;
        TitleEditor.KeyDown += OnEditorKeyDown;
        TitleEditor.LostFocus += (_, _) => CommitRename();
        RefreshItems();
    }

    /// <summary>目录变更时只取前 N 个快捷方式图标，小盒从不生成名称标签或提示。</summary>
    public void RefreshItems()
    {
        TitleText.Text = _window.Block.Name;
        PreviewGrid.Rows = Math.Clamp(_window.Block.PreviewRows, 2, 4);
        PreviewGrid.Columns = Math.Clamp(_window.Block.PreviewColumns, 2, 4);
        PreviewGrid.Children.Clear();
        IReadOnlyList<IconItem> items;
        try { items = _host.Blocks.EnumerateItems(_window.Block).Where(i => i.IsShortcut).ToList(); }
        catch { items = Array.Empty<IconItem>(); }
        var slots = PreviewGrid.Rows * PreviewGrid.Columns;
        MoreBadge.Visibility = items.Count > slots ? Visibility.Visible : Visibility.Collapsed;
        MoreCount.Text = $"+{Math.Max(0, items.Count - slots)}";
        foreach (var item in items.Take(slots))
        {
            var icon = new Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4),
                IsHitTestVisible = false
            };
            PreviewGrid.Children.Add(icon);
            _ = LoadIconAsync(item.FullPath, icon);
        }
    }

    private async Task LoadIconAsync(string path, Image image)
    {
        var icon = await Task.Run(() => ShellIconService.GetIcon(path, 48));
        if (icon != null && PreviewGrid.Children.Contains(image)) image.Source = icon;
    }

    /// <summary>过渡动画只拍摄应用自身小盒，不读取或保存桌面图标文件。</summary>
    public BitmapSource? CapturePreview()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return null;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>小盒使用固定 10% 背景叠色，图标和文字保持完整不透明度。</summary>
    public void ApplyTheme(bool acrylic)
    {
        var tint = Color.FromRgb(0x70, 0x70, 0x70);
        TintLayer.Background = acrylic ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(26, tint.R, tint.G, tint.B));
        // 小盒固定透明背景会叠在任意壁纸上，标题不能仅按应用主题选黑或白。
        TitleText.Foreground = Brushes.White;
        TitleText.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 4,
            ShadowDepth = 0,
            Opacity = 0.95
        };
        TileBorder.BorderBrush = new SolidColorBrush(
            Color.FromArgb(110, 255, 255, 255));
    }

    /// <summary>整个小盒都是拖动热区；按下位置与窗口位置一起记录，避免移动时累积误差。</summary>
    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (TitleEditor.Visibility == Visibility.Visible) return;
        _dragStart = PointToScreen(e.GetPosition(this));
        _windowStartX = _window.Block.X;
        _windowStartY = _window.Block.Y;
        _pointerDown = true;
        _moved = false;
        TileRoot.CaptureMouse();
        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
        var position = PointToScreen(e.GetPosition(this));
        var dx = position.X - _dragStart.X;
        var dy = position.Y - _dragStart.Y;
        if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 5) return;
        _moved = true;
        _window.SetPositionScreen((int)Math.Round(_windowStartX + dx),
            (int)Math.Round(_windowStartY + dy));
        e.Handled = true;
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;
        _pointerDown = false;
        TileRoot.ReleaseMouseCapture();
        if (_moved) _host.PersistLayout();
        else _window.OpenLinkPopup();
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            _host.MoveIntoBlock(_window, paths);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) => BeginRename();
        menu.Items.Add(rename);
        var sizes = new MenuItem { Header = "设置预览规格" };
        foreach (var rows in new[] { 2, 3, 4 })
        foreach (var columns in new[] { 2, 3, 4 })
        {
            var size = new MenuItem
            {
                Header = $"{rows} × {columns}",
                IsCheckable = true,
                IsChecked = rows == _window.Block.PreviewRows &&
                            columns == _window.Block.PreviewColumns
            };
            size.Click += (_, _) => _host.SetLinkPreviewSize(_window, rows, columns);
            sizes.Items.Add(size);
        }
        menu.Items.Add(sizes);
        var openFolder = new MenuItem { Header = "打开收纳目录" };
        openFolder.Click += (_, _) => ShellFileService.Open(_window.Block.FolderPath);
        menu.Items.Add(openFolder);
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "删除收纳筐…" };
        delete.Click += (_, _) => _host.RequestDeleteBlock(_window);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void BeginRename()
    {
        TitleEditor.Text = _window.Block.Name;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        DesktopEmbedService.ActivateWindow(_window.Hwnd);
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        if (e.Key == Key.Escape)
        {
            TitleEditor.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void CommitRename()
    {
        if (TitleEditor.Visibility != Visibility.Visible) return;
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        var name = TitleEditor.Text.Trim();
        if (name.Length > 0 && name != _window.Block.Name)
            _host.RenameBlock(_window, name);
        TitleText.Text = _window.Block.Name;
    }
}
