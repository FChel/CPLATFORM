using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    // ── View model returned by PPMPrepaymentReportService ───────────────────────────────

    public class PPMPrepaymentReportViewModel
    {
        public List<PPMKpi>                 Kpis            { get; set; }
        public List<PPMReportRow>           Rows            { get; set; }
        public PPMReportTotals              Totals          { get; set; }
        public List<PPMDrilldownScheduleRow> DrilldownSchedule { get; set; }
        public PPMDrilldownTotals           DrilldownTotalsRow { get; set; }
        public List<PPMLabelValue>          BalanceMovement { get; set; }

        // Filter option lists (data-driven dropdowns).
        public List<PPMReportGroupOption>   Groups   { get; set; }
        public List<PPMReportGlOption>      GlAccounts { get; set; }
        public List<PPMReportPeriodOption>  Periods  { get; set; }

        // Active filter state (echoed back so the dropdowns keep their selection).
        public string Period      { get; set; }
        public string PeriodLabel { get; set; }
        public long?  GroupId     { get; set; }
        public long?  GlId        { get; set; }
        public string Status      { get; set; }

        // Drill-down header.
        public string DrilldownTitle    { get; set; }
        public string DrilldownProgress { get; set; }   // "Month 8 of 12"
        public bool   HasDrilldown      { get; set; }
        public long?  SelectedGroupId   { get; set; }
    }

    // ── Service ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the Prepayment Report by Group (Tab 7 / §3.7) view model. READ-ONLY — §3.7 never
    /// writes back to other pages; this service only reads. Balances come from the amortisation
    /// schedules/periods (Tab 2), with reconciliation status (Tab 6) surfaced per group.
    /// </summary>
    public class PPMPrepaymentReportService
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private readonly IPPMPrepaymentReportRepository _repo;

        public PPMPrepaymentReportService() : this(new PPMPrepaymentReportRepository()) { }
        public PPMPrepaymentReportService(IPPMPrepaymentReportRepository repo) { _repo = repo; }

        public PPMPrepaymentReportViewModel Build(
            string period = null, long? groupId = null, long? glId = null,
            string status = null, long? selectedGroupId = null)
        {
            period = string.IsNullOrWhiteSpace(period) ? null : period.Trim();
            status = string.IsNullOrWhiteSpace(status) || status == "All" ? null : status.Trim();
            if (groupId.HasValue && groupId.Value <= 0) groupId = null;
            if (glId.HasValue && glId.Value <= 0)       glId = null;

            var kpis    = _repo.GetKpis(period, groupId, glId, status);
            var groups  = _repo.GetGroups().ToList();
            var gls     = _repo.GetGlAccounts().ToList();
            var periods = _repo.GetPeriods().ToList();

            // Resolve the active period: explicit → KPI default → newest available.
            var firstPeriod = periods.FirstOrDefault();
            string activePeriod = period ?? kpis.Period ?? (firstPeriod != null ? firstPeriod.PeriodKey : null);

            var gridRows = _repo.GetGrid(activePeriod, groupId, glId, status).ToList();

            // Drill-down: the clicked group, else the first row's group.
            var firstGridRow = gridRows.FirstOrDefault();
            long? drillGroup = selectedGroupId
                ?? (firstGridRow != null ? (long?)firstGridRow.DeliveryGroupId : null);

            var rows = gridRows.Select(r => MapRow(r, drillGroup)).ToList();

            var vm = new PPMPrepaymentReportViewModel
            {
                Kpis        = BuildKpis(kpis),
                Rows        = rows,
                Totals      = BuildTotals(gridRows),
                Groups      = groups,
                GlAccounts  = gls,
                Periods     = periods,
                Period      = activePeriod,
                PeriodLabel = PeriodLabelOrDefault(periods, activePeriod),
                GroupId     = groupId,
                GlId        = glId,
                Status      = status,
                SelectedGroupId = drillGroup,
                DrilldownSchedule = new List<PPMDrilldownScheduleRow>(),
                BalanceMovement   = new List<PPMLabelValue>(),
                DrilldownTotalsRow = new PPMDrilldownTotals
                {
                    ScheduleTotal = "$0", PercentAmortised = "0%",
                    AmortisedLabel = "$0 amortised", RemainingLabel = "$0 remaining",
                },
            };

            if (drillGroup.HasValue && drillGroup.Value > 0)
                BuildDrilldown(vm, drillGroup.Value, activePeriod, glId);

            return vm;
        }

        // ── KPI cards (§3.7) ───────────────────────────────────────────────────────────

        private static List<PPMKpi> BuildKpis(PPMReportKpis k)
        {
            return new List<PPMKpi>
            {
                new PPMKpi { Label = "Total recognised",  Value = FormatMoney(k.TotalRecognised),
                          Sub = "Recognised prepayment asset", ValueClass = "" },
                new PPMKpi { Label = "Total amortised",   Value = FormatMoney(k.TotalAmortised),
                          Sub = "Expensed to date", ValueClass = "green" },
                new PPMKpi { Label = "Outstanding balance", Value = FormatMoney(k.Outstanding),
                          Sub = "Net prepayment asset", ValueClass = "blue" },
                new PPMKpi { Label = "Groups with balance", Value = k.GroupsWithBalance.ToString(),
                          Sub = "Of " + k.TotalGroups + " active groups", ValueClass = "amber" },
                new PPMKpi { Label = "Completed this period", Value = k.CompletedThisPeriod.ToString(),
                          Sub = "Fully amortised", ValueClass = "" },
            };
        }

        // ── Grid row mapping ─────────────────────────────────────────────────────────

        private static PPMReportRow MapRow(PPMReportGridRow r, long? selectedGroupId)
        {
            bool selected = selectedGroupId.HasValue && r.DeliveryGroupId == selectedGroupId.Value;

            // §3.7 summary report table columns: Group, Group name, GL account, Vendor,
            // Recognised amount, Amortised to date, Outstanding balance, % amortised (progress
            // bar), Periods left, End date, Status.
            return new PPMReportRow
            {
                DeliveryGroupId = r.DeliveryGroupId,
                Group       = r.DeliveryGroupCode,
                GroupName   = r.GroupName,
                GlAccount   = r.GlAccount,
                Vendor      = r.Vendor ?? "—",
                CapexOpex   = r.CapexOpex,
                Recognised  = r.Recognised.ToString("C0", Au),
                Amortised   = r.Amortised.ToString("C0", Au),
                AmortisedStyle = r.Amortised > 0 ? "color:var(--success)" : "color:var(--faint)",
                Outstanding = r.Outstanding.ToString("C0", Au),
                OutstandingStyle = r.Outstanding > 0.01m ? "color:var(--blue);font-weight:700"
                                 : "color:var(--success);font-weight:700",
                PercentLabel = r.PercentAmortised.ToString("0.#", Au) + "%",
                PercentWidth = Clamp(r.PercentAmortised).ToString("0.#", Au) + "%",
                PeriodsLeft = r.PeriodsLeft > 0 ? r.PeriodsLeft.ToString() : "—",
                EndDate     = r.EndDate.HasValue ? r.EndDate.Value.ToString("MMM yyyy", Au) : "—",
                Status      = StatusBadge(r.Status),
                RowStyle    = selected ? "background:#eef4ff" : "",
            };
        }

        private static PPMBadge StatusBadge(string status)
        {
            switch (status)
            {
                case "Amortising": return new PPMBadge("Amortising",       "b");
                case "Completed":  return new PPMBadge("Completed",        "s");
                case "Pending":    return new PPMBadge("Pending approval", "w");
                case "Suspended":  return new PPMBadge("Suspended",        "a");
                case "Blocked":    return new PPMBadge("Blocked",          "e");
                default:           return new PPMBadge(status,             "");
            }
        }

        private static PPMReportTotals BuildTotals(List<PPMReportGridRow> rows)
        {
            return new PPMReportTotals
            {
                Recognised  = rows.Sum(r => r.Recognised).ToString("C0", Au),
                Amortised   = rows.Sum(r => r.Amortised).ToString("C0", Au),
                Outstanding = rows.Sum(r => r.Outstanding).ToString("C0", Au),
            };
        }

        // ── Drill-down (schedule + balance movement) ─────────────────────────────────

        private void BuildDrilldown(PPMPrepaymentReportViewModel vm, long groupId, string period, long? glId)
        {
            var drilldown = _repo.GetDrilldown(groupId, period, glId);
            var periods = drilldown.Periods;
            var movement = drilldown.Movement;
            var header = drilldown.Header;
            if (header == null || header.ScheduleId == null)
                return;   // group with no non-Draft schedule — leave the empty defaults

            vm.HasDrilldown   = true;
            vm.DrilldownTitle = header.DeliveryGroupCode + " " + header.GroupName;

            vm.DrilldownSchedule = periods.Select(p => new PPMDrilldownScheduleRow
            {
                Num        = p.PeriodNumber.ToString(),
                Period     = p.PeriodLabel,
                Amount     = p.Amount.ToString("C0", Au),
                Cumulative = p.Cumulative.ToString("C0", Au),
                Status     = ScheduleStatusBadge(p.Status),
                RowStyle   = p.IsCurrent ? "background:#fff7e6" : "",
            }).ToList();

            decimal scheduleTotal = periods.Sum(p => p.Amount);

            if (movement != null)
            {
                decimal recognised  = movement.Recognised;
                decimal amortised   = movement.AmortisedToDate;
                decimal outstanding = recognised - amortised;
                decimal pct = recognised > 0 ? Math.Round(100m * amortised / recognised, 1) : 0m;

                vm.DrilldownProgress = movement.PeriodsTotal > 0
                    ? "Month " + Math.Min(movement.PeriodsExported + 1, movement.PeriodsTotal)
                      + " of " + movement.PeriodsTotal
                    : "—";

                vm.BalanceMovement = new List<PPMLabelValue>
                {
                    new PPMLabelValue("Opening balance" + DateSuffix(movement.StartDate), recognised.ToString("C0", Au)),
                    new PPMLabelValue("Amortised to date", "–" + amortised.ToString("C0", Au), "color:var(--success)"),
                    new PPMLabelValue("Current period (" + (vm.PeriodLabel ?? period) + ")",
                                   movement.ThisPeriod > 0 ? "–" + movement.ThisPeriod.ToString("C0", Au) : "$0",
                                   movement.ThisPeriod > 0 ? "color:var(--warn)" : null),
                    new PPMLabelValue("Closing balance", outstanding.ToString("C0", Au), "color:var(--blue)"),
                    new PPMLabelValue("Periods remaining",
                                   (movement.PeriodsTotal - movement.PeriodsExported) + " period"
                                   + (movement.PeriodsTotal - movement.PeriodsExported == 1 ? "" : "s")),
                    new PPMLabelValue("% amortised", pct.ToString("0.#", Au) + "%"),
                };

                vm.DrilldownTotalsRow = new PPMDrilldownTotals
                {
                    ScheduleTotal    = scheduleTotal.ToString("C0", Au),
                    PercentAmortised = Clamp(pct).ToString("0.#", Au) + "%",
                    AmortisedLabel   = amortised.ToString("C0", Au) + " amortised",
                    RemainingLabel   = outstanding.ToString("C0", Au) + " remaining",
                };
            }
            else
            {
                vm.DrilldownTotalsRow.ScheduleTotal = scheduleTotal.ToString("C0", Au);
            }
        }

        private static PPMBadge ScheduleStatusBadge(string status)
        {
            switch (status)
            {
                case "Exported":      return new PPMBadge("Exported",        "s");
                case "Posted":        return new PPMBadge("Posted",          "s");
                case "PendingExport": return new PPMBadge("Pending export",  "w");
                case "Pending":       return new PPMBadge("Pending export",  "w");
                case "Planned":       return new PPMBadge("Planned",         "b");
                case "Cancelled":     return new PPMBadge("Cancelled",       "e");
                default:              return new PPMBadge(status,            "");
            }
        }

        // ── Formatting helpers ───────────────────────────────────────────────────────

        private static string FormatMoney(decimal amount)
        {
            if (Math.Abs(amount) >= 1000000m)
                return "$" + (amount / 1000000m).ToString("0.00", Au) + "m";
            if (Math.Abs(amount) >= 1000m)
                return "$" + (amount / 1000m).ToString("0.0", Au) + "k";
            return amount.ToString("C0", Au);
        }

        private static decimal Clamp(decimal pct)
        {
            return pct < 0 ? 0 : (pct > 100 ? 100 : pct);
        }

        private static string PeriodLabelOrDefault(List<PPMReportPeriodOption> periods, string activePeriod)
        {
            var match = periods.FirstOrDefault(p => p.PeriodKey == activePeriod);
            return match != null ? match.PeriodLabel : activePeriod;
        }

        private static string DateSuffix(DateTime? d)
        {
            return d.HasValue ? " (" + d.Value.ToString("MMM yyyy", Au) + ")" : "";
        }
    }
}
