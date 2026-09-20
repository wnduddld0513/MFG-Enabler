$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {

$projectRoot = Split-Path $PSScriptRoot -Parent
$archive = Join-Path $projectRoot 'src\native\tools\zig-0.15.2.zip'
$expectedHash = '3a0ed1e8799a2f8ce2a6e6290a9ff22e6906f8227865911fb7ddedc3cc14cb0c'
$cacheRoot = Join-Path $projectRoot 'src\native\.cache'
$zig = Join-Path $cacheRoot 'zig-x86_64-windows-0.15.2\zig.exe'

if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw 'Bundled Zig compiler archive is missing.'
}

$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw 'Bundled Zig SHA-256 mismatch.'
}

if (!(Test-Path -LiteralPath $zig -PathType Leaf)) {
    New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $cacheRoot)
}

$outputRoot = Join-Path $PSScriptRoot 'bin'
$source = Join-Path $PSScriptRoot 'tray.c'
$resource = Join-Path $PSScriptRoot 'tray.rc'
$resourceObject = Join-Path $outputRoot 'tray.res'
$output = Join-Path $outputRoot 'MFG-Enabler.Tray.exe'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

& $zig rc /nologo /fo $resourceObject $resource
if ($LASTEXITCODE -ne 0) {
    throw 'Tray icon resource build failed.'
}

& $zig cc -target x86_64-windows-gnu -std=c11 -O2 -s -Wall -Wextra -Werror $source $resourceObject -o $output "-Wl,--subsystem,windows" -lshell32 -luser32 -lkernel32
if ($LASTEXITCODE -ne 0) {
    throw 'Tray build failed.'
}

Write-Host "Built $output"
} finally { Pop-Location }
