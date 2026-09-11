$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path $PSScriptRoot 'tools'
$archive = Join-Path $toolRoot 'zig-0.15.2.zip'
$expectedHash = '3a0ed1e8799a2f8ce2a6e6290a9ff22e6906f8227865911fb7ddedc3cc14cb0c'
$cacheRoot = Join-Path $PSScriptRoot '.cache'
$zig = Join-Path $cacheRoot 'zig-x86_64-windows-0.15.2\zig.exe'
if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw 'Bundled compiler missing: src/native/tools/zig-0.15.2.zip. Restore this file from the project copy.'
}
$hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
$archiveStream = [System.IO.File]::OpenRead($archive)
try {
    $actualHash = [System.BitConverter]::ToString($hashAlgorithm.ComputeHash($archiveStream)).Replace('-', '').ToLowerInvariant()
} finally {
    $archiveStream.Dispose()
    $hashAlgorithm.Dispose()
}
if ($actualHash -ne $expectedHash) {
    throw 'Bundled Zig SHA-256 mismatch'
}
if (!(Test-Path -LiteralPath $zig -PathType Leaf)) {
    New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $cacheRoot)
}
$outputRoot = Join-Path $PSScriptRoot 'bin'
$source = Join-Path $PSScriptRoot 'nvapi_proxy.c'
$output = Join-Path $outputRoot 'nvapi64.dll'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
Push-Location $PSScriptRoot
try {
    & $zig cc -target x86_64-windows-gnu -shared -O2 -s -Wall -Wextra -Werror $source -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Native proxy build failed' }
} finally {
    Pop-Location
}
Write-Host "Built $output"
