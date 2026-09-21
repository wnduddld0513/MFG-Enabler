# Version 1.4 validation

Validated on Windows x64 on 2026-09-21.

| Suite | Passed | Coverage |
| --- | ---: | --- |
| Payload | 64 | DLL backup/recovery, interrupted installation, INI handling, verified archive fixtures, global OFF recovery |
| RuntimeNotes | 6 | English extraction and release-note formatting |
| AppUpdates | 26 | Shared stable releases on Beta, no repeated update/downgrade, next beta series, package validation, update rollback and restart |
| Global | 42 | DX12/Vulkan/unknown API classification, DLL selection, collisions, cached recommendations, changed renderer path, exclusions and automatic installation policy, NVIDIA profile setting values |
| Native tray | 5 | Initial scan, notification debounce, queued workers, unrelated-file filtering, atomic replacement, clean shutdown |

Total: **143 checks passed**. WinUI Release build completed with zero warnings and errors. Native tray tests compile with warnings as errors.

The Korean MFG options dialog was inspected in an isolated application instance: text wraps before the reserved scrollbar area, and the small resource note is visible below the DX12 explanation. The user also checked the tray UI. UI automation ended when the user pressed Escape.

The existing native tray was sampled once per second for 30 seconds: normalized CPU usage was 0.00%; working set was 9.96–15.11 MiB (mean 10.20 MiB). The isolated native tray test used 9.8 MiB. These are local idle measurements, not a general average or a limit; the WinUI and scan/install worker have separate costs.

This run did not test actual frame generation in games, live driver profile writes, installed MSI upgrades, or a fresh upstream runtime download. Automatic installation and recovery use temporary fixtures. API/DLL detection is static evidence; actual runtime loading and rendering compatibility remain game-dependent.
