# Version 1.3 validation

Validated on Windows x64 on 2026-09-20.

| Suite | Passed | Coverage |
| --- | ---: | --- |
| Payload (`--live`) | 67 | Download/hash verification of upstream 0.3.5; INI editing; original DLL backup; rollback; interrupted recovery; global OFF with a damaged backup and retry |
| RuntimeNotes | 6 | English extraction, markdown cleanup, paragraphs, empty/non-English fallback |
| AppUpdates | 22 | Stable/beta selection, archive checks, version verification, replacement rollback and helper restart |
| Global (`--live-driver`) | 26 | DX12/Vulkan/mixed/unknown classification, legacy defaults, exclusions, ownership persistence, batch behavior, driver value validation and temporary NVIDIA profile readback/restore |
| Native tray | 5 | Startup scan, burst debounce, queued worker without overlap, unrelated event filtering, atomic library replacement and clean shutdown |

Total: **126 checks passed**. Release WinUI build: zero warnings and zero errors. Native tray builds with warnings as errors. Native tray test working set was 9.8 MiB on this machine; this is an observation, not a resource guarantee.

Live payload tests used temporary game/cache fixtures. Live driver tests created and removed a uniquely named temporary profile, without changing existing game/global profiles. Directory-watcher tests used an isolated tray class/mutex and temporary storage.

Not verified: actual rendering, image quality, performance, anti-cheat compatibility in individual games; full interactive UI walkthrough; installed-MSI upgrade/uninstall behavior. An additional built-executable smoke command was blocked by automatic tool approval and was not counted as passed. API detection is conservative static evidence, not a guarantee of the renderer selected at runtime.

Developer isolation: `MFG_ENABLER_DATA_DIR` redirects application settings/library/runtime cache and gives the process its own instance gate; it disables Windows startup/native tray integration. `MFG_ENABLER_STORAGE_FILE` can point discovery at a fixture. Unset both for normal use.
