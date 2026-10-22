using Windows.Gaming.Input;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace JelliBox.Services;

/// <summary>Key press that can be raised without a real KeyRoutedEventArgs.</summary>
public sealed class KeyEvent
{
    public VirtualKey Key { get; }
    public bool Handled { get; set; }
    public KeyEvent(VirtualKey key) => Key = key;
}

public interface IGamepadKeyTarget
{
    void HandleKey(KeyEvent e);
}

/// <summary>
/// Backup input for controllers Windows exposes as generic HID rather than XInput
/// (e.g. a Stadia controller over Bluetooth). XAML only raises GamepadA/B/... keys for
/// <see cref="Gamepad"/> devices, so this polls <see cref="RawGameController"/> and feeds
/// the pages' key handlers directly. Idle whenever a real Gamepad is connected.
/// </summary>
public sealed class RawGamepadFallback
{
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatRate = TimeSpan.FromMilliseconds(120);
    private const double StickThreshold = 0.5;
    private const int LeftTriggerAxis = 5;
    private const int RightTriggerAxis = 4;

    // Button indices for a Stadia controller in Bluetooth mode; used when the device
    // doesn't report Xbox button labels.
    private static readonly Dictionary<int, VirtualKey> DefaultMap = new()
    {
        [0] = VirtualKey.GamepadA,
        [1] = VirtualKey.GamepadB,
        [2] = VirtualKey.GamepadX,
        [3] = VirtualKey.GamepadY,
        [4] = VirtualKey.GamepadLeftShoulder,
        [5] = VirtualKey.GamepadRightShoulder,
        [6] = VirtualKey.GamepadView,
        [7] = VirtualKey.GamepadMenu,
    };

    private DispatcherTimer? _timer;
    private RawGameController? _mapped;
    private Dictionary<int, VirtualKey> _map = DefaultMap;
    private HashSet<VirtualKey> _down = new();
    private VirtualKey _navKey = VirtualKey.None;
    private DateTime _nextRepeat;
    private double[]? _axisRest;

    public void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private static RawGameController? FindDevice()
    {
        if (Gamepad.Gamepads.Count > 0) return null;
        return RawGameController.RawGameControllers
            .FirstOrDefault(c => Gamepad.FromGameController(c) == null);
    }

    private static Dictionary<int, VirtualKey> BuildMap(RawGameController raw)
    {
        var map = new Dictionary<int, VirtualKey>();
        for (var i = 0; i < raw.ButtonCount; i++)
        {
            VirtualKey? key = raw.GetButtonLabel(i) switch
            {
                GameControllerButtonLabel.XboxA => VirtualKey.GamepadA,
                GameControllerButtonLabel.XboxB => VirtualKey.GamepadB,
                GameControllerButtonLabel.XboxX => VirtualKey.GamepadX,
                GameControllerButtonLabel.XboxY => VirtualKey.GamepadY,
                GameControllerButtonLabel.XboxLeftBumper => VirtualKey.GamepadLeftShoulder,
                GameControllerButtonLabel.XboxRightBumper => VirtualKey.GamepadRightShoulder,
                GameControllerButtonLabel.XboxMenu => VirtualKey.GamepadMenu,
                GameControllerButtonLabel.XboxView => VirtualKey.GamepadView,
                _ => null,
            };
            if (key != null) map[i] = key.Value;
        }
        return map.Count > 0 ? map : DefaultMap;
    }

    // HID sticks are unsigned and centre at 0.5; triggers rest at 0. Axes 0-3 are the two sticks.
    private static double[] RestPositions(int count)
    {
        var rest = new double[count];
        for (var i = 0; i < Math.Min(count, 4); i++) rest[i] = 0.5;
        return rest;
    }

