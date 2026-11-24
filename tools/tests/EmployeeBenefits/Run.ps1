[CmdletBinding()]
param([switch]$Nihracs, [string]$ConsolidatedWorkbook)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-employee-benefits-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$epplus = Join-Path $root 'bin/EPPlus-LGPL.dll'
Copy-Item -LiteralPath $epplus -Destination $output
$options = @('/nologo', '/target:exe', '/define:EMPLOYEE_BENEFITS_TESTS', "/out:$output/Tests.exe",
    '/r:System.Data.dll', '/r:System.Data.DataSetExtensions.dll', '/r:System.Drawing.dll', '/r:System.Web.dll', '/r:System.Web.Extensions.dll', "/r:$epplus")
foreach ($name in @('StatementEngine','NORMReportingFramework','NORMStatementEnhancements','NORMAdministeredStatements',
    'NORMDisclosureCompleteness','NORMNihracsDisclosureCatalogue','NORMReviewPack',
    'NORMWordExport','NORMFinancialStatementsExcel','NORMImportModels','NORMCrypto','NORMErrorHandling',
    'NORMMappingManagement','NORMAnnualSourceConfiguration','NihracsTrialBalanceImporter')) {
    $source = [IO.File]::ReadAllText((Join-Path $root "App_Code/$name.cs"))
    $target = Join-Path $output "$name.cs"
    [IO.File]::WriteAllText($target, $source.Replace('using System.Data.OleDb;', 'using TestDb;'))
    $options += $target
}
$options += @((Join-Path $root 'NORM/NORM_Statements.aspx.cs'), (Join-Path $root 'NORM/NORM_Reporting.aspx.cs'),
    (Join-Path $root 'tools/tests/FR016/TestSupport.cs'), (Join-Path $PSScriptRoot 'PilotTests.cs'),
    (Join-Path $PSScriptRoot 'NihracsHierarchyTests.cs'), (Join-Path $PSScriptRoot 'DisclosureTests.cs'))
& $compiler @options
if ($LASTEXITCODE -ne 0) { throw 'Employee benefits harness compilation failed' }
if ($Nihracs) {
    $arguments = @($output, $root)
    if ($ConsolidatedWorkbook) { $arguments += (Resolve-Path -LiteralPath $ConsolidatedWorkbook).Path }
    & (Join-Path $output 'Tests.exe') @arguments
    if ($LASTEXITCODE -ne 0) { throw 'NIHRACS hierarchy verification failed' }
    & node (Join-Path $PSScriptRoot 'PresentationTests.js') (Join-Path $root 'js/norm.js') $output --nihracs
    if ($LASTEXITCODE -ne 0) { throw 'NIHRACS web and print checks failed' }
    Write-Output "Isolated artifacts: $output"
    return
}
& (Join-Path $output 'Tests.exe') $output
if ($LASTEXITCODE -ne 0) { throw 'Employee benefits calculations or export checks failed' }
& node (Join-Path $PSScriptRoot 'PresentationTests.js') (Join-Path $root 'js/norm.js') $output
if ($LASTEXITCODE -ne 0) { throw 'Employee benefits web or print checks failed' }
& node (Join-Path $PSScriptRoot 'PresentationTests.js') (Join-Path $root 'js/norm.js') (Join-Path $output 'negative') 1 '(2)'
if ($LASTEXITCODE -ne 0) { throw 'Negative Employee benefits web or print checks failed' }
Write-Output "Isolated artifacts: $output"
