# Moonlight Mobile

A UWP game streaming client for **Windows 10 Mobile**, built with **Visual Studio 2017**. Stream games from a Sunshine or compatible NVIDIA GameStream host to an ARM32 phone.

Based on the official [Moonlight Windows project](https://github.com/moonlight-stream/moonlight-windows). The UWP interface and media integration use the official [moonlight-common-c](https://github.com/moonlight-stream/moonlight-common-c) protocol library. Native dependencies are compiled from source at pinned revisions.

## Features

- Discover PCs on the local network, add an address manually, and save multiple hosts.
- PIN pairing with a persistent client identity and pinned host certificate.
- Browse game cards with cached cover art; launch, resume, or quit a game.
- H.264 SDR video at 480p, 720p, or 1080p, with a fixed **30 FPS** frame rate.
- Adjustable **2-40 Mbps** bitrate; the default is **40 Mbps** at **720p**.
- Stereo 48 kHz audio using the official Opus decoder.
- Touch mouse controls, a connected mouse, and a physical keyboard recognized by Windows.
- Wake-on-LAN, fullscreen playback, and a disconnect confirmation using the system Back button.
- English UWP interface with a black background, system accent color, and bottom AppBar.

The implemented media path supports H.264 SDR and stereo audio. HEVC, AV1, HDR, surround audio, gamepads, and USB forwarding are not implemented.

## Requirements

| Component | Required version |
| --- | --- |
| Visual Studio | 2017, version 15.9 |
| Workload | Universal Windows Platform development, including C++ UWP tools |
| C++ toolset | v141, with ARM32 tools; x86/x64 tools for desktop builds |
| Windows SDK | 10.0.15063.0 |
| Minimum target OS | Windows 10 build 14393 |
| NuGet runtime package | Microsoft.NETCore.UniversalWindowsPlatform 5.2.2 |
| Streaming host | Sunshine or a compatible GameStream server |

The phone target is **ARM32**, not ARM64. Keep the SDK and toolset versions specified by the project. NuGet restore and initial submodule checkout require network access.

## Get the source

Clone this repository with its Git submodules. If it has already been cloned without them, initialize them from the repository root:

```powershell
git submodule update --init --recursive
.\scripts\Bootstrap.ps1
```

Bootstrap verifies the revisions in [dependencies.lock.json](dependencies.lock.json), applies the compatibility patches, and generates the native project. All external libraries come from their official repositories; no third-party prebuilt DLL or LIB is required.

GitHub's automatically generated source ZIP does not include submodule contents. Use a Git clone with submodules to build the project.

## Build

Run from the repository root on Windows:

```powershell
.\scripts\Build.ps1 -Configuration Release -Platform ARM -Package
```

The build script runs Bootstrap and NuGet restore automatically. Release builds use .NET Native. Packages and their framework dependencies are written to `Moonlight/AppPackages/`.

For Visual Studio, run Bootstrap first, open [Moonlight.sln](Moonlight.sln), set **Moonlight** as the startup project, and select **Release | ARM** for the phone. Both projects must use ARM in Configuration Manager. Select **Device** as the deployment target.

Desktop builds use x86 or x64; the native x86 configuration is named Win32:

```powershell
.\scripts\Build.ps1 -Configuration Debug -Platform x64 -Package
```

### Sign a development package

The source project does not contain a signing key. Generate your own development certificate, then pass its PFX explicitly:

```powershell
.\scripts\New-DevCertificate.ps1
.\scripts\Build.ps1 -Configuration Release -Platform ARM -Package `
  -SigningCertificate .\build\signing\MoonlightMobile.pfx
```

The certificate subject must match the manifest publisher, `CN=MoonlightMobile`. The helper creates a local development PFX and public CER in the ignored `build/signing/` directory. Keep the private PFX local. The generated key is for development, not a shared release signing identity.

For sideloading, enable the phone's developer mode, install the public certificate, and install the ARM APPX with its required framework dependencies. Visual Studio deployment is also supported.

### Troubleshooting the build

- **`win10-aot` / Any CPU:** reload the solution, select ARM for both projects, restore NuGet packages, and rebuild. The project declares architecture-specific runtime identifiers for ARM/x86/x64. Any CPU is not a supported configuration.
- **Missing SDK or toolset:** install SDK 15063 and the VS2017 v141 UWP tools. Do not retarget the project automatically to a newer SDK when building for W10M.
- **ILTransform warnings:** VS2017's .NET Native framework directives can report `ILTransform_0027` for `CreateLambda` and `ParameterIsAssignable`. These two framework warnings occurred in successful Release builds.
- **Missing submodule files:** run Bootstrap again and check that Git can access the official dependency repositories.

## Connect to a PC

1. Enable streaming on the host and make sure the phone can reach it.
2. Use **Add PC** or **Discover** in the bottom AppBar, then select the host.
3. Select **Pair** and enter the displayed PIN in Sunshine or the host's pairing prompt.
4. Select **Connect** to load games, then choose a game card. **Refresh** reloads the list.
5. Press the system **Back** button while streaming to choose **Cancel** or **Disconnect**. Disconnecting leaves the game running; **Quit game on PC** in the PC menu closes it explicitly.

Pairing data is stored locally. The client private key uses Windows `DataProtectionProvider` with `LOCAL=user`; host certificates are pinned. Replacing the host certificate requires pairing again.

### Input

| Input | Action |
| --- | --- |
| Tap | Left click |
| Hold for more than 0.5 seconds without moving, then release | Right click |
| Move a finger | Relative mouse movement |
| Connected keyboard/mouse | Forward recognized input to the host |
| Ctrl+Alt+Shift+M | Toggle relative mouse capture |
| Ctrl+Alt+Shift+T | Open the text input dialog |
| Ctrl+Alt+Shift+Q | Open the disconnect confirmation |

Wake-on-LAN requires support and configuration on the host's firmware and network adapter.

## Validation

ARM Release packaging has been checked with VS2017/v141 and SDK 15063. Build success does not establish hardware decoder performance or end-to-end latency on every phone.

An optional desktop AppContainer self-test covers cryptography, identity persistence, discovery parsing, frame queue recovery, keyboard mapping, settings migration, and artwork decoding:

```powershell
.\scripts\Build.ps1 -Configuration Debug -Platform x64 -Package
.\scripts\Test-App.ps1
```

`Test-App.ps1` registers and launches the desktop development package for the current Windows user. It requires Windows developer mode, preserves existing app data when replacing a development registration from this workspace, and writes `build/self-test-Debug-x64.txt`. It does not connect to a streaming host. Do not run it if you only want a source/build check.

See [the latency audit](docs/LATENCY_AUDIT.md) for the current pipeline, queue limits, and optimization tradeoffs.

## Repository layout

| Path | Contents |
| --- | --- |
| `Moonlight/` | C#/XAML UWP application and image assets |
| `Moonlight/Core/` | Protocol control, pairing, discovery, media, input, and persistence source code |
| `native/` | C++/CX bridge, HTTPS integration, and native project |
| `moonlight-common-c/` | Official protocol library submodule, including ENet and nanors |
| `third_party/` | Official Opus and Mbed TLS submodules |
| `patches/` | Reproducible UWP and VS2017 compatibility patches |
| `scripts/` | Bootstrap, build, signing, cleanup, and desktop self-test helpers |
| `docs/` | Technical documentation |

### Clean generated files

```powershell
.\scripts\Clean.ps1 -WhatIf
.\scripts\Clean.ps1
```

Clean removes local build outputs, AppPackages, artifacts, IDE caches, user settings, and local development signing files in the build directories. **It also removes `build/signing/`.** Keep any signing key you need outside generated directories before cleaning. `Moonlight/Core/` is application source and is retained.

## Contributing and license

Read [CONTRIBUTING.md](CONTRIBUTING.md) for build and contribution guidance. Please write issues, pull requests, and public documentation in English.

The application is licensed under [GPL-3.0](License.txt). Upstream acknowledgements, dependency licenses, and source locations are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Preserve those notices, pinned revisions, and compatibility patches when distributing source or binaries.
