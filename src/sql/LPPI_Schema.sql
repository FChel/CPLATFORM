/* =============================================================================
   LPPI Review — PRODUCTION schema
   File: LPPI_Schema.sql
   Database: CPlatform
   -----------------------------------------------------------------------------
   Idempotent: safe to re-run. Each object is guarded by an existence
   check; reason-code seed inserts only codes that do not already exist.

   Access model:
     Reviewer page  = token-based (no Windows identity check). Two token
                      types: AS Fin token (full package, can finalise) and
                      POC token (POC-scoped view, no finalise).
     Everything else = gated by tblLPPI_AdminUsers.
     Admin           = full access to all LPPI admin pages and actions.
     Non-admin       = LPPI_Review.aspx only (via token link received by email).

   Run order on a fresh database:
     1. LPPI_Drop.sql        (DEV / UAT reset only — NOT for PROD)
     2. LPPI_Schema.sql      (this file)
     3. LPPI_AdminSeed.sql   (set usernames in that file before running)
   ============================================================================= */

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [CPlatform];
GO

/* =============================================================================
   1. LOOKUP / CONFIG TABLES
      ------------------------
      Created first; no inbound FKs. Order is alphabetical for readability.
   ============================================================================= */

/* ----------------------------- tblLPPI_AdminUsers ---------------------------
   Admin access list. Reviewer page is unaffected (uses tokens, not identity).
   UserId matched case-insensitively by the application.
   Deactivation (IsActive = 0) is preferred over hard delete for audit trail.
   Seed: see LPPI_AdminSeed.sql.
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_AdminUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_AdminUsers
    (
        AdminUserID  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_AdminUsers PRIMARY KEY CLUSTERED,
        UserId       NVARCHAR(100)  NOT NULL,
        IsActive     BIT            NOT NULL CONSTRAINT DF_tblLPPI_AdminUsers_IsActive DEFAULT (1),
        CreatedDate  DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_AdminUsers_CreatedDate DEFAULT (SYSDATETIME()),
        ModifiedDate DATETIME2(3)   NULL,
        CreatedBy    NVARCHAR(100)  NULL,
        CONSTRAINT UQ_tblLPPI_AdminUsers_UserId UNIQUE (UserId)
    );
END
GO

/* ----------------------------- tblLPPI_CapabilityManagers -------------------
   One row per Capability Manager program (e.g. ARMY, NAVY).

   Email / EmailDisplayName: the AS Fin team mailbox for this CM.
     - TO recipient on the AS Fin review email.
     - From address on POC review emails (so POC replies land in the
       AS Fin team mailbox by design — they want to field "why am I
       getting this" questions directly).
   Both nullable. The application gate refuses to send / mark-as-sent
   unless both are populated. Format check (defence-in-depth) below; the
   application enforces a stricter regex with a useful error message.
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_CapabilityManagers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_CapabilityManagers
    (
        CmID              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_CapabilityManagers PRIMARY KEY CLUSTERED,
        Program           NVARCHAR(200)  NOT NULL,
        Email             NVARCHAR(200)  NULL,
        EmailDisplayName  NVARCHAR(200)  NULL,
        IsActive          BIT            NOT NULL CONSTRAINT DF_tblLPPI_CapabilityManagers_IsActive DEFAULT (1),
        CreatedDate       DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_CapabilityManagers_CreatedDate DEFAULT (SYSDATETIME()),
        ModifiedDate      DATETIME2(3)   NULL,
        CONSTRAINT UQ_tblLPPI_CapabilityManagers_Program UNIQUE (Program),
        CONSTRAINT CK_tblLPPI_CapabilityManagers_Email
            CHECK (Email IS NULL
                OR (Email LIKE '%_@_%.[a-z]%'
                    AND (Email LIKE '%@defence.gov.au'
                      OR Email LIKE '%@%.defence.gov.au'
                      OR Email LIKE '%@annpsr.gov.au'
                      OR Email LIKE '%@%.annpsr.gov.au')))
    );
END
GO

/* ----------------------------- tblLPPI_ReasonCodes --------------------------
   Reviewer-facing reason codes with Outcome (Payable / NotPayable).
   Seed at the bottom of this file inserts the 16 canonical RC01-RC16, plus
   RC-RL (Reload-eligible / incorrect data) and the system code RC-NR
   (inactive — set automatically on AS Fin finalise).
   ============================================================================= */
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

