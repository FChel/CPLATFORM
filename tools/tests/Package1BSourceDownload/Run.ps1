[CmdletBinding()]
param([switch]$Baseline)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output=Join-Path ([IO.Path]::GetTempPath()) ('norm-source-download-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$source=Join-Path $root 'App_Code/NORMSourceFileHandler.cs'
if($Baseline){
    $old=& git -C $root show origin/uat:App_Code/NORMSourceFileHandler.cs
    if($LASTEXITCODE -ne 0){throw 'Cannot read baseline handler'}
    $source=Join-Path $output 'NORMSourceFileHandler.cs'
    [IO.File]::WriteAllText($source,($old -join "`n"))
}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /warnaserror+ /target:exe "/out:$output/Tests.exe" /r:System.Data.dll /r:System.Web.dll `
    $source (Join-Path $PSScriptRoot 'TestSupport.cs') (Join-Path $PSScriptRoot 'Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Source-download test compilation failed'}
& (Join-Path $output 'Tests.exe') $root ([string]$Baseline.IsPresent)
if($LASTEXITCODE -ne 0){throw 'Source-download tests failed'}
Write-Output "Isolated artifacts: $output"
