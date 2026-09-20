param([Parameter(Mandatory=$true)][string]$JobDirectory, [switch]$NoRestart)
$ErrorActionPreference = 'Stop'
$jobRoot = [IO.Path]::GetFullPath($JobDirectory)
$changed = [Collections.Generic.List[object]]::new()
$updateLock = $null
$lockHeld = $false
$installStarted = $false
$canRestart = $false
$rollbackOk = $true
function SafePath([string]$root, [string]$relative) {
    $root = [IO.Path]::GetFullPath($root).TrimEnd('\')
    $path = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $relative))
    if (!$path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escapes its folder.' }
    $part = $path
    while ($part) {
        if (([IO.File]::Exists($part) -or [IO.Directory]::Exists($part)) -and (([IO.File]::GetAttributes($part) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Update path contains a reparse point.' }
        $part = [IO.Path]::GetDirectoryName($part)
    }
    return $path
}
function Hash([string]$path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
try {
    $job = [IO.File]::ReadAllText((Join-Path $jobRoot 'job.json')) | ConvertFrom-Json
    $targetRoot = [IO.Path]::GetFullPath($job.target).TrimEnd('\')
    $stageRoot = [IO.Path]::GetFullPath($job.stage)
    if ($stageRoot -ne (Join-Path $jobRoot 'stage')) { throw 'Invalid staging folder.' }
    if ($targetRoot.Length -le 3 -or !$job.files.Count) { throw 'Invalid update target.' }
    $exe = SafePath $targetRoot 'MFG-Enabler.exe'
    $parent = [Diagnostics.Process]::GetProcessById([int]$job.processId)
    if ($parent.StartTime.ToUniversalTime().Ticks.ToString() -ne $job.processStart) { throw 'Application process changed.' }
    [IO.File]::WriteAllText((Join-Path $jobRoot 'ready'), 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (![IO.File]::Exists((Join-Path $jobRoot 'commit'))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Update was not confirmed.' }
        Start-Sleep -Milliseconds 100
    }
    if (!$parent.WaitForExit(60000)) { throw 'Application did not close. No files were replaced.' }
    $parent.Dispose()
    $updateLock = [Threading.Mutex]::new($false, 'Local\MFG-Enabler-1')
    try { $lockHeld = $updateLock.WaitOne(10000) } catch [Threading.AbandonedMutexException] { $lockHeld = $true }
    if (!$lockHeld) { throw 'Another MFG-Enabler instance is running.' }
    # Stop only the tray executable belonging to this installation before replacing it.
    $trayExe = SafePath $targetRoot 'MFG-Enabler.Tray.exe'
    $trays = @(Get-Process -Name 'MFG-Enabler.Tray' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $trayExe })
    if ($trays.Count -gt 0) {
        $stopper = Start-Process -FilePath $trayExe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
        if (!$stopper.WaitForExit(10000)) { throw 'Tray shutdown command timed out.' }
        foreach ($trayProcess in $trays) { if (!$trayProcess.WaitForExit(10000)) { throw 'Tray did not close. No files were replaced.' } }
    }
    $canRestart = $true
    $backupRoot = Join-Path $jobRoot 'backup'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $plan = @()
    foreach ($file in $job.files) {
        $source = SafePath $stageRoot $file.path
        $destination = SafePath $targetRoot $file.path
        $backup = SafePath $backupRoot $file.path
        if (!$seen.Add($destination) -or (Hash $source) -ne $file.hash) { throw 'Staged file validation failed.' }
        $exists = [IO.File]::Exists($destination)
        if ($exists) {
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
            [IO.File]::Copy($destination, $backup, $false)
        }
        $plan += [pscustomobject]@{ source=$source; destination=$destination; backup=$backup; existed=$exists; hash=$file.hash }
    }
    $installStarted = $true
    foreach ($file in $plan) {
        # Recheck immediately before mutation and record before copying (copy may fail partway).
        [void](SafePath $targetRoot ([IO.Path]::GetFullPath($file.destination).Substring($targetRoot.Length + 1)))
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.destination))
        $changed.Add($file)
        [IO.File]::Copy($file.source, $file.destination, $true)
        if ((Hash $file.destination) -ne $file.hash) { throw 'Installed file validation failed.' }
    }
    [IO.File]::WriteAllText((Join-Path $jobRoot 'result.txt'), 'Update completed.')
} catch {
    $failure = $_.Exception.Message
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $file = $changed[$i]
        try {
            [void](SafePath $targetRoot ($file.destination.Substring($targetRoot.Length + 1)))
            if ($file.existed) { [IO.File]::Copy($file.backup, $file.destination, $true) }
            elseif ([IO.File]::Exists($file.destination)) { [IO.File]::Delete($file.destination) }
        } catch { $rollbackOk = $false; $failure += "`r`nRollback: " + $_.Exception.Message }
    }
    [IO.File]::WriteAllText((Join-Path $jobRoot 'result.txt'), "Update failed: $failure`r`nBackup: " + (Join-Path $jobRoot 'backup'))
    if (!$NoRestart) {
        Add-Type -AssemblyName System.Windows.Forms
        [void][Windows.Forms.MessageBox]::Show("Update failed: $failure`r`nDetails: $jobRoot", 'MFG-Enabler')
    }
} finally {
    if ($lockHeld) { $updateLock.ReleaseMutex() }
    if ($updateLock) { $updateLock.Dispose() }
}
if (!$NoRestart -and $canRestart -and $rollbackOk) {
    Start-Process -FilePath $exe -WorkingDirectory $targetRoot -WindowStyle Hidden
}
