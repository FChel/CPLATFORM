using System;
using System.Web.UI;
using Prepayment.Web.Models.Entities;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// Journal Generation user control (the dashboard's third page, §3.3). Part A recognition
    /// + Part B amortisation journal queues, a drill-down detail/approval panel, and the
    /// submit/approve/reject/export workflow. Loads its own data from the service layer.
    /// </summary>
    public partial class PPMJournalGeneration : UserControl
    {
        private readonly PPMJournalService _service = new PPMJournalService();

        // Exposed to the markup binding expressions.
        protected PPMJournalDetailHeader SelectedHeader;
        protected string SelectedRef;
        protected string DetailTotal;
        protected string StatusBadgeCls;
        protected string StatusBadgeText;
        protected bool CanSubmit;
        protected bool CanApprove;
        protected bool CanExport;
        protected string RecVendorFilter;    // comma-separated vendors for Part A
        protected string AmortVendorFilter;  // comma-separated vendors for Part B

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            long? selectedJournalId = null;
            long parsed;
            if (long.TryParse(Request.QueryString["journal"], out parsed))
                selectedJournalId = parsed;

            // Opened from the Tab 1 "Existing Prepayment POs" grid: resolve the PO to its journal.
            string openPo = Request.QueryString["openpo"];
            if (!selectedJournalId.HasValue && !string.IsNullOrWhiteSpace(openPo))
                selectedJournalId = _service.ResolveJournalByPo(openPo);

            RecVendorFilter = Request.QueryString["recvendors"] ?? "";
            AmortVendorFilter = Request.QueryString["amortvendors"] ?? "";

            var vm = _service.Build(selectedJournalId, RecVendorFilter, AmortVendorFilter);

            SelectedHeader = vm.SelectedHeader;
            DetailTotal = vm.DetailTotal;

            if (SelectedHeader != null)
            {
                phRecognitionDetail.Visible = true;
                SelectedRef = SelectedHeader.JournalRef;
                SetStatusBadge(SelectedHeader.Status);

                // Workflow-button visibility follows the full doc workflow (§3.3 / §7 #8):
                // Draft -> Submit -> PendingApproval -> Approve/Reject -> Approved -> Export.
                CanSubmit = SelectedHeader.Status == "Draft";
                CanApprove = SelectedHeader.Status == "PendingApproval";
                CanExport = SelectedHeader.Status == "Approved";

                rptPoSource.DataSource = vm.PoSourceFields;
                rptApproval.DataSource = vm.ApprovalAuditFields;
                rptDetailEntries.DataSource = vm.DetailEntries;
            }

            phEmptyAmort.Visible = vm.AmortisationJournals.Count == 0;

            rptKpis.DataSource = vm.Kpis;
            rptRecognitionJournals.DataSource = vm.RecognitionJournals;
            rptAmortisationJournals.DataSource = vm.AmortisationJournals;

            // Bind the always-present repeaters individually. We avoid a control-wide DataBind():
            // it would cascade into phRecognitionDetail and evaluate its <%# SelectedHeader... %>
            // expressions even when no journal is selected, throwing NullReferenceException.
            rptKpis.DataBind();
            rptRecognitionJournals.DataBind();
            rptAmortisationJournals.DataBind();

            if (SelectedHeader != null)
            {
                phRecognitionDetail.DataBind();   // binds SelectedHeader scalars + detail repeaters
            }
        }

        private void SetStatusBadge(string status)
        {
            switch (status)
            {
                case "PendingApproval": StatusBadgeCls = "w"; StatusBadgeText = "Awaiting approval"; break;
                case "Approved": StatusBadgeCls = "s"; StatusBadgeText = "Approved"; break;
                case "Rejected": StatusBadgeCls = "e"; StatusBadgeText = "Rejected"; break;
                case "Exported": StatusBadgeCls = "b"; StatusBadgeText = "Exported"; break;
                default: StatusBadgeCls = "a"; StatusBadgeText = status; break;
            }
        }
    }
}
