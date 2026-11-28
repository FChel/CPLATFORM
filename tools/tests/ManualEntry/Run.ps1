[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$Timestamp,
    [string]$Playwright = "$env:USERPROFILE/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright"
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../../..").Path
$database = 'NORM_ManualEntryTest_' + [Guid]::NewGuid().ToString('N')
$output = Join-Path ([IO.Path]::GetTempPath()) $database
$hosted = Join-Path $output 'Hosted'
New-Item -ItemType Directory -Path "$hosted/bin" -Force | Out-Null
$master = [Data.SqlClient.SqlConnection]::new('Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;Connect Timeout=30;Pooling=false')
$fixture = $null
$created = $false

function Invoke-FixtureSql($connection, [string]$sql, [switch]$Scalar) {
    $command = $connection.CreateCommand()
    try {
        $command.CommandText = $sql
        if ($Scalar) { return $command.ExecuteScalar() }
        $null = $command.ExecuteNonQuery()
    } finally { $command.Dispose() }
}

function Get-ProtectedEvidence($connection) {
    $tables = @{
        ConfigurationRelease = 'ConfigurationReleaseId'; Import = 'ImportId'; TrialBalanceRow = 'TbRowId'
        CalculationRun = 'CalculationRunId'; LineResult = 'LineResultId'; ValidationResult = 'ValidationResultId'
        AccountMap = 'AccountMapId'; StatementLine = 'StatementLineId'; Lineage = 'LineageId'
        DisclosureRule = 'DisclosureRuleId'; ImportFile = 'ImportFileId'; ManualInput = 'ManualInputId'
        AdminUser = 'AdminUserId'; UserEntity = 'AdminUserId,EntityCode'; RunNarrative = 'RunNarrativeId'
    }
    $rows = @{}
    foreach ($table in $tables.Keys) {
        $rows[$table] = Invoke-FixtureSql $connection "SELECT (SELECT * FROM dbo.tblNORM_$table ORDER BY $($tables[$table]) FOR JSON PATH, INCLUDE_NULL_VALUES)" -Scalar
    }
    return $rows
}

try {
    $master.Open()
    if ((Invoke-FixtureSql $master "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'))" -Scalar) -ne 1) {
        throw 'This suite requires SQL Server LocalDB.'
    }
    Invoke-FixtureSql $master "CREATE DATABASE [$database];"
    $created = $true
    $fixture = [Data.SqlClient.SqlConnection]::new("Server=(localdb)\MSSQLLocalDB;Database=$database;Integrated Security=true;Connect Timeout=30;Pooling=false")
    $fixture.Open()
    foreach ($file in @("$root/sql/NORM_01_ProofEngine_Schema.sql", "$root/sql/NORM_Stage2B_Reported_Amounts.sql",
        "$PSScriptRoot/Fixture.sql", "$root/sql/NORM_04_GovernmentReportingPlatform.sql", "$root/sql/NORM_05_StatementDemoEnhancements.sql",
        "$root/sql/NORM_06_PreparationControlCentre.sql")) {
        $fixtureSql = (Get-Content -Raw -LiteralPath $file) -replace '(?im)^USE \[CPlatform\];\s*$', ''
        foreach ($batch in ($fixtureSql -split '(?im)^GO\s*$')) {
            if (![string]::IsNullOrWhiteSpace($batch)) { Invoke-FixtureSql $fixture $batch }
        }
    }
    Invoke-FixtureSql $fixture "INSERT dbo.tblNORM_ManualInput(CalculationRunId,InputCode,InputLabel,StatusCode,UpdatedBy) VALUES (2,'LEGACY_FIXTURE','Legacy disclosure fixture','Draft','fixture');"
    $before = Get-ProtectedEvidence $fixture
    $migration = Get-Content -Raw -LiteralPath "$root/sql/NORM_Stage3_I4_ManualEntry.sql"
    Invoke-FixtureSql $fixture $migration
    Invoke-FixtureSql $fixture $migration
    if ((Invoke-FixtureSql $fixture 'SELECT COUNT(*) FROM dbo.tblNORM_ManualSchedule' -Scalar) -ne 2) {
        throw 'Expected exactly two seeded definitions.'
    }
    if ((Invoke-FixtureSql $fixture 'SELECT COUNT(*) FROM dbo.tblNORM_ManualValueRevision' -Scalar) -ne 0) {
        throw 'Migration seeded annual input values.'
    }
    $localDb = (Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (!$localDb) {
        $localDb = (Get-ChildItem "$env:ProgramFiles/Microsoft SQL Server/*/Tools/Binn/SqlLocalDB.exe" -File |
            Sort-Object FullName -Descending | Select-Object -First 1).FullName
    }
    $pipe = [regex]::Match((& $localDb info MSSQLLocalDB | Out-String), '(?im)np:(\\[^\r\n]+)')
    if (!$pipe.Success) { throw 'Could not find the isolated LocalDB pipe.' }
    $connection = 'Provider=SQLOLEDB;Data Source=' + $pipe.Groups[1].Value.Trim() +
        ";Network Library=dbnmpntw;Initial Catalog=$database;Integrated Security=SSPI;OLE DB Services=-2;"
    $escaped = [Security.SecurityElement]::Escape($connection)
    Copy-Item "$root/App_Code" -Destination $hosted -Recurse
    Copy-Item "$root/bin/*.dll" -Destination "$hosted/bin"
    Copy-Item "$root/NORM/NORM_ManualEntry.aspx*","$root/NORM/NORM_Statements.aspx*" -Destination $hosted
    $exe = Join-Path $output 'ManualEntryHosted.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    & $compiler /nologo /target:exe /platform:x64 "/out:$exe" /r:System.Web.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'Hosted.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Hosted test compilation failed.' }
    Copy-Item -LiteralPath $exe -Destination "$hosted/bin"
    [IO.File]::WriteAllText("$hosted/web.config", @"
<configuration><appSettings><add key="NORM.ConnectionString" value="$escaped" /></appSettings><system.web>
<compilation targetFramework="4.8"/><httpRuntime targetFramework="4.8" requestValidationMode="4.5"/>
<sessionState cookieless="UseCookies"/><customErrors mode="Off"/>
</system.web></configuration>
"@)
    [IO.File]::WriteAllText("$hosted/Global.asax", @'
<%@ Application Language="C#" %>
<script runat="server">
void Application_PostAuthenticateRequest(object sender, EventArgs e) {
    Context.User = new System.Security.Principal.GenericPrincipal(
        new System.Security.Principal.GenericIdentity(Context.Request.Headers["X-NORM-Fixture-Identity"], "LocalDB fixture"), new string[0]);
}
</script>
'@)
    & node "$PSScriptRoot/Browser.cjs" $exe $hosted $Playwright $EvidenceDirectory $Timestamp
    if ($LASTEXITCODE -ne 0) {
        Invoke-FixtureSql $fixture "SELECT TOP 1 DetailText FROM dbo.tblNORM_AuditEvent WHERE EventCode='NORM_REQUEST_FAILED' ORDER BY AuditEventId DESC" -Scalar
        throw "Hosted Edge acceptance failed. Scratch application: $hosted"
    }

    $revisions = 'SELECT (SELECT * FROM dbo.tblNORM_ManualValueRevision ORDER BY RevisionId FOR JSON PATH, INCLUDE_NULL_VALUES)'
    $snapshots = 'SELECT (SELECT * FROM dbo.tblNORM_ManualScheduleEvidence ORDER BY EvidenceId FOR JSON PATH, INCLUDE_NULL_VALUES)'
    $savedValues = Invoke-FixtureSql $fixture $revisions -Scalar
    $savedEvidence = Invoke-FixtureSql $fixture $snapshots -Scalar
    Invoke-FixtureSql $fixture $migration
    if ($savedValues -cne (Invoke-FixtureSql $fixture $revisions -Scalar) -or
        $savedEvidence -cne (Invoke-FixtureSql $fixture $snapshots -Scalar)) { throw 'Reapplying migration changed annual evidence.' }
    $amendment = @'
SELECT COUNT(*) FROM dbo.tblNORM_ManualValueRevision r
JOIN dbo.tblNORM_ManualValueRevision p ON p.RevisionId=r.OriginalPriorRevisionId
WHERE r.ReportingYear=2026 AND r.IsComparative=1 AND r.Amount=241 AND r.Unit=N'A$''000'
AND r.InputPrecision=0 AND p.Amount=240 AND p.ReportingYear=2025
AND r.ChangeReason='Prior payroll correction' AND r.EnteredBy='Manual tester'
'@
    if ((Invoke-FixtureSql $fixture $amendment -Scalar) -ne 1) { throw 'Comparative audit chain missing.' }
    if ((Invoke-FixtureSql $fixture "SELECT COUNT(*) FROM dbo.tblNORM_ManualValueRevision WHERE ReportingYear=2027 OR EnteredBy<>'Manual tester'" -Scalar) -ne 0) {
        throw 'The rollover or denied request wrote a value.'
    }
    $after = Get-ProtectedEvidence $fixture
    foreach ($table in $before.Keys) {
        if ($before[$table] -cne $after[$table]) { throw "Protected data changed in $table." }
    }
    'PASS additive and repeatable migration, annual audit chain and unchanged source, run, mapping, result, access and narrative data.'
} finally {
    if ($fixture) { $fixture.Dispose() }
    if ($created) { Invoke-FixtureSql $master "DROP DATABASE [$database];" }
    $master.Dispose()
}
