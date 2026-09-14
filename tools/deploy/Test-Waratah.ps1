[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseUrl,
    [Parameter(Mandatory)][object[]]$Checks,
    [string]$HostHeader = '',
    [int]$Attempts = 12
)
$ErrorActionPreference = 'Stop'
foreach ($check in $Checks) {
    $uri = [Uri]::new([Uri]($BaseUrl.TrimEnd('/') + '/'), [string]$check.path)
    if ($uri.Authority -ne ([Uri]$BaseUrl).Authority) { throw 'Health check must stay on the configured host.' }
    $passed = $false
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $headers = @{}
            if ($HostHeader) { $headers.Host = $HostHeader }
            $response = Invoke-WebRequest -Uri $uri -Headers $headers -UseBasicParsing -UseDefaultCredentials -MaximumRedirection 0 -TimeoutSec 20
            if ([int]$response.StatusCode -ne 200 -or $response.Content -notmatch $check.pattern) { throw 'Unexpected status or content.' }
            $passed = $true
            Write-Output "PASS $($check.path)"
            break
        } catch { if ($attempt -lt $Attempts) { Start-Sleep -Seconds 5 } }
    }
    if (-not $passed) { throw "Health check failed: $uri (requires HTTP 200 and expected content)." }
}
