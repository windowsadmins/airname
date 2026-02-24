# AirName for Windows

A lightweight Windows system tray application that displays your computer's
friendly name for easy identification. Built with .NET 10 WinForms.

Great for shared device environments like offices, labs, and classrooms.

Windows equivalent of [AirName for macOS](https://github.com/rodchristiansen/airname).

## Features

- **System Tray Display**: Shows your computer's friendly name as the tray icon tooltip
- **Right-Click Menu**: Displays friendly name (bold), hostname, and copy-to-clipboard
- **Auto-Refresh**: Picks up name changes every 5 minutes and on session switch
- **Lightweight**: Framework-dependent single-file executable (~180KB)
- **No User Interaction Required**: Runs silently in the background
- **Single Instance**: Mutex prevents duplicate instances

## How It Works

AirName reads the computer's **sharing name** from the Windows registry:

```
HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\srvcomment
```

This is the same value set by `net config server /srvcomment:"Friendly Name"` and
visible in Windows network browsing. If no sharing name is set, AirName falls
back to the standard Windows hostname (`$env:COMPUTERNAME`).

## Building

Requires .NET 10 SDK.

```powershell
# Development build
dotnet build src/AirName.csproj

# Production build (single-file, self-contained)
./build.ps1

# With code signing
./build.ps1 -Sign -Thumbprint <certificate-thumbprint>
```

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
- .NET 9 Desktop Runtime (included with .NET SDK)

## License

MIT License - see [LICENSE](LICENSE) for details.
