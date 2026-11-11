$ErrorActionPreference = 'Stop'
$root = 'C:\HanaMediaBackups'
$appRoot = 'C:\HanaMedia'
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
function Assert-BackupChild([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside backup root' }
    return $absolute
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$pending = Assert-BackupChild (Join-Path $root ('pending-' + $stamp))
$completed = Assert-BackupChild (Join-Path $root ('backup-' + $stamp))
New-Item -ItemType Directory -Path $pending | Out-Null
try {
    $bak = Join-Path $pending 'HanaMedia.bak'
    $escapedBak = $bak.Replace("'", "''")
    $sqlFile = Join-Path $pending 'backup.sql'
    @"
BACKUP DATABASE [HanaMedia] TO DISK=N'$escapedBak' WITH CHECKSUM, STATS=10;
RESTORE VERIFYONLY FROM DISK=N'$escapedBak' WITH CHECKSUM;
"@ | Set-Content -LiteralPath $sqlFile -Encoding UTF8
    & $sqlcmd -S '.\SQLEXPRESS' -E -C -b -V 11 -i $sqlFile -o (Join-Path $pending 'database-check.log')
    if ($LASTEXITCODE -ne 0) { throw 'SQL backup or VERIFYONLY failed; old backups retained.' }

    $snapshot = Assert-BackupChild (Join-Path $pending 'files')
    New-Item -ItemType Directory -Path $snapshot | Out-Null
    foreach ($config in @('appsettings.json','appsettings.Production.json','appsettings.Development.json')) {
        $configPath = Join-Path $appRoot $config
        if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination $snapshot }
    }
    $dataSource = Join-Path $appRoot 'App_Data'
    if (Test-Path -LiteralPath $dataSource) {
        $dataTarget = Join-Path $snapshot 'App_Data'
        New-Item -ItemType Directory -Path $dataTarget | Out-Null
        Get-ChildItem -LiteralPath $dataSource -Force | Where-Object Name -ne 'hosting' |
            Copy-Item -Destination $dataTarget -Recurse
    }
    $uploadSource = Join-Path $appRoot 'wwwroot\uploads'
    if (Test-Path -LiteralPath $uploadSource) {
        $www = Join-Path $snapshot 'wwwroot'
        New-Item -ItemType Directory -Path $www | Out-Null
        Copy-Item -LiteralPath $uploadSource -Destination $www -Recurse
    }
    $hashes = @{}
    Get-ChildItem -LiteralPath $snapshot -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($snapshot.Length + 1).Replace('\','/')
        $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = Join-Path $pending 'application-files.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($snapshot, $archive)
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $checked = 0
        foreach ($entry in $zip.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name) -or $entry.FullName.Replace('\','/').EndsWith('/')) { continue }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','') }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($actual -ne $hashes[$entry.FullName.Replace('\','/')]) { throw 'File archive verification failed' }
            $checked++
        }
        if ($checked -ne $hashes.Count) { throw 'File archive count mismatch' }
    } finally { $zip.Dispose() }
    Remove-Item -LiteralPath (Assert-BackupChild $snapshot) -Recurse -Force
    [ordered]@{
        Job='HanaMedia-Daily-Backup'; Status='Complete'; Database='HanaMedia'
        CreatedAt=(Get-Date).ToString('o'); DatabaseVerified=$true
        DatabaseSHA256=(Get-FileHash -LiteralPath $bak).Hash
        FilesSHA256=(Get-FileHash -LiteralPath $archive).Hash
        FileHashes=$hashes; UploadFolderPresent=(Test-Path -LiteralPath $uploadSource)
        RetainSuccessfulSets=1
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $pending 'manifest.json') -Encoding UTF8
    Move-Item -LiteralPath (Assert-BackupChild $pending) -Destination (Assert-BackupChild $completed)
    # Only remove this job's older COMPLETE sets after the new set passes verification.
    Get-ChildItem -LiteralPath $root -Directory | Where-Object {
        $_.Name -match '^backup-\d{8}-\d{9}$' -and $_.FullName -ne $completed -and
        -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
    } | ForEach-Object {
        $candidate = Assert-BackupChild $_.FullName
        $marker = Join-Path $candidate 'manifest.json'
        if (Test-Path -LiteralPath $marker) {
            $old = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
            if ($old.Job -eq 'HanaMedia-Daily-Backup' -and $old.Status -eq 'Complete') {
                Remove-Item -LiteralPath $candidate -Recurse -Force
            }
        }
    }
    (Get-Date).ToString('o') + ' SUCCESS ' + $completed | Add-Content -LiteralPath (Join-Path $root 'backup-history.log')
} catch {
    (Get-Date).ToString('o') + ' FAILED ' + $_.Exception.Message | Add-Content -LiteralPath (Join-Path $root 'backup-history.log')
    throw
}
