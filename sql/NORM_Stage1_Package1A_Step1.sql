/* Package 1A Step 1. Apply separately after NORM_CompanyLedger_Increment1B and
   NORM_08_StartOfFinancialYearSetup. Review the named target and take a backup.
   The only reset is the old NORM permitted-source list. Reseed from the reviewed
   release/population matrix. No company, ledger or stream values are inferred. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ConfirmedDatabase SYSNAME = NULL;
DECLARE @ReviewedPopulationReset BIT = 0;

IF @ConfirmedDatabase IS NULL OR DB_NAME() <> @ConfirmedDatabase
   OR @ReviewedPopulationReset <> 1
    THROW 51000, 'Name the approved database and explicitly approve reseeding the NORM source list.', 1;

IF OBJECT_ID('dbo.tblNORM_YearSetup','U') IS NULL
   OR COL_LENGTH('dbo.tblNORM_EntityLedger','SourceCompanyCode') IS NULL
   OR COL_LENGTH('dbo.tblNORM_TrialBalanceRow','SourceCompanyCode') IS NULL
    THROW 51000, 'Required NORM baseline is missing.', 1;

IF COL_LENGTH('dbo.tblNORM_EntityLedger','ConfigurationReleaseId') IS NOT NULL
   OR OBJECT_ID('dbo.tblNORM_UserEntity','U') IS NOT NULL
    THROW 51000, 'Step 1 is already installed or the schema has drifted. Review the baseline.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM dbo.tblNORM_EntityLedger;
    ALTER TABLE dbo.tblNORM_EntityLedger DROP CONSTRAINT UQ_tblNORM_EntityLedger_CompanyLedger;
    ALTER TABLE dbo.tblNORM_EntityLedger ALTER COLUMN SourceCompanyCode VARCHAR(20) NOT NULL;
    ALTER TABLE dbo.tblNORM_EntityLedger ALTER COLUMN SourceLedger VARCHAR(20) NULL;
    ALTER TABLE dbo.tblNORM_ConfigurationRelease ADD CONSTRAINT UQ_tblNORM_ConfigurationRelease_Scope
        UNIQUE (ConfigurationReleaseId, FinancialYear, EntityCode);
    ALTER TABLE dbo.tblNORM_EntityLedger ADD ConfigurationReleaseId INT NOT NULL,
        ReportingStream VARCHAR(20) NOT NULL, IsRequired BIT NOT NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.tblNORM_EntityLedger ADD
            CONSTRAINT FK_tblNORM_EntityLedger_Release FOREIGN KEY (ConfigurationReleaseId, FinancialYear, EntityCode)
                REFERENCES dbo.tblNORM_ConfigurationRelease (ConfigurationReleaseId, FinancialYear, EntityCode),
            CONSTRAINT UQ_tblNORM_EntityLedger_Population
                UNIQUE (ConfigurationReleaseId, SourceCompanyCode, SourceLedger, ReportingStream),
            CONSTRAINT CK_tblNORM_EntityLedger_Population CHECK
                (LEN(LTRIM(RTRIM(SourceCompanyCode))) > 0
                 AND (SourceLedger IS NULL OR LEN(LTRIM(RTRIM(SourceLedger))) > 0)
                 AND ReportingStream IN (''Departmental'', ''Administered''));';

    DROP INDEX IX_tblNORM_TbRow_ImportGl ON dbo.tblNORM_TrialBalanceRow;
    ALTER TABLE dbo.tblNORM_TrialBalanceRow ALTER COLUMN SourceLedger VARCHAR(20) NULL;
    ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD ReportingStream VARCHAR(20) NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD CONSTRAINT CK_tblNORM_TbRow_ReportingStream CHECK
            (ReportingStream IS NULL OR (ReportingStream IN (''Departmental'', ''Administered'')
             AND SourceCompanyCode IS NOT NULL));
        CREATE INDEX IX_tblNORM_TbRow_ImportGl ON dbo.tblNORM_TrialBalanceRow(ImportId, GlAccount)
            INCLUDE (AccumBalance, SourceLedger, GlText) WHERE IsDeactivated = 0;';

    CREATE TABLE dbo.tblNORM_UserEntity (
        AdminUserId INT NOT NULL REFERENCES dbo.tblNORM_AdminUser(AdminUserId),
        EntityCode VARCHAR(20) NOT NULL,
        CONSTRAINT PK_tblNORM_UserEntity PRIMARY KEY (AdminUserId, EntityCode)
    );
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