/* =============================================================================
   2. CORE DATA TABLES
      ----------------
      Documents (one row per LINE), batches, packages, exports.
   ============================================================================= */

/* ----------------------------- tblLPPI_LoadBatches --------------------------
   One row per file load. Source bookkeeping for provenance.
   ============================================================================= */
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

/* ----------------------------- tblLPPI_ExportBatches ------------------------
   Header table for ERP payment-file generations. One row per Generate
   payment file click; rows in tblLPPI_Documents and tblLPPI_ReviewPackages
   point back via ExportBatchID.

   FileBytes / FileSizeBytes / ContentType: the generated xlsx is stored
   here so admins can re-download a past export without regenerating it.
   varbinary(max) keeps everything self-contained in the DB; no filesystem
   permissions needed. LPPI volumes are tens-to-hundreds of payments per
   run so the storage cost is trivial.
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_ExportBatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_ExportBatches
    (
        ExportBatchID    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_ExportBatches PRIMARY KEY CLUSTERED,
        FileName         NVARCHAR(260)  NOT NULL,
        GeneratedDate    DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_ExportBatches_GeneratedDate DEFAULT (SYSDATETIME()),
        GeneratedByUser  NVARCHAR(100)  NULL,
        GeneratedByName  NVARCHAR(200)  NULL,
        PackageCount     INT            NOT NULL CONSTRAINT DF_tblLPPI_ExportBatches_PackageCount DEFAULT (0),
        DocumentCount    INT            NOT NULL CONSTRAINT DF_tblLPPI_ExportBatches_DocumentCount DEFAULT (0),
        LineCount        INT            NOT NULL CONSTRAINT DF_tblLPPI_ExportBatches_LineCount DEFAULT (0),
        TotalAmount      DECIMAL(18,2)  NOT NULL CONSTRAINT DF_tblLPPI_ExportBatches_TotalAmount DEFAULT (0),
        FileBytes        VARBINARY(MAX) NULL,
        FileSizeBytes    INT            NULL,
        ContentType      NVARCHAR(200)  NULL,
        Notes            NVARCHAR(MAX)  NULL
    );
END
GO

/* ----------------------------- tblLPPI_Documents ----------------------------
   One row per LINE. BODS supplies ITEM_SEQUENCE so a single DocNoAccounting
   may have many lines. The reviewer codes the DOCUMENT once (review row
   stored against the smallest-ItemSequence DocumentID), and joins inherit
   that code at read time.

   ExportedDate / ExportedBy / ExportBatchID are populated when the line is
   shipped in an ERP payment file. NULL = not (yet) exported.

   IsDeactivated / SupersededByDocumentID: support the RC-RL reload-eligible
   workflow. When a document is deactivated (incorrect data, needs reload),
   IsDeactivated is set to 1 and SupersededByDocumentID points to the
   replacement DocumentID once the corrected line arrives in a later load.
   ============================================================================= */
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
        ExportBatchID               INT           NULL,
        IsDeactivated               BIT           NOT NULL CONSTRAINT DF_tblLPPI_Documents_IsDeactivated DEFAULT (0),
        SupersededByDocumentID      INT           NULL,
        /* Uniqueness on (DocNoAccounting, ItemSequence) is enforced by a
           FILTERED unique index over live rows only — see the
           UX_tblLPPI_Documents_Live_DocNoAccounting_ItemSequence definition
           after this CREATE TABLE block. The RC-RL reload-eligible workflow
           keeps the deactivated predecessor row in place while the corrected
           replacement loads, so a hard constraint here would block reloads. */
        CONSTRAINT FK_tblLPPI_Documents_Batch        FOREIGN KEY (BatchID)                REFERENCES dbo.tblLPPI_LoadBatches(BatchID),
        CONSTRAINT FK_tblLPPI_Documents_ExportBatch  FOREIGN KEY (ExportBatchID)          REFERENCES dbo.tblLPPI_ExportBatches(ExportBatchID),
        CONSTRAINT FK_tblLPPI_Documents_SupersededBy FOREIGN KEY (SupersededByDocumentID) REFERENCES dbo.tblLPPI_Documents(DocumentID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_BatchID
        ON dbo.tblLPPI_Documents(BatchID);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_CapabilityManagerProgram
        ON dbo.tblLPPI_Documents(CapabilityManagerProgram);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_ExportedDate
        ON dbo.tblLPPI_Documents(ExportedDate);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_ExportBatchID
        ON dbo.tblLPPI_Documents(ExportBatchID)
        WHERE ExportBatchID IS NOT NULL;

    /* PocEmail index — supports the per-POC document filter on the reviewer
       page (POC token resolves to a PocEmail; reviewer query filters by it)
       and the per-POC outstanding-count subquery used by reminder builds. */
    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_PocEmail
        ON dbo.tblLPPI_Documents(PocEmail)
        WHERE PocEmail IS NOT NULL;

    /* Deactivated index — supports the deactivated-watchlist admin page and
       the load-time supersession lookup. Filtered to the small subset of
       rows where IsDeactivated = 1, so it stays cheap on the hot path. */
    CREATE NONCLUSTERED INDEX IX_tblLPPI_Documents_Deactivated
        ON dbo.tblLPPI_Documents(DocNoAccounting, ItemSequence)
        INCLUDE (DocumentID, SupersededByDocumentID, CapabilityManagerProgram)
        WHERE IsDeactivated = 1;

    /* Filtered unique index — replaces the hard
       UQ_tblLPPI_Documents_DocNoAccounting_ItemSequence constraint. Enforces
       "at most one LIVE row per (DocNoAccounting, ItemSequence)". Deactivated
       history rows are exempt from the uniqueness check, allowing the
       RC-RL reload workflow to keep the deactivated predecessor in place
       while the corrected replacement loads. */
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblLPPI_Documents_Live_DocNoAccounting_ItemSequence
        ON dbo.tblLPPI_Documents(DocNoAccounting, ItemSequence)
     WHERE IsDeactivated = 0;
END
GO

/* ----------------------------- tblLPPI_ReviewPackages -----------------------
   Status lifecycle (driven entirely by app code):

     'NotSent'   — created at file-load time; reviewer link works but nobody
                   has been notified. Reviewer page is editable for admin QA.
                   Document set may still grow on subsequent loads.
     'Sent'      — initial email dispatched. Document set frozen. Reminders
                   allowed.
     'InReview'  — at least one document has a reason code. Reminders allowed.
     'Finalised' — AS Fin clicked Finalise on the reviewer page. Form fields
                   locked. Reversible — Unfinalise wipes auto-applied RC-NR
                   rows and returns to InReview.
     'Exported'  — admin included the package in an ERP export run. Terminal
                   — no further changes. Form fields locked.
     'Cancelled' — admin-cancelled side branch. ClosedDate is set. Documents
                   in this package become eligible for repackaging on the
                   next load.

   ClosedDate stamps the moment a package becomes terminal — Cancelled or
   Exported flows set it; not used for Finalised (which is reversible).

   FinalisedBy: Windows display name of whoever clicked Finalise. Captured
   even though the reviewer page is token-gated (IIS Windows auth still
   provides identity context).
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_ReviewPackages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_ReviewPackages
    (
        PackageID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_ReviewPackages PRIMARY KEY CLUSTERED,
        CmID           INT            NOT NULL,
        Token          NVARCHAR(100)  NOT NULL,
        CreatedDate    DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_ReviewPackages_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy      NVARCHAR(200)  NULL,
        DueDate        DATETIME2(3)   NOT NULL,
        SentDate       DATETIME2(3)   NULL,
        ClosedDate     DATETIME2(3)   NULL,
        FinalisedDate  DATETIME2(3)   NULL,
        FinalisedBy    NVARCHAR(200)  NULL,
        ExportBatchID  INT            NULL,
        Status         NVARCHAR(20)   NOT NULL CONSTRAINT DF_tblLPPI_ReviewPackages_Status DEFAULT ('NotSent'),
        Notes          NVARCHAR(MAX)  NULL,
        CONSTRAINT UQ_tblLPPI_ReviewPackages_Token UNIQUE (Token),
        CONSTRAINT FK_tblLPPI_ReviewPackages_Cm          FOREIGN KEY (CmID)          REFERENCES dbo.tblLPPI_CapabilityManagers(CmID),
        CONSTRAINT FK_tblLPPI_ReviewPackages_ExportBatch FOREIGN KEY (ExportBatchID) REFERENCES dbo.tblLPPI_ExportBatches(ExportBatchID),
        CONSTRAINT CK_tblLPPI_ReviewPackages_Status
            CHECK (Status IN ('NotSent','Sent','InReview','Finalised','Exported','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewPackages_CmID
        ON dbo.tblLPPI_ReviewPackages(CmID);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewPackages_Status
        ON dbo.tblLPPI_ReviewPackages(Status);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewPackages_ExportBatchID
        ON dbo.tblLPPI_ReviewPackages(ExportBatchID)
        WHERE ExportBatchID IS NOT NULL;
END
GO

/* ----------------------------- tblLPPI_PackagePocs --------------------------
   One row per (PackageID, PocEmail) pair. Each POC referenced by a
   document in the package gets their own unguessable Token, used to
   build a per-POC reviewer URL that filters the page to docs assigned
   to that POC's email.

   Populated by the file-load reconcile (LPPIFileParser.ReconcilePocs)
   for any package in NotSent status. Once a package transitions to
   Sent, its POC set is frozen alongside its document set — new POCs
   on subsequent loads do not retroactively get added to a Sent
   package.

   InitialSentDate / LastReminderDate mirror the package-level audit
   so the Send-outs page can show per-POC contact history if needed.
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_PackagePocs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_PackagePocs
    (
        PackagePocID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_PackagePocs PRIMARY KEY CLUSTERED,
        PackageID         INT            NOT NULL,
        PocEmail          NVARCHAR(200)  NOT NULL,
        Token             NVARCHAR(100)  NOT NULL,
        CreatedDate       DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_PackagePocs_CreatedDate DEFAULT (SYSDATETIME()),
        InitialSentDate   DATETIME2(3)   NULL,
        LastReminderDate  DATETIME2(3)   NULL,
        CONSTRAINT UQ_tblLPPI_PackagePocs_PackagePoc UNIQUE (PackageID, PocEmail),
        CONSTRAINT UQ_tblLPPI_PackagePocs_Token      UNIQUE (Token),
        CONSTRAINT FK_tblLPPI_PackagePocs_Package    FOREIGN KEY (PackageID) REFERENCES dbo.tblLPPI_ReviewPackages(PackageID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_PackagePocs_PackageID
        ON dbo.tblLPPI_PackagePocs(PackageID);
END
GO

/* ----------------------------- tblLPPI_ReviewPackageDocuments ---------------
   Many-to-one link from packages to documents. PK is the composite
   (PackageID, DocumentID). DocumentID points to the package-time first-line
   id for that document — every line of the document inherits the
   package's review via that linkage.
   ============================================================================= */
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

/* ----------------------------- tblLPPI_Reviews ------------------------------
   Latest-state-only table. One row per document (UQ on DocumentID).
   ReviewedDate doubles as the optimistic-locking version token — the save
   handler reads it on load, posts it back on save, and refuses the update
   if it has changed in between.
   ============================================================================= */
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

/* ----------------------------- tblLPPI_ReviewHistory ------------------------
   Append-only audit log of review changes. One row per Save click that
   actually changed something for a given document. Snapshot model — each
   row captures the new state at that point in time.

   The first row for a DocumentID is the initial review. Every subsequent
   row is an update — reconstructable via LAG(...) OVER (PARTITION BY
   DocumentID ORDER BY ChangedDate) when reporting.

   Finalise / unfinalise also write history rows: finalise inserts an RC-NR
   row for every auto-applied review; unfinalise inserts a NULL-reason-code
   row for every review it wipes.
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_ReviewHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_ReviewHistory
    (
        HistoryID            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_ReviewHistory PRIMARY KEY CLUSTERED,
        DocumentID           INT             NOT NULL,
        PackageID            INT             NOT NULL,
        ReasonCodeID         INT             NULL,
        Comments             NVARCHAR(MAX)   NULL,
        ObjectiveReference   NVARCHAR(200)   NULL,
        ChangedByUserId      NVARCHAR(100)   NULL,
        ChangedByName        NVARCHAR(200)   NULL,
        ChangedDate          DATETIME2(3)    NOT NULL CONSTRAINT DF_tblLPPI_ReviewHistory_ChangedDate DEFAULT (SYSDATETIME()),
        CONSTRAINT FK_tblLPPI_ReviewHistory_Document   FOREIGN KEY (DocumentID)   REFERENCES dbo.tblLPPI_Documents(DocumentID),
        CONSTRAINT FK_tblLPPI_ReviewHistory_Package    FOREIGN KEY (PackageID)    REFERENCES dbo.tblLPPI_ReviewPackages(PackageID),
        CONSTRAINT FK_tblLPPI_ReviewHistory_ReasonCode FOREIGN KEY (ReasonCodeID) REFERENCES dbo.tblLPPI_ReasonCodes(ReasonCodeID)
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewHistory_DocumentID
        ON dbo.tblLPPI_ReviewHistory(DocumentID, ChangedDate DESC);

    CREATE NONCLUSTERED INDEX IX_tblLPPI_ReviewHistory_PackageID
        ON dbo.tblLPPI_ReviewHistory(PackageID, ChangedDate DESC);
END
GO

/* ----------------------------- tblLPPI_EmailLog -----------------------------
   Audit log of every email send (real or mark-as-sent simulated).

   Audience: 'ASFIN' = the per-package send to the CM's AS Fin team
   mailbox; 'POC' = a per-POC send (one row per POC dispatched);
   'CONTROL' = the vendor-of-interest heads-up sent to the contract
   manager mailbox when a package contains docs from a vendor in
   LPPI.ControlVendorNumbers; 'NOTIFY' = the admin-initiated
   "Notify AS Fin" email sent on Finalised packages, capturing the
   package summary for visibility to the responsible AS Fin officer.

   RecipientEmail keeps the full "to | CC: ... | BCC: ..." string for
   AS Fin sends; for POC sends it is just the single TO address.
   ============================================================================= */
IF OBJECT_ID(N'dbo.tblLPPI_EmailLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblLPPI_EmailLog
    (
        EmailLogID     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblLPPI_EmailLog PRIMARY KEY CLUSTERED,
        PackageID      INT            NULL,
        RecipientEmail NVARCHAR(500)  NOT NULL,
        EmailType      NVARCHAR(40)   NOT NULL,
        Audience       NVARCHAR(10)   NULL,
        PocEmail       NVARCHAR(200)  NULL,
        Subject        NVARCHAR(500)  NULL,
        Body           NVARCHAR(MAX)  NULL,
        SentDate       DATETIME2(3)   NOT NULL CONSTRAINT DF_tblLPPI_EmailLog_SentDate DEFAULT (SYSDATETIME()),
        SentBy         NVARCHAR(200)  NULL,
        Success        BIT            NOT NULL CONSTRAINT DF_tblLPPI_EmailLog_Success DEFAULT (0),
        ErrorMessage   NVARCHAR(MAX)  NULL,
        CONSTRAINT FK_tblLPPI_EmailLog_Package FOREIGN KEY (PackageID) REFERENCES dbo.tblLPPI_ReviewPackages(PackageID),
        CONSTRAINT CK_tblLPPI_EmailLog_Audience
            CHECK (Audience IS NULL OR Audience IN ('ASFIN','POC','CONTROL','NOTIFY'))
    );

    CREATE NONCLUSTERED INDEX IX_tblLPPI_EmailLog_PackageID
        ON dbo.tblLPPI_EmailLog(PackageID);
END
GO

/* =============================================================================
   3. SEED DATA — REASON CODES
      ------------------------
      The 16 canonical RC01-RC16 codes from the RMG-417 LPPI process, plus
      RC-RL (Reload-eligible / incorrect data) and the system code RC-NR used
      for auto-applied "no response" reviews written when AS Fin clicks Finalise.

      Re-runnable: only inserts codes that do not already exist. Existing
      rows are NOT updated by the seed — operations team can edit codes
      via the Reason Codes admin page.
   ============================================================================= */
;WITH Seed(Code, Description, Outcome, DisplayOrder, RequiresComments, IsActive) AS
(
    SELECT 'RC01',  N'Interest Payable – ERP Technical/Migration/Access or other ERP related issues',                                          'Payable',      1, 0, 1 UNION ALL
    SELECT 'RC02',  N'Interest Payable – POC issues (incorrect/unavailable)',                                                                   'Payable',      2, 0, 1 UNION ALL
    SELECT 'RC03',  N'Interest Payable – Problems with Purchase Order',                                                                         'Payable',      3, 0, 1 UNION ALL
    SELECT 'RC04',  N'Interest Payable – Problems with Account Assignment (cost centre, WBS etc)',                                              'Payable',      4, 0, 1 UNION ALL
    SELECT 'RC05',  N'Interest Payable – Account payable processing delays',                                                                    'Payable',      5, 0, 1 UNION ALL
    SELECT 'RC06',  N'Interest Payable – Incorrect Baseline date used in calculation',                                                          'Payable',      6, 0, 1 UNION ALL
    SELECT 'RC07',  N'Interest Payable – Other',                                                                                                'Payable',      7, 1, 1 UNION ALL
    SELECT 'RC08',  N'Interest Not Payable – Contract older than RMG 417 Key date (1 July 2022)',                                               'NotPayable',   8, 0, 1 UNION ALL
    SELECT 'RC09',  N'Interest Not Payable – Goods not received when invoiced',                                                                 'NotPayable',   9, 0, 1 UNION ALL
    SELECT 'RC10',  N'Interest Not Payable – Goods not accepted (broken / faulty)',                                                             'NotPayable',  10, 0, 1 UNION ALL
    SELECT 'RC11',  N'Interest Not Payable – Invoice submitted prior to delivery of goods / services',                                          'NotPayable',  11, 0, 1 UNION ALL
    SELECT 'RC12',  N'Interest Not Payable – Delayed due to invoice dispute',                                                                   'NotPayable',  12, 0, 1 UNION ALL
    SELECT 'RC13',  N'Interest Not Payable – Commonwealth or State entity',                                                                     'NotPayable',  13, 0, 1 UNION ALL
    SELECT 'RC14',  N'Interest Not Payable – It''s a lease, Forex or GST Invoice',                                                             'NotPayable',  14, 0, 1 UNION ALL
    SELECT 'RC15',  N'Interest Not Payable – Services delivered overseas',                                                                      'NotPayable',  15, 0, 1 UNION ALL
    SELECT 'RC16',  N'Interest Not Payable – Other',                                                                                            'NotPayable',  16, 1, 1 UNION ALL
    /* RC-RL — Reload-eligible. Outcome is NotPayable (the line, as it stands,
       is not payable). RequiresComments = 1 (justification mandatory; the
       existing NotPayable validation also requires an Objective Reference).
       IsActive = 1 — visible in the reviewer dropdown for both POC and AS Fin. */
    SELECT 'RC-RL', N'Interest Not Payable – Incorrect data, document eligible for reload (e.g. baseline date dispute, line-level error)',      'NotPayable',  17, 1, 1 UNION ALL
    /* RC-NR — system code, IsActive=0 so it does not appear in the reviewer
       dropdown. Looked up by Code at runtime by the finalise flow and applied
       to any document that has not been coded by the AS Fin team at finalise
       time. Outcome = Payable per RMG-417 default position. */
    SELECT 'RC-NR', N'Interest Payable – Default per RMG-417 (no review decision recorded at finalise)',                                       'Payable',   9999, 0, 0
)
INSERT INTO dbo.tblLPPI_ReasonCodes (Code, Description, Outcome, DisplayOrder, RequiresComments, IsActive)
SELECT s.Code, s.Description, s.Outcome, s.DisplayOrder, s.RequiresComments, s.IsActive
FROM Seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblLPPI_ReasonCodes rc WHERE rc.Code = s.Code);
GO

PRINT 'LPPI_Schema.sql complete.';
GO
