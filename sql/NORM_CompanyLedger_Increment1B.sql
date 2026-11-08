/* Increment 1B: REVIEW/APPLY SEPARATELY through the approved SQL baseline process.
   Not invoked by application deployment. Does not update historical records. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID('dbo.tblNORM_TrialBalanceRow','U') IS NULL
   OR OBJECT_ID('dbo.tblNORM_EntityLedger','U') IS NULL
    THROW 51000, 'Required NORM baseline tables are missing.', 1;

-- Existing installations/reruns must have the expected nullable VARCHAR(20).
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id IN (OBJECT_ID('dbo.tblNORM_TrialBalanceRow'), OBJECT_ID('dbo.tblNORM_EntityLedger'))
      AND name='SourceCompanyCode'
      AND (system_type_id<>167 OR max_length<>20 OR is_nullable<>1)
) THROW 51000, 'Unexpected SourceCompanyCode definition; review schema drift.', 1;

-- Refuse to remove an unexpectedly redefined legacy constraint.
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID('dbo.tblNORM_EntityLedger') AND name='UQ_tblNORM_EntityLedger')
AND NOT EXISTS (
    SELECT 1 FROM sys.key_constraints k
    WHERE k.parent_object_id=OBJECT_ID('dbo.tblNORM_EntityLedger') AND k.name='UQ_tblNORM_EntityLedger'
      AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=k.parent_object_id AND ic.index_id=k.unique_index_id AND ic.key_ordinal>0)=3
      AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,1)='FinancialYear'
      AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,2)='EntityCode'
      AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,3)='SourceLedger'
) THROW 51000, 'Unexpected legacy permitted-source constraint; review schema drift.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH('dbo.tblNORM_TrialBalanceRow','SourceCompanyCode') IS NULL
        ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD SourceCompanyCode VARCHAR(20) NULL;
    IF COL_LENGTH('dbo.tblNORM_EntityLedger','SourceCompanyCode') IS NULL
        ALTER TABLE dbo.tblNORM_EntityLedger ADD SourceCompanyCode VARCHAR(20) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID('dbo.tblNORM_EntityLedger') AND name='UQ_tblNORM_EntityLedger_CompanyLedger')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tblNORM_EntityLedger
            ADD CONSTRAINT UQ_tblNORM_EntityLedger_CompanyLedger
            UNIQUE (FinancialYear,EntityCode,SourceCompanyCode,SourceLedger);';
    ELSE IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints k
        WHERE k.parent_object_id=OBJECT_ID('dbo.tblNORM_EntityLedger') AND k.name='UQ_tblNORM_EntityLedger_CompanyLedger'
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=k.parent_object_id AND ic.index_id=k.unique_index_id AND ic.key_ordinal>0)=4
          AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,1)='FinancialYear'
          AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,2)='EntityCode'
          AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,3)='SourceCompanyCode'
          AND INDEX_COL('dbo.tblNORM_EntityLedger',k.unique_index_id,4)='SourceLedger'
    ) THROW 51000, 'Unexpected company/ledger constraint; review schema drift.', 1;

    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID('dbo.tblNORM_EntityLedger') AND name='UQ_tblNORM_EntityLedger')
        ALTER TABLE dbo.tblNORM_EntityLedger DROP CONSTRAINT UQ_tblNORM_EntityLedger;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
