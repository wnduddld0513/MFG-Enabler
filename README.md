# MFG-Enabler

<img width="1940" height="1354" alt="program" src="https://github.com/user-attachments/assets/fd5b9764-4b58-4d71-9d25-a379e89c1669" />
<img width="1940" height="1354" alt="mfg_all_game" src="https://github.com/user-attachments/assets/c6240b9c-ce34-4afb-a616-d4e7bce885a0" />
<img width="1940" height="1354" alt="inspector" src="https://github.com/user-attachments/assets/f91bce03-5b06-4599-895d-d6769d32153c" />


MFG-Enabler is a Windows tool for enabling Multi Frame Generation (MFG) on GeForce RTX 20, 30, and 40 series GPUs in supported games.

MFG-Enabler 1.4 manages the sdli1995/dlssg_for_sm86 runtime in supported Windows games, with separate NVIDIA driver profile controls.

## Changes in 1.4

- Global automatic MFG installation for detected frame-generation games, including newly discovered games. **DX12 only by default; Vulkan is unchecked.** Exclusions and future-game consent are configured before enabling.
- Native C tray companion monitors NVIDIA App library changes using Windows directory notifications. The WinUI interface exits when minimized/closed with tray mode enabled; a background worker runs only when needed.
- Automatic DLL selection finds the best-matching DLL for each game and uses it to apply MFG, checks existing-file conflicts, and refreshes the selection when the rendering executable changes.
- NVIDIA Profile Inspector-style FG settings, introduced in 1.3 and completed in 1.4: independent global/per-game presets, fixed/dynamic multipliers, and target FPS. Separate DLSS SR/RR presets are also available. Smooth Motion controls have been removed.
- Turning automatic MFG off stops startup/tray behavior and restores the games it manages. Interrupted or failed recovery retains the original backups for a retry.
- Correct preset enable flags, cleared stale dynamic settings, serialized UI/background writes, queued library changes, and fewer repeated payload downloads/scans.
- Compact controls, corrected dialog scrollbar spacing, and a concise English/Korean What's new dialog. The options dialog includes a small measured idle-tray resource note.

See [the 1.4 release notes](docs/RELEASE-NOTES-1.4.md) and [validation results](docs/VALIDATION-1.4.md).

## Runtime installation and recovery

- Uses sdli1995/dlssg_for_sm86 as runtime source. the editor now uses the supported runtime settings.
- Downloads the latest stable upstream release as a source ZIP pinned to its resolved commit. Only the required runtime files are extracted, verified against the commit's Git blob hashes, and checked for x64 compatibility.
- Supports `version.dll` and the alternatives `winmm.dll`, `dinput8.dll`, `dxgi.dll`, `d3d12.dll`, and `dbghelp.dll`. One selected DLL and `dlssg_sm86.ini` are installed beside the rendering executable.
- Updates restore the previous installation first, then install the verified new files. Original DLLs are backed up with the `.backup` extension (for example, `.mfg-enabler/version.backup`).
- Restore removes the runtime INI, including local edits, and the default runtime logs. It restores the original proxy DLL, or removes the proxy when no original existed.

Earlier changes are documented in [the 1.2 release notes](docs/RELEASE-NOTES-1.2.md).

## Requirements and usage

