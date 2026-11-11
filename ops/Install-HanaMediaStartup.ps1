param([Parameter(Mandatory=$true)][string]$RunAsUser)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$logPath = Join-Path $repoPath 'App_Data\hosting'
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
Start-Transcript -Path (Join-Path $logPath 'install.log') -Append
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this installer as Administrator.' }
    $appPath = Join-Path $repoPath 'bin\Release\net8.0\HanaMedia.exe'
    if (-not (Test-Path -LiteralPath $appPath)) { throw 'Build Release first.' }
    $taskName = 'HanaMedia-Web'
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'Task already exists; inspect it before updating.' }
    $shellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $scriptPath = Join-Path $PSScriptRoot 'Run-HanaMedia.ps1'
    $action = New-ScheduledTaskAction -Execute $shellPath -Argument "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File `"$scriptPath`"" -WorkingDirectory $repoPath
    $boot = New-ScheduledTaskTrigger -AtStartup
    $boot.Delay = 'PT30S'
    $logon = New-ScheduledTaskTrigger -AtLogOn -User $RunAsUser
    # S4U runs without storing a password and does not grant network credentials.
    # This deployment uses local SQL Server and the existing user's local configuration.
    $principal = New-ScheduledTaskPrincipal -UserId $RunAsUser -LogonType S4U -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($boot,$logon) -Principal $principal -Settings $settings -Description 'HanaMedia local web with automatic restart and local health checks.' | Out-Null
    powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure lid action.' }
    powercfg /change standby-timeout-ac 0
    powercfg /change hibernate-timeout-ac 0
    powercfg /setactive SCHEME_CURRENT
    foreach ($listener in (Get-NetTCPConnection -LocalPort 5028 -State Listen -ErrorAction SilentlyContinue)) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        if ($process.ExecutablePath -ieq $appPath) { Stop-Process -Id $process.ProcessId -Force }
        else { throw 'Port 5028 belongs to another executable. It has not been stopped.' }
    }
    Start-ScheduledTask -TaskName $taskName
    Write-Output 'Installed and started HanaMedia-Web. Battery power settings unchanged.'
} catch { Write-Error $_; exit 1 }
finally { Stop-Transcript }
