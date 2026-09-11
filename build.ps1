$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$distRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist'))
if ($distRoot -ne ($projectRoot + '\dist')) { throw 'Unexpected dist path.' }
if (Test-Path -LiteralPath $distRoot) {
    if (((Get-Item -LiteralPath $distRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to clean a dist reparse point.' }
    Remove-Item -LiteralPath $distRoot -Recurse -Force
}
dotnet publish .\WinUI\MFG-Enabler.csproj -c Release -p:Platform=x64 -o .\dist
if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 build failed' }
Write-Host 'Built dist\MFG-Enabler.exe (keep the runtime files beside the EXE).'
