/*======================================================================================
  NORM statement-demo enhancements
  Run after NORM_04_GovernmentReportingPlatform.sql.

  Adds controlled run-level inputs for:
    - Original Budget figures and their source references
    - manual note / reconciliation inputs
    - cash-flow classification journals

  The tables deliberately do not seed financial amounts. A blank value is presented as a
  controlled gap, not as a fabricated figure. The reporting workspace seeds only the input
  register rows needed to demonstrate the preparation workflow.
======================================================================================*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_BudgetFigure', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_BudgetFigure
    (
        BudgetFigureId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_BudgetFigure PRIMARY KEY,
        CalculationRunId     INT NOT NULL,
        StatementCode        VARCHAR(20) NOT NULL,
        LineCode             NVARCHAR(240) NOT NULL,
        OriginalBudget       DECIMAL(19,3) NULL,
        SourceSystem         NVARCHAR(100) NULL,
        SourceReference      NVARCHAR(500) NULL,
        StatusCode           VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_BudgetFigure_Status DEFAULT ('Loaded'),
        UpdatedBy            NVARCHAR(256) NOT NULL,
        UpdatedUtc           DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_BudgetFigure_Updated DEFAULT (SYSUTCDATETIME()),
        IsDeactivated        BIT NOT NULL CONSTRAINT DF_tblNORM_BudgetFigure_Deactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_BudgetFigure UNIQUE (CalculationRunId,StatementCode,LineCode),
        CONSTRAINT FK_tblNORM_BudgetFigure_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId),
        CONSTRAINT CK_tblNORM_BudgetFigure_Status CHECK (StatusCode IN ('Loaded','Prepared','Validated'))
    );
END
GO

IF OBJECT_ID('dbo.tblNORM_ManualInput', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ManualInput
    (
        ManualInputId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_ManualInput PRIMARY KEY,
        CalculationRunId     INT NOT NULL,
        InputCode            VARCHAR(50) NOT NULL,
        DisclosureCode       VARCHAR(40) NULL,
        InputLabel           NVARCHAR(300) NOT NULL,
        InputTypeCode        VARCHAR(30) NOT NULL CONSTRAINT DF_tblNORM_ManualInput_Type DEFAULT ('Disclosure'),
        AmountCurrent        DECIMAL(19,3) NULL,
        AmountPrior          DECIMAL(19,3) NULL,
        ReconcileLineCode    NVARCHAR(240) NULL,
        EvidenceReference    NVARCHAR(500) NULL,
        Commentary           NVARCHAR(2000) NULL,
        StatusCode           VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_ManualInput_Status DEFAULT ('NotStarted'),
        UpdatedBy            NVARCHAR(256) NOT NULL,
        UpdatedUtc           DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_ManualInput_Updated DEFAULT (SYSUTCDATETIME()),
        IsDeactivated        BIT NOT NULL CONSTRAINT DF_tblNORM_ManualInput_Deactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_ManualInput UNIQUE (CalculationRunId,InputCode),
        CONSTRAINT FK_tblNORM_ManualInput_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId),
        CONSTRAINT CK_tblNORM_ManualInput_Status CHECK (StatusCode IN ('NotStarted','Draft','Prepared','Validated'))
    );
END
GO

IF OBJECT_ID('dbo.tblNORM_CashFlowJournal', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_CashFlowJournal
    (
        CashFlowJournalId    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_CashFlowJournal PRIMARY KEY,
        CalculationRunId     INT NOT NULL,
        JournalReference     NVARCHAR(100) NOT NULL,
        JournalDescription   NVARCHAR(500) NOT NULL,
        CashFlowClass        NVARCHAR(200) NOT NULL,
        Amount               DECIMAL(19,3) NOT NULL,
        EvidenceReference    NVARCHAR(500) NULL,
        StatusCode           VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_CashFlowJournal_Status DEFAULT ('Draft'),
        UpdatedBy            NVARCHAR(256) NOT NULL,
        UpdatedUtc           DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_CashFlowJournal_Updated DEFAULT (SYSUTCDATETIME()),
        IsDeactivated        BIT NOT NULL CONSTRAINT DF_tblNORM_CashFlowJournal_Deactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_CashFlowJournal UNIQUE (CalculationRunId,JournalReference),
        CONSTRAINT FK_tblNORM_CashFlowJournal_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId),
        CONSTRAINT CK_tblNORM_CashFlowJournal_Status CHECK (StatusCode IN ('Draft','Prepared','Approved','Posted'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_tblNORM_ManualInput_RunStatus' AND object_id=OBJECT_ID('dbo.tblNORM_ManualInput'))
    CREATE INDEX IX_tblNORM_ManualInput_RunStatus ON dbo.tblNORM_ManualInput(CalculationRunId,StatusCode) INCLUDE (DisclosureCode,AmountCurrent,ReconcileLineCode) WHERE IsDeactivated=0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_tblNORM_CashFlowJournal_RunClass' AND object_id=OBJECT_ID('dbo.tblNORM_CashFlowJournal'))
    CREATE INDEX IX_tblNORM_CashFlowJournal_RunClass ON dbo.tblNORM_CashFlowJournal(CalculationRunId,CashFlowClass) INCLUDE (Amount,StatusCode) WHERE IsDeactivated=0;
GO

PRINT 'NORM statement-demo enhancement objects are ready.';
GO
