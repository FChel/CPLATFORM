# Integration tests run against a temporary fake IIS/S3 environment; never contacts AWS or IIS.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageInfo)
$ErrorActionPreference = 'Stop'
$info = Get-Content -LiteralPath $PackageInfo -Raw | ConvertFrom-Json
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('waratah-safety-' + [Guid]::NewGuid().ToString('N'))
$testTools = Join-Path $testRoot 'tools'
$state = Join-Path $testRoot 'state'
$app = Join-Path $testRoot 'site'
New-Item -ItemType Directory -Path $testTools,$state,$app,"$testRoot/modules/WebAdministration" -Force | Out-Null
Copy-Item -Path "$PSScriptRoot/*.ps1" -Destination $testTools
@'
param($BaseUrl,$HostHeader,$Checks,$Attempts)
$global:HealthCalls++
if ($global:FailHealthCall -eq $global:HealthCalls) { throw 'Forced post-deployment health failure' }
'@ | Set-Content "$testTools/Test-Waratah.ps1"
@'
function Get-Website { param($Name) [pscustomobject]@{PhysicalPath=$global:FixtureApp;ApplicationPool='CPLATFORM';State='Started'} }
function Get-WebAppPoolState { param($Name) [pscustomobject]@{Value=$global:FixturePool} }
function Stop-WebAppPool { param($Name) $global:FixturePool='Stopped' }
function Start-WebAppPool { param($Name) $global:FixturePool='Started' }
Export-ModuleMember -Function *
'@ | Set-Content "$testRoot/modules/WebAdministration/WebAdministration.psm1"
$oldModulePath = $env:PSModulePath
$env:PSModulePath = "$testRoot/modules;$oldModulePath"
$global:FixtureApp = $app
$global:FixturePool = 'Started'
$global:FixtureZip = $info.package
function global:Read-S3Object { param($BucketName,$Key,$File,$Region) Copy-Item -LiteralPath $global:FixtureZip -Destination $File }
. "$PSScriptRoot/Waratah.Common.ps1"
function Assert($Condition,[string]$Message) { if (-not $Condition) { throw "FAIL: $Message" } }
function Expect-Failure([scriptblock]$Action,[string]$Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    Assert ($null -ne $caught -and "$caught" -match $Pattern) "Expected failure matching $Pattern; got $caught"
}
try {
    foreach ($path in @('../escape.aspx','App_Code/../../escape.cs','web.config','App_Code/web.config','Database/a.udl','NORM/uploads/a.aspx','NORM/evidence/a.aspx','C:\x.aspx')) {
        Assert (-not (Test-ApplicationPath $path)) "Reject protected path $path"
    }
    Expect-Failure { Get-SafePath $app '../escape.aspx' } 'escapes root'
    $config = @{siteName='CPLATFORM';applicationPath=$app;applicationPool='CPLATFORM';baseUrl='http://localhost/';hostHeader='';checks=@(@{path='CFO-Toolkit.aspx';pattern='CFO'});region='us-east-1';bucket='test'}
    Write-JsonFile $config "$state/config.json"
    Write-JsonFile @{sqlSha256=$info.sqlSha256} "$state/database-baseline.json"
    New-Item -ItemType Directory -Path "$app/App_Data/Uploads","$app/Database","$app/css" -Force | Out-Null
    [IO.File]::WriteAllText("$app/web.config",'server config')
    [IO.File]::WriteAllText("$app/Database/CPlatform.udl",'server connection')
    [IO.File]::WriteAllText("$app/App_Data/Uploads/evidence.txt",'evidence')
    [IO.File]::WriteAllText("$app/css/unknown.css",'unmanaged')
    [IO.File]::WriteAllText("$app/css/obsolete.css",'previous managed file')
    [IO.File]::WriteAllText("$app/CFO-Toolkit.aspx",'previous home page')
    $previous = @{schema=1;commit=('b'*40);sqlSha256=$info.sqlSha256;files=@(@{path='css/obsolete.css';sha256=('0'*64)},@{path='CFO-Toolkit.aspx';sha256=('0'*64)})}
    Write-JsonFile $previous "$state/current.json"
    $global:HealthCalls=0; $global:FailHealthCall=2
    Expect-Failure { & "$testTools/Deploy-Waratah.ps1" -Commit $info.commit -PackageSha256 $info.sha256 -RunId '100-1' -ConfigPath "$state/config.json" } 'previous files restored'
    Assert ((Get-Content "$app/CFO-Toolkit.aspx" -Raw) -eq 'previous home page') 'Rollback restores replaced file'
    Assert ((Get-Content "$app/css/obsolete.css" -Raw) -eq 'previous managed file') 'Rollback restores removed file'
    Assert (-not (Test-Path "$app/Default.aspx")) 'Rollback removes newly added file'
    Assert (-not (Test-Path "$state/pending.json")) 'Rollback clears transaction'
    Assert ($global:FixturePool -eq 'Started') 'Rollback starts pool'
    $global:HealthCalls=0; $global:FailHealthCall=0
    & "$testTools/Deploy-Waratah.ps1" -Commit $info.commit -PackageSha256 $info.sha256 -RunId '101-1' -ConfigPath "$state/config.json"
    Assert (-not (Test-Path "$app/css/obsolete.css")) 'Success removes only previously managed stale file'
    Assert ((Get-Content "$app/css/unknown.css" -Raw) -eq 'unmanaged') 'Preserve unknown server files'
    Assert ((Get-Content "$app/web.config" -Raw) -eq 'server config') 'Preserve web.config'
    Assert ((Get-Content "$app/Database/CPlatform.udl" -Raw) -eq 'server connection') 'Preserve UDL'
    Assert ((Get-Content "$app/App_Data/Uploads/evidence.txt" -Raw) -eq 'evidence') 'Preserve evidence'
    Assert ((Get-Content "$state/current.json" -Raw | ConvertFrom-Json).commit -eq $info.commit) 'Record deployed commit'
    Expect-Failure { & "$testTools/Deploy-Waratah.ps1" -Commit $info.commit -PackageSha256 ('0'*64) -RunId '102-1' -ConfigPath "$state/config.json" } 'checksum mismatch'
    Write-JsonFile @{sqlSha256=('0'*64)} "$state/database-baseline.json"
    Expect-Failure { & "$testTools/Deploy-Waratah.ps1" -Commit $info.commit -PackageSha256 $info.sha256 -RunId '103-1' -ConfigPath "$state/config.json" } 'SQL source changed'
    $caseNumber = 104
    foreach ($names in @(@('../escape.aspx'), @('app/web.config'), @('manifest.json','manifest.json'))) {
        $badZip = Join-Path $testRoot "$caseNumber.zip"
        $archive = [IO.Compression.ZipFile]::Open($badZip, [IO.Compression.ZipArchiveMode]::Create)
        try { foreach ($name in $names) { $null = $archive.CreateEntry($name) } } finally { $archive.Dispose() }
        $global:FixtureZip = $badZip
        $badHash = (Get-FileHash -LiteralPath $badZip).Hash.ToLowerInvariant()
        Expect-Failure { & "$testTools/Deploy-Waratah.ps1" -Commit $info.commit -PackageSha256 $badHash -RunId "$caseNumber-1" -ConfigPath "$state/config.json" } 'Unexpected ZIP entry|Duplicate ZIP entry'
        $caseNumber++
    }
    Assert (-not (Test-Path "$app/app_offline.htm")) 'Preflight failures leave site online'
    Write-Output 'PASS: deployment, rollback, stale-file removal, preservation, traversal, checksum and SQL gate tests.'
} finally {
    Remove-Module WebAdministration -ErrorAction SilentlyContinue
    Remove-Item Function:/Read-S3Object -ErrorAction SilentlyContinue
    $env:PSModulePath = $oldModulePath
    # Retain fixture for inspection; the path is printed rather than recursively deleting files.
    Write-Output "Safety test fixture: $testRoot"
}
