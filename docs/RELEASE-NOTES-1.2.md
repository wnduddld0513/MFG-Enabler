# MFG-Enabler 1.2

## INI editor for upstream 0.3.4

Added a game-specific INI editor below the MFG toggle, replacing the retired runtime's settings with controls supported by sdli1995/dlssg_for_sm86:

- **Optimization tier:** `Optimized=0-3`, default `1`. Tiers 2 and 3 trade image quality for speed.
- **Frame-generation ceiling:** `MaxGeneratedFrames=0-5`, default `3` (4X). Zero keeps the runtime limit; values 1-5 represent ceilings of 2X-6X. The game chooses the actual multiplier, and older game plugins may still limit it to 4X.
- **Render preset:** Auto, A, or B. Preset B depends on the game providing the required HUD/UI data.
- **Log level:** off, errors, configuration, or detailed diagnostics.

Install MFG with runtime 0.3.4 or newer first, close the game, then edit and save. The defaults button selects factory values for the displayed controls; Save applies them. Settings take effect on the next game launch.

## Safe saving and recovery

Saving edits the selected game's INI, preserves unrelated keys and comments, and updates the recorded INI hash. It does not modify the cached upstream package, installed DLL, or original DLL backup. If saving fails before the recovery record is updated, the INI is rolled back.

The 1.2 source ZIP downloader and restore-first upgrade flow are retained. Original DLLs use the `.backup` extension inside `.mfg-enabler`. Restore puts the original DLL back first, then removes the edited INI and runtime logs. Once restoration is verified, owned backups and recovery records are deleted and the empty `.mfg-enabler` folder is removed. Failed restoration keeps the backup for a retry. Runtime upgrades reset the INI to the upstream factory configuration.

## Release details and navigation

Update details loads the latest upstream release, displays its English notes and source link, and includes the last local update result. About displays the built application version. The green navigation indicator stretches to the full height of its button.

## Download

Download `MFG-Enabler-Package-1.2.zip` and extract all files, or select **Stable** in the application settings and check for updates. The ZIP includes the .NET and Windows App SDK runtimes.
