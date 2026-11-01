[CmdletBinding()]
param([string]$SamplePath = 'C:\Development\Transfer\Defence TB - ERP Format FY26.xlsx')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-increment1a-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$epplus = Join-Path $root 'bin/EPPlus-LGPL.dll'
Copy-Item -LiteralPath $epplus -Destination $output
# Reuse the FR016 substitutes; no real helper, connection or configuration is loaded.
$service = [IO.File]::ReadAllText((Join-Path $root 'App_Code/NORMImportService.cs'))
[IO.File]::WriteAllText((Join-Path $output 'NORMImportService.cs'), $service.Replace('using System.Data.OleDb;', 'using TestDb;'))
$options = @('/nologo', '/warnaserror+', '/define:SERVICE', '/r:System.Data.dll', '/target:exe', "/out:$output/Tests.exe", "/r:$epplus",
    (Join-Path $root 'App_Code/NORMCrypto.cs'), (Join-Path $root 'App_Code/NORMImportModels.cs'),
    (Join-Path $root 'App_Code/ErpTrialBalanceImporter.cs'), (Join-Path $root 'App_Code/RomanTrialBalanceImporter.cs'),
    (Join-Path $root 'tools/tests/FR016/TestSupport.cs'), (Join-Path $output 'NORMImportService.cs'), (Join-Path $PSScriptRoot 'Tests.cs'))
& $compiler @options
if ($LASTEXITCODE -ne 0) { throw 'Increment 1A compilation failed' }
& (Join-Path $output 'Tests.exe') $SamplePath
if ($LASTEXITCODE -ne 0) { throw 'Increment 1A parsing tests failed' }
Write-Output "Isolated artifacts: $output"
