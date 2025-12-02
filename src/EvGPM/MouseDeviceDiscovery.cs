namespace EvGPM;

/// <summary>
/// Discovers and filters mouse devices from /dev/input/event* devices
/// </summary>
public class MouseDeviceDiscovery
{
    public static List<string> DiscoverMouseDevices()
    {
        var mouseDevices = new List<string>();
        var inputDir = "/dev/input";

        if (!Directory.Exists(inputDir))
        {
            Console.Error.WriteLine($"Error: {inputDir} directory not found");
            return mouseDevices;
        }

        // Enumerate all event devices
        var eventFiles = Directory.GetFiles(inputDir, "event*")
            .OrderBy(f => f)
            .ToList();

        foreach (var devicePath in eventFiles)
        {
            try
            {
                int fd = EvDev.Open(devicePath);
                if (fd < 0)
                    continue;

                try
                {
                    // Check if device has mouse capabilities
                    if (IsMouseDevice(fd))
                    {
                        string deviceName = EvDev.GetDeviceName(fd);
                        mouseDevices.Add(devicePath);
                        Console.WriteLine($"Found mouse device: {devicePath} ({deviceName})");
                    }
                }
                finally
                {
                    EvDev.Close(fd);
                }
            }
            catch (Exception ex)
            {
                // Silently skip devices we can't access
                Console.Error.WriteLine($"Warning: Cannot access {devicePath}: {ex.Message}");
            }
        }

        return mouseDevices;
    }

    private static bool IsMouseDevice(int fd)
    {
        // A mouse typically has:
        // - Relative axes (REL_X, REL_Y)
        // - Button events (BTN_LEFT, BTN_RIGHT, BTN_MIDDLE)
        
        bool hasRelativeMovement = EvDev.HasEventType(fd, EvDev.EV_REL);
        bool hasButtons = EvDev.HasEventType(fd, EvDev.EV_KEY);

        return hasRelativeMovement && hasButtons;
    }

    public static string? SelectMouseDevice(string? preferredDevice = null)
    {
        if (!string.IsNullOrEmpty(preferredDevice) && File.Exists(preferredDevice))
        {
            Console.WriteLine($"Using specified device: {preferredDevice}");
            return preferredDevice;
        }

        var devices = DiscoverMouseDevices();
        
        if (devices.Count == 0)
        {
            Console.Error.WriteLine("No mouse devices found!");
            return null;
        }

        // Return the first mouse device found
        return devices[0];
    }
}