Windows 10 version 2004 or later, or Windows 11, x64, and an internet connection are required. Runtime compatibility depends on the game and GPU; consult the [upstream installation guide](https://github.com/sdli1995/dlssg_for_sm86/blob/0.3.5/README.en.md). NVIDIA App is used for automatic game discovery; games can also be added manually. Driver overrides require an installed NVIDIA driver and support for the selected feature.

1. Close the game, launch MFG-Enabler, and select the game's rendering executable.
2. Select a proxy DLL and turn on MFG. Any existing file at that selected DLL path is backed up before replacement.
3. Launch the game and configure its frame-generation options.
4. To remove the installation, close the game and click **Restore**.

Keep the game's `.mfg-enabler` folder while MFG is installed: it contains recovery records and original DLL backups. Restore verifies the original DLL before deleting those backups and the recovery folder. Do not copy a modded DLL over an original backup.

The upstream factory INI is used for installations and updates. Existing custom INI settings are removed during restore and reset during an upgrade.

## Global automatic MFG

Open **Graphics > Global settings > MFG override** and enable automatic application. Review the game exclusions first. Future detected games, Windows startup, and tray operation are initially selected when enabling; Vulkan inclusion is initially off. Turning the override off also turns off those three options and restores tracked runtime installations.

Only NVIDIA App entries with frame-generation evidence, an unambiguous rendering executable, and matching API evidence are eligible. The executable's PE imports (including delay imports and adjacent UnityPlayer.dll) identify DX12/Vulkan. Mixed DX12/Vulkan executables are excluded unless Vulkan is explicitly enabled; unidentified APIs and manual entries are never automatically installed. Engines that load graphics APIs only dynamically can remain unidentified. This conservative detection does not guarantee that a game uses a particular API at launch. Manual installation remains available after checking compatibility.

NVIDIA App must discover a new game and update its library before automatic installation can occur. The running interface watches that library; when the interface is closed, the optional native tray does so. Without either process running, games are checked on the next launch. Repeated change notifications are debounced and workers cannot overlap with the interface. Online/anti-cheat games should be excluded according to their rules and the upstream guidance.

Keep game processes closed during installation, updates, and recovery. Failed recovery is reported and retains its journal/backups. A game's **Restore** action excludes it from subsequent global automatic installation. Changing exclusions prevents future automatic application; use **Restore** to remove an installation already present.

## NVIDIA driver settings

Global and program panels expose DLSS frame-generation, super-resolution and ray-reconstruction presets, fixed/dynamic frame counts, dynamic target FPS, and Smooth Motion. Dynamic mode clears the fixed multiplier; fixed mode removes stale dynamic values. Applying a preset also writes its NVIDIA override-enable flag.

These controls update NVIDIA DRS profiles independently of runtime DLL installation and `dlssg_sm86.ini`. **Restore NVIDIA settings** removes only the override keys managed by these controls; it does not remove game DLLs, INI files, or unrelated driver settings. Program defaults inherit the driver's global settings. Available effects depend on the installed driver, GPU and game; saving a setting cannot add unsupported hardware capabilities.

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

Restore puts the original DLL back first (or removes the injected DLL if no original existed), then deletes `dlssg_sm86.ini`, `dlssg_sm86/logs`, `dlssg_sm86.log`, and `dlssg_sm86_loader.log`. After verifying restoration, it removes owned backups and recovery records, including older `.bak`/`.upg` records, and the empty `.mfg-enabler` folder. If restoration fails, backups and the journal remain available for a retry. Unknown files are preserved; custom log paths and unrelated capture data are not removed.

The runtime cache is `%LOCALAPPDATA%\MFG Enabler\payload\sdli1995`. Cache replacement is staged and rolled back if publication fails. Application update packaging is documented in [application updates](docs/APP-UPDATES.md).

## Build

Install Visual Studio 2026 with the .NET 10 SDK and WinUI development components. From the project root:

```powershell
.\build.ps1
```

This publishes the app and required runtimes to `dist`. For a versioned stable ZIP:

```powershell
.\build-release.ps1 -Version 1.4
```

Output: `releases/MFG-Enabler-Package-1.4.zip`. Extract the entire ZIP before starting `MFG-Enabler.exe`. Published downloads are available from [GitHub Releases](https://github.com/wnduddld0513/MFG-Enabler/releases); automatic Source code archives of MFG-Enabler itself are not runnable app packages.

Build the MSI after `build.ps1` with `MFG-Enabler-Installer/build.ps1 -Version 1.4.0`. The native tray build uses the SHA-256-verified bundled Zig 0.15.2 compiler; see [tray build and behavior](MFG-Enabler-Tray/README.md). Keep `MFG-Enabler.Tray.exe` and `NvAPIWrapper.dll` beside the application.

Run recovery and update tests:

```powershell
dotnet run --project tests/Payload/Payload.Tests.csproj -c Release
dotnet run --project tests/RuntimeNotes/RuntimeNotes.Tests.csproj -c Release
dotnet run --project tests/AppUpdates/AppUpdates.Tests.csproj -c Release
dotnet run --project tests/Global/Global.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Tray/run.ps1
```

The payload test's optional `--live` argument also checks a real upstream release ZIP download, using an isolated temporary cache and game fixtures.

The global test's optional `--live-driver` argument writes and reads a uniquely named temporary NVIDIA profile, then deletes it. It does not change the global profile or existing game profiles. Tests cover management behavior; they do not establish rendering compatibility or performance in every game.

## Credits and license

[sdli1995/dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) supplies the runtime, downloaded separately.

[NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) by Soroush Falahati supplies NVIDIA API bindings under LGPLv3; its [license](docs/NvAPIWrapper-LICENSE.txt) is included with the application.

MFG-Enabler is licensed under [GNU GPL version 3](LICENSE) (GPLv3). Third-party components retain their respective licenses; see [upstream notices](docs/UPSTREAM-NOTICES.txt).
