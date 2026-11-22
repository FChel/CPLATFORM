/* Departmental FY2026 hierarchy from the Owner-reviewed NIHRACS proposal.
   The optional schema change is independent of any agency data.
   Configuration requires an explicitly selected empty NIHRACS draft. No GL mappings are inserted. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 0 OR COL_LENGTH('dbo.tblNORM_StatementLine','ParentLineCode') IS NULL
    OR COL_LENGTH('dbo.tblNORM_LineResult','ReportedAmount') IS NULL
    OR OBJECT_ID('dbo.tblNORM_DisclosureRule','U') IS NULL
    THROW 51000, 'Install the approved Employee Benefits pilot baseline first.', 1;

IF COL_LENGTH('dbo.tblNORM_DisclosureRule','NoteSourceLineCode') IS NULL
    ALTER TABLE dbo.tblNORM_DisclosureRule ADD NoteSourceLineCode varchar(120) NULL;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Set explicitly during a separately authorised configuration operation.
DECLARE @ReleaseId int = NULL;
IF @ReleaseId = 10 THROW 51010, 'NIHRACS Draft 10 is protected from catalogue seeding.', 1;
IF @ReleaseId IS NULL OR @ReleaseId < 1
    THROW 51011, 'Supply the separate NIHRACS FY2026 draft release ID explicitly.', 1;
IF @@TRANCOUNT <> 0 THROW 51000, 'Use a dedicated session.', 1;

BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK)
    WHERE ConfigurationReleaseId=@ReleaseId AND EntityCode='NIHRACS' AND FinancialYear=2026
      AND StatusCode='Draft' AND IsDeactivated=0)
    THROW 51000, 'Select an active NIHRACS FY2026 draft.', 1;
IF EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@ReleaseId)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=@ReleaseId)
   OR EXISTS (SELECT 1 FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@ReleaseId)
    THROW 51000, 'The proposed NIHRACS catalogue requires an empty draft.', 1;

INSERT dbo.tblNORM_StatementLine
 (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,
  CalculationKind,FormulaSpec,NaturalSign,ParentLineCode,IsDeactivated)
SELECT @ReleaseId,2026,'NIHRACS',v.*
FROM (VALUES
    (N'SOCI', 10, N'line', N'Employee benefits', N'Employee benefits', N'1.1A', N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 20, N'line', N'Supplier expenses', N'Suppliers', N'1.1B', N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 30, N'line', N'Depreciation and amortisation', N'Depreciation and amortisation', N'2.2A', N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 40, N'line', N'Finance costs', N'Finance costs', NULL, N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 50, N'line', N'IS_EXP_LAS', N'Loss on disposal of assets', N'1.1E', N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 60, N'line', N'Impairment loss on financial instruments', N'Impairment loss on financial instruments', N'1.1C', N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 70, N'line', N'Write-down of non-financial assets', N'Impairment of non-financial assets', N'1.1D', N'Formula', NULL, NULL, N'Total expenses', 0),
    (N'SOCI', 80, N'total', N'Total expenses', N'Total expenses', NULL, N'Formula', NULL, NULL, NULL, 0),
    (N'SOCI', 90, N'line', N'Revenue from contracts with customers', N'Revenue from contracts with customers', N'1.2A', N'Formula', NULL, NULL, N'Total own-source income', 0),
    (N'SOCI', 100, N'line', N'IS_REV_INT', N'Interest', NULL, N'Formula', NULL, NULL, N'Total own-source income', 0),
    (N'SOCI', 110, N'line', N'Other revenue', N'Other revenue', N'1.2B', N'Formula', NULL, NULL, N'Total own-source income', 0),
    (N'SOCI', 120, N'total', N'Total own-source income', N'Total own-source revenue', NULL, N'Formula', NULL, NULL, NULL, 0),
    (N'SOCI', 130, N'total', N'Net cost of services', N'Net cost of services', NULL, N'Formula', N'+Total own-source income|-Total expenses', NULL, NULL, 0),
    (N'SOCI', 140, N'line', N'Revenue from Government', N'Revenue from Government', N'1.2C', N'Formula', NULL, NULL, NULL, 0),
    (N'SOCI', 150, N'total', N'Operating result', N'Deficit on continuing operations', NULL, N'Formula', N'+Revenue from Government|+Net cost of services', NULL, NULL, 0),
    (N'SOFP', 10, N'line', N'Cash and cash equivalents', N'Cash and cash equivalents', N'2.1A', N'Formula', NULL, NULL, N'TOTAL_FINANCIAL_ASSETS', 0),
    (N'SOFP', 20, N'line', N'BS_ASS_FIA_RFG', N'Receivable from Government', N'2.1B', N'Formula', NULL, NULL, N'TOTAL_FINANCIAL_ASSETS', 0),
    (N'SOFP', 30, N'line', N'Trade and other receivables', N'Trade and other receivables', N'2.1C', N'Formula', NULL, NULL, N'TOTAL_FINANCIAL_ASSETS', 0),
    (N'SOFP', 40, N'line', N'BS_ASS_FIA_OTH', N'Other investments', N'2.1D', N'Formula', NULL, NULL, N'TOTAL_FINANCIAL_ASSETS', 0),
    (N'SOFP', 50, N'total', N'TOTAL_FINANCIAL_ASSETS', N'Total financial assets', NULL, N'Formula', NULL, NULL, N'Total assets', 0),
    (N'SOFP', 60, N'line', N'BS_ASS_NFA_LND', N'Land', N'2.2A', N'Formula', NULL, NULL, N'TOTAL_NON_FINANCIAL_ASSETS', 0),
    (N'SOFP', 70, N'line', N'BS_ASS_NFA_BLD', N'Buildings', N'2.2A', N'Formula', NULL, NULL, N'TOTAL_NON_FINANCIAL_ASSETS', 0),
    (N'SOFP', 80, N'line', N'Property plant and equipment', N'Property, plant and equipment', N'2.2A', N'Formula', NULL, NULL, N'TOTAL_NON_FINANCIAL_ASSETS', 0),
    (N'SOFP', 90, N'line', N'Inventories', N'Inventories', N'2.2B', N'Formula', NULL, NULL, N'TOTAL_NON_FINANCIAL_ASSETS', 0),
    (N'SOFP', 100, N'line', N'Prepayments', N'Other non-financial assets', N'2.2C', N'Formula', NULL, NULL, N'TOTAL_NON_FINANCIAL_ASSETS', 0),
    (N'SOFP', 110, N'total', N'TOTAL_NON_FINANCIAL_ASSETS', N'Total non-financial assets', NULL, N'Formula', NULL, NULL, N'Total assets', 0),
    (N'SOFP', 120, N'total', N'Total assets', N'Total assets', NULL, N'Formula', NULL, NULL, NULL, 0),
    (N'SOFP', 130, N'line', N'Suppliers payables', N'Suppliers', N'2.3A', N'Formula', NULL, NULL, N'BS_LIA_PAY', 0),
    (N'SOFP', 140, N'line', N'Other payables', N'Other payables', N'2.3B', N'Formula', NULL, NULL, N'BS_LIA_PAY', 0),
    (N'SOFP', 150, N'total', N'BS_LIA_PAY', N'Total payables', NULL, N'Formula', NULL, NULL, N'Total liabilities', 0),
    (N'SOFP', 160, N'line', N'Leases', N'Leases', N'2.4A', N'Formula', NULL, NULL, N'BS_LIA_IBL', 0),
    (N'SOFP', 170, N'total', N'BS_LIA_IBL', N'Total interest-bearing liabilities', NULL, N'Formula', NULL, NULL, N'Total liabilities', 0),
    (N'SOFP', 180, N'line', N'Employee provisions', N'Employee provisions', N'3.1A', N'Formula', NULL, NULL, N'BS_LIA_PRV', 0),
    (N'SOFP', 190, N'total', N'BS_LIA_PRV', N'Total provisions', NULL, N'Formula', NULL, NULL, N'Total liabilities', 0),
    (N'SOFP', 200, N'total', N'Total liabilities', N'Total liabilities', NULL, N'Formula', NULL, NULL, NULL, 0),
    (N'SOFP', 210, N'total', N'Net assets', N'Net assets', NULL, N'Formula', N'+Total assets|-Total liabilities', NULL, NULL, 0),
    (N'SOCI', 5, N'section', NULL, N'Expenses', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOCI', 85, N'section', NULL, N'Own-source revenue', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOFP', 5, N'section', NULL, N'Financial assets', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOFP', 55, N'section', NULL, N'Non-financial assets', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOFP', 125, N'section', NULL, N'Payables', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOFP', 155, N'section', NULL, N'Interest-bearing liabilities', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOFP', 175, N'section', NULL, N'Provisions', NULL, N'Heading', NULL, NULL, NULL, 0),
    (N'SOCI', 1148, N'hierarchy-detail', N'EMP_WAGES', N'Wages and salaries', NULL, N'Mapped', NULL, NULL, N'Employee benefits', 0),
    (N'SOCI', 1150, N'hierarchy-detail', N'EMP_DC', N'Defined contribution plans', NULL, N'Mapped', NULL, NULL, N'Employee benefits', 0),
    (N'SOCI', 1151, N'hierarchy-detail', N'EMP_DB', N'Defined benefit plans', NULL, N'Mapped', NULL, NULL, N'Employee benefits', 1),
    (N'SOCI', 1152, N'hierarchy-detail', N'EMP_LEAVE', N'Leave and other entitlements', NULL, N'Mapped', NULL, NULL, N'Employee benefits', 0),
    (N'SOCI', 1158, N'hierarchy-detail', N'SUP_GAS_GDS_MDV', N'Medivac expenses', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1159, N'hierarchy-detail', N'SUP_GAS_GDS_NIP', N'NIPTAAS expenses', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1160, N'hierarchy-detail', N'SUP_GAS_GDS_INC', N'Inventory consumed', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1161, N'hierarchy-detail', N'SUP_GAS_GDS_INS', N'Insurance', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1162, N'hierarchy-detail', N'SUP_GAS_GDS_ICT', N'ICT service costs', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1163, N'hierarchy-detail', N'SUP_GAS_GDS_MPS', N'MPS Home Care expenses', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1164, N'hierarchy-detail', N'SUP_GAS_GDS_PPE', N'Equipment repairs and maintenance', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1165, N'hierarchy-detail', N'SUP_GAS_GDS_FRT', N'Freight and transport', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1166, N'hierarchy-detail', N'SUP_GAS_GDS_LEA', N'Short-term property lease expenses', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1167, N'hierarchy-detail', N'SUP_GAS_GDS_OTH', N'Other', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_GDS', 0),
    (N'SOCI', 1168, N'hierarchy-detail', N'SUP_GAS_GDS', N'Goods supplied', NULL, N'Formula', NULL, NULL, N'SUP_GAS', 0),
    (N'SOCI', 1170, N'hierarchy-detail', N'SUP_GAS_SRV_CNS', N'Consultants', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_SRV', 0),
    (N'SOCI', 1171, N'hierarchy-detail', N'SUP_GAS_SRV_FFS', N'GP – fee for service', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_SRV', 0),
    (N'SOCI', 1172, N'hierarchy-detail', N'SUP_GAS_SRV_CNT', N'Contractors', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_SRV', 0),
    (N'SOCI', 1173, N'hierarchy-detail', N'SUP_GAS_SRV_BLD', N'Building repairs and maintenance', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_SRV', 0),
    (N'SOCI', 1174, N'hierarchy-detail', N'SUP_GAS_SRV_LAB', N'Laboratory fees', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_SRV', 0),
    (N'SOCI', 1175, N'hierarchy-detail', N'SUP_GAS_SRV_FOC', N'Resource received free of charge expense', NULL, N'Mapped', NULL, N'C', N'SUP_GAS_SRV', 0),
    (N'SOCI', 1176, N'hierarchy-detail', N'SUP_GAS_SRV_OTH', N'Other', NULL, N'Mapped', NULL, NULL, N'SUP_GAS_SRV', 0),
    (N'SOCI', 1177, N'hierarchy-detail', N'SUP_GAS_SRV', N'Services rendered', NULL, N'Formula', NULL, NULL, N'SUP_GAS', 0),
    (N'SOCI', 1178, N'hierarchy-detail', N'SUP_GAS', N'Total goods and services supplied or rendered', NULL, N'Formula', NULL, NULL, N'Supplier expenses', 0),
    (N'SOCI', 1180, N'hierarchy-detail', N'SUP_OTH_WCE', N'Workers compensation expenses', NULL, N'Mapped', NULL, NULL, N'SUP_OTH', 0),
    (N'SOCI', 1181, N'hierarchy-detail', N'SUP_OTH', N'Total other suppliers', NULL, N'Formula', NULL, NULL, N'Supplier expenses', 0),
    (N'SOCI', 1189, N'hierarchy-detail', N'IFI_IFI', N'Impairment loss on financial instruments', NULL, N'Mapped', NULL, NULL, N'Impairment loss on financial instruments', 0),
    (N'SOCI', 1195, N'hierarchy-detail', N'IFA_IFA', N'Impairment of work in progress assets', NULL, N'Mapped', NULL, NULL, N'Write-down of non-financial assets', 0),
    (N'SOCI', 1230, N'hierarchy-detail', N'GNS_OTH', N'Loss on disposal of assets', NULL, N'Mapped', NULL, NULL, N'IS_EXP_LAS', 0),
    (N'SOCI', 1201, N'hierarchy-detail', N'CWC_GPC', N'GP consultations', NULL, N'Mapped', NULL, NULL, N'Revenue from contracts with customers', 0),
    (N'SOCI', 1202, N'hierarchy-detail', N'CWC_INP', N'Inpatient fees', NULL, N'Mapped', NULL, NULL, N'Revenue from contracts with customers', 0),
    (N'SOCI', 1203, N'hierarchy-detail', N'CWC_DOF', N'Dental and orthodontic fees', NULL, N'Mapped', NULL, NULL, N'Revenue from contracts with customers', 0),
    (N'SOCI', 1204, N'hierarchy-detail', N'CWC_SUB', N'Medicare subsidies', NULL, N'Mapped', NULL, NULL, N'Revenue from contracts with customers', 0),
    (N'SOCI', 1205, N'hierarchy-detail', N'CWC_OTH', N'Other revenue', NULL, N'Mapped', NULL, NULL, N'Revenue from contracts with customers', 0),
    (N'SOCI', 1206, N'hierarchy-detail', N'CWC_MSI', N'Sale of medical and surgical items', NULL, N'Mapped', NULL, NULL, N'Revenue from contracts with customers', 0),
    (N'SOCI', 1212, N'hierarchy-detail', N'REV_AUD', N'Audit services received free of charge', NULL, N'Mapped', NULL, N'D', N'Other revenue', 0),
    (N'SOCI', 1213, N'hierarchy-detail', N'REV_OTH', N'Other resources received free of charge from DITRDCSA', NULL, N'Mapped', NULL, N'D', N'Other revenue', 0),
    (N'SOCI', 1214, N'hierarchy-detail', N'REV_DON', N'Donations', NULL, N'Mapped', NULL, NULL, N'Other revenue', 1),
    (N'SOCI', 1215, N'hierarchy-detail', N'REV_COM', N'Commission received', NULL, N'Mapped', NULL, NULL, N'Other revenue', 0),
    (N'SOCI', 1216, N'hierarchy-detail', N'REV_PHN', N'Funding from Primary Health Network', NULL, N'Mapped', NULL, NULL, N'Other revenue', 0),
    (N'SOCI', 1217, N'hierarchy-detail', N'REV_OGF', N'Other grant funding', NULL, N'Mapped', NULL, NULL, N'Other revenue', 1),
    (N'SOCI', 1223, N'hierarchy-detail', N'RFG_DIR', N'Department of Infrastructure, Transport, Regional Development, Communications, Sport and the Arts (DITRDCSA)', NULL, N'Mapped', NULL, NULL, N'Revenue from Government', 0),
    (N'SOCI', 1224, N'hierarchy-detail', N'RFG_DOH', N'Department of Health, Disability and Ageing (DOHDA)', NULL, N'Mapped', NULL, NULL, N'Revenue from Government', 0),
    (N'SOFP', 1236, N'hierarchy-detail', N'CSH_COH', N'Cash on hand or on deposit', NULL, N'Mapped', NULL, NULL, N'Cash and cash equivalents', 0),
    (N'SOFP', 1242, N'hierarchy-detail', N'REC_DIR', N'DITRDCSA – Service Level Agreement', NULL, N'Mapped', NULL, NULL, N'BS_ASS_FIA_RFG', 0),
    (N'SOFP', 1248, N'hierarchy-detail', N'TOR_INV_CWC', N'Receivables from contracts with customers', NULL, N'Mapped', NULL, NULL, N'TOR_INV', 0),
    (N'SOFP', 1249, N'hierarchy-detail', N'TOR_INV', N'Gross receivables from contracts with customers', NULL, N'Formula', NULL, NULL, N'Trade and other receivables', 0),
    (N'SOFP', 1251, N'hierarchy-detail', N'TOR_ILA', N'Less impairment loss allowance', NULL, N'Mapped', NULL, NULL, N'Trade and other receivables', 0),
    (N'SOFP', 1257, N'hierarchy-detail', N'INV_DEP', N'Term deposits', NULL, N'Mapped', NULL, NULL, N'BS_ASS_FIA_OTH', 0),
    (N'SOCI', 1129, N'hierarchy-detail', N'DAA_OLB_BLD', N'Buildings', NULL, N'Mapped', NULL, NULL, N'DAA_OLB', 0),
    (N'SOCI', 1130, N'hierarchy-detail', N'DAA_OLB_LND', N'Leasehold improvements', NULL, N'Mapped', NULL, NULL, N'DAA_OLB', 1),
    (N'SOCI', 1131, N'hierarchy-detail', N'DAA_OLB', N'Total owned land and buildings', NULL, N'Formula', NULL, NULL, N'Depreciation and amortisation', 0),
    (N'SOCI', 1132, N'hierarchy-detail', N'DAA_RLB_BLD', N'Buildings ROU', NULL, N'Mapped', NULL, NULL, N'DAA_RLB', 1),
    (N'SOCI', 1133, N'hierarchy-detail', N'DAA_RLB_LND', N'Land ROU', NULL, N'Mapped', NULL, NULL, N'DAA_RLB', 1),
    (N'SOCI', 1134, N'hierarchy-detail', N'DAA_RLB', N'Total right-of-use land and buildings', NULL, N'Formula', NULL, NULL, N'Depreciation and amortisation', 0),
    (N'SOCI', 1135, N'hierarchy-detail', N'DAA_PPE', N'Property, plant and equipment', NULL, N'Mapped', NULL, NULL, N'Depreciation and amortisation', 0),
    (N'SOCI', 1136, N'hierarchy-detail', N'DAA_SOF', N'Computer software', NULL, N'Mapped', NULL, NULL, N'Depreciation and amortisation', 1),
    (N'SOFP', 1276, N'hierarchy-detail', N'LND_GBV', N'Gross book value', NULL, N'Mapped', NULL, NULL, N'BS_ASS_NFA_LND', 0),
    (N'SOFP', 1277, N'hierarchy-detail', N'LND_WIP', N'Work in progress', NULL, N'Mapped', NULL, NULL, N'BS_ASS_NFA_LND', 1),
    (N'SOFP', 1278, N'hierarchy-detail', N'LND_DAA', N'Accumulated depreciation, amortisation and impairment', NULL, N'Mapped', NULL, NULL, N'BS_ASS_NFA_LND', 1),
    (N'SOFP', 1296, N'hierarchy-detail', N'BLD_GBV', N'Gross book value', NULL, N'Mapped', NULL, NULL, N'BS_ASS_NFA_BLD', 0),
    (N'SOFP', 1297, N'hierarchy-detail', N'BLD_WIP', N'Work in progress', NULL, N'Mapped', NULL, NULL, N'BS_ASS_NFA_BLD', 1),
    (N'SOFP', 1298, N'hierarchy-detail', N'BLD_DAA', N'Accumulated depreciation, amortisation and impairment', NULL, N'Mapped', NULL, NULL, N'BS_ASS_NFA_BLD', 0),
    (N'SOFP', 1316, N'hierarchy-detail', N'PPE_GBV', N'Gross book value', NULL, N'Mapped', NULL, NULL, N'Property plant and equipment', 0),
    (N'SOFP', 1317, N'hierarchy-detail', N'PPE_WIP', N'Work in progress', NULL, N'Mapped', NULL, NULL, N'Property plant and equipment', 0),
    (N'SOFP', 1318, N'hierarchy-detail', N'PPE_DAA', N'Accumulated depreciation, amortisation and impairment', NULL, N'Mapped', NULL, NULL, N'Property plant and equipment', 0),
    (N'SOFP', 1345, N'hierarchy-detail', N'VEN_CON', N'Consumables', NULL, N'Mapped', NULL, NULL, N'Inventories', 0),
    (N'SOFP', 1346, N'hierarchy-detail', N'VEN_PHP', N'Pharmaceutical products', NULL, N'Mapped', NULL, NULL, N'Inventories', 0),
    (N'SOFP', 1352, N'hierarchy-detail', N'PRE_PRE', N'Prepayments', NULL, N'Mapped', NULL, NULL, N'Prepayments', 0),
    (N'SOFP', 1358, N'hierarchy-detail', N'TRD_SUP', N'Trade creditors and accruals', NULL, N'Mapped', NULL, NULL, N'Suppliers payables', 0),
    (N'SOFP', 1364, N'hierarchy-detail', N'PAY_SAW', N'Accrued salaries and wages', NULL, N'Mapped', NULL, NULL, N'Other payables', 0),
    (N'SOFP', 1365, N'hierarchy-detail', N'PAY_SUP', N'Superannuation payable', NULL, N'Mapped', NULL, NULL, N'Other payables', 1),
    (N'SOFP', 1366, N'hierarchy-detail', N'PAY_AGE', N'Aged care fees received in advance', NULL, N'Mapped', NULL, NULL, N'Other payables', 0),
    (N'SOFP', 1367, N'hierarchy-detail', N'PAY_TAX', N'PAYG payable', NULL, N'Mapped', NULL, NULL, N'Other payables', 1),
    (N'SOFP', 1368, N'hierarchy-detail', N'PAY_CON', N'Contract liabilities', NULL, N'Mapped', NULL, NULL, N'Other payables', 0),
    (N'SOFP', 1374, N'hierarchy-detail', N'LEA_LIA', N'Lease liabilities', NULL, N'Mapped', NULL, NULL, N'Leases', 0),
    (N'SOFP', 1380, N'hierarchy-detail', N'PRV_EMP', N'Employee and medical personnel provisions', NULL, N'Mapped', NULL, NULL, N'Employee provisions', 0),
    (N'SOCI', 1406, N'hierarchy-detail', N'GFA_INT', N'Interest revenue', NULL, N'Mapped', NULL, NULL, N'IS_REV_INT', 0),
    (N'SOCI', 1142, N'hierarchy-detail', N'FIN_INT', N'Interest on lease liabilities', NULL, N'Mapped', NULL, NULL, N'Finance costs', 0)
) v(StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,CalculationKind,FormulaSpec,NaturalSign,ParentLineCode,IsDeactivated);

INSERT dbo.tblNORM_DisclosureRule
 (ConfigurationReleaseId,DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,SortOrder,NoteSourceLineCode,
  TriggerCode,IsBaseRequired,RequiresNarrative)
SELECT @ReleaseId,v.*,'ALWAYS',1,0
FROM (VALUES
    (N'N1_1A', N'1', N'Financial performance', N'1.1A', N'Employee benefits', 10, N'Employee benefits'),
    (N'NI_IS_EXP_SUP', N'1', N'Financial performance', N'1.1B', N'Suppliers', 20, N'Supplier expenses'),
    (N'NI_IS_EXP_IFI', N'1', N'Financial performance', N'1.1C', N'Impairment loss on financial instruments', 30, N'Impairment loss on financial instruments'),
    (N'NI_IS_EXP_IFA', N'1', N'Financial performance', N'1.1D', N'Impairment of non-financial assets', 40, N'Write-down of non-financial assets'),
    (N'NI_IS_EXP_LAS', N'1', N'Financial performance', N'1.1E', N'Loss on disposal of assets', 50, N'IS_EXP_LAS'),
    (N'NI_IS_EXP_FIN', N'1', N'Financial performance', NULL, N'Finance costs', 60, N'Finance costs'),
    (N'NI_IS_REV_CWC', N'1', N'Financial performance', N'1.2A', N'Revenue from contracts with customers', 70, N'Revenue from contracts with customers'),
    (N'NI_IS_REV_OTH', N'1', N'Financial performance', N'1.2B', N'Other revenue', 80, N'Other revenue'),
    (N'NI_IS_RFG', N'1', N'Financial performance', N'1.2C', N'Revenue from Government', 90, N'Revenue from Government'),
    (N'NI_BS_ASS_FIA_CSH', N'2', N'Financial position', N'2.1A', N'Cash and cash equivalents', 100, N'Cash and cash equivalents'),
    (N'NI_BS_ASS_FIA_RFG', N'2', N'Financial position', N'2.1B', N'Receivable from Government', 110, N'BS_ASS_FIA_RFG'),
    (N'NI_BS_ASS_FIA_TOR', N'2', N'Financial position', N'2.1C', N'Trade and other receivables', 120, N'Trade and other receivables'),
    (N'NI_BS_ASS_FIA_OTH', N'2', N'Financial position', N'2.1D', N'Other investments', 130, N'BS_ASS_FIA_OTH'),
    (N'NI_BS_ASS_NFA_BLD', N'2', N'Financial position', N'2.2A', N'Buildings', 140, N'BS_ASS_NFA_BLD'),
    (N'NI_BS_ASS_NFA_LND', N'2', N'Financial position', N'2.2A', N'Land', 150, N'BS_ASS_NFA_LND'),
    (N'NI_IS_EXP_DAA', N'2', N'Financial position', N'2.2A', N'Depreciation and amortisation', 160, N'Depreciation and amortisation'),
    (N'NI_BS_ASS_NFA_PPE', N'2', N'Financial position', N'2.2A', N'Property, plant and equipment', 170, N'Property plant and equipment'),
    (N'NI_BS_ASS_NFA_INV', N'2', N'Financial position', N'2.2B', N'Inventories', 180, N'Inventories'),
    (N'NI_BS_ASS_NFA_OTH', N'2', N'Financial position', N'2.2C', N'Other non-financial assets', 190, N'Prepayments'),
    (N'NI_BS_LIA_PAY_SUP', N'2', N'Financial position', N'2.3A', N'Suppliers', 200, N'Suppliers payables'),
    (N'NI_BS_LIA_PAY_OTH', N'2', N'Financial position', N'2.3B', N'Other payables', 210, N'Other payables'),
    (N'NI_BS_LIA_IBL_LEA', N'2', N'Financial position', N'2.4A', N'Leases', 220, N'Leases'),
    (N'NI_BS_LIA_PRV_EMP', N'3', N'People and relationships', N'3.1A', N'Employee provisions', 230, N'Employee provisions'),
    (N'NI_IS_REV_INT', N'4', N'Managing uncertainties', N'4.1', N'Interest', 240, N'IS_REV_INT')
) v(DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,SortOrder,NoteSourceLineCode);

UPDATE dbo.tblNORM_ConfigurationRelease
 SET ContentHash=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(36),NEWID())),2)),ReviewedBy=NULL,ReviewedUtc=NULL
 WHERE ConfigurationReleaseId=@ReleaseId;
INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy)
 VALUES ('NIHRACS_HIERARCHY_CONFIGURED','ConfigurationRelease',CONVERT(varchar(20),@ReleaseId),
 N'FY2026 Departmental proposal. Free-of-charge presentation signs and building depreciation classification are provisional.',
 ORIGINAL_LOGIN());
COMMIT TRANSACTION;
