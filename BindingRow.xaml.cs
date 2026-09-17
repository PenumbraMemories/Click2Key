using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Click2Key.Models;
using MouseBindingAction = Click2Key.Models.MouseAction;

namespace Click2Key.Controls;

public partial class BindingRow : UserControl
{
    private bool _ready;
    public BindingRule Rule { get; }
    public event EventHandler? RuleChanged;
    public event EventHandler? RemoveRequested;

    public BindingRow(BindingRule rule)
    {
        Rule = rule;
        InitializeComponent();
        var actions = Enum.GetValues<MouseBindingAction>().Select(a => new Option<MouseBindingAction>(a, ActionLabel(a))).ToArray();
        ActionCombo.ItemsSource = actions; ActionCombo.DisplayMemberPath = nameof(Option<MouseBindingAction>.Label); ActionCombo.SelectedValuePath = nameof(Option<MouseBindingAction>.Value);
        KeyButton.Content = FormatKey(rule.Trigger); ActionCombo.SelectedValue = rule.Action; EnabledToggle.IsChecked = rule.Enabled;
        HoldDelayBox.Text = rule.HoldDelayMs.ToString(); RepeatBox.Text = rule.RepeatMs.ToString(); WheelDeltaBox.Text = rule.WheelDelta.ToString();
        _ready = true;
    }

    private void Control_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (ActionCombo.SelectedValue is MouseBindingAction action) Rule.Action = action;
        Rule.Enabled = EnabledToggle.IsChecked == true;
        RuleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CaptureKey_Click(object sender, RoutedEventArgs e) { KeyButton.Content = "请按键…"; KeyButton.Focus(); }
    private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        Rule.Trigger = key; KeyButton.Content = FormatKey(key); e.Handled = true; RuleChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);

    private void Parameter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Parameter_Changed(sender, e); Keyboard.ClearFocus(); e.Handled = true; }
    }

    private void Parameter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Rule.HoldDelayMs = ParseClamped(HoldDelayBox, Rule.HoldDelayMs, 100, 2000);
        Rule.RepeatMs = ParseClamped(RepeatBox, Rule.RepeatMs, 20, 1000);
        Rule.WheelDelta = ParseClamped(WheelDeltaBox, Rule.WheelDelta, 30, 1200);
        RuleChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int ParseClamped(TextBox box, int fallback, int min, int max)
    {
        var value = int.TryParse(box.Text, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
        box.Text = value.ToString(); return value;
    }

    private static string ActionLabel(MouseBindingAction action) => action switch
    {
        MouseBindingAction.LeftClick => "鼠标左键", MouseBindingAction.RightClick => "鼠标右键", MouseBindingAction.MiddleClick => "鼠标中键",
        MouseBindingAction.WheelUp => "滚轮向上（长按连续）", MouseBindingAction.WheelDown => "滚轮向下（长按连续）",
        MouseBindingAction.HoldLeft => "左键（短按/长按）", MouseBindingAction.HoldRight => "右键（短按/长按）", _ => action.ToString()
    };
    private static string FormatKey(Key key) => key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString();
    private sealed record Option<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
