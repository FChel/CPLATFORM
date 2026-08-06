using System;
using System.Web.UI;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// Prepayment Report by Group user control (Tab 7 / §3.7). Loads the KPIs, the per-group
    /// balance grid, the totals row, the data-driven filter dropdowns (delivery group, period,
    /// GL account, status) and the per-group drill-down (amortisation schedule + balance
    /// movement) from the DB via PPMPrepaymentReportService. READ-ONLY — §3.7 never writes back to
    /// other pages. Filters and the selected drill-down group are read from the query string on
    /// the AJAX render request.
    /// </summary>
    public partial class PPMPrepaymentReport : UserControl
    {
        private readonly PPMPrepaymentReportService _service = new PPMPrepaymentReportService();

        // Exposed to the markup binding expressions.
        protected string Period = "", PeriodLabel = "", StatusFilter = "";
        protected long?  GroupFilterId;
        protected long?  GlFilterId;

        // Totals row.
        protected int    RowCount;
        protected string TotalRecognised = "", TotalAmortised = "", TotalOutstanding = "";

        // Drill-down progress bar width.
        protected string ProgressWidth = "0%";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            string period = Request.QueryString["period"];
            string status = Request.QueryString["status"];
            long?  groupId = ParseLong(Request.QueryString["group"]);
            long?  glId    = ParseLong(Request.QueryString["gl"]);
            long?  drill   = ParseLong(Request.QueryString["drill"]);

            var vm = _service.Build(period, groupId, glId, status, drill);

            Period       = vm.Period ?? "";
            PeriodLabel  = vm.PeriodLabel ?? "";
            StatusFilter = vm.Status ?? "All";
            GroupFilterId = vm.GroupId;
            GlFilterId    = vm.GlId;

            rptKpis.DataSource         = vm.Kpis;
            rptReportRows.DataSource   = vm.Rows;
            rptGroupOptions.DataSource = vm.Groups;
            rptGlOptions.DataSource    = vm.GlAccounts;
            rptPeriodOptions.DataSource = vm.Periods;

            litPeriodLabel.Text = Server.HtmlEncode(vm.PeriodLabel ?? "—");

            phNoRows.Visible = vm.Rows.Count == 0;

            RowCount         = vm.Rows.Count;
            TotalRecognised  = vm.Totals.Recognised;
            TotalAmortised   = vm.Totals.Amortised;
            TotalOutstanding = vm.Totals.Outstanding;

            // Drill-down panels.
            if (vm.HasDrilldown)
            {
                phDrilldown.Visible          = true;
                rptDrilldownSchedule.DataSource = vm.DrilldownSchedule;
                rptBalanceMovement.DataSource   = vm.BalanceMovement;

                litDrillTitle.Text     = Server.HtmlEncode(vm.DrilldownTitle ?? "—");
                litDrillProgress.Text  = Server.HtmlEncode(vm.DrilldownProgress ?? "—");
                litScheduleTotal.Text  = Server.HtmlEncode(vm.DrilldownTotalsRow.ScheduleTotal ?? "—");
                litAmortisedLabel.Text = Server.HtmlEncode(vm.DrilldownTotalsRow.AmortisedLabel ?? "");
                litRemainingLabel.Text = Server.HtmlEncode(vm.DrilldownTotalsRow.RemainingLabel ?? "");
                ProgressWidth          = vm.DrilldownTotalsRow.PercentAmortised ?? "0%";
            }

            DataBind();
        }

        // ── Markup helpers (selection state of the filter dropdowns) ───────────────────

        protected string IsPeriod(object key)
        {
            return string.Equals(Period, Convert.ToString(key), StringComparison.OrdinalIgnoreCase) ? " selected" : "";
        }

        protected string IsStatus(string value)
        {
            return string.Equals(StatusFilter, value, StringComparison.OrdinalIgnoreCase) ? " selected" : "";
        }

        protected string IsGroup(object id)
        {
            return GroupFilterId.HasValue && Convert.ToString(id) == GroupFilterId.Value.ToString() ? " selected" : "";
        }

        protected string IsGl(object id)
        {
            return GlFilterId.HasValue && Convert.ToString(id) == GlFilterId.Value.ToString() ? " selected" : "";
        }

        private static long? ParseLong(string s)
        {
            long v;
            return long.TryParse(s, out v) && v > 0 ? (long?)v : null;
        }
    }
}
