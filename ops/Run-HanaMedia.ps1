$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$appPath = Join-Path $repoPath 'bin\Release\net8.0\HanaMedia.exe'
$logPath = Join-Path $repoPath 'App_Data\hosting'
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Logging__LogLevel__Microsoft = 'Warning'
Set-Location -LiteralPath $repoPath
function Write-HostLog([string]$message) {
    Add-Content -LiteralPath (Join-Path $logPath ('watchdog-' + (Get-Date -Format 'yyyyMMdd') + '.log')) -Value ('{0:o} {1}' -f (Get-Date), $message)
}
$child = $null
try {
    while ($true) {
        try {
            if (-not (Test-Path -LiteralPath $appPath)) { throw 'Release executable missing. Build Release before starting.' }
            $listener = Get-NetTCPConnection -LocalPort 5028 -State Listen -ErrorAction SilentlyContinue
            if ($listener) { Write-HostLog 'Port 5028 already occupied; retry in 30 seconds without stopping another process.'; Start-Sleep 30; continue }
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
            $child = Start-Process -FilePath $appPath -ArgumentList '--urls http://*:5028' -WorkingDirectory $repoPath -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logPath "$stamp-out.log") -RedirectStandardError (Join-Path $logPath "$stamp-error.log")
            Write-HostLog "Started web PID $($child.Id)"
            Start-Sleep 30
            $failures = 0
            while (-not $child.HasExited) {
                try {
                    $response = Invoke-WebRequest 'http://127.0.0.1:5028/Login' -UseBasicParsing -TimeoutSec 10
                    if ($response.StatusCode -ne 200) { throw 'Health response not 200' }
                    $failures = 0
                } catch {
                    $failures++
                    Write-HostLog "Health check failed ($failures/3): $($_.Exception.Message)"
                }
                if ($failures -ge 3) {
                    Write-HostLog "Restarting unresponsive web PID $($child.Id)"
                    $child.Kill(); $child.WaitForExit(); break
                }
                Start-Sleep 30
                $child.Refresh()
            }
            Write-HostLog "Web exited; retry in 10 seconds."
        } catch { Write-HostLog "Start/run error: $($_.Exception.Message)" }
        finally { if ($child -and -not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child = $null }
        Start-Sleep 10
    }
} finally { if ($child -and -not $child.HasExited) { $child.Kill() } }
