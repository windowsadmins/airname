# AirName for Windows

A lightweight Windows application that displays your computer's friendly name
as a floating label on the taskbar and in the system tray. Built with .NET 10
WinForms and Win32 layered windows.

Great for shared device environments like offices, labs, and classrooms.

Windows equivalent of [AirName for macOS](https://github.com/rodchristiansen/airname).

## Features

- **Floating Taskbar Label**: Always-visible text overlay on the taskbar showing the friendly name, using per-pixel alpha compositing via UpdateLayeredWindow
- **System Tray Icon**: Tooltip shows the friendly name; right-click menu offers bold name display and copy-to-clipboard
- **Dark/Light Mode**: Automatically adapts text color based on the system theme (reads SystemUsesLightTheme registry value)
- **Always On Top**: Uses SetWinEventHook to instantly re-assert topmost when any window takes focus, plus a 500ms polling fallback
- **Click-Through**: The floating label is fully transparent to mouse input (WS_EX_TRANSPARENT + HTTRANSPARENT)
- **Auto-Refresh**: Picks up name changes every 5 minutes, on session switch, and on display/theme changes
- **Self-Contained**: Single-file executable with embedded runtime (~110 MB x64, ~120 MB arm64)
- **Single Instance**: Global mutex prevents duplicate instances
- **Dual Architecture**: Native x64 and arm64 builds

## How It Works

AirName reads the computer's **sharing name** from the Windows registry:

```
HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\srvcomment
```

This is the same value set by `net config server /srvcomment:"Friendly Name"` and
visible in Windows network browsing. If no sharing name is set, AirName falls
back to the standard Windows hostname (`$env:COMPUTERNAME`).

The floating label is rendered as a Win32 layered window (UpdateLayeredWindow with
32bpp ARGB bitmap) positioned at the bottom-left of the screen, overlaying the
taskbar. Text is drawn with a subtle drop shadow for readability against both
light and dark taskbar backgrounds.

## Building

Requires .NET 10 SDK.

```powershell
# Development build
dotnet build src/AirName.csproj

# Production build (self-contained, single-file, dual-arch, signed)
./build.ps1

# Skip signing for local testing
./build.ps1 -NoSign
```

The build script auto-detects the enterprise signing certificate and signtool.exe
from the Windows SDK. Output goes to `release/x64/` and `release/arm64/`.

## Installation

1. Copy `airname.exe` to `C:\Program Files\AirName\`
2. Create a startup entry (shortcut in shell:startup, or scheduled task at logon)

For enterprise deployment via Cimian:
- Deployed as a signed package via `managedsoftwareupdate`
- Autostart managed by deployment system

## Configuration

No configuration needed. AirName reads the sharing name that your deployment
system (Cimian preflight, MDM, or manual `net config server`) has already set.

To set a sharing name manually:
```powershell
# PowerShell (admin)
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters' `
    -Name 'srvcomment' -Value 'My Friendly Name'
```

Or:
```cmd
net config server /srvcomment:"My Friendly Name"
```

## Requirements

- Windows 10 1903+ or Windows 11

## License

MIT License - see [LICENSE](LICENSE) for details.
