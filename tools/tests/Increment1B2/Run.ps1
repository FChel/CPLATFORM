[CmdletBinding()]
param([string]$SamplePath = 'C:\Development\Transfer\Defence TB - ERP Format FY26.xlsx')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-increment1b2-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$epplus = Join-Path $root 'bin/EPPlus-LGPL.dll'
Copy-Item -LiteralPath $epplus -Destination $output
$code = [IO.File]::ReadAllText((Join-Path $root 'App_Code/NORMImportService.cs'))
[IO.File]::WriteAllText((Join-Path $output 'NORMImportService.cs'), $code.Replace('using System.Data.OleDb;', 'using TestDb;'))
$exe = Join-Path $output 'Tests.exe'
$options = @('/nologo','/warnaserror+','/define:SERVICE','/target:exe',"/out:$exe",'/r:System.Data.dll','/r:System.Web.dll',"/r:$epplus",
    (Join-Path $root 'App_Code/NORMCrypto.cs'),(Join-Path $root 'App_Code/NORMImportModels.cs'),
    (Join-Path $root 'App_Code/ErpTrialBalanceImporter.cs'),(Join-Path $root 'App_Code/RomanTrialBalanceImporter.cs'),
    (Join-Path $root 'tools/tests/FR016/TestSupport.cs'),(Join-Path $output 'NORMImportService.cs'),
    (Join-Path $root 'NORM/NORM_Import.aspx.cs'),(Join-Path $PSScriptRoot 'PageHarness.cs'),(Join-Path $PSScriptRoot 'Tests.cs'))
& $compiler @options
if ($LASTEXITCODE -ne 0) { throw 'B2 compilation failed' }
& $exe $SamplePath
if ($LASTEXITCODE -ne 0) { throw 'B2 tests failed' }
Write-Output "Isolated artifacts: $output"
