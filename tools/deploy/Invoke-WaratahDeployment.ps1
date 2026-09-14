[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageInfo)
$ErrorActionPreference = 'Stop'
$info = Get-Content -LiteralPath $PackageInfo -Raw | ConvertFrom-Json
foreach ($name in @('AWS_REGION','AWS_INSTANCE_ID','DEPLOY_BUCKET','SSM_DOCUMENT_NAME','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT')) {
    if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Missing variable $name" }
}
$runId = "$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
$key = "releases/$($info.commit)/$runId.zip"
& aws s3 cp $info.package "s3://$env:DEPLOY_BUCKET/$key" --sse AES256 --only-show-errors
if ($LASTEXITCODE -ne 0) { throw 'Package upload failed.' }
$parameters = @{ Commit = @($info.commit); PackageSha256 = @($info.sha256); RunId = @($runId) }
$parameters | ConvertTo-Json | Set-Content -LiteralPath "$PackageInfo.parameters.json" -Encoding utf8
$commandId = & aws ssm send-command --instance-ids $env:AWS_INSTANCE_ID --document-name $env:SSM_DOCUMENT_NAME --document-version 1 --parameters "file://$PackageInfo.parameters.json" --timeout-seconds 600 --comment "CPLATFORM $($info.commit) run $runId" --query Command.CommandId --output text
if ($LASTEXITCODE -ne 0) { throw 'SSM dispatch failed.' }
$commandId = $commandId.Trim()
Write-Output "SSM command: $commandId; commit: $($info.commit)"
if ($env:GITHUB_STEP_SUMMARY) { "Commit: $($info.commit)`n`nSSM command: $commandId`n`nPackage SHA256: $($info.sha256)" | Add-Content $env:GITHUB_STEP_SUMMARY }
$deadline = (Get-Date).AddMinutes(40)
do {
    Start-Sleep -Seconds 15
    $raw = & aws ssm get-command-invocation --command-id $commandId --instance-id $env:AWS_INSTANCE_ID --output json 2> "$PackageInfo.ssm-error.txt"
    if ($LASTEXITCODE -ne 0) {
        $detail = Get-Content "$PackageInfo.ssm-error.txt" -Raw
        if ($detail -match 'InvocationDoesNotExist') { continue }
        throw "Cannot read SSM result; command may still run: $commandId. $detail"
    }
    $result = $raw | ConvertFrom-Json
    if ($result.Status -in @('Pending','InProgress','Delayed')) { continue }
    Write-Output $result.StandardOutputContent
    if ($result.Status -ne 'Success' -or $result.ResponseCode -ne 0 -or $result.StandardOutputContent -notmatch "DEPLOYED commit=$($info.commit) run=$runId") {
        throw "Deployment failed ($($result.Status)): $($result.StandardErrorContent)"
    }
    exit 0
} while ((Get-Date) -lt $deadline)
throw "Timed out waiting for SSM $commandId. It may still be running; inspect it before another deployment."
