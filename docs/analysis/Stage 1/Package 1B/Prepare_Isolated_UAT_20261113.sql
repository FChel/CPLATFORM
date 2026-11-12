/* Owner-authorised Package 1B test configuration DATA ONLY. No schema/baseline change.
   Source-import mechanics only; no genuine accounting mappings/classifications. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> N'CPlatform' OR CONVERT(sysname,SERVERPROPERTY('MachineName')) <> N'WARATAH'
 THROW 51000,'STOP: unexpected target',1;
IF @@TRANCOUNT<>0 THROW 51000,'STOP: use dedicated operator session',1;
IF NOT EXISTS(SELECT 1 FROM dbo.tblNORM_AdminUser WHERE UserId=N'WARATAH\Steve' AND RoleCode='Administrator' AND IsDeactivated=0)
 THROW 51000,'STOP: authorised Administrator unavailable',1;
IF EXISTS(SELECT 1 FROM dbo.tblNORM_ReportingEntity WHERE EntityCode IN ('UAT_AAG','UAT_ICON'))
 OR EXISTS(SELECT 1 FROM dbo.tblNORM_ConfigurationRelease WHERE EntityCode IN ('UAT_AAG','UAT_ICON'))
 OR EXISTS(SELECT 1 FROM dbo.tblNORM_YearSetup WHERE EntityCode IN ('UAT_AAG','UAT_ICON'))
 THROW 51000,'STOP: test context already exists; inspect before any retry',1;
DECLARE @DeptBefore varbinary(32),@DeptAfter varbinary(32);
DECLARE @Snapshot nvarchar(max)=N'SELECT @h=HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 (SELECT * FROM dbo.tblNORM_ReportingEntity WHERE EntityCode=''DEPT'' ORDER BY ReportingEntityId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE EntityCode=''DEPT'' ORDER BY ConfigurationReleaseId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.tblNORM_EntityLedger WHERE EntityCode=''DEPT'' ORDER BY EntityLedgerId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.tblNORM_YearSetup WHERE EntityCode=''DEPT'' ORDER BY YearSetupId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.tblNORM_AccountMap WHERE EntityCode=''DEPT'' ORDER BY AccountMapId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.tblNORM_StatementLine WHERE EntityCode=''DEPT'' ORDER BY StatementLineId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.tblNORM_Import WHERE EntityCode=''DEPT'' ORDER BY ImportId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT f.* FROM dbo.tblNORM_ImportFile f JOIN dbo.tblNORM_Import i ON i.ImportId=f.ImportId WHERE i.EntityCode=''DEPT'' ORDER BY ImportFileId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT t.* FROM dbo.tblNORM_TrialBalanceRow t JOIN dbo.tblNORM_Import i ON i.ImportId=t.ImportId WHERE i.EntityCode=''DEPT'' ORDER BY TbRowId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT r.* FROM dbo.tblNORM_CalculationRun r JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId WHERE i.EntityCode=''DEPT'' ORDER BY CalculationRunId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT d.* FROM dbo.tblNORM_YearSetupDocument d JOIN dbo.tblNORM_YearSetup s ON s.YearSetupId=d.YearSetupId WHERE s.EntityCode=''DEPT'' ORDER BY YearSetupDocumentId FOR JSON PATH,INCLUDE_NULL_VALUES)
 ));';
BEGIN TRY
 BEGIN TRANSACTION;
 EXEC sys.sp_executesql @Snapshot,N'@h varbinary(32) OUTPUT',@h=@DeptBefore OUTPUT;
 DECLARE @Actor nvarchar(160)=SUSER_SNAME(),@Release int,@Entity varchar(20),@FY int,@Company varchar(20),@Index int=1;
 WHILE @Index<=2
 BEGIN
  SELECT @Entity=CASE WHEN @Index=1 THEN 'UAT_AAG' ELSE 'UAT_ICON' END,
         @FY=CASE WHEN @Index=1 THEN 2020 ELSE 2016 END,
         @Company=CASE WHEN @Index=1 THEN '150' ELSE '86' END;
  INSERT dbo.tblNORM_ReportingEntity(FinancialYear,EntityCode,EntityName,BasisNote)
   VALUES(@FY,@Entity,N'TEST ONLY - Package 1B '+@Entity,N'Owner-authorised source-import mechanics only. Departmental/null ledger are test conventions. No accounting/publication authority.');
  INSERT dbo.tblNORM_ConfigurationRelease(FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy,ChangeReason)
   VALUES(@FY,@Entity,'uat-package1b-20261113',N'TEST ONLY - native source import mechanics - no financial mappings','Draft',@Actor,N'Owner authorisation 13 November 2026; approved native files; no DEPT change or copied financial classifications.');
  SET @Release=CONVERT(int,SCOPE_IDENTITY());
  INSERT dbo.tblNORM_EntityLedger(ConfigurationReleaseId,FinancialYear,EntityCode,SourceCompanyCode,SourceLedger,ReportingStream,IsRequired)
   VALUES(@Release,@FY,@Entity,@Company,NULL,'Departmental',1);
  INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy)
   VALUES('UAT_CONFIGURATION_DRAFT_CREATED','ConfigurationRelease',CONVERT(varchar(20),@Release),
    @Entity+N'; owner-approved 20261113; source-import mechanics only; company='+@Company+N'; ledger=NULL; stream=Departmental (test convention); mappings=0',@Actor);
  /* Initial source-mechanics baseline approval, not normal mapping-change approval.
     ContentHash is the existing mapping-hash convention for an empty mapping set. */
  UPDATE dbo.tblNORM_ConfigurationRelease SET StatusCode='Approved',
   ContentHash=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),'')),2)),
   ReviewedBy=N'WARATAH\Steve',ReviewedUtc=SYSUTCDATETIME(),ApprovedBy=N'WARATAH\Steve',ApprovedUtc=SYSUTCDATETIME()
   WHERE ConfigurationReleaseId=@Release AND StatusCode='Draft';
  INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy)
   VALUES('INITIAL_TEST_BASELINE_APPROVED','ConfigurationRelease',CONVERT(varchar(20),@Release),
    @Entity+N'; owner-authorised 20261113; initial source-mechanics baseline only; no mapped accounts or statement definitions; empty mapping SHA256; approval does not assert genuine financial classifications',@Actor);
  INSERT dbo.tblNORM_YearSetup(EntityCode,CurrentFinancialYear,IsCurrent,UpdatedBy) VALUES(@Entity,@FY,1,@Actor);
  INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy)
   VALUES('YEAR_SETUP_UPDATED','YearSetup',CONVERT(varchar(20),SCOPE_IDENTITY()),@Entity+N'; initial current year='+CONVERT(nvarchar(10),@FY)+N'; owner-approved isolated UAT only',@Actor);
  SET @Index+=1;
 END;
 EXEC sys.sp_executesql @Snapshot,N'@h varbinary(32) OUTPUT',@h=@DeptAfter OUTPUT;
 IF @DeptBefore<>@DeptAfter THROW 51000,'STOP: DEPT preservation check failed',1;
 IF EXISTS(SELECT 1 FROM dbo.tblNORM_AccountMap m JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=m.ConfigurationReleaseId WHERE c.EntityCode IN ('UAT_AAG','UAT_ICON'))
  THROW 51000,'STOP: unexpected UAT financial mappings',1;
 COMMIT TRANSACTION;
 SELECT CONVERT(varchar(64),@DeptBefore,2) AS DeptBeforeSHA256,CONVERT(varchar(64),@DeptAfter,2) AS DeptAfterSHA256;
 SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,StatusCode,ContentHash FROM dbo.tblNORM_ConfigurationRelease WHERE EntityCode IN ('UAT_AAG','UAT_ICON');
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
