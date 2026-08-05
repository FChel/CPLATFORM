/* ============================================================================
   NORM FY2025 Departmental configuration promotion

   Run after:
     1. NORM_01_ProofEngine_Schema.sql
     2. NORM_FY2025_mapping.sql from this folder (901 GL mappings)

   This script attaches the seeded accounting content to an immutable approved
   configuration release. Safe to rerun.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ReportingEntity WHERE FinancialYear = 2025 AND EntityCode = 'DEPT')
    INSERT dbo.tblNORM_ReportingEntity (FinancialYear, EntityCode, EntityName, BasisNote)
    VALUES (2025, 'DEPT', N'Department of Defence - Departmental', N'ERP company code 1000; audited FY2025 replay baseline.');

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_EntityLedger WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND SourceLedger = '1000')
    INSERT dbo.tblNORM_EntityLedger (FinancialYear, EntityCode, SourceLedger)
    VALUES (2025, 'DEPT', '1000');

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ConfigurationRelease WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND VersionCode = 'v1.0')
    INSERT dbo.tblNORM_ConfigurationRelease
        (FinancialYear, EntityCode, VersionCode, ReleaseLabel, StatusCode, ApprovedBy, ApprovedUtc, CreatedBy)
    VALUES
        (2025, 'DEPT', 'v1.0', N'FY2025 audited replay baseline', 'Approved', N'DFG accounting baseline', SYSUTCDATETIME(), N'NORM deployment');

DECLARE @ReleaseId INT = (
    SELECT ConfigurationReleaseId
    FROM dbo.tblNORM_ConfigurationRelease
    WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND VersionCode = 'v1.0' AND IsDeactivated = 0
);

IF @ReleaseId IS NULL
    THROW 51000, 'NORM FY2025 configuration release could not be resolved.', 1;

UPDATE dbo.tblNORM_AccountMap
SET ConfigurationReleaseId = @ReleaseId
WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND ConfigurationReleaseId IS NULL;

UPDATE dbo.tblNORM_StatementLine
SET ConfigurationReleaseId = @ReleaseId,
    CalculationKind = CASE WHEN LineType = 'section' THEN 'Heading' ELSE 'Mapped' END,
    IsClickable = CASE WHEN LineType = 'section' THEN 0 ELSE 1 END
WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND ConfigurationReleaseId IS NULL;

UPDATE dbo.tblNORM_PublishedFigure
SET ConfigurationReleaseId = @ReleaseId,
    SourceReference = COALESCE(SourceReference, N'Defence Annual Report 2024-25, audited financial statements')
WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND ConfigurationReleaseId IS NULL;

/* Complete the two proof-engine faces with data-driven calculated totals. */
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOCI' AND LineCode = 'Total expenses')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOCI',115,'total','Total expenses',N'Total expenses','D','Formula',
         N'+Employee benefits|+Supplier expenses|+Grants|+Finance costs|+Impairment loss on financial instruments|+Write-down of non-financial assets|+Expenses in relation to special accounts|+Other expenses|+Foreign exchange|+Depreciation and amortisation',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOCI' AND LineCode = 'Total own-source income')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOCI',195,'total','Total own-source income',N'Total own-source income','C','Formula',
         N'+Revenue from contracts with customers|+Revenue in relation to special accounts|+Rental income|+Other revenue|+Gain on sale of asset|+Reversals of previous asset write-downs|+Other gains',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOCI' AND LineCode = 'Net cost of services')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOCI',197,'total','Net cost of services',N'Net cost of services','D','Formula',N'+Total expenses|-Total own-source income',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOCI' AND LineCode = 'Operating result')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOCI',210,'total','Operating result',N'Operating result','D','Formula',N'+Revenue from Government|-Net cost of services',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOFP' AND LineCode = 'Total assets')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOFP',85,'total','Total assets',N'Total assets','D','Formula',
         N'+Cash and cash equivalents|+Trade and other receivables|+Property plant and equipment|+Inventories|+Prepayments|+Assets held for sale',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOFP' AND LineCode = 'Total liabilities')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOFP',170,'total','Total liabilities',N'Total liabilities','C','Formula',
         N'+Suppliers payables|+Employee payables|+Other payables|+Leases|+Employee provisions|+Asset restoration provisions|+Other provisions',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOFP' AND LineCode = 'Net assets')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,FormulaSpec,IsClickable)
    VALUES
        (@ReleaseId,2025,'DEPT','SOFP',175,'total','Net assets',N'Net assets','D','Formula',N'+Total assets|-Total liabilities',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOFP' AND LineCode IS NULL AND LineLabel = N'Equity')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,CalculationKind,IsClickable)
    VALUES (@ReleaseId,2025,'DEPT','SOFP',176,'section',NULL,N'Equity','Heading',0);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOFP' AND LineCode = 'Statement of Changes in Equity')
    INSERT dbo.tblNORM_StatementLine
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NaturalSign,CalculationKind,IsClickable)
    VALUES (@ReleaseId,2025,'DEPT','SOFP',180,'total','Statement of Changes in Equity',N'Total equity','C','Mapped',1);

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_PublishedFigure WHERE ConfigurationReleaseId = @ReleaseId AND LineCode = 'Total expenses')
    INSERT dbo.tblNORM_PublishedFigure (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,AmountCurrent,AmountPrior,SourceReference) VALUES
      (@ReleaseId,2025,'DEPT','SOCI','Total expenses',50386401,46297957,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOCI','Total own-source income',2677797,2293753,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOCI','Net cost of services',47708604,44004204,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOCI','Operating result',-8730925,-8163634,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOFP','Total assets',158436070,146458730,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOFP','Total liabilities',15800563,13848233,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOFP','Net assets',142635507,132610497,N'Defence Annual Report 2024-25, audited financial statements'),
      (@ReleaseId,2025,'DEPT','SOFP','Statement of Changes in Equity',142635507,132610497,N'Defence Annual Report 2024-25, audited financial statements');

