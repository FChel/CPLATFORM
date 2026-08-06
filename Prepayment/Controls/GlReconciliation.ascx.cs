using System;
using System.Web.UI;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// GL Balance Reconciliation user control (Tab 6 / §3.6). Loads the KPIs, the per-group/GL
    /// reconciliation grid (SAP extract vs live FINHUB balances), the totals row and the variance
    /// investigation panels from the DB via PPMGlReconciliationService. Filters (period + variances
    /// only) and the selected row are read from the query string on the AJAX render request.
    /// </summary>
    public partial class PPMGlReconciliation : UserControl
    {
        private readonly PPMGlReconciliationService _service = new PPMGlReconciliationService();

        // Exposed to the markup binding expressions.
        protected string Period = "";
        protected bool   VariancesOnly;
        protected bool   DetailIsVariance;

        // Totals row.
        protected int    RowCount;
        protected string TotalSap = "", TotalFinhub = "", TotalVar = "", TotalVarStyle = "";
        protected string TotalBadgeCls = "", TotalBadgeText = "";

        // FINHUB detail badge.
        protected string FinhubBadgeCls = "", FinhubBadgeText = "";

        // Currently assigned investigator (for the "Assign to" dropdown selection).
        protected int AssignedToUserId;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            string period        = Request.QueryString["period"];
            bool   variancesOnly = Request.QueryString["variancesOnly"] == "1";
            long rid;
            long?  selectedRecon = long.TryParse(Request.QueryString["recon"], out rid) ? (long?)rid : null;

            var vm = _service.Build(period, variancesOnly, selectedRecon);

            Period        = vm.Period ?? "";
            VariancesOnly = vm.VariancesOnly;

            rptKpis.DataSource          = vm.Kpis;
            rptReconciliation.DataSource = vm.Rows;
            rptPeriodOptions.DataSource  = vm.Periods;

            litPeriodLabel.Text = Server.HtmlEncode(vm.PeriodLabel ?? "—");

            phNoRows.Visible    = vm.Rows.Count == 0;
            phBanner.Visible    = vm.HasExtract;
            phNoExtract.Visible = !vm.HasExtract;
            if (vm.HasExtract) litBanner.Text = Server.HtmlEncode(vm.LastLoadBanner);

            // Totals row.
            RowCount       = vm.Rows.Count;
            TotalSap       = vm.Totals.SapBalance;
            TotalFinhub    = vm.Totals.FinhubBalance;
            TotalVar       = vm.Totals.Variance;
            TotalVarStyle  = vm.Totals.VarianceStyle;
            TotalBadgeCls  = vm.Totals.VarianceBadge.Cls;
            TotalBadgeText = vm.Totals.VarianceBadge.Text;

            // Variance investigation panels.
            if (vm.DetailHeader != null)
            {
                phDetail.Visible = true;
                rptGlExtractDetail.DataSource = vm.GlExtractDetail;
                rptFinhubDetail.DataSource    = vm.FinhubDetail;
                rptAssignees.DataSource       = vm.Users;

                var h = vm.DetailHeader;
                AssignedToUserId    = h.AssignedToUserId ?? 0;
                DetailIsVariance = Math.Abs(h.Variance) > 0.01m && h.Status != "Reconciled";
                litDetailTitle.Text = Server.HtmlEncode(h.DeliveryGroupCode + " — " + h.GroupName);
                litGlTitle.Text     = Server.HtmlEncode(h.GlAccount + " — " + h.DeliveryGroupCode);
                litFinhubTitle.Text = Server.HtmlEncode(h.DeliveryGroupCode);
                litNote.Text        = Server.HtmlEncode(h.InvestigationNote ?? "");

                if (DetailIsVariance)
                {
                    FinhubBadgeCls  = "e";
                    FinhubBadgeText = FormatShort(h.Variance) + " variance";
                }
                else
                {
                    FinhubBadgeCls  = "s";
                    FinhubBadgeText = "Reconciled";
                }
            }

            DataBind();
        }

        protected string IsPeriod(object key)
        {
            return string.Equals(Period, Convert.ToString(key), StringComparison.OrdinalIgnoreCase) ? " selected" : "";
        }

        protected string IsAssignee(object id)
        {
            return AssignedToUserId > 0 && Convert.ToString(id) == AssignedToUserId.ToString() ? " selected" : "";
        }

        private static string FormatShort(decimal amount)
        {
            decimal a = Math.Abs(amount);
            string s = a >= 1000m ? "$" + (a / 1000m).ToString("0.#") + "k" : "$" + a.ToString("0");
            return s;
        }
    }
}
