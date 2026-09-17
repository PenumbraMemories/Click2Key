using System.Windows.Input;

namespace Click2Key.Models;

public enum MouseAction
{
    LeftClick, RightClick, MiddleClick, WheelUp, WheelDown, HoldLeft, HoldRight
}

public sealed class BindingRule
{
    public Key Trigger { get; set; } = Key.F8;
    public MouseAction Action { get; set; } = MouseAction.LeftClick;
    public int HoldDelayMs { get; set; } = 320;
    public int RepeatMs { get; set; } = 70;
    public int WheelDelta { get; set; } = 120;
    public bool Enabled { get; set; } = true;
}
