/* Apply only after baseline review. Existing releases, population rows and imports are retained. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 0 OR OBJECT_ID('dbo.tblNORM_ReportingEntity','U') IS NULL
 OR OBJECT_ID('dbo.tblNORM_Import','U') IS NULL
    THROW 51000, 'Use a dedicated session against the approved NORM baseline.', 1;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.tblNORM_ReportingEntity','PriorYearsRequired') IS NULL
    ALTER TABLE dbo.tblNORM_ReportingEntity ADD PriorYearsRequired int NULL;
IF COL_LENGTH('dbo.tblNORM_ReportingEntity','SourceApprovalSnapshot') IS NULL
    ALTER TABLE dbo.tblNORM_ReportingEntity ADD SourceApprovalSnapshot nvarchar(max) NULL, SourceApprovalHash char(64) NULL;
IF OBJECT_ID('dbo.CK_tblNORM_ReportingEntity_SourceApproval','C') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_ReportingEntity WITH CHECK ADD CONSTRAINT CK_tblNORM_ReportingEntity_SourceApproval CHECK
        (PriorYearsRequired >= 0 AND PriorYearsRequired <= FinancialYear - 1900 AND
        ((SourceApprovalSnapshot IS NULL AND SourceApprovalHash IS NULL) OR
        (PriorYearsRequired IS NOT NULL AND SourceApprovalSnapshot IS NOT NULL AND SourceApprovalHash IS NOT NULL)));');
IF COL_LENGTH('dbo.tblNORM_Import','AnnualSourceApprovalSnapshot') IS NULL
    ALTER TABLE dbo.tblNORM_Import ADD AnnualSourceApprovalSnapshot nvarchar(max) NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblNORM_Import')
    AND name='ConfigurationReleaseId' AND is_nullable=0)
    ALTER TABLE dbo.tblNORM_Import ALTER COLUMN ConfigurationReleaseId int NULL;
IF OBJECT_ID('dbo.CK_tblNORM_Import_SourceAuthority','C') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_Import WITH CHECK ADD CONSTRAINT CK_tblNORM_Import_SourceAuthority CHECK
        ((ConfigurationReleaseId IS NULL AND AnnualSourceApprovalSnapshot IS NOT NULL) OR
        (ConfigurationReleaseId IS NOT NULL AND AnnualSourceApprovalSnapshot IS NULL));');
COMMIT TRANSACTION;
