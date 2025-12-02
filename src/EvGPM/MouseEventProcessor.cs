using System.Diagnostics;

namespace EvGPM;

/// <summary>
/// Processes raw evdev mouse events and converts them to ANSI sequences
/// </summary>
public class MouseEventProcessor
{
    private readonly AnsiMouseEncoder _encoder;
    private readonly TtyOutputHandler _ttyOutput;
    private readonly Dictionary<int, bool> _buttonStates;
    private readonly Func<bool> _isTrackingEnabled;
    
    // Mouse position tracking
    private int _currentX = 0;
    private int _currentY = 0;
    
    // Terminal dimensions (can be updated)
    private int _terminalWidth = 80;
    private int _terminalHeight = 24;
    
    public MouseEventProcessor(AnsiMouseEncoder encoder, TtyOutputHandler ttyOutput, Func<bool>? isTrackingEnabled = null)
    {
        _encoder = encoder;
        _ttyOutput = ttyOutput;
        _buttonStates = new Dictionary<int, bool>();
        _isTrackingEnabled = isTrackingEnabled ?? (() => true); // Default: always enabled (legacy behavior)
        
        // Try to get actual terminal dimensions
        UpdateTerminalDimensions();
    }

    /// <summary>
    /// Process a button event (press or release)
    /// </summary>
    public void ProcessButtonEvent(EvDev.InputEvent inputEvent)
    {
        Console.WriteLine($"[{_currentX},{_currentY}] Button event: type={inputEvent.Type} code={inputEvent.Code}, value={inputEvent.Value}");
        
        // Only send events if mouse tracking is enabled
        if (!_isTrackingEnabled())
        {
            return;
        }
        
        int button = MapEvDevButtonToAnsi(inputEvent.Code);
        if (button < 0) return; // Unknown button

        bool isPressed = inputEvent.Value == 1;
        bool isReleased = inputEvent.Value == 0;

        if (isPressed)
        {
            _buttonStates[button] = true;
            string sequence = _encoder.EncodeButtonPress(button, _currentX, _currentY);
            _ttyOutput.WriteMouseSequence(sequence);
        }
        else if (isReleased)
        {
            _buttonStates[button] = false;
            string sequence = _encoder.EncodeButtonRelease(button, _currentX, _currentY);
            _ttyOutput.WriteMouseSequence(sequence);
        }
    }

    /// <summary>
    /// Process a relative motion event
    /// </summary>
    public void ProcessRelativeMotion(EvDev.InputEvent inputEvent)
    {
        Console.WriteLine($"[{_currentX},{_currentY}] Relative motion event: type={inputEvent.Type} code={inputEvent.Code}, value={inputEvent.Value}");
        switch (inputEvent.Code)
        {
            case EvDev.REL_X:
                _currentX = Math.Clamp(_currentX + inputEvent.Value, 0, _terminalWidth - 1);
                break;
            case EvDev.REL_Y:
                _currentY = Math.Clamp(_currentY + inputEvent.Value, 0, _terminalHeight - 1);
                break;
            case EvDev.REL_WHEEL:
                ProcessScrollWheel(inputEvent.Value);
                return; // Scroll wheel handling is separate
            case EvDev.REL_HWHEEL:
                // Horizontal wheel - could be supported in future
                return;
        }

        // Only send motion events if mouse tracking is enabled
        if (!_isTrackingEnabled())
        {
            return;
        }

        // If any button is pressed, send motion event
        if (_buttonStates.Any(kvp => kvp.Value))
        {
            int pressedButton = _buttonStates.First(kvp => kvp.Value).Key;
            string sequence = _encoder.EncodeMotion(pressedButton, _currentX, _currentY);
            _ttyOutput.WriteMouseSequence(sequence);
        }
    }

    /// <summary>
    /// Process an absolute motion event (touchpads, tablets)
    /// </summary>
    public void ProcessAbsoluteMotion(EvDev.InputEvent inputEvent)
    {
        Console.WriteLine($"Absolute motion event: type={inputEvent.Type} code={inputEvent.Code}, value={inputEvent.Value}");
        
        // For absolute coordinates, we need to scale the input value to terminal dimensions
        // Most touchpads/tablets use a large coordinate space (e.g., 0-4096 or 0-32768)
        // We'll need to track the max values for proper scaling
        switch (inputEvent.Code)
        {
            case EvDev.REL_X:
                // Assuming a typical touchpad range of 0-4096, scale to terminal width
                // This may need adjustment based on actual device capabilities
                _currentX = ScaleAbsoluteCoordinate(inputEvent.Value, 4096, _terminalWidth);
                break;
            case EvDev.ABS_Y:
                // Scale Y coordinate to terminal height
                _currentY = ScaleAbsoluteCoordinate(inputEvent.Value, 4096, _terminalHeight);
                break;
            case EvDev.ABS_Z:
                // Pressure or other Z-axis data - typically not used for mouse positioning
                break;
        }

        // If any button is pressed, send motion event
        if (_buttonStates.Any(kvp => kvp.Value))
        {
            int pressedButton = _buttonStates.First(kvp => kvp.Value).Key;
            string sequence = _encoder.EncodeMotion(pressedButton, _currentX, _currentY);
            _ttyOutput.WriteMouseSequence(sequence);
        }
    }

    /// <summary>
    /// Scale an absolute coordinate from device range to terminal range
    /// </summary>
    private int ScaleAbsoluteCoordinate(int value, int deviceMax, int terminalMax)
    {
        // Clamp input value to valid range
        value = Math.Clamp(value, 0, deviceMax);
        
        // Scale to terminal dimensions
        int scaled = (value * terminalMax) / deviceMax;
        
        // Ensure result is within terminal bounds
        return Math.Clamp(scaled, 0, terminalMax - 1);
    }

    /// <summary>
    /// Process mouse wheel scrolling
    /// </summary>
    private void ProcessScrollWheel(int value)
    {
        bool scrollUp = value > 0;
        string sequence = _encoder.EncodeScroll(scrollUp, _currentX, _currentY);
        _ttyOutput.WriteMouseSequence(sequence);
    }

    /// <summary>
    /// Map evdev button codes to ANSI button numbers
    /// </summary>
    private int MapEvDevButtonToAnsi(ushort code)
    {
        return code switch
        {
            EvDev.BTN_LEFT => 0,
            EvDev.BTN_MIDDLE => 1,
            EvDev.BTN_RIGHT => 2,
            EvDev.BTN_SIDE => 3,      // Side button (back)
            EvDev.BTN_EXTRA => 4,     // Extra button (forward)
            _ => -1
        };
    }

    /// <summary>
    /// Update terminal dimensions for coordinate clamping
    /// </summary>
    public void UpdateTerminalDimensions()
    {
        try
        {
            _terminalWidth = Console.WindowWidth;
            _terminalHeight = Console.WindowHeight;
        }
        catch
        {
            // Fallback to defaults if we can't get dimensions
            _terminalWidth = 80;
            _terminalHeight = 24;
        }
    }

    public (int x, int y) GetCurrentPosition() => (_currentX, _currentY);
}
