using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models;
using Prepayment.Web.Models.Dtos;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// PO Identification view-model bundle handed to the user control for binding.
    /// </summary>
    public class PPMPoIdentificationViewModel
    {
        public List<PPMKpi> Kpis { get; set; }
        public List<PPMSearchResultPo> SearchResults { get; set; }
        public PPMDeliveryScheduleHeader ScheduleHeader { get; set; }
        public List<PPMDeliveryLine> DeliveryLines { get; set; }
        public List<PPMExistingPrepaymentPo> ExistingPrepaymentPos { get; set; }

        // Footer summary line under the delivery-schedule grid.
        public string ScheduleSummary { get; set; }
        public int ExistingActiveCount { get; set; }
    }

    /// <summary>
    /// Business/presentation layer for PO Identification. Pulls raw entities from the repository and shapes
    /// them into the display view models the .ascx binds to (currency formatting, badges,
    /// toggle labels). Keeps all formatting out of both the DB and the markup.
    /// </summary>
    public class PPMPoIdentificationService
    {
        private readonly IPPMPoIdentificationRepository _repo;
        private static readonly CultureInfo AuCulture = CultureInfo.GetCultureInfo("en-AU");

        public PPMPoIdentificationService() : this(new PPMPoIdentificationRepository()) { }

        public PPMPoIdentificationService(IPPMPoIdentificationRepository repo)
        {
            _repo = repo;
        }

        // ── Full page load ───────────────────────────────────────────────────────
        public PPMPoIdentificationViewModel Build(PPMPoSearchCriteria criteria, long? selectedPoId, string vendorFilter = null)
        {
            var search = _repo.SearchPurchaseOrders(criteria);

            // Default the open delivery schedule to: the explicitly selected PO, else the first
            // PO that still has lines needing classification (what the user most likely wants to
            // action), else the first result.
            long? poId = selectedPoId;
            if (!poId.HasValue && search.Count > 0)
            {
                var needsAction = search.FirstOrDefault(r => r.UnreviewedLines > 0);
                poId = (needsAction ?? search[0]).PoId;
            }

            PPMDeliveryScheduleHeader header = null;
            var lines = new List<PPMDeliveryLine>();
            if (poId.HasValue)
            {
                IReadOnlyList<PPMPoDeliveryLine> raw;
                header = _repo.GetDeliverySchedule(poId.Value, out raw);
                lines = raw.Select(ToDeliveryLine).ToList();
            }

            var existing = _repo.GetExistingPrepaymentPos(vendorFilter);

            return new PPMPoIdentificationViewModel
            {
                Kpis = BuildKpis(_repo.GetKpis()),
                SearchResults = search.Select(ToSearchResult).ToList(),
                ScheduleHeader = header,
                DeliveryLines = lines,
                ExistingPrepaymentPos = existing.Select(ToExistingPo).ToList(),
                ScheduleSummary = BuildScheduleSummary(header, lines),
                ExistingActiveCount = existing.Count
            };
        }

        /// <summary>Distinct delivery groups for the Tab 1 search dropdown.</summary>
        public IReadOnlyList<Models.Entities.PPMDeliveryGroupOption> GetDeliveryGroups()
        {
            return _repo.GetDeliveryGroups();
        }

        // ── Writes (delegated, kept here so the handler stays thin) ─────────────
        public int SaveLineFlag(PPMFlagLineRequest request, int userId)
        {
            return _repo.UpdateLineFlag(request, userId);
        }
        public Models.Entities.PPMConfirmResult ConfirmAndAdvance(long poId, int userId)
        {
            return _repo.ConfirmAndAdvance(poId, userId);
        }

        // ── KPI mapping ─────────────────────────────────────────────────────────
        private static List<PPMKpi> BuildKpis(PPMPoIdentificationKpis k)
        {
            return new List<PPMKpi>
            {
                new PPMKpi { Label = "New POs today",          Value = k.NewPosToday.ToString(),            Sub = "From daily SQL commitment load",            ValueClass = "amber" },
                new PPMKpi { Label = "Flagged as prepayment",  Value = k.FlaggedAsPrepayment.ToString(),    Sub = "Across " + k.FlaggedVendorCount + " vendors", ValueClass = "green" },
                new PPMKpi { Label = "Awaiting user review",   Value = k.AwaitingReview.ToString(),         Sub = "Lines unclassified",                        ValueClass = "amber" },
                new PPMKpi { Label = "Not prepayment",         Value = k.NotPrepayment.ToString(),          Sub = "Marked not applicable",                     ValueClass = "" },
                new PPMKpi { Label = "Total commitment value", Value = FormatMillions(k.TotalCommitmentValue), Sub = "Current period",                         ValueClass = "" },
            };
        }

        // ── Search-result mapping (badge logic mirrors the screenshot) ──────────
        private static PPMSearchResultPo ToSearchResult(PPMPoSearchResult r)
        {
            PPMBadge badge;
            if (r.UnreviewedLines > 0 && r.FlaggedLines == 0 && r.UnreviewedLines == r.LinesCount)
                badge = new PPMBadge("Not reviewed", "a");
            else if (r.UnreviewedLines > 0 && r.FlaggedLines == 0)
                badge = new PPMBadge(r.UnreviewedLines + " unreviewed", "w");
            else if (r.FlaggedLines > 0 && r.UnreviewedLines > 0)
                badge = new PPMBadge(r.UnreviewedLines + " unreviewed", "w");
            else if (r.FlaggedLines > 0)
                badge = new PPMBadge(r.FlaggedLines + " flagged", "s");
            else
                badge = new PPMBadge("Not reviewed", "a");

            return new PPMSearchResultPo
            {
                PoNumber = r.PoNumber,
                Vendor = r.Vendor,
                Project = r.Project,
                Wbs = r.Wbs ?? r.Project,
                DeliveryGroup = r.DeliveryGroup,
                DeliveryGroupName = r.DeliveryGroupName,
                CapexOpex = r.CapexOpex,
                CapabilityManager = r.CapabilityManager,
                DeliveryManager = r.DeliveryManager,
                PoValue = FormatCurrency(r.PoValue),
                CurrentCommitment = FormatCurrency(r.CurrentCommitment),
                Currency = r.Currency,
                PoDate = FormatDate(r.PoDate),
                Lines = r.LinesCount.ToString(),
                PrepaymentLines = badge,
                ActionText = "Open delivery schedule",
                ActionPrimary = r.UnreviewedLines > 0,
                RowStyle = ""
            };
        }

        // ── Delivery-line mapping (toggle label + note rendering) ───────────────
        private static PPMDeliveryLine ToDeliveryLine(PPMPoDeliveryLine l)
        {
            bool isPrepay = string.Equals(l.PrepaymentFlag, "Prepayment", StringComparison.OrdinalIgnoreCase);
            bool isNot = string.Equals(l.PrepaymentFlag, "NotPrepayment", StringComparison.OrdinalIgnoreCase);
            bool decided = isPrepay || isNot;

            return new PPMDeliveryLine
            {
                Line = l.LineNumber.ToString(),
                AcctAssign = l.AcctAssignNumber.ToString(),
                Description = l.Description,
                ServiceNote = l.ServiceNote,
                GlAccount = l.GlAccount,
                GlDescription = l.GlDescription,
                Wbs = l.WbsCostCentre,
                WbsDescription = l.WbsDescription,
                CapexOpex = l.CapexOpex,
                ScheduledDate = FormatDate(l.ScheduledDate),
                Qty = l.Quantity.HasValue ? l.Quantity.Value.ToString("#,0.###", AuCulture) : "",
                UnitPrice = FormatCurrency(l.UnitPrice),
                LineValue = FormatCurrency(l.LineValue),
                IsPrepayment = isPrepay,
                Decided = decided,
                // §3.1 3-state selector value.
                Flag = isPrepay ? "Prepayment" : (isNot ? "NotPrepayment" : "Pending"),
                Note = l.FlagNote,
                RowStyle = decided ? "" : "background:#fffdf4",
                // carried so the UI/JS can post writes back against the real row id
                DeliveryLineId = l.Id
            };
        }

        private static PPMExistingPrepaymentPo ToExistingPo(PPMExistingPrepaymentPoEntity e)
        {
            return new PPMExistingPrepaymentPo
            {
                PoNumber = e.PoNumber,
                Vendor = e.Vendor,
                DeliveryGroup = e.DeliveryGroup,
                RecognisedAmount = FormatCurrency(e.RecognisedAmount),
                AmortisationStatus = StatusBadge(e.AmortisationStatus),
                OutstandingBalance = FormatCurrency(e.OutstandingBalance),
                ActionText = ActionForStatus(e.AmortisationStatus),
                ActionTarget = TargetForStatus(e.AmortisationStatus)
            };
        }

        // "Ready for export" rows open their journals (Tab 3); everything else opens the
        // amortisation schedule / setup (Tab 2). Mirrors ActionForStatus.
        private static string TargetForStatus(string status)
        {
            return string.Equals((status ?? "").Trim(), "Ready for export", StringComparison.OrdinalIgnoreCase)
                ? "journals" : "schedule";
        }

        private static PPMBadge StatusBadge(string status)
        {
            switch ((status ?? "").Trim())
            {
                case "Amortising": return new PPMBadge("Amortising", "s");
                case "Ready for export": return new PPMBadge("Ready for export", "b");
                case "Pending approval": return new PPMBadge("Pending approval", "a");
                default: return new PPMBadge(status, "");
            }
        }

        private static string ActionForStatus(string status)
        {
            switch ((status ?? "").Trim())
            {
                case "Amortising": return "View schedule";
                case "Ready for export": return "View journals";
                default: return "Open";
            }
        }

        private static string BuildScheduleSummary(PPMDeliveryScheduleHeader header, List<PPMDeliveryLine> lines)
        {
            if (header == null) return "";
            int classified = lines.Count(l => l.Decided);
            int awaiting = lines.Count - classified;
            decimal flagged = lines.Where(l => l.IsPrepayment)
                                   .Sum(l => ParseCurrency(l.LineValue));
            return string.Format(AuCulture,
                "{0} of {1} lines classified · {2:C0} flagged as prepayment · {3} lines awaiting decision",
                classified, lines.Count, flagged, awaiting);
        }

        // ── Formatting helpers ──────────────────────────────────────────────────
        private static string FormatCurrency(decimal? amount)
        {
            if (!amount.HasValue) return "";
            return amount.Value.ToString("C0", AuCulture); // "$120,000"
        }

        private static decimal ParseCurrency(string formatted)
        {
            decimal v;
            return decimal.TryParse(formatted, NumberStyles.Currency, AuCulture, out v) ? v : 0m;
        }

        private static string FormatMillions(decimal amount)
        {
            if (amount >= 1000000m)
                return "$" + (amount / 1000000m).ToString("0.0", AuCulture) + "m";
            if (amount >= 1000m)
                return "$" + (amount / 1000m).ToString("0.0", AuCulture) + "k";
            return amount.ToString("C0", AuCulture);
        }

        private static string FormatDate(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd MMM yyyy", AuCulture) : "";
        }
    }
}
