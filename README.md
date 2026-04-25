<p align="center">
  <img src="assets/Square150x150Logo.scale-200.png" alt="Fenstr icon" width="128">
</p>

# Fenstr

A lightweight tiling window manager for Windows that lets you snap windows into custom regions using mouse or keyboard.

## Features

- **Drag-snap**: Drag a window to the edge of your screen to see a region overlay, then drop to snap
- **Scroll to divide**: Use the scroll wheel while dragging to split your monitor into 1-10 columns (or rows on portrait displays)
- **Span regions**: Hold Shift or right-click drag across multiple regions to create larger zones
- **Keyboard placement mode**: Press a hotkey (default: Win+Ctrl+Space) to enter placement mode, then type a letter to snap the active window
- **Win+Arrow shortcuts**: Move windows between regions with Win+Left/Right
- **Runs in the tray**: Fenstr lives in the system tray with a minimal icon

Fenstr automatically disables Windows 11's built-in Snap Layouts to avoid conflicts, and restores them when it exits.

## Install

### Installer (recommended)

Download `Fenstr-vX.Y.Z-setup.exe` from the [Releases](https://github.com/patrickiel/Fenstr/releases) page and run it. The installer places Fenstr in `Program Files\Fenstr`, which is required for hooking elevated windows (Task Manager, etc.). An optional "Run at login" checkbox registers Fenstr as a startup app.

### Portable

Download the `.zip` from the [Releases](https://github.com/patrickiel/Fenstr/releases) page, extract, and run `Fenstr.exe`. Note: hooks over elevated windows won't work from arbitrary directories (see [Working with elevated windows](#working-with-elevated-windows-thunderbird-task-manager-) below).

Both downloads are self-contained; no separate .NET or Windows App SDK install needed.

### Build from source

Requires Windows 10 1903+ (Windows 11 recommended) and the .NET 9 SDK.

```
git clone https://github.com/patrickiel/Fenstr.git
cd Fenstr
dotnet restore
dotnet build -c Release
```

To publish a self-contained executable (no runtime install required):

```
dotnet publish -c Release -r win-x64 --self-contained
```

Replace `win-x64` with `win-x86` or `win-arm64` for other architectures. The output will be in `bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/`.

## Working with elevated windows (Thunderbird, Task Manager, …)

Edge-drag snapping works for every window. But the mid-drag gestures (right-click to start a span, Shift to span, mouse wheel to change divisions) rely on low-level mouse/keyboard hooks, and Windows UIPI blocks those hooks over elevated windows (Task Manager always, Thunderbird if it was launched as admin). The overlay simply never appears.

The fix is `uiAccess="true"` in `app.manifest` (already set). Windows only honors it when the executable is **signed by a trusted cert** and launched from a **protected directory** (`%ProgramFiles%` or `%SystemRoot%\System32`). If either is missing, Windows silently drops `uiAccess` and the hooks stay blocked over elevated windows.

### One-time dev setup

Create a self-signed code-signing cert and trust it on this machine (elevated PowerShell):

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=Fenstr Dev" -Type CodeSigningCert `
  -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature -KeyExportPolicy Exportable
$pwd = ConvertTo-SecureString -String "temp" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath fenstr-dev.pfx -Password $pwd
Import-PfxCertificate -FilePath fenstr-dev.pfx -CertStoreLocation Cert:\LocalMachine\Root -Password $pwd
Import-PfxCertificate -FilePath fenstr-dev.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPublisher -Password $pwd
```

Place `fenstr-dev.pfx` in the repository root. It's in `.gitignore`. The cert expires after 1 year; regenerate when needed.

### Build + deploy

From an **elevated** terminal (writes to Program Files):

```
dotnet build Fenstr.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:DeployFenstr=true
```

This signs `Fenstr.exe` (via `signtool` from the Windows SDK) and xcopies the output to `C:\Program Files\Fenstr\`. Launch `C:\Program Files\Fenstr\Fenstr.exe`.

Override paths if needed with `-p:FenstrSignPfx=...`, `-p:FenstrSignPassword=...`, `-p:FenstrDeployDir=...`.

### Notes

- Release builds are **self-contained** (`WindowsAppSDKSelfContained=true`). A uiAccess process can't resolve the user's per-user Windows App SDK package registration and crashes on startup without the runtime bundled. Output is ~200 MB for that reason.
- To verify uiAccess is active: in Process Explorer, Fenstr's Security tab shows `UIAccess: True`. Simpler check: right-click or Shift during a drag of a Task Manager window shows the zone overlay.
- Debug builds use `app.Debug.manifest` with `uiAccess="false"` and therefore cannot interact with elevated windows. This is required because VS F5 activation of a packaged `uiAccess="true"` app fails with *"The request is not supported"* (the dev cert isn't trusted and the app isn't in Program Files). That's fine for most dev iteration; only test this feature against a Release deploy. Keep `app.Debug.manifest` in sync with `app.manifest` (they differ only in the `uiAccess` attribute).

## Configuration

Settings are accessible from the tray icon menu. Configuration is stored in `%AppData%\Fenstr\config.json`.

| Setting               | Description                                                                        |
| --------------------- | ---------------------------------------------------------------------------------- |
| Run at login          | Start Fenstr automatically when you sign in                                        |
| Placement mode hotkey | The keyboard shortcut to enter placement mode (default: Win+Ctrl+Space)            |
| Per-monitor divisions | Number of snap regions per display (1-10), adjustable via scroll wheel during drag |
| Region hotkeys        | Assign keys (A-Z, 0-9, F1-F12) to specific regions for keyboard placement          |

## Usage tips

- During a drag, scroll the mouse wheel to change how many regions the monitor is divided into
- Right-click drag (or Shift+drag) across region boundaries to span multiple regions
- In placement mode, press multiple letters quickly to snap a window across those combined regions
- Press Delete while hovering over a region in placement mode to clear its hotkey assignment
- Press Escape to cancel placement mode

## License

MIT
