using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Prepayment.Web.DataAccess;

namespace Prepayment.Web.Services.ExcelImport
{
    /// <summary>Result summary returned to the Import tab after a load.</summary>
    public class PPMImportResult
    {
        public int Gls, Managers, Vendors, Groups, Users, PurchaseOrders, Lines, Invoices, GlBalances, Journals, ReconRows;
        public string FileName;
    }

    /// <summary>
    /// Parses an uploaded Excel workbook and FULL-REPLACES the prepayment data: clears every
    /// prepayment table in FK-safe order and loads the parsed dataset in a single transaction,
    /// then generates schedules/journals and builds the reconciliation. This is the sole way data
    /// enters the database (the standalone SQL seed has been retired in favour of this import).
    /// </summary>
    public class PPMImportService
    {
        // Tables cleared child-first (mirrors the seed's delete block).
        private static readonly string[] ClearOrder =
        {
            "ExceptionItem","WorkflowReminder","GroupWorkflowState","JournalAudit","JournalEntry",
            "Journal","ExportBatch","AmortisationPeriod","AmortisationSchedule","Reconciliation",
            "GlBalanceRecord","GlExtractFile","Invoice","PoDeliveryLine","PurchaseOrder",
            "DeliveryGroup","Manager","Vendor","PrepaymentGlAccount","Division","CompanyCode","AppUser",
        };

        public PPMImportResult ImportWorkbook(Stream xlsx, string fileName, int userId)
        {
            var ds = PPMExcelParser.Parse(xlsx);

            using (var tx = PPMDbHelper.BeginTransaction())
            {
                // Required: Reconciliation has a PERSISTED computed column -> writes need these ON.
                tx.ExecuteText("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;");

                // Keep the real Windows administrator across the full-replace import. The
                // workbook contains demonstration accounts; replacing the caller with one of
                // them would lock the administrator out after a successful load.
                var importingUser = tx.QuerySingleText(
                    "SELECT Id, WindowsAccount, DisplayName, RoleName FROM dbo.tblPPM_AppUser WHERE Id = ? AND IsDeleted = 0 AND IsActive = 1;",
                    r => new PPMUserRow
                    {
                        Id = Convert.ToInt32(r.GetValue(0)),
                        WindowsAccount = Convert.ToString(r.GetValue(1)),
                        DisplayName = r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2)),
                        RoleName = Convert.ToString(r.GetValue(3))
                    },
                    userId);

                // 1) Clear (child-first)
                foreach (var t in ClearOrder)
                    tx.ExecuteText("DELETE FROM dbo.tblPPM_" + t + ";");

                // 2) Users (IDENTITY_INSERT — fixed ids drive group preparer/approver refs)
                tx.ExecuteText("SET IDENTITY_INSERT dbo.tblPPM_AppUser ON;");
                foreach (var u in ds.Users.Where(u =>
                    u.Id != importingUser.Id &&
                    !string.Equals(u.WindowsAccount, importingUser.WindowsAccount, StringComparison.OrdinalIgnoreCase)))
                    tx.ExecuteText(@"INSERT dbo.tblPPM_AppUser (Id,WindowsAccount,DisplayName,RoleName,IsActive,CreatedBy,CreatedDate)
                                 VALUES (?,?,?,?,1,1,SYSUTCDATETIME());",
                               u.Id, u.WindowsAccount, u.DisplayName, u.RoleName);
                tx.ExecuteText(@"INSERT dbo.tblPPM_AppUser (Id,WindowsAccount,DisplayName,RoleName,IsActive,CreatedBy,CreatedDate)
                             VALUES (?,?,?,?,1,?,SYSUTCDATETIME());",
                           importingUser.Id, importingUser.WindowsAccount, importingUser.DisplayName,
                           "Admin", importingUser.Id);
                tx.ExecuteText("SET IDENTITY_INSERT dbo.tblPPM_AppUser OFF;");

