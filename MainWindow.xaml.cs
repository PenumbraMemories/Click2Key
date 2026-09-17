using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using Click2Key.Controls;
using Click2Key.Models;
using Click2Key.Services;
using MouseBindingAction = Click2Key.Models.MouseAction;

namespace Click2Key;

public partial class MainWindow : Window
{
    private readonly InputEngine _engine = new();
    private readonly System.Drawing.Icon _enabledWindowIcon;
    private readonly System.Drawing.Icon _disabledWindowIcon;
    private readonly BitmapImage _enabledTaskbarImage;
    private readonly BitmapImage _disabledTaskbarImage;
    private readonly List<SwapRule> _swaps = [];
    private bool _capturingToggleKey;
    private bool _resumeAfterToggleCapture;
    private bool _darkMode;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private long _lastUiEventTicks;
    private readonly List<BindingRule> _rules =
    [
        new() { Trigger = Key.D1, Action = MouseBindingAction.HoldLeft },
        new() { Trigger = Key.D2, Action = MouseBindingAction.WheelUp },
        new() { Trigger = Key.D3, Action = MouseBindingAction.HoldRight },
        new() { Trigger = Key.D4, Action = MouseBindingAction.WheelDown }
    ];

    public MainWindow()
    {
        InitializeComponent();
        _enabledWindowIcon = LoadWindowIcon("taskbar-enabled.ico");
        _disabledWindowIcon = LoadWindowIcon("taskbar-disabled.ico");
        _enabledTaskbarImage = LoadTaskbarImage("taskbar-enabled.png");
        _disabledTaskbarImage = LoadTaskbarImage("taskbar-disabled.png");
        SourceInitialized += (_, _) => ApplyNativeWindowIcon(false);
        UpdateTaskbarIcon(false);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettingsNow(); };
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        };
        try
        {
            var settings = PortableSettings.Load();
            if (settings is not null)
            {
                _rules.Clear(); _rules.AddRange(settings.Rules);
                _swaps.AddRange(settings.Swaps);
                _engine.ToggleKey = settings.ToggleKey;
                _darkMode = settings.DarkMode;
                ToggleKeyButton.Content = FormatKey(settings.ToggleKey);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, $"配置读取失败，已使用默认设置：{ex.Message}", "配置提示", MessageBoxButton.OK, MessageBoxImage.Warning); }
        ApplyTheme();
        Loaded += (_, _) =>
        {
            RefreshRows();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => MainScroll.ScrollToTop());
            _ = LoadIllustrationsAsync();
            try { _engine.Start(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法启动全局按键监听", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        Closed += (_, _) => { _saveTimer.Stop(); SaveSettingsNow(); _engine.Dispose(); _enabledWindowIcon.Dispose(); _disabledWindowIcon.Dispose(); };
        _engine.RuleTriggered += rule =>
        {
            var now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref _lastUiEventTicks) < TimeSpan.FromMilliseconds(150).Ticks) return;
            Interlocked.Exchange(ref _lastUiEventTicks, now);
            Dispatcher.BeginInvoke(() => LastAction.Text = $"已触发：{ActionLabel(rule.Action)}  ·  {DateTime.Now:HH:mm:ss}");
        };
        _engine.ActiveChanged += active => Dispatcher.BeginInvoke(() => SetRunning(active));
    }

    private void RefreshRows()
    {
        var rows = _rules.Select(CreateRow).ToArray();
        RulesList.ItemsSource = rows;
        BindingCountText.Text = $"共 {_rules.Count} 个绑定";
        SwapList.ItemsSource = _swaps.Select(CreateSwapRow).ToArray();
        SwapCountText.Text = $"{_swaps.Count} 组";
        EmptySwap.Visibility = _swaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyRules();
    }

    private SwapRow CreateSwapRow(SwapRule rule)
    {
        var row = new SwapRow(rule);
        row.RuleChanged += (_, _) => ApplyRules();
        row.RemoveRequested += (_, _) => { _swaps.Remove(rule); RefreshRows(); };
        return row;
    }

    private void AddSwap_Click(object sender, RoutedEventArgs e)
    {
        var used = _swaps.SelectMany(s => new[] { s.First, s.Second }).Concat(_rules.Select(r => r.Trigger)).Append(_engine.ToggleKey).ToHashSet();
        var available = Enumerable.Range((int)Key.A, 26).Select(n => (Key)n).Where(k => !used.Contains(k)).Take(2).ToArray();
        if (available.Length < 2) { LastAction.Text = "没有可用的默认字母键，请先删除一组互换"; return; }
        _swaps.Add(new SwapRule { First = _swaps.Count == 0 && !used.Contains(Key.D) && !used.Contains(Key.F) ? Key.D : available[0], Second = _swaps.Count == 0 && !used.Contains(Key.D) && !used.Contains(Key.F) ? Key.F : available[1] });
        RefreshRows();
    }

    private BindingRow CreateRow(BindingRule rule)
    {
        var row = new BindingRow(rule);
        row.RuleChanged += (_, _) => ApplyRules();
        row.RemoveRequested += (_, _) => RemoveRule(rule);
        return row;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var used = _rules.Select(r => r.Trigger).ToHashSet();
        var trigger = Enumerable.Range((int)Key.F1, 12).Select(value => (Key)value).FirstOrDefault(key => !used.Contains(key));
        if (trigger == Key.None) trigger = Key.F12;
        _rules.Add(new BindingRule { Trigger = trigger, Action = MouseBindingAction.LeftClick });
        RefreshRows();
        LastAction.Text = $"已添加第 {_rules.Count} 条绑定";
    }

    private void RemoveRule(BindingRule rule)
    {
        if (_rules.Count == 1) { LastAction.Text = "至少需要保留一条绑定"; return; }
        _rules.Remove(rule); RefreshRows(); LastAction.Text = "绑定已删除";
    }

    private void ApplyRules()
    {
        var error = ValidateBindings();
        if (error is not null)
        {
            _engine.SetActive(false);
            _engine.SetRules([]);
            _engine.SetSwaps([]);
            LastAction.Text = error;
            SaveSettings();
            return;
        }
        _engine.SetRules(_rules);
        _engine.SetSwaps(_swaps);
        SaveSettings();
    }

    private void SaveSettings()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettingsNow()
    {
        try { PortableSettings.Save(new PortableSettings { ToggleKey = _engine.ToggleKey, DarkMode = _darkMode, Rules = _rules, Swaps = _swaps }); }
        catch (Exception ex) { LastAction.Text = $"配置保存失败：{ex.Message}"; }
    }

    private async Task LoadIllustrationsAsync()
    {
        try
        {
            var art = await Task.Run(() =>
            {
                var soft = LoadIllustration("catgirl-soft.png");
                var main = LoadIllustration("catgirl-mascot.png");
                var tech = LoadIllustration("catgirl-tech.png");
                return (soft, main, tech);
            });
            if (!IsLoaded) return;
            SoftPortrait.Source = art.soft;
            EmptyPortrait.Source = art.soft;
            MainPortrait.Source = art.main;
            TechPortrait.Source = art.tech;
        }
        catch (Exception ex) { LastAction.Text = $"插画加载失败：{ex.Message}"; }
    }

    private static BitmapImage LoadIllustration(string file)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/Assets/{file}", UriKind.Absolute);
        bitmap.DecodePixelWidth = 320;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private string? ValidateBindings()
    {
        var mouseKeys = _rules.Where(r => r.Enabled).Select(r => r.Trigger).ToArray();
        if (mouseKeys.Length != mouseKeys.Distinct().Count()) return "鼠标绑定存在重复触发键";
        var swapKeys = _swaps.Where(s => s.Enabled).SelectMany(s => new[] { s.First, s.Second }).ToArray();
        if (swapKeys.Contains(Key.None) || _swaps.Any(s => s.Enabled && s.First == s.Second) || swapKeys.Length != swapKeys.Distinct().Count()) return "互换按键重复或无效，请调整";
        if (mouseKeys.Concat(swapKeys).Contains(_engine.ToggleKey)) return "总开关键与绑定冲突，请调整";
        if (mouseKeys.Intersect(swapKeys).Any()) return "互换按键与鼠标绑定冲突，请调整";
        return null;
    }


    private void Toggle_Click(object sender, RoutedEventArgs e)
        => ToggleActive();

    private void ToggleActive()
    {
        try
        {
            if (_engine.IsActive) _engine.SetActive(false);
            else
            {
                var conflict = ValidateBindings();
                if (conflict is not null)
                {
                    MessageBox.Show(this, conflict, "绑定冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ApplyRules();
                if (!_engine.IsRunning) _engine.Start();
                _engine.SetActive(true);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法开启全局绑定", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ToggleKey_Click(object sender, RoutedEventArgs e)
    {
        _capturingToggleKey = true;
        _resumeAfterToggleCapture = _engine.IsActive;
        _engine.Stop();
        ToggleKeyButton.Content = "请按键…";
        ToggleKeyButton.Focus();
    }

    private void ToggleKey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingToggleKey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        _engine.ToggleKey = key;
        ToggleKeyButton.Content = FormatKey(key);
        ApplyRules();
        e.Handled = true;
        FinishToggleKeyCapture();
    }

    private void ToggleKey_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturingToggleKey) FinishToggleKeyCapture();
    }

    private void FinishToggleKeyCapture()
    {
        _capturingToggleKey = false;
        if (!_engine.IsRunning) _engine.Start();
        if (_resumeAfterToggleCapture && ValidateBindings() is null) _engine.SetActive(true);
        _resumeAfterToggleCapture = false;
    }

    private void SetRunning(bool running)
    {
        UpdateTaskbarIcon(running);
        StatusText.Text = running ? "键映生效中" : "已停止";
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#7188DC" : "#AAB8CD"));
        ToggleButton.Content = running ? "停止" : "启动";
        ToggleButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, running ? "PinkKeycapBrush" : "KeycapBrush");
        ToggleButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, running ? "PinkInkBrush" : "KeyInkBrush");
        LastAction.Text = running ? "映射已开启，最小化窗口后仍然有效" : "监听已停止";
    }

    private static BitmapImage LoadTaskbarImage(string file)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/Assets/{file}", UriKind.Absolute);
        bitmap.DecodePixelWidth = 256;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void UpdateTaskbarIcon(bool active)
    {
        Icon = active ? _enabledTaskbarImage : _disabledTaskbarImage;
        ApplyNativeWindowIcon(active);
    }

    private static System.Drawing.Icon LoadWindowIcon(string file)
    {
        var resource = Application.GetResourceStream(new Uri($"/Assets/{file}", UriKind.Relative))
            ?? throw new FileNotFoundException($"找不到任务栏图标：{file}");
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, new System.Drawing.Size(256, 256));
    }

    private void ApplyNativeWindowIcon(bool active)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero) return;
        var icon = active ? _enabledWindowIcon : _disabledWindowIcon;
        NativeMethods.SendMessage(handle, NativeMethods.WM_SETICON, nint.Zero, icon.Handle);
        NativeMethods.SendMessage(handle, NativeMethods.WM_SETICON, new nint(1), icon.Handle);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _darkMode = !_darkMode;
        ApplyTheme(animate: true);
        SaveSettings();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    internal void RestoreWindow()
    {
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyTheme(bool animate = false)
    {
        var colors = _darkMode
            ? new Dictionary<string, string> { ["CanvasBrush"] = "#191D2B", ["SurfaceBrush"] = "#E52A3045", ["InkBrush"] = "#F8F5FF", ["MutedBrush"] = "#D2D4E7", ["LineBrush"] = "#AA535A73", ["SoftBrush"] = "#D33A425C", ["HeroBrush"] = "#DB3B3957", ["BlueGlowBrush"] = "#485B83", ["PinkGlowBrush"] = "#78546B", ["AccentBrush"] = "#B9C7FF", ["PrimaryBrush"] = "#8196E3", ["FieldBrush"] = "#F1313A51", ["ThumbBrush"] = "#848EBC", ["KeycapBrush"] = "#465779", ["KeyInkBrush"] = "#D8E4FF", ["PinkKeycapBrush"] = "#65465F", ["PinkInkBrush"] = "#FFCCEB", ["PinkAccentBrush"] = "#F2AAD5" }
            : new Dictionary<string, string> { ["CanvasBrush"] = "#F5F3FC", ["SurfaceBrush"] = "#EFFFFFFF", ["InkBrush"] = "#29324F", ["MutedBrush"] = "#64708D", ["LineBrush"] = "#CCDED9F0", ["SoftBrush"] = "#DDEDEBFA", ["HeroBrush"] = "#E7E9E7FA", ["BlueGlowBrush"] = "#DDEBFF", ["PinkGlowBrush"] = "#FADDEB", ["AccentBrush"] = "#6576C3", ["PrimaryBrush"] = "#6578C6", ["FieldBrush"] = "#F9FCFBFF", ["ThumbBrush"] = "#B6BAD8", ["KeycapBrush"] = "#DDE7F2FF", ["KeyInkBrush"] = "#465FB0", ["PinkKeycapBrush"] = "#F9E4F0", ["PinkInkBrush"] = "#AA517F", ["PinkAccentBrush"] = "#B45990" };
        foreach (var (name, hex) in colors)
            Application.Current.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        if (animate)
            MainSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(160)));
        ThemeButton.Content = _darkMode ? "☀  浅色" : "☾  暗色";
    }

    private static string ActionLabel(MouseBindingAction action) => action switch
    {
        MouseBindingAction.LeftClick => "鼠标左键", MouseBindingAction.RightClick => "鼠标右键", MouseBindingAction.MiddleClick => "鼠标中键",
        MouseBindingAction.WheelUp => "滚轮向上", MouseBindingAction.WheelDown => "滚轮向下",
        MouseBindingAction.HoldLeft => "左键（短按点击/长按按住）", MouseBindingAction.HoldRight => "右键（短按点击/长按按住）", _ => action.ToString()
    };

    private static string FormatKey(Key key) => key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString();
}
