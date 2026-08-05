/* ============================================================================
   NORM proof engine - additive, idempotent schema

   Run against the CPlatform database before all other NORM scripts.
   The script creates missing objects and adds missing columns. It never drops,
   truncates or recreates an existing object.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.tblNORM_ReportingEntity', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ReportingEntity (
        ReportingEntityId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_ReportingEntity PRIMARY KEY,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        EntityName NVARCHAR(200) NOT NULL,
        BasisNote NVARCHAR(500) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_ReportingEntity_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_ReportingEntity_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_tblNORM_ReportingEntity UNIQUE (FinancialYear, EntityCode)
    );
END;

IF OBJECT_ID('dbo.tblNORM_ConfigurationRelease', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ConfigurationRelease (
        ConfigurationReleaseId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_ConfigurationRelease PRIMARY KEY,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        VersionCode VARCHAR(30) NOT NULL,
        ReleaseLabel NVARCHAR(160) NOT NULL,
        StatusCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_ConfigurationRelease_Status DEFAULT ('Draft'),
        ContentHash CHAR(64) NULL,
        ApprovedBy NVARCHAR(160) NULL,
        ApprovedUtc DATETIME2(3) NULL,
        CreatedBy NVARCHAR(160) NOT NULL,
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_ConfigurationRelease_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_ConfigurationRelease_IsDeactivated DEFAULT (0),
        CONSTRAINT CK_tblNORM_ConfigurationRelease_Status CHECK (StatusCode IN ('Draft','Approved','Retired')),
        CONSTRAINT UQ_tblNORM_ConfigurationRelease UNIQUE (FinancialYear, EntityCode, VersionCode)
    );
END;

IF OBJECT_ID('dbo.tblNORM_EntityLedger', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_EntityLedger (
        EntityLedgerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_EntityLedger PRIMARY KEY,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        SourceLedger VARCHAR(20) NOT NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_EntityLedger_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_EntityLedger_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_tblNORM_EntityLedger UNIQUE (FinancialYear, EntityCode, SourceLedger)
    );
END;

/* These three content tables deliberately retain the original FY/entity
   columns. The supplied FY2025 mapping script can therefore seed them before
   NORM_02 promotes the rows into an immutable configuration release. */
