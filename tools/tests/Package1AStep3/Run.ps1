[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path ([IO.Path]::GetTempPath()) ('norm-step3-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
# Database boundary only: compile actual production service and page handlers.
$code = [IO.File]::ReadAllText((Join-Path $root 'App_Code/NORMStartOfYearSetup.cs'))
[IO.File]::WriteAllText((Join-Path $output 'NORMStartOfYearSetup.cs'), $code.Replace('using System.Data.OleDb;', 'using TestDb;'))
$options = @('/nologo','/warnaserror+','/target:exe',("/out:$output/Tests.exe"),
    '/r:System.Data.dll','/r:System.Web.dll','/r:System.Xml.Linq.dll')
foreach ($dll in (Get-ChildItem (Join-Path $root 'bin') -Filter '*.dll')) {
    Copy-Item -LiteralPath $dll.FullName -Destination $output
    $options += '/r:' + $dll.FullName
}
$options += @((Join-Path $root 'App_Code/NORMCrypto.cs'),(Join-Path $output 'NORMStartOfYearSetup.cs'),
    (Join-Path $root 'App_Code/NORMYearSetupDocumentHandler.cs'),(Join-Path $root 'NORM/NORM_YearSetup.aspx.cs'),
    (Join-Path $PSScriptRoot 'TestSupport.cs'),(Join-Path $PSScriptRoot 'PageHarness.cs'),(Join-Path $PSScriptRoot 'Tests.cs'))
& $compiler @options
if ($LASTEXITCODE -ne 0) { throw 'Step 3 compilation failed' }
& (Join-Path $output 'Tests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Step 3 tests failed' }
$engine = [IO.File]::ReadAllText((Join-Path $root 'App_Code/StatementEngine.cs'))
[IO.File]::WriteAllText((Join-Path $output 'StatementEngine.cs'), $engine.Replace('using System.Data.OleDb;', 'using TestDb;'))
& $compiler /nologo /warnaserror+ /target:exe "/out:$output/EngineContext.exe" /r:System.Data.dll `
    (Join-Path $output 'StatementEngine.cs') (Join-Path $root 'App_Code/NORMCrypto.cs') `
    (Join-Path $root 'tools/tests/FR016/TestSupport.cs') (Join-Path $PSScriptRoot 'EngineContextTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Engine context compilation failed' }
& (Join-Path $output 'EngineContext.exe')
if ($LASTEXITCODE -ne 0) { throw 'Engine context tests failed' }
Write-Output "Isolated artifacts: $output"
& node (Join-Path $PSScriptRoot 'UploadFeedbackTests.js')
if ($LASTEXITCODE -ne 0) { throw 'Upload feedback tests failed' }
