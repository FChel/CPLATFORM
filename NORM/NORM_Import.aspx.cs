using System;
using System.Data;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    public partial class NORM_Import : NORMBasePage
    {
        private const string PendingKey = "NORM.BatchImportPreview";

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
                List<NORMImportPreview> previews = Session[PendingKey] as List<NORMImportPreview>;
                if (previews != null && previews[0].Token == ViewState["PreviewToken"] as string && previews[0].Owner == NORMHelper.CurrentUserId())
                { ShowPreview(previews); }
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
                long bytes = 0;
                foreach (HttpPostedFile file in TrialBalanceFile.PostedFiles) { bytes += file.ContentLength; }
                if (bytes == 0) { throw new InvalidDataException("Choose non-empty trial balance files."); }
                if (bytes > NORMHelper.SettingInt("NORM.MaxUploadBytes", 104857600))
                { throw new InvalidDataException("The combined files exceed the configured upload limit."); }
                List<NORMImportPreview> previews = new List<NORMImportPreview>();
                foreach (HttpPostedFile file in TrialBalanceFile.PostedFiles)
                {
                    byte[] content;
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        file.InputStream.CopyTo(buffer);
                        content = buffer.ToArray();
                    }
                    previews.Add(NORMImportService.Inspect(content, file.FileName, releaseId, NORMHelper.CurrentUserId()));
                }
                NORMImportService.ValidateBatch(previews);
                Session[PendingKey] = previews;
                ViewState["PreviewToken"] = previews[0].Token;
                ShowPreview(previews);
            }
            catch (Exception error) { ShowError(error.Message); }
        }

        protected void ConfirmButton_Click(object sender, EventArgs e)
        {
            ErrorPanel.Visible = false;
            List<NORMImportPreview> previews = Session[PendingKey] as List<NORMImportPreview>;
            try
            {
                if (!NORMHelper.HasPrepareAccess()) { throw new UnauthorizedAccessException("Preparer access is required."); }
                if (previews == null || !String.Equals(previews[0].Token, ViewState["PreviewToken"] as string, StringComparison.Ordinal))
                { throw new InvalidOperationException("This preview expired, was cancelled or has already been used. Check existing imports before inspecting again."); }
                if (TrialBalanceFile.HasFiles || ReleaseList.SelectedValue != previews[0].ReleaseId.ToString(CultureInfo.InvariantCulture))
                { throw new InvalidOperationException("The selected files or configuration changed. Inspect the batch again."); }
                // ASP.NET's exclusive session lock serialises requests for this session.
                // Consume before acceptance: replay, refresh and double clicks cannot reuse it.
                bool replace = ReplaceExisting.Checked;
                ClearPreview();
                List<NORMImportOutcome> outcomes = NORMImportService.ConfirmBatch(previews, NORMHelper.CurrentUserId(), replace);
                ResultPanel.Visible = true;
                StringBuilder result = new StringBuilder();
                foreach (NORMImportOutcome outcome in outcomes)
                {
                    result.Append("<p>Source accepted. Import reference: ").Append(outcome.ImportId.ToString(CultureInfo.InvariantCulture)).Append(".</p>");
                    if (outcome.CalculationRunId > 0)
                    {
                        result.Append("<p>Calculation completed. <a href=\"NORM_Statements.aspx?run=")
                            .Append(outcome.CalculationRunId.ToString(CultureInfo.InvariantCulture))
                            .Append("\">Review statements, validation results and unmapped accounts</a>.</p>");
                    }
                    else { result.Append("<p>").Append(HttpUtility.HtmlEncode(outcome.CalculationError)).Append("</p>"); }
                }
                ResultMessage.Text = result.ToString();
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
            ReplaceExisting.Checked = false;
        }

        private void ShowError(string message)
        {
            ErrorPanel.Visible = true;
            ErrorMessage.Text = "<p>" + HttpUtility.HtmlEncode(message) + "</p>";
        }

        private void ShowPreview(IList<NORMImportPreview> previews)
        {
            PreviewPanel.Visible = true;
            bool canAccept = true;
            bool replacement = false;
            StringBuilder html = new StringBuilder();
            foreach (NORMImportPreview preview in previews)
            {
                canAccept &= preview.CanAccept;
                replacement |= preview.HasReplacement;
                AppendSourcePreview(html, preview);
            }
            ConfirmButton.Enabled = canAccept;
            ReplaceExisting.Visible = replacement && canAccept;
            ConfirmButton.Text = replacement ? "Confirm batch and replace sources" : "Confirm batch acceptance";
            html.Append("<p>").Append(canAccept ? "Confirmation applies to the entire batch." :
                "The batch cannot be accepted while any selected source fails validation.").Append("</p>");
            html.Append("<p>The preview expires after 15 minutes. Changes to the files, configuration or accepted sources require another inspection.</p>");
            PreviewMessage.Text = html.ToString();
        }

        private static void AppendSourcePreview(StringBuilder html, NORMImportPreview preview)
        {
            html.Append("<dl>");
            string[,] fields = {
                { "Source file", preview.FileName }, { "Identified layout", preview.Layout },
                { "Approved configuration", preview.ConfigurationVersion + " (release " + preview.ReleaseId.ToString() + ")" },
                { "Agency/entity", preview.Entity }, { "Financial year", preview.FinancialYear.ToString() },
                { "Company", preview.Companies }, { "Ledger", preview.Ledgers },
                { "Reporting periods", preview.PeriodStart.ToString("00") + "â€“" + preview.PeriodEnd.ToString("00") },
                { "Account rows", preview.RowCount.ToString("N0") }, { "Unmapped accounts", preview.UnmappedAccounts.ToString("N0") },
                { "Debit balances (AUD)", preview.TotalsAvailable ? preview.Debit.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" },
                { "Credit balances (AUD)", preview.TotalsAvailable ? preview.Credit.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" },
                { "Net (AUD)", preview.TotalsAvailable ? preview.Net.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" }
            };
            for (int i = 0; i < fields.GetLength(0); i++)
            { html.Append("<dt>").Append(HttpUtility.HtmlEncode(fields[i,0])).Append("</dt><dd>").Append(HttpUtility.HtmlEncode(fields[i,1])).Append("</dd>"); }
            html.Append("</dl><p>").Append(preview.CanAccept ? "Source validation passed. This upload has not been accepted." : "Source validation failed. Acceptance is disabled.").Append("</p>");
            html.Append("<ul><li>Content layout, year, period and permitted source: Pass</li>");
            if (preview.Layout == "Defence ERP") { html.Append("<li>Detail reconciles to company and grand-total controls: Pass</li>"); }
            html.Append("<li>Exact-zero balance: ").Append(!preview.TotalsAvailable ? "Not run" : preview.Net == 0m ? "Pass" : "Fail").Append("</li>");
            html.Append("<li>Lossless decimal storage: ").Append(preview.PrecisionValid ? "Pass" : "Fail").Append("</li></ul>");
            foreach (string error in preview.Errors) { html.Append("<p>").Append(HttpUtility.HtmlEncode(error)).Append("</p>"); }
            foreach (string warning in preview.Warnings) { html.Append("<p>").Append(HttpUtility.HtmlEncode(warning)).Append("</p>"); }
            foreach (NORMSourceScope scope in preview.SourceScopes)
            {
                if (scope.ImportId == 0) { continue; }
                html.Append("<p><strong>Replacement:</strong> ")
                    .Append(HttpUtility.HtmlEncode("Company " + (scope.Company ?? "(legacy)") + ", ledger " + scope.Ledger +
                        " will replace " + scope.Description + ". The original remains available as historical evidence."))
                    .Append("</p>");
            }
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
