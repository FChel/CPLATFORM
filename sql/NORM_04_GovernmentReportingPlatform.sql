/* ============================================================================
   NORM government financial reporting platform

   Adds the entity reporting profile, conditional PRIMA disclosure catalogue,
   editable run narratives and collaborative review workflow. Safe to rerun.

   Run after NORM_02_FY2025_Promote.sql. This script is additive only.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.tblNORM_ReportingProfile', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ReportingProfile (
        ReportingProfileId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_ReportingProfile PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        EntityTypeCode VARCHAR(30) NOT NULL CONSTRAINT DF_tblNORM_Profile_EntityType DEFAULT ('NCE'),
        ReportingBasisCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_Profile_Basis DEFAULT ('GPFS'),
        DisclosureTierCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_Profile_Tier DEFAULT ('FULL'),
        MaterialityBasis NVARCHAR(1000) NULL,
        UpdatedBy NVARCHAR(256) NOT NULL,
        UpdatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_Profile_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_Profile_IsDeactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_ReportingProfile_Release UNIQUE (ConfigurationReleaseId),
        CONSTRAINT CK_tblNORM_Profile_EntityType CHECK (EntityTypeCode IN ('NCE','CCE','COMMONWEALTH_COMPANY')),
        CONSTRAINT CK_tblNORM_Profile_Basis CHECK (ReportingBasisCode IN ('GPFS','SPFS')),
        CONSTRAINT CK_tblNORM_Profile_Tier CHECK (DisclosureTierCode IN ('FULL','REDUCED'))
    );
END;

IF OBJECT_ID('dbo.tblNORM_RequirementSelection', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_RequirementSelection (
        RequirementSelectionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_RequirementSelection PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        CapabilityCode VARCHAR(50) NOT NULL,
        IsRequired BIT NOT NULL,
        Rationale NVARCHAR(1000) NULL,
        UpdatedBy NVARCHAR(256) NOT NULL,
        UpdatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_Requirement_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_Requirement_IsDeactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_RequirementSelection UNIQUE (ConfigurationReleaseId,CapabilityCode)
    );
END;

IF OBJECT_ID('dbo.tblNORM_DisclosureRule', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_DisclosureRule (
        DisclosureRuleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_DisclosureRule PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        DisclosureCode VARCHAR(40) NOT NULL,
        SectionCode VARCHAR(10) NOT NULL,
        SectionTitle NVARCHAR(200) NOT NULL,
        NoteRef VARCHAR(20) NULL,
        DisclosureTitle NVARCHAR(300) NOT NULL,
        TriggerCode VARCHAR(50) NOT NULL,
        IsBaseRequired BIT NOT NULL CONSTRAINT DF_tblNORM_DisclosureRule_Base DEFAULT (0),
        RequiresNarrative BIT NOT NULL CONSTRAINT DF_tblNORM_DisclosureRule_Narrative DEFAULT (0),
        SortOrder INT NOT NULL,
        GuidanceText NVARCHAR(2000) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_DisclosureRule_IsDeactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_DisclosureRule UNIQUE (ConfigurationReleaseId,DisclosureCode)
    );
END;

IF OBJECT_ID('dbo.tblNORM_NarrativeTemplate', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_NarrativeTemplate (
        NarrativeTemplateId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_NarrativeTemplate PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        DisclosureCode VARCHAR(40) NOT NULL,
        NarrativeType VARCHAR(30) NOT NULL,
        TemplateText NVARCHAR(MAX) NOT NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_NarrativeTemplate_IsDeactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_NarrativeTemplate UNIQUE (ConfigurationReleaseId,DisclosureCode,NarrativeType)
    );
END;

IF OBJECT_ID('dbo.tblNORM_RunNarrative', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_RunNarrative (
        RunNarrativeId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_RunNarrative PRIMARY KEY,
        CalculationRunId INT NOT NULL,
        DisclosureCode VARCHAR(40) NOT NULL,
        NarrativeType VARCHAR(30) NOT NULL,
        NarrativeText NVARCHAR(MAX) NOT NULL,
        StatusCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_RunNarrative_Status DEFAULT ('Draft'),
        UpdatedBy NVARCHAR(256) NOT NULL,
        UpdatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_RunNarrative_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_RunNarrative_IsDeactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_RunNarrative UNIQUE (CalculationRunId,DisclosureCode,NarrativeType),
        CONSTRAINT CK_tblNORM_RunNarrative_Status CHECK (StatusCode IN ('Draft','Prepared','Reviewed','Approved'))
    );
END;

IF OBJECT_ID('dbo.tblNORM_WorkflowItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_WorkflowItem (
        WorkflowItemId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_WorkflowItem PRIMARY KEY,
        CalculationRunId INT NOT NULL,
        ModuleCode VARCHAR(40) NOT NULL,
        ItemCode VARCHAR(60) NOT NULL,
        ItemLabel NVARCHAR(300) NOT NULL,
        OwnerUserId NVARCHAR(256) NULL,
        ReviewerUserId NVARCHAR(256) NULL,
        StatusCode VARCHAR(20) NOT NULL CONSTRAINT DF_tblNORM_WorkflowItem_Status DEFAULT ('NotStarted'),
        DueDate DATE NULL,
        Commentary NVARCHAR(2000) NULL,
        UpdatedBy NVARCHAR(256) NOT NULL,
        UpdatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_WorkflowItem_UpdatedUtc DEFAULT (SYSUTCDATETIME()),
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_WorkflowItem_IsDeactivated DEFAULT (0),
        CONSTRAINT UQ_tblNORM_WorkflowItem UNIQUE (CalculationRunId,ModuleCode,ItemCode),
        CONSTRAINT CK_tblNORM_WorkflowItem_Status CHECK (StatusCode IN ('NotStarted','InProgress','Prepared','Reviewed','Approved','Blocked'))
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_ReportingProfile_Release')
    ALTER TABLE dbo.tblNORM_ReportingProfile ADD CONSTRAINT FK_tblNORM_ReportingProfile_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_RequirementSelection_Release')
    ALTER TABLE dbo.tblNORM_RequirementSelection ADD CONSTRAINT FK_tblNORM_RequirementSelection_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_DisclosureRule_Release')
    ALTER TABLE dbo.tblNORM_DisclosureRule ADD CONSTRAINT FK_tblNORM_DisclosureRule_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_NarrativeTemplate_Release')
    ALTER TABLE dbo.tblNORM_NarrativeTemplate ADD CONSTRAINT FK_tblNORM_NarrativeTemplate_Release FOREIGN KEY (ConfigurationReleaseId) REFERENCES dbo.tblNORM_ConfigurationRelease(ConfigurationReleaseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_RunNarrative_Run')
    ALTER TABLE dbo.tblNORM_RunNarrative ADD CONSTRAINT FK_tblNORM_RunNarrative_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblNORM_WorkflowItem_Run')
    ALTER TABLE dbo.tblNORM_WorkflowItem ADD CONSTRAINT FK_tblNORM_WorkflowItem_Run FOREIGN KEY (CalculationRunId) REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId);

DECLARE @ReleaseId INT = (
    SELECT ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease
    WHERE FinancialYear = 2025 AND EntityCode = 'DEPT' AND VersionCode = 'v1.0' AND IsDeactivated = 0
);

IF @ReleaseId IS NULL
    THROW 51400, 'FY2025 NORM configuration release is required before installing the reporting platform.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ReportingProfile WHERE ConfigurationReleaseId = @ReleaseId)
    INSERT dbo.tblNORM_ReportingProfile
        (ConfigurationReleaseId,EntityTypeCode,ReportingBasisCode,DisclosureTierCode,MaterialityBasis,UpdatedBy)
    VALUES
        (@ReleaseId,'NCE','GPFS','FULL',N'Materiality is assessed quantitatively and qualitatively for each class of transactions, account balance and disclosure.',N'NORM deployment');

DECLARE @Requirements TABLE (CapabilityCode VARCHAR(50), IsRequired BIT, Rationale NVARCHAR(1000));
INSERT @Requirements VALUES
 ('APPROPRIATIONS',1,N'Non-corporate Commonwealth entity funded through appropriations.'),
 ('ADMINISTERED_ACTIVITIES',1,N'The entity administers activities on behalf of Government.'),
 ('SPECIAL_ACCOUNTS',1,N'Special account balances and transactions are reported.'),
 ('INVESTMENTS',1,N'Investment and financial asset balances are present.'),
 ('CONSOLIDATION',0,N'Enable where controlled entities are consolidated.'),
 ('HERITAGE_ASSETS',1,N'Heritage and cultural assets form part of the asset base.'),
 ('INTANGIBLE_ASSETS',1,N'Intangible assets form part of the asset base.'),
 ('MILITARY_ASSETS',1,N'Specialist military equipment forms part of the asset base.'),
 ('BIOLOGICAL_ASSETS',0,N'Enable when biological assets are held.'),
 ('SERVICE_CONCESSIONS',0,N'Enable when service concession arrangements apply.'),
 ('LEASES',1,N'Lease assets, liabilities and cash flows are present.'),
 ('GRANTS',1,N'Grant expenses or income are present.'),
 ('CONCESSIONAL_LOANS',0,N'Enable when concessional loans are issued or held.'),
 ('FINANCIAL_INSTRUMENTS',1,N'Financial instruments and associated risks require disclosure.'),
 ('INVENTORIES',1,N'Inventory balances are present.'),
 ('CASH_ADMINISTERED',1,N'Cash is administered on behalf of Government.'),
 ('EMPLOYEE_BENEFITS',1,N'Employee expenses and provisions are present.'),
 ('FAIR_VALUE',1,N'Assets are measured at fair value.'),
 ('CONTINGENCIES',1,N'Contingent assets and liabilities require annual assessment.'),
 ('OUTCOMES_REPORTING',1,N'Annual report outcome reporting is required.');

INSERT dbo.tblNORM_RequirementSelection
    (ConfigurationReleaseId,CapabilityCode,IsRequired,Rationale,UpdatedBy)
SELECT @ReleaseId,r.CapabilityCode,r.IsRequired,r.Rationale,N'NORM deployment'
FROM @Requirements r
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.tblNORM_RequirementSelection x
    WHERE x.ConfigurationReleaseId=@ReleaseId AND x.CapabilityCode=r.CapabilityCode
);

DECLARE @Rules TABLE (
    DisclosureCode VARCHAR(40), SectionCode VARCHAR(10), SectionTitle NVARCHAR(200), NoteRef VARCHAR(20),
    DisclosureTitle NVARCHAR(300), TriggerCode VARCHAR(50), IsBaseRequired BIT, RequiresNarrative BIT,
    SortOrder INT, GuidanceText NVARCHAR(2000)
);
INSERT @Rules VALUES
 ('PRIMARY_SOCI','P',N'Primary financial statements',NULL,N'Statement of Comprehensive Income','ALWAYS',1,0,10,N'PRIMA primary statement with income, expenses, gains and other comprehensive income.'),
 ('PRIMARY_SOFP','P',N'Primary financial statements',NULL,N'Statement of Financial Position','ALWAYS',1,0,20,N'PRIMA primary statement separating financial assets, non-financial assets, payables, interest-bearing liabilities, provisions and equity.'),
 ('PRIMARY_SOCE','P',N'Primary financial statements',NULL,N'Statement of Changes in Equity','ALWAYS',1,0,30,N'PRIMA reconciliation of each component of equity.'),
 ('PRIMARY_CASH','P',N'Primary financial statements',NULL,N'Cash Flow Statement','ALWAYS',1,0,40,N'PRIMA cash flows classified as operating, investing and financing.'),
 ('ADMIN_SOCI','P',N'Administered schedules',NULL,N'Administered Schedule of Comprehensive Income','ADMINISTERED_ACTIVITIES',0,0,50,N'Required when activities are administered on behalf of Government.'),
 ('ADMIN_SOFP','P',N'Administered schedules',NULL,N'Administered Schedule of Assets and Liabilities','ADMINISTERED_ACTIVITIES',0,0,60,N'Required when administered assets or liabilities exist.'),
 ('ADMIN_RECON','P',N'Administered schedules',NULL,N'Administered Reconciliation Schedule','ADMINISTERED_ACTIVITIES',0,0,70,N'Reconciles administered assets and liabilities.'),
 ('ADMIN_CASH','P',N'Administered schedules',NULL,N'Administered Cash Flow Statement','ADMINISTERED_ACTIVITIES',0,0,80,N'Required when administered cash flows exist.'),
 ('OVERVIEW','0',N'Overview',NULL,N'Overview and basis of preparation','ALWAYS',1,1,100,N'Entity-specific basis, objectives, legislation, tax and events after reporting period.'),
 ('N1_1A','1',N'Departmental financial performance','1.1A',N'Employee benefits','EMPLOYEE_BENEFITS',0,1,110,N'Wages and salaries, superannuation, leave and other employee benefits.'),
 ('N1_1B','1',N'Departmental financial performance','1.1B',N'Suppliers','ALWAYS',1,1,120,N'Supplier expense classes and leasing-related short-term or low-value expenses.'),
 ('N1_1C','1',N'Departmental financial performance','1.1C',N'Grants','GRANTS',0,1,130,N'Grant expense classes and recognition policy.'),
 ('N1_1D','1',N'Departmental financial performance','1.1D',N'Finance costs','ALWAYS',1,1,140,N'Interest and unwinding of discounts.'),
 ('N1_1E','1',N'Departmental financial performance','1.1E',N'Impairment loss on financial instruments','FINANCIAL_INSTRUMENTS',0,1,150,N'Expected credit losses by financial asset class.'),
 ('N1_1F','1',N'Departmental financial performance','1.1F',N'Write-down and impairment of other assets','ALWAYS',1,1,160,N'Inventory write-downs and impairment of non-financial assets.'),
 ('N1_1G','1',N'Departmental financial performance','1.1G',N'Foreign exchange losses','ALWAYS',0,1,170,N'Foreign exchange losses where material.'),
 ('N1_1H','1',N'Departmental financial performance','1.1H',N'Other expenses','ALWAYS',1,1,180,N'Material other expense classes.'),
 ('N1_2A','1',N'Departmental financial performance','1.2A',N'Revenue from contracts with customers','ALWAYS',1,1,210,N'Revenue disaggregation and performance obligations.'),
 ('N1_2B','1',N'Departmental financial performance','1.2B',N'Fees and fines','ALWAYS',0,1,220,N'Fees and fines where applicable.'),
 ('N1_2C','1',N'Departmental financial performance','1.2C',N'Interest','INVESTMENTS',0,1,230,N'Interest income by asset class.'),
 ('N1_2D','1',N'Departmental financial performance','1.2D',N'Dividends','INVESTMENTS',0,1,240,N'Dividend income where applicable.'),
 ('N1_2E','1',N'Departmental financial performance','1.2E',N'Rental income','LEASES',0,1,250,N'Rental income and maturity analysis of operating and finance lease receivables.'),
 ('N1_2F','1',N'Departmental financial performance','1.2F',N'Other revenue','ALWAYS',1,1,260,N'Material other revenue classes.'),
 ('N1_2G','1',N'Departmental financial performance','1.2G',N'Foreign exchange gains','ALWAYS',0,1,270,N'Foreign exchange gains where material.'),
 ('N1_2H','1',N'Departmental financial performance','1.2H',N'Reversal of write-downs and impairments','ALWAYS',0,1,280,N'Reversals by asset class.'),
 ('N1_2I','1',N'Departmental financial performance','1.2I',N'Other gains','ALWAYS',0,1,290,N'Material other gains.'),
 ('N1_2J','1',N'Departmental financial performance','1.2J',N'Revenue from Government','APPROPRIATIONS',0,1,300,N'Appropriation funding recognised as revenue from Government.'),
 ('N1_3','1',N'Departmental financial performance','1.3',N'Other comprehensive income','ALWAYS',1,1,320,N'Reclassification adjustments and tax relating to other comprehensive income.'),
 ('N2','2',N'Income and expenses administered on behalf of Government','2',N'Administered income, expenses and other comprehensive income','ADMINISTERED_ACTIVITIES',0,1,400,N'Administered note series 2.1 to 2.3.'),
 ('N3_1A','3',N'Departmental financial position','3.1A',N'Cash and cash equivalents','ALWAYS',1,1,510,N'Cash at bank and cash on hand.'),
 ('N3_1B','3',N'Departmental financial position','3.1B',N'Trade and other receivables','ALWAYS',1,1,520,N'Goods and services, appropriations and other receivables, gross and net of expected credit losses.'),
 ('N3_1C','3',N'Departmental financial position','3.1C-3.1E',N'Investments and other financial assets','INVESTMENTS',0,1,530,N'Equity-accounted investments, other investments and financial assets.'),
 ('N3_2A','3',N'Departmental financial position','3.2A',N'Property, plant and equipment and intangibles reconciliation','ALWAYS',1,1,610,N'Opening-to-closing movement table by PRIMA asset class, including heritage, specialist military equipment and intangibles when selected.'),
 ('N3_2B','3',N'Departmental financial position','3.2B',N'Inventories','INVENTORIES',0,1,620,N'Inventory classes, write-downs and expense recognition.'),
 ('N3_2C','3',N'Departmental financial position','3.2C',N'Other non-financial assets','ALWAYS',1,1,630,N'Prepayments and other non-financial assets.'),
 ('N3_3','3',N'Departmental financial position','3.3',N'Payables','ALWAYS',1,1,710,N'Suppliers and other payables.'),
 ('N3_4','3',N'Departmental financial position','3.4',N'Interest-bearing liabilities','LEASES',0,1,720,N'Lease liabilities and other interest-bearing liabilities.'),
 ('N3_5','3',N'Departmental financial position','3.5',N'Other provisions','ALWAYS',1,1,730,N'Employee, restoration and other provisions with movement reconciliations.'),
 ('N4','4',N'Assets and liabilities administered on behalf of Government','4',N'Administered assets and liabilities','ADMINISTERED_ACTIVITIES',0,1,800,N'Administered financial assets, non-financial assets, payables, interest-bearing liabilities and provisions.'),
 ('N5_1','5',N'Funding','5.1',N'Appropriations','APPROPRIATIONS',0,1,910,N'Annual and special appropriations, including agent disclosures.'),
 ('N5_2','5',N'Funding','5.2',N'Special accounts','SPECIAL_ACCOUNTS',0,1,920,N'Special account balance and transaction disclosures.'),
 ('N5_4','5',N'Funding','5.4',N'Net cash appropriation arrangements','APPROPRIATIONS',0,1,940,N'Net cash appropriation arrangements.'),
 ('N5_5','5',N'Funding','5.5',N'Cash flow reconciliation','ALWAYS',1,1,950,N'Reconciliation of operating result to net cash from operating activities.'),
 ('N6_1','6',N'People and relationships','6.1',N'Employee provisions','EMPLOYEE_BENEFITS',0,1,1010,N'Annual leave, long service leave and superannuation policies and balances.'),
 ('N6_2','6',N'People and relationships','6.2',N'Key management personnel remuneration','ALWAYS',1,1,1020,N'Key management personnel remuneration categories.'),
 ('N6_3','6',N'People and relationships','6.3',N'Related party disclosures','ALWAYS',1,1,1030,N'Related party transactions and balances.'),
 ('N7_1','7',N'Managing uncertainties','7.1',N'Contingent assets and liabilities','CONTINGENCIES',0,1,1110,N'Quantifiable and unquantifiable contingencies.'),
 ('N7_2','7',N'Managing uncertainties','7.2',N'Financial instruments','FINANCIAL_INSTRUMENTS',0,1,1120,N'Categories, gains and losses, credit risk, liquidity risk and market risk.'),
 ('N7_3','7',N'Managing uncertainties','7.3',N'Administered financial instruments','ADMINISTERED_ACTIVITIES',0,1,1130,N'Administered financial instrument disclosures.'),
 ('N7_4','7',N'Managing uncertainties','7.4',N'Fair value measurement','FAIR_VALUE',0,1,1140,N'Fair value hierarchy and level 3 reconciliations.'),
 ('N8_1','8',N'Other information','8.1',N'Current/non-current distinction','ALWAYS',1,1,1210,N'Current and non-current analysis of assets and liabilities.'),
 ('N8_2','8',N'Other information','8.2',N'Assets held in trust','CASH_ADMINISTERED',0,1,1220,N'Assets held in trust or on behalf of others.'),
 ('N8_3','8',N'Other information','8.3',N'Restructuring','ALWAYS',0,1,1230,N'Restructuring transactions where applicable.'),
 ('N8_4','8',N'Other information','8.4',N'Reporting of outcomes','OUTCOMES_REPORTING',0,1,1240,N'Net cost of outcome delivery for the annual report.'),
 ('ANNUAL_REPORT','AR',N'Annual report financial modules',NULL,N'Annual report financial information','OUTCOMES_REPORTING',0,1,1300,N'Outcome reporting, executive remuneration and other finance-linked annual report tables.'),
 ('AUDIT_PACK','AC',N'Audit committee pack',NULL,N'Audit committee reporting pack','ALWAYS',1,1,1400,N'Financial statement summary, judgements, new standards, movements, risks, draft statements, representation checklist and certification status.');

INSERT dbo.tblNORM_DisclosureRule
    (ConfigurationReleaseId,DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,TriggerCode,
     IsBaseRequired,RequiresNarrative,SortOrder,GuidanceText)
SELECT @ReleaseId,r.DisclosureCode,r.SectionCode,r.SectionTitle,r.NoteRef,r.DisclosureTitle,r.TriggerCode,
       r.IsBaseRequired,r.RequiresNarrative,r.SortOrder,r.GuidanceText
FROM @Rules r
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.tblNORM_DisclosureRule x
    WHERE x.ConfigurationReleaseId=@ReleaseId AND x.DisclosureCode=r.DisclosureCode
);

DECLARE @Narratives TABLE (DisclosureCode VARCHAR(40), NarrativeType VARCHAR(30), TemplateText NVARCHAR(MAX));
INSERT @Narratives VALUES
 ('OVERVIEW','AccountingPolicy',N'These general purpose financial statements are required by section 42 of the Public Governance, Performance and Accountability Act 2013 and have been prepared in accordance with the Public Governance, Performance and Accountability (Financial Reporting) Rule and Australian Accounting Standards. Replace this template with the entity-specific basis, activities, legislation, tax status, judgements and events after the reporting period.'),
 ('N1_1A','AccountingPolicy',N'Employee benefits are recognised as services are rendered. Liabilities for short-term benefits are measured at the undiscounted amount expected to be paid. Long service leave is measured at present value. Superannuation expense reflects contributions to the relevant schemes. Tailor assumptions and scheme details to the entity.'),
 ('N1_2A','AccountingPolicy',N'Revenue from contracts with customers is recognised when control of promised goods or services transfers to the customer, at the amount the entity expects to be entitled to. Add the entity-specific performance obligations and timing.'),
 ('N1_2E','AccountingPolicy',N'Rental income is recognised on a straight-line basis over the lease term unless another systematic basis is more representative. Complete the operating and finance lease receivable maturity analysis.'),
 ('N3_1B','AccountingPolicy',N'Trade receivables are initially recognised at fair value and subsequently measured at amortised cost, less an allowance for expected credit losses. Complete the receivable classes, credit terms and loss allowance movement.'),
 ('N3_2A','AccountingPolicy',N'Property, plant and equipment is measured in accordance with the entity asset policy and applicable Australian Accounting Standards. Complete the opening-to-closing reconciliation for each selected asset class, including additions, disposals, depreciation, impairment, revaluations and transfers.'),
 ('N3_2B','AccountingPolicy',N'Inventories are measured at the lower of cost and net realisable value, or current replacement cost where held for distribution at no or nominal consideration. Tailor costing methods and write-down policy.'),
 ('N3_4','AccountingPolicy',N'Lease liabilities are measured at the present value of remaining lease payments and remeasured when specified lease terms change. Right-of-use assets are disclosed with the related asset class.'),
 ('N6_1','AccountingPolicy',N'Employee provisions include annual leave and long service leave. Complete the measurement assumptions, discount rates, expected settlement profile and superannuation arrangements.'),
 ('N7_2','AccountingPolicy',N'Financial assets and liabilities are classified and measured under AASB 9. Complete the entity categories, expected credit loss approach, liquidity exposures and market risk sensitivities.'),
 ('AUDIT_PACK','ExecutiveSummary',N'Use this section for the CFO and Audit Committee executive summary: reporting status, material movements, significant judgements, new standards, unresolved risks and decisions required.');

INSERT dbo.tblNORM_NarrativeTemplate (ConfigurationReleaseId,DisclosureCode,NarrativeType,TemplateText)
SELECT @ReleaseId,n.DisclosureCode,n.NarrativeType,n.TemplateText
FROM @Narratives n
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.tblNORM_NarrativeTemplate x
    WHERE x.ConfigurationReleaseId=@ReleaseId AND x.DisclosureCode=n.DisclosureCode AND x.NarrativeType=n.NarrativeType
);

COMMIT TRANSACTION;

PRINT 'NORM government financial reporting platform installed.';
