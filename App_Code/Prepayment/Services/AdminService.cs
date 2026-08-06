using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    // ── View models returned by PPMAdminService ─────────────────────────────────────

    public class PPMAdminControlTowerViewModel
    {
        public List<PPMKpi>                    Kpis          { get; set; }
        public List<PPMProcessTrackerRow>      ProcessTracker { get; set; }
        public List<PPMAdminExceptionViewModel> Exceptions    { get; set; }
        public List<PPMLabelValue>             PeriodSummary  { get; set; }
        public string PeriodLabel  { get; set; }
        public int    ExceptionCount { get; set; }

        // §3.4 Admin-action pickers (data-driven).
        public List<PPMAdminStuckItem>     StuckItems     { get; set; }
        public List<PPMAdminApprover>      Approvers      { get; set; }
        public List<PPMAdminFailedBatch>   FailedBatches  { get; set; }
        public List<PPMAdminOpenException> OpenExceptions { get; set; }
    }

    public class PPMAdminExceptionViewModel
    {
        public string Title  { get; set; }
        public string Detail { get; set; }
        public PPMBadge  Tag    { get; set; }
    }

    // ── Service ──────────────────────────────────────────────────────────────────

    public class PPMAdminService
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private readonly IPPMAdminRepository _repo;

        public PPMAdminService() : this(new PPMAdminRepository()) { }
        public PPMAdminService(IPPMAdminRepository repo) { _repo = repo; }

        public PPMAdminControlTowerViewModel Build()
        {
            var kpis      = _repo.GetKpis();
            var tracker   = _repo.GetProcessTracker();
            var exceptions = _repo.GetExceptions();
            var period    = _repo.GetPeriodSummary();
            var actionTargets = _repo.GetActionTargets();
            var stuck = actionTargets.Stuck;
            var approvers = actionTargets.Approvers;
            var batches = actionTargets.Batches;
            var openExc = actionTargets.Exceptions;

            return new PPMAdminControlTowerViewModel
            {
                Kpis           = BuildKpis(kpis),
                ProcessTracker = tracker.Select(MapTrackerRow).ToList(),
                Exceptions     = exceptions.Select(MapException).ToList(),
                PeriodSummary  = BuildPeriodSummary(period),
                PeriodLabel    = period.PeriodLabel ?? "—",
                ExceptionCount = kpis.ExceptionsOpen,
                StuckItems     = stuck.ToList(),
                Approvers      = approvers.ToList(),
                FailedBatches  = batches.ToList(),
                OpenExceptions = openExc.ToList(),
            };
        }

        // ── §3.4 Admin actions (write paths) ──────────────────────────────────────

        public int ForceAdvance(string poNumber, int userId)
        {
            if (string.IsNullOrWhiteSpace(poNumber)) throw new System.ArgumentException("Select a stuck item to advance.");
            return _repo.ForceAdvance(poNumber.Trim(), userId);
        }

        public int ReassignApprover(string poNumber, int approverUserId, int userId)
        {
            if (string.IsNullOrWhiteSpace(poNumber)) throw new System.ArgumentException("Select a PO to reassign.");
            if (approverUserId <= 0) throw new System.ArgumentException("Select an approver.");
            return _repo.ReassignApprover(poNumber.Trim(), approverUserId, userId);
        }

        public int ResolveException(long exceptionId, string note, int userId)
        {
            if (exceptionId <= 0) throw new System.ArgumentException("Select an exception to clear.");
            return _repo.ResolveException(exceptionId, note, userId);
        }

        public int ReExportBatch(long batchId, int userId)
        {
            if (batchId <= 0) throw new System.ArgumentException("Select a failed batch to re-export.");
            return _repo.ReExportBatch(batchId, userId);
        }

        // ── KPI mapping ──────────────────────────────────────────────────────────

        private static List<PPMKpi> BuildKpis(PPMAdminKpis k)
        {
            decimal outstanding = k.OutstandingBalance;
            return new List<PPMKpi>
            {
                new PPMKpi { Label = "Total recognised",    Value = FormatMoney(k.TotalRecognised),  Sub = "All company codes YTD",          ValueClass = "" },
                new PPMKpi { Label = "Total amortised",     Value = FormatMoney(k.TotalAmortised),   Sub = "Expensed to date",               ValueClass = "green" },
                new PPMKpi { Label = "Outstanding balance", Value = FormatMoney(outstanding),        Sub = "Net prepayment asset",           ValueClass = "blue" },
                new PPMKpi { Label = "Awaiting approval",   Value = k.AwaitingApproval.ToString(),   Sub = "Recognition & amortisation",     ValueClass = k.AwaitingApproval > 0 ? "amber" : "" },
                new PPMKpi { Label = "Exceptions open",     Value = k.ExceptionsOpen.ToString(),     Sub = "Blocked or rejected items",      ValueClass = "",
                          ValueStyle = k.ExceptionsOpen > 0 ? "color:var(--error)" : "" },
            };
        }

        // ── Process tracker mapping ───────────────────────────────────────────────

        private static PPMProcessTrackerRow MapTrackerRow(PPMAdminProcessTrackerRow r)
        {
            return new PPMProcessTrackerRow
            {
                PoNumber        = r.PoNumber,
                Vendor          = r.VendorName,
                CapexOpex       = r.CapexOpex,
                Amount          = r.TotalValue.ToString("C0", Au),
                PoFlag          = BoolGlyph(r.PoFlagStage),
                PoFlagStyle     = BoolStyle(r.PoFlagStage),
                Invoice         = BoolGlyph(r.InvoiceStage),
                InvoiceStyle    = BoolStyle(r.InvoiceStage),
                Setup           = SetupText(r.SetupStage),
                SetupStyle      = SetupTextStyle(r.SetupStage),
                Recognised      = JournalGlyph(r.RecognitionStage),
                RecognisedStyle = JournalStyle(r.RecognitionStage),
                Amortising      = JournalGlyph(r.AmortisationStage),
                AmortisingStyle = JournalStyle(r.AmortisationStage),
                Export          = StageGlyph(r.ExportStage),
                ExportStyle     = StageStyle(r.ExportStage),
                Status          = OverallStatus(r),
            };
        }

        // PoFlag / Invoice: 1 = positive (✅), 3 = negative (❌ red), 0 = pending / none (—).
        private static string BoolGlyph(int stage)
        {
            if (stage == 1) return "✅";
            if (stage == 3) return "❌";
            return "—";
        }

        private static string BoolStyle(int stage)
        {
            return stage == 3 ? "color:var(--error)" : "";
        }

        // Recognition / Amortisation journals: 1 = approved/exported (✅), 2 = pending (⚠),
        // 3 = rejected (❌ red), 0 = none (—).
        private static string JournalGlyph(int stage)
        {
            if (stage == 1) return "✅";
            if (stage == 2) return "⚠";
            if (stage == 3) return "❌";
            return "—";
        }

        private static string JournalStyle(int stage)
        {
            if (stage == 2) return "color:var(--warn)";
            if (stage == 3) return "color:var(--error)";
            return "";
        }

        // Setup is shown as text per the SetupStatus value rather than a glyph.
        private static string SetupText(int stage)
        {
            if (stage == 1) return "Complete";
            if (stage == 2) return "AmortisationNeeded";
            return "—";
        }

        private static string SetupTextStyle(int stage)
        {
            if (stage == 1) return "color:var(--success)";
            if (stage == 2) return "color:var(--warn)";
            return "";
        }

        // Export still uses the original 0/1/2 glyph scheme.
        private static string StageGlyph(int stage)
        {
            if (stage == 1) return "✅";
            if (stage == 2) return "⚠";
            return "—";
        }

        private static string StageStyle(int stage)
        {
            return stage == 2 ? "color:var(--warn)" : "";
        }

        private static PPMBadge OverallStatus(PPMAdminProcessTrackerRow r)
        {
            if (r.ExportStage == 1)             return new PPMBadge("Completed",         "s");
            if (r.ExportStage == 2)             return new PPMBadge("Ready for export",  "b");
            if (r.AmortisationStage == 3)       return new PPMBadge("Amort. rejected",   "e");
            if (r.AmortisationStage == 2)       return new PPMBadge("Pending approval",  "w");
            if (r.AmortisationStage == 1)       return new PPMBadge("Amortising",        "b");
            if (r.RecognitionStage == 3)        return new PPMBadge("Recog. rejected",   "e");
            if (r.RecognitionStage == 2)        return new PPMBadge("Pending approval",  "w");
            if (r.RecognitionStage == 1)        return new PPMBadge("Journal pending",   "b");
            if (r.SetupStage == 2)              return new PPMBadge("Setup in progress", "w");
            if (r.SetupStage == 1)              return new PPMBadge("Setup complete",    "s");
            if (r.InvoiceStage == 1)            return new PPMBadge("Invoice received",  "b");
            if (r.PoFlagStage == 1)             return new PPMBadge("PO flagged",        "a");
            if (r.PoFlagStage == 3)             return new PPMBadge("Not prepayment",    "e");
            return new PPMBadge("New", "");
        }

        // ── Exception mapping ─────────────────────────────────────────────────────

        private static PPMAdminExceptionViewModel MapException(PPMAdminExceptionRow e)
        {
            string tagText, tagCls;
            switch ((e.ExceptionType ?? "").Trim())
            {
                case "Blocked":   tagText = "Blocked";    tagCls = "e"; break;
                case "FollowUp":  tagText = "Follow up";  tagCls = "w"; break;
                case "Error":     tagText = "Error";      tagCls = "e"; break;
                case "Ready":     tagText = "Ready";      tagCls = "s"; break;
                case "Duplicate": tagText = "Duplicate";  tagCls = "e"; break;
                case "Variance":  tagText = "Variance";   tagCls = "w"; break;
                default:          tagText = "Notice";     tagCls = "a"; break;
            }

            return new PPMAdminExceptionViewModel
            {
                Title  = e.Title,
                Detail = e.Detail,
                Tag    = new PPMBadge(tagText, tagCls),
            };
        }

        // ── Period summary mapping ────────────────────────────────────────────────

        private static List<PPMLabelValue> BuildPeriodSummary(PPMAdminPeriodSummary ps)
        {
            return new List<PPMLabelValue>
            {
                new PPMLabelValue("New PO lines flagged",         ps.LinesFlagged.ToString()),
                new PPMLabelValue("New invoices assessed",        ps.InvoicesAssessed.ToString()),
                new PPMLabelValue("Recognition journals raised",  ps.RecognitionJournals.ToString()),
                new PPMLabelValue("Amortisation journals posted", ps.AmortisationJournals.ToString()),
                new PPMLabelValue("Journals exported to ERP",     ps.JournalsExported.ToString(),
                               ps.JournalsExported > 0 ? "green" : null),
            };
        }

        // ── Formatting ────────────────────────────────────────────────────────────

        private static string FormatMoney(decimal amount)
        {
            if (amount >= 1000000m)
                return "$" + (amount / 1000000m).ToString("0.00", Au) + "m";
            if (amount >= 1000m)
                return "$" + (amount / 1000m).ToString("0.0", Au) + "k";
            return amount.ToString("C0", Au);
        }
    }
}
