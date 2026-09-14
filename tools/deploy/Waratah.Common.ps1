Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ApplicationPath([string]$Path) {
    if ($Path -match '[\\:]|(^|/)\.{1,2}(/|$)|(^|/)(web\.config|app_offline\.htm)$' -or
        $Path -match '(?i)(^|/)(App_Data|Database|uploads|evidence)(/|$)') { return $false }
    return $Path -cmatch '^((App_Code|bin|css|js|images|LPPI|NORM|Prepayment|eJET)/[A-Za-z0-9_./ -]+\.(cs|dll|css|js|png|jpg|jpeg|gif|svg|ico|aspx|ashx|master|ascx|html)|[A-Za-z0-9_-]+\.aspx(\.cs)?)$'
}

function Get-SafePath([string]$Root, [string]$Relative) {
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes root: $Relative" }
    $cursor = $full
    while ($cursor -and $cursor.Length -ge $base.TrimEnd('\','/').Length) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Reparse point is not permitted: $cursor"
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

function Write-JsonFile($Value, [string]$Path) {
    $temp = "$Path.tmp"
    [IO.File]::WriteAllText($temp, ($Value | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Read-ReleaseManifest([string]$Directory, [string]$Commit) {
    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or $manifest.commit -cne $Commit -or $Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Release commit/schema mismatch.' }
    if ($manifest.sqlSha256 -cnotmatch '^[0-9a-f]{64}$' -or @($manifest.files).Count -eq 0) { throw 'Invalid manifest.' }
    $seen = @{}
    foreach ($entry in $manifest.files) {
        if (-not (Test-ApplicationPath $entry.path) -or $seen.ContainsKey($entry.path) -or $entry.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "Invalid/duplicate manifest entry: $($entry.path)" }
        $seen[$entry.path] = $true
        $file = Get-SafePath (Join-Path $Directory 'app') $entry.path
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.sha256) { throw "Hash mismatch: $($entry.path)" }
    }
    foreach ($required in @('CFO-Toolkit.aspx','CFO-Toolkit.aspx.cs','Default.aspx','Default.aspx.cs','bin/EPPlus-LGPL.dll')) {
        if (-not $seen.ContainsKey($required)) { throw "Missing required file: $required" }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Directory 'app') -Recurse -File) {
        $relative = $file.FullName.Substring((Join-Path $Directory 'app').TrimEnd('\','/').Length + 1).Replace('\','/')
        if (-not $seen.ContainsKey($relative)) { throw "Unlisted payload file: $relative" }
    }
    return $manifest
}
