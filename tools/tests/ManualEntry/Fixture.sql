/* Disposable fixture only. No approved accounting amounts are seeded by the migration. */
ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD ReportingStream VARCHAR(20) NULL, SourceCompanyCode VARCHAR(20) NULL;
ALTER TABLE dbo.tblNORM_CalculationRun ADD ReportingEntityName NVARCHAR(200) NULL;
ALTER TABLE dbo.tblNORM_ImportFile ADD CoverageStart DATE NULL, CoverageEnd DATE NULL, IsFullYear BIT NULL;
GO
CREATE TABLE dbo.tblNORM_UserEntity (AdminUserId INT NOT NULL, EntityCode VARCHAR(20) NOT NULL);
INSERT dbo.tblNORM_AdminUser(UserId, DisplayName, RoleCode, CreatedBy)
VALUES ('Manual tester','Manual preparer','Preparer','fixture'),
       ('Outside tester','Other entity preparer','Preparer','fixture');
INSERT dbo.tblNORM_UserEntity SELECT AdminUserId, CASE UserId WHEN 'Manual tester' THEN 'NIHRACS' ELSE 'OTHER' END
FROM dbo.tblNORM_AdminUser WHERE UserId IN ('Manual tester','Outside tester');

INSERT dbo.tblNORM_ReportingEntity(FinancialYear,EntityCode,EntityName)
VALUES (2025,'NIHRACS','NIHRACS fixture'),(2026,'NIHRACS','NIHRACS fixture'),(2027,'NIHRACS','NIHRACS fixture'),(2026,'OTHER','Other fixture');
SET IDENTITY_INSERT dbo.tblNORM_ConfigurationRelease ON;
INSERT dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy)
VALUES (1,2025,'DEPT','v1.0','Base fixture','Approved','fixture'),
       (18,2025,'NIHRACS','fixture','FY2025 fixture','Draft','fixture'),
       (19,2026,'NIHRACS','fixture','FY2026 fixture','Draft','fixture'),
       (20,2027,'NIHRACS','fixture','FY2027 fixture','Draft','fixture');
SET IDENTITY_INSERT dbo.tblNORM_ConfigurationRelease OFF;

SET IDENTITY_INSERT dbo.tblNORM_Import ON;
INSERT dbo.tblNORM_Import(ImportId,ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,DataFingerprint,
    SourceFileBytes,StatusCode,ImportedBy,[RowCount])
SELECT Id,Release,Year,'NIHRACS','ERP','Manual entry fixture.xlsx',REPLICATE('0',62)+CONVERT(varchar(2),Id),
    REPLICATE('1',62)+CONVERT(varchar(2),Id),1,'Imported','fixture',1
FROM (VALUES (1,18,2025),(2,19,2026),(3,20,2027),(4,19,2026)) x(Id,Release,Year);
SET IDENTITY_INSERT dbo.tblNORM_Import OFF;
INSERT dbo.tblNORM_TrialBalanceRow(ImportId,SourceRowNo,SourceLedger,ReportingStream,GlAccount,AccumBalance,RowHash)
SELECT ImportId,1,'fixture','Departmental','12345',1.23,REPLICATE('5',64) FROM dbo.tblNORM_Import;

SET IDENTITY_INSERT dbo.tblNORM_CalculationRun ON;
INSERT dbo.tblNORM_CalculationRun(CalculationRunId,ImportId,ConfigurationReleaseId,InputFingerprint,StatusCode,StartedBy,CompletedUtc)
SELECT Run,ImportId,Release,REPLICATE('8',62)+CONVERT(varchar(2),Run),'Complete','fixture',SYSUTCDATETIME()
FROM (VALUES (1,1,18),(2,2,19),(3,1,19),(4,4,19),(5,3,20),(6,2,20)) x(Run,ImportId,Release);
SET IDENTITY_INSERT dbo.tblNORM_CalculationRun OFF;
INSERT dbo.tblNORM_ValidationResult(CalculationRunId,CheckCode,CheckLabel,SeverityCode,ResultCode,ActualValue,DetailText)
VALUES (3,'COMPARATIVE_PERIOD','Comparative fixture','Information','Pass',NULL,'fixture'),
       (6,'COMPARATIVE_PERIOD','Comparative fixture','Information','Pass',NULL,'fixture'),
       (2,'COMPARATIVE_RUN','Linked comparative fixture','Information','Pass',3,'fixture'),
       (4,'COMPARATIVE_RUN','Linked comparative fixture','Information','Pass',3,'fixture'),
       (5,'COMPARATIVE_RUN','Linked comparative fixture','Information','Pass',6,'fixture'),
       (2,'FIXTURE_OPEN','Independent unresolved validation','Blocking','Fail',NULL,'Retain the existing failed check.');
INSERT dbo.tblNORM_ValidationResult(CalculationRunId,CheckCode,CheckLabel,SeverityCode,ResultCode,DetailText)
SELECT Run,'DRAFT_MAPPING_REVIEW','Draft review fixture','Information','Pass','0' FROM (VALUES (1),(2),(4),(5)) x(Run);

INSERT dbo.tblNORM_StatementLine(ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,CalculationKind,ParentLineCode)
VALUES (19,2026,'NIHRACS','SOFP',1,'line','Employee provisions','Employee provisions','Formula',NULL),
       (19,2026,'NIHRACS','SOFP',2,'line','Leases','Leases','Formula',NULL),
       (19,2026,'NIHRACS','SOFP',100,'hierarchy-detail','PRV_EMP','Employee provisions','Mapped','Employee provisions');
INSERT dbo.tblNORM_LineResult(CalculationRunId,StatementLineId,StatementCode,LineCode,ComputedAmount,ReportedAmount,StatusCode)
SELECT v.Run,l.StatementLineId,'SOFP',v.Line,v.Exact,v.Presented,'Mapped'
FROM (VALUES (1,'Employee provisions',1235.211,1235),(1,'Leases',104.936,105),
    (2,'Employee provisions',1398.426,1398),(2,'Leases',70.086,70),
    (3,'Employee provisions',1235.211,1235),(3,'Leases',104.936,105),
    (4,'Employee provisions',1327.508,1328),(4,'Leases',33.400,34),
    (6,'Employee provisions',1398.426,1398),(6,'Leases',70.086,70)) v(Run,Line,Exact,Presented)
JOIN dbo.tblNORM_StatementLine l ON l.LineCode=v.Line AND l.ConfigurationReleaseId=19;
