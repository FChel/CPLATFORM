SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
  FY2025 demo reconstruction for two current-year departmental notes only.

  This is deliberately separate from Start of Financial Year Setup: in a live
  reporting cycle the current-year statements do not yet exist. The face
  statements remain trial-balance driven; these rows reconstruct the published
  note presentation for the retrospective Defence demonstration and retain a
  clear source reference.
*/
IF OBJECT_ID('dbo.tblNORM_DemoCurrentNoteFigure','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_DemoCurrentNoteFigure
    (
        DemoCurrentNoteFigureId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_DemoCurrentNoteFigure PRIMARY KEY,
        EntityCode VARCHAR(30) NOT NULL,
        FinancialYear INT NOT NULL,
        DisclosureCode VARCHAR(40) NOT NULL,
        SourceLineCode NVARCHAR(240) NOT NULL,
        LineLabel NVARCHAR(240) NOT NULL,
        PriorLookupLabel NVARCHAR(240) NULL,
        LineTypeCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_DemoCurrentNoteFigure_LineType DEFAULT ('detail'),
        Amount DECIMAL(19,3) NULL,
        ContributesToTotal BIT NOT NULL CONSTRAINT DF_tblNORM_DemoCurrentNoteFigure_Contributes DEFAULT (1),
        SortOrder INT NOT NULL,
        SourceReference NVARCHAR(300) NOT NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_DemoCurrentNoteFigure_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_DemoCurrentNoteFigure_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_tblNORM_DemoCurrentNoteFigure_Type CHECK (LineTypeCode IN ('detail','section','subtotal')),
        CONSTRAINT UQ_tblNORM_DemoCurrentNoteFigure UNIQUE(EntityCode,FinancialYear,DisclosureCode,SortOrder)
    );
END;

DECLARE @Figures TABLE
(
    DisclosureCode VARCHAR(40), SourceLineCode NVARCHAR(240), LineLabel NVARCHAR(240),
    PriorLookupLabel NVARCHAR(240), LineTypeCode VARCHAR(20), Amount DECIMAL(19,3) NULL,
    ContributesToTotal BIT, SortOrder INT, SourceReference NVARCHAR(300)
);

DECLARE @SupplierSource NVARCHAR(300)=N'Defence Annual Report 2024-25, Note 1.1B, p.259 (PDF p.261)';
DECLARE @CashSource NVARCHAR(300)=N'Defence Annual Report 2024-25, Note 3.1A, p.265 (PDF p.267)';

INSERT @Figures VALUES
-- Note 1.1B: Supplier expenses
('N1_1B',N'Supplier expenses',N'Goods and services supplied or rendered',NULL,'section',NULL,0,10,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Sustainment (including repair and overhaul)',N'Sustainment (including repair and overhaul)','detail',10766890,1,20,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Communications and information technology',N'Communications and information technology','detail',2637074,1,30,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Estate upkeep',N'Estate upkeep','detail',2103141,1,40,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Inventory consumption',N'Inventory consumption','detail',1008711,1,50,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Administration',N'Administration','detail',1104514,1,60,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Professional services / technical advice',N'Professional services/technical advice','detail',1030976,1,70,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Freight, storage and removal',N'Freight','detail',654399,1,80,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Project management costs',N'Project management costs','detail',696247,1,90,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Training',N'Training','detail',681972,1,100,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Purchase of minor assets',N'Purchase if minor assets','detail',531052,1,110,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Research and development',N'Research and development','detail',633563,1,120,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Utilities',N'Utilities','detail',523625,1,130,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Garrison support and mess operations',N'Garrison & mess operations','detail',444640,1,140,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Travel',N'Travel','detail',448494,1,150,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Foreign Government activities',N'Foreign government activities','detail',96231,1,160,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Other',N'Other','detail',1747472,1,170,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Total goods and services supplied or rendered',N'Total goods and services supplied or rendered','subtotal',25109001,0,180,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Goods and services supplied or rendered are made up of:',NULL,'section',NULL,0,190,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Goods supplied',N'Goods supplied','detail',2907864,0,200,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Services rendered',N'Services rendered','detail',22201137,0,210,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Total goods and services supplied or rendered',N'Total goods and services supplied or rendered','subtotal',25109001,0,220,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Other supplier expenses',NULL,'section',NULL,0,230,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Short-term leases',N'Short-term leases','detail',109830,1,240,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Low value leases',N'Low Value leases','detail',26444,1,250,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Variable lease payments',N'Variable lease payments','detail',2111,1,260,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Workers'' compensation expenses',N'Workers compensation expenses','detail',34650,1,270,@SupplierSource),
('N1_1B',N'Supplier expenses',N'Total other supplier expenses',N'Total other supplier expenses','subtotal',173035,0,280,@SupplierSource),
-- Note 3.1A: Cash and cash equivalents
('N3_1A',N'Cash and cash equivalents',N'Cash on hand',N'Cash on hand','detail',2969,1,10,@CashSource),
('N3_1A',N'Cash and cash equivalents',N'Cash at bank - at call',N'Cash at bank - at call','detail',580439,1,20,@CashSource),
('N3_1A',N'Cash and cash equivalents',N'Cash held in OPA - special accounts',N'Cash held in OPA - Special Account','detail',123953,1,30,@CashSource);

UPDATE target
SET target.SourceLineCode=source.SourceLineCode,target.LineLabel=source.LineLabel,
    target.PriorLookupLabel=source.PriorLookupLabel,target.LineTypeCode=source.LineTypeCode,
    target.Amount=source.Amount,target.ContributesToTotal=source.ContributesToTotal,
    target.SourceReference=source.SourceReference,target.IsDeactivated=0
FROM dbo.tblNORM_DemoCurrentNoteFigure target
INNER JOIN @Figures source ON source.DisclosureCode=target.DisclosureCode AND source.SortOrder=target.SortOrder
WHERE target.EntityCode='DEPT' AND target.FinancialYear=2025;

INSERT dbo.tblNORM_DemoCurrentNoteFigure
    (EntityCode,FinancialYear,DisclosureCode,SourceLineCode,LineLabel,PriorLookupLabel,LineTypeCode,
     Amount,ContributesToTotal,SortOrder,SourceReference)
SELECT 'DEPT',2025,source.DisclosureCode,source.SourceLineCode,source.LineLabel,source.PriorLookupLabel,
       source.LineTypeCode,source.Amount,source.ContributesToTotal,source.SortOrder,source.SourceReference
FROM @Figures source
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_DemoCurrentNoteFigure target
    WHERE target.EntityCode='DEPT' AND target.FinancialYear=2025
      AND target.DisclosureCode=source.DisclosureCode AND target.SortOrder=source.SortOrder
);

