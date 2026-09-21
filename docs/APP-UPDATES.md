# Application releases

Build an update ZIP from the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-release.ps1 -Version 1.3
```

Upload `releases/MFG-Enabler-Package-1.3.zip` as a GitHub release asset.
Do not rename an older ZIP: the embedded DLL ProductVersion must match the tag.
The script publishes all .NET and Windows App SDK runtime files and checks the embedded version.

| Setting | Stable example | Beta example |
| --- | --- | --- |
| Tag | `v1.3` | `v1.3b1` |
| Target branch / API target_commitish | `main` | `beta` |
| Pre-release | Off | On |
| ZIP asset name | `MFG-Enabler-Package-1.3.zip` | `MFG-Enabler-Package-1.3b1.zip` |
| Embedded AppReleaseVersion | `1.3` | `1.3b1` |

Create the `beta` branch before publishing a Beta release. Verify `target_commitish`
with the GitHub releases API; selecting an existing tag may retain an earlier target.
GitHub must finish uploading and report a `sha256:` digest for the ZIP.
MSI and automatic Source code archives are ignored by the in-app updater.

From 1.4 onward, **Beta** receives both beta and stable releases, choosing the newest
version; **Stable** only receives stable releases. A final 1.4 replaces 1.4b2 in both
channels, and 1.4 on Beta can later receive 1.5b1 without reverting to 1.4b2.
Versions through 1.4b2 only check beta releases while Beta is selected: switch once
to Stable or install the shared 1.4 ZIP manually to adopt the new behavior.
Manual checks also show a previously dismissed version.

The updater verifies the downloaded SHA-256, runtime files, and embedded version
before applying changes. It backs up replaced files and rolls back on failure.
Diagnostics and backups are under `%LOCALAPPDATA%\MFG Enabler\app-updates\<job>`.

Version 1.3 stops the matching native tray before replacing its executable. The helper holds the application mutex during replacement; the restarted app restores the configured tray behavior.
