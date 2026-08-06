/*======================================================================================
  Prepayment Management Dashboard
  Database Schema  -  SQL Server (T-SQL)

  File        : 01_Schema_PrepaymentManagement.sql
  Purpose     : Creates every dbo.tblPPM_* table required by the dashboard (Tabs 1-7) in
                the existing CPlatform database, matching the tblLPPI_* convention already
                used by the LPPI Review module. The [prepayment] schema is retained only
                for this module's stored procedures/functions (02_/04_-10_), not tables.
                Schema + relationships only - NO seed data.
  Source      : Documentation/prepayment-requirements-v2.docx (System Requirements v1.0)
                README.md + Documentation/Prepayment_Flow_Diagram.md
  Target      : SQL Server 2016+

  Conventions (applied to EVERY table)
  ------------------------------------
  * Surrogate PK            : [Id] BIGINT IDENTITY(1,1) PRIMARY KEY.
                              EXCEPTION: dbo.tblPPM_AppUser.Id is INT (see below).
  * Natural/business keys    : kept as UNIQUE columns (e.g. PoNumber, GlAccount), not the PK.
  * Foreign keys             : reference the parent table's [Id]; the FK column's data type
                              MUST match the referenced PK's type.
  * Audit columns (all tables):
        CreatedBy     INT       NOT NULL  (FK -> dbo.tblPPM_AppUser.Id)
        CreatedDate   DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
        ModifiedBy    INT       NULL      (FK -> dbo.tblPPM_AppUser.Id)
        ModifiedDate  DATETIME2(3) NULL
        IsDeleted     BIT       NOT NULL DEFAULT(0)   -- soft-delete flag
  * Because the audit columns are INT and they FK to AppUser, dbo.tblPPM_AppUser.Id is INT.
    Consequently EVERY column that references a user (CreatedBy, ModifiedBy, PreparerUserId,
    ApproverUserId, FlaggedByUserId, ActionByUserId, AssignedToUserId) is INT. All other
    surrogate keys / FKs are BIGINT.
  * AppUser is created first; its own CreatedBy/ModifiedBy are plain INT with NO
    self-referencing FK (avoids a bootstrap/insert ordering problem on the first user).
  * Money = NUMERIC(18,2); single currency per PO (Assumption #7).
  * CHECK constraints mirror the requirement wording so the DB rejects bad values.
  * Tab 8 (Email Workflow) is intentionally OUT OF SCOPE - no email tables.

  NOTE: re-running is safe - every CREATE TABLE below is guarded by
        IF OBJECT_ID(...) IS NULL, so existing tables and data are left untouched (see 0).
======================================================================================*/

SET NOCOUNT ON;
-- Required for the PERSISTED computed column on dbo.tblPPM_Reconciliation (Variance).
-- Without these, CREATE TABLE fails with Msg 1934 under clients that default them OFF (e.g. sqlcmd).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*--------------------------------------------------------------------------------------
  0. Database + schema
--------------------------------------------------------------------------------------*/
USE [CPlatform];
GO

-- Retained for this module's stored procedures/functions (02_/04_-10_), which stay in the
-- [prepayment] schema; tables below live in [dbo] per the tblPPM_* convention.
IF SCHEMA_ID(N'prepayment') IS NULL
    EXEC(N'CREATE SCHEMA [prepayment] AUTHORIZATION [dbo];');
GO

/*======================================================================================
  1. REFERENCE / MASTER DATA
======================================================================================*/

