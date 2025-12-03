using System.Runtime.InteropServices;

namespace EvGPM;

/// <summary>
/// Native Linux interop layer for evdev, TTY, and PTY operations
/// Consolidates all P/Invoke declarations
/// </summary>
public static class EvDev
{
    // ============================================================================
    // Event Device (evdev) Constants
    // ============================================================================
    
    // Event types
    public const ushort EV_SYN = 0x00;
    public const ushort EV_KEY = 0x01;
    public const ushort EV_REL = 0x02;
    public const ushort EV_ABS = 0x03;

    // Relative axes
    public const ushort REL_X = 0x00;
    public const ushort REL_Y = 0x01;
    public const ushort REL_Z = 0x02;
    public const ushort REL_WHEEL = 0x08;
    public const ushort REL_HWHEEL = 0x06;

    // Absolute axes
    public const ushort ABS_X = 0x00;
    public const ushort ABS_Y = 0x01;
    public const ushort ABS_Z = 0x02;

    // Mouse buttons
    public const ushort BTN_LEFT = 0x110;
    public const ushort BTN_RIGHT = 0x111;
    public const ushort BTN_MIDDLE = 0x112;
    public const ushort BTN_SIDE = 0x113;
    public const ushort BTN_EXTRA = 0x114;
    public const ushort BTN_FORWARD = 0x115;
    public const ushort BTN_BACK = 0x116;
    public const ushort BTN_TASK = 0x117;

    // ============================================================================
    // File Operation Constants
    // ============================================================================
    
    public const int O_RDONLY = 0;
    public const int O_WRONLY = 1;
    public const int O_RDWR = 2;
    public const int O_NONBLOCK = 0x800;
    public const int O_NOCTTY = 0x100;

    // ============================================================================
    // ioctl Constants
    // ============================================================================
    
    private const uint EVIOCGRAB = 0x40044590;
    private const uint EVIOCGNAME = 0x80ff4506;
    private const uint EVIOCGBIT = 0x80ff4520;
    
    public const ulong TCGETS = 0x5401;
    public const ulong TCSETS = 0x5402;
    public const ulong TIOCGWINSZ = 0x5413;
    public const ulong TIOCSWINSZ = 0x5414;

    // ============================================================================
    // Terminal Mode Flags
    // ============================================================================
    
    public const uint ICANON = 0x00000002;  // Canonical mode
    public const uint ECHO = 0x00000008;    // Echo input

    // ============================================================================
    // Poll Constants
    // ============================================================================
    
    public const short POLLIN = 0x001;
    public const short POLLOUT = 0x004;

    // ============================================================================
    // Structures
    // ============================================================================
    
    [StructLayout(LayoutKind.Sequential)]
    public struct InputEvent
    {
        public long TimeSeconds;
        public long TimeMicroseconds;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Termios
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

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    // ============================================================================
    // P/Invoke Declarations
    // ============================================================================

    // --- File Operations ---
    
    [DllImport("libc", SetLastError = true)]
    public static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr read(int fd, byte[] buf, UIntPtr count);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, IntPtr buf, int count);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr write(int fd, byte[] buf, UIntPtr count);

