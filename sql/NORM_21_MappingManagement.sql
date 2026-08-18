/* NORM 21 - controlled account-mapping management.
   Adds draft lineage and a row-level mapping change register. Safe to rerun. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.tblNORM_ConfigurationRelease','ParentConfigurationReleaseId') IS NULL
    ALTER TABLE dbo.tblNORM_ConfigurationRelease ADD ParentConfigurationReleaseId INT NULL;
IF COL_LENGTH('dbo.tblNORM_ConfigurationRelease','ChangeReason') IS NULL
    ALTER TABLE dbo.tblNORM_ConfigurationRelease ADD ChangeReason NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.tblNORM_ConfigurationRelease','ReviewedBy') IS NULL
    ALTER TABLE dbo.tblNORM_ConfigurationRelease ADD ReviewedBy NVARCHAR(160) NULL;
IF COL_LENGTH('dbo.tblNORM_ConfigurationRelease','ReviewedUtc') IS NULL
    ALTER TABLE dbo.tblNORM_ConfigurationRelease ADD ReviewedUtc DATETIME2(3) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_tblNORM_ConfigurationRelease_Parent')
    ALTER TABLE dbo.tblNORM_ConfigurationRelease ADD CONSTRAINT FK_tblNORM_ConfigurationRelease_Parent
        FOREIGN KEY(ParentConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);

IF OBJECT_ID('dbo.tblNORM_MappingChange','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_MappingChange (
        MappingChangeId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_MappingChange PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        GlCode VARCHAR(30) NOT NULL,
        BeforeAccountType VARCHAR(20) NULL,
        AfterAccountType VARCHAR(20) NULL,
        BeforeStatementLine VARCHAR(120) NULL,
        AfterStatementLine VARCHAR(120) NULL,
        BeforeNoteSubLine NVARCHAR(240) NULL,
        AfterNoteSubLine NVARCHAR(240) NULL,
        BeforeCashFlowClass NVARCHAR(120) NULL,
        AfterCashFlowClass NVARCHAR(120) NULL,
        ChangeReason NVARCHAR(500) NOT NULL,
        WorkbookHash CHAR(64) NOT NULL,
        ChangedBy NVARCHAR(160) NOT NULL,
        ChangedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_MappingChange_ChangedUtc DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_tblNORM_MappingChange_Release FOREIGN KEY(ConfigurationReleaseId)
            REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_tblNORM_MappingChange_ReleaseGl' AND object_id=OBJECT_ID('dbo.tblNORM_MappingChange'))
    CREATE INDEX IX_tblNORM_MappingChange_ReleaseGl
        ON dbo.tblNORM_MappingChange(ConfigurationReleaseId,GlCode,MappingChangeId DESC);

COMMIT TRANSACTION;
PRINT 'NORM mapping-management schema is ready.';
