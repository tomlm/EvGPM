# EvGPM - Event Device General Purpose Mouse Daemon

A modern Linux console mouse daemon that replaces GPM by using the Linux evdev interface (`/dev/input/event*`) to monitor mouse input and route it to TTY as ANSI escape sequences.

## Features

- ??? **Auto-detection** of mouse devices from `/dev/input/event*`
- ?? **Multiple protocol support**: X10, Normal, and SGR (default)
- ? **Full mouse support**: buttons (left, middle, right, side, extra), movement tracking, and scroll wheel
- ??? **Device grabbing** to prevent interference with other processes
- ??? **Flexible configuration** via command-line arguments
- ?? **Graceful shutdown** with Ctrl+C handling
- ?? **Native P/Invoke implementation** - no external dependencies

## Requirements

- **Linux** operating system
- **.NET 10** runtime
- **Root privileges** to access `/dev/input` devices

## Building

```bash
cd src/EvGPM
dotnet build -c Release
```

The compiled binary will be located at:
```
src/EvGPM/bin/Release/net10.0/EvGPM
```

## Installation

### Option 1: Run from build directory

```bash
sudo dotnet run --project src/EvGPM/EvGPM.csproj
```

### Option 2: Install globally

```bash
# Build and publish
cd src/EvGPM
dotnet publish -c Release -o /opt/evgpm

# Create symlink
sudo ln -s /opt/evgpm/EvGPM /usr/local/bin/evgpm

# Run from anywhere
sudo evgpm
```

### Option 3: Create systemd service

Create `/etc/systemd/system/evgpm.service`:

```ini
[Unit]
Description=EvGPM - Event Device General Purpose Mouse Daemon
After=multi-user.target

[Service]
Type=simple
ExecStart=/opt/evgpm/EvGPM
Restart=on-failure
RestartSec=5s

[Install]
WantedBy=multi-user.target
```

Enable and start the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable evgpm
sudo systemctl start evgpm
```

## Usage

### Basic Usage

```bash
# Auto-detect mouse device and use SGR protocol (recommended)
sudo evgpm
```

### Command-Line Options

```
Usage: evgpm [OPTIONS]

Options:
  -h, --help              Show help message
  -d, --device PATH       Specify mouse device path (e.g., /dev/input/event3)
                         If not specified, auto-detects the first mouse device
  -t, --tty PATH         Specify TTY output path (default: stdout)
  -p, --protocol PROTO   Mouse protocol: X10, Normal, or SGR (default: SGR)
```

### Examples

```bash
# Auto-detect and run with default SGR protocol
sudo evgpm

# Specify a particular mouse device
sudo evgpm -d /dev/input/event3

# Use Normal protocol instead of SGR
sudo evgpm -p Normal

# Output to a specific TTY
sudo evgpm -t /dev/tty1

# Show help
evgpm --help
```

## Mouse Protocols

EvGPM supports three ANSI mouse tracking protocols:

### SGR (1006) - Recommended
- Modern protocol with better coordinate support
- Format: `ESC [ < Cb ; Cx ; Cy M/m`
- Supports button release events
- No coordinate limitations
- **This is the default and recommended protocol**

### Normal (1000)
- Standard xterm mouse protocol
- Format: `ESC [ M Cb Cx Cy`
- Supports button press and release
- Coordinates limited to 223 (255-32)

### X10 (9)
- Original X10 mouse protocol
- Format: `ESC [ M Cb Cx Cy`
- Button press only (no release events)
- Coordinates limited to 223 (255-32)

## Terminal Configuration

To enable mouse support in your terminal applications, you need to enable mouse tracking mode. Most applications do this automatically, but you can test manually:

```bash
# Enable SGR mouse tracking
printf '\033[?1006h\033[?1000h'

# Disable mouse tracking
printf '\033[?1006l\033[?1000l'
```

## Supported Mouse Events

- ? Left button click
- ? Right button click
- ? Middle button click
- ? Side button (back)
- ? Extra button (forward)
- ? Mouse movement (while button held)
- ? Scroll wheel up/down
- ? Relative motion tracking

## Troubleshooting

### Permission Denied

If you see permission errors:

```
Error: Cannot open device: /dev/input/event3
```

**Solution**: Run with sudo:
```bash
sudo evgpm
```

Alternatively, add your user to the `input` group:
```bash
sudo usermod -a -G input $USER
# Log out and back in for changes to take effect
```

### No Mouse Devices Found

If you see:

```
Error: No suitable mouse device found
```

**Solution 1**: List available input devices:
```bash
ls -l /dev/input/event*
```

**Solution 2**: Check which device is your mouse:
```bash
sudo cat /proc/bus/input/devices | grep -A 5 mouse
```

**Solution 3**: Manually specify the device:
```bash
sudo evgpm -d /dev/input/event3
```

### Mouse Not Working in Application

If mouse events aren't appearing in your terminal application:

1. Ensure your application supports ANSI mouse tracking
2. Verify the application has enabled mouse mode
3. Try a different protocol:
   ```bash
   sudo evgpm -p Normal
   ```

### Testing Mouse Events

You can test if mouse events are being sent by running:

```bash
# In one terminal
sudo evgpm

# In another terminal (should show escape sequences when you move/click mouse)
cat -v
```

## Architecture

```
???????????????????????????????????????????
?         /dev/input/event*               ?
?       (Linux evdev devices)             ?
???????????????????????????????????????????
                  ?
                  ?
???????????????????????????????????????????
?   EvDev.cs (P/Invoke wrapper)           ?
?   - Open/Close devices                  ?
?   - Read input events                   ?
?   - Device capabilities                 ?
???????????????????????????????????????????
                  ?
                  ?
???????????????????????????????????????????
?   MouseEventProcessor.cs                ?
?   - Button press/release                ?
?   - Motion tracking                     ?
?   - Scroll wheel                        ?
???????????????????????????????????????????
                  ?
                  ?
???????????????????????????????????????????
?   AnsiMouseEncoder.cs                   ?
?   - X10 protocol                        ?
?   - Normal protocol                     ?
?   - SGR protocol                        ?
???????????????????????????????????????????
                  ?
                  ?
???????????????????????????????????????????
?   TtyOutputHandler.cs                   ?
?   - Write to TTY/stdout                 ?
???????????????????????????????????????????
                  ?
                  ?
???????????????????????????????????????????
?          Terminal Application           ?
?        (reads ANSI sequences)           ?
???????????????????????????????????????????
```

## Comparison with GPM

| Feature | EvGPM | GPM |
|---------|-------|-----|
| Interface | evdev | PS/2, Serial, USB (via gpm protocol) |
| Protocol | ANSI/VT escape sequences | GPM protocol + selection |
| Dependencies | None (native P/Invoke) | libgpm |
| Modern .NET | ? .NET 10 | ? C library |
| Easy deployment | ? Single binary | ? System package |
| Text selection | ? Not yet | ? |

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is open source. Please check the repository for license details.

## Author

Created as a modern replacement for the traditional GPM daemon using .NET and Linux evdev.

---

**Note**: This is designed for Linux console/TTY environments. For GUI applications, use the native window system mouse handling instead.
