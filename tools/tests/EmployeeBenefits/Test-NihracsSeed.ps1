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

function Read-SqlConstant([string]$source, [string]$name, [hashtable]$references = @{}) {
    $expression = [regex]::Match($source, '\bconst string ' + $name + '\s*=\s*([^;]+);')
    if (!$expression.Success) { throw "Could not find the production constant $name." }
    $parts = foreach ($part in ($expression.Groups[1].Value.Trim() -split '\s+\+\s+')) {
        if ($references.ContainsKey($part)) { $references[$part] }
        elseif ($part -match '^"[^"]*"$') { ConvertFrom-Json -InputObject $part }
        else { throw "Unexpected component in production constant $name." }
    }
    $parts -join ''
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
       (14,2026,'NIHRACS','approved','Approved release','Approved','LocalDB fixture'),
       (18,2026,'UAT_EMP_PILOT','pilot','Employee Benefits UAT','Approved','LocalDB fixture'),
       (19,2026,'NIHRACS','retained-draft','Existing hierarchy draft','Draft','LocalDB fixture');
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
    Invoke-SeedTestSql $fixture ($seed.Replace('DECLARE @ReleaseId int = NULL;', 'DECLARE @ReleaseId int = 19;'))
    Invoke-SeedTestSql $fixture @'
INSERT dbo.tblNORM_AccountMap (ConfigurationReleaseId,FinancialYear,EntityCode,GlCode,StatementLine)
VALUES (10,2026,'NIHRACS','10001',NULL), (18,2026,'UAT_EMP_PILOT','10001','EMP_WAGES');
SET IDENTITY_INSERT dbo.tblNORM_Import ON;
INSERT dbo.tblNORM_Import (ImportId,ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,DataFingerprint,SourceFileBytes,StatusCode,ImportedBy)
SELECT v.ImportId,v.ReleaseId,v.FinancialYear,v.EntityCode,'ERP','fixture.xlsx',REPLICATE('1',64),
    REPLICATE('0',62)+CONVERT(varchar(2),v.ImportId),1,v.StatusCode,'LocalDB fixture'
FROM (VALUES (23,10,2024,'NIHRACS','Imported'),(24,10,2025,'NIHRACS','Imported'),(25,10,2026,'NIHRACS','Imported'),
    (31,18,2026,'UAT_EMP_PILOT','Calculated')) v(ImportId,ReleaseId,FinancialYear,EntityCode,StatusCode);
SET IDENTITY_INSERT dbo.tblNORM_Import OFF;
INSERT dbo.tblNORM_ImportFile (ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,[RowCount],FileContent)
SELECT ImportId,'ERP','fixture.xlsx',REPLICATE('1',64),1,1,0x01 FROM dbo.tblNORM_Import;
INSERT dbo.tblNORM_TrialBalanceRow (ImportId,SourceRowNo,SourceLedger,GlAccount,AccumBalance,RowHash)
SELECT ImportId,1,'0L','10001',100,REPLICATE('2',64) FROM dbo.tblNORM_Import;
SET IDENTITY_INSERT dbo.tblNORM_CalculationRun ON;
INSERT dbo.tblNORM_CalculationRun (CalculationRunId,ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy)
VALUES (28,31,18,REPLICATE('3',64),'Complete','LocalDB fixture');
SET IDENTITY_INSERT dbo.tblNORM_CalculationRun OFF;
INSERT dbo.tblNORM_LineResult (CalculationRunId,StatementCode,LineCode,ComputedAmount,StatusCode,ReportedAmount)
VALUES (28,'SOCI','Employee benefits',2.5,'Mapped',4);
'@
    $protectedQuery = @'
SELECT (SELECT
    (SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId IN (10,18,19) ORDER BY ConfigurationReleaseId FOR JSON PATH, INCLUDE_NULL_VALUES) AS Releases,
    (SELECT StatementLineId,LineCode,LineLabel,ParentLineCode FROM dbo.tblNORM_StatementLine
        WHERE ConfigurationReleaseId=19 ORDER BY StatementLineId FOR JSON PATH, INCLUDE_NULL_VALUES) AS Draft19Catalogue,
    (SELECT * FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=19 ORDER BY DisclosureRuleId FOR JSON PATH, INCLUDE_NULL_VALUES) AS Draft19Notes,
    (SELECT * FROM dbo.tblNORM_AccountMap ORDER BY AccountMapId FOR JSON PATH, INCLUDE_NULL_VALUES) AS Mappings,
    (SELECT * FROM dbo.tblNORM_Import ORDER BY ImportId FOR JSON PATH, INCLUDE_NULL_VALUES) AS Imports,
    (SELECT * FROM dbo.tblNORM_ImportFile ORDER BY ImportFileId FOR JSON PATH, INCLUDE_NULL_VALUES) AS Originals,
    (SELECT * FROM dbo.tblNORM_TrialBalanceRow ORDER BY TbRowId FOR JSON PATH, INCLUDE_NULL_VALUES) AS RetainedRows,
    (SELECT * FROM dbo.tblNORM_CalculationRun ORDER BY CalculationRunId FOR JSON PATH, INCLUDE_NULL_VALUES) AS PilotRuns,
    (SELECT * FROM dbo.tblNORM_LineResult ORDER BY LineResultId FOR JSON PATH, INCLUDE_NULL_VALUES) AS PilotResults
FOR JSON PATH, INCLUDE_NULL_VALUES);
'@
    $protected = Invoke-SeedTestSql $fixture $protectedQuery -Scalar
    $labelQuery = 'SELECT (SELECT StatementLineId,LineCode,LineLabel FROM dbo.tblNORM_StatementLine ORDER BY StatementLineId FOR JSON PATH, INCLUDE_NULL_VALUES);'
    $labels = Invoke-SeedTestSql $fixture $labelQuery -Scalar
    $descriptions = Get-Content -LiteralPath (Join-Path $sqlRoot 'NORM_Stage2B_Mapping_Descriptions.sql') -Raw
    Invoke-SeedTestSql $fixture $descriptions
    Invoke-SeedTestSql $fixture $descriptions
    if ($labels -cne (Invoke-SeedTestSql $fixture $labelQuery -Scalar) -or
        (Invoke-SeedTestSql $fixture "SELECT COL_LENGTH('dbo.tblNORM_StatementLine','MappingDescription');" -Scalar) -ne 480 -or
        (Invoke-SeedTestSql $fixture 'SELECT COUNT(*) FROM dbo.tblNORM_StatementLine WHERE MappingDescription IS NOT NULL;' -Scalar) -ne 0) {
        throw 'Mapping description migration changed existing labels or did not add nullable storage.'
    }
    Write-Output 'PASS LocalDB: repeatable MappingDescription migration, existing keys and labels preserved.'
    $cashFlow = Get-Content -LiteralPath (Join-Path $sqlRoot 'NORM_Stage2B_NIHRACS_Cash_Flow_References.sql') -Raw
    Invoke-SeedTestSql $fixture ($cashFlow.Replace('DECLARE @ReleaseId int = NULL;', 'DECLARE @ReleaseId int = 11;'))
    Invoke-SeedTestSql $fixture @'
IF (SELECT COUNT(*) FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=11 AND StatementCode='SCFREF') <> 19
   OR (SELECT COUNT(*) FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=11 AND StatementCode='SCFREF'
       AND LineType='cashflow-detail' AND CalculationKind='Heading' AND MappingDescription IS NOT NULL) <> 10
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine s WHERE s.ConfigurationReleaseId=11 AND s.StatementCode='SCFREF'
       AND s.ParentLineCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine p
           WHERE p.ConfigurationReleaseId=11 AND p.StatementCode='SCFREF' AND p.LineCode=s.ParentLineCode))
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE StatementCode='SCFREF'
       AND (ConfigurationReleaseId<>11 OR LineCode='CF_OPA_USE_SUP1'))
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=11)
    THROW 51001, 'Cash-flow references, draft isolation or source exception did not survive seeding.', 1;
