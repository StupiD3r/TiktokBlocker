# InstallBlocker

A Windows Service that prevents TikTok Live Studio from being installed or run. It continuously monitors for blocked processes, installers, and directories, and terminates/deletes them immediately.

## Prerequisites

- Windows 10/11
- Administrator privileges (to install the service)

## Quick Install (no SDK required)

Run PowerShell **as Administrator** and paste this:

```powershell
# 1. Download the latest build
curl.exe -LO https://github.com/your-username/InstallBlocker/releases/latest/download/InstallBlocker.zip
Expand-Archive -Path InstallBlocker.zip -DestinationPath "$env:ProgramFiles\InstallBlocker" -Force

# 2. Install the service
& "$env:ProgramFiles\InstallBlocker\InstallBlocker.Installer.exe" install
```

## Manual Install (with .NET 8 SDK)

If you have the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed:

```powershell
# 1. Build the solution
cd InstallBlocker
dotnet publish InstallBlocker.Service -c Release -o bin\publish --self-contained -r win-x64
dotnet publish InstallBlocker.Installer -c Release -o bin\publish --self-contained -r win-x64

# 2. Install the service (run PowerShell as Administrator)
cd bin\publish
.\InstallBlocker.Installer.exe install
```

This publishes two standalone executables (no .NET runtime needed on the target machine), registers the service, configures auto-restart, and starts it.

## Manage the Service

```powershell
# From the publish folder:
.\InstallBlocker.Installer.exe status   # Check status
.\InstallBlocker.Installer.exe uninstall  # Stop and remove
```

Or use `services.msc` — the service is named **InstallBlocker**.

## What Gets Blocked

| Type | Targets |
|---|---|
| Processes | `TikTokLiveStudio`, `TikTokLiveStudioLauncher`, `TikTokLiveStudioInstaller`, `TikTokLiveStudioSetup` |
| Directories | `TikTok LIVE Studio`, `TikTokLiveStudio` |
| Installers | `TikTokLiveStudioInstaller.exe`, `TikTokLiveStudioSetup.exe` |

To add more targets, edit `InstallBlocker.Service/appsettings.json` and restart the service:

```powershell
sc stop InstallBlocker
sc start InstallBlocker
```

## Logs

- **Console output** visible when running interactively
- **Windows Event Log** under source `InstallBlocker` in the `Application` log

## Uninstall

```powershell
dotnet run --project InstallBlocker.Installer -- uninstall
```

Or via `services.msc` → stop the service → delete it, then delete the build folder.
"# TiktokBlocker" 
