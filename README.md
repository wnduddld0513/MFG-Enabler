# MFG-Enabler 1.1

<img width="1940" height="1354" alt="ui2" src="https://github.com/user-attachments/assets/2f6db7c1-3b62-4e9a-b74f-a55a6a1746d2" />
<img width="1940" height="1354" alt="ui1" src="https://github.com/user-attachments/assets/a85754a4-f826-4e03-8b8d-678f7e9ea007" />

MFG-Enabler is a Windows tool for enabling Multi Frame Generation (MFG) on GeForce RTX 20, 30, and 40 series GPUs in supported games.

# Screenshots
Enable in-game MFG option (on RTX 4060 Laptop)
<img width="2560" height="1440" alt="mfg" src="https://github.com/user-attachments/assets/944e0aee-69cd-46c5-8bee-70091ca007af" />


4X MFG (on RTX 4060 Laptop)
<img width="2560" height="1440" alt="4x" src="https://github.com/user-attachments/assets/a4b7a6f2-dda3-4361-bdb7-85bbae5ac025" />


3X MFG (on RTX 4060 Laptop)
<img width="2560" height="1440" alt="3x" src="https://github.com/user-attachments/assets/91939696-5f6c-4105-869a-c73e0ef24370" />


2X FG (on RTX 4060 Laptop)
<img width="2560" height="1440" alt="2x" src="https://github.com/user-attachments/assets/6ee143f5-45ce-4ba4-9ff1-ae95644f85ee" />
Tested on RTX 3070, RTX 4060 Laptop, Cyberpunk 2077


MFG-Enabler is a Windows utility for managing Multi Frame Generation in supported games on GeForce RTX 20, 30, and 40 series GPUs. It downloads the selected runtime, backs up replaced files, and restores the original files when you remove the mod.

## What's new in 1.1

- Choose between **DLSSG 310.9.1 - SilyNoMeta** (default) and **dlssg_for_sm86 - sdli1995** in Settings.
- Configure the DLSSG 310.9.1 INI from the game details panel: maximum and fixed multipliers, Dynamic MFG, target FPS, and four optional optimizations with tooltips.
- Restore the default INI settings with one click. Custom values apply to new installations; runtime updates reset the INI to the upstream defaults.
- Reduced control flicker during operations, clearer update details, and a loading overlay during startup scanning.
- Improved game discovery when NVIDIA metadata is incomplete, while retaining checks for the frame-generation DLL and explicitly blocked profiles.

See the [1.1 release notes](docs/RELEASE-NOTES-1.1.md) for details and upgrade behavior.

## Download

Get [MFG-Enabler 1.1](https://github.com/wnduddld0513/MFG-Enabler/releases/tag/v1.1):

- **Windows installer:** `MFG-Enabler-Setup-1.1.0.msi`.
- **Portable package:** `MFG-Enabler-Package-1.1.zip`. Extract the entire archive and launch `MFG-Enabler.exe`.

Both packages include the .NET and Windows App SDK runtimes. Keep all accompanying files and folders beside the EXE. GitHub's automatic **Source code** archives are for development and do not contain a runnable application.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11, x64.
- A compatible NVIDIA GeForce RTX GPU and game.
- Internet access for runtime downloads and update checks.
- NVIDIA App for automatic game discovery; games can also be added manually.

Compatibility and available frame-generation multipliers depend on the game and runtime. The settings do not guarantee support in every game.

## Usage

1. Launch the application and let the startup scan finish. Click **Refresh** to scan again, or use the **...** menu to add a game executable manually.
2. In **Settings**, choose a **Runtime channel**. The default is **DLSSG 310.9.1 - SilyNoMeta**.
3. Select a game and its rendering executable, choose a proxy, and turn on MFG.
4. For the DLSSG 310.9.1 channel, use **INI settings** below the MFG toggle to configure values for new installations.
5. Launch the game and configure its frame-generation options.
6. Close the game before using **Restore** to restore its original files.

Changing the runtime channel checks the selected source and updates eligible managed games. English and Korean interfaces are available.

## INI settings

The INI editor is shown only for the DLSSG 310.9.1 channel.

| Setting | Values / behavior |
| --- | --- |
| Max multiplier | 2-6 |
| Fixed multiplier | 0 uses the game's selection; a fixed value must not exceed the maximum multiplier |
| Dynamic MFG | On / off |
| Dynamic target FPS | 0 uses the display; otherwise set a target up to 1000 |
| Additional settings | HardwareBilinear, Conv13SharedInput, Conv0SharedInput, ResidualVectorLoads |
| Restore defaults | Returns to the upstream INI defaults |

Changes apply to new installations on this channel. Runtime updates reset the INI to stock values; reapply custom settings as needed.

## Updates

Runtime updates and application updates are separate. On the first migration to the new settings, the application selects the SilyNoMeta runtime and enables automatic runtime updates. You can change both preferences in Settings.

To install application version 1.1 through the updater, choose **Stable** and click **Check for updates**. Users of 1.0b1 or 1.0b2 must also select Stable: the Beta channel only offers Beta releases.

Application packages are checked against their SHA-256 digest and embedded version before installation. See [application updates](docs/APP-UPDATES.md) for release packaging and recovery information.

## Build from source

Install Visual Studio 2026 with the .NET 10 SDK and WinUI development components. Open `MFG-Enabler.sln` and select **Release / x64**, or publish from the project root:

```powershell
.\build.ps1
```

The application and its runtime files are written to `dist`.

To create the versioned ZIP used by GitHub Releases and the application updater:

```powershell
.\build-release.ps1 -Version 1.1
```

The package is written to `releases/MFG-Enabler-Package-1.1.zip`, with a SHA-256 sidecar file. Use a new version number for each public release.

To build the MSI after publishing the application to `dist`:

```powershell
.\MFG-Enabler-Installer\build.ps1 -Version 1.1.0
```

See [the installer guide](MFG-Enabler-Installer/README.md) for details.

## Credits and license

- [SilyNoMeta/dlssg_for_sm86](https://github.com/SilyNoMeta/dlssg_for_sm86) provides the default DLSSG 310.9.1 runtime channel.
- [sdli1995/dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) provides the alternative runtime channel.

MFG-Enabler is licensed under [GNU GPL version 3](LICENSE) (GPL-3.0-only). Copyright (c) 2026 MFG Enabler contributors. Third-party components retain their respective licenses; see [upstream notices](docs/UPSTREAM-NOTICES.txt).
