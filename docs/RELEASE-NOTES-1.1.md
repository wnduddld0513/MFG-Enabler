# MFG-Enabler 1.1

This stable release adds runtime selection, an INI editor for the DLSSG 310.9.1 channel, and interface improvements introduced during the 1.0 Beta cycle.

## New features

### Runtime selection

Choose a runtime in **Settings > Runtime channel**:

- **DLSSG 310.9.1 - SilyNoMeta** (default).
- **dlssg_for_sm86 - sdli1995**.

The SilyNoMeta channel downloads `version.dll` and `dlssg_sm86.ini` from its GitHub releases and verifies their hashes. Switching channels updates eligible managed games to the selected runtime.

### INI editor

An **INI settings** section below the MFG toggle is available for the DLSSG 310.9.1 channel. It provides:

- Maximum multiplier (2-6) and fixed multiplier (0 uses the game's selection).
- Dynamic MFG and target FPS (0 uses the display).
- An expandable **Additional settings** section for HardwareBilinear, Conv13SharedInput, Conv0SharedInput, and ResidualVectorLoads, each with an explanatory tooltip.
- A **Restore defaults** button.

Custom values apply to new installations. Runtime updates reset the INI to the upstream defaults.

## Interface and discovery

- Reduced flicker in the MFG toggle and proxy selector during operations. Busy-state input is ignored and controls return to the actual state.
- Refined update-detail button text and added a loading overlay during startup scanning.
- Improved game detection when NVIDIA fingerprint or profile metadata is incomplete, while retaining frame-generation DLL checks and explicit profile exclusions.

## Upgrade notes

On the first migration to the new settings, the application selects the SilyNoMeta runtime and enables automatic runtime updates. Both preferences can be changed in Settings.

For application updates, select **Stable** and click **Check for updates**. This also applies to users upgrading from 1.0b1 or 1.0b2: the Beta channel does not offer Stable releases.

## Downloads

- `MFG-Enabler-Setup-1.1.0.msi`: Windows x64 installer with an optional desktop shortcut.
- `MFG-Enabler-Package-1.1.zip`: portable Windows x64 package, also used by the application updater.

Both include the .NET and Windows App SDK runtimes. Extract the entire ZIP before launching `MFG-Enabler.exe`. Automatic Source code archives are for developers.

Requires Windows 10 version 2004 or later, or Windows 11, x64. Runtime and game compatibility varies.

## Validation

The application update test suite passed 22 checks, including channel selection, package rejection, rollback, and restart behavior. This does not represent an in-game compatibility test for every runtime or game.

## SHA-256

```text
a9de057c19f169a584e502254d5896e58447f262483da9bd96f8ac77220059fa  MFG-Enabler-Package-1.1.zip
3335eeca81099176f14a2c6c2610f891d62aead31a1e699b1368e04be22bfa6b  MFG-Enabler-Setup-1.1.0.msi
```
