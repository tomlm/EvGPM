using System.Runtime.InteropServices;
using System.Text;

namespace EvGPM;

/// <summary>
/// Intercepts PTY communication to detect mouse tracking control sequences
/// This provides a transparent layer between applications and the terminal
/// </summary>
public class PtyInterceptor : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int openpt(int flags);
    
    [DllImport("libc", SetLastError = true)]
    private static extern int grantpt(int fd);
    
    [DllImport("libc", SetLastError = true)]
    private static extern int unlockpt(int fd);
    
    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr ptsname(int fd);

    private const int O_RDWR = 2;
    private const int O_NOCTTY = 256;
    
    private int _masterFd = -1;
    private int _slaveFd = -1;
    private string? _slavePath;
    private readonly TtyInputMonitor _monitor;
    private bool _disposed = false;

    public bool MouseTrackingEnabled => _monitor.MouseTrackingEnabled;
    public TtyInputMonitor Monitor => _monitor;

    public PtyInterceptor()
    {
        _monitor = new TtyInputMonitor();
    }

    /// <summary>
    /// Create a pseudo-terminal pair for interception
    /// </summary>
    public bool Initialize()
    {
        try
        {
            _masterFd = openpt(O_RDWR | O_NOCTTY);
            if (_masterFd < 0)
            {
                Console.Error.WriteLine("Failed to open pseudo-terminal master");
                return false;
            }

            if (grantpt(_masterFd) != 0)
            {
                Console.Error.WriteLine("Failed to grant pseudo-terminal");
                return false;
            }

            if (unlockpt(_masterFd) != 0)
            {
                Console.Error.WriteLine("Failed to unlock pseudo-terminal");
                return false;
            }

            IntPtr namePtr = ptsname(_masterFd);
            if (namePtr == IntPtr.Zero)
            {
                Console.Error.WriteLine("Failed to get pseudo-terminal slave name");
                return false;
            }

            _slavePath = Marshal.PtrToStringAnsi(namePtr);
            Console.WriteLine($"Created PTY pair: master={_masterFd}, slave={_slavePath}");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error initializing PTY: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Start intercepting and monitoring terminal I/O
    /// </summary>
    public async Task StartInterceptingAsync(CancellationToken cancellationToken)
    {
        if (_masterFd < 0)
        {
            throw new InvalidOperationException("PTY not initialized");
        }

        var buffer = new byte[4096];
        var escapeSequenceBuffer = new StringBuilder();
        bool inEscapeSequence = false;

        // Read from master PTY (application writes)
        using var stream = new FileStream(new SafeFileHandle((IntPtr)_masterFd, false), FileAccess.Read);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead > 0)
                {
                    ParseForEscapeSequences(buffer, bytesRead);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void ParseForEscapeSequences(byte[] data, int length)
    {
        var text = Encoding.UTF8.GetString(data, 0, length);
        
        // Simple escape sequence parser
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            
            if (c == '\x1b' && i + 1 < text.Length && text[i + 1] == '[')
            {
                // Start of CSI sequence
                int end = FindEscapeSequenceEnd(text, i + 2);
                if (end > i)
                {
                    string sequence = text.Substring(i, end - i + 1);
                    _monitor.ParseEscapeSequence(sequence);
                    i = end;
                }
            }
        }
    }

    private int FindEscapeSequenceEnd(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            // CSI sequences end with a letter (or specific characters)
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || 
                c == '@' || c == '`' || c == '~')
            {
                return i;
            }
        }
        return -1;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _monitor?.Dispose();
            
            if (_masterFd >= 0)
            {
                // Close file descriptors
                _masterFd = -1;
            }
            
            _disposed = true;
        }
    }
}