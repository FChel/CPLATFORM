SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
  Controlled note comparatives from the active Start of Financial Year source.
  Values are only exposed when the active Prior Year Financial Statements upload
  has the matching SHA-256, preserving document-level source lineage.
*/
IF OBJECT_ID('dbo.tblNORM_SourceNoteFigure','U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_SourceNoteFigure
    (
        SourceNoteFigureId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_SourceNoteFigure PRIMARY KEY,
        SourceFileHash CHAR(64) NOT NULL,
        StatementLine NVARCHAR(240) NOT NULL,
        NoteSubLine NVARCHAR(240) NOT NULL,
        Amount DECIMAL(19,3) NOT NULL,
        SourceReference NVARCHAR(300) NULL,
        IsDeactivated BIT NOT NULL CONSTRAINT DF_tblNORM_SourceNoteFigure_IsDeactivated DEFAULT (0),
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_SourceNoteFigure_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_tblNORM_SourceNoteFigure UNIQUE(SourceFileHash,StatementLine,NoteSubLine)
    );
END;

DECLARE @Hash CHAR(64)='1D02B0EE80D807735035CEED18EB7871D0D50193845DB6E7317335F3CA9CD62B';
DECLARE @Source NVARCHAR(300)=N'Defence Annual Report 2023-24 · Departmental notes · 2024 column';
DECLARE @Figures TABLE(StatementLine NVARCHAR(240),NoteSubLine NVARCHAR(240),Amount DECIMAL(19,3));

INSERT @Figures VALUES
-- Note 1.1A Employee benefits
(N'Employee benefits',N'Wages and Salaries (APS)',1773262),(N'Employee benefits',N'Defined contribution plans (APS)',271029),
(N'Employee benefits',N'Defined benefit plans (APS)',71491),(N'Employee benefits',N'Leave and other entitlements (APS)',257983),
(N'Employee benefits',N'Fringe benefits tax (APS)',15469),(N'Employee benefits',N'Separation and redundancies (APS)',2800),
(N'Employee benefits',N'Other allowances (APS)',111139),(N'Employee benefits',N'Health expenses (APS)',3455),
(N'Employee benefits',N'Other employee expenses (APS)',1241),(N'Employee benefits',N'Wages and Salaries (ADF)',6071083),
(N'Employee benefits',N'Defined contribution plans (ADF)',459599),(N'Employee benefits',N'Defined benefit plans (ADF)',1264512),
(N'Employee benefits',N'Housing (ADF)',947492),(N'Employee benefits',N'Leave and other entitlements (ADF)',749134),
(N'Employee benefits',N'Fringe benefits tax (ADF)',583426),(N'Employee benefits',N'Overseas allowances (ADF)',114247),
(N'Employee benefits',N'Separation and redundancies (ADF)',9508),(N'Employee benefits',N'Other allowances (ADF)',407636),
(N'Employee benefits',N'Health expenses (ADF)',679897),(N'Employee benefits',N'Other employee expenses (ADF)',191763),
-- Notes 1.1B-1.1G expenses
(N'Supplier expenses',N'Administration',1001817),(N'Supplier expenses',N'Communications and information technology',2149876),
(N'Supplier expenses',N'Estate upkeep',2115504),(N'Supplier expenses',N'Freight',642336),
(N'Supplier expenses',N'Foreign government activities',72325),(N'Supplier expenses',N'Garrison & mess operations',404647),
(N'Supplier expenses',N'Inventory consumption',1173727),(N'Supplier expenses',N'Other',1711139),
(N'Supplier expenses',N'Professional services/technical advice',1081219),(N'Supplier expenses',N'Project management costs',821853),
(N'Supplier expenses',N'Purchase if minor assets',669584),(N'Supplier expenses',N'Research and development',824578),
(N'Supplier expenses',N'Sustainment (including repair and overhaul)',9018983),(N'Supplier expenses',N'Training',599120),
(N'Supplier expenses',N'Travel',380580),(N'Supplier expenses',N'Utilities',507673),
(N'Supplier expenses',N'Short-term leases',51811),(N'Supplier expenses',N'Low Value leases',33450),
(N'Supplier expenses',N'Variable lease payments',2067),(N'Supplier expenses',N'Workers compensation expenses',24751),
(N'Grants',N'State and Territory Governments',7061),(N'Grants',N'Non-profit organisations',54380),(N'Grants',N'Overseas',51760),
(N'Finance costs',N'Interest on lease liabilities',125574),(N'Finance costs',N'Unwinding of discount',42354),
(N'Impairment loss on financial instruments',N'Impairment on financial instruments',3492),
(N'Write-down of non-financial assets',N'Write-down of Land and building',135045),
(N'Write-down of non-financial assets',N'Write-down of Speacialist military equipment',770154),
(N'Write-down of non-financial assets',N'Write-down of Infrastructure',25978),
(N'Write-down of non-financial assets',N'Write-down of Plant and equipment',63265),
(N'Write-down of non-financial assets',N'Write-down of Heritage and cultural assets',14089),
(N'Write-down of non-financial assets',N'Write-down of Intangibles',7786),
(N'Write-down of non-financial assets',N'Write-down of Inventories',355648),
(N'Expenses in relation to special accounts',N'Expenses in relation to special accounts',18634),
(N'Other expenses',N'Returns to the official public account',82636),(N'Other expenses',N'Defective Administration Scheme payments',1381),
(N'Other expenses',N'decontamination and decommissioning costs',27462),(N'Other expenses',N'Other',606),
(N'Foreign exchange losses',N'Non-speculative (losses)',39266),
-- Notes 1.2A-1.2H own-source income and gains
(N'Revenue from contracts with customers',N'Rations and quarters - cost recovery',64294),
(N'Revenue from contracts with customers',N'Provision of fuel - cost recovery',98578),
(N'Revenue from contracts with customers',N'Foreign government activities',27270),
(N'Revenue from contracts with customers',N'Other (including sale of obsolete and surplus inventory)',22146),
(N'Revenue from contracts with customers',N'Logistics support recovery',85985),
(N'Revenue from contracts with customers',N'Other recoveries',98438),
(N'Revenue from contracts with customers',N'Other governments/agencies',134732),
(N'Revenue from contracts with customers',N'Other',39048),
(N'Revenue in relation to special accounts',N'Revenue in relation to Special Accounts',139448),
(N'Rental income',N'Group rental scheme',217551),(N'Rental income',N'Other',22413),(N'Rental income',N'Finance Income',11382),
(N'Other revenue',N'Foreign government activities',76822),(N'Other revenue',N'Interest revenue',79934),
(N'Other revenue',N'Other refunds',51768),(N'Other revenue',N'Excise refunds',702),
(N'Other revenue',N'Other minor revenues',1103),(N'Other revenue',N'Settlement of damages',63781),
(N'Other revenue',N'Remuneration of auditors',3600),
(N'Reversals of previous asset write-downs',N'Land',9249),(N'Reversals of previous asset write-downs',N'Buildings',64072),
(N'Reversals of previous asset write-downs',N'Specialist military equipment',357754),
(N'Reversals of previous asset write-downs',N'Infrastructure',45401),(N'Reversals of previous asset write-downs',N'Plant and equipment',39566),
(N'Reversals of previous asset write-downs',N'Heritage and cultural assets',2169),
(N'Reversals of previous asset write-downs',N'Software and intangibles',202),(N'Reversals of previous asset write-downs',N'Inventory',348235),
(N'Foreign exchange gains',N'Non-speculative (gains)',69822),(N'Other gains',N'Reversal/write back of provisions',169719),
(N'Revenue from Government',N'Departmental appropriations',35840570),
-- Notes 3.1-3.5 financial position
(N'Cash and cash equivalents',N'Cash on hand',2865),(N'Cash and cash equivalents',N'Cash at bank - at call',195378),
(N'Cash and cash equivalents',N'Cash held in OPA - Special Account',134106),
(N'Trade and other receivables',N'Goods and services',281068),(N'Trade and other receivables',N'Appropriations receivable',425972),
(N'Trade and other receivables',N'GST receivable from ATO',396818),(N'Trade and other receivables',N'Accrued revenue',134407),
(N'Trade and other receivables',N'Sub-Lease receivables',484516),(N'Trade and other receivables',N'Other',244803),
(N'Trade and other receivables',N'Allowance for doubtful debts',-9846),
(N'Inventories',N'Inventories - General',3378835),(N'Inventories',N'Inventories - Explosive ordnance',6091468),(N'Inventories',N'Inventories - Fuel',127024),
(N'Prepayments',N'Capital prepayments',2174833),(N'Prepayments',N'Non-capital prepayments',1325199),
(N'Assets held for sale',N'Land',68366),(N'Assets held for sale',N'Infrastrucutre',51),
(N'Suppliers payables',N'Trade creditors and accruals',5351477),
(N'Employee payables',N'Salaries and wages (APS)',73007),(N'Employee payables',N'Superannuation (APS)',9540),
(N'Employee payables',N'Salaries and wages (ADF)',215467),(N'Employee payables',N'Superannuation (ADF)',55392),
(N'Other payables',N'Statutory payable',277884),(N'Other payables',N'Other',117619),
(N'Leases',N'Lease liabilities',3139113),
(N'Employee provisions',N'Leave (APS)',770102),(N'Employee provisions',N'Leave (ADF)',2505007),(N'Employee provisions',N'Super Provision (ADF)',10533),
(N'Asset restoration provisions',N'Restoration',177736),(N'Asset restoration provisions',N'Decommissioning',350854),
(N'Asset restoration provisions',N'Decontamination',527611),(N'Other provisions',N'Other provisions',266891);

UPDATE target
SET target.Amount=source.Amount,target.SourceReference=@Source,target.IsDeactivated=0
FROM dbo.tblNORM_SourceNoteFigure target
INNER JOIN @Figures source ON source.StatementLine=target.StatementLine AND source.NoteSubLine=target.NoteSubLine
WHERE target.SourceFileHash=@Hash;

INSERT dbo.tblNORM_SourceNoteFigure(SourceFileHash,StatementLine,NoteSubLine,Amount,SourceReference)
SELECT @Hash,source.StatementLine,source.NoteSubLine,source.Amount,@Source
FROM @Figures source
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_SourceNoteFigure target
    WHERE target.SourceFileHash=@Hash AND target.StatementLine=source.StatementLine AND target.NoteSubLine=source.NoteSubLine
);

PRINT 'NORM prior-year note comparative source figures installed.';
