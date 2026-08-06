using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    /// <summary>View-model bundle for the Journal Generation page.</summary>
    public class PPMJournalViewModel
    {
        public List<PPMKpi> Kpis { get; set; }
        public List<PPMRecognitionJournal> RecognitionJournals { get; set; }
        public List<PPMAmortisationJournal> AmortisationJournals { get; set; }

        // Selected journal drill-down
        public PPMJournalDetailHeader SelectedHeader { get; set; }
        public List<PPMJournalEntry> DetailEntries { get; set; }
        public string DetailTotal { get; set; }
        public List<PPMLabelValue> PoSourceFields { get; set; }
        public List<PPMLabelValue> ApprovalAuditFields { get; set; }
        public List<PPMJournalAuditRow> AuditTrail { get; set; }
    }

    /// <summary>
    /// Presentation layer for the Journal Generation page (§3.3): shapes journal entities into
    /// the display view models the .ascx binds, and delegates the approval-workflow writes.
    /// </summary>
    public class PPMJournalService
    {
        private readonly IPPMJournalRepository _repo;
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

        public PPMJournalService() : this(new PPMJournalRepository()) { }
        public PPMJournalService(IPPMJournalRepository repo) { _repo = repo; }

        public PPMJournalViewModel Build(long? selectedJournalId, string recVendors = null, string amortVendors = null)
        {
            var recognition = _repo.GetRecognitionQueue(recVendors);
            var amortisation = _repo.GetAmortisationQueue(amortVendors);

            long? jid = selectedJournalId
                ?? (recognition.Count > 0 ? (long?)recognition[0].JournalId : null);

            PPMJournalDetailHeader header = null;
            var entries = new List<PPMJournalEntry>();
            var poSource = new List<PPMLabelValue>();
            var approval = new List<PPMLabelValue>();
            var audit = new List<PPMJournalAuditRow>();
            string total = "";
            if (jid.HasValue)
            {
                IReadOnlyList<PPMJournalEntryRow> rawEntries;
                IReadOnlyList<PPMJournalAuditRow> rawAudit;
                header = _repo.GetDetail(jid.Value, out rawEntries, out rawAudit);
                if (header != null)
                {
                    entries = rawEntries.Select(ToEntry).ToList();
                    total = entries.Where(e => e.Dc == "Dr").Sum(e => e.AmountValue).ToString("C2", Au);
                    poSource = BuildPoSource(header);
                    approval = BuildApproval(header);
                    audit = rawAudit.ToList();
                }
            }

            return new PPMJournalViewModel
            {
                Kpis = BuildKpis(_repo.GetKpis()),
                RecognitionJournals = recognition.Select(ToRecognition).ToList(),
                AmortisationJournals = amortisation.Select(ToAmortisation).ToList(),
                SelectedHeader = header,
                DetailEntries = entries,
                DetailTotal = total,
                PoSourceFields = poSource,
                ApprovalAuditFields = approval,
                AuditTrail = audit
            };
        }

        // Resolves a PO number to the journal the page should open (Tab 1 "View journals" action).
        public long? ResolveJournalByPo(string poNumber)
        {
            return _repo.ResolveJournalByPo(poNumber);
        }

        // ── Workflow writes ──────────────────────────────────────────────────────
        public int Submit(long id, int userId)
        {
            return _repo.Submit(id, userId);
        }
        public int Approve(long id, int userId, string comments)
        {
            return _repo.Approve(id, userId, comments);
        }
        public int Reject(long id, int userId, string comments)
        {
            return _repo.Reject(id, userId, comments);
        }
        public int Export(long? id, int userId)
        {
            return _repo.Export(id, userId);
        }
        public int ApproveAllReady(string type, int userId)
        {
            return _repo.ApproveAllReady(type, userId);
        }

        // ── Mapping ──────────────────────────────────────────────────────────────
        private static List<PPMKpi> BuildKpis(PPMJournalKpis k)
        {
            return new List<PPMKpi>
        {
            new PPMKpi { Label = "Recognition journals ready",  Value = k.RecognitionJournalsReady.ToString(),  Sub = "Awaiting approval or export",    ValueClass = "blue" },
            new PPMKpi { Label = "Amortisation journals ready", Value = k.AmortisationJournalsReady.ToString(), Sub = "Current period recommended",      ValueClass = "blue" },
            new PPMKpi { Label = "Pending approval",            Value = k.PendingApproval.ToString(),           Sub = "Awaiting approver action",       ValueClass = "amber" },
            new PPMKpi { Label = "Approved & export ready",     Value = k.ApprovedExportReady.ToString(),       Sub = "Ready for interface batch",      ValueClass = "green" },
            new PPMKpi { Label = "Exported this period",        Value = k.ExportedThisPeriod.ToString(),        Sub = "Successfully sent to ERP",       ValueClass = "green" },
        };
        }

        private static PPMRecognitionJournal ToRecognition(PPMRecognitionJournalRow r)
        {
            bool approved = r.Status == "Approved";
            return new PPMRecognitionJournal
            {
                JournalId = r.JournalId,
                JournalRef = r.JournalRef,
                PoInvoice = r.PoNumber + " / " + Short(r.InvoiceNo),
                Vendor = r.Vendor,
                CapexOpex = r.CapexOpex,
                DrAsset = r.DrAccount,
                CrExpense = r.CrAccount,
                Amount = r.Amount.ToString("C0", Au),
                Period = FormatPeriod(r.Period),
                Status = StatusBadge(r.Status),
                ActionText = approved ? "Export" : "Review & approve",
                ActionPrimary = !approved,
                ActionSuccess = approved
            };
        }

        private static PPMAmortisationJournal ToAmortisation(PPMAmortisationJournalRow r)
        {
            bool approved = r.Status == "Approved";
            return new PPMAmortisationJournal
            {
                JournalId = r.JournalId,
                JournalRef = r.JournalRef,
                PoPrepayment = "PO " + r.PoNumber,
                Vendor = r.Vendor,
                CapexOpex = r.CapexOpex,
                Period = FormatPeriod(r.Period),
                DrExpense = r.DrAccount,
                CrAsset = r.CrAccount,
                PeriodAmount = r.PeriodAmount.ToString("C0", Au),
                RemainingBalance = r.RemainingBalance.HasValue ? r.RemainingBalance.Value.ToString("C0", Au) : "",
                Status = StatusBadge(r.Status),
                ActionText = approved ? "Export" : "Review & approve",
                ActionPrimary = !approved,
                ActionSuccess = approved
            };
        }

        private static PPMJournalEntry ToEntry(PPMJournalEntryRow e)
        {
            return new PPMJournalEntry
        {
            Dc = e.DebitCredit,
            Account = e.Account,
            Description = e.Description,
            CostObject = e.CostObject,
            Amount = e.Amount.ToString("N2", Au),
            AmountValue = e.Amount
        };
        }

        private static List<PPMLabelValue> BuildPoSource(PPMJournalDetailHeader h)
        {
            return new List<PPMLabelValue>
        {
            new PPMLabelValue("GL account (original)", h.OriginalGl),
            new PPMLabelValue("Cost centre / WBS",     h.CostObject),
            new PPMLabelValue("Company code",          h.CompanyCode),
            new PPMLabelValue("Vendor",                h.Vendor),
            new PPMLabelValue("Invoice amount",        h.Amount.ToString("C0", Au)),
            new PPMLabelValue("PO line flag",          "✅ Prepayment"),
        };
        }

        private static List<PPMLabelValue> BuildApproval(PPMJournalDetailHeader h)
        {
            return new List<PPMLabelValue>
        {
            new PPMLabelValue("Journal type",      h.JournalType == "Recognition" ? "Recognition (capitalisation)" : "Amortisation (expense)"),
            new PPMLabelValue("Posting period",    FormatPeriod(h.Period)),
            new PPMLabelValue("Preparer",          h.PreparerName ?? "—"),
            new PPMLabelValue("Required approver", h.ApproverName ?? "Finance Controller"),
            new PPMLabelValue("Status",            h.Status),
        };
        }

        private static PPMBadge StatusBadge(string s)
        {
            switch (s)
            {
                case "PendingApproval": return new PPMBadge("Pending approval", "w");
                case "Approved": return new PPMBadge("Approved", "s");
                case "Rejected": return new PPMBadge("Rejected", "e");
                case "Exported": return new PPMBadge("Exported", "b");
                case "Draft": return new PPMBadge("Draft", "a");
                default: return new PPMBadge(s, "");
            }
        }

        private static string Short(string invoiceNo)
        {
            if (string.IsNullOrEmpty(invoiceNo)) return "";
            var parts = invoiceNo.Split('-');
            return parts.Length > 0 ? "INV-" + parts[parts.Length - 1] : invoiceNo;
        }

        private static string FormatPeriod(string yyyymm)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(yyyymm) && DateTime.TryParseExact(yyyymm + "/01", "yyyy/MM/dd",
                Au, DateTimeStyles.None, out d))
                return d.ToString("MMM yyyy", Au);
            return yyyymm;
        }
    }
}
