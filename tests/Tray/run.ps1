$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$zig = Join-Path $root 'src/native/.cache/zig-x86_64-windows-0.15.2/zig.exe'
if (!(Test-Path -LiteralPath $zig)) { & (Join-Path $root 'MFG-Enabler-Tray/build.ps1') }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MFG-tray-tests-' + [Guid]::NewGuid().ToString('N'))
$watch = Join-Path $fixture 'NVIDIA Corporation/NVIDIA App/NvBackend'
New-Item -ItemType Directory -Path $watch -Force | Out-Null
$exe = Join-Path $fixture 'MFG-Enabler.Tray.exe'
& $zig cc -target x86_64-windows-gnu -std=c11 -O2 -Wall -Wextra -Werror -DMFG_TRAY_TEST (Join-Path $root 'MFG-Enabler-Tray/tray.c') -o $exe '-Wl,--subsystem,windows' -lshell32 -luser32 -lkernel32
if ($LASTEXITCODE) { throw 'Test tray build failed.' }
& $zig cc -target x86_64-windows-gnu -O2 (Join-Path $PSScriptRoot 'worker.c') -o (Join-Path $fixture 'MFG-Enabler.exe') '-Wl,--subsystem,windows' -lkernel32
if ($LASTEXITCODE) { throw 'Test worker build failed.' }
$calls = Join-Path $fixture 'calls.txt'
function Read-Calls { if (Test-Path -LiteralPath $calls) { return [IO.File]::ReadAllText($calls) }; return '' }
function Wait-Calls([string]$expected) {
    $until = [DateTime]::UtcNow.AddSeconds(15)
    while ((Read-Calls) -ne $expected) {
        if ([DateTime]::UtcNow -gt $until) { throw "Expected $expected; got $(Read-Calls)" }
        Start-Sleep -Milliseconds 100
    }
}
$info = New-Object Diagnostics.ProcessStartInfo $exe
$info.UseShellExecute = $false
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.EnvironmentVariables['LOCALAPPDATA'] = $fixture
$process = [Diagnostics.Process]::Start($info)
try {
    Wait-Calls 'S'
    Write-Host 'PASS Initial scan on tray startup'
    1..10 | ForEach-Object { [IO.File]::WriteAllText((Join-Path $watch 'ApplicationStorage.json'), "$_"); Start-Sleep -Milliseconds 30 }
    Wait-Calls 'SESE'
    Write-Host 'PASS Burst coalesces; changes during worker run are queued without overlap'
    [IO.File]::WriteAllText((Join-Path $watch 'unrelated.txt'), 'ignore')
    Start-Sleep -Milliseconds 1700
    if ((Read-Calls) -ne 'SESE') { throw 'Unrelated file triggered a scan.' }
    Write-Host 'PASS Unrelated notifications ignored'
    [IO.File]::WriteAllText((Join-Path $watch 'replacement.json'), '{}')
    [IO.File]::Delete((Join-Path $watch 'ApplicationStorage.json'))
    [IO.File]::Move((Join-Path $watch 'replacement.json'), (Join-Path $watch 'ApplicationStorage.json'))
    Wait-Calls 'SESESE'
    Write-Host 'PASS Atomic storage replacement detected'
    $process.Refresh()
    Write-Host ('Idle working set: {0:N1} MiB' -f ($process.WorkingSet64 / 1MB))
} finally {
    Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -Wait
    if (!$process.WaitForExit(5000)) { $process.Kill(); throw 'Tray did not cancel its pending directory read.' }
}
Write-Host 'PASS Clean shutdown while directory read is pending'
Write-Host "5 checks passed. Fixtures: $fixture"