    // --- ioctl Operations ---
    
    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, ulong request, IntPtr argp);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl_int(int fd, uint request, ref int arg);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl_bytes(int fd, uint request, byte[] arg);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    public static extern int ioctl_termios(int fd, ulong request, ref Termios termios);

    // --- PTY Operations ---
    
    [DllImport("libc", SetLastError = true)]
    public static extern int posix_openpt(int flags);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int grantpt(int fd);
    
    [DllImport("libc", SetLastError = true)]
    public static extern int unlockpt(int fd);
    
    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr ptsname(int fd);

    // --- Poll Operations ---
    
    [DllImport("libc", SetLastError = true)]
    public static extern int poll(ref PollFd fds, uint nfds, int timeout);

    // ============================================================================
    // Event Device Helper Methods
    // ============================================================================

    /// <summary>
    /// Opens an input device for reading in blocking mode.
    /// </summary>
    public static int Open(string devicePath)
    {
        return open(devicePath, O_RDONLY);
    }

    /// <summary>
    /// Closes an open device file descriptor.
    /// </summary>
    public static void Close(int fd)
    {
        if (fd >= 0)
            close(fd);
    }

    /// <summary>
    /// Reads a single input event from the device synchronously (blocking).
    /// </summary>
    public static unsafe bool ReadEvent(int fd, out InputEvent evt)
    {
        evt = default;
        int size = Marshal.SizeOf<InputEvent>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        
        try
        {
            int result = read(fd, buffer, size);
            if (result == size)
            {
                evt = Marshal.PtrToStructure<InputEvent>(buffer);
                return true;
            }
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Grabs or releases exclusive access to the input device.
    /// </summary>
    public static void Grab(int fd, bool grab)
    {
        int value = grab ? 1 : 0;
        ioctl_int(fd, EVIOCGRAB, ref value);
    }

    /// <summary>
    /// Retrieves the human-readable name of the input device.
    /// </summary>
    public static string GetDeviceName(int fd)
    {
        byte[] buffer = new byte[256];
        int result = ioctl_bytes(fd, EVIOCGNAME, buffer);
    
        if (result > 0)
        {
            int length = Array.IndexOf(buffer, (byte)0);
            if (length < 0) length = buffer.Length;
            return System.Text.Encoding.ASCII.GetString(buffer, 0, length);
        }
    
        return "Unknown";
    }

    /// <summary>
    /// Checks whether the device supports a specific event type.
    /// </summary>
    public static bool HasEventType(int fd, ushort eventType)
    {
        byte[] buffer = new byte[32]; // 256 bits = 32 bytes
        uint request = EVIOCGBIT | ((uint)eventType << 8);
    
        int result = ioctl_bytes(fd, request, buffer);
        if (result < 0)
            return false;
    
        // Check if any bit is set
        foreach (byte b in buffer)
        {
            if (b != 0)
                return true;
        }
    
        return false;
    }

    /// <summary>
    /// Opens an input device as a FileStream in non-blocking mode.
    /// </summary>
    public static FileStream OpenAsStream(string devicePath)
    {
        int fd = open(devicePath, O_RDONLY | O_NONBLOCK);
        if (fd < 0)
            throw new IOException($"Cannot open device: {devicePath}");
        
        var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)fd, ownsHandle: true);
        return new FileStream(handle, FileAccess.Read, bufferSize: Marshal.SizeOf<InputEvent>());
    }

    /// <summary>
    /// Asynchronously reads a single input event from the device stream.
    /// </summary>
    public static async Task<(bool success, InputEvent evt)> ReadEventAsync(FileStream stream, CancellationToken cancellationToken = default)
    {
        int size = Marshal.SizeOf<InputEvent>();
        byte[] buffer = new byte[size];
        
        try
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, size, cancellationToken);
            if (bytesRead == size)
            {
                var evt = MemoryMarshal.Read<InputEvent>(buffer);
                return (true, evt);
            }
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            // EAGAIN/EWOULDBLOCK - no data available
        }
        
        return (false, default);
    }

    // ============================================================================
    // Terminal Helper Methods
    // ============================================================================

    /// <summary>
    /// Check if a TTY is in raw mode (canonical mode disabled).
    /// </summary>
    public static bool IsInRawMode(int fd)
    {
        try
        {
            var termios = new Termios();
            termios.c_cc = new byte[32];
            
            if (ioctl_termios(fd, TCGETS, ref termios) == 0)
            {
                // Check if canonical mode is disabled (raw mode)
                bool isCanonical = (termios.c_lflag & ICANON) != 0;
                return !isCanonical;
            }
        }
        catch
        {
            // If we can't check, assume not raw mode (safer)
        }
        
        return false;
    }

    /// <summary>
    /// Check if a TTY is in raw mode by path.
    /// </summary>
    public static bool IsInRawMode(string ttyPath)
    {
        int fd = open(ttyPath, O_RDONLY | O_NOCTTY);
        if (fd < 0)
            return false;

        try
        {
            return IsInRawMode(fd);
        }
        finally
        {
            close(fd);
        }
    }

    /// <summary>
    /// Get terminal window size.
    /// </summary>
    public static (int rows, int cols) GetWindowSize(int fd)
    {
        var winsize = new Winsize();
        IntPtr winsizePtr = Marshal.AllocHGlobal(Marshal.SizeOf(winsize));
        
        try
        {
            if (ioctl(fd, TIOCGWINSZ, winsizePtr) == 0)
            {
                winsize = Marshal.PtrToStructure<Winsize>(winsizePtr);
                return (winsize.ws_row, winsize.ws_col);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(winsizePtr);
        }
        
        return (24, 80); // Default
    }

    /// <summary>
    /// Set terminal window size.
    /// </summary>
    public static void SetWindowSize(int fd, int rows, int cols)
    {
        var winsize = new Winsize
        {
            ws_row = (ushort)rows,
            ws_col = (ushort)cols,
            ws_xpixel = 0,
            ws_ypixel = 0
        };
        
        IntPtr winsizePtr = Marshal.AllocHGlobal(Marshal.SizeOf(winsize));
        
        try
        {
            Marshal.StructureToPtr(winsize, winsizePtr, false);
            ioctl(fd, TIOCSWINSZ, winsizePtr);
        }
        finally
        {
            Marshal.FreeHGlobal(winsizePtr);
        }
    }

    /// <summary>
    /// Copy window size from source to destination file descriptor.
    /// </summary>
    public static void CopyWindowSize(int sourceFd, int destFd)
    {
        var (rows, cols) = GetWindowSize(sourceFd);
        SetWindowSize(destFd, rows, cols);
    }
}
