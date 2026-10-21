[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo ('.artifact-qa\pord-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('PORDModels.cs','PORDRules.cs','PORDChecks.cs','PORDHelper.cs','PORDStore.cs','PORDCsvParser.cs','PORDEmail.cs') | ForEach-Object { Join-Path $repo ('App_Code\' + $_) }
$exe = Join-Path $output 'PordRegression.exe'
& $compiler /nologo /langversion:5 /r:System.Web.dll /r:System.Configuration.dll "/out:$exe" @sources (Join-Path $PSScriptRoot 'tests\PordRegression.cs')
if ($LASTEXITCODE -ne 0) { throw 'POD regression test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'POD regression tests failed.' }
