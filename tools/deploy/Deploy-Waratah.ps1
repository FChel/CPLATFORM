[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$PackageSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+-[0-9]+$')][string]$RunId,
    [string]$ConfigPath = 'C:\ProgramData\CPLATFORM-Deploy\config.json'
)
. "$PSScriptRoot/Waratah.Common.ps1"
Import-Module WebAdministration
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($config.applicationPath).TrimEnd('\')
$state = [IO.Path]::GetFullPath((Split-Path $ConfigPath))
if ($state.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment state must be outside IIS.' }
$site = Get-Website -Name $config.siteName
if ([IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($site.PhysicalPath)).TrimEnd('\') -ne $root -or $site.ApplicationPool -ne $config.applicationPool) { throw 'Live IIS target differs from approved configuration.' }
if ($site.State -ne 'Started' -or (Get-WebAppPoolState -Name $config.applicationPool).Value -ne 'Started') { throw 'Site and pool must be running before deployment.' }
$null = Get-SafePath $root 'web.config'
$lock = [IO.File]::Open((Join-Path $state 'deploy.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $pending = Join-Path $state 'pending.json'
    if (Test-Path -LiteralPath $pending) { throw 'An interrupted deployment requires administrator recovery; see pending.json.' }
    $offline = Get-SafePath $root 'app_offline.htm'
    if (Test-Path -LiteralPath $offline) { throw 'Existing app_offline.htm: refusing to interfere with maintenance.' }
    $work = Get-SafePath $state "runs/$RunId"
    if (Test-Path -LiteralPath $work) { throw 'Run directory already exists; use a new workflow attempt.' }
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $zip = Join-Path $work 'package.zip'
    Read-S3Object -BucketName $config.bucket -Key "releases/$Commit/$RunId.zip" -File $zip -Region $config.region | Out-Null
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -cne $PackageSha256) { throw 'Downloaded package checksum mismatch.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $release = Join-Path $work 'release'
    New-Item -ItemType Directory -Path $release | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = @{}
        [long]$total = 0
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ($name -eq 'manifest.json') { }
            elseif ($name.StartsWith('app/') -and (Test-ApplicationPath $name.Substring(4))) { }
            else { throw "Unexpected ZIP entry: $name" }
            if ($entries.ContainsKey($name)) { throw 'Duplicate ZIP entry.' }
            $entries[$name] = $true
            $total += $entry.Length
            if ($total -gt 1GB) { throw 'Expanded package exceeds 1 GB.' }
            $target = Get-SafePath $release $name
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $archive.Dispose() }
    $manifest = Read-ReleaseManifest $release $Commit
    $database = Get-Content -LiteralPath (Join-Path $state 'database-baseline.json') -Raw | ConvertFrom-Json
    if ($database.sqlSha256 -cne $manifest.sqlSha256) { throw 'SQL source changed. Apply/review migrations and record the approved SQL digest before deploying.' }
    $currentPath = Join-Path $state 'current.json'
    $previous = $null
    $affected = @($manifest.files.path)
    if (Test-Path -LiteralPath $currentPath) {
        $previous = Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json
        $affected += @($previous.files.path)
    }
    $affected = @($affected | Sort-Object -Unique)
    $backup = Join-Path $work 'backup'
    New-Item -ItemType Directory -Path $backup | Out-Null
    $records = @()
    foreach ($path in $affected) {
        if (-not (Test-ApplicationPath $path)) { throw "Protected path in deployment state: $path" }
        $target = Get-SafePath $root $path
        $records += @{ path = $path; existed = (Test-Path -LiteralPath $target -PathType Leaf) }
    }
    $configHashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force | Where-Object { $_.Name -ieq 'web.config' -or $_.Extension -ieq '.udl' }) {
        $configHashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    # Baseline health must pass before any application files are changed.
    & "$PSScriptRoot/Test-Waratah.ps1" -BaseUrl $config.baseUrl -HostHeader $config.hostHeader -Checks $config.checks -Attempts 2
    Write-JsonFile @{ runId = $RunId; commit = $Commit; previous = $previous; files = $records; backup = $backup; phase = 'stopping' } $pending
    $backupComplete = $false
    $mutationStarted = $false
    try {
        [IO.File]::WriteAllText($offline, '<html><body>CPLATFORM maintenance in progress.</body></html>')
        Stop-WebAppPool -Name $config.applicationPool
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-WebAppPoolState -Name $config.applicationPool).Value -ne 'Stopped') {
            if ((Get-Date) -gt $deadline) { throw 'Application pool did not stop.' }
            Start-Sleep -Seconds 1
        }
        foreach ($record in $records) {
            if ($record.existed) {
                $destination = Get-SafePath $backup $record.path
                New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
                Copy-Item -LiteralPath (Get-SafePath $root $record.path) -Destination $destination
            }
        }
        $backupComplete = $true
        Write-JsonFile @{ runId = $RunId; commit = $Commit; previous = $previous; files = $records; backup = $backup; phase = 'copying' } $pending
        $mutationStarted = $true
        $newPaths = @{}
        foreach ($entry in $manifest.files) {
            $newPaths[$entry.path] = $true
            $destination = Get-SafePath $root $entry.path
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath (Get-SafePath (Join-Path $release 'app') $entry.path) -Destination $destination -Force
            if ((Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -cne $entry.sha256) { throw "Deployed hash mismatch: $($entry.path)" }
        }
        foreach ($record in $records) {
            if (-not $newPaths.ContainsKey($record.path) -and $record.existed) { Remove-Item -LiteralPath (Get-SafePath $root $record.path) -Force }
        }
        foreach ($path in $configHashes.Keys) {
            if ((Get-FileHash -LiteralPath $path).Hash -ne $configHashes[$path]) { throw 'Server configuration changed during deployment.' }
        }
        Remove-Item -LiteralPath $offline -Force
        Start-WebAppPool -Name $config.applicationPool
        & "$PSScriptRoot/Test-Waratah.ps1" -BaseUrl $config.baseUrl -HostHeader $config.hostHeader -Checks $config.checks
        Write-JsonFile $manifest $currentPath
        Copy-Item -LiteralPath $pending -Destination (Join-Path $work 'transaction.json')
        Remove-Item -LiteralPath $pending
        Write-Output "DEPLOYED commit=$Commit run=$RunId backup=$backup"
    } catch {
        $deploymentError = $_
        try {
            if ($mutationStarted -and $backupComplete) {
                [IO.File]::WriteAllText($offline, '<html><body>CPLATFORM rollback in progress.</body></html>')
                if ((Get-WebAppPoolState -Name $config.applicationPool).Value -ne 'Stopped') { Stop-WebAppPool -Name $config.applicationPool }
                $deadline = (Get-Date).AddSeconds(60)
                while ((Get-WebAppPoolState -Name $config.applicationPool).Value -ne 'Stopped') {
                    if ((Get-Date) -gt $deadline) { throw 'Pool did not stop for rollback.' }
                    Start-Sleep -Seconds 1
                }
                foreach ($record in $records) {
                    $destination = Get-SafePath $root $record.path
                    if ($record.existed) { Copy-Item -LiteralPath (Get-SafePath $backup $record.path) -Destination $destination -Force }
                    elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
                }
            }
            if (Test-Path -LiteralPath $offline) { Remove-Item -LiteralPath $offline -Force }
            if ((Get-WebAppPoolState -Name $config.applicationPool).Value -ne 'Started') { Start-WebAppPool -Name $config.applicationPool }
            & "$PSScriptRoot/Test-Waratah.ps1" -BaseUrl $config.baseUrl -HostHeader $config.hostHeader -Checks $config.checks
            if ($null -ne $previous) { Write-JsonFile $previous $currentPath }
            elseif (Test-Path -LiteralPath $currentPath) { Remove-Item -LiteralPath $currentPath }
            Copy-Item -LiteralPath $pending -Destination (Join-Path $work 'rolled-back.json')
            Remove-Item -LiteralPath $pending
        } catch { throw "Deployment failed: $deploymentError. Recovery failed: $_. Preserve pending.json and recover manually." }
        throw "Deployment failed and previous files restored: $deploymentError"
    }
} finally { $lock.Dispose() }
