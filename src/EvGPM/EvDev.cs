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

    // Mouse buttons
    public const ushort BTN_LEFT = 0x110;
    public const ushort BTN_RIGHT = 0x111;
    public const ushort BTN_MIDDLE = 0x112;
    public const ushort BTN_SIDE = 0x113;
    public const ushort BTN_EXTRA = 0x114;

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

    public static int Open(string devicePath)
    {
        return open(devicePath, O_RDONLY);
    }

    public static void Close(int fd)
    {
        if (fd >= 0)
            close(fd);
    }

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

    public static void Grab(int fd, bool grab)
    {
        IntPtr arg = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(arg, grab ? 1 : 0);
            ioctl(fd, EVIOCGRAB, arg);
        }
        finally
        {
            Marshal.FreeHGlobal(arg);
        }
    }

    public static unsafe string GetDeviceName(int fd)
    {
        const int bufferSize = 256;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        
        try
        {
            // Clear buffer
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            int result = ioctl(fd, EVIOCGNAME, buffer);
            if (result > 0)
            {
                return Marshal.PtrToStringAnsi(buffer) ?? "Unknown";
            }
            return "Unknown";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static unsafe bool HasEventType(int fd, ushort eventType)
    {
        const int bitsPerByte = 8;
        const int maxBits = 256;
        int byteSize = maxBits / bitsPerByte;
        IntPtr buffer = Marshal.AllocHGlobal(byteSize);
        
        try
        {
            // Clear buffer
            for (int i = 0; i < byteSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            uint request = EVIOCGBIT | ((uint)eventType << 8);
            int result = ioctl(fd, request, buffer);
            
            if (result >= 0)
            {
                // Check if any bit is set
                byte* ptr = (byte*)buffer;
                for (int i = 0; i < byteSize; i++)
                {
                    if (ptr[i] != 0)
                        return true;
                }
            }
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