IF OBJECT_ID('dbo.tblNORM_AccountMap', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_AccountMap (
        AccountMapId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_AccountMap PRIMARY KEY,
        ConfigurationReleaseId INT NULL,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        GlCode VARCHAR(20) NOT NULL,
        GlDescription NVARCHAR(200) NULL,
        AccountType VARCHAR(20) NULL,
        StatementLine VARCHAR(120) NULL,
        NoteSubLine NVARCHAR(240) NULL,
        CashFlowClass NVARCHAR(120) NULL,
        MappingRationale NVARCHAR(500) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_AccountMap_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_AccountMap_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
END;
ELSE IF COL_LENGTH('dbo.tblNORM_AccountMap', 'ConfigurationReleaseId') IS NULL
    ALTER TABLE dbo.tblNORM_AccountMap ADD ConfigurationReleaseId INT NULL;

IF OBJECT_ID('dbo.tblNORM_StatementLine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_StatementLine (
        StatementLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_StatementLine PRIMARY KEY,
        ConfigurationReleaseId INT NULL,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        StatementCode VARCHAR(20) NOT NULL,
        SeqNo INT NOT NULL,
        LineType VARCHAR(20) NOT NULL,
        LineCode VARCHAR(120) NULL,
        LineLabel NVARCHAR(240) NOT NULL,
        NoteRef VARCHAR(30) NULL,
        NaturalSign CHAR(1) NULL,
        CalculationKind VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_StatementLine_CalculationKind DEFAULT ('Mapped'),
        FormulaSpec NVARCHAR(1000) NULL,
        IsClickable BIT NOT NULL CONSTRAINT DF_tblNORM_StatementLine_IsClickable DEFAULT (1),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_StatementLine_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_StatementLine_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_tblNORM_StatementLine_NaturalSign CHECK (NaturalSign IS NULL OR NaturalSign IN ('D','C')),
        CONSTRAINT CK_tblNORM_StatementLine_CalculationKind CHECK (CalculationKind IN ('Mapped','Formula','Heading'))
    );
END;
ELSE
BEGIN
    IF COL_LENGTH('dbo.tblNORM_StatementLine', 'ConfigurationReleaseId') IS NULL
        ALTER TABLE dbo.tblNORM_StatementLine ADD ConfigurationReleaseId INT NULL;
    IF COL_LENGTH('dbo.tblNORM_StatementLine', 'CalculationKind') IS NULL
        ALTER TABLE dbo.tblNORM_StatementLine ADD CalculationKind VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_StatementLine_CalculationKind DEFAULT ('Mapped');
    IF COL_LENGTH('dbo.tblNORM_StatementLine', 'FormulaSpec') IS NULL
        ALTER TABLE dbo.tblNORM_StatementLine ADD FormulaSpec NVARCHAR(1000) NULL;
    IF COL_LENGTH('dbo.tblNORM_StatementLine', 'IsClickable') IS NULL
        ALTER TABLE dbo.tblNORM_StatementLine ADD IsClickable BIT NOT NULL CONSTRAINT DF_tblNORM_StatementLine_IsClickable DEFAULT (1);
END;

IF OBJECT_ID('dbo.tblNORM_PublishedFigure', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_PublishedFigure (
        PublishedFigureId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_PublishedFigure PRIMARY KEY,
        ConfigurationReleaseId INT NULL,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        StatementCode VARCHAR(20) NOT NULL,
        LineCode VARCHAR(120) NOT NULL,
        AmountCurrent DECIMAL(19,3) NULL,
        AmountPrior DECIMAL(19,3) NULL,
        SourceReference NVARCHAR(300) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_PublishedFigure_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_PublishedFigure_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
END;
ELSE IF COL_LENGTH('dbo.tblNORM_PublishedFigure', 'ConfigurationReleaseId') IS NULL
    ALTER TABLE dbo.tblNORM_PublishedFigure ADD ConfigurationReleaseId INT NULL;

IF OBJECT_ID('dbo.tblNORM_AdminUser', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_AdminUser (
        AdminUserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_AdminUser PRIMARY KEY,
        UserId NVARCHAR(160) NOT NULL,
        DisplayName NVARCHAR(200) NULL,
        RoleCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_AdminUser_Role DEFAULT ('Preparer'),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_AdminUser_IsDeactivated DEFAULT (0),
        CreatedBy NVARCHAR(160) NOT NULL,
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_AdminUser_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_tblNORM_AdminUser_Role CHECK (RoleCode IN ('Preparer','Administrator')),
        CONSTRAINT UQ_tblNORM_AdminUser_UserId UNIQUE (UserId)
    );
END;

IF OBJECT_ID('dbo.tblNORM_Import', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_Import (
        ImportId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_Import PRIMARY KEY,
        ImportGuid UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_tblNORM_Import_Guid DEFAULT (NEWSEQUENTIALID()),
        ConfigurationReleaseId INT NOT NULL,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        SourceType VARCHAR(20) NOT NULL,
        SourceFileName NVARCHAR(260) NOT NULL,
        SourceFileHash CHAR(64) NOT NULL,
        DataFingerprint CHAR(64) NOT NULL,
        SourceFileBytes BIGINT NOT NULL,
        [RowCount] INT NOT NULL CONSTRAINT DF_tblNORM_Import_RowCount DEFAULT (0),
        TotalDebit DECIMAL(19,2) NOT NULL CONSTRAINT DF_tblNORM_Import_TotalDebit DEFAULT (0),
        TotalCredit DECIMAL(19,2) NOT NULL CONSTRAINT DF_tblNORM_Import_TotalCredit DEFAULT (0),
        NetBalance DECIMAL(19,2) NOT NULL CONSTRAINT DF_tblNORM_Import_NetBalance DEFAULT (0),
        StatusCode VARCHAR(20) NOT NULL,
        IsTestBreak BIT NOT NULL CONSTRAINT DF_tblNORM_Import_IsTestBreak DEFAULT (0),
        ParentImportId INT NULL,
        ImportedBy NVARCHAR(160) NOT NULL,
        ImportedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_Import_ImportedUtc DEFAULT (SYSUTCDATETIME()),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_Import_IsDeactivated DEFAULT (0),
        CONSTRAINT CK_tblNORM_Import_Status CHECK (StatusCode IN ('Importing','Imported','Calculated','Failed')),
        CONSTRAINT CK_tblNORM_Import_SourceType CHECK (SourceType IN ('ERP','ROMAN','ROMAN+ERP','TestBreak')),
        CONSTRAINT UQ_tblNORM_Import_Guid UNIQUE (ImportGuid)
    );
END;

IF OBJECT_ID('dbo.tblNORM_TrialBalanceRow', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_TrialBalanceRow (
        TbRowId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_TrialBalanceRow PRIMARY KEY,
        ImportId INT NOT NULL,
        SourceRowNo INT NOT NULL,
        SourceLedger VARCHAR(20) NOT NULL,
        GlAccount VARCHAR(30) NOT NULL,
        GlText NVARCHAR(300) NULL,
        OpeningBalance DECIMAL(19,2) NULL,
        DebitMovement DECIMAL(19,2) NULL,
        CreditMovement DECIMAL(19,2) NULL,
        AccumBalance DECIMAL(19,2) NOT NULL,
        RowHash CHAR(64) NOT NULL,
        IsSynthetic BIT NOT NULL CONSTRAINT DF_tblNORM_TrialBalanceRow_IsSynthetic DEFAULT (0),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_TrialBalanceRow_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_TrialBalanceRow_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_tblNORM_TrialBalanceRow UNIQUE (ImportId, SourceRowNo)
    );
END;

IF OBJECT_ID('dbo.tblNORM_ImportFile', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ImportFile (
        ImportFileId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_ImportFile PRIMARY KEY,
        ImportId INT NOT NULL,
        SourceType VARCHAR(20) NOT NULL,
        SourceFileName NVARCHAR(260) NOT NULL,
        SourceFileHash CHAR(64) NOT NULL,
        SourceFileBytes BIGINT NOT NULL,
        PeriodStart TINYINT NULL,
        PeriodEnd TINYINT NULL,
        [RowCount] INT NOT NULL,
        IsStatementInput BIT NOT NULL CONSTRAINT DF_tblNORM_ImportFile_IsStatementInput DEFAULT (1),
        FileContent VARBINARY(MAX) NOT NULL,
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_ImportFile_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_tblNORM_ImportFile_SourceType CHECK (SourceType IN ('ERP','ROMAN')),
        CONSTRAINT CK_tblNORM_ImportFile_Period CHECK
            ((PeriodStart IS NULL AND PeriodEnd IS NULL) OR
             (PeriodStart BETWEEN 1 AND 16 AND PeriodEnd BETWEEN PeriodStart AND 16)),
        CONSTRAINT UQ_tblNORM_ImportFile_ImportSource UNIQUE (ImportId, SourceType)
    );
END;

/* Safe in-place promotion from the original one-file evidence table. Existing
   evidence is retained; new imports record each source file and period range. */
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'SourceType') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD SourceType VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'SourceFileName') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD SourceFileName NVARCHAR(260) NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'SourceFileHash') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD SourceFileHash CHAR(64) NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'SourceFileBytes') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD SourceFileBytes BIGINT NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'PeriodStart') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD PeriodStart TINYINT NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'PeriodEnd') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD PeriodEnd TINYINT NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'RowCount') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD [RowCount] INT NULL;
IF COL_LENGTH('dbo.tblNORM_ImportFile', 'IsStatementInput') IS NULL
    ALTER TABLE dbo.tblNORM_ImportFile ADD IsStatementInput BIT NOT NULL CONSTRAINT DF_tblNORM_ImportFile_IsStatementInput DEFAULT (1);

EXEC sys.sp_executesql N'
UPDATE f
SET SourceType = CASE WHEN i.SourceType = ''ROMAN'' OR p.SourceType = ''ROMAN'' THEN ''ROMAN'' ELSE ''ERP'' END,
    SourceFileName = i.SourceFileName,
    SourceFileHash = i.SourceFileHash,
    SourceFileBytes = i.SourceFileBytes,
    [RowCount] = i.[RowCount]
FROM dbo.tblNORM_ImportFile f
INNER JOIN dbo.tblNORM_Import i ON i.ImportId = f.ImportId
LEFT JOIN dbo.tblNORM_Import p ON p.ImportId = i.ParentImportId
WHERE f.SourceType IS NULL OR f.SourceFileName IS NULL OR f.SourceFileHash IS NULL OR
      f.SourceFileBytes IS NULL OR f.[RowCount] IS NULL;';

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_ImportFile') AND name = 'SourceType' AND is_nullable = 1)
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ALTER COLUMN SourceType VARCHAR(20) NOT NULL;');
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_ImportFile') AND name = 'SourceFileName' AND is_nullable = 1)
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ALTER COLUMN SourceFileName NVARCHAR(260) NOT NULL;');
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_ImportFile') AND name = 'SourceFileHash' AND is_nullable = 1)
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ALTER COLUMN SourceFileHash CHAR(64) NOT NULL;');
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_ImportFile') AND name = 'SourceFileBytes' AND is_nullable = 1)
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ALTER COLUMN SourceFileBytes BIGINT NOT NULL;');
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNORM_ImportFile') AND name = 'RowCount' AND is_nullable = 1)
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ALTER COLUMN [RowCount] INT NOT NULL;');

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblNORM_ImportFile_Import' AND parent_object_id = OBJECT_ID('dbo.tblNORM_ImportFile'))
    ALTER TABLE dbo.tblNORM_ImportFile DROP CONSTRAINT UQ_tblNORM_ImportFile_Import;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblNORM_ImportFile_ImportSource' AND parent_object_id = OBJECT_ID('dbo.tblNORM_ImportFile'))
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ADD CONSTRAINT UQ_tblNORM_ImportFile_ImportSource UNIQUE (ImportId, SourceType);');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_tblNORM_ImportFile_SourceType' AND parent_object_id = OBJECT_ID('dbo.tblNORM_ImportFile'))
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ADD CONSTRAINT CK_tblNORM_ImportFile_SourceType CHECK (SourceType IN (''ERP'',''ROMAN''));');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_tblNORM_ImportFile_Period' AND parent_object_id = OBJECT_ID('dbo.tblNORM_ImportFile'))
    EXEC(N'ALTER TABLE dbo.tblNORM_ImportFile ADD CONSTRAINT CK_tblNORM_ImportFile_Period CHECK
        ((PeriodStart IS NULL AND PeriodEnd IS NULL) OR
         (PeriodStart BETWEEN 1 AND 16 AND PeriodEnd BETWEEN PeriodStart AND 16));');

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = 'CK_tblNORM_Import_SourceType'
      AND parent_object_id = OBJECT_ID('dbo.tblNORM_Import')
      AND definition NOT LIKE '%ROMAN+ERP%'
)
    ALTER TABLE dbo.tblNORM_Import DROP CONSTRAINT CK_tblNORM_Import_SourceType;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_tblNORM_Import_SourceType' AND parent_object_id = OBJECT_ID('dbo.tblNORM_Import'))
    ALTER TABLE dbo.tblNORM_Import ADD CONSTRAINT CK_tblNORM_Import_SourceType CHECK (SourceType IN ('ERP','ROMAN','ROMAN+ERP','TestBreak'));

