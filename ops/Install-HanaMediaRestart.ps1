$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run once as Administrator to register the restart task.'
}
$hostingTask = Get-ScheduledTask -TaskName 'HanaMedia-Web' -ErrorAction Stop
if (Get-ScheduledTask -TaskName 'HanaMedia-Web-Restart' -ErrorAction SilentlyContinue) {
    throw 'Restart task already exists; inspect before replacing.'
}
$repoPath = Split-Path $PSScriptRoot -Parent
$appPath = Join-Path $repoPath 'bin\Release\net8.0\HanaMedia.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Release executable missing.' }
# Embed a fixed action, not an elevated reference to an editable script.
# Same limited S4U identity as the web: no administrator privileges at runtime.
$command = @'
$ErrorActionPreference = 'Stop'
$appPath = '__APP_PATH__'
Stop-ScheduledTask -TaskName 'HanaMedia-Web'
try {
    Get-CimInstance Win32_Process -Filter "Name='HanaMedia.exe'" |
        Where-Object { $_.ExecutablePath -ieq $appPath } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop }
} finally {
    Start-ScheduledTask -TaskName 'HanaMedia-Web'
}
Start-Sleep -Seconds 5
$response = Invoke-WebRequest 'http://127.0.0.1:5028/Login' -UseBasicParsing -TimeoutSec 15
if ($response.StatusCode -ne 200) { throw 'Web health check failed after restart.' }
'@
$command = $command.Replace('__APP_PATH__', $appPath.Replace("'", "''"))
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
$shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $shell -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand $encoded" -WorkingDirectory $repoPath
$principal = New-ScheduledTaskPrincipal -UserId $hostingTask.Principal.UserId -LogonType S4U -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'HanaMedia-Web-Restart' -Action $action -Principal $principal -Settings $settings -Description 'On-demand restart of HanaMedia only, using the same limited identity as the web.' | Out-Null
