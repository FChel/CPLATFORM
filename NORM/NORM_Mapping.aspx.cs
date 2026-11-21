using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    public partial class NORM_Mapping : NORMBasePage
    {
        protected string ReleaseSummaryHtml = "";
        protected string ValidationHtml = "";
        protected string ValidationStatus = "Not run";
        protected string ImpactHtml = "";
        protected string AuditHtml = "";
        protected string UnmappedAccountCount = "—";
        protected string UnmappedAccountLabel = "unmapped accounts";

        private int CurrentReleaseId
        {
            get { return ViewState["MappingReleaseId"] == null ? 0 : Convert.ToInt32(ViewState["MappingReleaseId"]); }
            set { ViewState["MappingReleaseId"] = value; }
        }

        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            ViewStateUserKey = Session.SessionID + "|" + NORMHelper.CurrentUserId();
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                try
                {
                    BindMappingContexts();
                    int requestedRelease;
                    if (Int32.TryParse(Request.QueryString["release"], out requestedRelease) && requestedRelease > 0)
                        CurrentReleaseId = requestedRelease;
                    else if (MappingContextList.Items.Count == 2)
                    {
                        MappingContextList.SelectedIndex = 1;
                        SelectMappingContext();
                    }
                }
                catch (Exception error) { Show("Mapping workbook is not available", NORMErrorHandling.UserMessage(error, System.Web.HttpContext.Current, "Mapping"), false); }
            }
            BindSelectedRelease();
        }

        private void BindMappingContexts()
        {
            DataTable contexts = NORMHelper.Query(
                "SELECT EntityCode,FinancialYear FROM dbo.tblNORM_ReportingEntity WHERE IsDeactivated=0 " +
                "UNION SELECT EntityCode,FinancialYear FROM dbo.tblNORM_ConfigurationRelease " +
                "WHERE IsDeactivated=0 AND StatusCode IN ('Draft','Approved') ORDER BY FinancialYear DESC,EntityCode");
            MappingContextList.Items.Add(new ListItem("Select agency and financial year", ""));
            foreach (DataRow context in contexts.Rows)
            {
                string entity = NORMHelper.Str(context, "EntityCode");
                if (!NORMHelper.CanPrepareEntity(entity)) continue;
                int year = NORMHelper.Int(context, "FinancialYear");
                MappingContextList.Items.Add(new ListItem(entity + " FY" + year.ToString(CultureInfo.InvariantCulture), ContextValue(entity, year)));
            }
        }

        protected void MappingContextList_Changed(object sender, EventArgs e)
        {
            MessagePanel.Visible = false;
            try { SelectMappingContext(); BindSelectedRelease(); }
            catch (Exception error)
            {
                WorkflowPanel.Visible = false;
                BlankWorkbookPanel.Visible = true;
                Show("Mapping workbook is not available", NORMErrorHandling.UserMessage(error, HttpContext.Current, "Mapping"), false);
            }
        }

        private void SelectMappingContext()
        {
            CurrentReleaseId = 0;
            string[] scope = MappingContextList.SelectedValue.Split('|');
            if (scope.Length != 2) return;
            CurrentReleaseId = NORMMappingManagement.GetOrCreateWorkingDraft(scope[0],
                Int32.Parse(scope[1], CultureInfo.InvariantCulture), NORMHelper.CurrentUserId());
        }

        protected void CreateInitialDraftButton_Click(object sender, EventArgs e)
        {
            RunAction(delegate {
                string[] scope = MappingContextList.SelectedValue.Split('|');
                if (scope.Length != 2) return;
                CurrentReleaseId = NORMMappingManagement.CreateInitialDraft(scope[0],
                    Int32.Parse(scope[1], CultureInfo.InvariantCulture), NORMHelper.CurrentUserId());
                Show("Mapping draft ready", "Download the editable workbook for this draft.", true);
                BindSelectedRelease();
            }, "Mapping draft ready", "Download the editable workbook for this draft.");
        }

        private static string ContextValue(string entity, int year)
        { return entity + "|" + year.ToString(CultureInfo.InvariantCulture); }

        protected void ApplyWorkbookButton_Click(object sender, EventArgs e)
        {
            RunAction(delegate {
                if (!MappingFile.HasFile) throw new InvalidOperationException("Choose the completed mapping workbook.");
                int releaseId = RequireReleaseId("The mapping workbook is not ready. Return to the Control Centre and select Import mapping.");
                NORMMappingUploadOutcome result = NORMMappingManagement.ApplyWorkbook(releaseId, MappingFile.FileBytes, MappingFile.FileName, NORMHelper.CurrentUserId());
                Show("Workbook applied", result.ChangedCount.ToString("N0") + " account mapping(s) were changed. The draft has been revalidated.", true);
                BindSelectedRelease();
            }, "Workbook applied", "The mapping changes and their audit evidence have been saved to the draft.");
        }

        protected void ApproveButton_Click(object sender, EventArgs e)
        {
            RunAction(delegate {
                int releaseId = RequireReleaseId("The mapping workbook is not ready.");
                NORMMappingManagement.Approve(releaseId, WarningsAcknowledged.Checked, NORMHelper.CurrentUserId());
                Show("Release approved", "The mapping release is now immutable and ready for recalculation.", true);
                BindSelectedRelease();
            }, "Release approved", "The mapping release is immutable and ready for recalculation.");
        }

        protected void RecalculateButton_Click(object sender, EventArgs e)
        {
            try
            {
                int releaseId = RequireReleaseId("The approved mapping is not available.");
                NORMImportOutcome outcome = NORMMappingManagement.RecalculateLatest(releaseId, NORMHelper.CurrentUserId());
                Response.Redirect("NORM_Statements.aspx?run=" + outcome.CalculationRunId.ToString(CultureInfo.InvariantCulture), true);
            }
            catch (System.Threading.ThreadAbortException) { throw; }
            catch (Exception error) { Show("Recalculation could not be completed", NORMErrorHandling.UserMessage(error, System.Web.HttpContext.Current, "Mapping"), false); BindSelectedRelease(); }
        }

        private void BindSelectedRelease()
        {
            int releaseId = CurrentReleaseId;
            BlankWorkbookPanel.Visible = releaseId == 0;
            CreateInitialDraftButton.Visible = releaseId == 0 && MappingContextList.SelectedIndex > 0 && IsAdministrator;
            if (releaseId <= 0) { WorkflowPanel.Visible = false; return; }
            DataTable releases = NORMHelper.Query("SELECT c.*,p.VersionCode ParentVersion FROM dbo.tblNORM_ConfigurationRelease c LEFT JOIN dbo.tblNORM_ConfigurationRelease p ON p.ConfigurationReleaseId=c.ParentConfigurationReleaseId WHERE c.ConfigurationReleaseId=@release", NORMHelper.P("@release", releaseId));
            if (releases.Rows.Count == 0) { WorkflowPanel.Visible = false; return; }
            DataRow release = releases.Rows[0];
            if (!NORMHelper.CanPrepareEntity(NORMHelper.Str(release, "EntityCode"))) { WorkflowPanel.Visible = false; return; }
            ListItem scope = MappingContextList.Items.FindByValue(ContextValue(NORMHelper.Str(release, "EntityCode"), NORMHelper.Int(release, "FinancialYear")));
            if (!IsPostBack && scope != null) MappingContextList.SelectedValue = scope.Value;
            bool draft = String.Equals(NORMHelper.Str(release, "StatusCode"), "Draft", StringComparison.OrdinalIgnoreCase); bool approved = String.Equals(NORMHelper.Str(release, "StatusCode"), "Approved", StringComparison.OrdinalIgnoreCase);
            WorkflowPanel.Visible = true; UploadPanel.Visible = draft; ApprovalPanel.Visible = draft && IsAdministrator; RecalculatePanel.Visible = approved;
            DownloadWorkbookLink.Visible = draft; DownloadWorkbookLink.NavigateUrl = "NORM_MappingWorkbook.ashx?release=" + releaseId.ToString(CultureInfo.InvariantCulture);
            ApprovalNote.Text = draft ? (IsAdministrator ? "" : "<p class=\"norm-muted\">An active NORM administrator must approve this release.</p>") : "<p class=\"norm-success-copy\">Approved by " + Enc(NORMHelper.Str(release, "ApprovedBy")) + " on " + Enc(Convert.ToString(release["ApprovedUtc"])) + ". This version is locked.</p>";
            RecalculateNote.Text = approved ? "" : "<p class=\"norm-muted\">Approval is required before recalculation.</p>";
            ReleaseSummaryHtml = "<strong>" + Enc(NORMHelper.Str(release, "StatusCode")) + " · " + Enc(NORMHelper.Str(release, "VersionCode")) + "</strong><span>Based on " + Enc(NORMHelper.Str(release, "ParentVersion")) + "</span><small>" + Enc(NORMHelper.Str(release, "ChangeReason")) + "</small>";

            NORMMappingValidation validation = NORMMappingManagement.Validate(releaseId); ValidationStatus = validation.CanApprove ? (validation.Warnings.Count == 0 ? "Ready" : "Ready with warnings") : "Action required"; ValidationHtml = RenderValidation(validation);
            UnmappedAccountCount = validation.UnmappedCount.ToString("N0"); UnmappedAccountLabel = validation.UnmappedCount == 1 ? "unmapped account" : "unmapped accounts";
            ImpactHtml = RenderImpact(NORMMappingManagement.Impact(releaseId)); AuditHtml = RenderAudit(releaseId);
            if (draft && validation.Warnings.Count == 0) WarningsAcknowledged.Checked = true;
        }

        private string RenderValidation(NORMMappingValidation value)
        {
            StringBuilder html = new StringBuilder("<div class=\"norm-validation-summary\"><article><strong>" + value.MappingCount.ToString("N0") + "</strong><span>active mappings</span></article><article><strong>" + value.ChangedCount.ToString("N0") + "</strong><span>accounts changed</span></article><article><strong>" + value.UnmappedCount.ToString("N0") + "</strong><span>unmapped accounts</span></article><article><strong>" + value.ErrorCount.ToString("N0") + "</strong><span>blocking errors</span></article></div>");
            if (value.Errors.Count > 0) { html.Append("<div class=\"norm-validation-list error\"><strong>Resolve before approval</strong><ul>"); foreach (string item in value.Errors) html.Append("<li>").Append(Enc(item)).Append("</li>"); html.Append("</ul></div>"); }
            if (value.Warnings.Count > 0) { html.Append("<div class=\"norm-validation-list warning\"><strong>Review warnings</strong><ul>"); foreach (string item in value.Warnings) html.Append("<li>").Append(Enc(item)).Append("</li>"); html.Append("</ul></div>"); }
            return html.ToString();
        }

        private string RenderImpact(DataTable table)
        {
            if (table.Rows.Count == 0) return "<div class=\"norm-empty\">No mapping differences from the approved parent release.</div>";
            StringBuilder html = new StringBuilder("<table><thead><tr><th>G/L account</th><th class=\"amount\">TB balance</th><th>Previous face line</th><th>Draft face line</th><th>Note change</th><th>Reason</th></tr></thead><tbody>");
            foreach (DataRow row in table.Rows) html.Append("<tr><td><strong>").Append(Enc(NORMHelper.Str(row, "GlCode"))).Append("</strong></td><td class=\"amount\">").Append(NORMHelper.Dec(row, "Balance").ToString("N2")).Append("</td><td>").Append(Enc(NORMHelper.Str(row, "PreviousLine"))).Append("</td><td><strong>").Append(Enc(NORMHelper.Str(row, "DraftLine"))).Append("</strong></td><td>").Append(Enc(NORMHelper.Str(row, "PreviousNote"))).Append(" &rarr; ").Append(Enc(NORMHelper.Str(row, "DraftNote"))).Append("</td><td>").Append(Enc(NORMHelper.Str(row, "ChangeReason"))).Append("</td></tr>");
            html.Append("</tbody></table>"); return html.ToString();
        }

        private string RenderAudit(int releaseId)
        {
            DataTable table = NORMHelper.Query("SELECT TOP 100 GlCode,BeforeStatementLine,AfterStatementLine,BeforeNoteSubLine,AfterNoteSubLine,ChangeReason,WorkbookHash,ChangedBy,ChangedUtc FROM dbo.tblNORM_MappingChange WHERE ConfigurationReleaseId=@release ORDER BY MappingChangeId DESC", NORMHelper.P("@release", releaseId));
            if (table.Rows.Count == 0) return "<div class=\"norm-empty\">No account-level mapping changes recorded yet.</div>";
            StringBuilder html = new StringBuilder("<div class=\"norm-mapping-audit\">"); foreach (DataRow row in table.Rows) html.Append("<article><strong>").Append(Enc(NORMHelper.Str(row, "GlCode"))).Append("</strong><span>").Append(Enc(NORMHelper.Str(row, "BeforeStatementLine"))).Append(" &rarr; ").Append(Enc(NORMHelper.Str(row, "AfterStatementLine"))).Append("</span><p>").Append(Enc(NORMHelper.Str(row, "ChangeReason"))).Append("</p><small>").Append(Enc(NORMHelper.Str(row, "ChangedBy"))).Append(" · ").Append(Enc(Convert.ToString(row["ChangedUtc"]))).Append(" · SHA-256 ").Append(Enc(NORMHelper.Str(row, "WorkbookHash").Substring(0, 12))).Append("…</small></article>"); html.Append("</div>"); return html.ToString();
        }

        private void RunAction(Action action, string title, string success)
        {
            try { action(); if (!MessagePanel.Visible) Show(title, success, true); }
            catch (System.Threading.ThreadAbortException) { throw; }
            catch (Exception error) { Show("Action could not be completed", NORMErrorHandling.UserMessage(error, System.Web.HttpContext.Current, "Mapping"), false); BindSelectedRelease(); }
        }
        private void Show(string title, string text, bool success) { MessagePanel.Visible = true; MessagePanel.CssClass = "norm-alert " + (success ? "norm-alert-success" : "norm-alert-error"); MessageTitle.Text = Enc(title); MessageText.Text = "<span>" + Enc(text) + "</span>"; }
        private int RequireReleaseId(string message) { if (CurrentReleaseId <= 0) throw new InvalidOperationException(message); return CurrentReleaseId; }
        private static string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
