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
        string? devicePath = MouseDeviceDiscovery.SelectMouseDevice(config.DevicePath ?? "/dev/input/mice");
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

        // Initialize mouse tracking monitor
        TtyInputMonitor? trackingMonitor = null;
        if (config.HonorTrackingCommands)
        {
            trackingMonitor = new TtyInputMonitor(config.TtyPath);
            Console.WriteLine("Mouse tracking control: Enabled (will only send events when apps request it)");
        }
        else
        {
            Console.WriteLine("Mouse tracking control: Disabled (always sending events)");
        }

        var encoder = new AnsiMouseEncoder(config.Protocol);
        _processor = new MouseEventProcessor(
            encoder, 
            ttyOutput,
            () => trackingMonitor?.MouseTrackingEnabled ?? true
        );

        Console.WriteLine($"Mouse protocol: {config.Protocol}");
        Console.WriteLine("Daemon running. Press Ctrl+C to exit.");
        Console.WriteLine();

        // Start tracking monitor if enabled
        var cts = new CancellationTokenSource();
        if (trackingMonitor != null)
        {
            _ = trackingMonitor.StartMonitoringAsync(cts.Token);
        }

        using var stream = EvDev.OpenAsStream(devicePath);
        int fd = (int)stream.SafeFileHandle.DangerousGetHandle();

        try
        {
            EvDev.Grab(fd, true); // Grab exclusive access

            // Main event loop
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var (success, inputEvent) = await EvDev.ReadEventAsync(stream, cts.Token);
                    
                    if (success)
                    {
                        switch (inputEvent.Type)
                        {
                            case EvDev.EV_KEY:
                                _processor.ProcessButtonEvent(inputEvent);
                                break;

                            case EvDev.EV_ABS:
                                _processor.ProcessAbsoluteMotion(inputEvent);
                                break;

                            case EvDev.EV_REL:
                                _processor.ProcessRelativeMotion(inputEvent);
                                break;

                            case EvDev.EV_SYN:
                                break;
                        }
                    }
                    else
                    {
                        // No event available, small delay
                        await Task.Delay(1, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing event: {ex.Message}");
                    await Task.Delay(100, cts.Token);
                }
            }

            EvDev.Grab(fd, false);
        }
        finally
        {
            stream.Close(); // This will close the fd
        }

        cts.Cancel();
        trackingMonitor?.Dispose();
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

                case "--honor-tracking":
                case "--smart":
                    config.HonorTrackingCommands = true;
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
        public bool HonorTrackingCommands { get; set; } = false;
    }
}
