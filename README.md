# Snapcast Windows

C# Windows service and WinUI 3 tray controls. Target: Windows 11 and Windows Server 2025 Desktop Experience, x64. Older Windows versions are outside support scope.

## Architecture

The boot-start LocalService service owns the bundled Snapclient process in Session 0. It supervises crashes with bounded retry intervals. A kernel job closes child processes when the service exits. Snapclient handles streaming and synchronization; the tray app is included in the installer but is not required for playback. No interactive auto-login is configured.

The tray app starts at sign-in and provides host/audio/control ports, automatic playback, player name, device listing from the service session, output selection, shared/exclusive mode, resampling, latency, volume/mute, reconnect, and a bounded diagnostic log. Closing the window hides it. Right-click the custom play/network-wave tray icon for Open GUI, Restart service, and Exit app. Exit asks whether to keep playback running or stop the service. Service restart/stop uses a fixed installed helper with Windows administrator approval; cancelling approval leaves the tray open. Icon color reflects server-confirmed stream activity: green playing, cyan connected/idle, grey disconnected, red service/status unavailable. This reports Snapserver status, not a physical speaker measurement. Stream routing, buffering and office playlist automation remain server/Music Assistant responsibilities.

Authenticated local users can control this shared office player through an ACL-protected named pipe. Network logons are denied pipe access. Configuration is in `%ProgramData%\SnapcastWindows`; executable paths are fixed in Program Files and cannot be configured through IPC. Administrators and the service can write stored settings; other users can submit validated changes through the pipe. Use this only where signed-in local users are trusted to control office playback.

## Install and configure

Download the `SnapcastWindows-x64` build artifact from Actions and run its installer as an administrator. Both the service and tray app are installed. Enter the Snapserver hostname/IP in the tray controls and save. The Music Assistant preset selects WebSocket audio and HTTP control on 1780. Plain Snapcast defaults are TCP audio 1704 and TCP control 1705; match the actual server configuration (Music Assistant may expose different ports/transports). Volume and name controls require the selected TCP or HTTP(S) control API to be reachable. WebSocket and secure WebSocket audio are supported; secure connections use normal certificate verification. Use List devices to select a physical output visible to LocalService. Avoid an RDP session-only output.

Installer enables Windows Audio and Audio Endpoint Builder automatic startup and registers SnapcastWindows with service recovery. Upgrades stop the old service and tray process. Uninstall preserves configuration. The service plays only when enabled and a host is configured.

## Build

The Actions workflow runs directly in this repository on a Windows x64 self-hosted runner with the custom label `snapcast`. It builds main-branch code changes and supports manual dispatch. Installers and logs appear under this repository's Actions tab. There is no GitHub-hosted fallback or pull-request trigger. The temporary windows-ha-sidebar build bridge has been retired.

### One-time home runner registration

1. Open https://github.com/talporal/snapclient-windows-tray/settings/actions/runners/new and select Windows, x64.
2. On T-NET-SERVER, open PowerShell as administrator and follow GitHub's download/extraction commands in a new directory such as `C:\\actions-runner-snapcast`.
3. Run GitHub's generated configuration command for this repository. Use runner name `T-NET-SERVER-SNAPCAST`, add custom label `snapcast`, and choose installation as a Windows service. Keep this installation separate from the sidebar runner's directory.
4. Confirm the new runner is online in this repository's runner settings. Pending builds with the `snapcast` label can then run.

The registration token is time-limited; enter it only on the host using GitHub's generated command. Do not commit it. Runner registration and Windows service installation require repository administration and access to the host; the connected code-editing tools cannot perform those steps.

Requirements on the Windows build machine: Windows SDK 26100, compatible WinUI XAML build tools and .NET 8 SDK. Workflow installs .NET into the runner temporary directory. The same Windows App SDK package version as the working sidebar is used. Inno Setup is initialized in the runner temp directory; the build does not install/start this application's service on the build PC.

```powershell
.\installer\build.ps1 -Version 0.1.0 -OutputDirectory .\artifacts
```

Snapclient download is version-pinned and SHA-256 checked. The VC runtime and installer bootstrapper signatures are checked. The installer includes .NET/Windows App SDK runtimes, the Snapclient engine and exact upstream source archive.

## Tray startup diagnostics

The tray startup log is `%LOCALAPPDATA%\SnapcastWindows\tray-startup.log`. Published GUI initialization and icon assets are checked during packaging with `--smoke-test`; the check does not install or control a service. The window is activated before shell integration and hidden for background launches. Tray registration retries if Explorer is not ready, and opening the app again signals the existing per-session instance to show the GUI. Fatal startup exceptions are recorded and reported.

The VC runtime bootstrapper is included only once for setup; upgrades remove the previous duplicate from the engine directory.

## Required physical-host acceptance checks

**Not yet verified on the target host.** A successful compile is not proof of service-session sound.

1. Install, configure server/device and confirm speakers produce audio.
2. Reboot, leave the machine at the login screen, send playback from HA/MA and confirm sound physically.
3. Sign in, change volume/output; use Exit tray only, then log out and confirm audio continues. Reopen the GUI; verify Restart service, Cancel exit, and Stop service and exit, including cancelled UAC approval.
4. Disconnect/reconnect network; restart Snapserver; confirm recovery.
5. Unplug/replug the output device and inspect recovery/logs.
6. Connect/disconnect RDP and verify physical output remains selected.
7. Upgrade and uninstall; verify only this app's service/tray registration changes.

If LocalService cannot render to the target driver/device in Session 0, diagnose its permissions and audio initialization before changing service identity. The project does not fall back to an interactive user session, because that would violate pre-login playback.

## Current limits

No automatic app updates, mDNS discovery, server group/stream editor, or installer code-signing yet. Hostname/IP persistence provides automatic connection. Tray volume starts at 50 as a proposed control value; the UI does not yet mirror server-side volume changes. Diagnostics report process state separately from server-confirmed connection; they do not claim audible output.


The packaging check launches the published tray in an interactive desktop when available. A service runner without permission to create an interactive test task reports startup as unverified; this is not a successful UI test. Tray startup diagnostics are written to `%LOCALAPPDATA%\SnapcastWindows\tray-startup.log`.