/* Fail clearly when the accounting seed was not run or is incomplete. */
IF (SELECT COUNT(*) FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId = @ReleaseId AND IsDeactivated = 0) < 850
    THROW 51001, 'FY2025 mapping is missing or incomplete. Run src/sql/NORM_FY2025_mapping.sql before this script.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOCI' AND IsDeactivated = 0)
    THROW 51002, 'FY2025 statement template is missing.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_PublishedFigure WHERE ConfigurationReleaseId = @ReleaseId AND StatementCode = 'SOCI' AND IsDeactivated = 0)
    THROW 51003, 'FY2025 published figures are missing.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblNORM_AccountMap_ReleaseGl' AND object_id = OBJECT_ID('dbo.tblNORM_AccountMap'))
    CREATE UNIQUE INDEX UX_tblNORM_AccountMap_ReleaseGl
    ON dbo.tblNORM_AccountMap(ConfigurationReleaseId,GlCode) WHERE ConfigurationReleaseId IS NOT NULL AND IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblNORM_StatementLine_ReleaseSeq' AND object_id = OBJECT_ID('dbo.tblNORM_StatementLine'))
    CREATE UNIQUE INDEX UX_tblNORM_StatementLine_ReleaseSeq
    ON dbo.tblNORM_StatementLine(ConfigurationReleaseId,StatementCode,SeqNo) WHERE ConfigurationReleaseId IS NOT NULL AND IsDeactivated = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblNORM_PublishedFigure_ReleaseLine' AND object_id = OBJECT_ID('dbo.tblNORM_PublishedFigure'))
    CREATE UNIQUE INDEX UX_tblNORM_PublishedFigure_ReleaseLine
    ON dbo.tblNORM_PublishedFigure(ConfigurationReleaseId,StatementCode,LineCode) WHERE ConfigurationReleaseId IS NOT NULL AND IsDeactivated = 0;

/* Approved accounting content is immutable. New annual content is prepared
   under a Draft release and becomes protected as soon as it is approved. */
IF OBJECT_ID('dbo.trgNORM_AccountMap_ApprovedImmutable', 'TR') IS NULL
EXEC(N'CREATE TRIGGER dbo.trgNORM_AccountMap_ApprovedImmutable ON dbo.tblNORM_AccountMap AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted x INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=x.ConfigurationReleaseId WHERE c.StatusCode=''Approved'')
 OR EXISTS (SELECT 1 FROM deleted x INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=x.ConfigurationReleaseId WHERE c.StatusCode=''Approved'')
 BEGIN; THROW 51300, ''Approved NORM account mappings are immutable. Create a new configuration release.'', 1; END;
END');

IF OBJECT_ID('dbo.trgNORM_StatementLine_ApprovedImmutable', 'TR') IS NULL
EXEC(N'CREATE TRIGGER dbo.trgNORM_StatementLine_ApprovedImmutable ON dbo.tblNORM_StatementLine AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted x INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=x.ConfigurationReleaseId WHERE c.StatusCode=''Approved'')
 OR EXISTS (SELECT 1 FROM deleted x INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=x.ConfigurationReleaseId WHERE c.StatusCode=''Approved'')
 BEGIN; THROW 51301, ''Approved NORM statement templates are immutable. Create a new configuration release.'', 1; END;
END');

IF OBJECT_ID('dbo.trgNORM_PublishedFigure_ApprovedImmutable', 'TR') IS NULL
EXEC(N'CREATE TRIGGER dbo.trgNORM_PublishedFigure_ApprovedImmutable ON dbo.tblNORM_PublishedFigure AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted x INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=x.ConfigurationReleaseId WHERE c.StatusCode=''Approved'')
 OR EXISTS (SELECT 1 FROM deleted x INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=x.ConfigurationReleaseId WHERE c.StatusCode=''Approved'')
 BEGIN; THROW 51302, ''Approved NORM replay baselines are immutable. Create a new configuration release.'', 1; END;
END');

IF OBJECT_ID('dbo.trgNORM_ConfigurationRelease_ApprovedImmutable', 'TR') IS NULL
EXEC(N'CREATE TRIGGER dbo.trgNORM_ConfigurationRelease_ApprovedImmutable ON dbo.tblNORM_ConfigurationRelease AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM deleted WHERE StatusCode=''Approved'')
 BEGIN; THROW 51303, ''Approved NORM configuration releases are immutable.'', 1; END;
END');

COMMIT TRANSACTION;

PRINT 'NORM FY2025 v1.0 has been promoted as an approved immutable configuration release.';