/* Supply the structured comparative rows from the retained prior-year document. */
IF OBJECT_ID('dbo.tblNORM_SourceNoteFigure','U') IS NOT NULL
BEGIN
    DECLARE @PriorHash CHAR(64)='1D02B0EE80D807735035CEED18EB7871D0D50193845DB6E7317335F3CA9CD62B';
    DECLARE @PriorSource NVARCHAR(300)=N'Defence Annual Report 2023-24, Note 1.1B, 2024 column';
    DECLARE @PriorRows TABLE(StatementLine NVARCHAR(240),NoteSubLine NVARCHAR(240),Amount DECIMAL(19,3));
    INSERT @PriorRows VALUES
        (N'Supplier expenses',N'Goods supplied',3277003),
        (N'Supplier expenses',N'Services rendered',19897958),
        (N'Supplier expenses',N'Total goods and services supplied or rendered',23174961),
        (N'Supplier expenses',N'Total other supplier expenses',112079);

    UPDATE target SET target.Amount=source.Amount,target.SourceReference=@PriorSource,target.IsDeactivated=0
    FROM dbo.tblNORM_SourceNoteFigure target
    INNER JOIN @PriorRows source ON source.StatementLine=target.StatementLine AND source.NoteSubLine=target.NoteSubLine
    WHERE target.SourceFileHash=@PriorHash;

    INSERT dbo.tblNORM_SourceNoteFigure(SourceFileHash,StatementLine,NoteSubLine,Amount,SourceReference)
    SELECT @PriorHash,source.StatementLine,source.NoteSubLine,source.Amount,@PriorSource
    FROM @PriorRows source
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.tblNORM_SourceNoteFigure target
        WHERE target.SourceFileHash=@PriorHash AND target.StatementLine=source.StatementLine
          AND target.NoteSubLine=source.NoteSubLine
    );
END;

/* Published policy wording is a demo template and remains editable in the control centre. */
DECLARE @Narratives TABLE(DisclosureCode VARCHAR(40),NarrativeType VARCHAR(30),TemplateText NVARCHAR(MAX));
INSERT @Narratives VALUES
('N1_1B','AccountingPolicy',N'The lease disclosures should be read in conjunction with notes 1.1D, 1.2C, 3.2A and 3.4A. Defence has short-term lease commitments of $4.3m as at 30 June 2025. Defence has elected not to recognise right-of-use assets and lease liabilities for short-term leases of assets with a lease term of 12 months or less and leases of low-value assets (less than $10,000). Defence recognises the lease payments associated with these leases as an expense on a straight-line basis over the lease term.'),
('N3_1A','AccountingPolicy',N'The closing balance of cash held in OPA - special accounts excludes amounts held in trust on behalf of other entities. Cash and cash equivalents includes cash on hand; demand deposits in bank accounts with an original maturity of three months or less that are readily convertible to known amounts of cash and subject to insignificant risk of changes in value; and cash in special accounts. Cash is measured at its nominal amount. Cash and cash equivalents denominated in a foreign currency are converted using the applicable exchange rate at the reporting date.');

UPDATE target SET target.TemplateText=source.TemplateText,target.IsDeactivated=0
FROM dbo.tblNORM_NarrativeTemplate target
INNER JOIN dbo.tblNORM_ConfigurationRelease release ON release.ConfigurationReleaseId=target.ConfigurationReleaseId
INNER JOIN @Narratives source ON source.DisclosureCode=target.DisclosureCode AND source.NarrativeType=target.NarrativeType
WHERE release.EntityCode='DEPT' AND release.FinancialYear=2025 AND release.IsDeactivated=0;

INSERT dbo.tblNORM_NarrativeTemplate(ConfigurationReleaseId,DisclosureCode,NarrativeType,TemplateText)
SELECT release.ConfigurationReleaseId,source.DisclosureCode,source.NarrativeType,source.TemplateText
FROM dbo.tblNORM_ConfigurationRelease release CROSS JOIN @Narratives source
WHERE release.EntityCode='DEPT' AND release.FinancialYear=2025 AND release.IsDeactivated=0
AND NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_NarrativeTemplate target
    WHERE target.ConfigurationReleaseId=release.ConfigurationReleaseId
      AND target.DisclosureCode=source.DisclosureCode AND target.NarrativeType=source.NarrativeType
);

PRINT 'FY2025 demo current-year cash and supplier note figures installed.';
