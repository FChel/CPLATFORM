[CmdletBinding()]
param(
    [string]$Commit = 'HEAD',
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Compile
)
. "$PSScriptRoot/Waratah.Common.ps1"
$sha = (& git rev-parse --verify "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve commit.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$stage = Join-Path $output ([Guid]::NewGuid().ToString('N'))
$source = Join-Path $stage 'source'
$release = Join-Path $stage 'release'
New-Item -ItemType Directory -Path $source,(Join-Path $release 'app') -Force | Out-Null
& git archive --format=zip "--output=$stage/source.zip" $sha
if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
Expand-Archive -LiteralPath "$stage/source.zip" -DestinationPath $source
$files = @()
$sqlLines = @()
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File | Sort-Object FullName) {
    $relative = $file.FullName.Substring($source.Length + 1).Replace('\','/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($relative -cmatch '^sql/.*\.sql$') { $sqlLines += "$relative $hash" }
    if (-not (Test-ApplicationPath $relative)) { continue }
    $target = Get-SafePath (Join-Path $release 'app') $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $files += @{ path = $relative; sha256 = $hash }
}
$sqlText = ($sqlLines -join "`n") + "`n"
$hasher = [Security.Cryptography.SHA256]::Create()
try { $sqlHash = ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sqlText)))).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
$manifest = @{ schema = 1; commit = $sha; sqlSha256 = $sqlHash; files = $files }
Write-JsonFile $manifest (Join-Path $release 'manifest.json')
$null = Read-ReleaseManifest $release $sha
if ($Compile) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/aspnet_compiler.exe'
    # Use a build-only config; production web.config and UDL never enter the package.
    [IO.File]::WriteAllText((Join-Path $release 'app/web.config'), '<configuration><system.web><compilation debug="false" targetFramework="4.8"/><httpRuntime targetFramework="4.8"/></system.web></configuration>')
    try {
        & $compiler -v / -p (Join-Path $release 'app') (Join-Path $stage 'compiled')
        if ($LASTEXITCODE -ne 0) { throw 'ASP.NET compilation failed.' }
    } finally { Remove-Item -LiteralPath (Join-Path $release 'app/web.config') -Force }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $output "$sha.zip"
if (Test-Path -LiteralPath $zip) { throw "Output already exists: $zip" }
# .NET Framework's CreateFromDirectory can emit backslashes. Use canonical ZIP paths
# explicitly so the same manifest/archive validation works in PowerShell 5.1 and 7.
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $release -Recurse -File | Sort-Object FullName) {
        $name = $file.FullName.Substring($release.Length + 1).Replace('\','/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$result = @{ commit = $sha; sqlSha256 = $sqlHash; package = $zip; sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant(); files = $files.Count }
Write-JsonFile $result (Join-Path $output 'package.json')
$result | ConvertTo-Json
