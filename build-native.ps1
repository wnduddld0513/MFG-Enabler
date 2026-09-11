$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'src\native\build.ps1')
