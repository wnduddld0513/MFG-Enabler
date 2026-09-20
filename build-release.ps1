param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^\d+\.\d+(?:\.\d+)?(?:b[1-9]\d*)?$')]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'releases'
$stage = Join-Path $output ('build-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$zip = Join-Path $output "MFG-Enabler-Package-$Version.zip"
if (Test-Path -LiteralPath $zip) { throw "Package already exists: $zip. Move it aside before rebuilding." }
& (Join-Path $PSScriptRoot 'MFG-Enabler-Tray/build.ps1')
dotnet publish (Join-Path $PSScriptRoot 'WinUI/MFG-Enabler.csproj') -c Release -p:Platform=x64 "-p:AppReleaseVersion=$Version" -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
$tray = Join-Path $PSScriptRoot 'MFG-Enabler-Tray/bin/MFG-Enabler.Tray.exe'
if (!(Test-Path -LiteralPath $tray -PathType Leaf)) { throw 'Tray executable is missing.' }
Copy-Item -LiteralPath $tray -Destination (Join-Path $stage 'MFG-Enabler.Tray.exe')
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'MFG-Enabler.dll')).ProductVersion
if ($actual -ne $Version) { throw "Built version $actual does not match $Version." }
foreach ($file in @('MFG-Enabler.exe','MFG-Enabler.Tray.exe','MFG-Enabler.deps.json','MFG-Enabler.runtimeconfig.json','Microsoft.ui.xaml.dll','coreclr.dll','hostfxr.dll','Assets/MFG-Enabler.ico')) {
    if (!(Test-Path -LiteralPath (Join-Path $stage $file))) { throw "Missing runtime file: $file" }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/UPSTREAM-NOTICES.txt') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/NvAPIWrapper-LICENSE.txt') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CONTRIBUTORS.md') -Destination $stage
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
$channel = if ($Version -match 'b') { 'beta' } else { 'main' }
Write-Host "Package: $zip"
Write-Host "GitHub tag: v$Version; target_commitish: $channel; prerelease: $($channel -eq 'beta')"
Write-Host "SHA-256: $hash"