    private void Poll()
    {
        var raw = FindDevice();
        if (raw == null)
        {
            _down.Clear();
            _navKey = VirtualKey.None;
            _mapped = null;
            _axisRest = null;
            return;
        }
        if (raw != _mapped)
        {
            _mapped = raw;
            _map = BuildMap(raw);
            _axisRest = null;
            _down.Clear();
        }

        var buttons = new bool[raw.ButtonCount];
        var switches = new GameControllerSwitchPosition[raw.SwitchCount];
        var axes = new double[raw.AxisCount];
        raw.GetCurrentReading(buttons, switches, axes);

        if (_axisRest == null || _axisRest.Length != axes.Length) _axisRest = RestPositions(axes.Length);

        var now = new HashSet<VirtualKey>();
        for (var i = 0; i < buttons.Length; i++)
        {
            if (!buttons[i]) continue;
            if (_map.TryGetValue(i, out var key)) now.Add(key);
        }

        if (switches.Length > 0)
        {
            var pos = switches[0];
            if (pos is GameControllerSwitchPosition.Up or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.UpRight)
                now.Add(VirtualKey.GamepadDPadUp);
            if (pos is GameControllerSwitchPosition.Down or GameControllerSwitchPosition.DownLeft or GameControllerSwitchPosition.DownRight)
                now.Add(VirtualKey.GamepadDPadDown);
            if (pos is GameControllerSwitchPosition.Left or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.DownLeft)
                now.Add(VirtualKey.GamepadDPadLeft);
            if (pos is GameControllerSwitchPosition.Right or GameControllerSwitchPosition.UpRight or GameControllerSwitchPosition.DownRight)
                now.Add(VirtualKey.GamepadDPadRight);
        }

        if (axes.Length >= 2)
        {
            // HID axes are 0..1 with the stick centred at 0.5; Y grows downwards.
            var x = (axes[0] - _axisRest[0]) * 2;
            var y = (axes[1] - _axisRest[1]) * 2;
            if (x <= -StickThreshold) now.Add(VirtualKey.GamepadLeftThumbstickLeft);
            if (x >= StickThreshold) now.Add(VirtualKey.GamepadLeftThumbstickRight);
            if (y <= -StickThreshold) now.Add(VirtualKey.GamepadLeftThumbstickUp);
            if (y >= StickThreshold) now.Add(VirtualKey.GamepadLeftThumbstickDown);
        }

        // Triggers report as axes that rest at their starting value.
        if (axes.Length > Math.Max(LeftTriggerAxis, RightTriggerAxis))
        {
            if (Math.Abs(axes[LeftTriggerAxis] - _axisRest[LeftTriggerAxis]) >= StickThreshold)
                now.Add(VirtualKey.GamepadLeftTrigger);
            if (Math.Abs(axes[RightTriggerAxis] - _axisRest[RightTriggerAxis]) >= StickThreshold)
                now.Add(VirtualKey.GamepadRightTrigger);
        }

        foreach (var key in now)
        {
            if (!_down.Contains(key) && !IsNav(key)) Press(key);
        }

        var nav = now.FirstOrDefault(IsNav);
        var time = DateTime.UtcNow;
        if (nav == VirtualKey.None)
        {
            _navKey = VirtualKey.None;
        }
        else if (nav != _navKey)
        {
            _navKey = nav;
            _nextRepeat = time + RepeatDelay;
            Press(nav);
        }
        else if (time >= _nextRepeat)
        {
            _nextRepeat = time + RepeatRate;
            Press(nav);
        }

        _down = now;
    }

    // Keys that auto-repeat while held (navigation and seeking).
    private static bool IsNav(VirtualKey key) => key is
        VirtualKey.GamepadLeftTrigger or VirtualKey.GamepadRightTrigger or
        VirtualKey.GamepadDPadUp or VirtualKey.GamepadDPadDown or
        VirtualKey.GamepadDPadLeft or VirtualKey.GamepadDPadRight or
        VirtualKey.GamepadLeftThumbstickUp or VirtualKey.GamepadLeftThumbstickDown or
        VirtualKey.GamepadLeftThumbstickLeft or VirtualKey.GamepadLeftThumbstickRight;

