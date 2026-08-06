using System;
using System.Globalization;
using System.Web;
using System.Web.UI;
using Prepayment.Web.Models.Dtos;
using Prepayment.Web.Models.Entities;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// PO Identification user control (the dashboard's first tab). Owns its own data load
    /// (from the service layer) so Default.aspx doesn't need to know its internals. The hosting
    /// page simply drops &lt;uc:PoIdentification /&gt; into the first pane.
    /// </summary>
    public partial class PPMPoIdentification : UserControl
    {
        private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
        private readonly PPMPoIdentificationService _service = new PPMPoIdentificationService();

        // Exposed to the markup binding expressions.
        protected string SearchPo, SearchVendor, SearchProject, SearchGroup;
        protected string ExistingVendorFilter;   // comma-separated vendors from query string
        protected int SearchResultCount;
        protected string LastLoadLabel = DateTime.Today.ToString("dd MMM yyyy", Au);
        protected PPMDeliveryScheduleHeader ScheduleHeader;
        protected string ScheduleTotalLabel;
        protected string ScheduleSummary;
        protected int ExistingActiveCount;
        protected string GroupOptionsHtml;   // <option> list for the delivery-group dropdown

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            // Read the search filters + selected PO from the query string (set by the JS search).
            var criteria = new PPMPoSearchCriteria
            {
                PoNumber = Request.QueryString["po"],
                VendorName = Request.QueryString["vendor"],
                ProjectCode = Request.QueryString["project"],
                DeliveryGroupCode = Request.QueryString["group"]
            };
            SearchPo = criteria.PoNumber;
            SearchVendor = criteria.VendorName;
            SearchProject = criteria.ProjectCode;
            SearchGroup = criteria.DeliveryGroupCode;
            ExistingVendorFilter = Request.QueryString["existingvendors"] ?? "";

            // The schedule defaults to the first PO needing action; it can also be opened by
            // PO number via query string (?openpo=3000077540), handled below.
            string openPo = Request.QueryString["openpo"];
            var vm = _service.Build(criteria, null, ExistingVendorFilter);

            // If an explicit PO number was requested, re-resolve the schedule to that PO.
            if (!string.IsNullOrWhiteSpace(openPo))
            {
                foreach (var r in vm.SearchResults)
                {
                    if (string.Equals(r.PoNumber, openPo, StringComparison.OrdinalIgnoreCase))
                    {
                        // Rebuild with the chosen PO selected.
                        var byNumber = new PPMPoSearchCriteria { PoNumber = openPo };
                        var single = _service.Build(byNumber, null);
                        vm.ScheduleHeader = single.ScheduleHeader;
                        vm.DeliveryLines = single.DeliveryLines;
                        vm.ScheduleSummary = single.ScheduleSummary;
                        break;
                    }
                }
            }

            SearchResultCount = vm.SearchResults.Count;
            ScheduleHeader = vm.ScheduleHeader;
            ScheduleSummary = vm.ScheduleSummary;
            ExistingActiveCount = vm.ExistingActiveCount;
            GroupOptionsHtml = BuildGroupOptions(SearchGroup);

            if (ScheduleHeader != null)
            {
                ScheduleTotalLabel = ScheduleHeader.TotalValue.ToString("C0", Au);
                phSchedule.Visible = true;
            }
            else
            {
                // No schedule resolved (e.g. a search / Group Workflow "View detail" that matched
                // no PO). The placeholder is hidden, but the control-wide DataBind() below still
                // recurses into it and evaluates its <%# ScheduleHeader.* %> expressions — so give
                // it a harmless empty header to bind against instead of null (which would throw).
                ScheduleHeader = PPMDeliveryScheduleHeader.Empty;
                ScheduleTotalLabel = "";
            }

            // (GroupOptionsHtml built above from the live group list)
            rptKpis.DataSource = vm.Kpis;
            rptSearchResults.DataSource = vm.SearchResults;
            rptDeliveryLines.DataSource = vm.DeliveryLines;
            rptExistingPos.DataSource = vm.ExistingPrepaymentPos;

            DataBind();
        }

        /// <summary>
        /// Builds the &lt;option&gt; list for the delivery-group dropdown from the live group list,
        /// pre-selecting the current search value. First option is the "all groups" blank.
        /// </summary>
        private string BuildGroupOptions(string selected)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<option value=\"\">All delivery groups</option>");
            foreach (var g in _service.GetDeliveryGroups())
            {
                string code = HttpUtility.HtmlEncode(g.Code);
                string name = HttpUtility.HtmlEncode(g.Name);
                bool isSel = string.Equals(g.Code, selected, StringComparison.OrdinalIgnoreCase);
                sb.Append("<option value=\"").Append(code).Append("\"")
                  .Append(isSel ? " selected" : "")
                  .Append(">").Append(code).Append(" — ").Append(name).Append("</option>");
            }
            return sb.ToString();
        }
    }
}
