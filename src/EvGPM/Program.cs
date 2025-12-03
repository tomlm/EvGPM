using EvGPM;

class Program
{
    private static MouseEventProcessor? _processor;
    private static CancellationTokenSource? _cts;

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
        
        // Create cancellation token source for clean shutdown
        _cts = new CancellationTokenSource();
        
        // Set up signal handlers for graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down...");
            _cts?.Cancel();
        };

        try
        {
            await RunDaemon(devicePath, config, _cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Daemon cancelled.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            _cts?.Dispose();
        }
    }

    static async Task RunDaemon(string devicePath, DaemonConfig config, CancellationToken cancellationToken)
    {
        // Determine TTY path
        string ttyPath = config.TtyPath ?? "/dev/tty";
        
        // Create output handler
        var ttyOutput = new TtyOutputHandler(ttyPath);
        
        if (!ttyOutput.IsValid())
        {
            throw new InvalidOperationException($"Cannot write to TTY: {ttyPath}");
        }

        var encoder = new AnsiMouseEncoder(config.Protocol);
        
        // Create processor with smart raw mode detection
        _processor = new MouseEventProcessor(
            encoder,
            ttyOutput,
            () => EvDev.IsInRawMode(ttyPath)
        );

        Console.WriteLine($"Target TTY: {ttyPath}");
        Console.WriteLine($"Mouse protocol: {config.Protocol}");
        Console.WriteLine("Smart mode: Only sending events when TTY is in raw mode");
        Console.WriteLine("Daemon running. Press Ctrl+C to exit.");
        Console.WriteLine();

        // Periodically log TTY state changes
        _ = Task.Run(async () =>
        {
            bool lastRawState = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                bool currentRawState = EvDev.IsInRawMode(ttyPath);
                if (currentRawState != lastRawState)
                {
                    Console.WriteLine($"[TTY State] Raw mode: {currentRawState} - {(currentRawState ? "Sending mouse events" : "NOT sending mouse events")}");
                    lastRawState = currentRawState;
                }
                await Task.Delay(1000, cancellationToken);
            }
        }, cancellationToken);

        using var stream = EvDev.OpenAsStream(devicePath);
        int fd = (int)stream.SafeFileHandle.DangerousGetHandle();

        try
        {
            EvDev.Grab(fd, true);
            Console.WriteLine("✓ Mouse grabbed - Press Ctrl+C to release");

            // Main event loop
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var (success, inputEvent) = await EvDev.ReadEventAsync(stream, cancellationToken);
                    
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
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing event: {ex.Message}");
                    
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation cancelled, cleaning up...");
        }
        finally
        {
            try
            {
                EvDev.Grab(fd, false);
                Console.WriteLine("✓ Mouse released");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error releasing mouse: {ex.Message}");
            }
            
            stream.Close();
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
  -t, --tty PATH         Specify TTY output path (default: /dev/tty)
  -p, --protocol PROTO   Mouse protocol: X10, Normal, or SGR (default: SGR)

  NOTE: EvGPM automatically detects when the TTY is in raw mode (indicating a 
  mouse-aware application like vim, tmux, mc, or emacs is running) and only
  sends mouse events during that time. 

Examples:
  evgpm                          # Auto-detect mouse device
  evgpm -t /dev/tty2             # Send events to specific TTY
  evgpm -d /dev/input/event3     # Use specific mouse device
  evgpm -p Normal                # Use Normal protocol instead of SGR

Testing:
  1. Switch to a TTY (Ctrl+Alt+F2)
  2. Run: sudo evgpm -t /dev/tty2
  3. Start vim: vim -c 'set mouse=a' test.txt
  4. Mouse should work in vim, but not in the shell

Note: Requires root privileges to access /dev/input devices.
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