/*--------------------------------------------------------------------------------------
  1.1  AppUser - created first because every other table's audit FKs point here.
        Id is INT (matches the INT CreatedBy/ModifiedBy audit columns).
        Its own audit columns are plain INT (no self-FK) to allow the first insert.
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_AppUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_AppUser
    (
        Id               INT            NOT NULL IDENTITY(1,1),
        WindowsAccount   NVARCHAR(150)  NOT NULL,             -- 'ADVITIYA\nihal.mali'
        DisplayName      NVARCHAR(150)  NULL,
        Email            NVARCHAR(256)  NULL,
        RoleName         VARCHAR(30)    NOT NULL DEFAULT('FinancePreparer'), -- 5 roles
        IsActive         BIT            NOT NULL DEFAULT(1),
        -- audit (no self-referencing FK on purpose)
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_AppUser PRIMARY KEY (Id),
        CONSTRAINT UQ_AppUser_Account UNIQUE (WindowsAccount),
        CONSTRAINT CK_AppUser_Role CHECK
            (RoleName IN ('FinancePreparer','FinanceApprover','Admin','ReadOnly'))
    );
END
GO

/*--------------------------------------------------------------------------------------
  1.2  CompanyCode - SAP company codes (e.g. 1000)
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_CompanyCode', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_CompanyCode
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        CompanyCode      VARCHAR(4)     NOT NULL,             -- '1000'
        CompanyName      NVARCHAR(150)  NULL,
        CurrencyCode     CHAR(3)        NOT NULL DEFAULT('AUD'),
        IsActive         BIT            NOT NULL DEFAULT(1),
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_CompanyCode PRIMARY KEY (Id),
        CONSTRAINT UQ_CompanyCode UNIQUE (CompanyCode),
        CONSTRAINT FK_CompanyCode_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_CompanyCode_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*--------------------------------------------------------------------------------------
  1.3  Division - DIV-001..DIV-010 (10 divisions)  2.4
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_Division', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_Division
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        DivisionCode     VARCHAR(10)    NOT NULL,             -- 'DIV-001'
        DivisionName     NVARCHAR(150)  NOT NULL,
        IsActive         BIT            NOT NULL DEFAULT(1),
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_Division PRIMARY KEY (Id),
        CONSTRAINT UQ_Division UNIQUE (DivisionCode),
        CONSTRAINT FK_Division_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Division_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*--------------------------------------------------------------------------------------
  1.4  PrepaymentGlAccount - the dedicated prepayment GLs (514xxx, from the real ERP feed)  2.5
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_PrepaymentGlAccount', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_PrepaymentGlAccount
    (
        Id                   BIGINT         NOT NULL IDENTITY(1,1),
        GlAccount            VARCHAR(10)    NOT NULL,         -- '514004'..'514110'
        GlDescription        NVARCHAR(200)  NOT NULL,
        AssetClassification  VARCHAR(15)    NOT NULL,         -- 'Current' | 'Non-current'
        ExpenditureType      VARCHAR(15)    NOT NULL,         -- 'Operational' | 'Capital' | 'Lease'
        AasbReference        NVARCHAR(60)   NULL,
        Notes                NVARCHAR(1000) NULL,
        IsActive             BIT            NOT NULL DEFAULT(1),
        CreatedBy            INT            NOT NULL,
        CreatedDate          DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy           INT            NULL,
        ModifiedDate         DATETIME2(3)   NULL,
        IsDeleted            BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_PrepaymentGlAccount PRIMARY KEY (Id),
        CONSTRAINT UQ_PrepaymentGlAccount UNIQUE (GlAccount),
        CONSTRAINT CK_PrepayGl_Asset CHECK (AssetClassification IN ('Current','Non-current')),
        CONSTRAINT CK_PrepayGl_Exp   CHECK (ExpenditureType    IN ('Operational','Capital','Lease')),
        CONSTRAINT FK_PrepayGl_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_PrepayGl_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*--------------------------------------------------------------------------------------
  1.5  Vendor
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_Vendor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_Vendor
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        VendorCode       VARCHAR(20)    NULL,                 -- SAP vendor code
        VendorName       NVARCHAR(200)  NOT NULL,
        IsActive         BIT            NOT NULL DEFAULT(1),
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_Vendor PRIMARY KEY (Id),
        CONSTRAINT UQ_Vendor_Name UNIQUE (VendorName),
        CONSTRAINT FK_Vendor_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Vendor_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*--------------------------------------------------------------------------------------
  1.5b  Manager - Capability Manager + Delivery Manager master (from the real ERP feed).
        Each PO line carries a CAPABILITY_MGR_ID and a DELIVERY_MGR_ID that resolve here.
        Id is the SAP manager id (e.g. 50000003), used directly as the natural PK.
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_Manager', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_Manager
    (
        Id               BIGINT         NOT NULL,             -- SAP manager id (e.g. 50000003)
        ManagerDesc      NVARCHAR(150)  NULL,                 -- 'Portfolio -GST'
        Program          NVARCHAR(200)  NULL,                 -- 'DEFENCE CORPORATE'
        IsActive         BIT            NOT NULL DEFAULT(1),
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_Manager PRIMARY KEY (Id),
        CONSTRAINT FK_Manager_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Manager_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*--------------------------------------------------------------------------------------
  1.6  DeliveryGroup - delivery groups; each -> division, preparer, approver, GL, company  2.4
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_DeliveryGroup', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_DeliveryGroup
    (
        Id                 BIGINT         NOT NULL IDENTITY(1,1),
        DeliveryGroupCode  VARCHAR(20)    NOT NULL,           -- 'DIG', 'Air Force', 'DPG - MPO'
        GroupName          NVARCHAR(150)  NOT NULL,
        DivisionId         BIGINT         NULL,               -- FK -> Division
        PreparerUserId     INT            NULL,               -- FK -> AppUser
        ApproverUserId     INT            NULL,               -- FK -> AppUser
        PrepaymentGlId     BIGINT         NULL,               -- FK -> PrepaymentGlAccount (group's prepay GL)
        CompanyCodeId      BIGINT         NULL,               -- FK -> CompanyCode
        IsActive           BIT            NOT NULL DEFAULT(1),
        CreatedBy          INT            NOT NULL,
        CreatedDate        DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy         INT            NULL,
        ModifiedDate       DATETIME2(3)   NULL,
        IsDeleted          BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_DeliveryGroup PRIMARY KEY (Id),
        CONSTRAINT UQ_DeliveryGroup UNIQUE (DeliveryGroupCode),
        CONSTRAINT FK_DG_Division   FOREIGN KEY (DivisionId)     REFERENCES dbo.tblPPM_Division(Id),
        CONSTRAINT FK_DG_Preparer   FOREIGN KEY (PreparerUserId) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_DG_Approver   FOREIGN KEY (ApproverUserId) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_DG_Gl         FOREIGN KEY (PrepaymentGlId) REFERENCES dbo.tblPPM_PrepaymentGlAccount(Id),
        CONSTRAINT FK_DG_Company    FOREIGN KEY (CompanyCodeId)  REFERENCES dbo.tblPPM_CompanyCode(Id),
        CONSTRAINT FK_DG_CreatedBy  FOREIGN KEY (CreatedBy)      REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_DG_ModifiedBy FOREIGN KEY (ModifiedBy)     REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*======================================================================================
  2. SOURCE A - PURCHASE ORDERS & DELIVERY SCHEDULE LINES   (Tab 1)
======================================================================================*/

