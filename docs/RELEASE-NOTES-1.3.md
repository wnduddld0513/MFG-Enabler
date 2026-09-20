# MFG-Enabler 1.3

## New features

- Automatic MFG installation for existing and newly detected frame-generation games. **DX12 only by default; Vulkan is disabled unless explicitly selected.** Mixed-API games follow the Vulkan option; unidentified APIs are excluded from automatic installation.
- Game exclusions, future-game consent, optional Windows startup, and a lightweight native tray companion that monitors NVIDIA App library changes.
- Separate global and per-program NVIDIA DLSS FG/SR/RR presets, fixed/dynamic MFG, dynamic target FPS and Smooth Motion. Program defaults inherit global driver settings.
- Updated 1.3 information/version display, English runtime release notes, collapsible settings and matched navigation indicators.

## Fixes and recovery

- Turning automatic MFG off stops automatic application/startup/tray behavior and restores tracked games. Original DLLs are restored first; successful recovery removes runtime INI files and owned recovery data. Failed recovery retains backups for a retry.
- Preset enable flags are now written with their values. Switching modes clears stale dynamic values; unsuccessful driver writes do not save misleading application settings.
- UI and background workers share an instance gate; queued library changes are preserved. Runtime preparation happens once per batch, including download failures.
- Tray shutdown correctly cancels pending directory I/O. Application updates stop the matching tray before replacing its executable.
- Retains the single sdli1995 runtime, supported INI editor, original `.backup` files, and safe upgrade/rollback behavior from 1.2.

## Usage

Close the game before installation or recovery. Enable **Graphics > Global settings > MFG override**, review exclusions, and leave Vulkan unchecked for the default DX12 policy. NVIDIA App must discover new games before they can be automatically applied. Use the separate NVIDIA restore controls for driver settings and the game's Restore button for runtime removal.

## Downloads

- `MFG-Enabler-Package-1.3.zip`: portable Windows x64 package; extract the entire archive.
- `MFG-Enabler-Setup-1.3.0.msi`: Windows x64 installer.

## Validation

126 automated checks passed, including actual upstream 0.3.5 download/install/restore in temporary fixtures, an isolated NVIDIA profile write/readback/restore, and native tray event tests. WinUI Release build completed with zero warnings/errors. Individual game rendering and full MSI installation/upgrade were not tested; see [validation details](VALIDATION-1.3.md).

## Contributors

Maintained by wnduddld0513, with implementation, debugging, regression tests, documentation and release preparation assisted by **OpenAI Codex**. Runtime by sdli1995/dlssg_for_sm86; NVIDIA bindings by NvAPIWrapper.
