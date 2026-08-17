/* NORM 08 - start-of-financial-year setup and retained comparative/budget documents.
   Idempotent. Source documents are retained in SQL and never served directly by IIS. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.tblNORM_YearSetup','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_YearSetup
    (
        YearSetupId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_YearSetup PRIMARY KEY,
        EntityCode           VARCHAR(20) NOT NULL,
        CurrentFinancialYear INT NOT NULL,
        IsCurrent            BIT NOT NULL CONSTRAINT DF_tblNORM_YearSetup_Current DEFAULT(1),
        UpdatedBy            NVARCHAR(256) NOT NULL,
        UpdatedUtc           DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_YearSetup_Updated DEFAULT(SYSUTCDATETIME()),
        IsDeactivated        BIT NOT NULL CONSTRAINT DF_tblNORM_YearSetup_Deactivated DEFAULT(0),
        CONSTRAINT CK_tblNORM_YearSetup_Year CHECK(CurrentFinancialYear BETWEEN 1900 AND 2999),
        CONSTRAINT UQ_tblNORM_YearSetup_EntityYear UNIQUE(EntityCode,CurrentFinancialYear)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblNORM_YearSetup') AND name='UX_tblNORM_YearSetup_Current')
    CREATE UNIQUE INDEX UX_tblNORM_YearSetup_Current ON dbo.tblNORM_YearSetup(EntityCode)
    WHERE IsCurrent=1 AND IsDeactivated=0;

IF OBJECT_ID('dbo.tblNORM_YearSetupDocument','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_YearSetupDocument
    (
        YearSetupDocumentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_YearSetupDocument PRIMARY KEY,
        YearSetupId         INT NOT NULL,
        DocumentTypeCode    VARCHAR(40) NOT NULL,
        SourceFileName      NVARCHAR(260) NOT NULL,
        SourceFileExtension VARCHAR(10) NOT NULL,
        SourceFileHash      CHAR(64) NOT NULL,
        SourceFileBytes     BIGINT NOT NULL,
        FileContent         VARBINARY(MAX) NOT NULL,
        ExtractionStatus    VARCHAR(30) NOT NULL CONSTRAINT DF_tblNORM_YearSetupDocument_Status DEFAULT('Loaded'),
        DetectedStart       NVARCHAR(300) NULL,
        ExtractedFigureCount INT NOT NULL CONSTRAINT DF_tblNORM_YearSetupDocument_Figures DEFAULT(0),
        ExtractionDetail    NVARCHAR(1000) NULL,
        UploadedBy          NVARCHAR(256) NOT NULL,
        UploadedUtc         DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_YearSetupDocument_Uploaded DEFAULT(SYSUTCDATETIME()),
        IsDeactivated       BIT NOT NULL CONSTRAINT DF_tblNORM_YearSetupDocument_Deactivated DEFAULT(0),
        CONSTRAINT FK_tblNORM_YearSetupDocument_Setup FOREIGN KEY(YearSetupId) REFERENCES dbo.tblNORM_YearSetup(YearSetupId),
        CONSTRAINT CK_tblNORM_YearSetupDocument_Type CHECK(DocumentTypeCode IN ('PriorYearFinancialStatements','PortfolioBudgetStatements')),
        CONSTRAINT CK_tblNORM_YearSetupDocument_Status CHECK(ExtractionStatus IN ('Loaded','Extracted','ReviewRequired','Failed'))
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblNORM_YearSetupDocument') AND name='IX_tblNORM_YearSetupDocument_SetupType')
    CREATE INDEX IX_tblNORM_YearSetupDocument_SetupType ON dbo.tblNORM_YearSetupDocument(YearSetupId,DocumentTypeCode,IsDeactivated,UploadedUtc DESC);

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_YearSetupFigure
    (
        YearSetupFigureId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_YearSetupFigure PRIMARY KEY,
        YearSetupDocumentId BIGINT NOT NULL,
        FigureType          VARCHAR(30) NOT NULL,
        StatementCode       VARCHAR(20) NOT NULL,
        LineCode            NVARCHAR(240) NOT NULL,
        LineLabel           NVARCHAR(300) NOT NULL,
        Amount              DECIMAL(19,3) NOT NULL,
        SourceLocator       NVARCHAR(300) NULL,
        MatchConfidence     DECIMAL(5,2) NOT NULL,
        ReviewStatus        VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_YearSetupFigure_Review DEFAULT('AutoMatched'),
        CreatedUtc          DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_YearSetupFigure_Created DEFAULT(SYSUTCDATETIME()),
        IsDeactivated       BIT NOT NULL CONSTRAINT DF_tblNORM_YearSetupFigure_Deactivated DEFAULT(0),
        CONSTRAINT FK_tblNORM_YearSetupFigure_Document FOREIGN KEY(YearSetupDocumentId) REFERENCES dbo.tblNORM_YearSetupDocument(YearSetupDocumentId),
        CONSTRAINT CK_tblNORM_YearSetupFigure_Type CHECK(FigureType IN ('PriorActual','OriginalBudget')),
        CONSTRAINT CK_tblNORM_YearSetupFigure_Review CHECK(ReviewStatus IN ('AutoMatched','ReviewRequired','Confirmed')),
        CONSTRAINT UQ_tblNORM_YearSetupFigure_Line UNIQUE(YearSetupDocumentId,StatementCode,LineCode)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblNORM_YearSetupFigure') AND name='IX_tblNORM_YearSetupFigure_Document')
    CREATE INDEX IX_tblNORM_YearSetupFigure_Document ON dbo.tblNORM_YearSetupFigure(YearSetupDocumentId,StatementCode,ReviewStatus) INCLUDE(LineCode,Amount,MatchConfidence);

PRINT 'NORM start-of-financial-year setup objects are ready.';
