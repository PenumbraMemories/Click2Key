using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Click2Key.Models;

namespace Click2Key.Controls;

public partial class SwapRow : UserControl
{
    private bool _ready;
    private Button? _capturing;
    public SwapRule Rule { get; }
    public event EventHandler? RuleChanged;
    public event EventHandler? RemoveRequested;

    public SwapRow(SwapRule rule)
    {
        Rule = rule;
        InitializeComponent();
        FirstButton.Content = Format(rule.First);
        SecondButton.Content = Format(rule.Second);
        EnabledToggle.IsChecked = rule.Enabled;
        _ready = true;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        _capturing = (Button)sender;
        _capturing.Content = "请按键…";
        _capturing.Focus();
    }

    private void Capture_KeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing != sender) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (sender == FirstButton) Rule.First = key; else Rule.Second = key;
        ((Button)sender).Content = Format(key);
        _capturing = null;
        e.Handled = true;
        RuleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Capture_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturing != sender) return;
        ((Button)sender).Content = Format(sender == FirstButton ? Rule.First : Rule.Second);
        _capturing = null;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Rule.Enabled = EnabledToggle.IsChecked == true;
        RuleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
    private static string Format(Key key) => key == Key.Back ? "Backspace" : key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString();
}
