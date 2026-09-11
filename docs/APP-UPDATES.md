# Application releases

Build an update ZIP from the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-release.ps1 -Version 1.1
```

Upload `releases/MFG-Enabler-Package-1.1.zip` as a GitHub release asset.
Do not rename an older ZIP: the embedded DLL ProductVersion must match the tag.
The script publishes all .NET and Windows App SDK runtime files and checks the embedded version.

| Setting | Stable example | Beta example |
| --- | --- | --- |
| Tag | `v1.1` | `v1.0b1` |
| Target branch / API target_commitish | `main` | `beta` |
| Pre-release | Off | On |
| ZIP asset name | `MFG-Enabler-Package-1.1.zip` | `MFG-Enabler-Package-1.0b1.zip` |
| Embedded AppReleaseVersion | `1.1` | `1.0b1` |

Create the `beta` branch before publishing a Beta release. Verify `target_commitish`
with the GitHub releases API; selecting an existing tag may retain an earlier target.
GitHub must finish uploading and report a `sha256:` digest for the ZIP.
MSI and automatic Source code archives are ignored by the in-app updater.

In the application, select **Beta** and click **Check for updates** to receive Beta
releases. Stable only offers Stable releases. Switching from 1.0 Stable to 1.0b1
Beta is supported. An already installed 1.0b1 does not update to another 1.0b1;
publish 1.0b2 for the next Beta. Manual checks also show a previously dismissed version.

The updater verifies the downloaded SHA-256, runtime files, and embedded version
before applying changes. It backs up replaced files and rolls back on failure.
Diagnostics and backups are under `%LOCALAPPDATA%\MFG Enabler\app-updates\<job>`.
