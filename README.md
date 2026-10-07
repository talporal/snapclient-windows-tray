# Sendspin Windows Speaker

This repository replaces its previous Snapcast implementation with a **Sendspin-only** C# Windows service and desktop tray/settings app. The repository name remains `snapclient-windows-tray` to preserve project continuity. Windows 11 and Windows Server 2025 Desktop Experience, x64.

## First milestone: normal desktop playback

Install the release while signed into Windows. The compact main window shows status, server, output and live volume/mute. Gear buttons reveal server/audio settings; Diagnostics opens the diagnostic panel. Volume changes apply without an Apply button, and setting-file writes are coalesced after slider movement. “Enable playback and reconnect automatically” controls connection/retry, not Windows service startup. Right-click the play/network-wave tray icon for **Settings**, **Restart service**, or **Exit app**. Closing the settings window hides it; Exit asks whether to stop the service too. Playback belongs to the service, not the GUI. Restart/stop requests Windows administrator approval.

1. Open Settings. Wait for discovered Sendspin servers, select one, then Save and reconnect. Or enter an address, port (usually 8927) and WebSocket path (usually `/sendspin`). With a blank address, exactly one discovered server is selected automatically; multiple servers require a choice.
2. Select the physical Windows output from the device dropdown. Save, then use **Test speakers** and confirm the short tone is audible. The test briefly interrupts/reconnects any active stream.
3. In Music Assistant or another Sendspin server, choose this computer's player name and play music. The app does not log into Home Assistant or Music Assistant; the server sends audio to it over the local Sendspin endpoint.
4. Confirm tray/settings work, audible playback works and music continues with the settings window hidden. Then test exit tray while keeping the service running.

**Physical playback and desktop GUI operation still require target-host acceptance.** CI compilation/protocol checks are not evidence that the speakers produced sound. Login-screen/pre-login playback is deliberately deferred until this milestone works.

## Settings and discovery

Player name, server address/port/path, output device, automatic connection, discovery, volume/mute, Windows output buffer and network buffering are saved in `%ProgramData%\SendspinWindows\settings.json`. Client identity persists across service restarts. Empty device ID follows the Windows multimedia default output; an unavailable selected device reports an error rather than silently choosing a different device. Avoid RDP-only audio endpoints when selecting the physical speakers.

Discovery uses `_sendspin-server._tcp.local.` mDNS on the LAN; it may not cross VM/VLAN/network boundaries. Manual address/port works independently. The installer adds a program-scoped UDP 5353 rule on Private/Domain profiles. Client-initiated connections are used; the app does not advertise a competing server-initiated listener.

## Dependencies and protocol compatibility

The service uses Sendspin.SDK **9.3.3**, the upstream maintained plaintext compatibility line, and NAudio.Wasapi **2.2.1** for the built-in Windows audio backend. PCM, Opus and FLAC decoding, time probes, timed buffering and bounded sync correction are provided by the SDK. The only advertised role is `player@v1`; no account sign-in, artwork or controller role is needed. This build does not implement the SDK 10.x encrypted/pairing protocol; use the server's compatible legacy Sendspin endpoint on a trusted LAN.

The tray uses styled WPF and Windows notification-area integration. .NET is bundled. There is **no Snapclient executable, VC++ redistributable, Windows App SDK runtime, Python or Node runtime** in the installation. NuGet restores managed build dependencies, not separate user-installed runtimes. Third-party licenses are included in Notices.

## Upgrade and service

The installer retains the original app identity, stops/removes the old Snapcast service and tray registration, replaces their executable payload and installs SendspinWindows. Old Snapcast settings are preserved but are not imported as Sendspin server settings. The service runs as LocalService with access to its own settings directory. Authenticated local users control the shared speaker through a bounded, ACL-protected named pipe; network logons cannot use that pipe.

No test before login is required for packaging. The installed service is configured for automatic startup, but boot-screen audio behavior is not part of the first acceptance milestone.

## Builds and downloads

Every successful build publishes the installer **directly to GitHub Releases** as a development release. No Actions artifact upload/download step is used. Source compilation/checks run only on the confirmed home self-hosted Windows runner `T-NET-SERVER`; no GitHub-hosted fallback. Build output is clean, with unique temporary package directories. Pushes to main or manual workflow dispatch build this repository directly.

```powershell
.\installer\build.ps1 -Version 0.2.0 -OutputDirectory .\artifacts
```

Requires a Windows .NET 8 SDK. Installer tooling is initialized in the runner's temporary directory. Tests cover connection settings, stable identity, PCM decoding, output callback silence/gain semantics and an actual local Sendspin WebSocket handshake/audio dispatch with a fake output backend. They do not install a service or request physical audio from the build machine.

GUI failures are recorded in `%LOCALAPPDATA%\SendspinWindows\tray-startup.log`. The Settings diagnostics panel shows service, connection and audio-output messages plus rendered-frame counts. A rendering count confirms samples reached the backend callback, not that an attached speaker was audible.
