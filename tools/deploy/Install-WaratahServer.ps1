# Run in an elevated Windows PowerShell session on WARATAH after reviewing the SQL baseline.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$SqlSha256,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$DatabaseApproval,
    [string]$ConfigSource = "$PSScriptRoot/waratah.config.json"
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration
$config = Get-Content -LiteralPath $ConfigSource -Raw | ConvertFrom-Json
$site = Get-Website -Name $config.siteName
if ([IO.Path]::GetFullPath($site.PhysicalPath).TrimEnd('\') -ne [IO.Path]::GetFullPath($config.applicationPath).TrimEnd('\') -or $site.ApplicationPool -ne $config.applicationPool) { throw 'IIS target mismatch.' }
if (-not (Get-Command Read-S3Object -ErrorAction SilentlyContinue)) { throw 'Install AWS Tools for PowerShell with S3 support first.' }
& "$PSScriptRoot/Test-Waratah.ps1" -BaseUrl $config.baseUrl -HostHeader $config.hostHeader -Checks $config.checks -Attempts 2
$state = 'C:\ProgramData\CPLATFORM-Deploy'
New-Item -ItemType Directory -Path "$state\tools" -Force | Out-Null
& icacls.exe $state /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F'
if ($LASTEXITCODE -ne 0) { throw 'Cannot secure deployment directory.' }
$lock = [IO.File]::Open("$state\deploy.lock", 'OpenOrCreate', 'ReadWrite', 'None')
try {
    if (Test-Path -LiteralPath "$state\pending.json") { throw 'Recover interrupted deployment first.' }
    foreach ($name in @('Waratah.Common.ps1','Deploy-Waratah.ps1','Test-Waratah.ps1')) {
        Copy-Item -LiteralPath "$PSScriptRoot/$name" -Destination "$state\tools\$name" -Force
    }
    Copy-Item -LiteralPath $ConfigSource -Destination "$state\config.json" -Force
    . "$PSScriptRoot/Waratah.Common.ps1"
    $record = @{sqlSha256=$SqlSha256; approval=$DatabaseApproval; recordedBy=[Security.Principal.WindowsIdentity]::GetCurrent().Name; recordedUtc=[DateTime]::UtcNow.ToString('o')}
    ($record | ConvertTo-Json -Compress) | Add-Content -LiteralPath "$state\database-history.jsonl"
    Write-JsonFile $record "$state\database-baseline.json"
} finally { $lock.Dispose() }
Write-Output 'WARATAH deployment scripts installed. No application files or SQL were changed.'
