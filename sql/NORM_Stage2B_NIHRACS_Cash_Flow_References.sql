/* NIHRACS Annual Statements.xlsm, Notes rows 101–121 and Accounts column V.
   Reference rows are excluded from statement calculation. No GL assignments are written.
   Set the release ID explicitly only for a separately authorised configuration operation. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ReleaseId int = NULL;
IF @ReleaseId IS NULL OR @ReleaseId <= 0 OR @ReleaseId = 10 OR @@TRANCOUNT <> 0
    THROW 51000, 'Supply the separate NIHRACS FY2026 draft ID in a dedicated session. Draft 10 is protected.', 1;
IF COL_LENGTH('dbo.tblNORM_StatementLine','ParentLineCode') IS NULL
    OR COL_LENGTH('dbo.tblNORM_StatementLine','MappingDescription') IS NULL
    THROW 51001, 'Install the hierarchy and mapping descriptions schema first.', 1;

BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK)
    WHERE ConfigurationReleaseId=@ReleaseId AND EntityCode='NIHRACS' AND FinancialYear=2026
      AND StatusCode='Draft' AND IsDeactivated=0)
    THROW 51002, 'Select an active NIHRACS FY2026 draft.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WITH (UPDLOCK,HOLDLOCK)
    WHERE ConfigurationReleaseId=@ReleaseId AND LineType='hierarchy-detail' AND IsDeactivated=0)
    THROW 51003, 'Configure the NIHRACS reporting hierarchy before adding cash-flow references.', 1;
IF EXISTS (SELECT 1 FROM dbo.tblNORM_StatementLine WITH (UPDLOCK,HOLDLOCK)
    WHERE ConfigurationReleaseId=@ReleaseId AND StatementCode IN ('SCF','SCFREF'))
    THROW 51004, 'The draft already has cash-flow configuration. Review it before seeding.', 1;

INSERT dbo.tblNORM_StatementLine
    (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,ParentLineCode,MappingDescription,CalculationKind,IsClickable)
SELECT @ReleaseId,2026,'NIHRACS','SCFREF',v.SeqNo,v.LineType,v.LineCode,v.LineLabel,v.ParentLineCode,v.MappingDescription,'Heading',0
FROM (VALUES
    (101, N'cashflow-detail', N'CF_OPA_REC_RAG', N'Receipts from Australian Government', N'CF_OPA_REC', N'Australian Government receipts'),
    (102, N'cashflow-detail', N'CF_OPA_REC_OTH', N'Receipts from other funding sources', N'CF_OPA_REC', N'Other funding receipts'),
    (103, N'cashflow-detail', N'CF_OPA_REC_GAS', N'Sale of goods and rendering of services', N'CF_OPA_REC', N'Goods and services receipts'),
    (104, N'cashflow-detail', N'CF_OPA_REC_INT', N'Interest', N'CF_OPA_REC', N'Interest received'),
    (105, N'cashflow-total', N'CF_OPA_REC', N'Total Cash Received', N'CF_OPA', NULL),
    (106, N'cashflow-detail', N'CF_OPA_USE_EMP', N'Employees', N'CF_OPA_USE', N'Employee payments'),
    (107, N'cashflow-detail', N'CF_OPA_USE_SUP', N'Suppliers', N'CF_OPA_USE', N'Supplier payments'),
    (108, N'cashflow-total', N'CF_OPA_USE', N'Total Cash Used', N'CF_OPA', NULL),
    (109, N'cashflow-total', N'CF_OPA', N'Net Cash from/(used by) Operating Activities', N'CF_CSH_NET', NULL),
    (111, N'cashflow-detail', N'CF_INV_REC_PPE', N'Proceeds from sales of property, plant and equipment', N'CF_INV_REC', N'Proceeds from PPE sales'),
    (112, N'cashflow-total', N'CF_INV_REC', N'Total Cash Received', N'CF_INV', NULL),
    (113, N'cashflow-detail', N'CF_INV_USE_PPE', N'Purchase of property, plant and equipment', N'CF_INV_USE', N'Purchase of property, plant and equipment'),
    (114, N'cashflow-detail', N'CF_INV_USE_INV', N'Investments', N'CF_INV_USE', N'Payments for investments'),
    (115, N'cashflow-total', N'CF_INV_USE', N'Total Cash Used', N'CF_INV', NULL),
    (116, N'cashflow-total', N'CF_INV', N'Net Cash (used by)/from Investing Activities', N'CF_CSH_NET', NULL),
    (118, N'cashflow-detail', N'CF_FIN_USE_LEL', N'Principal repayment on lease liabilities', N'CF_FIN_USE', N'Lease principal repayments'),
    (119, N'cashflow-total', N'CF_FIN_USE', N'Total Cash Used', N'CF_FIN', NULL),
    (120, N'cashflow-total', N'CF_FIN', N'Net Cash (used by) Financing Activities', N'CF_CSH_NET', NULL),
    (121, N'cashflow-total', N'CF_CSH_NET', N'Net lncrease/(Decrease) in Cash Held', NULL, NULL)
) v(SeqNo,LineType,LineCode,LineLabel,ParentLineCode,MappingDescription);

UPDATE dbo.tblNORM_ConfigurationRelease
SET ContentHash=CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(36),NEWID())),2),ReviewedBy=NULL,ReviewedUtc=NULL
WHERE ConfigurationReleaseId=@ReleaseId;
INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy)
VALUES ('NIHRACS_HIERARCHY_CHANGED','ConfigurationRelease',CONVERT(varchar(20),@ReleaseId),
    'Added 10 NIHRACS cash-flow classification references and 9 parent totals from the annual statements workbook.',ORIGINAL_LOGIN());
COMMIT TRANSACTION;
