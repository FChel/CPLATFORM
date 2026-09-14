[CmdletBinding()]
param([string]$Repository = 'FChel/CPLATFORM')
$ErrorActionPreference = 'Stop'
$token = $env:GH_TOKEN
if (-not $token) { $token = $env:GITHUB_TOKEN }
if (-not $token) {
    $credential = @{}
    foreach ($line in ("protocol=https`nhost=github.com`n`n" | git credential fill)) {
        $pair = $line -split '=',2
        if ($pair.Length -eq 2) { $credential[$pair[0]] = $pair[1] }
    }
    $token = $credential.password
}
if (-not $token) { throw 'Authenticate GitHub CLI/Git or set GH_TOKEN with repository administration permission.' }
$headers = @{Authorization="Bearer $token";Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
function Invoke-GitHub([string]$Method,[string]$Path,$Body=$null) {
    $args = @{Method=$Method;Uri="https://api.github.com/repos/$Repository/$Path";Headers=$headers}
    if ($null -ne $Body) { $args.Body=$Body | ConvertTo-Json -Depth 8; $args.ContentType='application/json' }
    Invoke-RestMethod @args
}
$oidc = Invoke-GitHub GET 'actions/oidc/customization/sub'
if (-not $oidc.use_default -or $oidc.sub_claim_prefix -ne 'repo:FChel/CPLATFORM') { throw 'OIDC subject changed: review AWS trust before continuing.' }
$null = Invoke-GitHub PUT 'environments/waratah' @{deployment_branch_policy=@{protected_branches=$false;custom_branch_policies=$true}}
$policies = Invoke-GitHub GET 'environments/waratah/deployment-branch-policies'
if (@($policies.branch_policies | Where-Object { $_.name -ne 'uat' -or $_.type -ne 'branch' }).Count -gt 0) { throw 'Unexpected existing environment branch policy; review it manually.' }
if (-not @($policies.branch_policies).Count) { $null = Invoke-GitHub POST 'environments/waratah/deployment-branch-policies' @{name='uat';type='branch'} }
$variables = @{
    AWS_REGION='us-east-1'; AWS_INSTANCE_ID='i-0b35e9aea70f49701';
    AWS_ROLE_ARN='arn:aws:iam::663358704059:role/GitHub-CPLATFORM-WARATAH';
    DEPLOY_BUCKET='cplatform-waratah-deploy-663358704059-us-east-1';
    SSM_DOCUMENT_NAME='CPLATFORM-Deploy-Waratah-v1'
}
$existing = Invoke-GitHub GET 'environments/waratah/variables'
foreach ($name in $variables.Keys) {
    if ($name -in @($existing.variables.name)) { $null = Invoke-GitHub PATCH "environments/waratah/variables/$name" @{name=$name;value=$variables[$name]} }
    else { $null = Invoke-GitHub POST 'environments/waratah/variables' @{name=$name;value=$variables[$name]} }
}
$repoVariables = Invoke-GitHub GET 'actions/variables'
if ('WARATAH_AUTO_DEPLOY' -notin @($repoVariables.variables.name)) {
    $null = Invoke-GitHub POST 'actions/variables' @{name='WARATAH_AUTO_DEPLOY';value='false'}
}
Write-Output 'waratah environment configured for the uat branch. Automatic deployment remains at its existing value (false on first setup).'
