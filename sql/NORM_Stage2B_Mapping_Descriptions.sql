IF OBJECT_ID('dbo.tblNORM_StatementLine', 'U') IS NULL
    THROW 51000, 'Install the NORM statement-line schema first.', 1;

IF COL_LENGTH('dbo.tblNORM_StatementLine', 'MappingDescription') IS NULL
    ALTER TABLE dbo.tblNORM_StatementLine ADD MappingDescription nvarchar(240) NULL;
