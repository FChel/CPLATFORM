[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-fr016-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$epplus = Join-Path $root 'bin/EPPlus-LGPL.dll'
Copy-Item -LiteralPath $epplus -Destination $output
foreach ($name in @('NORMImportService', 'StatementEngine')) {
    # Only substitute the database namespace in temporary copies. Business code is unchanged.
    $code = [IO.File]::ReadAllText((Join-Path $root "App_Code/$name.cs"))
    [IO.File]::WriteAllText((Join-Path $output "$name.cs"), $code.Replace('using System.Data.OleDb;', 'using TestDb;'))
}
$common = @('/nologo', '/warnaserror+', '/r:System.Data.dll', "/r:$epplus",
    (Join-Path $root 'App_Code/NORMCrypto.cs'), (Join-Path $root 'App_Code/NORMImportModels.cs'),
    (Join-Path $root 'App_Code/ErpTrialBalanceImporter.cs'), (Join-Path $root 'App_Code/RomanTrialBalanceImporter.cs'),
    (Join-Path $PSScriptRoot 'TestSupport.cs'), (Join-Path $PSScriptRoot 'Tests.cs'))
foreach ($suite in @('Service', 'Engine')) {
    $exe = Join-Path $output "$suite.exe"
    $source = if ($suite -eq 'Service') { 'NORMImportService.cs' } else { 'StatementEngine.cs' }
    $options = @('/target:exe', "/out:$exe") + @($common) + @((Join-Path $output $source))
    if ($suite -eq 'Service') { $options += '/define:SERVICE' }
    & $compiler @options
    if ($LASTEXITCODE -ne 0) { throw "$suite compilation failed" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "$suite tests failed" }
}
# Compile the unmodified production files against the real helper and OleDb APIs.
$production = @('/nologo', '/warnaserror+', '/target:library', ('/out:' + (Join-Path $output 'Production.dll')),
    '/r:System.Data.dll', '/r:System.Web.dll', '/r:System.Configuration.dll', "/r:$epplus")
foreach ($name in @('NORMHelper', 'NORMCrypto', 'NORMImportModels', 'ErpTrialBalanceImporter', 'RomanTrialBalanceImporter', 'NORMImportService', 'StatementEngine')) {
    $production += Join-Path $root "App_Code/$name.cs"
}
& $compiler @production
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed' }
Write-Output 'PASS production import and statement-engine compilation (.NET Framework compiler, real OleDb/helper)'
. (Join-Path $root 'tools/deploy/Waratah.Common.ps1')
$site = Join-Path $output 'site'
New-Item -ItemType Directory -Path $site | Out-Null
# Apply the existing application allowlist to working-tree files, without packaging or publishing.
$tracked = & git -C $root ls-files
if ($LASTEXITCODE -ne 0) { throw 'Cannot list application files' }
foreach ($relative in $tracked) {
    if (Test-ApplicationPath $relative) {
        $target = Join-Path $site $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
    }
}
# Build-only config in the temporary copy; no real config, UDL, uploads, SQL or evidence copied.
[IO.File]::WriteAllText((Join-Path $site 'web.config'), '<configuration><system.web><compilation debug="false" targetFramework="4.8"/><httpRuntime targetFramework="4.8"/></system.web></configuration>')
$webCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/aspnet_compiler.exe'
& $webCompiler -v / -p $site (Join-Path $output 'compiled')
if ($LASTEXITCODE -ne 0) { throw 'Full ASP.NET compilation failed' }
Write-Output 'PASS full ASP.NET site compilation (isolated working-tree copy, build-only config)'
Write-Output "Isolated artifacts: $output"