'@
    Write-Output 'PASS LocalDB: ten cash-flow details and nine totals, draft-only configuration, and no GL writes.'
    if ($protected -cne (Invoke-SeedTestSql $fixture $protectedQuery -Scalar)) { throw 'The migration or seed changed protected fixture data.' }
    Write-Output 'PASS LocalDB: Drafts 10 and 19, retained Imports 23–25 with source bytes and rows, and Employee Benefits UAT fixture fingerprints unchanged.'

    $appCode = Join-Path $PSScriptRoot '../../../App_Code'
    $engine = Get-Content -LiteralPath (Join-Path $appCode 'StatementEngine.cs') -Raw
    $helper = Get-Content -LiteralPath (Join-Path $appCode 'NORMHelper.cs') -Raw
    $marker = Read-SqlConstant $engine 'DraftReviewCheckCode'
    $revision = Read-SqlConstant $engine 'DraftRevisionSql'
    $references = @{'NORMStatementEngine.DraftReviewCheckCode'=$marker; 'NORMStatementEngine.DraftRevisionSql'=$revision}
    $standard = Read-SqlConstant $helper 'StandardRunPredicate' $references
    $current = Read-SqlConstant $helper 'CurrentDraftReviewPredicate' $references
    $reviewSql = @'
BEGIN TRANSACTION;
INSERT dbo.tblNORM_CalculationRun (ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy)
VALUES (25,11,REPLICATE('4',64),'Complete','LocalDB fixture');
DECLARE @review int = SCOPE_IDENTITY();
INSERT dbo.tblNORM_ValidationResult (CalculationRunId,CheckCode,CheckLabel,SeverityCode,ResultCode,DetailText)
SELECT @review,'__MARKER__','Draft mapping review','Blocking','Fail',CONVERT(nvarchar(20),__REVISION__)
FROM dbo.tblNORM_ConfigurationRelease c WHERE c.ConfigurationReleaseId=11;

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_CalculationRun r WHERE r.CalculationRunId=28 AND (__STANDARD__))
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_CalculationRun r WHERE r.CalculationRunId=@review AND (__STANDARD__))
   OR NOT EXISTS (SELECT 1 FROM dbo.tblNORM_CalculationRun r WHERE r.CalculationRunId=@review AND (__CURRENT__))
    THROW 51001, 'Approved run and current draft review were not kept separate.', 1;

