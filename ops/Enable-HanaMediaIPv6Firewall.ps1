param(
    [string]$LanPrefix = '2402:800:62d2:28ef::/64',
    [string]$WebProgram = 'C:\HanaMedia\bin\Release\net8.0\HanaMedia.exe'
)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator to add the IPv6 LAN firewall rules.'
}
if (-not (Test-Path -LiteralPath $WebProgram)) { throw 'HanaMedia executable not found.' }
$rules = @(
    @{ Name = 'HanaMedia-Web-IPv6-LAN'; DisplayName = 'HanaMedia web - IPv6 company LAN'; Protocol = 'TCP'; LocalPort = 5028; Profile = @('Domain', 'Private'); Program = $WebProgram },
    @{ Name = 'HanaMedia-DNS-TCP-IPv6-LAN'; DisplayName = 'Technitium DNS TCP - IPv6 LAN'; Protocol = 'TCP'; LocalPort = 53; Profile = 'Any' },
    @{ Name = 'HanaMedia-DNS-UDP-IPv6-LAN'; DisplayName = 'Technitium DNS UDP - IPv6 LAN'; Protocol = 'UDP'; LocalPort = 53; Profile = 'Any' }
)
foreach ($rule in $rules) {
    if (Get-NetFirewallRule -Name $rule.Name -ErrorAction SilentlyContinue) {
        throw "Rule $($rule.Name) already exists; inspect it before changing it."
    }
}
$created = @()
try {
    foreach ($rule in $rules) {
        New-NetFirewallRule @rule -Direction Inbound -Action Allow -Enabled True -RemoteAddress $LanPrefix | Out-Null
        $created += $rule.Name
    }
} catch {
    foreach ($name in $created) { Remove-NetFirewallRule -Name $name -ErrorAction SilentlyContinue }
    throw
}
Write-Output "Enabled HanaMedia TCP 5028 and DNS TCP/UDP 53 for IPv6 LAN $LanPrefix."
