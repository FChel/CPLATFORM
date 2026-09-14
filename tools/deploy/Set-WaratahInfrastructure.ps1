# Run once as an AWS administrator. Re-running reapplies this dedicated deployment configuration.
[CmdletBinding()]
param(
    [string]$Aws = 'aws',
    [string]$Region = 'us-east-1',
    [string]$AccountId = '663358704059',
    [string]$InstanceId = 'i-0b35e9aea70f49701',
    [string]$InstanceRole = 'PUKARA-Dev-IIS-SQL-InstanceRole',
    [string]$Bucket = 'cplatform-waratah-deploy-663358704059-us-east-1',
    [string]$OidcSubject = 'repo:FChel/CPLATFORM:environment:waratah',
    [string]$OutputDirectory = '.deployment/infrastructure'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = [IO.Path]::GetFullPath($OutputDirectory)
function Invoke-Aws([string[]]$Arguments) {
    $result = & $Aws @Arguments --region $Region --no-cli-pager --output json
    if ($LASTEXITCODE -ne 0) { throw "AWS failed: $($Arguments[0]) $($Arguments[1])" }
    if ($result) { return ($result | ConvertFrom-Json) }
}
function Json-Argument([string]$Name, $Value) {
    $path = Join-Path $output "$Name.json"
    [IO.File]::WriteAllText($path, ($Value | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
    return "file://$path"
}
$identity = Invoke-Aws @('sts','get-caller-identity')
if ($identity.Account -ne $AccountId) { throw 'Wrong AWS account.' }
$providerArn = "arn:aws:iam::${AccountId}:oidc-provider/token.actions.githubusercontent.com"
$providers = Invoke-Aws @('iam','list-open-id-connect-providers')
if ($providerArn -notin @($providers.OpenIDConnectProviderList.Arn)) {
    $null = Invoke-Aws @('iam','create-open-id-connect-provider','--url','https://token.actions.githubusercontent.com','--client-id-list','sts.amazonaws.com')
}
$buckets = Invoke-Aws @('s3api','list-buckets')
if ($Bucket -notin @($buckets.Buckets.Name)) {
    $arguments = @('s3api','create-bucket','--bucket',$Bucket)
    if ($Region -ne 'us-east-1') { $arguments += @('--create-bucket-configuration',"LocationConstraint=$Region") }
    $null = Invoke-Aws $arguments
}
$null = Invoke-Aws @('s3api','put-public-access-block','--bucket',$Bucket,'--public-access-block-configuration','BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true')
$null = Invoke-Aws @('s3api','put-bucket-versioning','--bucket',$Bucket,'--versioning-configuration','Status=Enabled')
$encryption = @{ Rules = @(@{ ApplyServerSideEncryptionByDefault = @{ SSEAlgorithm = 'AES256' } }) }
$null = Invoke-Aws @('s3api','put-bucket-encryption','--bucket',$Bucket,'--server-side-encryption-configuration',(Json-Argument 'encryption' $encryption))
$bucketPolicy = @{ Version = '2012-10-17'; Statement = @(@{ Sid='RequireTLS'; Effect='Deny'; Principal='*'; Action='s3:*'; Resource=@("arn:aws:s3:::$Bucket","arn:aws:s3:::$Bucket/*"); Condition=@{ Bool=@{'aws:SecureTransport'='false'} } }) }
$null = Invoke-Aws @('s3api','put-bucket-policy','--bucket',$Bucket,'--policy',(Json-Argument 'bucket-policy' $bucketPolicy))
$lifecycle = @{ Rules = @(@{ ID='ExpireReleasePackages'; Status='Enabled'; Filter=@{Prefix='releases/'}; Expiration=@{Days=90}; NoncurrentVersionExpiration=@{NoncurrentDays=30}; AbortIncompleteMultipartUpload=@{DaysAfterInitiation=1} }) }
$null = Invoke-Aws @('s3api','put-bucket-lifecycle-configuration','--bucket',$Bucket,'--lifecycle-configuration',(Json-Argument 'lifecycle' $lifecycle))
$roleName = 'GitHub-CPLATFORM-WARATAH'
$trust = @{ Version='2012-10-17'; Statement=@(@{Effect='Allow'; Principal=@{Federated=$providerArn}; Action='sts:AssumeRoleWithWebIdentity'; Condition=@{StringEquals=@{'token.actions.githubusercontent.com:aud'='sts.amazonaws.com';'token.actions.githubusercontent.com:sub'=$OidcSubject}}}) }
$roles = Invoke-Aws @('iam','list-roles')
if ($roleName -notin @($roles.Roles.RoleName)) {
    $null = Invoke-Aws @('iam','create-role','--role-name',$roleName,'--assume-role-policy-document',(Json-Argument 'trust' $trust),'--description','GitHub Actions CPLATFORM waratah environment deployment')
} else { $null = Invoke-Aws @('iam','update-assume-role-policy','--role-name',$roleName,'--policy-document',(Json-Argument 'trust' $trust)) }
$documentName = 'CPLATFORM-Deploy-Waratah-v1'
$policy = @{Version='2012-10-17'; Statement=@(
    @{Effect='Allow';Action=@('s3:PutObject','s3:AbortMultipartUpload');Resource="arn:aws:s3:::$Bucket/releases/*"},
    @{Effect='Allow';Action='ssm:SendCommand';Resource=@("arn:aws:ssm:${Region}:${AccountId}:document/$documentName","arn:aws:ec2:${Region}:${AccountId}:instance/$InstanceId")},
    @{Effect='Allow';Action='ssm:GetCommandInvocation';Resource='*'}
)}
$null = Invoke-Aws @('iam','put-role-policy','--role-name',$roleName,'--policy-name','WaratahDeployment','--policy-document',(Json-Argument 'deployment-policy' $policy))
$downloadPolicy = @{Version='2012-10-17'; Statement=@(@{Effect='Allow';Action='s3:GetObject';Resource="arn:aws:s3:::$Bucket/*"})}
$null = Invoke-Aws @('iam','put-role-policy','--role-name',$InstanceRole,'--policy-name','CPlatformDeploymentDownload','--policy-document',(Json-Argument 'instance-download-policy' $downloadPolicy))
$document = @{schemaVersion='2.2';description='Deploy a verified CPLATFORM package using the administrator-installed script';parameters=@{
    Commit=@{type='String';allowedPattern='^[0-9a-f]{40}$';interpolationType='ENV_VAR'}
    PackageSha256=@{type='String';allowedPattern='^[0-9a-f]{64}$';interpolationType='ENV_VAR'}
    RunId=@{type='String';allowedPattern='^[0-9]+-[0-9]+$';interpolationType='ENV_VAR'}
};mainSteps=@(@{action='aws:runPowerShellScript';name='Deploy';precondition=@{StringEquals=@('platformType','Windows')};inputs=@{timeoutSeconds='1800';runCommand=@(
    '$ErrorActionPreference = ''Stop''',
    'try { & ''C:\ProgramData\CPLATFORM-Deploy\tools\Deploy-Waratah.ps1'' -Commit $env:SSM_Commit -PackageSha256 $env:SSM_PackageSha256 -RunId $env:SSM_RunId; exit 0 } catch { Write-Error $_; exit 1 }'
)}})}
$documents = Invoke-Aws @('ssm','list-documents','--filters',"Key=Name,Values=$documentName")
if ($documentName -notin @($documents.DocumentIdentifiers.Name)) {
    $null = Invoke-Aws @('ssm','create-document','--name',$documentName,'--document-type','Command','--document-format','JSON','--content',(Json-Argument 'ssm-document' $document))
} else {
    # Workflow pins version 1. Changing a trusted command requires a new document name/version rollout.
    $null = Json-Argument 'ssm-document' $document
    Write-Output 'SSM document already exists; version 1 was left unchanged.'
}
@{AWS_REGION=$Region;AWS_INSTANCE_ID=$InstanceId;AWS_ROLE_ARN="arn:aws:iam::${AccountId}:role/$roleName";DEPLOY_BUCKET=$Bucket;SSM_DOCUMENT_NAME=$documentName} | ConvertTo-Json
