using System.Text;

namespace EvGPM;

/// <summary>
/// Parses and tracks mouse tracking control sequences
/// Maintains state of which mouse tracking modes are enabled
/// </summary>
public class TtyInputMonitor : IDisposable
{
    private bool _disposed = false;
    
    // Mouse tracking state flags
    public bool MouseTrackingEnabled { get; private set; }
    public bool ButtonEventTracking { get; private set; }
    public bool MotionTracking { get; private set; }
    public bool AnyMotionTracking { get; private set; }
    public bool SgrMode { get; private set; }
    public bool Utf8Mode { get; private set; }
    public bool UrxvtMode { get; private set; }

    public event EventHandler<bool>? MouseTrackingStateChanged;
    
    /// <summary>
    /// Parse escape sequences from TTY output to detect mouse tracking changes
    /// </summary>
    public void ParseEscapeSequence(string sequence)
    {
        // CSI ? Pm h - DEC Private Mode Set (enable)
        // CSI ? Pm l - DEC Private Mode Reset (disable)
        
        if (sequence.StartsWith("\x1b[?") && sequence.Length > 4)
        {
            bool enable = sequence.EndsWith('h');
            bool disable = sequence.EndsWith('l');
            
            if (!enable && !disable) return;

            // Extract parameter(s)
            string paramStr = sequence.Substring(3, sequence.Length - 4);
            string[] parameters = paramStr.Split(';');
            
            bool stateChanged = false;
            foreach (string param in parameters)
            {
                if (int.TryParse(param, out int mode))
                {
                    if (HandleMouseMode(mode, enable))
                    {
                        stateChanged = true;
                    }
                }
            }

            if (stateChanged)
            {
                MouseTrackingStateChanged?.Invoke(this, MouseTrackingEnabled);
            }
        }
    }

    /// <summary>
    /// Handle a specific mouse mode change
    /// Returns true if the mode affected mouse tracking state
    /// </summary>
    private bool HandleMouseMode(int mode, bool enable)
    {
        bool wasEnabled = MouseTrackingEnabled;

        switch (mode)
        {
            case 1000: // X10 mouse reporting (button press/release)
                ButtonEventTracking = enable;
                UpdateMouseTrackingState();
                return true;
                
            case 1002: // Button event tracking + motion while button pressed
                MotionTracking = enable;
                UpdateMouseTrackingState();
                return true;
                
            case 1003: // Any motion tracking
                AnyMotionTracking = enable;
                UpdateMouseTrackingState();
                return true;
                
            case 1006: // SGR extended mouse mode
                SgrMode = enable;
                // SGR is an encoding mode, doesn't affect tracking state
                return false;
                
            case 1005: // UTF-8 mouse mode
                Utf8Mode = enable;
                return false;
                
            case 1015: // URXVT mouse mode
                UrxvtMode = enable;
                return false;

            default:
                return false;
        }
    }

    private void UpdateMouseTrackingState()
    {
        MouseTrackingEnabled = ButtonEventTracking || MotionTracking || AnyMotionTracking;
    }

    /// <summary>
    /// Reset all mouse tracking states
    /// </summary>
    public void Reset()
    {
        MouseTrackingEnabled = false;
        ButtonEventTracking = false;
        MotionTracking = false;
        AnyMotionTracking = false;
        SgrMode = false;
        Utf8Mode = false;
        UrxvtMode = false;
    }

    /// <summary>
    /// Get a summary of the current mouse tracking state
    /// </summary>
    public string GetStateSummary()
    {
        if (!MouseTrackingEnabled)
            return "Mouse tracking: DISABLED";

        var modes = new List<string>();
        if (ButtonEventTracking) modes.Add("Buttons(1000)");
        if (MotionTracking) modes.Add("Motion(1002)");
        if (AnyMotionTracking) modes.Add("AnyMotion(1003)");
        
        var encoding = new List<string>();
        if (SgrMode) encoding.Add("SGR(1006)");
        if (Utf8Mode) encoding.Add("UTF8(1005)");
        if (UrxvtMode) encoding.Add("URXVT(1015)");

        string result = "Mouse tracking: ENABLED - " + string.Join(", ", modes);
        if (encoding.Any())
            result += " | Encoding: " + string.Join(", ", encoding);
        
        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}