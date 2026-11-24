[CmdletBinding()]
param([string]$NihracsWorkbook, [string]$NihracsReplacementWorkbook)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-package1c-step4-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$epplus = Join-Path $root 'bin/EPPlus-LGPL.dll'
Copy-Item -LiteralPath $epplus -Destination $output
$annualCode = [IO.File]::ReadAllText((Join-Path $root 'App_Code/NORMAnnualSourceConfiguration.cs'))
[IO.File]::WriteAllText((Join-Path $output 'NORMAnnualSourceConfiguration.cs'), $annualCode.Replace('using System.Data.OleDb;', 'using TestDb;'))
foreach ($name in @('NORMImportService','NORMSourceHistory')) {
    $code = [IO.File]::ReadAllText((Join-Path $root "App_Code/$name.cs"))
    [IO.File]::WriteAllText((Join-Path $output "$name.cs"), $code.Replace('using System.Data.OleDb;', 'using TestDb;'))
}
$options = @('/nologo','/warnaserror+','/define:SERVICE;STEP2;STEP3;STEP4;NIHRACS','/target:exe',"/out:$output/Tests.exe",'/r:System.Data.dll','/r:System.Web.dll',"/r:$epplus")
foreach ($name in @('NORMCrypto','NORMImportModels','ErpTrialBalanceImporter','RomanTrialBalanceImporter','NORMTextTrialBalanceImporter','NihracsTrialBalanceImporter','NORMSourceFileHandler')) { $options += Join-Path $root "App_Code/$name.cs" }
$options += @((Join-Path $output 'NORMImportService.cs'),(Join-Path $output 'NORMAnnualSourceConfiguration.cs'),(Join-Path $output 'NORMSourceHistory.cs'),(Join-Path $root 'tools/tests/FR016/TestSupport.cs'),
    (Join-Path $root 'NORM/NORM_Import.aspx.cs'),(Join-Path $root 'NORM/NORM_SourceHistory.aspx.cs'),(Join-Path $root 'NORM/NORM_Workspace.aspx.cs'),
    (Join-Path $root 'tools/tests/Increment1B2/PageHarness.cs'),(Join-Path $root 'tools/tests/Package1CStep1/PageHarness.cs'),
    (Join-Path $root 'tools/tests/Package1CStep1/Tests.cs'),(Join-Path $root 'tools/tests/Package1CStep2/Tests.cs'),
    (Join-Path $root 'tools/tests/Package1CStep3/Store.cs'),(Join-Path $root 'tools/tests/Package1CStep3/Tests.cs'),
    (Join-Path $PSScriptRoot 'HistorySupport.cs'),(Join-Path $PSScriptRoot 'PageHarness.cs'),(Join-Path $PSScriptRoot 'Tests.cs'),(Join-Path $PSScriptRoot 'NihracsTests.cs'))
& $compiler @options (Join-Path $root 'App_Code/NORMErrorHandling.cs')
if ($LASTEXITCODE -ne 0) { throw 'Package 1C Step 4 compilation failed' }
$testArgs = @($root)
if ($NihracsWorkbook) { $testArgs += (Resolve-Path -LiteralPath $NihracsWorkbook).Path }
if ($NihracsReplacementWorkbook) {
    if (!$NihracsWorkbook) { throw 'Supply the original NIHRACS workbook as well.' }
    $testArgs += (Resolve-Path -LiteralPath $NihracsReplacementWorkbook).Path
}
& (Join-Path $output 'Tests.exe') @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Package 1C Step 4 tests failed' }
Write-Output "Isolated artifacts: $output"
