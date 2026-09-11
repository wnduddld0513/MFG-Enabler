# MFG-Enabler 1.0

MFG-Enabler is a Windows tool for enabling Multi Frame Generation (MFG) on GeForce RTX 20, 30, and 40 series GPUs in supported games.

It downloads and applies [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86), backs up replaced files, and restores them when you remove the mod. It also includes a custom NVAPI proxy inspired by [FakeNvAPI](https://github.com/optiscaler/fakenvapi), which reports an RTX 5080 identity for supported GPU checks to unlock in-game MFG options.

## Features

- Download, apply, and restore dlssg_for_sm86 from one interface.
- Enable or disable the bundled NVAPI proxy separately from MFG.
- Find games through NVIDIA App detection data, or add a game executable manually.
- Back up original files and verify file integrity during installation and restoration.
- Optionally check for dlssg_for_sm86 updates. Automatic updates are off by default.
- Switch between English and Korean in the app settings.
- Check for MFG-Enabler releases at startup, choose Stable or Beta in Settings, and apply ZIP updates with an automatic restart.

MFG operation has been confirmed on RTX 20, 30, and 40 series GPUs. Compatibility depends on the game and how it loads the mod and queries the GPU; this does not imply support for every game.

## Download

Get the latest build from [GitHub Releases](https://github.com/wnduddld0513/MFG-Enabler/releases/latest):

- **MSI installer**: download `MFG-Enabler-Setup-1.0.0.msi` and run Setup.
- **Portable ZIP**: download `MFG-Enabler-Package-1.0.zip`, extract the entire archive, and launch `MFG-Enabler.exe`.

Choose the MSI or portable ZIP under **Assets**. GitHub's automatically generated **Source code** archives do not contain a runnable application.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11, x64.
- A compatible NVIDIA GeForce RTX GPU and game.
- Internet access to download dlssg_for_sm86 and check for updates.
- NVIDIA App for automatic game discovery. Games can also be added manually.

## Usage

1. Launch `MFG-Enabler.exe` from the distribution folder. Keep all accompanying files and subfolders beside it.
2. Click **Refresh** to find games, or use the **⋮** menu to add a game executable manually.
3. Select the game and its rendering executable, then enable MFG and the NVAPI option as needed.
4. Launch the game and configure its MFG options.
5. To undo the changes, close the game and use **Restore** to restore both components.

The distribution includes the .NET and Windows App SDK runtimes. English is the default language; you can switch to Korean in the app settings.

Application updates are separate from dlssg_for_sm86 updates. Stable and Beta only notify about their own releases. **Don't show again** hides the offered version; newer releases can still notify. See [application updates](docs/APP-UPDATES.md) for release naming, packaging, and recovery details.

## Build from source

1. Install Visual Studio 2026 with the .NET 10 SDK and WinUI development components.
2. Open `MFG-Enabler.sln`, allow NuGet restore, and select **Release / x64**.
3. Build the solution, or run the following command from the project folder to publish the application:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The publish output is `dist/MFG-Enabler.exe` together with its required runtime files.

Every application build compiles the NVAPI proxy from `src/native/nvapi_proxy.c` and embeds the resulting DLL. The pinned Zig compiler archive is included in `src/native/tools` and its SHA-256 is verified before use. Building the DLL does not require a separate NVIDIA SDK or a compiler download.

To move the build workspace to another PC, copy the entire project folder, including `src/native/tools`. The destination still needs the .NET 10 SDK, WinUI development components, and NuGet package restore.

The English MSI installer source is in `MFG-Enabler-Installer`. It installs to `C:\Program Files\MFGEnabler` by default and includes an optional desktop shortcut. See [the installer build guide](MFG-Enabler-Installer/README.md).

To build only the NVAPI DLL, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\native\build.ps1
```

The DLL output is `src/native/bin/nvapi64.dll`.

## Source layout

- `WinUI/`: application interface, language support, and application configuration.
- `src/Core.cs`: installation, backups, and restoration.
- `src/Discovery.cs`: game discovery and executable validation.
- `src/Updates.cs`: dlssg_for_sm86 downloads and updates.
- `src/payload.lock`: baseline runtime file hashes.
- `src/native/`: NVAPI proxy source, build script, and bundled compiler.

## Credits and license

- [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) provides the frame generation runtime, downloaded from its upstream repository.
- [FakeNvAPI](https://github.com/optiscaler/fakenvapi) inspired the bundled NVAPI identity proxy. The proxy is a separate implementation, not an upstream FakeNvAPI binary; see [NVAPI implementation details](docs/SPOOF5080.md).
- [Zig](https://ziglang.org/) is used to compile the native proxy.

MFG-Enabler is licensed under the [GNU General Public License version 3](LICENSE) (GPL-3.0-only), including its independent NVAPI proxy, to align with the GPLv3 source license declared by dlssg_for_sm86. Copyright (c) 2026 MFG Enabler contributors.

Third-party components retain their respective licenses. NVIDIA runtime, model, graph, and kernel assets are not relicensed by this GPL declaration. See [upstream notices](docs/UPSTREAM-NOTICES.txt) and [licensing details](docs/LICENSING.md).
