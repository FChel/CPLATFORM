/* NORM 17 - Defence administered schedules and note baselines. Idempotent; amounts are $'000. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];

IF OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL OR OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51030, 'Run the NORM preparation-control and start-of-year migrations before NORM 17.', 1;

DECLARE @ReleaseId INT=(SELECT TOP (1) ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease
 WHERE FinancialYear=2025 AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0 ORDER BY ConfigurationReleaseId DESC);
IF @ReleaseId IS NULL THROW 51031, 'Approved FY2025 DEPT release not found.', 1;

DECLARE @Figures TABLE(StatementCode VARCHAR(20),LineCode VARCHAR(200),LineLabel NVARCHAR(300),CurrentAmount DECIMAL(19,3) NULL,PriorAmount DECIMAL(19,3) NULL,BudgetAmount DECIMAL(19,3) NULL,PRIMARY KEY(StatementCode,LineCode));
INSERT @Figures VALUES
('ADMIN_SOCI','ADMIN_SOCI_EMPLOYEE',N'Employee benefits',10348132,9916047,9792331),
('ADMIN_SOCI','ADMIN_SOCI_SUBSIDIES',N'Subsidies',289412,366025,236275),
('ADMIN_SOCI','ADMIN_SOCI_IMPAIRMENT',N'Impairment loss allowance on financial instruments',39,0,0),
('ADMIN_SOCI','ADMIN_SOCI_TOTAL_EXPENSES',N'Total expenses',10637583,10282072,10028606),
('ADMIN_SOCI','ADMIN_SOCI_FEES',N'Fees and fines',25274,20964,23978),
('ADMIN_SOCI','ADMIN_SOCI_SUPER_CONTRIB',N'Military superannuation contributions',1102317,1129955,1067060),
('ADMIN_SOCI','ADMIN_SOCI_OTHER_REVENUE',N'Other revenue',48893,44015,41877),
('ADMIN_SOCI','ADMIN_SOCI_TOTAL_NONTAX',N'Total non-taxation revenue',1176484,1194934,1132915),
('ADMIN_SOCI','ADMIN_SOCI_TOTAL_INCOME',N'Total Income',1176484,1194934,1132915),
('ADMIN_SOCI','ADMIN_SOCI_NET_COST',N'Net (cost of) / contribution by services',-9461099,-9087138,-8895691),
('ADMIN_SOCI','ADMIN_SOCI_REVALUATION',N'Changes in asset revaluation surplus',362955,133731,NULL),
('ADMIN_SOCI','ADMIN_SOCI_ACTUARIAL',N'Actuarial gains / (losses) on defined benefits plans',3090000,5686600,NULL),
('ADMIN_SOCI','ADMIN_SOCI_TOTAL_OCI',N'Total other comprehensive income / (loss)',3452955,5820331,NULL),
('ADMIN_SOCI','ADMIN_SOCI_TOTAL_COMPREHENSIVE',N'Total comprehensive (loss) / income',-6008144,-3266807,-8895691),
('ADMIN_SOFP','ADMIN_SOFP_RECEIVABLES',N'Trade and other receivables',50103,43685,23978),
('ADMIN_SOFP','ADMIN_SOFP_INVESTMENTS',N'Equity accounted investments',4160995,3798039,3915985),
('ADMIN_SOFP','ADMIN_SOFP_TOTAL_FINANCIAL',N'Total financial assets',4211098,3841724,3939963),
('ADMIN_SOFP','ADMIN_SOFP_OTHER_NONFIN',N'Other non-financial assets',4972,20829,NULL),
('ADMIN_SOFP','ADMIN_SOFP_TOTAL_NONFIN',N'Total non-financial assets',4972,20829,NULL),
('ADMIN_SOFP','ADMIN_SOFP_TOTAL_ASSETS',N'Total assets administered on behalf of Government',4216070,3862553,3939963),
('ADMIN_SOFP','ADMIN_SOFP_OTHER_PAYABLES',N'Other payables',94631,106541,10004),
('ADMIN_SOFP','ADMIN_SOFP_TOTAL_PAYABLES',N'Total payables',94631,106541,10004),
('ADMIN_SOFP','ADMIN_SOFP_EMP_PROVISIONS',N'Employee provisions',141071500,138196100,138650045),
('ADMIN_SOFP','ADMIN_SOFP_TOTAL_PROVISIONS',N'Total provisions',141071500,138196100,138650045),
('ADMIN_SOFP','ADMIN_SOFP_TOTAL_LIABILITIES',N'Total liabilities administered on behalf of Government',141166131,138302641,138660049),
('ADMIN_SOFP','ADMIN_SOFP_NET_LIABILITIES',N'Net liabilities',-136950061,-134440088,-134720086),
('ADMIN_RECON','ADMIN_RECON_OPENING',N'Opening assets less liabilities as at 1 July',-134440088,-134108821,NULL),
('ADMIN_RECON','ADMIN_RECON_INCOME',N'Income',1176484,1194934,NULL),
('ADMIN_RECON','ADMIN_RECON_EXPENSES',N'Payments to entities other than corporate Commonwealth entities',-10637583,-10282072,NULL),
('ADMIN_RECON','ADMIN_RECON_DHA_REVAL',N'Revaluations - Defence Housing Australia',361037,135725,NULL),
('ADMIN_RECON','ADMIN_RECON_SMALL_REVAL',N'Revaluations - Small portfolio entities',1919,-1994,NULL),
('ADMIN_RECON','ADMIN_RECON_ACTUARIAL',N'Actuarial gains / (losses)',3090000,5686600,NULL),
('ADMIN_RECON','ADMIN_RECON_SPECIAL_LIMITED',N'Special appropriations (limited)',0,0,NULL),
('ADMIN_RECON','ADMIN_RECON_SPECIAL_UNLIMITED',N'Special appropriations (unlimited)',4774564,4271830,NULL),
('ADMIN_RECON','ADMIN_RECON_TO_OPA',N'Transfers to OPA',-1508432,-1489819,NULL),
('ADMIN_RECON','ADMIN_RECON_WRITE_OFF',N'Write off of liabilities',84799,0,NULL),
('ADMIN_RECON','ADMIN_RECON_FUNDED_BENEFITS',N'Funded benefit payments',147239,153529,NULL),
('ADMIN_RECON','ADMIN_RECON_CLOSING',N'Closing assets less liabilities as at 30 June',-136950061,-134440088,NULL),
('ADMIN_CASH','ADMIN_CASH_FEES',N'Fees',21408,18964,21614),
('ADMIN_CASH','ADMIN_CASH_SUPER_CONTRIB',N'Superannuation contributions',1437668,1405144,1349877),
('ADMIN_CASH','ADMIN_CASH_OTHER_RECEIVED',N'Other',49356,65711,41665),
('ADMIN_CASH','ADMIN_CASH_RECEIVED_TOTAL',N'Total cash received',1508432,1489819,1413156),
('ADMIN_CASH','ADMIN_CASH_SUBSIDIES',N'Subsidies',-215338,-270683,-236275),
('ADMIN_CASH','ADMIN_CASH_EMPLOYEES',N'Employees',-4559226,-4001147,-4453924),
('ADMIN_CASH','ADMIN_CASH_USED_TOTAL',N'Total cash used',-4774564,-4271830,-4690199),
('ADMIN_CASH','ADMIN_CASH_OPERATING_NET',N'Net cash (used by) operating activities',-3266132,-2782011,-3277043),
('ADMIN_CASH','ADMIN_CASH_DIVIDENDS',N'Dividends',0,0,0),
('ADMIN_CASH','ADMIN_CASH_INVESTING_NET',N'Net cash from investing activities',0,0,0),
('ADMIN_CASH','ADMIN_CASH_NET_DECREASE',N'Net (decrease) in cash held',-3266132,-2782011,-3277043),
('ADMIN_CASH','ADMIN_CASH_OPA_FROM',N'Cash from the Official Public Account for appropriations',4774564,4271830,4690199),
('ADMIN_CASH','ADMIN_CASH_OPA_FROM_TOTAL',N'Total cash from Official Public Account',4774564,4271830,4690199),
('ADMIN_CASH','ADMIN_CASH_OPA_TO',N'Cash to Official Public Account - appropriations',-1508432,-1489819,-1413156),
('ADMIN_CASH','ADMIN_CASH_OPA_TO_TOTAL',N'Total cash to Official Public Account',-1508432,-1489819,-1413156),
('ADMIN_CASH','ADMIN_CASH_OPEN',N'Cash and cash equivalents at the beginning of the reporting period',0,0,0),
('ADMIN_CASH','ADMIN_CASH_TRANSFER_DEPT',N'Transfer to Departmental',0,0,0),
('ADMIN_CASH','ADMIN_CASH_CLOSE',N'Cash and cash equivalents at the end of the reporting period',0,0,0),
('ADMIN_NOTE_2','ADMIN_N2_SERVICE_COST',N'Net service cost',3804000,3877000,NULL),
('ADMIN_NOTE_2','ADMIN_N2_INTEREST_COST',N'Net interest cost',6527971,6000461,NULL),
('ADMIN_NOTE_2','ADMIN_N2_RETENTION',N'Retention benefits',16161,38586,NULL),
('ADMIN_NOTE_2','ADMIN_N2_TOTAL_EMPLOYEE',N'Total employee benefits',10348132,9916047,NULL),
('ADMIN_NOTE_2','ADMIN_N2_DHOAS',N'Defence Home Ownership Assistance Scheme',289412,366025,NULL),
('ADMIN_NOTE_2','ADMIN_N2_TOTAL_SUBSIDIES',N'Total subsidies',289412,366025,NULL),
('ADMIN_NOTE_2','ADMIN_N2_IMPAIRMENT',N'Impairment on trade and other receivables',39,0,NULL),
('ADMIN_NOTE_2','ADMIN_N2_TOTAL_IMPAIRMENT',N'Total impairment loss allowance on financial instruments',39,0,NULL),
('ADMIN_NOTE_2','ADMIN_N2_FEES',N'Licence fees',25274,20964,NULL),
('ADMIN_NOTE_2','ADMIN_N2_SUPER_CONTRIB',N'Military superannuation contributions',1102317,1129955,NULL),
('ADMIN_NOTE_2','ADMIN_N2_DHA_REVENUE',N'Competitive neutrality revenue - Defence Housing Australia',48893,44013,NULL),
('ADMIN_NOTE_2','ADMIN_N2_OTHER_REVENUE',N'Other',0,2,NULL),
('ADMIN_NOTE_2','ADMIN_N2_TOTAL_OTHER_REVENUE',N'Total other revenue',48893,44015,NULL),
('ADMIN_NOTE_4','ADMIN_N4_EXTERNAL_RECEIVABLES',N'In connection with - external parties',50180,43762,NULL),
('ADMIN_NOTE_4','ADMIN_N4_GROSS_RECEIVABLES',N'Total trade and other receivables (gross)',50180,43762,NULL),
('ADMIN_NOTE_4','ADMIN_N4_IMPAIRMENT',N'Total impairment allowance',-77,-77,NULL),
('ADMIN_NOTE_4','ADMIN_N4_NET_RECEIVABLES',N'Total trade and other receivables (net)',50103,43685,NULL),
('ADMIN_NOTE_4','ADMIN_N4_DHA_INVESTMENT',N'Investments in Defence Housing Australia',4030375,3669338,NULL),
('ADMIN_NOTE_4','ADMIN_N4_SMALL_INVESTMENTS',N'Investments in other small portfolio entities',130620,128701,NULL),
('ADMIN_NOTE_4','ADMIN_N4_TOTAL_INVESTMENTS',N'Total equity accounted investments',4160995,3798039,NULL),
('ADMIN_NOTE_4','ADMIN_N4_PREPAYMENTS',N'Other non-financial assets',4972,20829,NULL),
('ADMIN_NOTE_4','ADMIN_N4_PAYABLES',N'Other payables',94631,106541,NULL),
('ADMIN_NOTE_4','ADMIN_N4_PROVISIONS',N'Employee provisions',141071500,138196100,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_RECEIVABLES',N'Trade and other receivables',50103,43685,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_AMORTISED_ASSETS',N'Total financial assets at amortised cost',50103,43685,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_DHA',N'Investment in Defence Housing Australia',4030375,3669338,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_SMALL',N'Investment in other small portfolio bodies',130620,128701,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_FVOCI',N'Total financial assets at fair value through other comprehensive income',4160995,3798039,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_ASSETS',N'Carrying amount of financial assets',4211098,3841724,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_PAYABLES',N'Other payables',94631,106541,NULL),
('ADMIN_NOTE_7_3','ADMIN_N73_LIABILITIES',N'Carrying amount of financial liabilities',94631,106541,NULL),
('ADMIN_NOTE_7_5','ADMIN_N75_INVESTMENT',N'Administered Investment',4160995,3798039,NULL),
('ADMIN_NOTE_7_5','ADMIN_N75_TOTAL_ASSETS',N'Total financial assets',4160995,3798039,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_CURR_RECEIVABLES',N'Trade and other receivables',50103,43685,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_CURR_PREPAYMENTS',N'Prepayments - no more than 12 months',1117,8734,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_CURR_ASSETS',N'Total no more than 12 months - assets',51220,52419,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_NONCURR_INVESTMENTS',N'Equity accounted investments',4160995,3798039,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_NONCURR_PREPAYMENTS',N'Prepayments - more than 12 months',3855,12095,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_NONCURR_ASSETS',N'Total more than 12 months - assets',4164850,3810134,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_TOTAL_ASSETS',N'Total assets',4216070,3862553,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_CURR_PAYABLES',N'Other payables',94631,106541,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_CURR_PROVISIONS',N'Employee provisions - no more than 12 months',4403000,4038800,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_CURR_LIABILITIES',N'Total no more than 12 months - liabilities',4497631,4145341,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_NONCURR_PROVISIONS',N'Employee provisions - more than 12 months',136668500,134157300,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_NONCURR_LIABILITIES',N'Total more than 12 months - liabilities',136668500,134157300,NULL),
('ADMIN_NOTE_8_2B','ADMIN_N82_TOTAL_LIABILITIES',N'Total liabilities',141166131,138302641,NULL);

DECLARE @AnnualRef NVARCHAR(500)=N'Defence Annual Report 2024-25, administered schedules and notes';
DECLARE @BudgetRef NVARCHAR(500)=N'Defence Portfolio Budget Statements 2024-25, administered budgeted financial statements';
DECLARE @SourceUrl NVARCHAR(1000)=N'https://www.defence.gov.au/about/reviews-inquiries/annual-reports';

BEGIN TRANSACTION;
UPDATE sf SET Amount=v.Amount,SourceReference=CASE WHEN v.FigureType='OriginalBudget' THEN @BudgetRef ELSE @AnnualRef END,SourceUrl=@SourceUrl,IsDeactivated=0
FROM dbo.tblNORM_SourceFigure sf JOIN
(SELECT StatementCode,LineCode,'AuditedActual' FigureType,CurrentAmount Amount FROM @Figures WHERE CurrentAmount IS NOT NULL UNION ALL
 SELECT StatementCode,LineCode,'PriorActual',PriorAmount FROM @Figures WHERE PriorAmount IS NOT NULL UNION ALL
 SELECT StatementCode,LineCode,'OriginalBudget',BudgetAmount FROM @Figures WHERE BudgetAmount IS NOT NULL) v
ON sf.ConfigurationReleaseId=@ReleaseId AND sf.StatementCode=v.StatementCode AND sf.LineCode=v.LineCode AND sf.FigureType=v.FigureType;

INSERT dbo.tblNORM_SourceFigure(ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
SELECT @ReleaseId,2025,'DEPT',v.StatementCode,v.LineCode,v.FigureType,v.Amount,CASE WHEN v.FigureType='OriginalBudget' THEN @BudgetRef ELSE @AnnualRef END,@SourceUrl
FROM (SELECT StatementCode,LineCode,'AuditedActual' FigureType,CurrentAmount Amount FROM @Figures WHERE CurrentAmount IS NOT NULL UNION ALL
 SELECT StatementCode,LineCode,'PriorActual',PriorAmount FROM @Figures WHERE PriorAmount IS NOT NULL UNION ALL
 SELECT StatementCode,LineCode,'OriginalBudget',BudgetAmount FROM @Figures WHERE BudgetAmount IS NOT NULL) v
WHERE NOT EXISTS(SELECT 1 FROM dbo.tblNORM_SourceFigure sf WHERE sf.ConfigurationReleaseId=@ReleaseId AND sf.StatementCode=v.StatementCode AND sf.LineCode=v.LineCode AND sf.FigureType=v.FigureType);

DECLARE @PriorDoc BIGINT=(SELECT TOP (1) d.YearSetupDocumentId FROM dbo.tblNORM_YearSetupDocument d JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
 WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0 AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.IsDeactivated=0 ORDER BY d.UploadedUtc DESC);
DECLARE @BudgetDoc BIGINT=(SELECT TOP (1) d.YearSetupDocumentId FROM dbo.tblNORM_YearSetupDocument d JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
 WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0 AND d.DocumentTypeCode='PortfolioBudgetStatements' AND d.IsDeactivated=0 ORDER BY d.UploadedUtc DESC);

IF @PriorDoc IS NOT NULL
BEGIN
 UPDATE yf SET Amount=f.PriorAmount,LineLabel=f.LineLabel,SourceLocator=N'Defence Annual Report 2024-25 administered schedule/note',MatchConfidence=100,ReviewStatus='Confirmed',IsDeactivated=0
 FROM dbo.tblNORM_YearSetupFigure yf JOIN @Figures f ON yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode WHERE yf.YearSetupDocumentId=@PriorDoc AND f.PriorAmount IS NOT NULL;
 INSERT dbo.tblNORM_YearSetupFigure(YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
 SELECT @PriorDoc,'PriorActual',f.StatementCode,f.LineCode,f.LineLabel,f.PriorAmount,N'Defence Annual Report 2024-25 administered schedule/note',100,'Confirmed' FROM @Figures f
 WHERE f.PriorAmount IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.tblNORM_YearSetupFigure yf WHERE yf.YearSetupDocumentId=@PriorDoc AND yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode);
END;
IF @BudgetDoc IS NOT NULL
BEGIN
 UPDATE yf SET Amount=f.BudgetAmount,LineLabel=f.LineLabel,SourceLocator=N'Defence PBS 2024-25 administered budget column',MatchConfidence=100,ReviewStatus='Confirmed',IsDeactivated=0
 FROM dbo.tblNORM_YearSetupFigure yf JOIN @Figures f ON yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode WHERE yf.YearSetupDocumentId=@BudgetDoc AND f.BudgetAmount IS NOT NULL;
 INSERT dbo.tblNORM_YearSetupFigure(YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
 SELECT @BudgetDoc,'OriginalBudget',f.StatementCode,f.LineCode,f.LineLabel,f.BudgetAmount,N'Defence PBS 2024-25 administered budget column',100,'Confirmed' FROM @Figures f
 WHERE f.BudgetAmount IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.tblNORM_YearSetupFigure yf WHERE yf.YearSetupDocumentId=@BudgetDoc AND yf.StatementCode=f.StatementCode AND yf.LineCode=f.LineCode);
END;
COMMIT TRANSACTION;

SELECT COUNT(*) AS AdministeredSourceFigures FROM dbo.tblNORM_SourceFigure WHERE ConfigurationReleaseId=@ReleaseId AND StatementCode LIKE 'ADMIN[_]%' AND IsDeactivated=0;
