/* NORM 06 - preparation control centre, materiality and public-source baselines.
   Idempotent. Amounts are $'000 and are kept separate from immutable TB results. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.tblNORM_ReportingProfile','OverallMateriality') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_ReportingProfile ADD OverallMateriality DECIMAL(19,3) NULL;');
IF COL_LENGTH('dbo.tblNORM_ReportingProfile','PerformanceMateriality') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_ReportingProfile ADD PerformanceMateriality DECIMAL(19,3) NULL;');
IF COL_LENGTH('dbo.tblNORM_ReportingProfile','ClearlyTrivialThreshold') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_ReportingProfile ADD ClearlyTrivialThreshold DECIMAL(19,3) NULL;');
IF COL_LENGTH('dbo.tblNORM_ReportingProfile','BudgetVarianceThreshold') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_ReportingProfile ADD BudgetVarianceThreshold DECIMAL(19,3) NULL;');
IF COL_LENGTH('dbo.tblNORM_ReportingProfile','QualitativeConsiderations') IS NULL
    EXEC(N'ALTER TABLE dbo.tblNORM_ReportingProfile ADD QualitativeConsiderations NVARCHAR(2000) NULL;');

IF OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_SourceFigure (
        SourceFigureId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_SourceFigure PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        FinancialYear INT NOT NULL,
        EntityCode VARCHAR(20) NOT NULL,
        StatementCode VARCHAR(20) NOT NULL,
        LineCode VARCHAR(200) NOT NULL,
        FigureType VARCHAR(30) NOT NULL,
        Amount DECIMAL(19,3) NOT NULL,
        SourceReference NVARCHAR(500) NOT NULL,
        SourceUrl NVARCHAR(1000) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_SourceFigure_Deactivated DEFAULT(0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_SourceFigure_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT UQ_tblNORM_SourceFigure UNIQUE(ConfigurationReleaseId,StatementCode,LineCode,FigureType),
        CONSTRAINT CK_tblNORM_SourceFigure_Type CHECK(FigureType IN ('AuditedActual','PriorActual','OriginalBudget')),
        CONSTRAINT FK_tblNORM_SourceFigure_Release FOREIGN KEY(ConfigurationReleaseId)
            REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId)
    );
END;

DECLARE @release INT = (
    SELECT TOP 1 ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease
    WHERE FinancialYear=2025 AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0
    ORDER BY ConfigurationReleaseId DESC
);

IF @release IS NOT NULL
BEGIN
    INSERT dbo.tblNORM_SourceFigure
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
    SELECT @release,2025,'DEPT',v.StatementCode,v.LineCode,'OriginalBudget',v.Amount,
        N'Defence Annual Report 2024-25, departmental primary statements - Original Budget',
        N'https://www.defence.gov.au/about/accessing-information/annual-reports'
    FROM (VALUES
        ('SOCI','Employee benefits',15190028.000),('SOCI','Supplier expenses',25057086.000),
        ('SOCI','Grants',49842.000),('SOCI','Finance costs',126015.000),
        ('SOCI','Impairment loss on financial instruments',0.000),('SOCI','Write-down of non-financial assets',1621821.000),
        ('SOCI','Expenses in relation to special accounts',58227.000),('SOCI','Other expenses',255647.000),
        ('SOCI','Foreign exchange',0.000),('SOCI','Depreciation and amortisation',6939017.000),
        ('SOCI','Revenue from contracts with customers',368382.000),('SOCI','Revenue in relation to special accounts',297555.000),
        ('SOCI','Rental income',243642.000),('SOCI','Other revenue',74196.000),
        ('SOCI','Gain on sale of asset',43961.000),('SOCI','Reversals of previous asset write-downs',919361.000),
        ('SOCI','Other gains',0.000),('SOCI','Revenue from Government',38784076.000),
        ('SOCI','Total expenses',49297683.000),('SOCI','Total own-source income',1947097.000),
        ('SOCI','Net cost of services',47350586.000),('SOCI','Operating result',-8566510.000),
        ('SOCI','TOTAL_OSR',983775.000),('SOCI','TOTAL_GAINS',963322.000),
        ('SOFP','Cash and cash equivalents',302213.000),('SOFP','Trade and other receivables',2579765.000),
        ('SOFP','Property plant and equipment',131432388.000),('SOFP','Inventories',9664481.000),
        ('SOFP','Prepayments',4243307.000),('SOFP','Assets held for sale',76598.000),
        ('SOFP','Suppliers payables',5896679.000),('SOFP','Employee payables',337093.000),
        ('SOFP','Other payables',378158.000),('SOFP','Leases',3362436.000),
        ('SOFP','Employee provisions',3580316.000),('SOFP','Asset restoration provisions',1087208.000),
        ('SOFP','Other provisions',305933.000),
        ('SOFP','Total assets',148298752.000),('SOFP','Total liabilities',14947823.000),
        ('SOFP','Net assets',133350929.000),('SOFP','Statement of Changes in Equity',133350929.000),
        ('SOCE','SOCE_OPEN',128120992.000),('SOCE','SOCE_RESULT',-8566510.000),
        ('SOCE','SOCE_OWNER',13796448.000),('SOCE','SOCE_CLOSE',133350929.000),
        ('CASH','CF_Appropriations',38470706.000),('CASH','CF_Sale of goods and rendering of services',612022.000),
        ('CASH','CF_GST received',2936945.000),('CASH','CF_Other cash received',347350.000),
        ('CASH','CF_Employees',-14876659.000),('CASH','CF_Suppliers',-23705914.000),
        ('CASH','CF_GST paid',-2936945.000),('CASH','CF_Grants',-49842.000),
        ('CASH','CF_Other cash used',-686852.000),('CASH','CF_TOTAL_OPERATING',110811.000),
        ('CASH','CF_Proceeds from sale of property, plant and equipment',140541.000),
        ('CASH','CF_Purchase of property, plant and equipment',-13796448.000),('CASH','CF_TOTAL_INVESTING',-13655907.000),
        ('CASH','CF_Contributed equity',13796448.000),('CASH','CF_Principal payments of lease liabilities',-283392.000),
        ('CASH','CF_TOTAL_FINANCING',13513056.000),('CASH','CF_NET',-32040.000),
        ('CASH','CF_OPEN',334253.000),('CASH','CF_CLOSE',302213.000)
    ) v(StatementCode,LineCode,Amount)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.tblNORM_SourceFigure f WHERE f.ConfigurationReleaseId=@release
        AND f.StatementCode=v.StatementCode AND f.LineCode=v.LineCode AND f.FigureType='OriginalBudget'
    );

    INSERT dbo.tblNORM_SourceFigure
        (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
    SELECT @release,2025,'DEPT',p.StatementCode,p.LineCode,'PriorActual',p.AmountPrior,
        N'Defence Annual Report 2024-25 - audited 2024 comparative',
        N'https://www.defence.gov.au/about/accessing-information/annual-reports'
    FROM dbo.tblNORM_PublishedFigure p
    WHERE p.ConfigurationReleaseId=@release AND p.AmountPrior IS NOT NULL AND p.IsDeactivated=0
      AND NOT EXISTS (SELECT 1 FROM dbo.tblNORM_SourceFigure f WHERE f.ConfigurationReleaseId=@release
          AND f.StatementCode=p.StatementCode AND f.LineCode=p.LineCode AND f.FigureType='PriorActual');
END;

COMMIT TRANSACTION;
PRINT 'NORM preparation control centre objects and public-source baselines applied.';