INSERT dbo.tblNORM_AccountMap (ConfigurationReleaseId,FinancialYear,EntityCode,GlCode,StatementLine)
VALUES (11,2026,'NIHRACS','10001','EMP_WAGES');
INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy)
VALUES ('MAPPING_WORKBOOK_APPLIED','ConfigurationRelease','11','Mapping edited','LocalDB fixture');
IF EXISTS (SELECT 1 FROM dbo.tblNORM_CalculationRun r WHERE r.CalculationRunId=@review AND (__CURRENT__))
    THROW 51001, 'A prior draft review survived a mapping change.', 1;

INSERT dbo.tblNORM_CalculationRun (ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy)
VALUES (25,11,REPLICATE('5',64),'Complete','LocalDB fixture');
SET @review = SCOPE_IDENTITY();
INSERT dbo.tblNORM_ValidationResult (CalculationRunId,CheckCode,CheckLabel,SeverityCode,ResultCode,DetailText)
SELECT @review,'__MARKER__','Draft mapping review','Blocking','Fail',CONVERT(nvarchar(20),__REVISION__)
FROM dbo.tblNORM_ConfigurationRelease c WHERE c.ConfigurationReleaseId=11;
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_CalculationRun r WHERE r.CalculationRunId=@review AND (__CURRENT__))
    THROW 51001, 'A fresh review could not be read after a mapping change.', 1;

UPDATE dbo.tblNORM_StatementLine SET LineLabel='Fixture label change' WHERE ConfigurationReleaseId=11 AND LineCode='EMP_WAGES';
INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy)
VALUES ('NIHRACS_HIERARCHY_CHANGED','ConfigurationRelease','11','Statement label edited','LocalDB fixture');
IF EXISTS (SELECT 1 FROM dbo.tblNORM_CalculationRun r WHERE r.CalculationRunId=@review AND (__STANDARD__ OR __CURRENT__))
    THROW 51001, 'A prior draft review survived a label change.', 1;
ROLLBACK TRANSACTION;
'@
    Invoke-SeedTestSql $fixture ($reviewSql.Replace('__MARKER__',$marker).Replace('__REVISION__',$revision).Replace('__STANDARD__',$standard).Replace('__CURRENT__',$current))
    if ($protected -cne (Invoke-SeedTestSql $fixture $protectedQuery -Scalar)) { throw 'Review isolation test changed protected fixture data.' }
    Write-Output 'PASS LocalDB: production run predicates exclude reviews from controlled runs and invalidate them after audited mapping and label changes.'
} finally {
    if ($fixture) { $fixture.Dispose() }
    if ($created) { Invoke-SeedTestSql $master "DROP DATABASE [$database];" }
    $master.Dispose()
}
