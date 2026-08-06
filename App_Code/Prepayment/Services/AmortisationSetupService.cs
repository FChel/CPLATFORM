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
    /// <summary>View-model bundle for the Amortisation Setup page.</summary>
    public class PPMAmortisationSetupViewModel
    {
        public List<PPMKpi> Kpis { get; set; }
        public List<PPMNewInvoice> NewInvoices { get; set; }
        public List<PPMExistingBalanceInvoice> ExistingBalanceInvoices { get; set; }
        public int NewInvoiceCount { get; set; }
        public int ExistingInvoiceCount { get; set; }

        // Selected-invoice setup panel
        public PPMInvoiceSetupDetail SelectedInvoice { get; set; }
        public List<PPMPrepaymentGlOption> GlOptions { get; set; }
        public List<PPMScheduleRow> SchedulePeriods { get; set; }
        public string ScheduleTotal { get; set; }
        public string SuggestedExpenseGl { get; set; }
    }

    /// <summary>
    /// Presentation layer for the Amortisation Setup page (§3.2): shapes invoice/schedule
    /// entities into the display view models the .ascx binds, and delegates writes.
    /// </summary>
    public class PPMAmortisationSetupService
    {
        private readonly IPPMAmortisationSetupRepository _repo;
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

        public PPMAmortisationSetupService() : this(new PPMAmortisationSetupRepository()) { }
        public PPMAmortisationSetupService(IPPMAmortisationSetupRepository repo) { _repo = repo; }

        public PPMAmortisationSetupViewModel Build(long? selectedInvoiceId)
        {
            var newInvoices = _repo.GetNewInvoices();
            var existing = _repo.GetExistingBalanceInvoices();

            long? invId = selectedInvoiceId ?? (newInvoices.Count > 0 ? (long?)newInvoices[0].InvoiceId : null);

            PPMInvoiceSetupDetail detail = null;
            var periods = new List<PPMScheduleRow>();
            string scheduleTotal = "";
            string suggestedExpense = "";
            if (invId.HasValue)
            {
                detail = _repo.GetInvoiceDetail(invId.Value);
                var p = _repo.GetScheduleForInvoice(invId.Value);
                periods = p.Select(ToScheduleRow).ToList();
                scheduleTotal = (detail != null ? detail.Amount : 0m).ToString("C2", Au);
                suggestedExpense = detail != null ? detail.OriginalGl : "";
            }

            return new PPMAmortisationSetupViewModel
            {
                Kpis = BuildKpis(_repo.GetKpis()),
                NewInvoices = newInvoices.Select(ToNewInvoice).ToList(),
                ExistingBalanceInvoices = existing.Select(ToExisting).ToList(),
                NewInvoiceCount = newInvoices.Count,
                ExistingInvoiceCount = existing.Count,
                SelectedInvoice = detail,
                GlOptions = _repo.GetPrepaymentGlAccounts().ToList(),
                SchedulePeriods = periods,
                ScheduleTotal = scheduleTotal,
                SuggestedExpenseGl = suggestedExpense
            };
        }

        // Resolves a PO number to the invoice its setup panel should open (Tab 1 "Open" action).
        public long? ResolveInvoiceByPo(string poNumber)
        {
            return _repo.ResolveInvoiceByPo(poNumber);
        }

        // ── Writes ──────────────────────────────────────────────────────────────
        public long SaveDraft(PPMAmortisationSetupRequest r, int userId)
        {
            return _repo.SaveDraft(r, userId);
        }
        public long GenerateScheduleAndJournals(PPMAmortisationSetupRequest r, int userId)
        {
            return _repo.GenerateScheduleAndJournals(r, userId);
        }
        public int SavePeriodAmounts(long invoiceId, string periodsJson, int userId)
        {
            return _repo.SavePeriodAmounts(invoiceId, periodsJson, userId);
        }

        // ── KPI mapping ──────────────────────────────────────────────────────────
        private static List<PPMKpi> BuildKpis(PPMAmortisationSetupKpis k)
        {
            return new List<PPMKpi>
        {
            new PPMKpi { Label = "New invoices to review",      Value = k.NewInvoicesToReview.ToString(),       Sub = "On prepayment-flagged PO lines",      ValueClass = "amber" },
            new PPMKpi { Label = "Existing balance invoices",   Value = k.ExistingBalanceInvoices.ToString(),   Sub = "Making up prepayment balance",        ValueClass = "blue" },
            new PPMKpi { Label = "Amortisation setups pending", Value = k.AmortisationSetupsPending.ToString(), Sub = "Lines need schedule input",           ValueClass = "amber" },
            new PPMKpi { Label = "Schedules active",            Value = k.SchedulesActive.ToString(),           Sub = "Amortising this period",              ValueClass = "green" },
            new PPMKpi { Label = "Total prepayment balance",    Value = FormatMillions(k.TotalPrepaymentBalance), Sub = "Net recognised, not yet amortised", ValueClass = "" },
        };
        }

        private static PPMNewInvoice ToNewInvoice(PPMNewInvoiceRow r)
        {
            PPMBadge flag = string.Equals(r.Flag, "Prepayment", StringComparison.OrdinalIgnoreCase)
                ? new PPMBadge("Prepayment", "s") : new PPMBadge("Under review", "w");
            PPMBadge setup; string action; bool primary = false; string rowStyle = "";
            switch (r.SetupStatus)
            {
                case "AmortisationNeeded": setup = new PPMBadge("Amortisation needed", "w"); action = "Set up"; primary = true; rowStyle = "background:#fffde7"; break;
                case "DraftInProgress": setup = new PPMBadge("Draft in progress", "a"); action = "Continue"; break;
                case "PendingClassification": setup = new PPMBadge("Pending classification", "a"); action = "Review"; break;
                case "Complete": setup = new PPMBadge("Complete", "s"); action = "View"; break;
                default: setup = new PPMBadge(r.SetupStatus, ""); action = "Open"; break;
            }
            return new PPMNewInvoice
            {
                InvoiceId = r.InvoiceId,
                InvoiceNo = r.InvoiceNo,
                PoLine = r.PoNumber + " / L" + r.LineNumber,
                Vendor = r.Vendor,
                GlAccount = r.GlAccount,
                CashGlAccount = r.CashGlAccount,
                CapexOpex = r.CapexOpex,
                InvoiceDate = FormatDate(r.InvoiceDate),
                Amount = FormatCurrency(r.Amount),
                ForeignAmount = FormatForeign(r.AmountDoc, r.ForeignCurrency, r.FxRate),
                Description = r.Description,
                Flag = flag,
                SetupStatus = setup,
                ActionText = action,
                ActionPrimary = primary,
                RowStyle = rowStyle
            };
        }

        private static PPMExistingBalanceInvoice ToExisting(PPMExistingBalanceInvoiceRow r)
        {
            int done = r.Periods.HasValue && r.Periods.Value > 0 && r.RecognisedAmount > 0
                ? (int)Math.Round(r.AmortisedToDate / (r.RecognisedAmount / r.Periods.Value)) : 0;
            PPMBadge status; string action; string target;
            switch (r.ScheduleStatus)
            {
                case "Active": status = new PPMBadge("Amortising · " + done + " of " + r.Periods, "s"); action = "View schedule"; target = "invoice"; break;
                case "Completed": status = new PPMBadge("Complete — export ready", "b"); action = "View journals"; target = "journals"; break;
                case "Draft": status = new PPMBadge("Pending approval", "a"); action = "Open"; target = "invoice"; break;
                default: status = new PPMBadge(r.ScheduleStatus ?? "—", ""); action = "Open"; target = "invoice"; break;
            }
            return new PPMExistingBalanceInvoice
            {
                InvoiceId = r.InvoiceId,
                InvoiceNo = r.InvoiceNo,
                PoNumber = r.PoNumber,
                PoLine = r.PoNumber + " / L" + r.LineNumber,
                Vendor = r.Vendor,
                GlAccount = r.GlAccount,
                CapexOpex = r.CapexOpex,
                InvoiceDate = FormatDate(r.InvoiceDate),
                Amount = FormatCurrency(r.Amount),
                RecognisedAmount = FormatCurrency(r.RecognisedAmount),
                AmortisationStatus = status,
                ActionText = action,
                ActionTarget = target
            };
        }

        private static PPMScheduleRow ToScheduleRow(PPMSchedulePeriodRow p)
        {
            return new PPMScheduleRow
        {
            PeriodId = p.PeriodId,
            Num = p.PeriodNumber.ToString(),
            Period = p.PeriodDate.HasValue ? p.PeriodDate.Value.ToString("MMM yyyy", Au) : "",
            Status = new PPMBadge(p.Status, p.Status == "Exported" || p.Status == "Posted" ? "s" : "b"),
            Amount = p.Amount.ToString("N2", Au),
        };
        }

        private static string FormatCurrency(decimal? a)
        {
            return a.HasValue ? a.Value.ToString("C0", Au) : "";
        }

        /// <summary>
        /// Foreign-amount label for FX invoices, e.g. "€12,345 @ 0.6647". Blank when the invoice
        /// is in AUD (no foreign currency / rate captured).
        /// </summary>
        private static string FormatForeign(decimal? amountDoc, string currency, decimal? fxRate)
        {
            if (!amountDoc.HasValue || string.IsNullOrEmpty(currency)) return "";
            string amt = amountDoc.Value.ToString("N0", Au);
            string rate = fxRate.HasValue ? " @ " + fxRate.Value.ToString("0.####", Au) : "";
            return currency + " " + amt + rate;
        }
        private static string FormatDate(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd MMM yyyy", Au) : "";
        }
        private static string FormatMillions(decimal a)
        {
            if (a >= 1000000m) return "$" + (a / 1000000m).ToString("0.00", Au) + "m";
            if (a >= 1000m) return "$" + (a / 1000m).ToString("0.0", Au) + "k";
            return a.ToString("C0", Au);
        }
    }
}
