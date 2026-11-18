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
            if (!IsPostBack)
            {
                ClearPreview(); BindAgencies();
                ListItem context = AgencyList.Items.FindByValue(Request.QueryString["agency"] ?? "");
                if (context != null) { AgencyList.SelectedValue = context.Value; }
                ShowAgencyContext();
            }
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
                string agency = AgencyList.SelectedValue;
                NORMHelper.RequireEntityAccess(agency);
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
                    previews.AddRange(NORMImportService.InspectFileForAnnualContext(content, file.FileName, agency, NORMHelper.CurrentUserId()));
                }
                NORMImportService.ValidateBatch(previews);
                Session[PendingKey] = previews;
                ViewState["PreviewToken"] = previews[0].Token;
                ViewState["PreviewAgency"] = agency;
                ShowPreview(previews);
            }
            catch (Exception error) { ShowError(NORMErrorHandling.UserMessage(error, HttpContext.Current, "ImportInspection")); }
        }

        protected void ConfirmButton_Click(object sender, EventArgs e)
        {
            ErrorPanel.Visible = false;
            ReplacementError.Text = "";
            List<NORMImportPreview> previews = Session[PendingKey] as List<NORMImportPreview>;
            try
            {
                if (!NORMHelper.HasPrepareAccess()) { throw new UnauthorizedAccessException("Preparer access is required."); }
                if (previews == null || !String.Equals(previews[0].Token, ViewState["PreviewToken"] as string, StringComparison.Ordinal))
                { throw new InvalidOperationException("This preview expired, was cancelled or has already been used. Check existing imports before inspecting again."); }
                if (TrialBalanceFile.HasFiles || AgencyList.SelectedValue != ViewState["PreviewAgency"] as string)
                { throw new InvalidOperationException("The selected files or agency changed. Inspect the batch again."); }
                string blocked = NORMImportService.BatchConfirmationBlockReason(previews);
                if (blocked != null) { throw new InvalidOperationException(blocked); }
                string user = NORMHelper.CurrentUserId();
                bool needsReplacement = false;
                foreach (NORMImportPreview preview in previews)
                {
                    if (!preview.CanAccept || !String.Equals(preview.Owner, user, StringComparison.Ordinal))
                    { throw new InvalidOperationException("This preview is invalid or expired. Inspect the batch again."); }
                    needsReplacement |= preview.HasReplacement;
                }
                bool replace = ReplaceExisting.Checked;
                if (needsReplacement && !replace)
                {
                    ReplacementError.Text = "<p class=\"norm-alert norm-alert-error\" role=\"alert\">Please tick the checkbox to confirm replacement.</p>";
                    return;
                }

                // ASP.NET's exclusive session lock serialises requests for this session.
                // Consume before acceptance: replay, refresh and double clicks cannot reuse it.
                ClearPreview();
                List<NORMImportOutcome> outcomes = NORMImportService.ConfirmBatch(previews, user, replace, true);
                ResultPanel.Visible = true;
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < outcomes.Count; i++)
                {
                    NORMImportOutcome outcome = outcomes[i];
                    NORMImportPreview accepted = previews[i];
                    result.Append("<p>Source accepted. Import reference: ").Append(outcome.ImportId.ToString(CultureInfo.InvariantCulture)).Append(".</p>");
                    result.Append("<p>").Append(HttpUtility.HtmlEncode("FY" + accepted.FinancialYear + ", company " + accepted.Companies +
                        ", ledger " + accepted.Ledgers + ", " + accepted.ReportingStreams + ", " + accepted.ConfigurationVersion +
                        "; " + accepted.FileName)).Append("</p>");
                    result.Append("<p><a href=\"NORM_SourceHistory.aspx?import=").Append(outcome.ImportId).Append("&amp;release=").Append(accepted.ReleaseId)
                        .Append("&amp;viewStream=").Append(HttpUtility.UrlEncode(accepted.Population.ReportingStream)).Append("\">Source history and original files</a></p>");
                    if (outcome.CalculationRunId > 0)
                    {
                        result.Append("<p>Calculation completed. <a href=\"NORM_Statements.aspx?run=")
                            .Append(outcome.CalculationRunId.ToString(CultureInfo.InvariantCulture))
                            .Append("\">Review statements, validation results and unmapped accounts</a>.</p>");
                    }
                    else if (!String.IsNullOrEmpty(outcome.CalculationError))
                    { result.Append("<p>").Append(HttpUtility.HtmlEncode(outcome.CalculationError)).Append("</p>"); }
                }
                ResultMessage.Text = result.ToString();
                if (previews[0].UsesAnnualSourceApproval) { ShowAgencyContext(); }
            }
            catch (Exception error) { ClearPreview(); ShowError(NORMErrorHandling.UserMessage(error, HttpContext.Current, "ImportConfirmation")); }
        }

        protected void CancelButton_Click(object sender, EventArgs e)
        {
            ClearPreview(); ErrorPanel.Visible = false; ResultPanel.Visible = false;
        }

        private void ClearPreview()
        {
            Session.Remove(PendingKey);
            ViewState.Remove("PreviewToken");
            ViewState.Remove("PreviewAgency");
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
            HashSet<string> replacementFiles = new HashSet<string>(StringComparer.Ordinal);
            int replacementScopes = 0;
            StringBuilder html = new StringBuilder();
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
            bool batchInvalid = false;
            foreach (NORMImportPreview member in previews) { batchInvalid |= !member.CanAccept; }
            string blocked = NORMImportService.BatchConfirmationBlockReason(previews);
            foreach (NORMImportPreview preview in previews)
            {
                NORMImportFileContext file = preview.SourceFile;
                if (file != null && files.Add(file.Token))
                {
                    html.Append("<h3>").Append(HttpUtility.HtmlEncode(file.FileName)).Append("</h3><p>File row accounting: ")
                        .Append(file.RecordCount).Append(" ").Append(HttpUtility.HtmlEncode(file.RecordUnit))
                        .Append(" = ").Append(file.DetailRowCount).Append(" financial detail + ").Append(file.MetadataRowCount)
                        .Append(" metadata/header/separator + ").Append(file.ControlRowCount).Append(" source control + ")
                        .Append(file.BlankRowCount).Append(" blank. Populations: ").Append(file.PopulationCount)
                        .Append(". Reconciliation: ").Append(file.RowsReconciled ? "Pass" : "Fail").Append(".</p>");
                }
                canAccept &= preview.CanAccept;
                if (preview.HasReplacement) { replacementFiles.Add(file == null ? preview.Token : file.Token); }
                replacementScopes += AppendSourcePreview(html, preview);
                html.Append("<p>Accepted rows: 0 (inspection only). Invalid rows: ").Append(preview.InvalidRowCount)
                    .Append(batchInvalid || blocked != null ? "; valid rows withheld: " : "; valid rows awaiting confirmation: ")
                    .Append(preview.ValidatedRowCount).Append(".</p>");
            }
            if (blocked != null)
            {
                canAccept = false;
                html.Append("<p role=\"alert\">").Append(HttpUtility.HtmlEncode(blocked)).Append("</p>");
            }
            ConfirmButton.Enabled = canAccept;
            bool replacement = replacementFiles.Count > 0;
            bool multipleFiles = replacementFiles.Count > 1;
            ReplaceExisting.Visible = replacement && canAccept;
            ConfirmButton.Text = replacement ? "Confirm replacement" : "Confirm batch acceptance";
            if (replacement)
            {
                html.Append(replacementScopes > 1
                    ? "<p>Confirming these replacements will replace the listed figures with those from the " +
                        (multipleFiles ? "corresponding uploaded files" : "newly uploaded file") +
                        ". The previous figures will be retained as historical evidence.</p>"
                    : "<p>Confirming this replacement will replace the existing figures with those from the newly uploaded file. The previous import will be retained as historical evidence.</p>");
            }
            html.Append("<p>").Append(canAccept ? "Confirmation applies to the entire batch." : blocked != null ?
                "This preview is available for review only; confirmation is disabled." : "The batch cannot be accepted while any selected source fails validation.").Append("</p>");
            html.Append("<p><small>The preview expires after 15 minutes. Changes to the files, configuration or accepted sources require another inspection.</small></p>");
            PreviewMessage.Text = html.ToString();
        }

        private static int AppendSourcePreview(StringBuilder html, NORMImportPreview preview)
        {
            html.Append("<dl class=\"norm-import-review\">");
            string[,] fields = {
                { "Source file", preview.FileName }, { "Layout", preview.Layout },
                { "Original SHA-256", preview.FileHash },
                { "Source profile", preview.ProfileVersion },
                { "Currency / units", preview.ProfileVersion == NihracsTrialBalanceImporter.Profile
                    ? "AUD dollars (multiplier 1), positive source debit and credit sides"
                    : "AUD dollars (multiplier 1); ending balances debit-positive / credit-negative" },
                { "Configuration", preview.ConfigurationVersion + (preview.ReleaseId > 0 ? " (release " + preview.ReleaseId + ")" : "") },
                { "Agency", preview.Entity }, { "Financial year", preview.FinancialYear.ToString() },
                { "Company", preview.Companies }, { "Ledger", preview.Ledgers }, { "Reporting stream", preview.ReportingStreams },
                { "Source status", preview.Population.ReportingStream == null ? "Unresolved" : preview.HasReplacement ? "Replacement of accepted population" : "New source population" },
                { "Periods", preview.PeriodStart.ToString("00") + "\u2013" + preview.PeriodEnd.ToString("00") },
                { "Actual coverage", preview.CoverageStart.HasValue && preview.CoverageEnd.HasValue
                    ? preview.CoverageStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " to " + preview.CoverageEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "Unknown" },
                { "Coverage", preview.IsFullYear.HasValue ? (preview.IsFullYear.Value ? "Full year" : "Partial year") : "Unknown" },
                { "Rows", preview.RowCount.ToString("N0") }, { "Unmapped accounts", preview.UnmappedAccounts.ToString("N0") },
                { "Debit (AUD)", preview.TotalsAvailable ? preview.Debit.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" },
                { "Credit (AUD)", preview.TotalsAvailable ? preview.Credit.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" },
                { "Net (AUD)", preview.TotalsAvailable ? preview.Net.ToString("0.00##########################", CultureInfo.InvariantCulture) : "Not available" }
            };
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                html.Append("<div><dt>").Append(HttpUtility.HtmlEncode(fields[i,0]))
                    .Append("</dt><dd>").Append(HttpUtility.HtmlEncode(fields[i,1])).Append("</dd></div>");
            }
            html.Append("</dl><p>").Append(preview.CanAccept ? "Source validation passed. This upload has not been accepted." : "Source validation failed. Acceptance is disabled.").Append("</p>");
            html.Append("<ul><li>Content layout recognised. Identity, coverage and population validation: ")
                .Append(preview.Errors.Count == 0 ? "Pass" : "Fail; see errors below").Append("</li>");
            if (preview.Layout == "Defence ERP") { html.Append("<li>Detail reconciles to company and grand-total controls: Pass</li>"); }
            html.Append("<li>Exact-zero balance: ").Append(!preview.TotalsAvailable ? "Not run" : preview.Net == 0m ? "Pass" : "Fail").Append("</li>");
            html.Append("<li>Lossless decimal storage: ").Append(preview.PrecisionValid ? "Pass" : "Fail").Append("</li></ul>");
            foreach (string error in preview.Errors) { html.Append("<p>").Append(HttpUtility.HtmlEncode(error)).Append("</p>"); }
            foreach (string warning in preview.Warnings) { html.Append("<p>").Append(HttpUtility.HtmlEncode(warning)).Append("</p>"); }
            int replacements = 0;
            foreach (NORMSourceScope scope in preview.SourceScopes)
            {
                if (scope.ImportId == 0) { continue; }
                if (replacements == 0) { html.Append("<h3>Existing trial balance will be replaced</h3>"); }
                replacements++;
                string scopeName = "Company " + scope.Company + ", Ledger " + (scope.Ledger ?? "Not applicable") + ", " + scope.ReportingStream;
                html.Append("<p class=\"norm-import-existing-source\">").Append(HttpUtility.HtmlEncode(
                    preview.FinancialYear.ToString(CultureInfo.InvariantCulture) + " figures for " + scopeName +
                    " already exist. They were imported on " + scope.ImportedUtc.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) +
                    " from " + scope.SourceFileName + ".")).Append("</p>");
            }
            return replacements;
        }
        protected void AgencyList_Changed(object sender, EventArgs e)
        {
            ClearPreview();
            ShowAgencyContext();
        }

        private void BindAgencies()
        {
            IList<string> agencies = NORMImportService.ImportAgencies();
            AgencyList.Items.Clear();
            AgencySelector.Visible = agencies.Count > 1;
            if (agencies.Count > 1) { AgencyList.Items.Add(new ListItem("Select an agency", "")); }
            foreach (string agency in agencies) { AgencyList.Items.Add(new ListItem(agency, agency)); }
            ImportButton.Enabled = agencies.Count > 0;
        }

        private void ShowAgencyContext()
        {
            AgencyContext.Text = "";
            if (String.IsNullOrEmpty(AgencyList.SelectedValue)) { return; }
            try
            {
                string agency = AgencyList.SelectedValue;
                int year = NORMImportService.ConfiguredCurrentYear(agency).Value;
                string name = Convert.ToString(NORMHelper.Scalar(
                    "SELECT EntityName FROM dbo.tblNORM_ReportingEntity WHERE EntityCode=@entity AND FinancialYear=@fy AND IsDeactivated=0",
                    NORMHelper.P("@entity", agency), NORMHelper.P("@fy", year)));
                StringBuilder context = new StringBuilder("<p>").Append(HttpUtility.HtmlEncode(String.IsNullOrEmpty(name) ? agency : name))
                    .Append(" · Reporting FY").Append(year).Append("</p>");
                IList<NORMSourceScope> missing = NORMImportService.MissingAnnualRequiredPopulations(agency);
                if (missing.Count > 0)
                {
                    context.Append("<p>Missing required sources</p><ul>");
                    foreach (NORMSourceScope scope in missing)
                    {
                        context.Append("<li>FY").Append(scope.FinancialYear).Append(" / ")
                            .Append(HttpUtility.HtmlEncode(scope.Company + " / " + (scope.Ledger ?? "No ledger") + " / " + scope.ReportingStream))
                            .Append("</li>");
                    }
                    context.Append("</ul>");
                }
                AgencyContext.Text = context.ToString();
            }
            catch (Exception error) { ShowError(NORMErrorHandling.UserMessage(error, HttpContext.Current, "ImportInspection")); }
        }
    }
}
