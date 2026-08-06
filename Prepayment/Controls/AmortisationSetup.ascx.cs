using System;
using System.Globalization;
using System.Web.UI;
using Prepayment.Web.Models.Entities;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// Prepayment &amp; Amortisation Setup user control (the dashboard's second page, §3.2).
    /// Loads its own data from the service layer; Default.aspx just drops
    /// &lt;uc:AmortisationSetup /&gt; into the second pane.
    /// </summary>
    public partial class PPMAmortisationSetup : UserControl
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private readonly PPMAmortisationSetupService _service = new PPMAmortisationSetupService();

        // Exposed to the markup binding expressions.
        protected int NewInvoiceCount;
        protected int ExistingInvoiceCount;
        protected PPMInvoiceSetupDetail SelectedInvoice;
        protected string ScheduleTotal;
        protected string ScheduleSummaryLine;
        protected long InvoiceIdValue
        {
            get { return SelectedInvoice != null ? SelectedInvoice.InvoiceId : 0; }
        }

        // Setup-panel pre-fill helpers
        protected bool IsCurrent = true;
        protected bool IsCapital = false;
        protected bool IsOneOff = false;
        protected string StartDateValue;
        protected string EndDateValue;
        protected string PeriodsValue = "12";
        protected string ExpenseGlValue;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            long? selectedInvoiceId = null;
            long parsed;
            if (long.TryParse(Request.QueryString["invoice"], out parsed))
                selectedInvoiceId = parsed;

            // Opened from the Tab 1 "Existing Prepayment POs" grid: resolve the PO to its invoice.
            string openPo = Request.QueryString["openpo"];
            if (!selectedInvoiceId.HasValue && !string.IsNullOrWhiteSpace(openPo))
                selectedInvoiceId = _service.ResolveInvoiceByPo(openPo);

            var vm = _service.Build(selectedInvoiceId);

            NewInvoiceCount = vm.NewInvoiceCount;
            ExistingInvoiceCount = vm.ExistingInvoiceCount;
            SelectedInvoice = vm.SelectedInvoice;
            ScheduleTotal = vm.ScheduleTotal;

            if (SelectedInvoice != null)
            {
                phSetup.Visible = true;

                IsCurrent = !string.Equals(SelectedInvoice.AssetClassification, "Non-current", StringComparison.OrdinalIgnoreCase);
                IsCapital = string.Equals(SelectedInvoice.ExpenditureType, "Capital", StringComparison.OrdinalIgnoreCase);
                IsOneOff = string.Equals(SelectedInvoice.AmortisationType, "OneOff", StringComparison.OrdinalIgnoreCase);

                StartDateValue = SelectedInvoice.StartDate.HasValue
                    ? SelectedInvoice.StartDate.Value.ToString("yyyy-MM-dd")
                    : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd");
                EndDateValue = SelectedInvoice.EndDate.HasValue
                    ? SelectedInvoice.EndDate.Value.ToString("yyyy-MM-dd") : "";
                PeriodsValue = SelectedInvoice.Periods.HasValue ? SelectedInvoice.Periods.Value.ToString() : "12";
                // Expense GL: existing setup value, else auto-suggest from the original PO line GL.
                ExpenseGlValue = !string.IsNullOrEmpty(SelectedInvoice.ExpenseGlAccount)
                    ? SelectedInvoice.ExpenseGlAccount : SelectedInvoice.OriginalGl;

                int periods = SelectedInvoice.Periods ?? 12;
                decimal per = periods > 0 ? SelectedInvoice.Amount / periods : SelectedInvoice.Amount;
                ScheduleSummaryLine = string.Format(Au,
                    "{0} equal periods · {1:C2} per period · Total must equal {2:C0}",
                    periods, per, SelectedInvoice.Amount);

                rptGlOptions.DataSource = vm.GlOptions;
            }

            rptKpis.DataSource = vm.Kpis;
            rptNewInvoices.DataSource = vm.NewInvoices;
            rptExistingBalanceInvoices.DataSource = vm.ExistingBalanceInvoices;
            rptSchedule.DataSource = vm.SchedulePeriods;

            // Bind the always-present repeaters. We deliberately avoid a control-wide DataBind():
            // it would cascade into phSetup and evaluate its <%# SelectedInvoice... %> expressions
            // even when no invoice is selected (SelectedInvoice == null), throwing
            // NullReferenceException. The setup panel is only bound when an invoice is selected.
            rptKpis.DataBind();
            rptNewInvoices.DataBind();
            rptExistingBalanceInvoices.DataBind();

            if (SelectedInvoice != null)
            {
                phSetup.DataBind();   // binds SelectedInvoice scalars + rptGlOptions + rptSchedule
            }
        }

        /// <summary>Used by the GL dropdown to mark the currently-assigned account selected.</summary>
        protected bool IsSelectedGl(object glId)
        {
            return SelectedInvoice != null
                && SelectedInvoice.PrepaymentGlId.HasValue
                && Convert.ToInt64(glId) == SelectedInvoice.PrepaymentGlId.Value;
        }
    }
}
