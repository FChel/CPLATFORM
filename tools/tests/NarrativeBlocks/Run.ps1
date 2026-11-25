[CmdletBinding()]
param([string]$EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$database = 'NORM_NarrativeTest_' + [Guid]::NewGuid().ToString('N')
$output = Join-Path ([IO.Path]::GetTempPath()) $database
New-Item -ItemType Directory -Path $output | Out-Null
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

function Get-ProtectedRows($connection) {
    $keys = @{
        AdminUser = 'AdminUserId'; UserEntity = 'AdminUserId,EntityCode'
        ReportingEntity = 'ReportingEntityId'; ConfigurationRelease = 'ConfigurationReleaseId'
        StatementLine = 'StatementLineId'; DisclosureRule = 'DisclosureRuleId'
        Import = 'ImportId'; CalculationRun = 'CalculationRunId'; RunNarrative = 'RunNarrativeId'
    }
    $rows = @{}
    foreach ($table in $keys.Keys) {
        $rows[$table] = Invoke-FixtureSql $connection "SELECT (SELECT * FROM dbo.tblNORM_$table ORDER BY $($keys[$table]) FOR JSON PATH, INCLUDE_NULL_VALUES)" -Scalar
    }
    return $rows
}

function Assert-ProtectedRows($connection, $expected) {
    $actual = Get-ProtectedRows $connection
    foreach ($table in $expected.Keys) {
        if ($actual[$table] -cne $expected[$table]) { throw "Protected table tblNORM_$table changed." }
    }
}

try {
    $master.Open()
    if ((Invoke-FixtureSql $master "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'));" -Scalar) -ne 1) {
        throw 'This test requires the local LocalDB instance.'
    }
    Invoke-FixtureSql $master "CREATE DATABASE [$database];"
    $created = $true
    $fixture = [Data.SqlClient.SqlConnection]::new("Server=(localdb)\MSSQLLocalDB;Database=$database;Integrated Security=true;Connect Timeout=30;Pooling=false")
    $fixture.Open()
    Invoke-FixtureSql $fixture 'SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;'
    Invoke-FixtureSql $fixture (Get-Content -LiteralPath (Join-Path $root 'sql/NORM_01_ProofEngine_Schema.sql') -Raw)
    Invoke-FixtureSql $fixture @'
SET IDENTITY_INSERT dbo.tblNORM_ConfigurationRelease ON;
INSERT dbo.tblNORM_ConfigurationRelease (ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy)
VALUES (1,2025,'DEPT','v1.0','Baseline','Approved','fixture'),
       (19,2026,'NIHRACS','v1','FY2026 narrative source','Draft','fixture'),
       (21,2027,'NIHRACS','v1','FY2027 target','Draft','fixture'),
       (22,2026,'NIHRACS','v2','FY2026 successor','Draft','fixture'),
       (23,2027,'OTHER','v1','Other entity','Draft','fixture');
SET IDENTITY_INSERT dbo.tblNORM_ConfigurationRelease OFF;
INSERT dbo.tblNORM_ReportingEntity (FinancialYear,EntityCode,EntityName)
VALUES (2026,'NIHRACS','NIHRACS'),(2027,'NIHRACS','NIHRACS'),(2027,'OTHER','Other entity');
INSERT dbo.tblNORM_StatementLine (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,CalculationKind)
VALUES (19,2026,'NIHRACS','SOCI',1,'section','INCOME','Income','Heading'),
       (21,2027,'NIHRACS','SOCI',1,'section','INCOME','Income','Heading'),
       (22,2026,'NIHRACS','SOCI',1,'section','INCOME','Income','Heading');
CREATE TABLE dbo.tblNORM_UserEntity (
    AdminUserId INT NOT NULL REFERENCES dbo.tblNORM_AdminUser(AdminUserId),
    EntityCode VARCHAR(20) NOT NULL,
    CONSTRAINT PK_tblNORM_UserEntity PRIMARY KEY (AdminUserId,EntityCode)
);
INSERT dbo.tblNORM_AdminUser (UserId,DisplayName,RoleCode,CreatedBy)
VALUES ('Narrative tester','Narrative tester','Preparer','fixture');
DECLARE @preparer int = SCOPE_IDENTITY();
INSERT dbo.tblNORM_UserEntity (AdminUserId,EntityCode) VALUES (@preparer,'NIHRACS'),(@preparer,'OTHER');
INSERT dbo.tblNORM_AdminUser (UserId,DisplayName,RoleCode,CreatedBy)
VALUES ('Narrative administrator','Narrative administrator','Administrator','fixture');
INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy)
VALUES ('NORM_REQUEST_FAILED','NORMRequest','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','Operation=Request;Outcome=Failed;Method=Fixture confidential','fixture');
INSERT dbo.tblNORM_Import (ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,DataFingerprint,SourceFileBytes,StatusCode,ImportedBy)
VALUES (19,2026,'NIHRACS','ERP','Legacy test.xlsx',REPLICATE('1',64),REPLICATE('2',64),0x01,'Imported','fixture');
DECLARE @import int = SCOPE_IDENTITY();
INSERT dbo.tblNORM_CalculationRun (ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy)
VALUES (@import,19,REPLICATE('3',64),'Complete','fixture');
'@
    Invoke-FixtureSql $fixture (Get-Content -LiteralPath (Join-Path $root 'sql/NORM_04_GovernmentReportingPlatform.sql') -Raw)
    Invoke-FixtureSql $fixture @'
INSERT dbo.tblNORM_RunNarrative (CalculationRunId,DisclosureCode,NarrativeType,NarrativeText,StatusCode,UpdatedBy)
VALUES (1,'NI_IS_EXP_SUP','AccountingPolicy',N'Legacy plain text < 2026', 'Reviewed','fixture');
INSERT dbo.tblNORM_DisclosureRule (ConfigurationReleaseId,DisclosureCode,SectionCode,SectionTitle,NoteRef,
    DisclosureTitle,TriggerCode,IsBaseRequired,RequiresNarrative,SortOrder)
SELECT c.Id,n.Code,n.Section,n.SectionTitle,n.Note,n.Title,'ALWAYS',1,0,n.Ordering
FROM (VALUES (19),(21),(22)) c(Id)
CROSS JOIN (VALUES ('NI_IS_EXP_SUP','1','Financial Performance','1.1B','Supplier expenses',10),
                   ('NI_BS_LIA_IBL_LEA','2','Financial Position','2.4A','Leases',20),
                   ('LOCAL_ONLY','6','Local section','6.1','Local schedule',30))
    n(Code,Section,SectionTitle,Note,Title,Ordering)
WHERE c.Id<>21 OR n.Code<>'LOCAL_ONLY';
'@
    $protectedRows = Get-ProtectedRows $fixture
    $migration = Get-Content -LiteralPath (Join-Path $root 'sql/NORM_Stage3_I3_NarrativeBlocks.sql') -Raw
    Invoke-FixtureSql $fixture $migration
    Invoke-FixtureSql $fixture $migration
    Assert-ProtectedRows $fixture $protectedRows

    $localDb = (Get-Command SqlLocalDB.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (!$localDb) {
        $localDb = (Get-ChildItem (Join-Path $env:ProgramFiles 'Microsoft SQL Server/*/Tools/Binn/SqlLocalDB.exe') -File |
            Sort-Object FullName -Descending | Select-Object -First 1).FullName
    }
    if (!$localDb) { throw 'Could not find the SQL Server LocalDB command.' }
    $pipe = [regex]::Match((& $localDb info MSSQLLocalDB | Out-String), '(?im)np:(\\[^\r\n]+)')
    if (!$pipe.Success) { throw 'Could not resolve the local named pipe.' }
    $connection = 'Provider=SQLOLEDB;Data Source=' + $pipe.Groups[1].Value.Trim() +
        ";Network Library=dbnmpntw;Initial Catalog=$database;Integrated Security=SSPI;OLE DB Services=-2;"
    $exe = Join-Path $output 'NarrativeTests.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $sources = @('NORMHelper','NORMCrypto','StatementEngine','NORMNarrativeBlocks','NORMErrorHandling') | ForEach-Object { Join-Path $root "App_Code/$_.cs" }
    & $compiler /nologo /target:exe /platform:x64 "/out:$exe" /r:System.Configuration.dll /r:System.Data.dll /r:System.Data.DataSetExtensions.dll /r:System.Web.dll /r:System.Web.Extensions.dll @sources (Join-Path $PSScriptRoot 'Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Narrative LocalDB harness compilation failed.' }
    $escaped = [Security.SecurityElement]::Escape($connection)
    [IO.File]::WriteAllText("$exe.config", "<configuration><appSettings><add key='NORM.ConnectionString' value='$escaped'/></appSettings></configuration>")
    & $exe $output
    if ($LASTEXITCODE -ne 0) { throw 'Narrative LocalDB acceptance failed.' }
    $blocksSql = 'SELECT (SELECT * FROM dbo.tblNORM_NarrativeBlock ORDER BY NarrativeBlockId FOR JSON PATH, INCLUDE_NULL_VALUES)'
    $beforeRepeat = Invoke-FixtureSql $fixture $blocksSql -Scalar
    Invoke-FixtureSql $fixture $migration
    Assert-ProtectedRows $fixture $protectedRows
    if ($beforeRepeat -cne (Invoke-FixtureSql $fixture $blocksSql -Scalar)) {
        throw 'Reapplying the migration changed narrative content.'
    }
    'PASS additive SQL migration, repeat application, legacy narratives and protected reporting, source, calculation and access records'

    $hosted = Join-Path $output 'HostedReporting'
    New-Item -ItemType Directory -Path "$hosted/bin" | Out-Null
    Copy-Item (Join-Path $root 'App_Code') -Destination $hosted -Recurse
    Copy-Item (Join-Path $root 'bin/*.dll') -Destination "$hosted/bin"
    Copy-Item (Join-Path $root 'NORM/NORM_Reporting.aspx*') -Destination $hosted
    Copy-Item (Join-Path $root 'NORM/NORM_Statements.aspx*') -Destination $hosted
    Copy-Item (Join-Path $root 'NORM/NORM_AdminUsers.aspx*') -Destination $hosted
    # Inject failures into the isolated hosted copy only. SQL still commits or rolls back through production code.
    $storagePath = Join-Path $hosted 'App_Code/NORMNarrativeBlocks.cs'
    $commit = 'NORMErrorHandling.CommitNarrativeSave(System.Web.HttpContext.Current, transaction.Commit);'
    $storage = [IO.File]::ReadAllText($storagePath)
    if (!$storage.Contains($commit)) { throw 'The hosted fixture could not locate the production save commit.' }
    [IO.File]::WriteAllText($storagePath, $storage.Replace($commit, @'
var fixtureFault = System.Web.HttpContext.Current.Request.Headers["X-NORM-Fixture-Fault"];
if (fixtureFault == "before") throw new InvalidOperationException("Fixture confidential before commit.");
NORMErrorHandling.CommitNarrativeSave(System.Web.HttpContext.Current, delegate {
    transaction.Commit();
    if (fixtureFault == "uncertain") throw new InvalidOperationException("Fixture confidential acknowledgement loss.");
});
'@))
    $reportingPath = Join-Path $hosted 'NORM_Reporting.aspx.cs'
    $reporting = [IO.File]::ReadAllText($reportingPath)
    $successNotice = 'NarrativeNotice = "Narrative saved.";'
    if (!$reporting.Contains($successNotice)) { throw 'The hosted fixture could not locate the successful save notice.' }
    [IO.File]::WriteAllText($reportingPath, $reporting.Replace($successNotice, @'
if (Request.Headers["X-NORM-Fixture-Fault"] == "after") throw new InvalidOperationException("Fixture confidential response failure.");
NarrativeNotice = "Narrative saved.";
'@))
    $postbackExe = Join-Path $output 'HostedPostback.exe'
    & $compiler /nologo /target:exe /platform:x64 "/out:$postbackExe" /r:System.Web.dll (Join-Path $PSScriptRoot 'HostedPostback.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Hosted Reporting harness compilation failed.' }
    Copy-Item -LiteralPath $postbackExe -Destination "$hosted/bin"
    [IO.File]::WriteAllText("$hosted/web.config", @"
<configuration><appSettings><add key="NORM.ConnectionString" value="$escaped"/></appSettings><system.web>
<compilation targetFramework="4.8"/><httpRuntime targetFramework="4.8" requestValidationMode="4.5"/>
<sessionState cookieless="UseCookies"/><customErrors mode="Off"/>
</system.web></configuration>
"@)
    [IO.File]::WriteAllText("$hosted/Global.asax", @'
<%@ Application Language="C#" %>
<script runat="server">
void Application_PostAuthenticateRequest(object sender, EventArgs e) {
    string identity = Context.Request.Headers["X-NORM-Fixture-Admin"] == "1" ? "Narrative administrator" : "Narrative tester";
    Context.User = new System.Security.Principal.GenericPrincipal(
        new System.Security.Principal.GenericIdentity(identity, "LocalDB fixture"), new string[0]);
}
</script>
'@)
    & $postbackExe $hosted
    if ($LASTEXITCODE -ne 0) {
        Invoke-FixtureSql $fixture "SELECT TOP 1 DetailText FROM dbo.tblNORM_AuditEvent WHERE EventCode='NORM_REQUEST_FAILED' ORDER BY AuditEventId DESC" -Scalar
        throw 'Hosted Reporting rich-text postback failed.'
    }
    $savedSql = "SELECT COUNT(*) FROM dbo.tblNORM_NarrativeBlock WHERE ConfigurationReleaseId=19 AND SequenceNo=9 AND ContentHtml=N'<p>Hosted <strong>policy</strong> wording.</p>'"
    if ((Invoke-FixtureSql $fixture $savedSql -Scalar) -ne 1 -or
        (Invoke-FixtureSql $fixture "SELECT COUNT(*) FROM dbo.tblNORM_NarrativeBlock WHERE ConfigurationReleaseId=19 AND SequenceNo=9" -Scalar) -ne 1) {
        throw 'Only one sanitised narrative can be persisted by the hosted postback fixture.'
    }
    $result = Invoke-FixtureSql $fixture "SELECT STRING_AGG(CONVERT(varchar(3),SequenceNo),',') WITHIN GROUP (ORDER BY SequenceNo) FROM dbo.tblNORM_NarrativeBlock WHERE ConfigurationReleaseId=19 AND DestinationKey='NI_IS_EXP_SUP' AND SequenceNo BETWEEN 6 AND 9" -Scalar
    if ($result -cne '7,8,9') { throw 'Hosted save persistence disagreed with the injected transaction outcomes.' }
    Assert-ProtectedRows $fixture $protectedRows

    if ($EvidenceDirectory) {
        Copy-Item -LiteralPath (Join-Path $output 'narratives.json') -Destination $EvidenceDirectory
    }
    "Fixture artifacts: $output"
} finally {
    if ($fixture) { $fixture.Dispose() }
    if ($created) { Invoke-FixtureSql $master "DROP DATABASE [$database];" }
    $master.Dispose()
}