    private static ComboBox? _openCombo;

    // Drop-downs and flyouts live in popups, whose keys never reach the page. With a real pad XAML
    // routes them; here we drive the popup ourselves so the page behind it doesn't move.
    private static bool HandlePopup(VirtualKey key)
    {
        var popups = VisualTreeHelper.GetOpenPopups(Window.Current).Where(p => p.Child is not ToolTip).ToList();
        if (popups.Count == 0)
        {
            _openCombo = null;
            return false;
        }

        switch (key)
        {
            case VirtualKey.GamepadB:
            case VirtualKey.Escape:
                if (_openCombo is { IsDropDownOpen: true } combo) combo.IsDropDownOpen = false;
                else foreach (var popup in popups) popup.IsOpen = false;
                _openCombo = null;
                break;
            case VirtualKey.GamepadA:
                if (FocusManager.GetFocusedElement() is ComboBoxItem item
                    && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox owner)
                {
                    // Choose the item directly and close the list; the accessibility route leaves it open.
                    var index = owner.IndexFromContainer(item);
                    if (index >= 0) owner.SelectedIndex = index;
                    owner.IsDropDownOpen = false;
                    _openCombo = null;
                }
                else
                {
                    Activate(FocusManager.GetFocusedElement() as UIElement);
                }
                break;
            case VirtualKey.GamepadDPadUp:
            case VirtualKey.GamepadLeftThumbstickUp:
                Move(FocusNavigationDirection.Up);
                break;
            case VirtualKey.GamepadDPadDown:
            case VirtualKey.GamepadLeftThumbstickDown:
                Move(FocusNavigationDirection.Down);
                break;
        }
        return true;
    }

    private static void Press(VirtualKey key)
    {
        if (HandlePopup(key)) return;

        var focused = FocusManager.GetFocusedElement() as DependencyObject;

        if (key == VirtualKey.GamepadMenu && FindAncestor<Controls.MediaCard>(focused) is { } card)
        {
            card.ShowContextMenu();
            return;
        }

        var e = new KeyEvent(key);
        ((Window.Current.Content as Frame)?.Content as IGamepadKeyTarget)?.HandleKey(e);
        if (e.Handled) return;

        switch (key)
        {
            case VirtualKey.GamepadDPadUp:
            case VirtualKey.GamepadLeftThumbstickUp:
                Move(FocusNavigationDirection.Up);
                break;
            case VirtualKey.GamepadDPadDown:
            case VirtualKey.GamepadLeftThumbstickDown:
                Move(FocusNavigationDirection.Down);
                break;
            case VirtualKey.GamepadDPadLeft:
            case VirtualKey.GamepadLeftThumbstickLeft:
                Move(FocusNavigationDirection.Left);
                break;
            case VirtualKey.GamepadDPadRight:
            case VirtualKey.GamepadLeftThumbstickRight:
                Move(FocusNavigationDirection.Right);
                break;
            case VirtualKey.GamepadA:
                Activate(focused as UIElement);
                break;
        }
    }

    private static void Move(FocusNavigationDirection direction)
    {
        if (FocusManager.GetFocusedElement() == null)
        {
            (FocusManager.FindFirstFocusableElement(Window.Current.Content) as Control)?.Focus(FocusState.Keyboard);
            return;
        }
        FocusManager.TryMoveFocus(direction);
    }

    private static void Activate(UIElement? element)
    {
        if (element == null) return;
        if (element is TextBox box)
        {
            box.Focus(FocusState.Keyboard);
            Windows.UI.ViewManagement.InputPane.GetForCurrentView().TryShow();
            return;
        }

        if (element is ComboBox combo)
        {
            _openCombo = combo;
            combo.IsDropDownOpen = true;
            return;
        }

        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider item) item.Select();
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is T match) return match;
        }
        return null;
    }
}
