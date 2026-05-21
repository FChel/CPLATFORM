using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Summary page. Admin-only operational view of the current review
    /// cycle — cycle picker, scope header, by-reason-code split (with
    /// Awaiting pseudo-row), non-payment subset, by-program / by-CM
    /// breakdowns, and the top-10 outstanding POCs.
    ///
    /// Read-only. No writes, no token paths. Every query honours the
    /// first-line-review model and the IsDeactivated = 0 filter via
    /// LPPIHelper helpers.
    ///
    /// The scope picker drives a postback that re-binds the whole page.
    /// The Export full data button is a separate handler call to
    /// LPPI_Summary_Export.ashx (admin-auth), which mirrors the 53-column
    /// reviewer-page export but scoped to whichever scope is selected.
    /// </summary>
    public partial class LPPI_Summary : LPPIBasePage
    {
        // Reviewed progress bar % surfaced into markup via <%= OvReviewedPct %>.
        protected int OvReviewedPct;

        // Scope query-string key. Survives postbacks via the dropdown's
        // SelectedValue, but the export handler reads it from the URL so
        // the picker state is bookmarkable / shareable.
        private const string ScopeQueryKey = "s";

        // Sentinel values for the dropdown. Batch options use "B<id>".
        private const string ScopeValueActive = "active";
        private const string ScopeValueAll    = "all";
        private const string ScopeValueBatchPrefix = "B";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindScopePicker();
                ApplyScopeFromQueryString();
                BindAll();
            }
        }

        // -------------------------------------------------------------------
        // Scope dropdown
        // -------------------------------------------------------------------

        private void BindScopePicker()
        {
            ddlScope.Items.Clear();

            // In-flight scopes group.
            ddlScope.Items.Add(new ListItem("Current cycle (in-flight packages)", ScopeValueActive));
            ddlScope.Items.Add(new ListItem("All active",                          ScopeValueAll));

            // Past load batches. The list is filtered to batches with at
            // least one document still attached to a package — older
            // batches whose docs have all dropped off no longer appear.
            DataTable batches = LPPIHelper.GetSummaryBatchList();
            foreach (DataRow r in batches.Rows)
            {
                int batchId   = Convert.ToInt32(r["BatchID"]);
                string file   = r["FileName"] == DBNull.Value ? "" : Convert.ToString(r["FileName"]);
                DateTime when = r["LoadedDate"] == DBNull.Value
                    ? DateTime.MinValue
                    : Convert.ToDateTime(r["LoadedDate"]);
                int docCount  = r["DocCount"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(r["DocCount"]);

                if (docCount == 0) continue;  // skip batches with no live, packaged docs

                string label = string.Format(CultureInfo.GetCultureInfo("en-AU"),
                    "Batch #{0} — {1} — {2}",
                    batchId,
                    string.IsNullOrEmpty(file) ? "(unnamed)" : file,
                    when == DateTime.MinValue ? "" : when.ToString("dd MMM yyyy"));

                ddlScope.Items.Add(new ListItem(label, ScopeValueBatchPrefix + batchId.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private void ApplyScopeFromQueryString()
        {
            string s = (Request.QueryString[ScopeQueryKey] ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return;

            ListItem li = ddlScope.Items.FindByValue(s);
            if (li != null)
            {
                ddlScope.ClearSelection();
                li.Selected = true;
            }
        }

        protected void ddlScope_SelectedIndexChanged(object sender, EventArgs e)
        {
            BindAll();
        }

        private LPPIHelper.SummaryScope CurrentScope()
        {
            string v = ddlScope.SelectedValue ?? ScopeValueActive;

            if (string.Equals(v, ScopeValueAll, StringComparison.OrdinalIgnoreCase))
                return LPPIHelper.SummaryScope.AllActive();

            if (v.StartsWith(ScopeValueBatchPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int batchId;
                if (int.TryParse(v.Substring(ScopeValueBatchPrefix.Length),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out batchId)
                    && batchId > 0)
                {
                    return LPPIHelper.SummaryScope.ForBatch(batchId);
                }
            }

            return LPPIHelper.SummaryScope.CurrentCycle();
        }

        // -------------------------------------------------------------------
        // Bind all sections for the currently-selected scope.
        // -------------------------------------------------------------------

        private void BindAll()
        {
            LPPIHelper.SummaryScope scope = CurrentScope();

            BindOverview(scope);
            BindByReason(scope);
            BindByNonPayment(scope);
            BindByProgram(scope);
            BindByCm(scope);
            BindByPoc(scope);
            BindScopeMeta(scope);
        }

        private void BindOverview(LPPIHelper.SummaryScope scope)
        {
            DataRow s = LPPIHelper.GetSummaryScopeHeader(scope);

            int pkgCount  = 0;
            int docCount  = 0;
            int reviewed  = 0;
            decimal total = 0m;

            if (s != null)
            {
                pkgCount  = s["PackageCount"] == DBNull.Value ? 0 : Convert.ToInt32(s["PackageCount"]);
                docCount  = s["DocCount"]     == DBNull.Value ? 0 : Convert.ToInt32(s["DocCount"]);
                reviewed  = s["ReviewedCount"] == DBNull.Value ? 0 : Convert.ToInt32(s["ReviewedCount"]);
                total     = s["TotalInterest"] == DBNull.Value ? 0m : Convert.ToDecimal(s["TotalInterest"]);
            }

            litOvPackages.Text  = pkgCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litOvDocs.Text      = docCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litOvDocs2.Text     = litOvDocs.Text;
            litOvReviewed.Text  = reviewed.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litOvInterest.Text  = total.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));

            OvReviewedPct = SharePct(reviewed, docCount);
        }

        private void BindByReason(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByReasonCode(scope);
            rptByReason.DataSource = dt;
            rptByReason.DataBind();
            phNoReason.Visible = dt.Rows.Count == 0;
        }

        private void BindByNonPayment(LPPIHelper.SummaryScope scope)
        {
            // Re-uses the by-reason-code result and filters in-memory to
            // NotPayable rows with DocCount > 0. Saves a round-trip and
            // guarantees the two views can not drift.
            DataTable src = LPPIHelper.GetSummaryByReasonCode(scope);
            DataTable np  = src.Clone();
            foreach (DataRow r in src.Rows)
            {
                string outcome = r["Outcome"] == DBNull.Value ? "" : Convert.ToString(r["Outcome"]);
                int    count   = r["DocCount"] == DBNull.Value ? 0  : Convert.ToInt32(r["DocCount"]);
                if (count > 0 && string.Equals(outcome, "NotPayable", StringComparison.OrdinalIgnoreCase))
                {
                    np.ImportRow(r);
                }
            }
            rptByNonPayment.DataSource = np;
            rptByNonPayment.DataBind();
            phNoNonPayment.Visible = np.Rows.Count == 0;
        }

        private void BindByProgram(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByProgram(scope);
            rptByProgram.DataSource = dt;
            rptByProgram.DataBind();
            phNoProgram.Visible = dt.Rows.Count == 0;
        }

        private void BindByCm(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByCm(scope);
            rptByCm.DataSource = dt;
            rptByCm.DataBind();
            phNoCm.Visible = dt.Rows.Count == 0;
        }

        private void BindByPoc(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByPocOutstanding(scope);
            rptByPoc.DataSource = dt;
            rptByPoc.DataBind();
            phNoPoc.Visible = dt.Rows.Count == 0;
        }

        private void BindScopeMeta(LPPIHelper.SummaryScope scope)
        {
            // Short descriptor sitting beside the dropdown.
            switch (scope.Kind)
            {
                case LPPIHelper.SummaryScopeKind.Batch:
                    litScopeMeta.Text = "Scoped to packages containing documents from this batch.";
                    break;
                case LPPIHelper.SummaryScopeKind.All:
                    litScopeMeta.Text = "All packages in NotSent / Sent / In review / Finalised — same set as Current cycle.";
                    break;
                case LPPIHelper.SummaryScopeKind.Active:
                default:
                    litScopeMeta.Text = "In-flight packages (NotSent / Sent / In review / Finalised). Exported and Cancelled packages drop off.";
                    break;
            }
        }

        // -------------------------------------------------------------------
        // Export — admin-auth handler call
        //
        // Builds a query-string with the current scope and redirects.
        // The handler does its own admin gate so this redirect is safe
        // even if a non-admin somehow reaches this page.
        // -------------------------------------------------------------------
        protected void btnExport_Click(object sender, EventArgs e)
        {
            string scopeValue = ddlScope.SelectedValue ?? ScopeValueActive;
            string url = "LPPI_Summary_Export.ashx?" + ScopeQueryKey + "=" + Server.UrlEncode(scopeValue);
            Response.Redirect(url, true);
        }

        // -------------------------------------------------------------------
        // Render helpers — called from the .aspx Eval()s
        // -------------------------------------------------------------------

        /// <summary>
        /// Class hook for the by-reason-code row so the toggle JS can find
        /// it. Also tags the Awaiting pseudo-row (DisplayOrder = -1) with
        /// a distinct class for CSS.
        /// </summary>
        protected string RowClassForReason(object dataItem)
        {
            DataRowView drv = dataItem as DataRowView;
            if (drv == null) return "";
            int order = drv.Row["DisplayOrder"] == DBNull.Value
                ? 0 : Convert.ToInt32(drv.Row["DisplayOrder"]);
            return order == -1 ? "summary-row-awaiting" : "";
        }

        protected string RenderOutcomePill(object outcomeObj)
        {
            if (outcomeObj == null || outcomeObj == DBNull.Value)
            {
                return "<span class=\"pill pill-awaiting\">Awaiting</span>";
            }
            string outcome = Convert.ToString(outcomeObj);
            if (string.Equals(outcome, "Payable", StringComparison.OrdinalIgnoreCase))
                return "<span class=\"pill pill-payable\">Payable</span>";
            return "<span class=\"pill pill-notpayable\">Not Payable</span>";
        }

        protected string RenderProgressBar(object reviewedObj, object totalObj)
        {
            int reviewed = reviewedObj == null || reviewedObj == DBNull.Value ? 0 : Convert.ToInt32(reviewedObj);
            int total    = totalObj    == null || totalObj    == DBNull.Value ? 0 : Convert.ToInt32(totalObj);
            int pct      = SharePct(reviewed, total);

            var sb = new StringBuilder();
            sb.Append("<div class=\"summary-progress\">");
            sb.Append("<div class=\"track\"><div class=\"fill\" style=\"width:").Append(pct).Append("%\"></div></div>");
            sb.Append("<div class=\"lbl\">").Append(reviewed).Append(" / ").Append(total).Append("</div>");
            sb.Append("</div>");
            return sb.ToString();
        }

        protected static string FormatInt(object val)
        {
            if (val == null || val == DBNull.Value) return "0";
            int n;
            if (int.TryParse(Convert.ToString(val), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out n))
            {
                return n.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            }
            return Convert.ToString(val);
        }

        protected static string FormatMoneyCell(object val)
        {
            if (val == null || val == DBNull.Value) return "$0.00";
            decimal d;
            if (decimal.TryParse(Convert.ToString(val), NumberStyles.Any,
                CultureInfo.InvariantCulture, out d))
            {
                return "$" + d.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));
            }
            return Convert.ToString(val);
        }

        protected static string FormatPctCell(object val)
        {
            if (val == null || val == DBNull.Value) return "0%";
            int n;
            if (int.TryParse(Convert.ToString(val), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out n))
            {
                if (n < 0) n = 0;
                if (n > 100) n = 100;
                return n.ToString(CultureInfo.InvariantCulture) + "%";
            }
            return Convert.ToString(val);
        }

        private static int SharePct(int part, int total)
        {
            if (total <= 0) return 0;
            int pct = (int)Math.Round(part * 100.0 / total);
            if (pct < 0) return 0;
            if (pct > 100) return 100;
            return pct;
        }
    }
}
