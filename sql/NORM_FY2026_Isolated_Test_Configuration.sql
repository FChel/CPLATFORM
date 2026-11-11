/* PREPARED ONLY. Never run on WARATAH. No migration is executed by this script.
   Operator must nominate an existing authorised ISOLATED database and reviewed
   FY2025 baseline. Creates a DRAFT: does not grant or copy approval.
   Set the four parameters below only after review. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ConfirmedIsolatedDatabase SYSNAME = NULL;
DECLARE @BaselineReleaseId INT = NULL;
DECLARE @ReviewedConfiguration BIT = 0;
DECLARE @TestVersion VARCHAR(30) = 'test-fy2026-b1';
IF @ConfirmedIsolatedDatabase IS NULL OR DB_NAME()<>@ConfirmedIsolatedDatabase
   OR @BaselineReleaseId IS NULL OR @ReviewedConfiguration<>1
    THROW 51000, 'Explicit isolated database, baseline ID and configuration review are required.', 1;
IF COL_LENGTH('dbo.tblNORM_EntityLedger','ReportingStream') IS NULL
    THROW 51000, 'Apply and verify the approved Package 1A Step 1 migration first.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ConfigurationRelease
    WHERE ConfigurationReleaseId=@BaselineReleaseId AND FinancialYear=2025
      AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0)
    THROW 51000, 'The named baseline must be an approved FY2025 DEPT release.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @ReleaseId INT;
    SELECT @ReleaseId=ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK)
        WHERE FinancialYear=2026 AND EntityCode='DEPT' AND VersionCode=@TestVersion;
    IF @ReleaseId IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId=@ReleaseId
            AND ParentConfigurationReleaseId=@BaselineReleaseId AND StatusCode='Draft' AND IsDeactivated=0
            AND ReleaseLabel=N'ISOLATED TEST ONLY - FY2026 Departmental / AUD')
            THROW 51000, 'Version exists with a different scope/state. No overwrite performed.', 1;
        COMMIT TRANSACTION;
        SELECT @ReleaseId AS ExistingDraftReleaseId;
        RETURN;
    END;
    IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ReportingEntity WHERE FinancialYear=2026 AND EntityCode='DEPT')
        INSERT dbo.tblNORM_ReportingEntity(FinancialYear,EntityCode,EntityName,BasisNote)
        VALUES(2026,'DEPT',N'Defence Departmental - ISOLATED TEST',N'FY2026 isolated test; AUD; no publication authority.');

    INSERT dbo.tblNORM_ConfigurationRelease(FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy,
        ParentConfigurationReleaseId,ChangeReason)
    VALUES(2026,'DEPT',@TestVersion,N'ISOLATED TEST ONLY - FY2026 Departmental / AUD','Draft',SUSER_SNAME(),
        @BaselineReleaseId,N'Reviewed FY2025 accounting configuration candidates reused for isolated FY2026 test; no financial data or approvals copied.');
    SET @ReleaseId=CONVERT(INT,SCOPE_IDENTITY());
    INSERT dbo.tblNORM_EntityLedger
        (ConfigurationReleaseId,FinancialYear,EntityCode,SourceCompanyCode,SourceLedger,ReportingStream,IsRequired)
    VALUES (@ReleaseId,2026,'DEPT','1000','0L','Departmental',1);
    INSERT dbo.tblNORM_AccountMap(ConfigurationReleaseId,FinancialYear,EntityCode,GlCode,GlDescription,AccountType,
        StatementLine,NoteSubLine,CashFlowClass,MappingRationale,IsDeactivated)
    SELECT @ReleaseId,2026,'DEPT',GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass,
        MappingRationale,0 FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@BaselineReleaseId AND IsDeactivated=0;
    INSERT dbo.tblNORM_StatementLine(ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,
        LineCode,LineLabel,NoteRef,NaturalSign,CalculationKind,FormulaSpec,IsClickable,IsDeactivated)
    SELECT @ReleaseId,2026,'DEPT',StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,NaturalSign,
        CalculationKind,FormulaSpec,IsClickable,0 FROM dbo.tblNORM_StatementLine
        WHERE ConfigurationReleaseId=@BaselineReleaseId AND IsDeactivated=0;
    INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy)
    VALUES('TEST_CONFIGURATION_DRAFT_CREATED','ConfigurationRelease',CONVERT(VARCHAR(20),@ReleaseId),
        N'Isolated FY2026 DEPT/1000/0L draft; AUD; Departmental; baseline '+CONVERT(NVARCHAR(20),@BaselineReleaseId)+
        N'. Financial figures, journals, manual inputs, approvals and results excluded.',SUSER_SNAME());
    COMMIT TRANSACTION;
    SELECT @ReleaseId AS NewDraftReleaseId;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
