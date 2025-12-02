using System.Text;

namespace EvGPM;

/// <summary>
/// Monitors TTY input/output for mouse tracking control sequences
/// Tracks when applications enable/disable mouse tracking
/// </summary>
public class TtyInputMonitor : IDisposable
{
    private readonly string _ttyPath;
    private FileStream? _ttyStream;
    private bool _disposed = false;
    private readonly byte[] _buffer = new byte[1024];
    
    // Mouse tracking state flags
    public bool MouseTrackingEnabled { get; private set; }
    public bool ButtonEventTracking { get; private set; }
    public bool MotionTracking { get; private set; }
    public bool AnyMotionTracking { get; private set; }
    public bool SgrMode { get; private set; }
    public bool Utf8Mode { get; private set; }
    
    public TtyInputMonitor(string? ttyPath = null)
    {
        _ttyPath = ttyPath ?? "/dev/tty";
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Open TTY for reading - we need to read the output stream that apps write to
            // This requires intercepting the PTY master side, which is complex
            // Alternative: Use ioctl to monitor terminal state or use pseudo-terminal
            
            // For now, we'll implement a simpler approach:
            // Monitor /dev/ptmx or the specific pty device
            // This is a simplified version that would need root privileges
            
            await MonitorTtyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error monitoring TTY: {ex.Message}");
        }
    }

    private async Task MonitorTtyAsync(CancellationToken cancellationToken)
    {
        // This is a placeholder for the actual implementation
        // In practice, you would need to:
        // 1. Use a pseudo-terminal (pty) pair
        // 2. Intercept application output
        // 3. Parse escape sequences
        // 4. Forward everything except mouse control sequences
        
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, cancellationToken);
        }
    }

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
            
            foreach (string param in parameters)
            {
                if (int.TryParse(param, out int mode))
                {
                    HandleMouseMode(mode, enable);
                }
            }
        }
    }

    private void HandleMouseMode(int mode, bool enable)
    {
        switch (mode)
        {
            case 1000: // X10 mouse reporting (button press/release)
                ButtonEventTracking = enable;
                UpdateMouseTrackingState();
                Console.WriteLine($"Mouse tracking 1000 (button events): {enable}");
                break;
                
            case 1002: // Button event tracking + motion while button pressed
                MotionTracking = enable;
                UpdateMouseTrackingState();
                Console.WriteLine($"Mouse tracking 1002 (motion): {enable}");
                break;
                
            case 1003: // Any motion tracking
                AnyMotionTracking = enable;
                UpdateMouseTrackingState();
                Console.WriteLine($"Mouse tracking 1003 (any motion): {enable}");
                break;
                
            case 1006: // SGR extended mouse mode
                SgrMode = enable;
                Console.WriteLine($"Mouse tracking 1006 (SGR mode): {enable}");
                break;
                
            case 1005: // UTF-8 mouse mode
                Utf8Mode = enable;
                Console.WriteLine($"Mouse tracking 1005 (UTF-8 mode): {enable}");
                break;
                
            case 1015: // URXVT mouse mode
                Console.WriteLine($"Mouse tracking 1015 (URXVT mode): {enable}");
                break;
        }
    }

    private void UpdateMouseTrackingState()
    {
        MouseTrackingEnabled = ButtonEventTracking || MotionTracking || AnyMotionTracking;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _ttyStream?.Dispose();
            _disposed = true;
        }
    }
}