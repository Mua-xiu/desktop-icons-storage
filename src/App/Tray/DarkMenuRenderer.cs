using System.Drawing;
using System.Windows.Forms;
using GdiColor = System.Drawing.Color;

namespace DesktopIconsStorage.App.Tray;

/// <summary>托盘右键菜单渲染：深/浅两套 Win11 风格配色，圆角与留白由 TrayManager 配合处理。</summary>
public class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public bool Dark { get; }

    public ThemedMenuRenderer(bool dark) : base(new ThemedColorTable(dark))
    {
        Dark = dark;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Dark
            ? (e.Item.Selected ? GdiColor.White : GdiColor.FromArgb(0xE8, 0xE8, 0xE8))
            : GdiColor.FromArgb(0x1B, 0x1B, 0x1B);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // 主题色实心块 + 白色对勾
        var g = e.Graphics;
        var rect = e.ImageRectangle;
        var bg = new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
        using var brush = new SolidBrush(GdiColor.FromArgb(0x3D, 0x9B, 0xFF));
        using var pen = new Pen(GdiColor.White, 1.6f);
        g.FillRectangle(brush, bg);
        g.DrawLines(pen, new System.Drawing.Point[]
        {
            new System.Drawing.Point(bg.X + 3, bg.Y + bg.Height / 2),
            new System.Drawing.Point(bg.X + bg.Width / 2 - 1, bg.Bottom - 4),
            new System.Drawing.Point(bg.Right - 3, bg.Y + 3)
        });
    }
}

public class ThemedColorTable : ProfessionalColorTable
{
    private readonly GdiColor _bg;
    private readonly GdiColor _hover;
    private readonly GdiColor _border;

    public ThemedColorTable(bool dark)
    {
        if (dark)
        {
            _bg = GdiColor.FromArgb(0x2B, 0x2B, 0x2E);
            _hover = GdiColor.FromArgb(0x3F, 0x4A, 0x5C);
            _border = GdiColor.FromArgb(0x50, 0x50, 0x55);
        }
        else
        {
            // Win11 浅色：直接白底、浅灰悬停与边线
            _bg = GdiColor.White;
            _hover = GdiColor.FromArgb(0xE9, 0xE9, 0xEA);
            _border = GdiColor.FromArgb(0xDC, 0xDC, 0xDC);
        }
    }

    public override GdiColor MenuStripGradientBegin => _bg;
    public override GdiColor MenuStripGradientEnd => _bg;
    public override GdiColor ToolStripDropDownBackground => _bg;
    public override GdiColor ToolStripBorder => _border;
    public override GdiColor MenuBorder => _border;
    public override GdiColor MenuItemBorder => GdiColor.Transparent;
    public override GdiColor MenuItemSelected => _hover;
    public override GdiColor MenuItemSelectedGradientBegin => _hover;
    public override GdiColor MenuItemSelectedGradientEnd => _hover;
    public override GdiColor MenuItemPressedGradientBegin => _bg;
    public override GdiColor MenuItemPressedGradientMiddle => _bg;
    public override GdiColor MenuItemPressedGradientEnd => _bg;
    public override GdiColor ImageMarginGradientBegin => _bg;
    public override GdiColor ImageMarginGradientMiddle => _bg;
    public override GdiColor ImageMarginGradientEnd => _bg;
    public override GdiColor SeparatorDark => _border;
    public override GdiColor SeparatorLight => GdiColor.Transparent;
    public override GdiColor CheckBackground => GdiColor.FromArgb(0x3D, 0x9B, 0xFF);
    public override GdiColor CheckSelectedBackground => GdiColor.FromArgb(0x3D, 0x9B, 0xFF);
    public override GdiColor CheckPressedBackground => GdiColor.FromArgb(0x3D, 0x9B, 0xFF);
    public override GdiColor ButtonSelectedHighlight => _hover;
    public override GdiColor ButtonSelectedBorder => GdiColor.Transparent;
}