                // 3) Company + division (single)
                tx.ExecuteText(@"INSERT dbo.tblPPM_CompanyCode (CompanyCode,CompanyName,CurrencyCode,IsActive,CreatedBy)
                             VALUES ('1000',N'Department of Defence','AUD',1,1);
                             INSERT dbo.tblPPM_Division (DivisionCode,DivisionName,IsActive,CreatedBy)
                             VALUES ('DIV-001',N'Defence',1,1);");

                // 4) GLs
                foreach (var g in ds.Gls)
                    tx.ExecuteText(@"INSERT dbo.tblPPM_PrepaymentGlAccount (GlAccount,GlDescription,AssetClassification,ExpenditureType,AasbReference,CreatedBy)
                                 VALUES (?,?,?,?,?,1);", g.GlAccount, g.GlDescription, g.AssetClassification, g.ExpenditureType, g.AasbReference);

                // 5) Managers
                foreach (var m in ds.Managers)
                    tx.ExecuteText(@"INSERT dbo.tblPPM_Manager (Id,ManagerDesc,Program,IsActive,CreatedBy)
                                 VALUES (?,?,?,1,1);", m.Id, m.ManagerDesc, m.Program);

                // 6) Vendors
                foreach (var v in ds.Vendors)
                    tx.ExecuteText(@"INSERT dbo.tblPPM_Vendor (VendorCode,VendorName,IsActive,CreatedBy)
                                 VALUES (?,?,1,1);", v.VendorCode, v.VendorName);

                // 7) Delivery groups (default GL 514004, division/company resolved)
                tx.ExecuteText("DECLARE @dv BIGINT=(SELECT Id FROM dbo.tblPPM_Division WHERE DivisionCode='DIV-001');");
                long companyId = tx.QuerySingleText("SELECT Id FROM dbo.tblPPM_CompanyCode WHERE CompanyCode='1000';", MapLong);
                long defaultGl = tx.QuerySingleText("SELECT Id FROM dbo.tblPPM_PrepaymentGlAccount WHERE GlAccount='514004';", MapLong);
                long divId = tx.QuerySingleText("SELECT Id FROM dbo.tblPPM_Division WHERE DivisionCode='DIV-001';", MapLong);
                foreach (var g in ds.Groups)
                    tx.ExecuteText(@"INSERT dbo.tblPPM_DeliveryGroup (DeliveryGroupCode,GroupName,DivisionId,PreparerUserId,ApproverUserId,PrepaymentGlId,CompanyCodeId,IsActive,CreatedBy)
                                 VALUES (?,?,?,?,?,?,?,1,1);",
                               g.Code, g.Name, divId, g.PreparerUserId, g.ApproverUserId, defaultGl, companyId);

                // Lookup maps (code -> id) for FK resolution
                var vendorIds = tx.QueryStringLongDictionaryText("SELECT VendorCode, Id FROM dbo.tblPPM_Vendor;");
                var groupIds = tx.QueryStringLongDictionaryText("SELECT DeliveryGroupCode, Id FROM dbo.tblPPM_DeliveryGroup;");

                // 8) Purchase orders
                foreach (var p in ds.PurchaseOrders)
                {
                    decimal totalCommitment = p.TotalCommitment ?? 0;
                    tx.ExecuteText(@"INSERT dbo.tblPPM_PurchaseOrder
                        (PoNumber,VendorId,DeliveryGroupId,ProjectCode,TotalValue,CurrentCommitment,TotalCommitment,CapexOpex,
                         CapabilityMgrId,DeliveryMgrId,GrIndicator,IrIndicator,ProcessControl,SourceSystem,CurrencyCode,PoDate,CompanyCodeId,LinesCount,SourceLoadDate,CreatedBy)
                        VALUES (?,?,?,?,?,?,?,?,
                         ?,?,?,?,?,?,'AUD',?,?,0,'2026-06-03',1);",
                        p.PoNumber,
                        LookupNullable(vendorIds, p.VendorCode),
                        LookupNullable(groupIds, p.GroupCode),
                        p.Wbs, totalCommitment, p.CurrentCommitment, totalCommitment, p.CapexOpex,
                        p.CapabilityMgrId, p.DeliveryMgrId, p.GrIndicator, p.IrIndicator,
                        p.ProcessControl, p.SourceSystem, ParseDate(p.PoDate), companyId);
                }

                var poIds = tx.QueryStringLongDictionaryText("SELECT PoNumber, Id FROM dbo.tblPPM_PurchaseOrder;");

                // 9) Delivery lines
                foreach (var l in ds.Lines)
                {
                    long poId;
                    if (!poIds.TryGetValue(l.PoNumber, out poId)) continue;
                    tx.ExecuteText(@"INSERT dbo.tblPPM_PoDeliveryLine
                        (PurchaseOrderId,LineNumber,AcctAssignNumber,Description,GlAccount,GlDescription,WbsCostCentre,WbsDescription,CapexOpex,ScheduledDate,Quantity,OpenQuantity,LineValue,PrepaymentFlag,FlaggedByUserId,FlaggedDate,CreatedBy)
                        VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,1,SYSUTCDATETIME(),1);",
                        poId, l.LineNumber, l.AcctAssignNumber, l.Description, l.GlAccount, l.GlDescription,
                        l.Wbs, l.WbsDescription, l.CapexOpex, ParseDate(l.ScheduledDate), l.Quantity, l.OpenQuantity, l.LineValue, l.Flag);
                }

                // 10) Invoices (link PO + first matching line + vendor)
                foreach (var inv in ds.Invoices)
                {
                    long? poId = inv.PoNumber != null && poIds.ContainsKey(inv.PoNumber) ? poIds[inv.PoNumber] : (long?)null;
                    long? lineId = null;
                    if (poId.HasValue && inv.LineNumber.HasValue)
                        lineId = tx.QuerySingleOrDefaultText(
                            "SELECT TOP 1 Id FROM dbo.tblPPM_PoDeliveryLine WHERE PurchaseOrderId=? AND LineNumber=?;",
                            MapNullableLong,
                            poId.Value, inv.LineNumber.Value);

                    tx.ExecuteText(@"INSERT dbo.tblPPM_Invoice
                        (InvoiceNo,PurchaseOrderId,PoDeliveryLineId,VendorId,GlAccount,PrepaymentGlDesc,CashGlAccount,CashGlDescription,
                         ProfitCentre,ProfitCentreDesc,WbsElement,WbsDescription,CapexOpex,InvoiceDate,PostFiscalYear,PostFiscalPeriod,PaymentRunDate,
                         Amount,AmountDoc,FxRate,ForeignCurrency,Description,Flag,SetupStatus,IsExistingBalance,SourceSystem,SourceLoadDate,CreatedBy)
                        VALUES (?,?,?,?,?,?,?,?,
                         ?,?,?,?,?,?,?,?,?,
                         ?,?,?,?,?,'Prepayment',?,0,?,'2026-06-03',1);",
                        inv.InvoiceNo, poId, lineId,
                        LookupNullable(vendorIds, inv.VendorCode), inv.GlAccount, inv.PrepaymentGlDesc, inv.CashGlAccount, inv.CashGlDescription,
                        inv.ProfitCentre, inv.ProfitCentreDesc, inv.WbsElement, inv.WbsDescription, inv.CapexOpex,
                        ParseDate(inv.InvoiceDate), inv.PostFiscalYear, inv.PostFiscalPeriod, ParseDate(inv.PaymentRunDate),
                        inv.Amount, inv.AmountDoc, inv.FxRate, inv.ForeignCurrency, inv.Description, inv.SetupStatus, inv.SourceSystem);
                }

                tx.ExecuteText("UPDATE po SET LinesCount=(SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine l WHERE l.PurchaseOrderId=po.Id) FROM dbo.tblPPM_PurchaseOrder po;");

                // 11) Generate schedules + journals for the 10 largest prepayment invoices (same as the seed).
                int journals = GenerateJournals(tx);

                // 12) GL extract + balance records + reconciliation
                int reconRows = BuildReconciliation(tx, ds, companyId);

                tx.Commit();

                return new PPMImportResult
                {
                    FileName = fileName,
                    Gls = ds.Gls.Count, Managers = ds.Managers.Count, Vendors = ds.Vendors.Count,
                    Groups = ds.Groups.Count, Users = ds.Users.Count, PurchaseOrders = ds.PurchaseOrders.Count,
                    Lines = ds.Lines.Count, Invoices = ds.Invoices.Count, GlBalances = ds.GlBalances.Count,
                    Journals = journals, ReconRows = reconRows,
                };
            }
        }

        /// <summary>Typed row for the journal-generation candidate query (avoids dynamic).</summary>
        private class GenRow
        {
            public long InvoiceId { get; set; }
            public long PrepaymentGlId { get; set; }
            public string Asset { get; set; }
            public string Exp { get; set; }
            public string ExpGl { get; set; }
            public string StartDate { get; set; }
            public string Wbs { get; set; }
            public int Periods { get; set; }
        }

        private static int GenerateJournals(PPMTransaction tx)
        {
            var rows = tx.QueryText(@"
                SELECT TOP (10) inv.Id AS InvoiceId, pg.Id AS PrepaymentGlId, pg.AssetClassification AS Asset,
                       pg.ExpenditureType AS Exp, ISNULL(inv.CashGlAccount, inv.GlAccount) AS ExpGl,
                       CASE WHEN pg.AssetClassification='Current' THEN 12 ELSE 24 END AS Periods,
                       CONVERT(VARCHAR(10), ISNULL(inv.InvoiceDate,'2026-06-01'),120) AS StartDate,
                       ISNULL(inv.WbsElement,'WBS') AS Wbs
                FROM dbo.tblPPM_Invoice inv
                JOIN dbo.tblPPM_PrepaymentGlAccount pg ON pg.GlAccount = inv.GlAccount
                WHERE inv.IsDeleted=0 AND inv.IsExistingBalance=0 AND inv.Amount>0
                ORDER BY inv.Amount DESC;", MapGenRow);

            foreach (var r in rows)
            {
                try
                {
                    tx.Execute("prepayment.AmortisationSetup_GenerateScheduleAndJournals",
                        r.InvoiceId, r.Asset, r.Exp, "Scheduled", ParseDate(r.StartDate), null,
                        r.Periods, "Monthly", r.PrepaymentGlId, r.ExpGl, r.Wbs, "1000", 1);
                }
                catch { /* skip any invoice that can't generate (mirrors the seed's TRY/CATCH) */ }
            }

            // mixed statuses so the queue looks realistic
            tx.ExecuteText(@"UPDATE dbo.tblPPM_Journal SET Status='PendingApproval'
                         WHERE Id IN (SELECT TOP 6 Id FROM dbo.tblPPM_Journal WHERE Status='Draft' ORDER BY Id);
                         UPDATE dbo.tblPPM_Journal SET Status='Approved', ApproverUserId=1, ApprovedDate=SYSUTCDATETIME()
                         WHERE Id IN (SELECT TOP 3 Id FROM dbo.tblPPM_Journal WHERE Status='Draft' ORDER BY Id DESC);");

            return tx.ExecuteScalarText<int>("SELECT COUNT(*) FROM dbo.tblPPM_Journal;");
        }

        private static int BuildReconciliation(PPMTransaction tx, PPMImportDataset ds, long companyId)
        {
            if (ds.GlBalances.Count == 0) return 0;
            int naccts = ds.GlBalances.Select(b => b.GlAccount).Distinct().Count();
            int ngrps = ds.GlBalances.Select(b => b.GroupCode).Distinct().Count();

            long fileId = tx.QuerySingleText(@"
                INSERT dbo.tblPPM_GlExtractFile (SourceFileName,ReportingPeriod,ExtractDate,AccountCount,GroupCount,CreatedBy)
                VALUES (N'GL_Balance_Jun2026.csv','2026/06','2026-06-30',?,?,1);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", MapLong, naccts, ngrps);

            foreach (var b in ds.GlBalances)
                tx.ExecuteText(@"INSERT dbo.tblPPM_GlBalanceRecord
                    (GlExtractFileId,DeliveryGroupId,PrepaymentGlId,CompanyCodeId,FiscalYear,FiscalPeriod,OpeningBalance,PeriodDebit,PeriodCredit,ClosingBalance,ExtractDate,CreatedBy)
                    SELECT ?,(SELECT Id FROM dbo.tblPPM_DeliveryGroup WHERE DeliveryGroupCode=?),
                           (SELECT Id FROM dbo.tblPPM_PrepaymentGlAccount WHERE GlAccount=?),?,?,?,0,?,?,?,'2026-06-30',1;",
                    fileId, b.GroupCode, b.GlAccount, companyId, b.FiscalYear, b.FiscalPeriod, b.Debit, b.Credit, b.Closing);

            // SAP (closing) vs live FINHUB (outstanding) -> Reconciliation rows
            tx.ExecuteText(@"
                ;WITH sap AS (
                    SELECT DeliveryGroupId, PrepaymentGlId, SapBalance=SUM(ClosingBalance)
                    FROM dbo.tblPPM_GlBalanceRecord WHERE GlExtractFileId=? AND IsDeleted=0
                    GROUP BY DeliveryGroupId, PrepaymentGlId),
                fin AS (
                    SELECT DeliveryGroupId, PrepaymentGlId, Finhub=SUM(Outstanding)
                    FROM prepayment.fn_FinhubBalance() GROUP BY DeliveryGroupId, PrepaymentGlId),
                merged AS (
                    SELECT DeliveryGroupId=ISNULL(sap.DeliveryGroupId,fin.DeliveryGroupId),
                           PrepaymentGlId=ISNULL(sap.PrepaymentGlId,fin.PrepaymentGlId),
                           SapBalance=ISNULL(sap.SapBalance,0), PrepaymentBal=ISNULL(fin.Finhub,0),
                           HasSap=CASE WHEN sap.DeliveryGroupId IS NOT NULL THEN 1 ELSE 0 END,
                           HasFin=CASE WHEN fin.DeliveryGroupId IS NOT NULL THEN 1 ELSE 0 END
                    FROM sap FULL OUTER JOIN fin ON fin.DeliveryGroupId=sap.DeliveryGroupId AND fin.PrepaymentGlId=sap.PrepaymentGlId
                    WHERE ISNULL(sap.DeliveryGroupId,fin.DeliveryGroupId) IS NOT NULL)
                INSERT dbo.tblPPM_Reconciliation (DeliveryGroupId,PrepaymentGlId,Period,GlExtractFileId,SapBalance,PrepaymentBalance,Status,CreatedBy)
                SELECT DeliveryGroupId,PrepaymentGlId,'2026/06',?,SapBalance,PrepaymentBal,
                       CASE WHEN HasSap=0 OR HasFin=0 THEN 'NotMatched'
                            WHEN ABS(SapBalance-PrepaymentBal)<=0.01 THEN 'Reconciled' ELSE 'Variance' END,1
                FROM merged;", fileId, fileId);

            return tx.ExecuteScalarText<int>("SELECT COUNT(*) FROM dbo.tblPPM_Reconciliation;");
        }

        private static long? LookupNullable(Dictionary<string, long> map, string key)
        {
            long id;
            return key != null && map.TryGetValue(key, out id) ? id : (long?)null;
        }

        private static DateTime? ParseDate(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, out d) ? d : (DateTime?)null;
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

        private static long MapLong(IDataRecord r)
        {
            return r.IsDBNull(0) ? 0L : Convert.ToInt64(r.GetValue(0));
        }

        private static long? MapNullableLong(IDataRecord r)
        {
            return r.IsDBNull(0) ? (long?)null : Convert.ToInt64(r.GetValue(0));
        }

        private static GenRow MapGenRow(IDataRecord r)
        {
            return new GenRow
        {
            InvoiceId      = PPMRow.GetLong(r, "InvoiceId"),
            PrepaymentGlId = PPMRow.GetLong(r, "PrepaymentGlId"),
            Asset          = PPMRow.GetString(r, "Asset"),
            Exp            = PPMRow.GetString(r, "Exp"),
            ExpGl          = PPMRow.GetString(r, "ExpGl"),
            StartDate      = PPMRow.GetString(r, "StartDate"),
            Wbs            = PPMRow.GetString(r, "Wbs"),
            Periods        = PPMRow.GetInt(r, "Periods"),
        };
        }
    }
}
