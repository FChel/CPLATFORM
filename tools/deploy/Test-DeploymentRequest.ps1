# Exercise the runner's AWS CLI contract without contacting AWS.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('waratah-request-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$info = @{commit=('a'*40);sha256=('b'*64);package=(Join-Path $fixture 'package.zip')}
$infoPath = Join-Path $fixture 'package.json'
$info | ConvertTo-Json | Set-Content -LiteralPath $infoPath
$savedEnvironment = @{}
$values = @{AWS_REGION='us-east-1';AWS_INSTANCE_ID='i-test';DEPLOY_BUCKET='test';SSM_DOCUMENT_NAME='test';GITHUB_RUN_ID='123';GITHUB_RUN_ATTEMPT='1';GITHUB_STEP_SUMMARY=(Join-Path $fixture 'summary.md')}
foreach ($key in $values.Keys) { $savedEnvironment[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$values[$key]) }
$global:RequestTestAccount='663358704059'
$global:RequestTestUploads=0
$global:RequestTestCommit=$info.commit
$global:RequestTestSent=$false
function global:aws {
    $global:LASTEXITCODE=0
    if ($args[0] -eq 'sts') { return $global:RequestTestAccount }
    if ($args[0] -eq 's3') { $global:RequestTestUploads++; return }
    if ($args[1] -eq 'send-command') {
        $index=[Array]::IndexOf($args,'--parameters')
        $path=([string]$args[$index+1]).Substring(7)
        $bytes=[IO.File]::ReadAllBytes($path)
        if ($bytes[0] -ne 123) { throw 'AWS parameter file must begin with {, without a UTF-8 BOM.' }
        $data=[Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        if ($data.Commit[0] -ne $global:RequestTestCommit -or $data.RunId[0] -ne '123-1' -or $data.PackageSha256[0] -ne ('b'*64)) { throw 'Incorrect SSM command parameters.' }
        $global:RequestTestSent=$true
        return '11111111-1111-1111-1111-111111111111'
    }
    if ($args[1] -eq 'get-command-invocation') {
        return (@{Status='Success';ResponseCode=0;StandardOutputContent="DEPLOYED commit=$global:RequestTestCommit run=123-1";StandardErrorContent=''} | ConvertTo-Json)
    }
    throw "Unexpected AWS command: $args"
}
function global:Start-Sleep { param($Seconds) }
try {
    & "$PSScriptRoot/Invoke-WaratahDeployment.ps1" -PackageInfo $infoPath
    if (-not $global:RequestTestSent -or $global:RequestTestUploads -ne 1) { throw 'Expected exactly one upload and deployment request.' }
    $global:RequestTestAccount='000000000000'
    $caught=$null
    try { & "$PSScriptRoot/Invoke-WaratahDeployment.ps1" -PackageInfo $infoPath } catch { $caught=$_ }
    if ($null -eq $caught -or "$caught" -notmatch 'WARATAH account' -or $global:RequestTestUploads -ne 1) { throw 'Wrong AWS account was not rejected before upload.' }
    Write-Output 'PASS: BOM-free SSM parameters, exact commit/hash/run ID, successful command polling and wrong-account rejection.'
} finally {
    Remove-Item Function:/aws,Function:/Start-Sleep
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key,$savedEnvironment[$key]) }
}
