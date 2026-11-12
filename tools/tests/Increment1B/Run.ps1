[CmdletBinding()]
param([string]$SamplePath = 'C:\Development\Transfer\Defence TB - ERP Format FY26.xlsx')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-increment1b-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$epplus = Join-Path $root 'bin/EPPlus-LGPL.dll'
Copy-Item -LiteralPath $epplus -Destination $output
foreach ($name in @('NORMImportService','StatementEngine')) {
    $code = [IO.File]::ReadAllText((Join-Path $root "App_Code/$name.cs"))
    [IO.File]::WriteAllText((Join-Path $output "$name.cs"), $code.Replace('using System.Data.OleDb;', 'using TestDb;'))
}
$common = @('/nologo','/warnaserror+','/target:exe','/r:System.Data.dll',"/r:$epplus",
    (Join-Path $root 'App_Code/NORMCrypto.cs'),(Join-Path $root 'App_Code/NORMImportModels.cs'),
    (Join-Path $root 'App_Code/ErpTrialBalanceImporter.cs'))
foreach ($suite in @('Service','Engine','Display')) {
    $exe = Join-Path $output "$suite.exe"
    $options = @("/out:$exe") + @($common)
    if ($suite -eq 'Service') {
        $options += @('/define:SERVICE',(Join-Path $root 'App_Code/RomanTrialBalanceImporter.cs'), (Join-Path $root 'App_Code/NORMTextTrialBalanceImporter.cs'),
            (Join-Path $root 'tools/tests/FR016/TestSupport.cs'),(Join-Path $output 'NORMImportService.cs'),(Join-Path $PSScriptRoot 'Tests.cs'))
    } elseif ($suite -eq 'Engine') {
        $options += @((Join-Path $root 'tools/tests/FR016/TestSupport.cs'),(Join-Path $output 'StatementEngine.cs'),(Join-Path $PSScriptRoot 'EngineTests.cs'))
    } else {
        $options += @('/r:System.Web.dll','/r:System.Configuration.dll',(Join-Path $root 'App_Code/NORMHelper.cs'),(Join-Path $PSScriptRoot 'DisplayTests.cs'))
        [IO.File]::WriteAllText("$exe.config", '<configuration><appSettings><add key="NORM.SapBaseUrl" value="https://sap.example.invalid"/></appSettings></configuration>')
    }
    & $compiler @options
    if ($LASTEXITCODE -ne 0) { throw "$suite compilation failed" }
    & $exe $SamplePath
    if ($LASTEXITCODE -ne 0) { throw "$suite tests failed" }
}
Write-Output "Isolated artifacts: $output"
