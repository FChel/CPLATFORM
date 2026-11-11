[CmdletBinding()]
param([switch]$ReproductionOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output=Join-Path ([IO.Path]::GetTempPath()) ('norm-viewstate-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output|Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $output 'ViewStateTests.exe'
& $compiler /nologo /warnaserror+ /target:exe "/out:$exe" /r:System.Web.dll (Join-Path $PSScriptRoot 'ViewStateTests.cs')
if($LASTEXITCODE -ne 0){throw 'Session host compilation failed'}
foreach($variant in @('original','corrected')){
    $app=Join-Path $output $variant
    New-Item -ItemType Directory -Path "$app/App_Code","$app/bin" -Force|Out-Null
    Copy-Item -LiteralPath $exe -Destination "$app/bin"
    Copy-Item -Path "$root/bin/*.dll" -Destination "$app/bin"
    Copy-Item -LiteralPath "$root/NORM/NORM_YearSetup.aspx" -Destination "$app/NORM_YearSetup.aspx"
    $code=[IO.File]::ReadAllText("$root/NORM/NORM_YearSetup.aspx.cs")
    # Reconstruct the deployed pre-correction line for a before/after regression.
    if($variant -eq 'original'){$code=$code.Replace('            Session["NORM.YearSetup.ViewStateInitialized"] = true;' + "`r`n",'').Replace('            Session["NORM.YearSetup.ViewStateInitialized"] = true;' + "`n",'')}
    [IO.File]::WriteAllText("$app/NORM_YearSetup.aspx.cs",$code)
    $service=[IO.File]::ReadAllText("$root/App_Code/NORMStartOfYearSetup.cs").Replace('using System.Data.OleDb;','using TestDb;')
    [IO.File]::WriteAllText("$app/App_Code/NORMStartOfYearSetup.cs",$service)
    Copy-Item -LiteralPath "$root/App_Code/NORMCrypto.cs" -Destination "$app/App_Code"
    $support=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TestSupport.cs'))
    $support=$support.Replace("public static class NORMHelper`r`n{","public static class NORMHelper`r`n{`r`n    static NORMHelper() { Reset(); }").Replace("public static class NORMHelper`n{","public static class NORMHelper`n{`n    static NORMHelper() { Reset(); }")
    $support=[regex]::Replace($support,'public static string CurrentUserId\(\) \{ return .*?; \}','public static string CurrentUserId() { return System.Web.HttpContext.Current.Request.Headers["X-Isolated-User"] ?? "isolated-preparer"; }')
    [IO.File]::WriteAllText("$app/App_Code/TestSupport.cs",$support)
    [IO.File]::WriteAllText("$app/App_Code/BasePage.cs",'namespace CPlatform.NORM { public class NORMBasePage : System.Web.UI.Page { public string CurrentEnvironment { get {return "ISOLATED";} } public string CurrentUser { get {return NORMHelper.CurrentUserId();} } } }')
    [IO.File]::WriteAllText("$app/web.config",'<configuration><system.web><compilation debug="false" targetFramework="4.8"/><httpRuntime targetFramework="4.8"/><customErrors mode="Off"/><pages enableViewStateMac="true" enableEventValidation="true"/><sessionState mode="InProc" cookieless="UseCookies" timeout="30"/></system.web></configuration>')
}
if($ReproductionOnly){& $exe "$output/original"}else{& $exe "$output/original" "$output/corrected"}
if($LASTEXITCODE -ne 0){throw 'Real ASP.NET ViewState checks failed'}
Write-Output "Isolated artifacts: $output"
