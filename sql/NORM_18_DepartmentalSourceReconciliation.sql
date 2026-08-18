/* NORM 18 - authoritative departmental comparative and original-budget reconciliation.
   Comparative amounts come from the FY2024 column in the Defence Annual Report 2024-25.
   Original-budget amounts come from the 2024-25 Budget column in the Defence PBS 2024-25.
   Idempotent; amounts are $'000. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];

IF OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL OR OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51040, 'Run the NORM preparation-control and start-of-year migrations before NORM 18.', 1;

DECLARE @ReleaseId INT=(SELECT TOP (1) ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease
 WHERE FinancialYear=2025 AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0 ORDER BY ConfigurationReleaseId DESC);
IF @ReleaseId IS NULL THROW 51041, 'Approved FY2025 DEPT release not found.', 1;

DECLARE @Figures TABLE
(
    StatementCode VARCHAR(20) NOT NULL,
    LineCode VARCHAR(200) NOT NULL,
    LineLabel NVARCHAR(300) NOT NULL,
    PriorAmount DECIMAL(19,3) NOT NULL,
    BudgetAmount DECIMAL(19,3) NOT NULL,
    PRIMARY KEY(StatementCode,LineCode)
);

INSERT @Figures(StatementCode,LineCode,LineLabel,PriorAmount,BudgetAmount) VALUES
-- Statement of Comprehensive Income
('SOCI','Employee benefits',N'Employee benefits',13986166,15190028),
('SOCI','Supplier expenses',N'Supplier expenses',23248553,25057086),
('SOCI','Grants',N'Grants',113201,49842),
('SOCI','Finance costs',N'Finance costs',167928,126015),
('SOCI','Impairment loss on financial instruments',N'Impairment loss allowance on financial instruments',3492,0),
('SOCI','Write-down of non-financial assets',N'Write-down of non-financial assets',1371965,1621821),
('SOCI','Expenses in relation to special accounts',N'Expenses in relation to Defence Trusts and Joint Accounts',18634,58227),
('SOCI','Other expenses',N'Other expenses',112085,255645),
('SOCI','Foreign exchange losses',N'Net foreign exchange losses',0,0),
('SOCI','Depreciation and amortisation',N'Depreciation and amortisation',7275933,6939017),
('SOCI','Total expenses',N'Total expenses',46297957,49297681),
('SOCI','Revenue from contracts with customers',N'Revenue from contracts with customers',570491,368382),
('SOCI','Revenue in relation to special accounts',N'Revenue in relation to Defence Trusts and Joint Accounts',139448,297555),
('SOCI','Rental income',N'Rental income',251346,243642),
('SOCI','Other revenue',N'Other revenue',277710,74196),
('SOCI','TOTAL_OSR',N'Total own-source revenue',1238995,983775),
('SOCI','Gain on sale of asset',N'Net gains from sale of assets',18391,43961),
('SOCI','Reversals of previous asset write-downs',N'Reversals of previous asset write-downs',866648,919361),
('SOCI','Foreign exchange gains',N'Net foreign exchange gains',30556,0),
('SOCI','Other gains',N'Other gains',169719,0),
('SOCI','TOTAL_GAINS',N'Total gains',1085314,963322),
('SOCI','Total own-source income',N'Total income',2324309,1947097),
('SOCI','Net cost of services',N'Net cost of services',43973648,47350584),
('SOCI','Revenue from Government',N'Revenue from Government',35840570,38784076),
('SOCI','Operating result',N'(Deficit) / Surplus',-8133078,-8566510),
('SOCI','OCI_REVALUATION',N'Changes in asset revaluation reserves',3367554,0),
('SOCI','OCI_SUBTOTAL',N'Total other comprehensive income / (loss)',3367554,0),
('SOCI','OCI_TOTAL',N'Total comprehensive (loss) / income',-4765524,-8566510),

-- Statement of Financial Position
('SOFP','Cash and cash equivalents',N'Cash and cash equivalents',332349,302213),
('SOFP','Trade and other receivables',N'Trade and other receivables',1957738,2579765),
('SOFP','TOTAL_FINANCIAL_ASSETS',N'Total financial assets',2290087,2881978),
('SOFP','PPE_LAND',N'Land',7033480,6150905),
('SOFP','PPE_BUILDINGS',N'Buildings',19665541,20579589),
('SOFP','PPE_SPECIALIST_MILITARY_EQUIPMENT',N'Specialist military equipment',88628062,91851358),
('SOFP','PPE_INFRASTRUCTURE',N'Infrastructure',9446765,8163428),
('SOFP','PPE_PLANT_AND_EQUIPMENT',N'Plant and equipment',1641318,1628742),
('SOFP','PPE_HERITAGE_AND_CULTURAL_ASSETS',N'Heritage and cultural assets',401671,404289),
('SOFP','PPE_INTANGIBLES',N'Intangibles',3622578,2654077),
('SOFP','Property plant and equipment',N'Property, plant, equipment and intangibles',130439415,131432388),
('SOFP','Inventories',N'Inventories',10160779,9664481),
('SOFP','Prepayments',N'Prepayments',3500032,4243307),
('SOFP','TOTAL_NON_FINANCIAL_ASSETS',N'Total non-financial assets',144100226,145340176),
('SOFP','Assets held for sale',N'Assets held for sale',68417,76598),
('SOFP','Total assets',N'Total assets',146458730,148298752),
('SOFP','Suppliers payables',N'Supplier payables',5351477,5896679),
('SOFP','Employee payables',N'Employee payables',353406,337093),
('SOFP','Other payables',N'Other payables',395503,378158),
('SOFP','TOTAL_PAYABLES',N'Total payables',6100386,6611930),
('SOFP','Leases',N'Leases',3139113,3362436),
('SOFP','TOTAL_INTEREST_LIABILITIES',N'Total interest-bearing liabilities',3139113,3362436),
('SOFP','Employee provisions',N'Employee provisions',3285642,3580316),
('SOFP','Asset restoration provisions',N'Asset restoration provisions',1056201,1087208),
('SOFP','Other provisions',N'Other provisions',266891,305933),
('SOFP','TOTAL_PROVISIONS',N'Total provisions',4608734,4973457),
('SOFP','Total liabilities',N'Total liabilities',13848233,14947823),
('SOFP','Net assets',N'Net assets',132610497,133350929),
('SOFP','EQUITY_CONTRIBUTED',N'Contributed equity',93451248,107248719),
('SOFP','EQUITY_RETAINED',N'(Accumulated Deficit) / Retained surpluses',1753672,-7935813),
('SOFP','EQUITY_RESERVES',N'Reserves',37405577,34038023),
('SOFP','EQUITY_TOTAL',N'Total equity',132610497,133350929),
('SOFP','Statement of Changes in Equity',N'Total equity',132610497,133350929),

-- Statement of Changes in Equity
('SOCE','SOCE_CONTRIBUTED_OPEN',N'Contributed equity - opening balance',79150682,93452272),
('SOCE','SOCE_CONTRIBUTED_OWNER',N'Contributed equity - transactions with owners',14300566,13796448),
('SOCE','SOCE_CONTRIBUTED_CLOSE',N'Contributed equity - closing balance',93451248,107248719),
('SOCE','SOCE_RETAINED_OPEN',N'Retained earnings - opening balance',9886750,630697),
('SOCE','SOCE_RETAINED_RESULT',N'Retained earnings - deficit',-8133078,-8566510),
('SOCE','SOCE_RETAINED_CLOSE',N'Retained earnings - closing balance',1753672,-7935813),
('SOCE','SOCE_RESERVE_OPEN',N'Asset revaluation reserve - opening balance',34038023,34038023),
('SOCE','SOCE_RESERVE_OCI',N'Changes in asset revaluation reserve',3367554,0),
('SOCE','SOCE_RESERVE_CLOSE',N'Asset revaluation reserve - closing balance',37405577,34038023),
('SOCE','SOCE_TOTAL_OPEN',N'Total equity - opening balance',123075455,128120992),
('SOCE','SOCE_TOTAL_RESULT',N'Total equity - deficit',-8133078,-8566510),
('SOCE','SOCE_TOTAL_OCI',N'Total equity - other comprehensive income',3367554,0),
('SOCE','SOCE_TOTAL_COMPREHENSIVE',N'Total comprehensive (loss) / income',-4765524,-8566510),
('SOCE','SOCE_TOTAL_OWNER',N'Transactions with owners',14300566,13796448),
('SOCE','SOCE_TOTAL_CLOSE',N'Total equity - closing balance',132610497,133350929),

-- Cash Flow Statement (published lines aligned to NORM cash-flow classes)
('CASH','CF_Appropriations',N'Appropriations',36350327,38470706),
('CASH','CF_Sale of goods and rendering of services',N'Sale of goods and rendering of services',710080,612022),
('CASH','CF_Interest',N'Interest',79934,0),
('CASH','CF_GST received',N'GST received',2893959,2936945),
('CASH','CF_Other cash received',N'Other cash received',199783,347350),
('CASH','CF_Employees',N'Employees',-14041269,-14876659),
('CASH','CF_Suppliers',N'Suppliers',-22013695,-23705914),
('CASH','CF_Interest payments on lease liabilities',N'Interest payments on lease liabilities',-125422,0),
('CASH','CF_GST paid',N'GST paid',-2899834,-2936945),
('CASH','CF_Section 74 receipts transferred to the OPA',N'Section 74 receipts transferred to the OPA',-1068380,0),
('CASH','CF_Grants',N'Grants',-119727,-49842),
('CASH','CF_Other cash used',N'Other cash used',-267456,-686852),
('CASH','CF_TOTAL_OPERATING',N'Net cash from/(used by) operating activities',766680,110811),
('CASH','CF_Proceeds from sale of property, plant and equipment',N'Proceeds from sale of property, plant and equipment',41857,140541),
('CASH','CF_Other investing cash received',N'Other investing cash received',88,0),
('CASH','CF_Purchase of property, plant and equipment',N'Purchase of property, plant and equipment',-14868842,-13796448),
('CASH','CF_TOTAL_INVESTING',N'Net cash from/(used by) investing activities',-14826897,-13655907),
('CASH','CF_Contributed equity',N'Contributed equity',14361748,13796448),
('CASH','CF_Principal payments of lease liabilities',N'Principal payments of lease liabilities',-395837,-283392),
('CASH','CF_TOTAL_FINANCING',N'Net cash from/(used by) financing activities',13965911,13513056),
('CASH','CF_NET',N'Net increase/(decrease) in cash held',-94306,-32040),
('CASH','CF_OPEN',N'Cash and cash equivalents at the beginning of the reporting period',427000,334253),
('CASH','CF_CLOSE',N'Cash and cash equivalents at the end of the reporting period',332349,302213);

DECLARE @AnnualRef NVARCHAR(500)=N'Defence Annual Report 2024-25, departmental primary statements - FY2024 comparative column';
DECLARE @BudgetRef NVARCHAR(500)=N'Defence Portfolio Budget Statements 2024-25, departmental budgeted financial statements - 2024-25 Budget column';
DECLARE @AnnualUrl NVARCHAR(1000)=N'https://www.defence.gov.au/sites/default/files/2025-10/Defence-Annual-Report-2024-25.pdf';
DECLARE @BudgetUrl NVARCHAR(1000)=N'https://www.defence.gov.au/about/strategic-planning/2024-25-portfolio-budget-statements';

BEGIN TRANSACTION;

UPDATE sf SET Amount=v.Amount,SourceReference=CASE WHEN v.FigureType='PriorActual' THEN @AnnualRef ELSE @BudgetRef END,
    SourceUrl=CASE WHEN v.FigureType='PriorActual' THEN @AnnualUrl ELSE @BudgetUrl END,IsDeactivated=0
FROM dbo.tblNORM_SourceFigure sf
JOIN
(
    SELECT StatementCode,LineCode,'PriorActual' FigureType,PriorAmount Amount FROM @Figures
    UNION ALL
    SELECT StatementCode,LineCode,'OriginalBudget',BudgetAmount FROM @Figures
) v ON sf.ConfigurationReleaseId=@ReleaseId AND sf.StatementCode=v.StatementCode
   AND sf.LineCode=v.LineCode AND sf.FigureType=v.FigureType;

INSERT dbo.tblNORM_SourceFigure
    (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
SELECT @ReleaseId,2025,'DEPT',v.StatementCode,v.LineCode,v.FigureType,v.Amount,
       CASE WHEN v.FigureType='PriorActual' THEN @AnnualRef ELSE @BudgetRef END,
       CASE WHEN v.FigureType='PriorActual' THEN @AnnualUrl ELSE @BudgetUrl END
FROM
(
    SELECT StatementCode,LineCode,'PriorActual' FigureType,PriorAmount Amount FROM @Figures
    UNION ALL
    SELECT StatementCode,LineCode,'OriginalBudget',BudgetAmount FROM @Figures
) v
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_SourceFigure sf WHERE sf.ConfigurationReleaseId=@ReleaseId
      AND sf.StatementCode=v.StatementCode AND sf.LineCode=v.LineCode AND sf.FigureType=v.FigureType
);

DECLARE @PriorDoc BIGINT=(SELECT TOP (1) d.YearSetupDocumentId FROM dbo.tblNORM_YearSetupDocument d
 JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
 WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
   AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.IsDeactivated=0 ORDER BY d.UploadedUtc DESC);
DECLARE @BudgetDoc BIGINT=(SELECT TOP (1) d.YearSetupDocumentId FROM dbo.tblNORM_YearSetupDocument d
 JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
 WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
   AND d.DocumentTypeCode='PortfolioBudgetStatements' AND d.IsDeactivated=0 ORDER BY d.UploadedUtc DESC);

IF @PriorDoc IS NOT NULL
BEGIN
    UPDATE yf SET Amount=f.PriorAmount,LineLabel=f.LineLabel,SourceLocator=N'Controlled Defence Annual Report 2024-25 FY2024 comparative',
        MatchConfidence=100,ReviewStatus='Confirmed',IsDeactivated=0
    FROM dbo.tblNORM_YearSetupFigure yf JOIN @Figures f ON yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode
    WHERE yf.YearSetupDocumentId=@PriorDoc;

    INSERT dbo.tblNORM_YearSetupFigure
        (YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
    SELECT @PriorDoc,'PriorActual',f.StatementCode,f.LineCode,f.LineLabel,f.PriorAmount,
        N'Controlled Defence Annual Report 2024-25 FY2024 comparative',100,'Confirmed'
    FROM @Figures f WHERE NOT EXISTS(SELECT 1 FROM dbo.tblNORM_YearSetupFigure yf
        WHERE yf.YearSetupDocumentId=@PriorDoc AND yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode);

    UPDATE yf SET IsDeactivated=1 FROM dbo.tblNORM_YearSetupFigure yf
    WHERE yf.YearSetupDocumentId=@PriorDoc AND yf.StatementCode IN ('SOCI','SOFP','SOCE','CASH')
      AND NOT EXISTS(SELECT 1 FROM @Figures f WHERE f.StatementCode=yf.StatementCode AND f.LineCode=yf.LineCode);

    UPDATE dbo.tblNORM_YearSetupDocument SET ExtractionStatus='Extracted',
        ExtractedFigureCount=(SELECT COUNT(*) FROM dbo.tblNORM_YearSetupFigure WHERE YearSetupDocumentId=@PriorDoc AND IsDeactivated=0),
        ExtractionDetail=N'All departmental comparative figures reconciled to the controlled FY2024 column in the uploaded Defence Annual Report 2024-25.'
    WHERE YearSetupDocumentId=@PriorDoc;
END;

IF @BudgetDoc IS NOT NULL
BEGIN
    UPDATE yf SET Amount=f.BudgetAmount,LineLabel=f.LineLabel,SourceLocator=N'Controlled Defence PBS 2024-25 Budget column',
        MatchConfidence=100,ReviewStatus='Confirmed',IsDeactivated=0
    FROM dbo.tblNORM_YearSetupFigure yf JOIN @Figures f ON yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode
    WHERE yf.YearSetupDocumentId=@BudgetDoc;

    INSERT dbo.tblNORM_YearSetupFigure
        (YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
    SELECT @BudgetDoc,'OriginalBudget',f.StatementCode,f.LineCode,f.LineLabel,f.BudgetAmount,
        N'Controlled Defence PBS 2024-25 Budget column',100,'Confirmed'
    FROM @Figures f WHERE NOT EXISTS(SELECT 1 FROM dbo.tblNORM_YearSetupFigure yf
        WHERE yf.YearSetupDocumentId=@BudgetDoc AND yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode);

    UPDATE yf SET IsDeactivated=1 FROM dbo.tblNORM_YearSetupFigure yf
    WHERE yf.YearSetupDocumentId=@BudgetDoc AND yf.StatementCode IN ('SOCI','SOFP','SOCE','CASH')
      AND NOT EXISTS(SELECT 1 FROM @Figures f WHERE f.StatementCode=yf.StatementCode AND f.LineCode=yf.LineCode);

    UPDATE dbo.tblNORM_YearSetupDocument SET ExtractionStatus='Extracted',
        ExtractedFigureCount=(SELECT COUNT(*) FROM dbo.tblNORM_YearSetupFigure WHERE YearSetupDocumentId=@BudgetDoc AND IsDeactivated=0),
        ExtractionDetail=N'All departmental original-budget figures reconciled to the controlled 2024-25 Budget column in the uploaded Defence Portfolio Budget Statements.'
    WHERE YearSetupDocumentId=@BudgetDoc;
END;

COMMIT TRANSACTION;

IF (SELECT PriorAmount FROM @Figures WHERE StatementCode='SOCI' AND LineCode='Total own-source income')<>2324309
    THROW 51042, 'Comparative Statement of Comprehensive Income control total failed.', 1;
IF (SELECT BudgetAmount FROM @Figures WHERE StatementCode='SOCI' AND LineCode='Depreciation and amortisation')<>6939017
    THROW 51043, 'Original-budget depreciation control failed.', 1;
IF (SELECT BudgetAmount FROM @Figures WHERE StatementCode='SOCI' AND LineCode='Write-down of non-financial assets')<>1621821
    THROW 51044, 'Original-budget write-down control failed.', 1;
IF (SELECT PriorAmount FROM @Figures WHERE StatementCode='SOFP' AND LineCode='Total assets')<>146458730
    THROW 51045, 'Comparative Statement of Financial Position control total failed.', 1;

SELECT COUNT(*) AS DepartmentalControlledLines,
       @PriorDoc AS PriorYearDocumentId,@BudgetDoc AS BudgetDocumentId
FROM @Figures;