IF OBJECT_ID('dbo.tblNORM_CalculationRun', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_CalculationRun (
        CalculationRunId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_CalculationRun PRIMARY KEY,
        RunGuid UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_tblNORM_CalculationRun_Guid DEFAULT (NEWSEQUENTIALID()),
        ImportId INT NOT NULL,
        ConfigurationReleaseId INT NOT NULL,
        InputFingerprint CHAR(64) NOT NULL,
        StatusCode VARCHAR(20) NOT NULL,
        StartedBy NVARCHAR(160) NOT NULL,
        StartedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_CalculationRun_StartedUtc DEFAULT (SYSUTCDATETIME()),
        CompletedUtc DATETIME2(3) NULL,
        FailureDetail NVARCHAR(2000) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_CalculationRun_IsDeactivated DEFAULT (0),
        CONSTRAINT CK_tblNORM_CalculationRun_Status CHECK (StatusCode IN ('Running','Complete','Failed')),
        CONSTRAINT UQ_tblNORM_CalculationRun_Guid UNIQUE (RunGuid)
    );
END;

IF OBJECT_ID('dbo.tblNORM_LineResult', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_LineResult (
        LineResultId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_LineResult PRIMARY KEY,
        CalculationRunId INT NOT NULL,
        StatementLineId INT NULL,
        StatementCode VARCHAR(20) NOT NULL,
        LineCode VARCHAR(120) NOT NULL,
        ComputedAmount DECIMAL(19,3) NOT NULL,
        PublishedAmount DECIMAL(19,3) NULL,
        Variance DECIMAL(19,3) NULL,
        StatusCode VARCHAR(20) NOT NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_LineResult_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_LineResult_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_tblNORM_LineResult_Status CHECK (StatusCode IN ('Tied','Close','Variance','Mapped','Unmapped')),
        CONSTRAINT UQ_tblNORM_LineResult UNIQUE (CalculationRunId, StatementCode, LineCode)
    );
END;

IF OBJECT_ID('dbo.tblNORM_Lineage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_Lineage (
        LineageId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_Lineage PRIMARY KEY,
        CalculationRunId INT NOT NULL,
        LineResultId BIGINT NOT NULL,
        TbRowId BIGINT NOT NULL,
        AccountMapId INT NULL,
        ConfigurationReleaseId INT NOT NULL,
        SourceAmount DECIMAL(19,2) NOT NULL,
        PresentedContribution DECIMAL(19,3) NOT NULL,
        DerivationCode VARCHAR(30) NOT NULL,
        MappingSnapshot NVARCHAR(1000) NOT NULL,
        AccountTypeSnapshot VARCHAR(20) NULL,
        StatementLineSnapshot NVARCHAR(240) NULL,
        NoteSubLineSnapshot NVARCHAR(240) NULL,
        CashFlowClassSnapshot NVARCHAR(120) NULL,
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_Lineage_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_tblNORM_Lineage UNIQUE (LineResultId, TbRowId)
    );
END;

IF OBJECT_ID('dbo.tblNORM_ValidationResult', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ValidationResult (
        ValidationResultId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_ValidationResult PRIMARY KEY,
        CalculationRunId INT NOT NULL,
        CheckCode VARCHAR(60) NOT NULL,
        CheckLabel NVARCHAR(240) NOT NULL,
        SeverityCode VARCHAR(20) NOT NULL,
        ResultCode VARCHAR(20) NOT NULL,
        ActualValue DECIMAL(19,3) NULL,
        ExpectedValue DECIMAL(19,3) NULL,
        DifferenceValue DECIMAL(19,3) NULL,
        ToleranceValue DECIMAL(19,3) NULL,
        DetailText NVARCHAR(1000) NOT NULL,
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_ValidationResult_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_tblNORM_ValidationResult_Severity CHECK (SeverityCode IN ('Blocking','Warning','Information')),
        CONSTRAINT CK_tblNORM_ValidationResult_Result CHECK (ResultCode IN ('Pass','Fail','Warning')),
        CONSTRAINT UQ_tblNORM_ValidationResult UNIQUE (CalculationRunId, CheckCode)
    );
END;

IF OBJECT_ID('dbo.tblNORM_AuditEvent', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_AuditEvent (
        AuditEventId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_AuditEvent PRIMARY KEY,
        EventCode VARCHAR(60) NOT NULL,
        EntityType VARCHAR(40) NOT NULL,
        EntityId VARCHAR(60) NOT NULL,
        DetailText NVARCHAR(2000) NULL,
        PerformedBy NVARCHAR(160) NOT NULL,
        PerformedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_AuditEvent_PerformedUtc DEFAULT (SYSUTCDATETIME())
    );
END;

/* Foreign keys are added only when absent, preserving safe reruns. */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_AccountMap_Release')
    ALTER TABLE dbo.tblNORM_AccountMap ADD CONSTRAINT FK_tblNORM_AccountMap_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_StatementLine_Release')
    ALTER TABLE dbo.tblNORM_StatementLine ADD CONSTRAINT FK_tblNORM_StatementLine_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_PublishedFigure_Release')
    ALTER TABLE dbo.tblNORM_PublishedFigure ADD CONSTRAINT FK_tblNORM_PublishedFigure_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Import_Release')
    ALTER TABLE dbo.tblNORM_Import ADD CONSTRAINT FK_tblNORM_Import_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Import_Parent')
    ALTER TABLE dbo.tblNORM_Import ADD CONSTRAINT FK_tblNORM_Import_Parent FOREIGN KEY (ParentImportId) REFERENCES dbo.tblNORM_Import(ImportId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_TbRow_Import')
    ALTER TABLE dbo.tblNORM_TrialBalanceRow ADD CONSTRAINT FK_tblNORM_TbRow_Import FOREIGN KEY (ImportId) REFERENCES dbo.tblNORM_Import(ImportId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_ImportFile_Import')
    ALTER TABLE dbo.tblNORM_ImportFile ADD CONSTRAINT FK_tblNORM_ImportFile_Import FOREIGN KEY (ImportId) REFERENCES dbo.tblNORM_Import(ImportId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Run_Import')
    ALTER TABLE dbo.tblNORM_CalculationRun ADD CONSTRAINT FK_tblNORM_Run_Import FOREIGN KEY (ImportId) REFERENCES dbo.tblNORM_Import(ImportId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Run_Release')
    ALTER TABLE dbo.tblNORM_CalculationRun ADD CONSTRAINT FK_tblNORM_Run_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Result_Run')
    ALTER TABLE dbo.tblNORM_LineResult ADD CONSTRAINT FK_tblNORM_Result_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Result_StatementLine')
    ALTER TABLE dbo.tblNORM_LineResult ADD CONSTRAINT FK_tblNORM_Result_StatementLine FOREIGN KEY (StatementLineId) REFERENCES dbo.tblNORM_StatementLine(StatementLineId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Lineage_Run')
    ALTER TABLE dbo.tblNORM_Lineage ADD CONSTRAINT FK_tblNORM_Lineage_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Lineage_Result')
    ALTER TABLE dbo.tblNORM_Lineage ADD CONSTRAINT FK_tblNORM_Lineage_Result FOREIGN KEY (LineResultId) REFERENCES dbo.tblNORM_LineResult(LineResultId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Lineage_TbRow')
    ALTER TABLE dbo.tblNORM_Lineage ADD CONSTRAINT FK_tblNORM_Lineage_TbRow FOREIGN KEY (TbRowId) REFERENCES dbo.tblNORM_TrialBalanceRow(TbRowId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Lineage_AccountMap')
    ALTER TABLE dbo.tblNORM_Lineage ADD CONSTRAINT FK_tblNORM_Lineage_AccountMap FOREIGN KEY (AccountMapId) REFERENCES dbo.tblNORM_AccountMap(AccountMapId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Lineage_Release')
    ALTER TABLE dbo.tblNORM_Lineage ADD CONSTRAINT FK_tblNORM_Lineage_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_Validation_Run')
    ALTER TABLE dbo.tblNORM_ValidationResult ADD CONSTRAINT FK_tblNORM_Validation_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblNORM_AccountMap_ReleaseGl' AND object_id = OBJECT_ID('dbo.tblNORM_AccountMap'))
    CREATE INDEX IX_tblNORM_AccountMap_ReleaseGl ON dbo.tblNORM_AccountMap(ConfigurationReleaseId, GlCode) INCLUDE (StatementLine, AccountType, NoteSubLine, CashFlowClass) WHERE IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblNORM_StatementLine_ReleaseStatement' AND object_id = OBJECT_ID('dbo.tblNORM_StatementLine'))
    CREATE INDEX IX_tblNORM_StatementLine_ReleaseStatement ON dbo.tblNORM_StatementLine(ConfigurationReleaseId, StatementCode, SeqNo) WHERE IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblNORM_Import_Latest' AND object_id = OBJECT_ID('dbo.tblNORM_Import'))
    CREATE INDEX IX_tblNORM_Import_Latest ON dbo.tblNORM_Import(FinancialYear, EntityCode, ImportId DESC) INCLUDE (StatusCode, SourceFileName, [RowCount], NetBalance) WHERE IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblNORM_TbRow_ImportGl' AND object_id = OBJECT_ID('dbo.tblNORM_TrialBalanceRow'))
    CREATE INDEX IX_tblNORM_TbRow_ImportGl ON dbo.tblNORM_TrialBalanceRow(ImportId, GlAccount) INCLUDE (AccumBalance, SourceLedger, GlText) WHERE IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblNORM_Run_Import' AND object_id = OBJECT_ID('dbo.tblNORM_CalculationRun'))
    CREATE INDEX IX_tblNORM_Run_Import ON dbo.tblNORM_CalculationRun(ImportId, CalculationRunId DESC) INCLUDE (StatusCode, CompletedUtc) WHERE IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblNORM_Lineage_Result' AND object_id = OBJECT_ID('dbo.tblNORM_Lineage'))
    CREATE INDEX IX_tblNORM_Lineage_Result ON dbo.tblNORM_Lineage(LineResultId) INCLUDE (TbRowId, AccountMapId, PresentedContribution);

PRINT 'NORM proof-engine schema is ready.';
