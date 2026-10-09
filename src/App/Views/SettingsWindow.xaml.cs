using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopIconsStorage.App.Helpers;
using DesktopIconsStorage.Platform.Services;

namespace DesktopIconsStorage.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private bool _loaded;
    private bool _allowCloseWithoutPrompt;
    private bool _closePromptPending;

    private sealed class NavEntry
    {
        public required string Icon { get; init; }
        public required string Title { get; init; }
    }

    public SettingsWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;

        NavList.ItemsSource = new[]
        {
            new NavEntry { Icon = "⚙", Title = "常规" },
            new NavEntry { Icon = "🎨", Title = "外观" },
            new NavEntry { Icon = "🖼", Title = "图标" },
            new NavEntry { Icon = "👁", Title = "自动隐藏" },
            new NavEntry { Icon = "📁", Title = "存储与数据" }
        };
        NavList.SelectionChanged += (_, _) => SwitchPage(NavList.SelectedIndex);

        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyNativeWindowTheme();
        Closing += OnClosingPrompt;
        // 系统主题变化时实时重刷（无需关闭重开窗口）
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) =>
        Dispatcher.Invoke(ApplyThemeColors);

    private void SwitchPage(int index)
    {
        PageGeneral.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageIcons.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageAutoHide.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageStorage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 关闭设置窗口时按用户设置处理：退出应用 / 隐藏到托盘 / 每次询问并记住选择。
    /// 注意：Closing 事件内禁止调用 ShowDialog/Close（会抛 InvalidOperationException），
    /// 必须用 BeginInvoke 延迟到关闭流程结束后执行。
    /// </summary>
    private void OnClosingPrompt(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowCloseWithoutPrompt) return;

        switch (_host.Settings.CloseBehavior)
        {
            case "hide":
                return; // 直接关闭窗口，应用继续在托盘运行
            case "exit":
                _host.ExitApp();
                return;
        }

        // ask：取消本次关闭，延迟到关闭流程结束后再弹窗询问
        e.Cancel = true;
        if (_closePromptPending) return;
        _closePromptPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var dlg = new CloseAskDialog(_host.IsDarkTheme) { Owner = this };
                if (dlg.ShowDialog() != true) return;

                if (dlg.Remember)
                {
                    _host.Settings.CloseBehavior =
                        dlg.Choice == CloseAskDialog.CloseChoice.Exit ? "exit" : "hide";
                    _host.ApplySettings();
                }

                // 即使不记住选择，本次关闭也必须直接通过，避免再次进入 ask 分支循环弹窗。
                _allowCloseWithoutPrompt = true;
                if (dlg.Choice == CloseAskDialog.CloseChoice.Exit)
                    _host.ExitApp();
                else
                    Close();
            }
            finally
            {
                if (!_allowCloseWithoutPrompt)
                    _closePromptPending = false;
            }
        }));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyThemeColors();
        ApplyThemeIcon();

        AutoStartBox.IsChecked = _host.Settings.AutoStart;
        CloseBehaviorCombo.SelectedIndex = _host.Settings.CloseBehavior switch
        {
            "hide" => 1,
            "exit" => 2,
            _ => 0
        };
        BlurBox.IsChecked = _host.Settings.BlurEnabled;
        OpacitySlider.Value = _host.Settings.BackdropOpacity;
        OpacityText.Text = $"{(int)(_host.Settings.BackdropOpacity * 100)}%";
        StoragePathText.Text = _host.Settings.StorageRoot;
        ThemeCombo.SelectedIndex = _host.Settings.ThemeMode switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        IconSizeCombo.SelectedIndex = _host.Settings.IconSize switch
        {
            32 => 0,
            48 => 2,
            _ => 1
        };
        ShowNamesBox.IsChecked = _host.Settings.ShowIconNames;
        TileOpacitySlider.Value = _host.Settings.IconTileOpacity;
        TileOpacityText.Text = $"{(int)(_host.Settings.IconTileOpacity * 100)}%";
        AutoFadeBox.IsChecked = _host.Settings.AutoFadeEnabled;
        FadeOpacitySlider.Value = _host.Settings.AutoFadeOpacity;
        FadeOpacityText.Text = $"{(int)(_host.Settings.AutoFadeOpacity * 100)}%";

        AutoStartBox.Click += (_, _) => _host.SetAutoStart(AutoStartBox.IsChecked == true);
        CloseBehaviorCombo.SelectionChanged += (_, _) =>
        {
            if (!_loaded) return;
            if (CloseBehaviorCombo.SelectedItem is ComboBoxItem item && item.Tag is string behavior)
            {
                _host.Settings.CloseBehavior = behavior;
                _host.ApplySettings();
            }
        };
        BlurBox.Click += (_, _) =>
        {
            _host.Settings.BlurEnabled = BlurBox.IsChecked == true;
            _host.ApplySettings();
        };
        ThemeCombo.SelectionChanged += (_, _) =>
        {
            if (!_loaded) return;
            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            {
                _host.Settings.ThemeMode = mode;
                _host.ApplySettings();
                ApplyThemeColors(); // 当前窗口立即换肤，无需关闭重开
            }
        };
        OpacitySlider.ValueChanged += (_, _) =>
        {
            if (!_loaded) return;
            _host.Settings.BackdropOpacity = OpacitySlider.Value;
            OpacityText.Text = $"{(int)(OpacitySlider.Value * 100)}%";
            _host.ApplySettings();
        };
        IconSizeCombo.SelectionChanged += (_, _) =>
        {
            if (!_loaded) return;
            if (IconSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string size &&
                int.TryParse(size, out var px))
            {
                _host.Settings.IconSize = px;
                _host.ApplySettings();
            }
        };
        ShowNamesBox.Click += (_, _) =>
        {
            _host.Settings.ShowIconNames = ShowNamesBox.IsChecked == true;
            _host.ApplySettings();
        };
        TileOpacitySlider.ValueChanged += (_, _) =>
        {
            if (!_loaded) return;
            _host.Settings.IconTileOpacity = TileOpacitySlider.Value;
            TileOpacityText.Text = $"{(int)(TileOpacitySlider.Value * 100)}%";
            _host.ApplySettings();
        };
        AutoFadeBox.Click += (_, _) =>
        {
            _host.Settings.AutoFadeEnabled = AutoFadeBox.IsChecked == true;
            _host.ApplySettings();
        };
        FadeOpacitySlider.ValueChanged += (_, _) =>
        {
            if (!_loaded) return;
            _host.Settings.AutoFadeOpacity = FadeOpacitySlider.Value;
            FadeOpacityText.Text = $"{(int)(FadeOpacitySlider.Value * 100)}%";
            _host.ApplySettings();
        };
        OpenStorageButton.Click += (_, _) => _host.OpenStorageRoot();
        RestoreAllButton.Click += (_, _) => _host.RestoreAllWithConfirm();

        _loaded = true;
        RefreshModeConstraints();
    }

    /// <summary>有链接筐时固定全局图标大小，保证展开窗口分页格数稳定。</summary>
    public void RefreshModeConstraints()
    {
        var hasLinkBasket = _host.Blocks.Blocks.Any(block => block.IsLink);
        IconSizeCombo.IsEnabled = !hasLinkBasket;
        IconSizeLockNote.Visibility = hasLinkBasket ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>按系统主题填充笔刷（Win11 设置应用配色：深墨灰底 / 浅浅灰底白卡片）。</summary>
    private void ApplyThemeColors()
    {
        ThemeResourceManager.Apply(_host.ThemePalette, _host.AccentColor);
        ApplyNativeWindowTheme();
    }

    /// <summary>让系统标题栏跟随应用内主题，而不是只跟随 Windows 当前主题。</summary>
    private void ApplyNativeWindowTheme()
    {
        if (PresentationSource.FromVisual(this) == null) return;
        BackdropService.ApplyWindowTheme(new WindowInteropHelper(this).Handle, _host.IsDarkTheme);
    }

    /// <summary>设置窗口图标与当前应用主题同步。</summary>
    public void ApplyThemeIcon()
    {
        try
        {
            var name = _host.IsDarkTheme ? "app-dark.ico" : "app-light.ico";
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute));
        }
        catch { /* 图标切换失败不影响设置窗口 */ }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
