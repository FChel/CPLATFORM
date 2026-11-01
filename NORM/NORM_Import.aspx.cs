using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    public partial class NORM_Import : NORMBasePage
    {
        private const string PendingKey = "NORM.SingleFileImportPreview";

        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            ViewStateUserKey = Session.SessionID + "|" + NORMHelper.CurrentUserId();
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) { ClearPreview(); BindReleases(); }
            else
            {
                NORMImportPreview preview = Session[PendingKey] as NORMImportPreview;
                if (preview != null && preview.Token == ViewState["PreviewToken"] as string && preview.Owner == NORMHelper.CurrentUserId())
                { ShowPreview(preview); }
            }
        }

        protected void ImportButton_Click(object sender, EventArgs e)
        {
            ErrorPanel.Visible = false;
            ResultPanel.Visible = false;
            ClearPreview();
            try
            {
                if (!NORMHelper.HasPrepareAccess()) { throw new UnauthorizedAccessException("Preparer access is required."); }
                int releaseId;
                if (!Int32.TryParse(ReleaseList.SelectedValue, out releaseId))
                { throw new InvalidOperationException("Select an approved configuration release."); }
                if (!TrialBalanceFile.HasFile) { throw new InvalidDataException("Choose a non-empty trial balance file."); }
                NORMImportPreview preview = NORMImportService.Inspect(TrialBalanceFile.FileBytes,
                    TrialBalanceFile.FileName, releaseId, NORMHelper.CurrentUserId());
                Session[PendingKey] = preview;
                ViewState["PreviewToken"] = preview.Token;
                ShowPreview(preview);
            }
            catch (Exception error) { ShowError(error.Message); }
        }

        protected void ConfirmButton_Click(object sender, EventArgs e)
        {
            ErrorPanel.Visible = false;
            NORMImportPreview preview = Session[PendingKey] as NORMImportPreview;
            try
            {
                if (!NORMHelper.HasPrepareAccess()) { throw new UnauthorizedAccessException("Preparer access is required."); }
                if (preview == null || !String.Equals(preview.Token, ViewState["PreviewToken"] as string, StringComparison.Ordinal))
                { throw new InvalidOperationException("This preview expired, was cancelled or has already been used. Check existing imports before inspecting again."); }
                if (TrialBalanceFile.HasFile || ReleaseList.SelectedValue != preview.ReleaseId.ToString(CultureInfo.InvariantCulture))
                { throw new InvalidOperationException("The file or selected configuration changed. Inspect the file again."); }
                // ASP.NET's exclusive session lock serialises requests for this session.
                // Consume before acceptance: replay, refresh and double clicks cannot reuse it.
                ClearPreview();
                NORMImportOutcome outcome = NORMImportService.Confirm(preview, NORMHelper.CurrentUserId());
                ResultPanel.Visible = true;
                ResultMessage.Text = "<p>Source accepted. Import reference: " + outcome.ImportId.ToString(CultureInfo.InvariantCulture) + ".</p>";
                if (outcome.CalculationRunId > 0)
                {
                    ResultMessage.Text += "<p>Calculation completed. <a href=\"NORM_Statements.aspx?run=" + outcome.CalculationRunId.ToString(CultureInfo.InvariantCulture) + "\">Review statements, validation results and unmapped accounts</a>.</p>";
                }
                else { ResultMessage.Text += "<p>" + HttpUtility.HtmlEncode(outcome.CalculationError) + "</p>"; }
            }
            catch (Exception error) { ClearPreview(); ShowError(error.Message); }
        }

        protected void CancelButton_Click(object sender, EventArgs e)
        {
            ClearPreview(); ErrorPanel.Visible = false; ResultPanel.Visible = false;
        }

        private void ClearPreview()
        {
            Session.Remove(PendingKey);
            ViewState.Remove("PreviewToken");
            PreviewPanel.Visible = false;
        }

        private void ShowError(string message)
        {
            ErrorPanel.Visible = true;
            ErrorMessage.Text = "<p>" + HttpUtility.HtmlEncode(message) + "</p>";
        }

        private void ShowPreview(NORMImportPreview preview)
        {
            PreviewPanel.Visible = true;
            ConfirmButton.Enabled = preview.CanAccept;
            StringBuilder html = new StringBuilder("<dl>");
            string[,] fields = {
                { "Source file", preview.FileName }, { "Identified layout", preview.Layout },
                { "Approved configuration", preview.ConfigurationVersion + " (release " + preview.ReleaseId.ToString() + ")" },
                { "Agency/entity", preview.Entity }, { "Financial year", preview.FinancialYear.ToString() },
                { "Company", preview.Companies }, { "Ledger", preview.Ledgers },
                { "Reporting periods", preview.PeriodStart.ToString("00") + "–" + preview.PeriodEnd.ToString("00") },
                { "Account rows", preview.RowCount.ToString("N0") }, { "Unmapped accounts", preview.UnmappedAccounts.ToString("N0") },
                { "Debit balances (AUD)", preview.TotalsAvailable ? preview.Debit.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" },
                { "Credit balances (AUD)", preview.TotalsAvailable ? preview.Credit.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" },
                { "Net (AUD)", preview.TotalsAvailable ? preview.Net.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" }
            };
            for (int i = 0; i < fields.GetLength(0); i++)
            { html.Append("<dt>").Append(HttpUtility.HtmlEncode(fields[i,0])).Append("</dt><dd>").Append(HttpUtility.HtmlEncode(fields[i,1])).Append("</dd>"); }
            html.Append("</dl><p>").Append(preview.CanAccept ? "Source validation passed. No source has been accepted yet." : "Source validation failed. Acceptance is disabled.").Append("</p>");
            html.Append("<ul><li>Content layout, year, period and permitted source: Pass</li>");
            if (preview.Layout == "Defence ERP") { html.Append("<li>Detail reconciles to company and grand-total controls: Pass</li>"); }
            html.Append("<li>Exact-zero balance: ").Append(!preview.TotalsAvailable ? "Not run" : preview.Net == 0m ? "Pass" : "Fail").Append("</li>");
            html.Append("<li>Lossless decimal storage: ").Append(preview.PrecisionValid ? "Pass" : "Fail").Append("</li></ul>");
            foreach (string error in preview.Errors) { html.Append("<p>").Append(HttpUtility.HtmlEncode(error)).Append("</p>"); }
            foreach (string warning in preview.Warnings) { html.Append("<p>").Append(HttpUtility.HtmlEncode(warning)).Append("</p>"); }
            html.Append("<p>Confirm acceptance of this file and configuration. The preview expires after 15 minutes; changed files or configuration require another inspection.</p>");
            PreviewMessage.Text = html.ToString();
        }
        private void BindReleases()
        {
            DataTable releases = NORMHelper.Query(
                "SELECT ConfigurationReleaseId,FinancialYear,CAST(FinancialYear AS VARCHAR(4)) + ' ' + EntityCode + ' ' + VersionCode + ' - ' + ReleaseLabel AS DisplayLabel " +
                "FROM dbo.tblNORM_ConfigurationRelease WHERE StatusCode = 'Approved' AND IsDeactivated = 0 " +
                "ORDER BY FinancialYear DESC,EntityCode,VersionCode DESC");
            ReleaseList.Items.Clear();
            for (int i = 0; i < releases.Rows.Count; i++)
            {
                DataRow row = releases.Rows[i];
                ListItem item = new ListItem(NORMHelper.Str(row, "DisplayLabel"),
                    NORMHelper.Int(row, "ConfigurationReleaseId").ToString());
                item.Attributes["data-financial-year"] = NORMHelper.Int(row, "FinancialYear").ToString();
                ReleaseList.Items.Add(item);
            }
            if (ReleaseList.Items.Count == 0)
            {
                ReleaseList.Items.Add(new ListItem("No approved configuration release is installed", ""));
                ImportButton.Enabled = false;
            }
        }

    }
}
