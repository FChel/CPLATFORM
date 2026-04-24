/* =============================================================================
   LPPI Review — schema create script
   Database: CPlatform
   All objects prefixed tblLPPI_ to avoid colliding with existing tblCC_*.
   Idempotent: safe to re-run. Each object is guarded by an existence check.

   Access model:
     Reviewer page  = token-based (no Windows identity check).
     Everything else = gated by tblLPPI_AdminUsers.
     Admin           = full access to all LPPI admin pages and actions.
     Non-admin       = LPPI_Review.aspx only (via token link received by email).

   Run order:
     1. LPPI_Drop.sql       (DEV / UAT reset only)
     2. LPPI_Schema.sql     (this file)
     3. LPPI_AdminSeed.sql  (set usernames in that file before running)
   ============================================================================= */

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [CPlatform];
GO

/* ----------------------------- tblLPPI_LoadBatches -------------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_LoadBatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_LoadBatches
    (
        BatchID            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_LoadBatches PRIMARY KEY CLUSTERED,
        FileName           NVARCHAR(260)  NOT NULL,
        SourcePath         NVARCHAR(500)  NULL,
        FileSizeBytes      BIGINT         NULL,
        FileModifiedDate   DATETIME2(3)   NULL,
        LoadedDate         DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_LoadBatches_LoadedDate DEFAULT (SYSDATETIME()),
        LoadedByUserId     NVARCHAR(100)  NULL,
        LoadedByName       NVARCHAR(200)  NULL,
        RowsInFile         INT            NOT NULL CONSTRAINT DF_tblLPPI_LoadBatches_RowsInFile DEFAULT (0),
        RowsInserted       INT            NOT NULL CONSTRAINT DF_tblLPPI_LoadBatches_RowsInserted DEFAULT (0),
        RowsSkipped        INT            NOT NULL CONSTRAINT DF_tblLPPI_LoadBatches_RowsSkipped DEFAULT (0),
        RowsFailed         INT            NOT NULL CONSTRAINT DF_tblLPPI_LoadBatches_RowsFailed DEFAULT (0),
        Notes              NVARCHAR(MAX)  NULL
    );
END
GO

/* ----------------------------- tblLPPI_Documents ---------------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_Documents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_Documents
    (
        DocumentID                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_Documents PRIMARY KEY CLUSTERED,
        DocNoAccounting             NVARCHAR(50)  NOT NULL,
        ItemSequence                INT           NOT NULL,
        BatchID                     INT           NOT NULL,
        CompanyCode                 NVARCHAR(20)  NULL,
        PoNumber                    NVARCHAR(50)  NULL,
        VendorNum                   NVARCHAR(50)  NULL,
        VendorName                  NVARCHAR(200) NULL,
        VendorAcct                  NVARCHAR(50)  NULL,
        WbsElement                  NVARCHAR(50)  NULL,
        WbsDesc                     NVARCHAR(200) NULL,
        Capex                       NVARCHAR(20)  NULL,
        ProfitCentre                NVARCHAR(50)  NULL,
        CapabilityManager           NVARCHAR(50)  NULL,
        CapabilityManagerName       NVARCHAR(200) NULL,
        CapabilityManagerProgram    NVARCHAR(200) NULL,
        DeliveryManager             NVARCHAR(50)  NULL,
        DeliveryManagerName         NVARCHAR(200) NULL,
        DeliveryManagerProgram      NVARCHAR(200) NULL,
        PocEmail                    NVARCHAR(200) NULL,
        GlAccount                   NVARCHAR(50)  NULL,
        TaxCode                     NVARCHAR(10)  NULL,
        ContractNo                  NVARCHAR(50)  NULL,
        VimDocumentId               NVARCHAR(50)  NULL,
        InvoiceReceivedDate         DATETIME2(3)  NULL,
        InvoiceDate                 DATETIME2(3)  NULL,
        GrCreateDateLatest          DATETIME2(3)  NULL,
        Currency                    NVARCHAR(10)  NULL,
        GlLineValueInclGst          DECIMAL(19,4) NULL,
        InvoiceValueInclGst         DECIMAL(19,4) NULL,
        PaymentTerms                NVARCHAR(20)  NULL,
        MaterialPo                  NVARCHAR(10)  NULL,
        ExclusionFlag               NVARCHAR(50)  NULL,
        ExclusionTest               NVARCHAR(500) NULL,
        ExclusionDescriptor         NVARCHAR(500) NULL,
        PossiblePayment             NVARCHAR(20)  NULL,
        PossibleDuplicateClearing   NVARCHAR(20)  NULL,
        ContractValueLocExGst       DECIMAL(19,4) NULL,
        PaymentRunDate              DATETIME2(3)  NULL,
        BodsPaymtBaselineDate       DATETIME2(3)  NULL,
        DaysVariance                INT           NULL,
        DailyRate                   DECIMAL(19,8) NULL,
        InvoiceInterestAmount       DECIMAL(19,4) NULL,
        InterestPayable             DECIMAL(19,4) NULL,
        SourceSystem                NVARCHAR(20)  NULL,
        PaymentChannel              NVARCHAR(20)  NULL,
        DocumentType                NVARCHAR(20)  NULL,
        VendorInvoiceNo             NVARCHAR(100) NULL,
        ClearingMonth               NVARCHAR(20)  NULL,
        FiscalYear                  NVARCHAR(10)  NULL,
        FirstSeenDate               DATETIME2(3)  NOT NULL CONSTRAINT DF_tblLPPI_Documents_FirstSeenDate DEFAULT (SYSDATETIME()),
        ExportedDate                DATETIME2(3)  NULL,
        ExportedBy                  NVARCHAR(200) NULL,
        CONSTRAINT UQ_tblLPPI_Documents_DocNoAccounting_ItemSequence UNIQUE (DocNoAccounting, ItemSequence),
        CONSTRAINT FK_tblLPPI_Documents_Batch FOREIGN KEY (BatchID) REFERENCES dbo.tblLPPI_LoadBatches(BatchID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_BatchID
        ON dbo.tblLPPI_Documents(BatchID);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_CapabilityManagerProgram
        ON dbo.tblLPPI_Documents(CapabilityManagerProgram);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_ExportedDate
        ON dbo.tblLPPI_Documents(ExportedDate);
END
GO

/* ----------------------------- tblLPPI_ReasonCodes -------------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_ReasonCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_ReasonCodes
    (
        ReasonCodeID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_ReasonCodes PRIMARY KEY CLUSTERED,
        Code              NVARCHAR(20)   NOT NULL,
        Description       NVARCHAR(500)  NOT NULL,
        Outcome           NVARCHAR(20)   NOT NULL,
        DisplayOrder      INT            NOT NULL CONSTRAINT DF_tblLPPI_ReasonCodes_DisplayOrder DEFAULT (0),
        RequiresComments  BIT            NOT NULL CONSTRAINT DF_tblLPPI_ReasonCodes_RequiresComments DEFAULT (0),
        IsActive          BIT            NOT NULL CONSTRAINT DF_tblLPPI_ReasonCodes_IsActive DEFAULT (1),
        CONSTRAINT UQ_tblLPPI_ReasonCodes_Code UNIQUE (Code),
        CONSTRAINT CK_tblLPPI_ReasonCodes_Outcome CHECK (Outcome IN ('Payable','NotPayable'))
    );
END
GO

/* ----------------------------- tblLPPI_Reviews ------------------------------ */
IF OBJECT_ID(N'dbo.tblLPPI_Reviews', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_Reviews
    (
        ReviewID            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_Reviews PRIMARY KEY CLUSTERED,
        DocumentID          INT             NOT NULL,
        ReasonCodeID        INT             NULL,
        Comments            NVARCHAR(MAX)   NULL,
        ObjectiveReference  NVARCHAR(200)   NULL,
        ReviewedByUserId    NVARCHAR(100)   NULL,
        ReviewedByName      NVARCHAR(200)   NULL,
        ReviewedDate        DATETIME2(3)    NULL,
        IsFinal             BIT             NOT NULL CONSTRAINT DF_tblLPPI_Reviews_IsFinal DEFAULT (0),
        CONSTRAINT UQ_tblLPPI_Reviews_DocumentID UNIQUE (DocumentID),
        CONSTRAINT FK_tblLPPI_Reviews_Document   FOREIGN KEY (DocumentID)   REFERENCES dbo.tblLPPI_Documents(DocumentID),
        CONSTRAINT FK_tblLPPI_Reviews_ReasonCode FOREIGN KEY (ReasonCodeID) REFERENCES dbo.tblLPPI_ReasonCodes(ReasonCodeID)
    );
END
GO

/* ----------------------------- tblLPPI_CapabilityManagers ------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_CapabilityManagers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_CapabilityManagers
    (
        CmID          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_CapabilityManagers PRIMARY KEY CLUSTERED,
        Program       NVARCHAR(200)  NOT NULL,
        DisplayName   NVARCHAR(200)  NULL,
        IsActive      BIT            NOT NULL CONSTRAINT DF_tblLPPI_CapabilityManagers_IsActive DEFAULT (1),
        CreatedDate   DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_CapabilityManagers_CreatedDate DEFAULT (SYSDATETIME()),
        ModifiedDate  DATETIME2(3)   NULL,
        CONSTRAINT UQ_tblLPPI_CapabilityManagers_Program UNIQUE (Program)
    );
END
GO

/* ----------------------------- tblLPPI_CapabilityManagerEmails -------------- */
IF OBJECT_ID(N'dbo.tblLPPI_CapabilityManagerEmails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_CapabilityManagerEmails
    (
        CmEmailID    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_CapabilityManagerEmails PRIMARY KEY CLUSTERED,
        CmID         INT            NOT NULL,
        Email        NVARCHAR(200)  NOT NULL,
        DisplayName  NVARCHAR(200)  NULL,
        IsCC         BIT            NOT NULL CONSTRAINT DF_tblLPPI_CapabilityManagerEmails_IsCC DEFAULT (0),
        CreatedDate  DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_CapabilityManagerEmails_CreatedDate DEFAULT (SYSDATETIME()),
        CONSTRAINT FK_tblLPPI_CapabilityManagerEmails_Cm FOREIGN KEY (CmID) REFERENCES dbo.tblLPPI_CapabilityManagers(CmID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_CapabilityManagerEmails_CmID
        ON dbo.tblLPPI_CapabilityManagerEmails(CmID);
END
GO

/* ----------------------------- tblLPPI_ReviewPackages ----------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_ReviewPackages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_ReviewPackages
    (
        PackageID    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_ReviewPackages PRIMARY KEY CLUSTERED,
        CmID         INT            NOT NULL,
        Token        NVARCHAR(100)  NOT NULL,
        CreatedDate  DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_ReviewPackages_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy    NVARCHAR(200)  NULL,
        DueDate      DATETIME2(3)   NOT NULL,
        ClosedDate   DATETIME2(3)   NULL,
        Status       NVARCHAR(20)   NOT NULL CONSTRAINT DF_tblLPPI_ReviewPackages_Status DEFAULT ('Open'),
        Notes        NVARCHAR(MAX)  NULL,
        CONSTRAINT UQ_tblLPPI_ReviewPackages_Token UNIQUE (Token),
        CONSTRAINT FK_tblLPPI_ReviewPackages_Cm FOREIGN KEY (CmID) REFERENCES dbo.tblLPPI_CapabilityManagers(CmID),
        CONSTRAINT CK_tblLPPI_ReviewPackages_Status CHECK (Status IN ('Open','Closed','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewPackages_CmID
        ON dbo.tblLPPI_ReviewPackages(CmID);
END
GO

/* ----------------------------- tblLPPI_ReviewPackageDocuments --------------- */
IF OBJECT_ID(N'dbo.tblLPPI_ReviewPackageDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_ReviewPackageDocuments
    (
        PackageID   INT NOT NULL,
        DocumentID  INT NOT NULL,
        AddedDate   DATETIME2(3) NOT NULL CONSTRAINT DF_tblLPPI_ReviewPackageDocuments_AddedDate DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_tblLPPI_ReviewPackageDocuments PRIMARY KEY CLUSTERED (PackageID, DocumentID),
        CONSTRAINT FK_tblLPPI_ReviewPackageDocuments_Package  FOREIGN KEY (PackageID)  REFERENCES dbo.tblLPPI_ReviewPackages(PackageID),
        CONSTRAINT FK_tblLPPI_ReviewPackageDocuments_Document FOREIGN KEY (DocumentID) REFERENCES dbo.tblLPPI_Documents(DocumentID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewPackageDocuments_DocumentID
        ON dbo.tblLPPI_ReviewPackageDocuments(DocumentID);
END
GO

/* ----------------------------- tblLPPI_EmailLog ----------------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_EmailLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_EmailLog
    (
        EmailLogID     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_EmailLog PRIMARY KEY CLUSTERED,
        PackageID      INT            NULL,
        RecipientEmail NVARCHAR(500)  NOT NULL,
        EmailType      NVARCHAR(20)   NOT NULL,
        Subject        NVARCHAR(500)  NULL,
        Body           NVARCHAR(MAX)  NULL,
        SentDate       DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_EmailLog_SentDate DEFAULT (SYSDATETIME()),
        SentBy         NVARCHAR(200)  NULL,
        Success        BIT            NOT NULL CONSTRAINT DF_tblLPPI_EmailLog_Success DEFAULT (0),
        ErrorMessage   NVARCHAR(MAX)  NULL,
        CONSTRAINT FK_tblLPPI_EmailLog_Package FOREIGN KEY (PackageID) REFERENCES dbo.tblLPPI_ReviewPackages(PackageID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_EmailLog_PackageID
        ON dbo.tblLPPI_EmailLog(PackageID);
END
GO

/* ----------------------------- tblLPPI_AdminUsers ---------------------------
   Access model:
     Reviewer page  = token-based (no Windows identity check).
     Everything else = gated by this table.
     Admin           = full access to all LPPI admin pages and actions.
     Non-admin       = LPPI_Review.aspx only (via token link received by email).

   Seeding: run LPPI_AdminSeed.sql after this script.
   UserId is matched case-insensitively by the application.
   Deactivation (IsActive = 0) is preferred over hard delete for audit trail.
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblLPPI_AdminUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_AdminUsers
    (
        AdminUserID  INT           IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tblLPPI_AdminUsers PRIMARY KEY CLUSTERED,
        UserId       NVARCHAR(100) NOT NULL,
        DisplayName  NVARCHAR(200) NULL,
        Email        NVARCHAR(200) NULL,
        IsActive     BIT           NOT NULL
            CONSTRAINT DF_tblLPPI_AdminUsers_IsActive DEFAULT (1),
        CreatedDate  DATETIME2(3)  NOT NULL
            CONSTRAINT DF_tblLPPI_AdminUsers_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy    NVARCHAR(200) NULL,
        ModifiedDate DATETIME2(3)  NULL,
        CONSTRAINT UQ_tblLPPI_AdminUsers_UserId UNIQUE (UserId)
    );
END
GO

/* ============================================================================
   Seed data — reason codes (16 canonical codes from RMG 417 LPPI process)
   Re-runnable: only inserts codes that do not already exist.
   ============================================================================ */
;WITH Seed(Code, Description, Outcome, DisplayOrder, RequiresComments) AS
(
    SELECT 'RC01', N'Interest Payable – ERP Technical/Migration/Access or other ERP related issues', 'Payable',     1, 0 UNION ALL
    SELECT 'RC02', N'Interest Payable – POC issues (incorrect/unavailable)',                          'Payable',     2, 0 UNION ALL
    SELECT 'RC03', N'Interest Payable – Problems with Purchase Order',                                 'Payable',     3, 0 UNION ALL
    SELECT 'RC04', N'Interest Payable – Problems with Account Assignment (cost centre, WBS etc)',     'Payable',     4, 0 UNION ALL
    SELECT 'RC05', N'Interest Payable – Account payable processing delays',                            'Payable',     5, 0 UNION ALL
    SELECT 'RC06', N'Interest Payable – Incorrect Baseline date used in calculation',                  'Payable',     6, 0 UNION ALL
    SELECT 'RC07', N'Interest Payable – Other',                                                        'Payable',     7, 1 UNION ALL
    SELECT 'RC08', N'Interest Not Payable – Contract older than RMG 417 Key date (1 July 2022)',      'NotPayable',  8, 0 UNION ALL
    SELECT 'RC09', N'Interest Not Payable – Goods not received when invoiced',                         'NotPayable',  9, 0 UNION ALL
    SELECT 'RC10', N'Interest Not Payable – Goods not accepted (broken / faulty)',                     'NotPayable', 10, 0 UNION ALL
    SELECT 'RC11', N'Interest Not Payable – Invoice submitted prior to delivery of goods / services',  'NotPayable', 11, 0 UNION ALL
    SELECT 'RC12', N'Interest Not Payable – Delayed due to invoice dispute',                           'NotPayable', 12, 0 UNION ALL
    SELECT 'RC13', N'Interest Not Payable – Commonwealth or State entity',                             'NotPayable', 13, 0 UNION ALL
    SELECT 'RC14', N'Interest Not Payable – It''s a lease, Forex or GST Invoice',                     'NotPayable', 14, 0 UNION ALL
    SELECT 'RC15', N'Interest Not Payable – Services delivered overseas',                              'NotPayable', 15, 0 UNION ALL
    SELECT 'RC16', N'Interest Not Payable – Other',                                                    'NotPayable', 16, 1
)
INSERT INTO dbo.tblLPPI_ReasonCodes (Code, Description, Outcome, DisplayOrder, RequiresComments, IsActive)
SELECT s.Code, s.Description, s.Outcome, s.DisplayOrder, s.RequiresComments, 1
FROM Seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblLPPI_ReasonCodes rc WHERE rc.Code = s.Code);
GO

PRINT 'LPPI_Schema.sql complete.';
GO
