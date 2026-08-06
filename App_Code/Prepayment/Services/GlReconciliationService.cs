using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    // ── View model returned by PPMGlReconciliationService ───────────────────────────────

    public class PPMGlReconciliationViewModel
    {
        public List<PPMKpi>                 Kpis          { get; set; }
        public List<PPMReconciliationRow>   Rows          { get; set; }
        public PPMReconciliationTotals      Totals        { get; set; }
        public List<PPMLabelValue>          GlExtractDetail { get; set; }
        public List<PPMLabelValue>          FinhubDetail  { get; set; }
        public PPMReconDetailHeader         DetailHeader  { get; set; }
        public List<PPMReconPeriodOption>   Periods       { get; set; }
        public List<PPMReconUser>           Users         { get; set; }
        public string  Period         { get; set; }
        public string  PeriodLabel    { get; set; }
        public bool    VariancesOnly  { get; set; }
        public bool    HasExtract     { get; set; }
        public string  LastLoadBanner { get; set; }
    }

    // ── Service ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the GL Balance Reconciliation (Tab 6 / §3.6) view model and drives its writes
    /// (upload &amp; reconcile, investigation outcomes). SAP balances come from the uploaded
    /// extract; the FINHUB side is derived live from Tab 2/3.
    /// </summary>
    public class PPMGlReconciliationService
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private readonly IPPMGlReconciliationRepository _repo;

        public PPMGlReconciliationService() : this(new PPMGlReconciliationRepository()) { }
        public PPMGlReconciliationService(IPPMGlReconciliationRepository repo) { _repo = repo; }

        public PPMGlReconciliationViewModel Build(string period = null, bool variancesOnly = false, long? selectedReconId = null)
        {
            period = string.IsNullOrWhiteSpace(period) ? null : period.Trim();

            var kpis    = _repo.GetKpis(period);
            var periods = _repo.GetPeriods().ToList();

            // Resolve the active period: explicit → KPI's latest → first available.
            var firstPeriod = periods.FirstOrDefault();
            string activePeriod = period ?? kpis.Period ?? (firstPeriod != null ? firstPeriod.PeriodKey : null);
            var rows = _repo.GetGrid(activePeriod, variancesOnly).Select(MapRow).ToList();

            // Variance detail: the selected row, else the first variance/not-matched row.
            var varianceRow = rows.FirstOrDefault(r => r.IsVariance);
            var firstRow = rows.FirstOrDefault();
            long? detailId = selectedReconId
                ?? (varianceRow != null ? (long?)varianceRow.ReconciliationId : null)
                ?? (firstRow != null ? (long?)firstRow.ReconciliationId : null);

            List<PPMLabelValue> glDetail = new List<PPMLabelValue>();
            List<PPMLabelValue> finDetail = new List<PPMLabelValue>();
            PPMReconDetailHeader header = null;
            if (detailId.HasValue && detailId.Value > 0)
            {
                var d = _repo.GetVarianceDetail(detailId.Value);
                header = d.Header;
                glDetail = BuildGlExtractDetail(d.Extract);
                finDetail = BuildFinhubDetail(d.Invoices, d.Finhub);
            }

            return new PPMGlReconciliationViewModel
            {
                Kpis            = BuildKpis(kpis),
                Rows            = rows,
                Totals          = BuildTotals(rows),
                GlExtractDetail = glDetail,
                FinhubDetail    = finDetail,
                DetailHeader    = header,
                Periods         = periods,
                Users           = _repo.GetUsers().ToList(),
                Period          = activePeriod,
                PeriodLabel     = PeriodLabelOrDefault(periods, activePeriod),
                VariancesOnly   = variancesOnly,
                HasExtract      = !string.IsNullOrEmpty(kpis.LastFileName),
                LastLoadBanner  = BuildLastLoadBanner(kpis),
            };
        }

        private static string PeriodLabelOrDefault(List<PPMReconPeriodOption> periods, string activePeriod)
        {
            var match = periods.FirstOrDefault(p => p.PeriodKey == activePeriod);
            return match != null ? match.PeriodLabel : activePeriod;
        }

        // ── Writes ───────────────────────────────────────────────────────────────────

        /// <summary>Persists a parsed extract and rebuilds the reconciliation for its period.</summary>
        public long SaveExtract(string fileName, string period, IList<PPMReconBalanceLine> lines, int userId)
        {
            if (string.IsNullOrWhiteSpace(period))
                throw new ArgumentException("A reporting period is required.");
            if (lines == null || lines.Count == 0)
                throw new ArgumentException("The file contained no balance rows.");

            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(lines);
            return _repo.SaveExtract(fileName, period, json, userId);
        }

        public int Resolve(long reconciliationId, string action, string note, int? assignedToUserId, int userId)
        {
            if (reconciliationId <= 0) throw new ArgumentException("A reconciliation row is required.");
            if (action != "MarkExplained" && action != "RaiseAdjustment")
                throw new ArgumentException("Unknown resolution action.");
            return _repo.Resolve(reconciliationId, action, note, assignedToUserId, userId);
        }

        // ── CSV parsing ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses the uploaded GL balance CSV (§3.6 columns; case-insensitive header):
        ///   Group, GL Account, Company Code, Opening Balance, Period Dr, Period Cr,
        ///   Closing Balance, Extract Date.
        /// Group, GL Account and Closing Balance are required; the rest are optional.
        /// </summary>
        public static IList<PPMReconBalanceLine> ParseCsv(string csv)
        {
            var lines = (csv ?? "").Replace("\r\n", "\n").Replace("\r", "\n")
                                   .Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count < 2) return new List<PPMReconBalanceLine>();

            var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant().Replace(" ", "")).ToList();

            int iGroup   = FindHeaderIndex(header, "groupcode", "group");
            int iGl      = FindHeaderIndex(header, "glaccount", "gl");
            int iCompany = FindHeaderIndex(header, "companycode", "company");
            int iOpen    = FindHeaderIndex(header, "openingbalance", "opening");
            int iDr      = FindHeaderIndex(header, "perioddebit", "perioddr", "dr");
            int iCr      = FindHeaderIndex(header, "periodcredit", "periodcr", "cr");
            int iClose   = FindHeaderIndex(header, "closingbalance", "closing", "balance");
            int iDate    = FindHeaderIndex(header, "extractdate", "date");

            if (iGroup < 0 || iGl < 0 || iClose < 0)
                throw new ArgumentException(
                    "CSV must have at least Group, GL Account and Closing Balance columns.");

            var result = new List<PPMReconBalanceLine>();
            for (int i = 1; i < lines.Count; i++)
            {
                var c = SplitCsv(lines[i]);
                if (c.Count <= iClose) continue;
                string group = Get(c, iGroup);
                string gl    = Get(c, iGl);
                if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(gl)) continue;

                result.Add(new PPMReconBalanceLine
                {
                    GroupCode      = group.Trim(),
                    GlAccount      = gl.Trim(),
                    CompanyCode    = Get(c, iCompany).Trim(),
                    OpeningBalance = Num(Get(c, iOpen)),
                    PeriodDebit    = Num(Get(c, iDr)),
                    PeriodCredit   = Num(Get(c, iCr)),
                    ClosingBalance = Num(Get(c, iClose)),
                    ExtractDate    = Get(c, iDate).Trim(),
                });
            }
            return result;
        }

        private static int FindHeaderIndex(List<string> header, params string[] names)
        {
            return header.FindIndex(h => names.Any(n => h == n.Replace(" ", "")));
        }

        /// <summary>A sample CSV for the "Download template" button (§3.6 columns).</summary>
        public static string CsvTemplate()
        {
            return "Group,GL Account,Company Code,Opening Balance,Period Dr,Period Cr,Closing Balance,Extract Date\r\n"
                 + "DIG,514008,1000,180000,0,0,180000,2026-06-30\r\n"
                 + "Air Force,514007,1000,198333,0,28333,170000,2026-06-30\r\n";
        }

        // ── KPI mapping ────────────────────────────────────────────────────────────────

        private static List<PPMKpi> BuildKpis(PPMReconKpis k)
        {
            string lastLoaded = k.LastLoadedDate.HasValue
                ? k.LastLoadedDate.Value.ToString("dd MMM yyyy", Au) : "—";

            return new List<PPMKpi>
            {
                new PPMKpi { Label = "Last file loaded",  Value = lastLoaded,
                          Sub = string.IsNullOrEmpty(k.LastFileName) ? "No extract loaded" : k.LastFileName,
                          ValueClass = "blue", ValueStyle = "font-size:16px" },
                new PPMKpi { Label = "Groups reconciled", Value = k.GroupsReconciled.ToString(),
                          Sub = "Of " + k.TotalGroups + " groups", ValueClass = "green" },
                new PPMKpi { Label = "Variances found",   Value = k.VariancesFound.ToString(),
                          Sub = "Require investigation", ValueClass = "",
                          ValueStyle = k.VariancesFound > 0 ? "color:var(--error)" : "" },
                new PPMKpi { Label = "Total GL balance",  Value = FormatMoney(k.TotalSapBalance),
                          Sub = "Per SAP extract", ValueClass = "" },
                new PPMKpi { Label = "FINHUB balance",    Value = FormatMoney(k.TotalFinhubBalance),
                          Sub = "Per prepayment records", ValueClass = "" },
            };
        }

        private static string BuildLastLoadBanner(PPMReconKpis k)
        {
            if (string.IsNullOrEmpty(k.LastFileName)) return null;
            string date = k.LastLoadedDate.HasValue ? k.LastLoadedDate.Value.ToString("dd MMM yyyy HH:mm", Au) : "";
            return string.Format("✅ Last load: {0} — {1} groups · {2} GL accounts · Loaded {3} by {4}",
                k.LastFileName, k.GroupCount, k.AccountCount, date, k.LastLoadedBy ?? "—");
        }

        // ── Grid row mapping ─────────────────────────────────────────────────────────

        private static PPMReconciliationRow MapRow(PPMReconGridRow r)
        {
            bool isVariance = r.Status == "Variance" || r.Status == "NotMatched";
            bool pending    = r.Status == "JournalPending";

            string varStyle = isVariance ? "color:var(--error);font-weight:700"
                            : pending      ? "color:var(--warn);font-weight:700"
                            : "color:var(--success);font-weight:700";

            string varText = FormatSignedMoney(r.Variance) + (pending ? "*" : "");

            return new PPMReconciliationRow
            {
                ReconciliationId = r.ReconciliationId,
                Group         = r.DeliveryGroupCode,
                GroupName     = r.GroupName,
                GlAccount     = r.GlAccount,
                GlDescription = r.GlDescription,
                SapBalance    = r.SapBalance.ToString("C2", Au),
                SapValue      = r.SapBalance,
                FinhubBalance = r.PrepaymentBalance.ToString("C2", Au),
                FinhubValue   = r.PrepaymentBalance,
                Variance      = varText,
                VarianceStyle = "text-align:right;font-variant-numeric:tabular-nums;" + varStyle,
                IsVariance    = isVariance,
                Status        = StatusBadge(r.Status),
                // §3.6 row action — "View variance detail" (red for variances, plain otherwise).
                ActionText    = "View variance detail",
                ActionStyle   = isVariance ? "background:var(--err-bg);color:var(--error);border-color:#f0b0b0" : "",
                RowStyle      = isVariance ? "background:#fdecea" : (pending ? "background:#fff7e6" : ""),
            };
        }

        private static PPMBadge StatusBadge(string status)
        {
            switch (status)
            {
                case "Reconciled":     return new PPMBadge("Reconciled",      "s");
                case "Variance":       return new PPMBadge("Variance",        "e");
                case "JournalPending": return new PPMBadge("Journal pending", "w");
                case "NotMatched":     return new PPMBadge("Not matched",     "e");
                default:               return new PPMBadge(status,            "");
            }
        }

        private static PPMReconciliationTotals BuildTotals(List<PPMReconciliationRow> rows)
        {
            decimal sap    = rows.Sum(r => r.SapValue);
            decimal finhub = rows.Sum(r => r.FinhubValue);
            decimal var    = sap - finhub;
            int variances  = rows.Count(r => r.IsVariance);

            return new PPMReconciliationTotals
            {
                SapBalance    = sap.ToString("C2", Au),
                FinhubBalance = finhub.ToString("C2", Au),
                Variance      = FormatSignedMoney(var),
                VarianceStyle = Math.Abs(var) > 0.01m ? "color:var(--error)" : "color:var(--success)",
                VarianceBadge = variances > 0
                    ? new PPMBadge(variances + " variance" + (variances == 1 ? "" : "s"), "e")
                    : new PPMBadge("All reconciled", "s"),
            };
        }

        // ── Variance detail panels ─────────────────────────────────────────────────────

        private static List<PPMLabelValue> BuildGlExtractDetail(PPMReconGlExtractDetail d)
        {
            if (d == null) return new List<PPMLabelValue>();
            return new List<PPMLabelValue>
            {
                new PPMLabelValue("Opening balance",      d.OpeningBalance.ToString("C2", Au)),
                new PPMLabelValue("Period postings (Dr)", d.PeriodDebit.ToString("C2", Au)),
                new PPMLabelValue("Period postings (Cr)", d.PeriodCredit.ToString("C2", Au)),
                new PPMLabelValue("Closing balance",      d.ClosingBalance.ToString("C2", Au)),
                new PPMLabelValue("Company code",         d.CompanyCode ?? "—"),
                new PPMLabelValue("Extract date",         d.ExtractDate.HasValue ? d.ExtractDate.Value.ToString("dd MMM yyyy", Au) : "—"),
            };
        }

        private static List<PPMLabelValue> BuildFinhubDetail(IReadOnlyList<PPMReconInvoiceRecognised> invoices, PPMReconFinhubDetail d)
        {
            if (d == null) return new List<PPMLabelValue>();
            bool variance = Math.Abs(d.Variance) > 0.01m;
            var list = new List<PPMLabelValue>();

            // §3.6: per-invoice recognised amounts, then the amortised total + balances.
            if (invoices != null && invoices.Count > 0)
                foreach (var inv in invoices)
                    list.Add(new PPMLabelValue("Recognised (" + inv.InvoiceNo + ")", inv.Recognised.ToString("C2", Au)));
            else
                list.Add(new PPMLabelValue("Recognised", d.Recognised.ToString("C2", Au)));

            list.Add(new PPMLabelValue("Amortised to date",  d.Amortised.ToString("C2", Au)));
            list.Add(new PPMLabelValue("FINHUB net balance", d.Outstanding.ToString("C2", Au), variance ? "color:var(--error)" : null));
            list.Add(new PPMLabelValue("SAP balance",        d.SapBalance.ToString("C2", Au)));
            list.Add(new PPMLabelValue("Variance",           FormatSignedMoney(d.Variance), variance ? "color:var(--error);font-weight:700" : null));
            return list;
        }

        // ── Formatting ────────────────────────────────────────────────────────────────

        private static string FormatMoney(decimal amount)
        {
            if (Math.Abs(amount) >= 1000000m)
                return "$" + (amount / 1000000m).ToString("0.00", Au) + "m";
            if (Math.Abs(amount) >= 1000m)
                return "$" + (amount / 1000m).ToString("0.0", Au) + "k";
            return amount.ToString("C0", Au);
        }

        private static string FormatSignedMoney(decimal amount)
        {
            return (amount < 0 ? "–" : "") + Math.Abs(amount).ToString("C2", Au);
        }

        // ── tiny CSV helpers ─────────────────────────────────────────────────────────

        private static string Get(List<string> cells, int i)
        {
            return (i >= 0 && i < cells.Count) ? cells[i] : "";
        }

        private static decimal Num(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace("$", "").Replace(",", "").Replace("(", "-").Replace(")", "").Trim();
            decimal v;
            return decimal.TryParse(s, NumberStyles.Any, Au, out v) ? v : 0m;
        }

        private static List<string> SplitCsv(string line)
        {
            var result = new List<string>();
            bool inQuotes = false; var cur = new System.Text.StringBuilder();
            foreach (char ch in line)
            {
                if (ch == '"') { inQuotes = !inQuotes; }
                else if (ch == ',' && !inQuotes) { result.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            result.Add(cur.ToString());
            return result;
        }
    }
}
