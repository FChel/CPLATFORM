/* Package 1A Step 3. PREPARED ONLY: separately reviewed target/backup required.
   No designation, release, source, user or document changes.
   Snapshot the best available entity/year name on existing runs before enabling name edits.
   Names already changed before this migration cannot be reconstructed from this snapshot. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ConfirmedDatabase SYSNAME = NULL;
IF @ConfirmedDatabase IS NULL OR DB_NAME() <> @ConfirmedDatabase
    THROW 51000, 'Confirm the approved target database before applying Step 3.', 1;
IF OBJECT_ID('dbo.tblNORM_YearSetupDocument','U') IS NULL
   OR OBJECT_ID('dbo.tblNORM_UserEntity','U') IS NULL
   OR COL_LENGTH('dbo.tblNORM_ImportFile','CoverageStart') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblNORM_YearSetup') AND name='UX_tblNORM_YearSetup_Current' AND is_unique=1 AND has_filter=1 AND is_disabled=0)
    THROW 51000, 'Required Step 2 / Year Setup baseline is missing.', 1;
IF COL_LENGTH('dbo.tblNORM_CalculationRun','ReportingEntityName') IS NOT NULL
    THROW 51000, 'Run reporting-name column exists; review baseline before rerunning.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    ALTER TABLE dbo.tblNORM_CalculationRun ADD ReportingEntityName NVARCHAR(200) NULL;
    EXEC sys.sp_executesql N'
        UPDATE r SET ReportingEntityName=COALESCE(e.EntityName,i.EntityCode)
        FROM dbo.tblNORM_CalculationRun r
        INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId
        LEFT JOIN dbo.tblNORM_ReportingEntity e ON e.EntityCode=i.EntityCode
            AND e.FinancialYear=i.FinancialYear;';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
