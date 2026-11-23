# Executes the candidate's actual SQL against a disposable SQL Server LocalDB database.
$ErrorActionPreference = 'Stop'
$sqlRoot = Join-Path $PSScriptRoot '../../../sql'
$database = 'NORM_SeedTest_' + [Guid]::NewGuid().ToString('N')
$master = [Data.SqlClient.SqlConnection]::new('Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;Connect Timeout=30;Pooling=false')
$fixture = $null
$created = $false

function Invoke-SeedTestSql($connection, [string]$sql, [switch]$Scalar) {
    $command = $connection.CreateCommand()
    try {
        $command.CommandTimeout = 60
        $command.CommandText = $sql
        if ($Scalar) { return $command.ExecuteScalar() }
        $null = $command.ExecuteNonQuery()
    } finally { $command.Dispose() }
}

function Assert-Rejected([string]$seed, [string]$id, [int]$number, [string]$message) {
    try {
        Invoke-SeedTestSql $fixture ($seed.Replace('DECLARE @ReleaseId int = NULL;', "DECLARE @ReleaseId int = $id;"))
    } catch {
        $errorDetail = $_.Exception.GetBaseException()
        if ($errorDetail -is [Data.SqlClient.SqlException] -and $errorDetail.Number -eq $number -and
            $errorDetail.Message.Contains($message)) {
            Invoke-SeedTestSql $fixture 'IF @@TRANCOUNT <> 0 ROLLBACK;'
            return
        }
        throw
    }
    throw "Seed unexpectedly accepted release $id."
}

try {
    $master.Open()
    if ((Invoke-SeedTestSql $master "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'));" -Scalar) -ne 1) {
        throw 'This test requires SQL Server LocalDB.'
    }
    Invoke-SeedTestSql $master "CREATE DATABASE [$database];"
    $created = $true
    $fixture = [Data.SqlClient.SqlConnection]::new("Server=(localdb)\MSSQLLocalDB;Database=$database;Integrated Security=true;Connect Timeout=30;Pooling=false")
    $fixture.Open()
    Invoke-SeedTestSql $fixture 'SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;'
    Invoke-SeedTestSql $fixture (Get-Content -LiteralPath (Join-Path $sqlRoot 'NORM_01_ProofEngine_Schema.sql') -Raw)
    Invoke-SeedTestSql $fixture @'
SET IDENTITY_INSERT dbo.tblNORM_ConfigurationRelease ON;
INSERT dbo.tblNORM_ConfigurationRelease
 (ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy)
VALUES (1,2025,'DEPT','v1.0','Reporting baseline','Approved','LocalDB fixture'),
       (10,2026,'NIHRACS','protected','Protected Draft 10','Draft','LocalDB fixture'),
       (11,2026,'NIHRACS','eligible','Separate eligible draft','Draft','LocalDB fixture'),
       (12,2026,'DEPT','wrong-entity','Wrong entity','Draft','LocalDB fixture'),
       (13,2025,'NIHRACS','wrong-year','Wrong year','Draft','LocalDB fixture'),
       (14,2026,'NIHRACS','approved','Approved release','Approved','LocalDB fixture');
SET IDENTITY_INSERT dbo.tblNORM_ConfigurationRelease OFF;
'@
    foreach ($file in @('NORM_04_GovernmentReportingPlatform.sql','NORM_21_MappingManagement.sql','NORM_Stage2B_Reported_Amounts.sql')) {
        Invoke-SeedTestSql $fixture (Get-Content -LiteralPath (Join-Path $sqlRoot $file) -Raw)
    }
    $migration = Get-Content -LiteralPath (Join-Path $sqlRoot 'NORM_Stage2B_NIHRACS_Hierarchy.sql') -Raw
    $batches = $migration -split '(?im)^[ \t]*GO[ \t]*\r?$'
    if ($batches.Count -ne 2 -or !$batches[1].Contains('DECLARE @ReleaseId int = NULL;')) { throw 'Unexpected migration batch format.' }
    Invoke-SeedTestSql $fixture $batches[0]
    $seed = $batches[1]
    $before = Invoke-SeedTestSql $fixture 'SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId=10 FOR JSON PATH, INCLUDE_NULL_VALUES;' -Scalar
    Assert-Rejected $seed 'NULL' 51011 'Supply the separate NIHRACS FY2026 draft release ID explicitly'
    Assert-Rejected $seed '10' 51010 'Draft 10 is protected'
    foreach ($ineligible in @('12','13','14')) { Assert-Rejected $seed $ineligible 51000 'Select an active NIHRACS FY2026 draft' }
    Invoke-SeedTestSql $fixture ($seed.Replace('DECLARE @ReleaseId int = NULL;', 'DECLARE @ReleaseId int = 11;'))
    Assert-Rejected $seed '11' 51000 'requires an empty draft'
    Invoke-SeedTestSql $fixture @'
IF (SELECT COUNT(*) FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=11) <> 122
   OR (SELECT COUNT(*) FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=11
       AND LineType='hierarchy-detail' AND CalculationKind='Mapped' AND IsDeactivated=0) <> 60
   OR (SELECT COUNT(*) FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=11 AND NoteSourceLineCode IS NOT NULL) <> 24
   OR (SELECT COUNT(*) FROM dbo.tblNORM_AuditEvent WHERE EventCode='NIHRACS_HIERARCHY_CONFIGURED' AND EntityId='11') <> 1
    THROW 51001, 'The eligible draft did not receive its catalogue, note links and audit.', 1;
IF EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=10)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=10)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_AccountMap)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_Import)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_TrialBalanceRow)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_AuditEvent WHERE EventCode='NIHRACS_HIERARCHY_CONFIGURED' AND EntityId='10')
    THROW 51001, 'The seed wrote outside the intended catalogue.', 1;
'@
    $after = Invoke-SeedTestSql $fixture 'SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId=10 FOR JSON PATH, INCLUDE_NULL_VALUES;' -Scalar
    if ($before -cne $after) { throw 'Draft 10 changed.' }
    Write-Output 'PASS LocalDB: missing ID and Draft 10 rejected, ineligible drafts rejected, separate empty draft seeded, Draft 10 unchanged.'
} finally {
    if ($fixture) { $fixture.Dispose() }
    if ($created) { Invoke-SeedTestSql $master "DROP DATABASE [$database];" }
    $master.Dispose()
}
