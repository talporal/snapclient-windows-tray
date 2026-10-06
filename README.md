# Snapcast Windows

C# Windows service and WinUI 3 tray controls. Target: Windows 11 and Windows Server 2025 Desktop Experience, x64. Older Windows versions are outside support scope.

## Architecture

The boot-start LocalService service owns the bundled Snapclient process in Session 0. It supervises crashes with bounded retry intervals. A kernel job closes child processes when the service exits. Snapclient handles streaming and synchronization; the tray app is included in the installer but is not required for playback. No interactive auto-login is configured.

The tray app starts at sign-in and provides host/audio/control ports, automatic playback, player name, device listing from the service session, output selection, shared/exclusive mode, resampling, latency, volume/mute, reconnect, and a bounded diagnostic log. Closing the window hides it; Exit tray leaves the service running. Stream routing, buffering and office playlist automation remain server/Music Assistant responsibilities.

Authenticated local users can control this shared office player through an ACL-protected named pipe. Network logons are denied pipe access. Configuration is in `%ProgramData%\SnapcastWindows`; executable paths are fixed in Program Files and cannot be configured through IPC. Administrators and the service can write stored settings; other users can submit validated changes through the pipe. Use this only where signed-in local users are trusted to control office playback.

## Install and configure

Download the `SnapcastWindows-x64` build artifact from Actions and run its installer as an administrator. Both the service and tray app are installed. Enter the Snapserver hostname/IP in the tray controls and save. Default Snapcast ports are audio 1704 and TCP control 1705; match the actual server configuration (Music Assistant may expose different ports/transports). Volume and name controls require the TCP control API to be reachable. Use List devices to select a physical output visible to LocalService. Avoid an RDP session-only output.

Installer enables Windows Audio and Audio Endpoint Builder automatic startup and registers SnapcastWindows with service recovery. Upgrades stop the old service and tray process. Uninstall preserves configuration. The service plays only when enabled and a host is configured.

## Build

The Actions workflow uses **only** `[self-hosted, Windows]`, matching windows-ha-sidebar's existing workflow. No GitHub-hosted fallback and no untrusted pull-request trigger. A repository-scoped runner from another repository must also be registered for this repository; matching labels alone do not share it.

Requirements on the Windows build machine: Windows SDK 26100, compatible WinUI XAML build tools and .NET 8 SDK. Workflow installs .NET into the runner temporary directory. The same Windows App SDK package version as the working sidebar is used. Inno Setup is initialized in the runner temp directory; the build does not install/start this application's service on the build PC.

```powershell
.\installer\build.ps1 -Version 0.1.0 -OutputDirectory .\artifacts
```

Snapclient download is version-pinned and SHA-256 checked. The VC runtime and installer bootstrapper signatures are checked. The installer includes .NET/Windows App SDK runtimes, the Snapclient engine and exact upstream source archive.

## Required physical-host acceptance checks

**Not yet verified on the target host.** A successful compile is not proof of service-session sound.

1. Install, configure server/device and confirm speakers produce audio.
2. Reboot, leave the machine at the login screen, send playback from HA/MA and confirm sound physically.
3. Sign in, change volume/output, exit the tray app, then log out; confirm audio continues.
4. Disconnect/reconnect network; restart Snapserver; confirm recovery.
5. Unplug/replug the output device and inspect recovery/logs.
6. Connect/disconnect RDP and verify physical output remains selected.
7. Upgrade and uninstall; verify only this app's service/tray registration changes.

If LocalService cannot render to the target driver/device in Session 0, diagnose its permissions and audio initialization before changing service identity. The project does not fall back to an interactive user session, because that would violate pre-login playback.

## Current limits

No automatic app updates, mDNS discovery, server group/stream editor, or installer code-signing yet. Hostname/IP persistence provides automatic connection. Tray volume starts at 50 as a proposed control value; the UI does not yet mirror server-side volume changes. Diagnostics report process state separately from server-confirmed connection; they do not claim audible output.

