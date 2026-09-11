# MFG-Enabler MSI installer

This folder contains the complete English-language MSI authoring for MFG-Enabler.

The installer:

- installs the complete `dist` payload to `C:\Program Files\MFGEnabler`;
- offers an **Add a desktop shortcut** checkbox, enabled by default;
- creates an all-users desktop shortcut when selected;
- registers MFG-Enabler in Windows Installed Apps;
- supports repair, removal, and major upgrades;
- embeds all application files in one MSI.

## Build

Install the .NET 10 SDK. The WiX Toolset SDK and UI extension are restored from NuGet on the first build.

Build the current contents of `dist`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\MFG-Enabler-Installer\build.ps1
```

Publish the application first and then build the MSI:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\MFG-Enabler-Installer\build.ps1 -BuildApplication
```

Specify another numeric MSI version when preparing a later release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\MFG-Enabler-Installer\build.ps1 -Version 1.1.0 -BuildApplication
```

Output: `MFG-Enabler-Installer/output/MFG-Enabler-Setup-<version>.msi`.

Keep the `UpgradeCode` in `Package.wxs` unchanged across releases. Increase the MSI version for every public installer release. The application can use a shorter display version, but MSI versions must remain numeric.

The installer source does not contain a second copy of the application. It packages the current project-level `dist` folder during each build, so the MSI and portable ZIP can be produced from the same published files.

Release publishing clears `dist` before writing new files and excludes PDB debug symbols. The MSI build also excludes any PDB files if a manually prepared payload contains them.
