using System;
using System.Linq;
using System.Web.UI;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// Group Workflow Control user control (Tab 5 / §3.5). Loads the KPI status buckets and the
    /// per-delivery-group summary (code, name, preparer, approver, #POs, #Invoices, #Journals,
    /// current stage, status) from the DB via PPMGroupWorkflowService. Every value — including the
    /// three filter dropdowns (Group name / Preparer / Status) — is derived live from the Tab 1/2/3
    /// transactional tables. Filters are read from the ?status= &amp; ?group= &amp; ?preparer= query
    /// string on the AJAX render request.
    /// </summary>
    public partial class PPMGroupWorkflowControl : UserControl
    {
        /// <summary>Rows shown in the grid before the "N more groups" footer.</summary>
        private const int VisibleRows = 7;

        private readonly PPMGroupWorkflowService _service = new PPMGroupWorkflowService();

        /// <summary>Active filter values, echoed into the markup so the dropdowns keep their state.</summary>
        protected string StatusFilter = "";
        protected string GroupNameFilter = "";
        protected string PreparerFilter = "";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            string status   = Request.QueryString["status"];
            string group    = Request.QueryString["group"];
            string preparer = Request.QueryString["preparer"];

            var vm = _service.Build(status, group, preparer);

            StatusFilter    = vm.StatusFilter ?? "";
            GroupNameFilter = vm.GroupNameFilter ?? "";
            PreparerFilter  = vm.PreparerFilter ?? "";

            // Data-driven dropdown options (only values present in live data).
            rptStatusOptions.DataSource    = vm.StatusOptions;
            rptGroupNameOptions.DataSource = vm.GroupNameOptions;
            rptPreparerOptions.DataSource  = vm.PreparerOptions;

            int total = vm.Footer.Total;
            var visible = vm.Rows.Take(VisibleRows).ToList();

            litGroupCount.Text     = total.ToString();
            litGroupCountHead.Text = total.ToString();

            rptKpis.DataSource          = vm.Kpis;
            rptGroupWorkflow.DataSource = visible;

            phNoRows.Visible = vm.Rows.Count == 0;

            int remaining = vm.Rows.Count - visible.Count;
            phFooter.Visible = remaining > 0;
            litRemaining.Text = remaining.ToString();
            litShown.Text     = visible.Count.ToString();
            litTotal.Text     = total.ToString();

            DataBind();
        }

        // Emit " selected" when an option's key matches the active filter (used inline in markup).
        protected string IsStatus(object key)
        {
            return Sel(StatusFilter, key);
        }
        protected string IsGroupName(object key)
        {
            return Sel(GroupNameFilter, key);
        }
        protected string IsPreparer(object key)
        {
            return Sel(PreparerFilter, key);
        }

        private static string Sel(string active, object key)
        {
            return string.Equals(active, Convert.ToString(key), StringComparison.OrdinalIgnoreCase) ? " selected" : "";
        }
    }
}
