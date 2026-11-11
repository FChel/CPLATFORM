/* Package 1A Step 2. Apply separately after Step 1 and reviewed backup.
   Existing source records retain unknown exact coverage. No backfill or reset. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ConfirmedDatabase SYSNAME = NULL;
IF @ConfirmedDatabase IS NULL OR DB_NAME() <> @ConfirmedDatabase
    THROW 51000, 'Confirm the approved target database before applying Step 2.', 1;

IF OBJECT_ID('dbo.tblNORM_YearSetup','U') IS NULL
   OR OBJECT_ID('dbo.tblNORM_UserEntity','U') IS NULL
   OR COL_LENGTH('dbo.tblNORM_TrialBalanceRow','ReportingStream') IS NULL
   OR OBJECT_ID('dbo.tblNORM_ImportFile','U') IS NULL
    THROW 51000, 'Required Step 1 baseline is missing.', 1;

IF COL_LENGTH('dbo.tblNORM_ImportFile','CoverageStart') IS NOT NULL
   OR COL_LENGTH('dbo.tblNORM_ImportFile','CoverageEnd') IS NOT NULL
   OR COL_LENGTH('dbo.tblNORM_ImportFile','IsFullYear') IS NOT NULL
    THROW 51000, 'Coverage columns already exist. Review the baseline before rerunning.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    ALTER TABLE dbo.tblNORM_ImportFile ADD
        CoverageStart DATE NULL,
        CoverageEnd DATE NULL,
        IsFullYear BIT NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.tblNORM_ImportFile WITH CHECK
        ADD CONSTRAINT CK_tblNORM_ImportFile_ExactCoverage CHECK (
            (CoverageStart IS NULL AND CoverageEnd IS NULL AND IsFullYear IS NULL)
            OR
            (CoverageStart IS NOT NULL AND CoverageEnd IS NOT NULL AND IsFullYear IS NOT NULL
                AND CoverageStart <= CoverageEnd)
        );';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
