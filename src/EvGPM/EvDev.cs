using System.Runtime.InteropServices;

namespace EvGPM;

/// <summary>
/// Native Linux evdev interop layer
/// </summary>
public static class EvDev
{
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

    // ioctl commands
    private const uint EVIOCGRAB = 0x40044590;
    private const uint EVIOCGNAME = 0x80ff4506;
    private const uint EVIOCGBIT = 0x80ff4520;

    // File operations
    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 0x800;

    [StructLayout(LayoutKind.Sequential)]
    public struct InputEvent
    {
        public long TimeSeconds;
        public long TimeMicroseconds;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, IntPtr buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, IntPtr arg);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl_int(int fd, uint request, ref int arg);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl_bytes(int fd, uint request, byte[] arg);

    /// <summary>
    /// Opens an input device for reading in blocking mode.
    /// </summary>
    /// <param name="devicePath">Path to the device file (e.g., /dev/input/event0)</param>
    /// <returns>File descriptor on success, -1 on failure</returns>
    public static int Open(string devicePath)
    {
        return open(devicePath, O_RDONLY);
    }

    /// <summary>
    /// Closes an open device file descriptor.
    /// </summary>
    /// <param name="fd">File descriptor to close</param>
    public static void Close(int fd)
    {
        if (fd >= 0)
            close(fd);
    }

    /// <summary>
    /// Reads a single input event from the device synchronously (blocking).
    /// </summary>
    /// <param name="fd">File descriptor of the open device</param>
    /// <param name="evt">Output parameter containing the read event</param>
    /// <returns>True if an event was successfully read, false otherwise</returns>
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
    /// When grabbed, other applications cannot read events from this device.
    /// </summary>
    /// <param name="fd">File descriptor of the open device</param>
    /// <param name="grab">True to grab exclusive access, false to release</param>
    public static void Grab(int fd, bool grab)
    {
        int value = grab ? 1 : 0;
        ioctl_int(fd, EVIOCGRAB, ref value);
    }

    /// <summary>
    /// Retrieves the human-readable name of the input device.
    /// </summary>
    /// <param name="fd">File descriptor of the open device</param>
    /// <returns>Device name string, or "Unknown" if the name cannot be retrieved</returns>
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
    /// Checks whether the device supports a specific event type (e.g., EV_KEY, EV_REL, EV_ABS).
    /// </summary>
    /// <param name="fd">File descriptor of the open device</param>
    /// <param name="eventType">Event type constant to check (e.g., EV_KEY, EV_REL)</param>
    /// <returns>True if the device supports the event type, false otherwise</returns>
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
    /// Opens an input device as a FileStream in non-blocking mode for asynchronous reading.
    /// </summary>
    /// <param name="devicePath">Path to the device file (e.g., /dev/input/event0)</param>
    /// <returns>FileStream wrapping the device file descriptor</returns>
    /// <exception cref="IOException">Thrown if the device cannot be opened</exception>
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
    /// Returns immediately if no event is available (non-blocking).
    /// </summary>
    /// <param name="stream">FileStream opened with OpenAsStream</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Tuple containing success flag and the input event if successful</returns>
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
}
