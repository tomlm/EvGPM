using EvGPM;

class Program
{
    private static bool _running = true;
    private static MouseEventProcessor? _processor;

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("EvGPM - Event Device General Purpose Mouse Daemon");
        Console.WriteLine("=================================================");
        
        // Parse command line arguments
        var config = ParseArguments(args);
        
        if (config.ShowHelp)
        {
            ShowHelp();
            return 0;
        }

        // Check if we're running on Linux
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Error: EvGPM only works on Linux systems");
            return 1;
        }

        // Discover and select mouse device
        string? devicePath = MouseDeviceDiscovery.SelectMouseDevice(config.DevicePath);
        if (devicePath == null)
        {
            Console.Error.WriteLine("Error: No suitable mouse device found");
            Console.Error.WriteLine("Try running with 'sudo' if you get permission errors");
            return 1;
        }

        Console.WriteLine($"Using mouse device: {devicePath}");
        
        // Set up signal handlers for graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _running = false;
            Console.WriteLine("\nShutting down...");
        };

        try
        {
            await RunDaemon(devicePath, config);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static async Task RunDaemon(string devicePath, DaemonConfig config)
    {
        using var ttyOutput = new TtyOutputHandler(config.TtyPath);
        
        if (!ttyOutput.IsValid())
        {
            throw new InvalidOperationException("Cannot write to TTY output");
        }

        var encoder = new AnsiMouseEncoder(config.Protocol);
        _processor = new MouseEventProcessor(encoder, ttyOutput);

        Console.WriteLine($"Mouse protocol: {config.Protocol}");
        Console.WriteLine("Daemon running. Press Ctrl+C to exit.");
        Console.WriteLine();

        int fd = EvDev.Open(devicePath);
        if (fd < 0)
        {
            throw new InvalidOperationException($"Cannot open device: {devicePath}");
        }

        try
        {
            EvDev.Grab(fd, true); // Grab exclusive access to prevent interference

            // Main event loop
            while (_running)
            {
                try
                {
                    if (EvDev.ReadEvent(fd, out var inputEvent))
                    {
                        switch (inputEvent.Type)
                        {
                            case EvDev.EV_KEY:
                                // Button press/release
                                _processor.ProcessButtonEvent(inputEvent);
                                break;

                            case EvDev.EV_REL:
                                // Relative motion or wheel
                                _processor.ProcessRelativeMotion(inputEvent);
                                break;

                            case EvDev.EV_SYN:
                                // Sync event - marks end of event batch
                                break;
                        }
                    }
                    else
                    {
                        // No event available, small delay to prevent CPU spinning
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex) when (_running)
                {
                    Console.Error.WriteLine($"Error processing event: {ex.Message}");
                    await Task.Delay(100); // Back off on errors
                }
            }

            EvDev.Grab(fd, false); // Release device
        }
        finally
        {
            EvDev.Close(fd);
        }

        Console.WriteLine("Daemon stopped.");
    }

    static DaemonConfig ParseArguments(string[] args)
    {
        var config = new DaemonConfig();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    config.ShowHelp = true;
                    break;

                case "-d":
                case "--device":
                    if (i + 1 < args.Length)
                        config.DevicePath = args[++i];
                    break;

                case "-t":
                case "--tty":
                    if (i + 1 < args.Length)
                        config.TtyPath = args[++i];
                    break;

                case "-p":
                case "--protocol":
                    if (i + 1 < args.Length)
                    {
                        if (Enum.TryParse<AnsiMouseEncoder.MouseProtocol>(args[++i], true, out var protocol))
                            config.Protocol = protocol;
                    }
                    break;
            }
        }

        return config;
    }

    static void ShowHelp()
    {
        Console.WriteLine(@"
Usage: evgpm [OPTIONS]

Options:
  -h, --help              Show this help message
  -d, --device PATH       Specify mouse device path (e.g., /dev/input/event3)
                         If not specified, auto-detects the first mouse device
  -t, --tty PATH         Specify TTY output path (default: stdout)
  -p, --protocol PROTO   Mouse protocol: X10, Normal, or SGR (default: SGR)

Examples:
  evgpm                          # Auto-detect mouse, use SGR protocol
  evgpm -d /dev/input/event3     # Use specific device
  evgpm -p Normal                # Use Normal protocol instead of SGR
  evgpm -t /dev/tty1             # Output to specific TTY

Note: May require root privileges to access /dev/input devices.
      Run with 'sudo' if you get permission errors.
");
    }

    class DaemonConfig
    {
        public bool ShowHelp { get; set; }
        public string? DevicePath { get; set; }
        public string? TtyPath { get; set; }
        public AnsiMouseEncoder.MouseProtocol Protocol { get; set; } = AnsiMouseEncoder.MouseProtocol.SGR;
    }
}
