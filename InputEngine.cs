using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Click2Key.Models;
using MouseBindingAction = Click2Key.Models.MouseAction;

namespace Click2Key.Services;

public sealed class InputEngine : IDisposable
{
    private readonly NativeMethods.HookProc _callback;
    private readonly ConcurrentDictionary<Key, PressState> _pressed = new();
    private nint _hook;
    private IReadOnlyList<BindingRule> _rules = [];
    private IReadOnlyDictionary<Key, Key> _swaps = new Dictionary<Key, Key>();
    private readonly Dictionary<Key, Key> _swapDown = new();
    public event Action<BindingRule>? RuleTriggered;
    public event Action<bool>? ActiveChanged;
    public Key ToggleKey { get; set; } = Key.Tab;
    public bool IsActive { get; private set; }
    private bool _toggleKeyDown;

    public InputEngine() => _callback = HookCallback;
    public bool IsRunning => _hook != nint.Zero;
    public void SetRules(IEnumerable<BindingRule> rules) => _rules = rules.Where(r => r.Enabled).ToArray();
    public void SetSwaps(IEnumerable<SwapRule> swaps)
    {
        _swaps = swaps.Where(s => s.Enabled).SelectMany(s => new[] { new KeyValuePair<Key, Key>(s.First, s.Second), new KeyValuePair<Key, Key>(s.Second, s.First) }).ToDictionary(p => p.Key, p => p.Value);
    }

    public void Start()
    {
        if (IsRunning) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, nint.Zero, 0);
        if (_hook == nint.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Stop()
    {
        SetActive(false);
        if (_hook != nint.Zero) NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    public void SetActive(bool active)
    {
        if (IsActive == active) return;
        if (!active)
        {
            foreach (var target in _swapDown.Values) SendKey(target, false);
            _swapDown.Clear();
            foreach (var state in _pressed.Values) state.Cancel();
            _pressed.Clear();
        }
        IsActive = active;
        ActiveChanged?.Invoke(active);
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        var raw = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(data);
        if ((raw.flags & NativeMethods.LLKHF_INJECTED) != 0) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        var key = KeyInterop.KeyFromVirtualKey((int)raw.vkCode);
        var down = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
        var up = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
        if (!down && !up) return NativeMethods.CallNextHookEx(_hook, code, message, data);

        if (key == ToggleKey)
        {
            if (down && !_toggleKeyDown) { _toggleKeyDown = true; SetActive(!IsActive); }
            if (up) _toggleKeyDown = false;
            return 1;
        }

        if (!IsActive) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        if (_swapDown.TryGetValue(key, out var heldTarget))
        {
            if (up) { SendKey(heldTarget, false); _swapDown.Remove(key); }
            return 1;
        }
        if (_swaps.TryGetValue(key, out var target))
        {
            if (down) { _swapDown[key] = target; SendKey(target, true); }
            return 1;
        }
        var rule = _rules.FirstOrDefault(r => r.Trigger == key);
        if (rule is null) return NativeMethods.CallNextHookEx(_hook, code, message, data);

        if (down && !_pressed.ContainsKey(key))
        {
            var state = new PressState(rule, Fire, () => RuleTriggered?.Invoke(rule));
            _pressed[key] = state;
            state.Begin();
        }
        else if (up && _pressed.TryRemove(key, out var state)) { state.End(); return 1; }

        return 1;
    }

    private void Fire(MouseBindingAction action, bool down = false, int wheelDelta = 120)
    {
        var flag = action switch
        {
            MouseBindingAction.LeftClick or MouseBindingAction.HoldLeft => down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP,
            MouseBindingAction.RightClick or MouseBindingAction.HoldRight => down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP,
            MouseBindingAction.MiddleClick => down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP,
            MouseBindingAction.WheelUp or MouseBindingAction.WheelDown => NativeMethods.MOUSEEVENTF_WHEEL,
            _ => 0u
        };
        var wheel = action == MouseBindingAction.WheelUp ? (uint)wheelDelta : action == MouseBindingAction.WheelDown ? unchecked((uint)-wheelDelta) : 0;
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, mouse = new NativeMethods.MOUSEINPUT { dwFlags = flag, mouseData = wheel } };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendKey(Key key, bool down)
    {
        var vk = KeyInterop.VirtualKeyFromKey(key);
        var flags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP;
        if (key is Key.RightCtrl or Key.RightAlt or Key.Insert or Key.Delete or Key.Home or Key.End or Key.Prior or Key.Next or Key.Up or Key.Down or Key.Left or Key.Right or Key.Divide or Key.NumLock)
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, keyboard = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk, dwFlags = flags } };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose() => Stop();

    private sealed class PressState
    {
        private readonly BindingRule _rule; private readonly Action<MouseBindingAction, bool, int> _fire; private readonly Action _notify;
        private readonly CancellationTokenSource _cts = new(); private bool _held;
        public PressState(BindingRule rule, Action<MouseBindingAction, bool, int> fire, Action notify) { _rule = rule; _fire = fire; _notify = notify; }
        public void Begin() => _ = RunAsync();
        private async Task RunAsync()
        {
            try
            {
                if (_rule.Action is MouseBindingAction.HoldLeft or MouseBindingAction.HoldRight)
                {
                    await Task.Delay(_rule.HoldDelayMs, _cts.Token); _held = true; _fire(_rule.Action, true, 0); _notify();
                }
                else if (_rule.Action is MouseBindingAction.WheelUp or MouseBindingAction.WheelDown)
                {
                    _fire(_rule.Action, false, _rule.WheelDelta); _notify();
                    await Task.Delay(_rule.HoldDelayMs, _cts.Token);
                    while (!_cts.IsCancellationRequested) { _fire(_rule.Action, false, _rule.WheelDelta); await Task.Delay(_rule.RepeatMs, _cts.Token); }
                }
            }
            catch (OperationCanceledException) { }
        }
        public void End()
        {
            _cts.Cancel();
            if (_rule.Action is MouseBindingAction.LeftClick or MouseBindingAction.RightClick or MouseBindingAction.MiddleClick) { _fire(_rule.Action, true, 0); _fire(_rule.Action, false, 0); _notify(); }
            else if (_rule.Action is MouseBindingAction.HoldLeft or MouseBindingAction.HoldRight)
            {
                if (_held) _fire(_rule.Action, false, 0); else { _fire(_rule.Action, true, 0); _fire(_rule.Action, false, 0); }
                _notify();
            }
        }
        public void Cancel() { _cts.Cancel(); if (_held) _fire(_rule.Action, false, 0); }
    }
}