/*--------------------------------------------------------------------------------------
  2.1  PurchaseOrder (header)  6 Purchase Order
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_PurchaseOrder', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_PurchaseOrder
    (
        Id                 BIGINT         NOT NULL IDENTITY(1,1),
        PoNumber           VARCHAR(20)    NOT NULL,           -- '3000077540', '4501195094'
        VendorId           BIGINT         NULL,               -- FK -> Vendor
        DeliveryGroupId    BIGINT         NULL,               -- FK -> DeliveryGroup
        ProjectCode        VARCHAR(30)    NULL,               -- 'PRJ-2026-041' (legacy; real feed uses WBS)
        TotalValue         NUMERIC(18,2)  NOT NULL DEFAULT(0), -- = TotalCommitment (kept for back-compat)
        CurrentCommitment  NUMERIC(18,2)  NULL,               -- ERP CURRENT_COMMITMENT (open/remaining)
        TotalCommitment    NUMERIC(18,2)  NULL,               -- ERP TOTAL_COMMITMENT
        CapexOpex          VARCHAR(5)     NULL,               -- 'CAPEX' | 'OPEX'
        CapabilityMgrId    BIGINT         NULL,               -- FK -> Manager (CAPABILITY_MGR_ID)
        DeliveryMgrId      BIGINT         NULL,               -- FK -> Manager (DELIVERY_MGR_ID)
        GrIndicator        VARCHAR(3)     NULL,               -- GR_INDICATOR ('X')
        IrIndicator        VARCHAR(3)     NULL,               -- IR_INDICATOR ('X')
        ProcessControl     VARCHAR(30)    NULL,               -- 'GR Based IV'
        SourceSystem       VARCHAR(10)    NULL,               -- 'ERP' | 'ECC'
        CurrencyCode       CHAR(3)        NOT NULL DEFAULT('AUD'),
        PoDate             DATE           NULL,
        CompanyCodeId      BIGINT         NULL,               -- FK -> CompanyCode
        LinesCount         INT            NOT NULL DEFAULT(0),
        SourceLoadDate     DATE           NULL,               -- daily SQL load date
        CreatedBy          INT            NOT NULL,
        CreatedDate        DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy         INT            NULL,
        ModifiedDate       DATETIME2(3)   NULL,
        IsDeleted          BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_PurchaseOrder PRIMARY KEY (Id),
        CONSTRAINT UQ_PurchaseOrder UNIQUE (PoNumber),
        CONSTRAINT FK_PO_Vendor     FOREIGN KEY (VendorId)        REFERENCES dbo.tblPPM_Vendor(Id),
        CONSTRAINT FK_PO_DG         FOREIGN KEY (DeliveryGroupId) REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_PO_Company    FOREIGN KEY (CompanyCodeId)   REFERENCES dbo.tblPPM_CompanyCode(Id),
        CONSTRAINT FK_PO_CapMgr     FOREIGN KEY (CapabilityMgrId) REFERENCES dbo.tblPPM_Manager(Id),
        CONSTRAINT FK_PO_DelMgr     FOREIGN KEY (DeliveryMgrId)   REFERENCES dbo.tblPPM_Manager(Id),
        CONSTRAINT CK_PO_CapexOpex  CHECK (CapexOpex IS NULL OR CapexOpex IN ('CAPEX','OPEX')),
        CONSTRAINT FK_PO_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_PO_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id)
    );

    CREATE NONCLUSTERED INDEX IX_PO_DeliveryGroup ON dbo.tblPPM_PurchaseOrder(DeliveryGroupId);
    CREATE NONCLUSTERED INDEX IX_PO_Vendor        ON dbo.tblPPM_PurchaseOrder(VendorId);
END
GO

/*--------------------------------------------------------------------------------------
  2.2  PoDeliveryLine (delivery schedule line - holds the prepayment flag)  6
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_PoDeliveryLine', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_PoDeliveryLine
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        PurchaseOrderId  BIGINT         NOT NULL,             -- FK -> PurchaseOrder
        LineNumber       INT            NOT NULL,             -- DOC_ITEM: 10, 20, 30 ...
        AcctAssignNumber INT            NOT NULL DEFAULT(1),  -- ACCT_ASSIGN_NUMBER (part of line identity)
        Description      NVARCHAR(300)  NULL,
        ServiceNote      NVARCHAR(300)  NULL,                 -- 'Service: 01 Jul 2026 - 30 Jun 2027'
        GlAccount        VARCHAR(10)    NULL,                 -- ORIGINAL expense GL on the PO line (ERP_GL_ACCOUNT)
        GlDescription    NVARCHAR(200)  NULL,                 -- ERP_GL_DESCRIPTION
        WbsCostCentre    VARCHAR(40)    NULL,                 -- WBS_ELEMENT / ERP_COST_CENTRE
        WbsDescription   NVARCHAR(200)  NULL,                 -- WBS_DESCRIPTION
        CapexOpex        VARCHAR(5)     NULL,                 -- 'CAPEX' | 'OPEX'
        ScheduledDate    DATE           NULL,                 -- EINDT
        Quantity         NUMERIC(18,3)  NULL,                 -- SCHEDULE_QUANTITY
        OpenQuantity     NUMERIC(18,3)  NULL,                 -- OPEN_QUANTITY
        UnitPrice        NUMERIC(18,2)  NULL,
        LineValue        NUMERIC(18,2)  NULL,                 -- Qty x UnitPrice
        PrepaymentFlag   VARCHAR(15)    NOT NULL DEFAULT('Pending'), -- Prepayment|NotPrepayment|Pending
        FlagNote         NVARCHAR(300)  NULL,
        FlaggedByUserId  INT            NULL,                 -- FK -> AppUser (who set the flag)
        FlaggedDate      DATETIME2(3)   NULL,
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_PoDeliveryLine PRIMARY KEY (Id),
        -- Real line identity is (PO, DOC_ITEM, ACCT_ASSIGN_NUMBER): the same PO+item can repeat
        -- across account assignments / delivery groups, so AcctAssignNumber is part of the key.
        CONSTRAINT UQ_PoDeliveryLine UNIQUE (PurchaseOrderId, LineNumber, AcctAssignNumber),
        CONSTRAINT FK_PoLine_PO         FOREIGN KEY (PurchaseOrderId) REFERENCES dbo.tblPPM_PurchaseOrder(Id),
        CONSTRAINT FK_PoLine_FlaggedBy  FOREIGN KEY (FlaggedByUserId) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_PoLine_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_PoLine_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_PoLine_Flag CHECK (PrepaymentFlag IN ('Prepayment','NotPrepayment','Pending'))
    );

    CREATE NONCLUSTERED INDEX IX_PoLine_Flag ON dbo.tblPPM_PoDeliveryLine(PrepaymentFlag) INCLUDE (PurchaseOrderId);
END
GO

/*======================================================================================
  3. SOURCE B - INVOICES (GR/IR activity on prepayment-flagged lines)   (Tab 2)
======================================================================================*/
IF OBJECT_ID(N'dbo.tblPPM_Invoice', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_Invoice
    (
        Id                BIGINT         NOT NULL IDENTITY(1,1),
        InvoiceNo         VARCHAR(30)    NOT NULL,            -- 'INV-20260601-001'
        PurchaseOrderId   BIGINT         NULL,               -- FK -> PurchaseOrder
        PoDeliveryLineId  BIGINT         NULL,               -- FK -> PoDeliveryLine (matched line)
        VendorId          BIGINT         NULL,               -- FK -> Vendor
        GlAccount         VARCHAR(10)    NULL,               -- PREPAYMENT_GL_ACCOUNT (514xxx the invoice posts to)
        PrepaymentGlDesc  NVARCHAR(200)  NULL,               -- PREPAYMENT_GL_DESCRIPTION
        CashGlAccount     VARCHAR(10)    NULL,               -- CASH_GL_ACCOUNT (offset/expense GL)
        CashGlDescription NVARCHAR(200)  NULL,               -- CASH_GL_DESCRIPTION
        ProfitCentre      VARCHAR(20)    NULL,               -- PROFIT_CENTRE
        ProfitCentreDesc  NVARCHAR(200)  NULL,               -- PROFIT_CENTRE_DESCRIPTION
        WbsElement        VARCHAR(40)    NULL,               -- WBS_ELEMENT
        WbsDescription    NVARCHAR(200)  NULL,               -- WBS_DESCRIPTION
        CapexOpex         VARCHAR(5)     NULL,               -- 'CAPEX' | 'OPEX'
        InvoiceDate       DATE           NULL,               -- POST_DATE (posting date in SAP)
        PostFiscalYear    SMALLINT       NULL,               -- POST_FY
        PostFiscalPeriod  TINYINT        NULL,               -- POST_FP (1..12 / 13..16)
        PaymentRunDate    DATE           NULL,               -- PAYMENT_RUN_DATE
        Amount            NUMERIC(18,2)  NOT NULL DEFAULT(0), -- AUD/local amount (GL_AMOUNT_LC)
        AmountDoc         NUMERIC(18,2)  NULL,               -- document/foreign amount (GL_AMOUNT_DC)
        FxRate            NUMERIC(18,8)  NULL,               -- foreign->AUD rate (LC/DC when DC<>0)
        ForeignCurrency   CHAR(3)        NULL,               -- transaction currency when foreign
        CurrencyCode      CHAR(3)        NOT NULL DEFAULT('AUD'),
        Description       NVARCHAR(300)  NULL,
        Flag              VARCHAR(20)    NOT NULL DEFAULT('Prepayment'), -- Prepayment|UnderReview|NotPrepayment
        SetupStatus       VARCHAR(25)    NOT NULL DEFAULT('Pending'),
        IsExistingBalance BIT            NOT NULL DEFAULT(0), -- 1 = pre-loaded from Source C
        SourceSystem      VARCHAR(10)    NULL,               -- 'ERP' | 'ECC'
        SourceLoadDate    DATE           NULL,
        CreatedBy         INT            NOT NULL,
        CreatedDate       DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy        INT            NULL,
        ModifiedDate      DATETIME2(3)   NULL,
        IsDeleted         BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_Invoice PRIMARY KEY (Id),
        CONSTRAINT UQ_Invoice_No UNIQUE (InvoiceNo),
        CONSTRAINT FK_Inv_PO         FOREIGN KEY (PurchaseOrderId)  REFERENCES dbo.tblPPM_PurchaseOrder(Id),
        CONSTRAINT FK_Inv_Line       FOREIGN KEY (PoDeliveryLineId) REFERENCES dbo.tblPPM_PoDeliveryLine(Id),
        CONSTRAINT FK_Inv_Vendor     FOREIGN KEY (VendorId)         REFERENCES dbo.tblPPM_Vendor(Id),
        CONSTRAINT FK_Inv_CreatedBy  FOREIGN KEY (CreatedBy)        REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Inv_ModifiedBy FOREIGN KEY (ModifiedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_Inv_Flag  CHECK (Flag IN ('Prepayment','UnderReview','NotPrepayment')),
        CONSTRAINT CK_Inv_CapexOpex CHECK (CapexOpex IS NULL OR CapexOpex IN ('CAPEX','OPEX')),
        CONSTRAINT CK_Inv_Setup CHECK (SetupStatus IN
            ('Pending','AmortisationNeeded','DraftInProgress','Complete','PendingClassification'))
    );

    CREATE NONCLUSTERED INDEX IX_Invoice_PoLine      ON dbo.tblPPM_Invoice(PurchaseOrderId, PoDeliveryLineId);
    CREATE NONCLUSTERED INDEX IX_Invoice_SetupStatus ON dbo.tblPPM_Invoice(SetupStatus);
END
GO

/*======================================================================================
  4. AMORTISATION SETUP & SCHEDULE   (Tab 2 -> feeds Tab 3 / 6 / 7)
======================================================================================*/

/*--------------------------------------------------------------------------------------
  4.1  AmortisationSchedule (header) - classification + prepayment-GL assignment for an invoice
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_AmortisationSchedule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_AmortisationSchedule
    (
        Id                   BIGINT         NOT NULL IDENTITY(1,1),
        InvoiceId            BIGINT         NOT NULL,          -- FK -> Invoice (1:1)
        DeliveryGroupId      BIGINT         NULL,              -- FK -> DeliveryGroup
        AssetClassification  VARCHAR(15)    NOT NULL,          -- 'Current' | 'Non-current'
        ExpenditureType      VARCHAR(15)    NOT NULL,          -- 'Operational' | 'Capital' | 'Lease'
        AmortisationType     VARCHAR(15)    NOT NULL,          -- 'OneOff' | 'Scheduled'
        StartDate            DATE           NULL,
        EndDate              DATE           NULL,
        Periods              INT            NULL,
        Frequency            VARCHAR(15)    NULL,              -- 'Monthly' | 'Quarterly' | 'Annual'
        Basis                VARCHAR(30)    NULL,              -- 'Equal monthly (straight-line)'
        PeriodAmount         NUMERIC(18,2)  NULL,
        TotalAmount          NUMERIC(18,2)  NOT NULL DEFAULT(0), -- must equal invoice amount
        PrepaymentGlId       BIGINT         NULL,              -- FK -> PrepaymentGlAccount (170xxx)
        ExpenseGlAccount     VARCHAR(10)    NULL,              -- auto-suggested from PO line GL
        CostCentreWbs        VARCHAR(30)    NULL,
        CompanyCodeId        BIGINT         NULL,              -- FK -> CompanyCode
        Status               VARCHAR(15)    NOT NULL DEFAULT('Active'), -- Active|Completed|Suspended|Draft|Blocked
        IsImported           BIT            NOT NULL DEFAULT(0), -- 1 = from Source C register
        CreatedBy            INT            NOT NULL,
        CreatedDate          DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy           INT            NULL,
        ModifiedDate         DATETIME2(3)   NULL,
        IsDeleted            BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_AmortisationSchedule PRIMARY KEY (Id),
        CONSTRAINT UQ_Amort_Invoice UNIQUE (InvoiceId),
        CONSTRAINT FK_Amort_Invoice    FOREIGN KEY (InvoiceId)       REFERENCES dbo.tblPPM_Invoice(Id),
        CONSTRAINT FK_Amort_DG         FOREIGN KEY (DeliveryGroupId) REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_Amort_PreGl      FOREIGN KEY (PrepaymentGlId)  REFERENCES dbo.tblPPM_PrepaymentGlAccount(Id),
        CONSTRAINT FK_Amort_Company    FOREIGN KEY (CompanyCodeId)   REFERENCES dbo.tblPPM_CompanyCode(Id),
        CONSTRAINT FK_Amort_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Amort_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_Amort_Asset  CHECK (AssetClassification IN ('Current','Non-current')),
        CONSTRAINT CK_Amort_Exp    CHECK (ExpenditureType IN ('Operational','Capital','Lease')),
        CONSTRAINT CK_Amort_Type   CHECK (AmortisationType IN ('OneOff','Scheduled')),
        CONSTRAINT CK_Amort_Status CHECK (Status IN ('Active','Completed','Suspended','Draft','Blocked'))
    );

    CREATE NONCLUSTERED INDEX IX_Amort_DG ON dbo.tblPPM_AmortisationSchedule(DeliveryGroupId);
END
GO

/*--------------------------------------------------------------------------------------
  4.2  AmortisationPeriod - one row per period (the write-off plan)
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_AmortisationPeriod', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_AmortisationPeriod
    (
        Id                       BIGINT         NOT NULL IDENTITY(1,1),
        AmortisationScheduleId   BIGINT         NOT NULL,      -- FK -> AmortisationSchedule
        PeriodNumber             INT            NOT NULL,      -- 1..N
        PeriodDate               DATE           NULL,          -- 'Jul 2026'
        FiscalYear               SMALLINT       NULL,
        FiscalPeriod             TINYINT        NULL,          -- 1..12
        Amount                   NUMERIC(18,2)  NOT NULL DEFAULT(0),
        CumulativeAmount         NUMERIC(18,2)  NULL,
        Status                   VARCHAR(15)    NOT NULL DEFAULT('Planned'),
        CreatedBy                INT            NOT NULL,
        CreatedDate              DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy               INT            NULL,
        ModifiedDate             DATETIME2(3)   NULL,
        IsDeleted                BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_AmortisationPeriod PRIMARY KEY (Id),
        CONSTRAINT UQ_AmortPeriod UNIQUE (AmortisationScheduleId, PeriodNumber),
        CONSTRAINT FK_AmortPeriod_Sched      FOREIGN KEY (AmortisationScheduleId) REFERENCES dbo.tblPPM_AmortisationSchedule(Id),
        CONSTRAINT FK_AmortPeriod_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_AmortPeriod_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_AmortPeriod_Status CHECK
            (Status IN ('Planned','Pending','PendingExport','Exported','Posted','Cancelled'))
    );

    CREATE NONCLUSTERED INDEX IX_AmortPeriod_Sched ON dbo.tblPPM_AmortisationPeriod(AmortisationScheduleId);
END
GO

/*======================================================================================
  5. JOURNALS & EXPORT BATCHES   (Tab 3)
======================================================================================*/

/*--------------------------------------------------------------------------------------
  5.1  ExportBatch - groups approved journals for the SAP interface  3.3
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_ExportBatch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_ExportBatch
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        BatchRef         VARCHAR(30)    NOT NULL,             -- 'PREP-0626-04'
        JournalCount     INT            NOT NULL DEFAULT(0),
        Status           VARCHAR(15)    NOT NULL DEFAULT('Ready'), -- Ready|Exported|Failed|Cancelled
        ExportedDate     DATETIME2(3)   NULL,
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_ExportBatch PRIMARY KEY (Id),
        CONSTRAINT UQ_ExportBatch_Ref UNIQUE (BatchRef),
        CONSTRAINT FK_Batch_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Batch_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_Batch_Status CHECK (Status IN ('Ready','Exported','Failed','Cancelled'))
    );
END
GO

/*--------------------------------------------------------------------------------------
  5.2  Journal (header) - Recognition (capitalise) or Amortisation (expense)  6
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_Journal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_Journal
    (
        Id                     BIGINT         NOT NULL IDENTITY(1,1),
        JournalRef             VARCHAR(30)    NOT NULL,       -- 'JRNL-REC-0626-001'
        JournalType            VARCHAR(15)    NOT NULL,       -- 'Recognition' | 'Amortisation'
        InvoiceId              BIGINT         NULL,           -- FK -> Invoice
        AmortisationScheduleId BIGINT         NULL,           -- FK -> AmortisationSchedule
        AmortisationPeriodId   BIGINT         NULL,           -- FK -> AmortisationPeriod (amort only)
        DeliveryGroupId        BIGINT         NULL,           -- FK -> DeliveryGroup
        DrAccount              VARCHAR(10)    NULL,           -- debit GL
        CrAccount              VARCHAR(10)    NULL,           -- credit GL
        CostObject             VARCHAR(30)    NULL,           -- cost centre / WBS
        Amount                 NUMERIC(18,2)  NOT NULL DEFAULT(0),
        PostingPeriod          VARCHAR(7)     NULL,           -- 'YYYY/MM'
        RemainingBalance       NUMERIC(18,2)  NULL,           -- amort: balance after this period
        Status                 VARCHAR(20)    NOT NULL DEFAULT('Draft'),
        PreparerUserId         INT            NULL,           -- FK -> AppUser
        ApproverUserId         INT            NULL,           -- FK -> AppUser
        ApprovedDate           DATETIME2(3)   NULL,
        ApproverComments       NVARCHAR(1000) NULL,
        RejectReasonCode       VARCHAR(40)    NULL,
        ExportBatchId          BIGINT         NULL,           -- FK -> ExportBatch
        CreatedBy              INT            NOT NULL,
        CreatedDate            DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy              INT           NULL,
        ModifiedDate           DATETIME2(3)   NULL,
        IsDeleted               BIT           NOT NULL DEFAULT(0),
        CONSTRAINT PK_Journal PRIMARY KEY (Id),
        CONSTRAINT UQ_Journal_Ref UNIQUE (JournalRef),
        CONSTRAINT FK_Jnl_Invoice    FOREIGN KEY (InvoiceId)              REFERENCES dbo.tblPPM_Invoice(Id),
        CONSTRAINT FK_Jnl_Schedule   FOREIGN KEY (AmortisationScheduleId) REFERENCES dbo.tblPPM_AmortisationSchedule(Id),
        CONSTRAINT FK_Jnl_Period     FOREIGN KEY (AmortisationPeriodId)   REFERENCES dbo.tblPPM_AmortisationPeriod(Id),
        CONSTRAINT FK_Jnl_DG         FOREIGN KEY (DeliveryGroupId)        REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_Jnl_Preparer   FOREIGN KEY (PreparerUserId)         REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Jnl_Approver   FOREIGN KEY (ApproverUserId)         REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Jnl_Batch      FOREIGN KEY (ExportBatchId)          REFERENCES dbo.tblPPM_ExportBatch(Id),
        CONSTRAINT FK_Jnl_CreatedBy  FOREIGN KEY (CreatedBy)              REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Jnl_ModifiedBy FOREIGN KEY (ModifiedBy)             REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_Jnl_Type   CHECK (JournalType IN ('Recognition','Amortisation')),
        CONSTRAINT CK_Jnl_Status CHECK (Status IN ('Draft','PendingApproval','Approved','Rejected','Exported'))
    );

    CREATE NONCLUSTERED INDEX IX_Journal_Status ON dbo.tblPPM_Journal(Status) INCLUDE (JournalType, Amount);
    CREATE NONCLUSTERED INDEX IX_Journal_Batch  ON dbo.tblPPM_Journal(ExportBatchId);
END
GO

/*--------------------------------------------------------------------------------------
  5.3  JournalEntry - double-entry Dr/Cr lines of a journal  3.3
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_JournalEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_JournalEntry
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        JournalId        BIGINT         NOT NULL,             -- FK -> Journal
        DebitCredit      CHAR(2)        NOT NULL,             -- 'Dr' | 'Cr'
        Account          VARCHAR(10)    NOT NULL,             -- GL account
        Description      NVARCHAR(200)  NULL,
        CostObject       VARCHAR(30)    NULL,
        Amount           NUMERIC(18,2)  NOT NULL DEFAULT(0),
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_JournalEntry PRIMARY KEY (Id),
        CONSTRAINT FK_JnlEntry_Journal    FOREIGN KEY (JournalId)  REFERENCES dbo.tblPPM_Journal(Id),
        CONSTRAINT FK_JnlEntry_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_JnlEntry_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_JnlEntry_DC CHECK (DebitCredit IN ('Dr','Cr'))
    );
END
GO

/*--------------------------------------------------------------------------------------
  5.4  JournalAudit - submit/approve/reject/export/override trail  7 #8
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_JournalAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_JournalAudit
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        JournalId        BIGINT         NOT NULL,             -- FK -> Journal
        Action           VARCHAR(25)    NOT NULL,            -- Submitted|Approved|Rejected|Exported|Reopened|Reassigned|Created
        ActionByUserId   INT            NULL,                -- FK -> AppUser
        Comments         NVARCHAR(1000) NULL,
        ActionDate       DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy        INT            NOT NULL,
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_JournalAudit PRIMARY KEY (Id),
        CONSTRAINT FK_JnlAudit_Journal    FOREIGN KEY (JournalId)      REFERENCES dbo.tblPPM_Journal(Id),
        CONSTRAINT FK_JnlAudit_ActionBy   FOREIGN KEY (ActionByUserId) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_JnlAudit_CreatedBy  FOREIGN KEY (CreatedBy)      REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_JnlAudit_ModifiedBy FOREIGN KEY (ModifiedBy)     REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_JnlAudit_Action CHECK (Action IN
            ('Submitted','Approved','Rejected','Exported','Reopened','Reassigned','Created'))
    );
END
GO

/*======================================================================================
  6. GROUP WORKFLOW & EXCEPTIONS   (Tab 4 / Tab 5)
======================================================================================*/

/*--------------------------------------------------------------------------------------
  6.1  GroupWorkflowState - per group/period stage + status matrix  3.5
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_GroupWorkflowState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_GroupWorkflowState
    (
        Id                 BIGINT       NOT NULL IDENTITY(1,1),
        DeliveryGroupId    BIGINT       NOT NULL,             -- FK -> DeliveryGroup
        Period             VARCHAR(7)   NOT NULL,             -- 'YYYY/MM'
        CurrentStage       VARCHAR(25)  NOT NULL DEFAULT('PoFlagged'),
        Status             VARCHAR(20)  NOT NULL DEFAULT('OnTrack'), -- OnTrack|NeedsAttention|Blocked|FullyExported
        PoFlagComplete     BIT          NOT NULL DEFAULT(0),
        InvoiceComplete    BIT          NOT NULL DEFAULT(0),
        SetupComplete      BIT          NOT NULL DEFAULT(0),
        RecognisedComplete BIT          NOT NULL DEFAULT(0),
        AmortisingComplete BIT          NOT NULL DEFAULT(0),
        ExportedComplete   BIT          NOT NULL DEFAULT(0),
        CreatedBy          INT          NOT NULL,
        CreatedDate        DATETIME2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy         INT          NULL,
        ModifiedDate       DATETIME2(3) NULL,
        IsDeleted          BIT          NOT NULL DEFAULT(0),
        CONSTRAINT PK_GroupWorkflowState PRIMARY KEY (Id),
        CONSTRAINT UQ_GroupWorkflow UNIQUE (DeliveryGroupId, Period),
        CONSTRAINT FK_GWF_DG         FOREIGN KEY (DeliveryGroupId) REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_GWF_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_GWF_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_GWF_Stage CHECK (CurrentStage IN
            ('PoFlagged','InvoiceMatched','SetupComplete','JournalGenerated','PendingApproval',
             'Approved','Exported','AmortSetup','ExportReady','Completed','Rejected','DuplicateBlocked')),
        CONSTRAINT CK_GWF_Status CHECK (Status IN ('OnTrack','NeedsAttention','Blocked','FullyExported'))
    );
END
GO

/*--------------------------------------------------------------------------------------
  6.2  ExceptionItem - blocked / error items  3.4
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_ExceptionItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_ExceptionItem
    (
        Id                BIGINT         NOT NULL IDENTITY(1,1),
        Title             NVARCHAR(200)  NOT NULL,
        Detail            NVARCHAR(400)  NULL,
        ExceptionType     VARCHAR(30)    NOT NULL DEFAULT('Other'), -- Blocked|FollowUp|Error|Ready|Duplicate|Variance|Other
        PurchaseOrderId   BIGINT         NULL,               -- FK -> PurchaseOrder
        JournalId         BIGINT         NULL,               -- FK -> Journal
        DeliveryGroupId   BIGINT         NULL,               -- FK -> DeliveryGroup
        Status            VARCHAR(15)    NOT NULL DEFAULT('Open'), -- Open|InProgress|Resolved
        AssignedToUserId  INT            NULL,               -- FK -> AppUser
        ResolutionNote    NVARCHAR(1000) NULL,
        ResolvedDate      DATETIME2(3)   NULL,
        CreatedBy         INT            NOT NULL,
        CreatedDate       DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy        INT            NULL,
        ModifiedDate      DATETIME2(3)   NULL,
        IsDeleted         BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_ExceptionItem PRIMARY KEY (Id),
        CONSTRAINT FK_Exc_PO         FOREIGN KEY (PurchaseOrderId) REFERENCES dbo.tblPPM_PurchaseOrder(Id),
        CONSTRAINT FK_Exc_Jnl        FOREIGN KEY (JournalId)       REFERENCES dbo.tblPPM_Journal(Id),
        CONSTRAINT FK_Exc_DG         FOREIGN KEY (DeliveryGroupId) REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_Exc_AssignedTo FOREIGN KEY (AssignedToUserId) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Exc_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Exc_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_Exc_Status CHECK (Status IN ('Open','InProgress','Resolved'))
    );
END
GO

/*--------------------------------------------------------------------------------------
  6.x  WorkflowReminder - one row per "Send reminder" (3.5) row action.
       The prototype has no live email (Tab 8 / 3.8 out of scope), so a reminder is
       recorded here as an auditable activity record - NOT as an ExceptionItem, so it
       does not pollute the Admin "clear exception" picker (Tab 4 / 3.4).
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_WorkflowReminder', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_WorkflowReminder
    (
        Id                BIGINT         NOT NULL IDENTITY(1,1),
        DeliveryGroupId   BIGINT         NOT NULL,            -- FK -> DeliveryGroup
        PreparerUserId    INT            NULL,                -- FK -> AppUser (recipient at send time)
        PreparerName      NVARCHAR(150)  NULL,                -- snapshot of preparer display name
        Detail            NVARCHAR(400)  NULL,
        SentByUserId      INT            NOT NULL,            -- FK -> AppUser (who clicked Send reminder)
        SentDate          DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy         INT            NOT NULL,
        CreatedDate       DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy        INT            NULL,
        ModifiedDate      DATETIME2(3)   NULL,
        IsDeleted         BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_WorkflowReminder PRIMARY KEY (Id),
        CONSTRAINT FK_Rem_DG         FOREIGN KEY (DeliveryGroupId) REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_Rem_Preparer   FOREIGN KEY (PreparerUserId)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Rem_SentBy     FOREIGN KEY (SentByUserId)    REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Rem_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Rem_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*======================================================================================
  7. SOURCE D - GL BALANCE RECONCILIATION   (Tab 6)
======================================================================================*/

/*--------------------------------------------------------------------------------------
  7.1  GlExtractFile - one row per uploaded CSV  3.6
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_GlExtractFile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_GlExtractFile
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        SourceFileName   NVARCHAR(260)  NOT NULL,             -- 'GL_Balance_Jun2026.csv'
        ReportingPeriod  VARCHAR(7)     NULL,                 -- 'YYYY/MM'
        ExtractDate      DATE           NULL,
        AccountCount     INT            NULL,
        GroupCount       INT            NULL,
        CreatedBy        INT            NOT NULL,             -- = the loader
        CreatedDate      DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy       INT            NULL,
        ModifiedDate     DATETIME2(3)   NULL,
        IsDeleted        BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_GlExtractFile PRIMARY KEY (Id),
        CONSTRAINT FK_GlFile_CreatedBy  FOREIGN KEY (CreatedBy)  REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_GlFile_ModifiedBy FOREIGN KEY (ModifiedBy) REFERENCES dbo.tblPPM_AppUser(Id)
    );
END
GO

/*--------------------------------------------------------------------------------------
  7.2  GlBalanceRecord - one balance row per group/GL/period  6 GL Balance Record
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_GlBalanceRecord', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_GlBalanceRecord
    (
        Id                 BIGINT         NOT NULL IDENTITY(1,1),
        GlExtractFileId    BIGINT         NULL,               -- FK -> GlExtractFile
        DeliveryGroupId    BIGINT         NULL,               -- FK -> DeliveryGroup
        PrepaymentGlId     BIGINT         NOT NULL,           -- FK -> PrepaymentGlAccount (170xxx)
        CompanyCodeId      BIGINT         NULL,               -- FK -> CompanyCode
        FiscalYear         SMALLINT       NULL,
        FiscalPeriod       TINYINT        NULL,               -- 1..12
        OpeningBalance     NUMERIC(18,2)  NOT NULL DEFAULT(0),
        PeriodDebit        NUMERIC(18,2)  NOT NULL DEFAULT(0),
        PeriodCredit       NUMERIC(18,2)  NOT NULL DEFAULT(0),
        ClosingBalance     NUMERIC(18,2)  NOT NULL DEFAULT(0),
        ExtractDate        DATE           NULL,
        CreatedBy          INT            NOT NULL,
        CreatedDate        DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy         INT            NULL,
        ModifiedDate       DATETIME2(3)   NULL,
        IsDeleted          BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_GlBalanceRecord PRIMARY KEY (Id),
        CONSTRAINT FK_GlBal_File       FOREIGN KEY (GlExtractFileId) REFERENCES dbo.tblPPM_GlExtractFile(Id),
        CONSTRAINT FK_GlBal_DG         FOREIGN KEY (DeliveryGroupId) REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_GlBal_Gl         FOREIGN KEY (PrepaymentGlId)  REFERENCES dbo.tblPPM_PrepaymentGlAccount(Id),
        CONSTRAINT FK_GlBal_Company    FOREIGN KEY (CompanyCodeId)   REFERENCES dbo.tblPPM_CompanyCode(Id),
        CONSTRAINT FK_GlBal_CreatedBy  FOREIGN KEY (CreatedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_GlBal_ModifiedBy FOREIGN KEY (ModifiedBy)      REFERENCES dbo.tblPPM_AppUser(Id)
    );

    CREATE NONCLUSTERED INDEX IX_GlBal_DG_Gl ON dbo.tblPPM_GlBalanceRecord(DeliveryGroupId, PrepaymentGlId);
END
GO

/*--------------------------------------------------------------------------------------
  7.3  Reconciliation - SAP vs Prepayment by group/GL/period; Variance is computed  3.6
--------------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.tblPPM_Reconciliation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPPM_Reconciliation
    (
        Id                 BIGINT         NOT NULL IDENTITY(1,1),
        DeliveryGroupId    BIGINT         NOT NULL,           -- FK -> DeliveryGroup
        PrepaymentGlId     BIGINT         NULL,               -- FK -> PrepaymentGlAccount
        Period             VARCHAR(7)     NOT NULL,           -- 'YYYY/MM'
        GlExtractFileId    BIGINT         NULL,               -- FK -> GlExtractFile
        SapBalance         NUMERIC(18,2)  NOT NULL DEFAULT(0),
        PrepaymentBalance  NUMERIC(18,2)  NOT NULL DEFAULT(0),
        Variance           AS (SapBalance - PrepaymentBalance) PERSISTED,
        Status             VARCHAR(20)    NOT NULL DEFAULT('Reconciled'), -- Reconciled|Variance|JournalPending|NotMatched
        InvestigationNote  NVARCHAR(1000) NULL,
        ResolutionAction   VARCHAR(30)    NULL,               -- 'MarkExplained' | 'RaiseAdjustment'
        AssignedToUserId   INT            NULL,               -- FK -> AppUser
        ResolvedDate       DATETIME2(3)   NULL,
        CreatedBy          INT            NOT NULL,
        CreatedDate        DATETIME2(3)   NOT NULL DEFAULT(SYSUTCDATETIME()),
        ModifiedBy         INT            NULL,
        ModifiedDate       DATETIME2(3)   NULL,
        IsDeleted          BIT            NOT NULL DEFAULT(0),
        CONSTRAINT PK_Reconciliation PRIMARY KEY (Id),
        CONSTRAINT UQ_Recon UNIQUE (DeliveryGroupId, PrepaymentGlId, Period),
        CONSTRAINT FK_Recon_DG         FOREIGN KEY (DeliveryGroupId)  REFERENCES dbo.tblPPM_DeliveryGroup(Id),
        CONSTRAINT FK_Recon_Gl         FOREIGN KEY (PrepaymentGlId)   REFERENCES dbo.tblPPM_PrepaymentGlAccount(Id),
        CONSTRAINT FK_Recon_File       FOREIGN KEY (GlExtractFileId)  REFERENCES dbo.tblPPM_GlExtractFile(Id),
        CONSTRAINT FK_Recon_AssignedTo FOREIGN KEY (AssignedToUserId) REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Recon_CreatedBy  FOREIGN KEY (CreatedBy)        REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT FK_Recon_ModifiedBy FOREIGN KEY (ModifiedBy)       REFERENCES dbo.tblPPM_AppUser(Id),
        CONSTRAINT CK_Recon_Status CHECK (Status IN ('Reconciled','Variance','JournalPending','NotMatched'))
    );

    CREATE NONCLUSTERED INDEX IX_Recon_Status ON dbo.tblPPM_Reconciliation(Status);
END
GO

PRINT 'dbo.tblPPM_* schema created successfully in CPlatform (Tabs 1-7, no seed data).';
GO
