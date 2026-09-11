param([string]$Version = '1.0.0', [switch]$BuildApplication)
$ErrorActionPreference = 'Stop'
$installerRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $installerRoot
$payload = Join-Path $projectRoot 'dist'
$mainExe = Join-Path $payload 'MFG-Enabler.exe'

if ($BuildApplication) {
    & (Join-Path $projectRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
}
if (!(Test-Path -LiteralPath $mainExe -PathType Leaf)) {
    throw 'dist/MFG-Enabler.exe is missing. Build the application first or use -BuildApplication.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'MSI version must use three numeric fields, for example 1.0.0.'
}
$output = [IO.Path]::GetFullPath((Join-Path $installerRoot 'output'))
if ($output -ne ([IO.Path]::GetFullPath($installerRoot).TrimEnd('\') + '\output')) { throw 'Unexpected installer output path.' }
if (Test-Path -LiteralPath $output) {
    if (((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to clean an output reparse point.' }
    Remove-Item -LiteralPath $output -Recurse -Force
}

& dotnet build (Join-Path $installerRoot 'MFG-Enabler-Installer.wixproj') -c Release -p:ProductVersion=$Version
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
Write-Host "Built MFG-Enabler-Installer\output\MFG-Enabler-Setup-$Version.msi"
