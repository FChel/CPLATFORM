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
    /// cycle — Scope picker, CM picker, scope header, reason-code split
    /// (Payable + NotPayable), by-program breakdown (with totals row),
    /// and the top-10 outstanding POCs by both count and value.
    ///
    /// Read-only. No writes, no token paths. Every query honours the
    /// first-line-review model and the IsDeactivated = 0 filter via
    /// LPPIHelper helpers.
    ///
    /// Two independent pickers:
    ///   - Scope: which packages are in the universe (active / all / batch).
    ///   - Capability Manager: narrow within that universe to one program.
    /// Both are AutoPostBack; both contribute to the bookmarkable
    /// query string (s=, cm=). The CM picker re-binds when Scope changes
    /// so it only ever shows programs actually represented in the scope;
    /// the user's selection is preserved across the rebind if still valid,
    /// otherwise falls back to (all).
    ///
    /// Two export buttons:
    ///   - Export full data — every line of every in-scope document.
    ///   - Export no-POC lines — only lines whose PocEmail is missing.
    /// Both call LPPI_Summary_Export.ashx (admin-auth); the no-POC variant
    /// adds &noPoc=1 to the query string.
    /// </summary>
    public partial class LPPI_Summary : LPPIBasePage
    {
        // Reviewed progress bar % surfaced into markup via <%= OvReviewedPct %>.
        protected int OvReviewedPct;

        // Program-table totals — surfaced into the tfoot via <%= ... %>
        // for the progress bar's two arguments. The numeric literals are
        // bound through their own asp:Literal controls (litProgTotXxx).
        protected int ProgTotReviewed;
        protected int ProgTotDocs;

        // Query-string keys. The pickers AutoPostBack and live via
        // SelectedValue, but the export handler reads from the URL so the
        // picker state is bookmarkable and shareable.
        private const string ScopeQueryKey = "s";
        private const string CmQueryKey    = "cm";
        private const string NoPocQueryKey = "noPoc";

        // Scope dropdown sentinel values. Batch options use "B<id>".
        private const string ScopeValueActive      = "active";
        private const string ScopeValueAll         = "all";
        private const string ScopeValueBatchPrefix = "B";

        // CM dropdown sentinel value for the (all programs) option.
        private const string CmValueAll = "";

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindScopePicker();
                ApplyScopeFromQueryString();
                BindCmPicker(ScopeWithoutCm());
                ApplyCmFromQueryString();
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
            ddlScope.Items.Add(new ListItem("All cycles (cumulative)",             ScopeValueAll));

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
            // Re-bind the CM picker against the new scope. Preserve the
            // current CM selection if it is still represented in the new
            // scope, otherwise fall back to (all).
            string previousCm = ddlCm.SelectedValue ?? CmValueAll;
            BindCmPicker(ScopeWithoutCm());
            ListItem keep = ddlCm.Items.FindByValue(previousCm);
            if (keep != null)
            {
                ddlCm.ClearSelection();
                keep.Selected = true;
            }
            // else default-selected (all) from BindCmPicker.

            BindAll();
        }

        // -------------------------------------------------------------------
        // CM dropdown
        //
        // Always starts with "(All programs)" as a sentinel for no CM
        // filter. Subsequent items are the CmIDs available within the
        // CURRENT Scope — passing pickerScope through GetSummaryCmList
        // ensures the dropdown collapses to just programs you can actually
        // reach with the current Scope selection.
        // -------------------------------------------------------------------

        private void BindCmPicker(LPPIHelper.SummaryScope pickerScope)
        {
            ddlCm.Items.Clear();
            ddlCm.Items.Add(new ListItem("(All programs)", CmValueAll));

            DataTable cms = LPPIHelper.GetSummaryCmList(pickerScope);
            foreach (DataRow r in cms.Rows)
            {
                int cmId      = Convert.ToInt32(r["CmID"]);
                string program = r["Program"] == DBNull.Value ? "" : Convert.ToString(r["Program"]);
                ddlCm.Items.Add(new ListItem(
                    string.IsNullOrEmpty(program) ? "(unnamed program)" : program,
                    cmId.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private void ApplyCmFromQueryString()
        {
            string cm = (Request.QueryString[CmQueryKey] ?? "").Trim();
            if (string.IsNullOrEmpty(cm)) return;

            ListItem li = ddlCm.Items.FindByValue(cm);
            if (li != null)
            {
                ddlCm.ClearSelection();
                li.Selected = true;
            }
        }

        protected void ddlCm_SelectedIndexChanged(object sender, EventArgs e)
        {
            BindAll();
        }

        // -------------------------------------------------------------------
        // Scope resolution
        // -------------------------------------------------------------------

        /// <summary>
        /// Scope with Scope-kind only, no CM filter. Used to seed the CM
        /// picker so its option list does not constrain itself.
        /// </summary>
        private LPPIHelper.SummaryScope ScopeWithoutCm()
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

        private LPPIHelper.SummaryScope CurrentScope()
        {
            LPPIHelper.SummaryScope scope = ScopeWithoutCm();

            string cmVal = ddlCm.SelectedValue ?? CmValueAll;
            int cmId;
            if (!string.IsNullOrEmpty(cmVal)
                && int.TryParse(cmVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out cmId)
                && cmId > 0)
            {
                scope.WithCm(cmId);
            }

            return scope;
        }

        // -------------------------------------------------------------------
        // Bind all sections for the currently-selected scope + CM filter.
        // -------------------------------------------------------------------

        private void BindAll()
        {
            LPPIHelper.SummaryScope scope = CurrentScope();

            // The reason-code source is shared between BindByPayable and
            // BindByNonPayment — one round trip, two filtered views.
            DataTable byReason = LPPIHelper.GetSummaryByReasonCode(scope);

            BindOverview(scope);
            BindByPayable(byReason);
            BindByNonPayment(byReason);
            BindByProgram(scope);
            BindByPoc(scope);
            BindByPocByValue(scope);
            BindScopeMeta(scope);
        }

        private void BindOverview(LPPIHelper.SummaryScope scope)
        {
            DataRow s = LPPIHelper.GetSummaryScopeHeader(scope);

            int pkgCount  = 0;
            int docCount  = 0;
            int reviewed  = 0;
            decimal total      = 0m;
            decimal payable    = 0m;
            decimal notPayable = 0m;

            if (s != null)
            {
                pkgCount   = s["PackageCount"]       == DBNull.Value ? 0  : Convert.ToInt32(s["PackageCount"]);
                docCount   = s["DocCount"]           == DBNull.Value ? 0  : Convert.ToInt32(s["DocCount"]);
                reviewed   = s["ReviewedCount"]      == DBNull.Value ? 0  : Convert.ToInt32(s["ReviewedCount"]);
                total      = s["TotalInterest"]      == DBNull.Value ? 0m : Convert.ToDecimal(s["TotalInterest"]);
                payable    = s["PayableInterest"]    == DBNull.Value ? 0m : Convert.ToDecimal(s["PayableInterest"]);
                notPayable = s["NotPayableInterest"] == DBNull.Value ? 0m : Convert.ToDecimal(s["NotPayableInterest"]);
            }

            CultureInfo au = CultureInfo.GetCultureInfo("en-AU");

            litOvPackages.Text   = pkgCount.ToString("N0", au);
            litOvDocs2.Text      = docCount.ToString("N0", au);
            litOvReviewed.Text   = reviewed.ToString("N0", au);
            litOvInterest.Text   = total.ToString("N2", au);
            litOvPayable.Text    = payable.ToString("N2", au);
            litOvNotPayable.Text = notPayable.ToString("N2", au);

            OvReviewedPct = SharePct(reviewed, docCount);
        }

        /// <summary>
        /// Bind the Payable reasons table. Filters the shared by-reason
        /// result to rows with Outcome = "Payable" and DocCount > 0.
        /// </summary>
        private void BindByPayable(DataTable src)
        {
            DataTable t = src.Clone();
            foreach (DataRow r in src.Rows)
            {
                string outcome = r["Outcome"] == DBNull.Value ? "" : Convert.ToString(r["Outcome"]);
                int    count   = r["DocCount"] == DBNull.Value ? 0  : Convert.ToInt32(r["DocCount"]);
                if (count > 0 && string.Equals(outcome, "Payable", StringComparison.OrdinalIgnoreCase))
                {
                    t.ImportRow(r);
                }
            }
            rptByPayable.DataSource = t;
            rptByPayable.DataBind();
            phNoPayable.Visible = t.Rows.Count == 0;
        }

        /// <summary>
        /// Bind the Non-payment reasons table. Filters the shared by-reason
        /// result to rows with Outcome = "NotPayable" and DocCount > 0.
        /// </summary>
        private void BindByNonPayment(DataTable src)
        {
            DataTable t = src.Clone();
            foreach (DataRow r in src.Rows)
            {
                string outcome = r["Outcome"] == DBNull.Value ? "" : Convert.ToString(r["Outcome"]);
                int    count   = r["DocCount"] == DBNull.Value ? 0  : Convert.ToInt32(r["DocCount"]);
                if (count > 0 && string.Equals(outcome, "NotPayable", StringComparison.OrdinalIgnoreCase))
                {
                    t.ImportRow(r);
                }
            }
            rptByNonPayment.DataSource = t;
            rptByNonPayment.DataBind();
            phNoNonPayment.Visible = t.Rows.Count == 0;
        }

        private void BindByProgram(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByProgram(scope);

            rptByProgram.DataSource = dt;
            rptByProgram.DataBind();

            bool hasRows = dt.Rows.Count > 0;
            phProgramTable.Visible = hasRows;
            phNoProgram.Visible    = !hasRows;

            // Compute the totals row. Documents, POCs and Interest are
            // per-program rollups: a document, POC or dollar that spans more
            // than one program is counted under each, so each Total can
            // exceed the distinct figure on the Cycle overview cards. The
            // standing footnote under the table (marked *) states this and
            // shows regardless of the data.
            //
            // Two conditional captions sit beneath that standing note, each
            // shown only when its count is non-zero:
            //   - flagged-for-reload: in-scope docs coded RC-RL (live, not
            //     yet finalised) plus the system-wide count of documents
            //     already deactivated and awaiting a corrected reload;
            //   - no-POC: in-scope first-line docs with no POC email.
            int totPackages = 0, totDocs = 0, totReviewed = 0, totPocs = 0, totNoPoc = 0, totReload = 0;
            decimal totInterest = 0m;

            foreach (DataRow r in dt.Rows)
            {
                totPackages += AsInt(r, "PackageCount");
                totDocs     += AsInt(r, "DocCount");
                totReviewed += AsInt(r, "ReviewedCount");
                totPocs     += AsInt(r, "PocCount");
                totNoPoc    += AsInt(r, "NoPocCount");
                totReload   += AsInt(r, "FlaggedReloadCount");
                totInterest += AsDec(r, "Interest");
            }

            litProgTotPackages.Text = totPackages.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litProgTotDocs.Text     = totDocs.ToString("N0",     CultureInfo.GetCultureInfo("en-AU"));
            litProgTotPocs.Text     = totPocs.ToString("N0",     CultureInfo.GetCultureInfo("en-AU"));
            litProgTotReload.Text   = totReload.ToString("N0",   CultureInfo.GetCultureInfo("en-AU"));
            litProgTotInterest.Text = "$" + totInterest.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));

            ProgTotReviewed = totReviewed;
            ProgTotDocs     = totDocs;

            // Toggle the no-POC export button — disabled when there are no
            // no-POC lines to export, so admins do not download an empty
            // file. Visible always so the action is discoverable.
            btnExportNoPoc.Enabled = totNoPoc > 0;

            // Flagged-for-reload caption. Combines the in-scope RC-RL docs
            // (flagged, still live) with the system-wide deactivated-and-
            // awaiting-reload backlog, which is not cycle-scoped.
            var au = CultureInfo.GetCultureInfo("en-AU");
            int deactivatedAwaitingReload = LPPIHelper.GetDeactivatedAwaitingReloadCount();
            if (totReload > 0 || deactivatedAwaitingReload > 0)
            {
                string text = "";
                if (totReload > 0)
                    text += string.Format(au,
                        "<b>{0}</b> document{1} in scope {2} flagged for reload (RC-RL) and will deactivate when the package is finalised.",
                        totReload.ToString("N0", au),
                        totReload == 1 ? "" : "s",
                        totReload == 1 ? "is" : "are");
                if (deactivatedAwaitingReload > 0)
                    text += string.Format(au,
                        " Across all cycles, <b>{0}</b> document{1} {2} deactivated and awaiting a corrected reload.",
                        deactivatedAwaitingReload.ToString("N0", au),
                        deactivatedAwaitingReload == 1 ? "" : "s",
                        deactivatedAwaitingReload == 1 ? "is" : "are");
                litReloadNote.Text = text.Trim();
                phReloadNote.Visible = true;
            }
            else
            {
                phReloadNote.Visible = false;
            }

            if (totNoPoc > 0)
            {
                phNoPocNote.Visible = true;
                litNoPocCount.Text = string.Format(au,
                    "<b>{0}</b> document{1} in scope ha{2} no POC email recorded; use the <em>Export no-POC lines</em> button above to pull the underlying lines.",
                    totNoPoc.ToString("N0", au),
                    totNoPoc == 1 ? "" : "s",
                    totNoPoc == 1 ? "s" : "ve");
            }
            else
            {
                phNoPocNote.Visible = false;
            }
        }

        private void BindByPoc(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByPocOutstanding(scope);
            rptByPoc.DataSource = dt;
            rptByPoc.DataBind();
            phNoPoc.Visible = dt.Rows.Count == 0;
        }

        private void BindByPocByValue(LPPIHelper.SummaryScope scope)
        {
            DataTable dt = LPPIHelper.GetSummaryByPocOutstandingByValue(scope);
            rptByPocValue.DataSource = dt;
            rptByPocValue.DataBind();
            phNoPocValue.Visible = dt.Rows.Count == 0;
        }

        private void BindScopeMeta(LPPIHelper.SummaryScope scope)
        {
            // Short descriptor sitting beside the dropdowns. Mentions CM
            // when a filter is applied so the operator sees at a glance
            // that the page is narrowed.
            string scopeText;
            switch (scope.Kind)
            {
                case LPPIHelper.SummaryScopeKind.Batch:
                    scopeText = "Scoped to packages containing documents from this batch.";
                    break;
                case LPPIHelper.SummaryScopeKind.All:
                    scopeText = "All cycles (cumulative) — every package including Exported. Cancelled is excluded.";
                    break;
                case LPPIHelper.SummaryScopeKind.Active:
                default:
                    scopeText = "In-flight packages (NotSent / Sent / In review / Finalised). Exported and Cancelled packages drop off.";
                    break;
            }

            if (scope.CmID.HasValue)
            {
                ListItem cmItem = ddlCm.Items.FindByValue(scope.CmID.Value.ToString(CultureInfo.InvariantCulture));
                string cmLabel = cmItem != null ? cmItem.Text : ("CM #" + scope.CmID.Value);
                scopeText += " Filtered to " + cmLabel + ".";
            }

            litScopeMeta.Text = LPPIHelper.Enc(scopeText);
        }

        // -------------------------------------------------------------------
        // Export — admin-auth handler call
        //
        // Builds a query-string with the current scope + CM filter and
        // redirects. The handler does its own admin gate so this redirect
        // is safe even if a non-admin somehow reaches this page.
        // -------------------------------------------------------------------
        protected void btnExport_Click(object sender, EventArgs e)
        {
            Response.Redirect(BuildExportUrl(noPocOnly: false), true);
        }

        protected void btnExportNoPoc_Click(object sender, EventArgs e)
        {
            Response.Redirect(BuildExportUrl(noPocOnly: true), true);
        }

        private string BuildExportUrl(bool noPocOnly)
        {
            string scopeValue = ddlScope.SelectedValue ?? ScopeValueActive;
            string cmValue    = ddlCm.SelectedValue    ?? CmValueAll;

            var sb = new StringBuilder("LPPI_Summary_Export.ashx?");
            sb.Append(ScopeQueryKey).Append('=').Append(Server.UrlEncode(scopeValue));
            if (!string.IsNullOrEmpty(cmValue))
            {
                sb.Append('&').Append(CmQueryKey).Append('=').Append(Server.UrlEncode(cmValue));
            }
            if (noPocOnly)
            {
                sb.Append('&').Append(NoPocQueryKey).Append("=1");
            }
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Render helpers — called from the .aspx Eval()s
        // -------------------------------------------------------------------

        protected string RenderProgressBar(object reviewedObj, object totalObj)
        {
            int reviewed = reviewedObj == null || reviewedObj == DBNull.Value
                ? 0 : Convert.ToInt32(reviewedObj);
            int total    = totalObj    == null || totalObj    == DBNull.Value
                ? 0 : Convert.ToInt32(totalObj);
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

        protected string FormatReloadCell(object v)
        {
            int n = (v == null || v == DBNull.Value) ? 0 : Convert.ToInt32(v);
            if (n == 0)
                return "<span class=\"summary-muted-zero\">&ndash;</span>";
            return "<span class=\"summary-reload-flag\">"
                 + n.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + "</span>";
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

        // -------------------------------------------------------------------
        // Tiny DataRow conversion helpers — used by BindByProgram's totals
        // loop. Tolerant of missing columns / DBNulls so a missing-column
        // bug shows up as a zero rather than a NullReferenceException.
        // -------------------------------------------------------------------

        private static int AsInt(DataRow r, string col)
        {
            if (r == null || !r.Table.Columns.Contains(col)) return 0;
            object v = r[col];
            if (v == null || v == DBNull.Value) return 0;
            return Convert.ToInt32(v);
        }

        private static decimal AsDec(DataRow r, string col)
        {
            if (r == null || !r.Table.Columns.Contains(col)) return 0m;
            object v = r[col];
            if (v == null || v == DBNull.Value) return 0m;
            return Convert.ToDecimal(v);
        }
    }
}
