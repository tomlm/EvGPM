using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace EvGPM;

/// <summary>
/// Manages multiple TTY sessions and monitors them for mouse tracking state
/// </summary>
public class TtySessionManager : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr read(int fd, byte[] buf, UIntPtr count);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr write(int fd, byte[] buf, UIntPtr count);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref Termios termios);

    private const int O_RDWR = 2;
    private const int O_NOCTTY = 256;
    private const int O_NONBLOCK = 2048;
    private const ulong TCGETS = 0x5401;
    private const ulong TCSETS = 0x5402;

    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;
        public uint c_ispeed;
        public uint c_ospeed;
    }

    private class TtySession
    {
        public string Path { get; set; } = "";
        public int Fd { get; set; } = -1;
        public bool IsActive { get; set; }
        public TtyInputMonitor Monitor { get; set; }
        public TtyOutputHandler OutputHandler { get; set; }
        public DateTime LastActivity { get; set; }

        public TtySession()
        {
            Monitor = new TtyInputMonitor();
            OutputHandler = null!;
        }
    }

    private readonly ConcurrentDictionary<string, TtySession> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed = false;
    private string? _activeTtyPath;

    public event EventHandler<string>? ActiveTtyChanged;
    public event EventHandler<(string tty, bool enabled)>? MouseTrackingChanged;

    /// <summary>
    /// Get the currently active TTY path (the one to send mouse events to)
    /// </summary>
    public string? ActiveTtyPath => _activeTtyPath;

    /// <summary>
    /// Check if the active TTY has mouse tracking enabled
    /// </summary>
    public bool IsMouseTrackingEnabled
    {
        get
        {
            if (_activeTtyPath != null && _sessions.TryGetValue(_activeTtyPath, out var session))
            {
                return session.Monitor.MouseTrackingEnabled;
            }
            return false;
        }
    }

    /// <summary>
    /// Get the output handler for the active TTY
    /// </summary>
    public TtyOutputHandler? GetActiveTtyOutput()
    {
        if (_activeTtyPath != null && _sessions.TryGetValue(_activeTtyPath, out var session))
        {
            return session.OutputHandler;
        }
        return null;
    }

    /// <summary>
    /// Start monitoring TTY sessions
    /// </summary>
    public async Task StartMonitoringAsync()
    {
        Console.WriteLine("Starting TTY session monitoring...");

        // Discover initial TTY devices
        var ttyPaths = DiscoverTtyDevices();
        foreach (var path in ttyPaths.Take(10))
        {
            await AddTtySession(path);
        }

        // Start monitoring tasks
        var tasks = new List<Task>
        {
            MonitorActiveTtyAsync(_cts.Token),
            MonitorTtySessionsAsync(_cts.Token)
        };

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Discover available TTY devices
    /// </summary>
    private List<string> DiscoverTtyDevices()
    {
        var ttyPaths = new List<string>();

        // Check common TTY paths
        for (int i = 1; i <= 63; i++)
        {
            string path = $"/dev/tty{i}";
            if (File.Exists(path))
            {
                ttyPaths.Add(path);
            }
        }

        // Also check pts (pseudo-terminals)
        if (Directory.Exists("/dev/pts"))
        {
            foreach (var path in Directory.GetFiles("/dev/pts"))
            {
                if (int.TryParse(Path.GetFileName(path), out _))
                {
                    ttyPaths.Add(path);
                }
            }
        }

        Console.WriteLine($"Discovered {ttyPaths.Count} TTY devices");
        return ttyPaths;
    }

    /// <summary>
    /// Add a TTY session to monitor
    /// </summary>
    private async Task AddTtySession(string ttyPath)
    {
        if (_sessions.ContainsKey(ttyPath))
            return;

        try
        {
            var session = new TtySession
            {
                Path = ttyPath,
                IsActive = false,
                LastActivity = DateTime.UtcNow
            };

            // Try to open the TTY for reading (to monitor output)
            int fd = open(ttyPath, O_RDWR | O_NOCTTY | O_NONBLOCK);
            if (fd >= 0)
            {
                session.Fd = fd;
                session.OutputHandler = new TtyOutputHandler(ttyPath);
                
                if (_sessions.TryAdd(ttyPath, session))
                {
                    Console.WriteLine($"Added TTY session: {ttyPath}");
                    
                    // Start monitoring this TTY
                    _ = MonitorTtyInputAsync(session, _cts.Token);
                }
                else
                {
                    close(fd);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error adding TTY session {ttyPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Monitor a specific TTY for escape sequences
    /// </summary>
    private async Task MonitorTtyInputAsync(TtySession session, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var escapeBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (session.Fd < 0)
                    break;

                // Read from TTY
                var bytesRead = read(session.Fd, buffer, new UIntPtr((uint)buffer.Length));
                int count = bytesRead.ToInt32();

                if (count > 0)
                {
                    session.LastActivity = DateTime.UtcNow;
                    
                    // Parse for escape sequences
                    string text = Encoding.UTF8.GetString(buffer, 0, count);
                    ParseEscapeSequences(session, text);
                }
                else
                {
                    // No data available, wait a bit
                    await Task.Delay(50, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error monitoring TTY {session.Path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse escape sequences from TTY output
    /// </summary>
    private void ParseEscapeSequences(TtySession session, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '[')
            {
                // Found CSI sequence
                int end = FindEscapeSequenceEnd(text, i + 2);
                if (end > i)
                {
                    string sequence = text.Substring(i, end - i + 1);
                    
                    // Track previous state
                    bool wasEnabled = session.Monitor.MouseTrackingEnabled;
                    
                    // Parse the sequence
                    session.Monitor.ParseEscapeSequence(sequence);
                    
                    // Check if mouse tracking state changed
                    bool isEnabled = session.Monitor.MouseTrackingEnabled;
                    if (wasEnabled != isEnabled)
                    {
                        Console.WriteLine($"TTY {session.Path}: Mouse tracking {(isEnabled ? "ENABLED" : "DISABLED")}");
                        MouseTrackingChanged?.Invoke(this, (session.Path, isEnabled));
                        
                        // If this is the active TTY, update routing
                        if (session.Path == _activeTtyPath)
                        {
                            Console.WriteLine($"Active TTY mouse tracking: {isEnabled}");
                        }
                    }
                    
                    i = end;
                }
            }
        }
    }

    /// <summary>
    /// Find the end of an ANSI escape sequence
    /// </summary>
    private int FindEscapeSequenceEnd(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            // CSI sequences end with a letter or specific characters
            if ((c >= '@' && c <= '~') || (c >= 'a' && c <= 'z'))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Monitor which TTY is currently active
    /// </summary>
    private async Task MonitorActiveTtyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? newActiveTty = DetectActiveTty();
                
                if (newActiveTty != _activeTtyPath)
                {
                    Console.WriteLine($"Active TTY changed: {_activeTtyPath} -> {newActiveTty}");
                    _activeTtyPath = newActiveTty;
                    ActiveTtyChanged?.Invoke(this, newActiveTty ?? "");
                    
                    // Report mouse tracking state of new active TTY
                    if (newActiveTty != null && _sessions.TryGetValue(newActiveTty, out var session))
                    {
                        Console.WriteLine($"New active TTY has mouse tracking: {session.Monitor.MouseTrackingEnabled}");
                    }
                }

                await Task.Delay(1000, cancellationToken); // Check every second
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Detect which TTY is currently active (foreground)
    /// </summary>
    private string? DetectActiveTty()
    {
        try
        {
            // Method 1: Check /sys/class/tty/tty0/active
            string activeFile = "/sys/class/tty/tty0/active";
            if (File.Exists(activeFile))
            {
                string active = File.ReadAllText(activeFile).Trim();
                string activePath = $"/dev/{active}";
                if (_sessions.ContainsKey(activePath))
                {
                    return activePath;
                }
            }

            // Method 2: Check which TTY has most recent activity
            var activeSessions = _sessions.Values
                .Where(s => s.Fd >= 0)
                .OrderByDescending(s => s.LastActivity)
                .FirstOrDefault();

            return activeSessions?.Path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error detecting active TTY: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Periodically check for new TTY sessions
    /// </summary>
    private async Task MonitorTtySessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check for new pseudo-terminals
                if (Directory.Exists("/dev/pts"))
                {
                    foreach (var path in Directory.GetFiles("/dev/pts"))
                    {
                        if (int.TryParse(Path.GetFileName(path), out _))
                        {
                            await AddTtySession(path);
                        }
                    }
                }

                await Task.Delay(5000, cancellationToken); // Check every 5 seconds
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            
            foreach (var session in _sessions.Values)
            {
                if (session.Fd >= 0)
                {
                    close(session.Fd);
                }
                session.Monitor?.Dispose();
                session.OutputHandler?.Dispose();
            }
            
            _sessions.Clear();
            _cts.Dispose();
            _disposed = true;
        }
    }
}