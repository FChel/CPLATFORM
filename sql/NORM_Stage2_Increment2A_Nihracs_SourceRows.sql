/* Additive NIHRACS source-row fields. Apply only after baseline review and backup. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.tblNORM_TrialBalanceRow', 'U') IS NULL
    THROW 51000, 'The NORM Stage 1 schema must already be installed.', 1;

BEGIN TRANSACTION;
IF COL_LENGTH('dbo.tblNORM_TrialBalanceRow', 'SourceCostCentre') IS NULL
    ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD SourceCostCentre NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.tblNORM_TrialBalanceRow', 'SourceDebit') IS NULL
    ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD SourceDebit DECIMAL(19,2) NULL;
IF COL_LENGTH('dbo.tblNORM_TrialBalanceRow', 'SourceCredit') IS NULL
    ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD SourceCredit DECIMAL(19,2) NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_TrialBalanceRow')
    AND name = 'SourceCostCentre' AND system_type_id = TYPE_ID('nvarchar') AND max_length = 60 AND is_nullable = 1
) OR (
    SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_TrialBalanceRow')
    AND name IN ('SourceDebit','SourceCredit') AND system_type_id = TYPE_ID('decimal')
    AND precision = 19 AND scale = 2 AND is_nullable = 1
) <> 2
    THROW 51000, 'Unexpected NIHRACS source-row column definitions.', 1;
COMMIT TRANSACTION;
