namespace EvGPM;

/// <summary>
/// Generates ANSI/VT escape sequences for mouse events
/// Supports X10, Normal, and SGR mouse protocols
/// </summary>
public class AnsiMouseEncoder
{
    public enum MouseProtocol
    {
        X10,        // ESC [ M Cb Cx Cy (original)
        Normal,     // ESC [ M Cb Cx Cy (with button release)
        SGR         // ESC [ < Cb ; Cx ; Cy M/m (modern, recommended)
    }

    private readonly MouseProtocol _protocol;
    private int _lastX = 0;
    private int _lastY = 0;

    public AnsiMouseEncoder(MouseProtocol protocol = MouseProtocol.SGR)
    {
        _protocol = protocol;
    }

    /// <summary>
    /// Encode a mouse button press event
    /// </summary>
    public string EncodeButtonPress(int button, int x, int y)
    {
        _lastX = x;
        _lastY = y;

        return _protocol switch
        {
            MouseProtocol.SGR => $"\x1b[<{button};{x};{y}M",
            MouseProtocol.Normal or MouseProtocol.X10 => EncodeLegacy(button, x, y, 'M'),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Encode a mouse button release event
    /// </summary>
    public string EncodeButtonRelease(int button, int x, int y)
    {
        _lastX = x;
        _lastY = y;

        return _protocol switch
        {
            MouseProtocol.SGR => $"\x1b[<{button};{x};{y}m",
            MouseProtocol.Normal => EncodeLegacy(3, x, y, 'M'), // Button 3 = release in normal mode
            MouseProtocol.X10 => string.Empty, // X10 doesn't support release events
            _ => string.Empty
        };
    }

    /// <summary>
    /// Encode a mouse movement event (with button held)
    /// </summary>
    public string EncodeMotion(int button, int x, int y)
    {
        _lastX = x;
        _lastY = y;

        int motionButton = button + 32; // Add 32 for motion events

        return _protocol switch
        {
            MouseProtocol.SGR => $"\x1b[<{motionButton};{x};{y}M",
            MouseProtocol.Normal => EncodeLegacy(motionButton, x, y, 'M'),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Encode mouse wheel scroll event
    /// </summary>
    public string EncodeScroll(bool scrollUp, int x, int y)
    {
        _lastX = x;
        _lastY = y;

        int scrollButton = scrollUp ? 64 : 65;

        return _protocol switch
        {
            MouseProtocol.SGR => $"\x1b[<{scrollButton};{x};{y}M",
            MouseProtocol.Normal or MouseProtocol.X10 => EncodeLegacy(scrollButton, x, y, 'M'),
            _ => string.Empty
        };
    }

    private string EncodeLegacy(int button, int x, int y, char suffix)
    {
        // Legacy encoding uses offset of 32
        char cb = (char)(button + 32);
        char cx = (char)(x + 32);
        char cy = (char)(y + 32);

        return $"\x1b[M{cb}{cx}{cy}";
    }

    public void UpdatePosition(int deltaX, int deltaY)
    {
        _lastX = Math.Max(0, _lastX + deltaX);
        _lastY = Math.Max(0, _lastY + deltaY);
    }

    public (int x, int y) GetCurrentPosition() => (_lastX, _lastY);
}
