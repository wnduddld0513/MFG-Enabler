# Application updates

MFG-Enabler checks the selected channel once after startup. Settings also provides an **Update / Check for updates** button and a **Stable / Beta** selector. Changing channels checks the new channel immediately.

**Don't show again** dismisses that version in that channel. Newer versions can still notify; the settings button always allows a manual check or installation. **Later** postpones the notification until the next launch.

## Publishing a compatible release

Updates use published GitHub Releases from [wnduddld0513/MFG-Enabler](https://github.com/wnduddld0513/MFG-Enabler/releases), not branch commits or GitHub's automatic source ZIPs.

| Channel | Release target (`target_commitish`) | Tag | Asset example |
| --- | --- | --- | --- |
| Stable | `main` | `1.1` or `v1.1` | `MFG-Enabler-Package-1.1.zip` |
| Beta | `beta` | `1.1b1` or `v1.1b1` | `MFG-Enabler-Package-1.1b1.zip` |

Stable ignores prereleases and beta version names. Beta ignores stable version names. Mark Beta releases as prereleases in GitHub. Set the release target to the literal branch name shown above; releases whose target is a commit SHA or another branch are ignored. Release tag, ZIP filename, and the packaged DLL's product version must agree.

Versions are compared numerically: `1.0b2` comes before `1.0b10`. The selected channel never falls back to the other channel. Switching channels explicitly allows the other channel's build of the same base version (for example, `1.0` to `1.0b3`), but never an older base version. A fresh Beta build defaults to Beta; saved channel preferences take precedence.

Upload the complete contents of `dist` as the named ZIP, either directly at the archive root or inside one enclosing folder. Include the EXE, DLLs, runtime files, resources, and Assets directory. Do not upload source files as the update package. MSI assets are ignored by this ZIP updater.

The updater requires the SHA-256 `digest` supplied by GitHub's release asset API. Wait for the ZIP upload to finish before expecting it to appear in the app. Assets without a valid SHA-256 digest are ignored.

## Build versions

For a Stable build, set `<Version>` in `WinUI/MFG-Enabler.csproj` to the release number, such as `1.1`.

For a Beta build, keep the numeric .NET version and set the displayed/product release version separately:

```powershell
dotnet publish .\WinUI\MFG-Enabler.csproj -c Release -p:Platform=x64 -p:Version=1.1 -p:AppReleaseVersion=1.1b1 -o .\dist
```

The About screen and update comparison both read this product version.

## Applying an update

The ZIP is downloaded to `%LOCALAPPDATA%/MFG Enabler/app-updates`, checked for its expected size and SHA-256, and extracted to a unique staging folder. Paths, duplicate entries, required application files, and package version are validated before the app closes.

A separate PowerShell helper waits for the app to exit, takes the application's single-instance lock, backs up files that will be replaced, copies the new files to the running application's directory, verifies the results, and restarts the app. If replacement fails, it attempts to restore the backed-up files. Unrelated files and user settings are preserved. A rollback failure retains the backup and does not launch a partially restored application.

Portable installations update with the current user's permissions. When the application is installed in a protected directory such as `C:\Program Files\MFGEnabler`, the updater requests elevation through Windows UAC before replacing files. Cancelling the UAC prompt leaves the installed application unchanged.

Each update keeps `result.txt`, the staging directory, and backups in its job folder for recovery. Download/check errors leave the application running. Startup check failures appear in Settings without an error popup; a manual check shows details.

## Verification

```powershell
dotnet run --project .\tests\AppUpdates\AppUpdates.Tests.csproj -c Release
```

These isolated fixtures cover channel separation, version ordering, package validation, file replacement, and rollback without modifying the installed app or launching a game.
