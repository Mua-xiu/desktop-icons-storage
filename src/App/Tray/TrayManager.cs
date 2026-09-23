using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopIconsStorage.App.Helpers;

namespace DesktopIconsStorage.App.Tray;

/// <summary>系统托盘（WinForms NotifyIcon）：应用图标 + 主题化圆角菜单。</summary>
public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;

    public TrayManager(AppHost host)
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "DesktopIconsStorage",
            Icon = LoadAppIcon(),
            Visible = true
        };

        _menu = new ContextMenuStrip
        {
            Renderer = new ThemedMenuRenderer(host.IsDarkTheme),
            Font = new Font("Microsoft YaHei UI", 9f),
            ShowImageMargin = false,
            Padding = new Padding(8, 12, 8, 12) // 菜单内边距
        };

        ToolStripMenuItem Add(string text, Action action)
        {
            var item = new ToolStripMenuItem(text, null, (_, _) => action())
            {
                Margin = new Padding(4, 5, 4, 5) // 菜单项上下间距
            };
            _menu.Items.Add(item);
            return item;
        }

        Add("新建收纳块", host.NewBlock);
        Add("折叠全部", () => host.SetAllCollapsed(true));
        Add("展开全部", () => host.SetAllCollapsed(false));
        _menu.Items.Add(new ToolStripSeparator());

        var autostart = new ToolStripMenuItem("开机自启")
        {
            Checked = host.Settings.AutoStart,
            CheckOnClick = true,
            Margin = new Padding(4, 5, 4, 5)
        };
        autostart.CheckedChanged += (_, _) => host.SetAutoStart(autostart.Checked);
        _menu.Items.Add(autostart);

        Add("设置…", host.OpenSettings);
        _menu.Items.Add(new ToolStripSeparator());
        Add("一键全部还原", host.RestoreAllWithConfirm);
        Add("退出", host.ExitApp);

        // 弹出时：跟随当前主题重建渲染器 + 裁圆角
        _menu.Opening += (_, _) =>
        {
            if (_menu.Renderer is not ThemedMenuRenderer r || r.Dark != host.IsDarkTheme)
                _menu.Renderer = new ThemedMenuRenderer(host.IsDarkTheme);
            TryRoundMenu();
        };

        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.DoubleClick += (_, _) => host.OpenSettings();
    }

    private void TryRoundMenu()
    {
        try
        {
            if (_menu.Handle == IntPtr.Zero) return;
            var rgn = CreateRoundRectRgn(0, 0, _menu.Width, _menu.Height, 12, 12);
            SetWindowRgn(_menu.Handle, rgn, true);
        }
        catch { /* 圆角失败不影响功能 */ }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    /// <summary>从打包资源加载应用图标。</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute))?.Stream;
            if (stream != null) return new Icon(stream);
        }
        catch { /* 回退到程序图标 */ }
        return SystemIcons.Application;
    }

    public void Balloon(string text) =>
        _notifyIcon.ShowBalloonTip(2500, "DesktopIconsStorage", text, ToolTipIcon.Info);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
