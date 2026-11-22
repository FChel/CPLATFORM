/* Apply only after baseline review. Existing runs retain their original results. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF @@TRANCOUNT <> 0 OR OBJECT_ID('dbo.tblNORM_LineResult','U') IS NULL
    OR OBJECT_ID('dbo.tblNORM_StatementLine','U') IS NULL
    THROW 51000, 'Use a dedicated session against the approved NORM baseline.', 1;

BEGIN TRANSACTION;
IF COL_LENGTH('dbo.tblNORM_LineResult','ReportedAmount') IS NULL
    ALTER TABLE dbo.tblNORM_LineResult ADD ReportedAmount decimal(19,0) NULL;
IF COL_LENGTH('dbo.tblNORM_StatementLine','ParentLineCode') IS NULL
    ALTER TABLE dbo.tblNORM_StatementLine ADD ParentLineCode varchar(120) NULL;
COMMIT TRANSACTION;
