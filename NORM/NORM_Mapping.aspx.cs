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

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) { BindBaseReleases(); BindWorkingReleases(); SelectQueryRelease(); }
            BindSelectedRelease();
        }

        protected void CreateDraftButton_Click(object sender, EventArgs e)
        {
            RunAction(delegate {
                int baseId = SelectedId(BaseReleaseList, "Select an approved starting release.");
                int releaseId = NORMMappingManagement.CreateDraft(baseId, VersionInput.Text, DraftReason.Text, NORMHelper.CurrentUserId());
                Response.Redirect("NORM_Mapping.aspx?release=" + releaseId.ToString(CultureInfo.InvariantCulture), true);
            }, "Draft created", "The controlled draft is ready. Download its mapping workbook to begin.");
        }

        protected void WorkingReleaseList_Changed(object sender, EventArgs e) { Response.Redirect("NORM_Mapping.aspx?release=" + WorkingReleaseList.SelectedValue, true); }

        protected void ApplyWorkbookButton_Click(object sender, EventArgs e)
        {
            RunAction(delegate {
                if (!MappingFile.HasFile) throw new InvalidOperationException("Choose the completed mapping workbook.");
                int releaseId = SelectedId(WorkingReleaseList, "Select a draft mapping release.");
                NORMMappingUploadOutcome result = NORMMappingManagement.ApplyWorkbook(releaseId, MappingFile.FileBytes, MappingFile.FileName, NORMHelper.CurrentUserId());
                Show("Workbook applied", result.ChangedCount.ToString("N0") + " account mapping(s) were changed. The draft has been revalidated.", true);
                BindWorkingReleases(); SelectRelease(releaseId); BindSelectedRelease();
            }, "Workbook applied", "The mapping changes and their audit evidence have been saved to the draft.");
        }

        protected void ApproveButton_Click(object sender, EventArgs e)
        {
            RunAction(delegate {
                int releaseId = SelectedId(WorkingReleaseList, "Select a draft mapping release.");
                NORMMappingManagement.Approve(releaseId, WarningsAcknowledged.Checked, NORMHelper.CurrentUserId());
                Show("Release approved", "The mapping release is now immutable and ready for recalculation.", true);
                BindBaseReleases(); BindWorkingReleases(); SelectRelease(releaseId); BindSelectedRelease();
            }, "Release approved", "The mapping release is immutable and ready for recalculation.");
        }

        protected void RecalculateButton_Click(object sender, EventArgs e)
        {
            try
            {
                int releaseId = SelectedId(WorkingReleaseList, "Select an approved mapping release.");
                NORMImportOutcome outcome = NORMMappingManagement.RecalculateLatest(releaseId, NORMHelper.CurrentUserId());
                Response.Redirect("NORM_Statements.aspx?run=" + outcome.CalculationRunId.ToString(CultureInfo.InvariantCulture), true);
            }
            catch (System.Threading.ThreadAbortException) { throw; }
            catch (Exception error) { Show("Recalculation could not be completed", error.GetBaseException().Message, false); BindSelectedRelease(); }
        }

        private void BindBaseReleases()
        {
            DataTable table = NORMHelper.Query("SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,ReleaseLabel FROM dbo.tblNORM_ConfigurationRelease WHERE StatusCode='Approved' AND IsDeactivated=0 ORDER BY FinancialYear DESC,EntityCode,ConfigurationReleaseId DESC");
            BaseReleaseList.Items.Clear(); foreach (DataRow row in table.Rows) BaseReleaseList.Items.Add(new ListItem("FY" + NORMHelper.Int(row, "FinancialYear") + " " + NORMHelper.Str(row, "EntityCode") + " " + NORMHelper.Str(row, "VersionCode") + " - " + NORMHelper.Str(row, "ReleaseLabel"), NORMHelper.Int(row, "ConfigurationReleaseId").ToString(CultureInfo.InvariantCulture)));
        }

        private void BindWorkingReleases()
        {
            string selected = WorkingReleaseList.SelectedValue;
            DataTable table = NORMHelper.Query("SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,StatusCode FROM dbo.tblNORM_ConfigurationRelease WHERE ParentConfigurationReleaseId IS NOT NULL AND IsDeactivated=0 ORDER BY CASE WHEN StatusCode='Draft' THEN 0 ELSE 1 END,ConfigurationReleaseId DESC");
            WorkingReleaseList.Items.Clear(); WorkingReleaseList.Items.Add(new ListItem("Select a mapping release", ""));
            foreach (DataRow row in table.Rows) WorkingReleaseList.Items.Add(new ListItem(NORMHelper.Str(row, "StatusCode") + " · FY" + NORMHelper.Int(row, "FinancialYear") + " " + NORMHelper.Str(row, "EntityCode") + " " + NORMHelper.Str(row, "VersionCode"), NORMHelper.Int(row, "ConfigurationReleaseId").ToString(CultureInfo.InvariantCulture)));
            if (!String.IsNullOrWhiteSpace(selected) && WorkingReleaseList.Items.FindByValue(selected) != null) WorkingReleaseList.SelectedValue = selected;
        }

        private void SelectQueryRelease() { int release; if (Int32.TryParse(Request.QueryString["release"], out release)) SelectRelease(release); }
        private void SelectRelease(int releaseId) { string value = releaseId.ToString(CultureInfo.InvariantCulture); if (WorkingReleaseList.Items.FindByValue(value) != null) WorkingReleaseList.SelectedValue = value; }

        private void BindSelectedRelease()
        {
            int releaseId; if (!Int32.TryParse(WorkingReleaseList.SelectedValue, out releaseId)) { WorkflowPanel.Visible = false; return; }
            DataTable releases = NORMHelper.Query("SELECT c.*,p.VersionCode ParentVersion FROM dbo.tblNORM_ConfigurationRelease c LEFT JOIN dbo.tblNORM_ConfigurationRelease p ON p.ConfigurationReleaseId=c.ParentConfigurationReleaseId WHERE c.ConfigurationReleaseId=@release", NORMHelper.P("@release", releaseId));
            if (releases.Rows.Count == 0) { WorkflowPanel.Visible = false; return; }
            DataRow release = releases.Rows[0]; bool draft = String.Equals(NORMHelper.Str(release, "StatusCode"), "Draft", StringComparison.OrdinalIgnoreCase); bool approved = String.Equals(NORMHelper.Str(release, "StatusCode"), "Approved", StringComparison.OrdinalIgnoreCase);
            WorkflowPanel.Visible = true; UploadPanel.Visible = draft; ApprovalPanel.Visible = draft && IsAdministrator; RecalculatePanel.Visible = approved;
            DownloadWorkbookLink.Visible = draft; DownloadWorkbookLink.NavigateUrl = "NORM_MappingWorkbook.ashx?release=" + releaseId.ToString(CultureInfo.InvariantCulture);
            ApprovalNote.Text = draft ? (IsAdministrator ? "" : "<p class=\"norm-muted\">An active NORM administrator must approve this release.</p>") : "<p class=\"norm-success-copy\">Approved by " + Enc(NORMHelper.Str(release, "ApprovedBy")) + " on " + Enc(Convert.ToString(release["ApprovedUtc"])) + ". This version is locked.</p>";
            RecalculateNote.Text = approved ? "" : "<p class=\"norm-muted\">Approval is required before recalculation.</p>";
            ReleaseSummaryHtml = "<strong>" + Enc(NORMHelper.Str(release, "StatusCode")) + " · " + Enc(NORMHelper.Str(release, "VersionCode")) + "</strong><span>Based on " + Enc(NORMHelper.Str(release, "ParentVersion")) + "</span><small>" + Enc(NORMHelper.Str(release, "ChangeReason")) + "</small>";

            NORMMappingValidation validation = NORMMappingManagement.Validate(releaseId); ValidationStatus = validation.CanApprove ? (validation.Warnings.Count == 0 ? "Ready" : "Ready with warnings") : "Action required"; ValidationHtml = RenderValidation(validation);
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
            catch (Exception error) { Show("Action could not be completed", error.GetBaseException().Message, false); BindSelectedRelease(); }
        }
        private void Show(string title, string text, bool success) { MessagePanel.Visible = true; MessagePanel.CssClass = "norm-alert " + (success ? "norm-alert-success" : "norm-alert-error"); MessageTitle.Text = Enc(title); MessageText.Text = "<span>" + Enc(text) + "</span>"; }
        private static int SelectedId(ListControl list, string message) { int value; if (!Int32.TryParse(list.SelectedValue, out value)) throw new InvalidOperationException(message); return value; }
        private static string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
