# MFG-Enabler 1.2b2

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


MFG-Enabler manages the sdli1995/dlssg_for_sm86 runtime in supported Windows games. Version 1.2b2 supports the upstream 0.3.4 source ZIP layout and restores an existing installation before upgrading it.

## Changes in 1.2b2

- Uses sdli1995/dlssg_for_sm86 as the only runtime source. The retired runtime selector has been removed; the editor now uses the supported runtime settings.
- Downloads the latest stable upstream release as a source ZIP pinned to its resolved commit. Only the required runtime files are extracted, verified against the commit's Git blob hashes, and checked for x64 compatibility.
- Supports `version.dll` and the alternatives `winmm.dll`, `dinput8.dll`, `dxgi.dll`, `d3d12.dll`, and `dbghelp.dll`. One selected DLL and `dlssg_sm86.ini` are installed beside the rendering executable.
- Updates restore the previous installation first, then install the verified new files. Original DLLs are backed up in the game's existing `.mfg-enabler` folder.
- Restore removes the runtime INI, including local edits, and the default runtime logs. It restores the original proxy DLL, or removes the proxy when no original existed.

See [the 1.2b2 release notes](docs/RELEASE-NOTES-1.2b2.md).

## Requirements and usage

Windows 10 version 2004 or later, or Windows 11, x64, and an internet connection are required. Runtime compatibility depends on the game and GPU; consult the [upstream 0.3.4 installation guide](https://github.com/sdli1995/dlssg_for_sm86/blob/0.3.4/README.en.md). NVIDIA App is used for automatic game discovery; games can also be added manually.

1. Close the game, launch MFG-Enabler, and select the game's rendering executable.
2. Select a proxy DLL and turn on MFG. Any existing file at that selected DLL path is backed up before replacement.
3. Launch the game and configure its frame-generation options.
4. To remove the installation, close the game and click **Restore**.

Keep the game's `.mfg-enabler` folder: it contains recovery records and original DLL backups. Do not copy a modded DLL over an original backup.

The upstream factory INI is used for installations and updates. Existing custom INI settings are removed during restore and reset during an upgrade.

## INI editor

After installing runtime 0.3.4 or newer, open the selected game's **Runtime INI settings** below the MFG toggle. The editor reads that game's installed `dlssg_sm86.ini`.

| Control | INI setting | Choices |
| --- | --- | --- |
| Optimization tier | `[FrameGeneration] Optimized` | 0: stock; 1: original image, default; 2-3: faster with image-quality loss |
| Frame-generation ceiling | `[FrameGeneration] MaxGeneratedFrames` | 0: runtime limit; 1-5: up to 2X-6X; default 3: 4X |
| Render preset | `[Compatibility] Preset` | Auto, A (UI recomposition off), B (on) |
| Log level | `[Logging] Level` | 0: off; 1: errors; 2: configuration; 3: detailed |

The multiplier is a ceiling; the game chooses the actual count. 6X requires a compatible game. Preset B only works when the game supplies the required HUD/UI data. These choices follow the [upstream configuration guide](https://github.com/sdli1995/dlssg_for_sm86/blob/0.3.4/docs/INSTALL.en.md).

Close the game, adjust the controls, and click **Save settings**. **Reset shown settings** selects the factory values for these four controls; click Save to apply them. Other INI keys, comments, and custom paths are retained. The editor updates the recovery record without changing original DLL backups. Runtime upgrades reset the INI to stock values.
## Updates and recovery

Runtime updates and application updates are separate. Enable automatic runtime updates to check once per launch. Runtime updates use stable upstream releases, not the moving `main` branch.

Downloads and package validation finish before a managed game is changed. The updater verifies the old original backup, restores the old installation, and backs up the restored original before installing the new version. If installation fails, recovery returns the game to the original state. If a backup is damaged or an installed DLL has been changed externally, the operation stops for manual recovery.

Old installations using `winhttp.dll` are restored and migrated to `version.dll`, since upstream 0.3.4 no longer supplies that alternative. Installs from the retired runtime source use their existing recovery records before switching to the supported runtime.

Restore deletes `dlssg_sm86.ini`, `dlssg_sm86/logs`, `dlssg_sm86.log`, and `dlssg_sm86_loader.log` for managed installations. An empty `dlssg_sm86` directory is removed. Unrelated game files, original DLL backups, and recovery records are retained. Custom log paths outside these known locations are not followed.

The runtime cache is `%LOCALAPPDATA%\MFG Enabler\payload\sdli1995`. Cache replacement is staged and rolled back if publication fails. Application update packaging is documented in [application updates](docs/APP-UPDATES.md).

## Build

Install Visual Studio 2026 with the .NET 10 SDK and WinUI development components. From the project root:

```powershell
.\build.ps1
```

This publishes the app and required runtimes to `dist`. For a versioned Beta ZIP:

```powershell
.\build-release.ps1 -Version 1.2b2
```

Output: `releases/MFG-Enabler-Package-1.2b2.zip`. Extract the entire ZIP before starting `MFG-Enabler.exe`. Published downloads are available from [GitHub Releases](https://github.com/wnduddld0513/MFG-Enabler/releases); automatic Source code archives of MFG-Enabler itself are not runnable app packages.

Run recovery and update tests:

```powershell
dotnet run --project tests/Payload/Payload.Tests.csproj -c Release
dotnet run --project tests/AppUpdates/AppUpdates.Tests.csproj -c Release
```

The payload test's optional `--live` argument also checks a real upstream release ZIP download, using an isolated temporary cache and game fixtures.

## Credits and license

[sdli1995/dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) supplies the runtime, downloaded separately.

MFG-Enabler is licensed under [GNU GPL version 3](LICENSE) (GPL-3.0-only). Copyright (c) 2026 MFG Enabler contributors. Third-party components retain their respective licenses; see [upstream notices](docs/UPSTREAM-NOTICES.txt).
