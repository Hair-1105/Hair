$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'bin\Release\payout-stage'
$target = Join-Path $repo 'bin\Release\net8.0'
$files = @('HanaMedia.exe', 'HanaMedia.dll', 'HanaMedia.pdb', 'HanaMedia.deps.json', 'HanaMedia.runtimeconfig.json')
foreach ($name in $files) { if (-not (Test-Path -LiteralPath (Join-Path $source $name))) { throw "Missing prepared file: $name" } }
$backup = Join-Path $repo ('App_Data\hosting\release-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Start-Transcript -Path (Join-Path $backup 'deploy.log')
try {
    foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $target $name) -Destination $backup }
    Disable-ScheduledTask -TaskName 'HanaMedia-Web' | Out-Null
    Stop-ScheduledTask -TaskName 'HanaMedia-Web'
    Start-Sleep -Seconds 2
    try {
        Get-CimInstance Win32_Process -Filter "Name='HanaMedia.exe'" |
            Where-Object { $_.ExecutablePath -ieq (Join-Path $target 'HanaMedia.exe') } |
            ForEach-Object {
                $running = Get-Process -Id $_.ProcessId -ErrorAction Stop
                $running.Kill()
                if (-not $running.WaitForExit(15000)) { throw 'Web process did not exit.' }
            }
        Start-Sleep -Seconds 1
        foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name) -Force }
    } catch {
        foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $target $name) -Force }
        throw
    } finally {
        Enable-ScheduledTask -TaskName 'HanaMedia-Web' | Out-Null
        Start-ScheduledTask -TaskName 'HanaMedia-Web'
    }
    Write-Output 'Prepared release installed. Database and configuration unchanged. Verify HTTP health separately.'
} finally { Stop-Transcript }
