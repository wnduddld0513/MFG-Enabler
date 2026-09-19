# MFG-Enabler 1.2b1

## Runtime source and package format

- Removed support for the retired runtime source, its channel selector, and its INI editor. sdli1995/dlssg_for_sm86 is now the only runtime source.
- Updated the baseline to upstream 0.3.4. The updater resolves a stable release tag to a commit, downloads that commit's source ZIP, and verifies selected runtime files against the Git tree before activating the cache.
- Supports the root 310.9 runtime: `version.dll` and `dlssg_sm86.ini`, with alternative DLLs under `alternatives/`. The older `310.1/` and archived builds are not installed.
- Added `d3d12.dll` and `dbghelp.dll` as proxy choices. Existing `winhttp.dll` installations are restored and moved to `version.dll` during an update.

## Backups, restore, and upgrade

- Original DLL backups remain inside the game's `.mfg-enabler` recovery folder.
- Upgrades download and validate first, restore the previous installation, and then install the new DLL and factory INI. The new backup therefore contains the original game DLL rather than a previous mod version.
- Restore returns the original DLL byte-for-byte, or removes the installed proxy if no original existed.
- Restore removes the INI even if it was edited, plus default runtime logs under `dlssg_sm86/logs` and the known flat log files. Backup records and unrelated game files are preserved.
- Damaged original backups and unexpected DLL modifications block replacement. New-installation failures restore the original game state, and interrupted restores can be retried.
- Retired runtime settings and cache metadata no longer select the old source. Existing game recovery records remain readable.

## Validation

Automated tests cover original backup preservation, old-runtime and proxy migration, edited INI removal, log cleanup, interrupted restore, installation failure, cache rollback, source ZIP integrity, and rejection of linked log directories. Real upstream 0.3.4 source ZIP extraction and the live GitHub download path were verified in isolated temporary folders.

Game-specific rendering and performance still require testing in the actual game.
