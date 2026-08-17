using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    public partial class NORM_YearSetup : NORMBasePage
    {
        protected string CurrentYearDisplay = "—";
        protected string PriorYearDisplay = "—";
        protected string PriorDocumentHtml = "";
        protected string BudgetDocumentHtml = "";
        protected string FigurePreviewHtml = "";

        protected void Page_Load(object sender, EventArgs e)
        {
            bool installed = NORMStartOfYearSetup.IsInstalled();
            InstallPanel.Visible = !installed;
            SaveYearButton.Enabled = installed;
            UploadPriorButton.Enabled = installed;
            UploadBudgetButton.Enabled = installed;
            if (!installed) { return; }
            if (!IsPostBack)
            {
                CurrentFinancialYear.Text = NORMStartOfYearSetup.DefaultFinancialYear(EntityCode()).ToString(CultureInfo.InvariantCulture);
            }
            BuildDisplay();
        }

        protected void SaveYearButton_Click(object sender, EventArgs e)
        {
            ClearMessages();
            try
            {
                int year = ReadYear();
                NORMStartOfYearSetup.SaveYear(EntityCode(), year, NORMHelper.CurrentUserId());
                ShowMessage("<strong>Financial year saved.</strong><span>FY" + year.ToString(CultureInfo.InvariantCulture) +
                    " now drives the statement headings; FY" + (year - 1).ToString(CultureInfo.InvariantCulture) + " is the comparative year.</span>");
                BuildDisplay();
            }
            catch (Exception error) { ShowError(error); }
        }

        protected void UploadPriorButton_Click(object sender, EventArgs e)
        {
            Upload(PriorYearFile, NORMStartOfYearSetup.PriorDocumentType);
        }

        protected void UploadBudgetButton_Click(object sender, EventArgs e)
        {
            Upload(BudgetFile, NORMStartOfYearSetup.BudgetDocumentType);
        }

        private void Upload(FileUpload upload, string documentType)
        {
            ClearMessages();
            try
            {
                int year = ReadYear();
                int setupId = NORMStartOfYearSetup.SaveYear(EntityCode(), year, NORMHelper.CurrentUserId());
                if (!upload.HasFile) throw new InvalidDataException("Choose the " +
                    (documentType == NORMStartOfYearSetup.PriorDocumentType ? "Prior Year Financial Statements" : "Portfolio Budget Statements") + " document.");
                NORMStartOfYearSetup.UploadOutcome outcome = NORMStartOfYearSetup.Upload(setupId, documentType,
                    upload.FileBytes, upload.FileName, NORMHelper.CurrentUserId());
                string label = documentType == NORMStartOfYearSetup.PriorDocumentType ? "Prior Year Financial Statements" : "Portfolio Budget Statements";
                ShowMessage("<strong>" + Enc(label) + " retained and scanned.</strong><span>" + Enc(outcome.Detail) + "</span>");
                BuildDisplay();
            }
            catch (Exception error) { ShowError(error); BuildDisplay(); }
        }

        private int ReadYear()
        {
            string text = (CurrentFinancialYear.Text ?? "").Trim();
            int year;
            if (!Regex.IsMatch(text, "^[0-9]{4}$") || !Int32.TryParse(text, out year) || year < 1900 || year > 2999)
                throw new InvalidDataException("Enter the financial year as exactly four digits, for example 2025.");
            return year;
        }

        private void BuildDisplay()
        {
            int year;
            if (!Int32.TryParse((CurrentFinancialYear.Text ?? "").Trim(), out year)) year = NORMStartOfYearSetup.DefaultFinancialYear(EntityCode());
            CurrentYearDisplay = year.ToString(CultureInfo.InvariantCulture);
            PriorYearDisplay = (year - 1).ToString(CultureInfo.InvariantCulture);
            int setupId = NORMStartOfYearSetup.CurrentSetupId(EntityCode());
            DataTable documents = NORMStartOfYearSetup.LoadDocuments(setupId);
            PriorDocumentHtml = BuildDocumentStatus(documents, NORMStartOfYearSetup.PriorDocumentType);
            BudgetDocumentHtml = BuildDocumentStatus(documents, NORMStartOfYearSetup.BudgetDocumentType);
            FigurePreviewHtml = BuildFigurePreview(NORMStartOfYearSetup.LoadFigures(setupId));
        }

        private string BuildDocumentStatus(DataTable documents, string type)
        {
            DataRow row = null;
            for (int i = 0; i < documents.Rows.Count; i++)
                if (String.Equals(NORMHelper.Str(documents.Rows[i], "DocumentTypeCode"), type, StringComparison.OrdinalIgnoreCase)) { row = documents.Rows[i]; break; }
            if (row == null) return "<div class=\"norm-year-document-empty\"><span>Not loaded</span><small>No controlled source document has been retained for this financial year.</small></div>";
            string status = NORMHelper.Str(row, "ExtractionStatus");
            string css = status == "Extracted" ? "pass" : (status == "Failed" ? "fail" : "warn");
            long bytes = NORMHelper.Long(row, "SourceFileBytes");
            string size = bytes >= 1048576 ? (bytes / 1048576m).ToString("N1") + " MB" : (bytes / 1024m).ToString("N0") + " KB";
            return "<div class=\"norm-year-document-status " + css + "\"><div><span class=\"norm-status " + css + "\"></span><strong>" +
                Enc(NORMHelper.Str(row, "SourceFileName")) + "</strong></div><small>" + Enc(status.Replace("Required", " required")) + " · " +
                NORMHelper.Int(row, "ExtractedFigureCount").ToString("N0") + " figures · " + Enc(size) + "</small><p>" +
                Enc(NORMHelper.Str(row, "ExtractionDetail")) + "</p><em>SHA-256 " + Enc((NORMHelper.Str(row, "SourceFileHash") ?? "").Substring(0, 12)) + "…</em></div>";
        }

        private string BuildFigurePreview(DataTable figures)
        {
            if (figures.Rows.Count == 0)
                return "<div class=\"norm-year-figure-empty\"><strong>No figures extracted yet</strong><p>Load one or both source documents. NORM will scan the entire file and show every automatically mapped figure here.</p></div>";
            StringBuilder html = new StringBuilder("<div class=\"norm-year-figure-summary\"><strong>");
            html.Append(figures.Rows.Count.ToString("N0")).Append("</strong><span>high-confidence figures linked to retained source locations</span></div>");
            html.Append("<div class=\"norm-workflow-table norm-year-figure-table\"><table><thead><tr><th>Source</th><th>Statement</th><th>Mapped line</th><th>Figure</th><th>Locator</th><th>Confidence</th></tr></thead><tbody>");
            for (int i = 0; i < figures.Rows.Count; i++)
            {
                DataRow row = figures.Rows[i];
                html.Append("<tr><td><small>").Append(Enc(NORMHelper.Str(row, "FigureType") == "PriorActual" ? "Comparative" : "Original Budget"))
                    .Append("</small><strong>").Append(Enc(NORMHelper.Str(row, "SourceFileName"))).Append("</strong></td><td>")
                    .Append(Enc(StatementName(NORMHelper.Str(row, "StatementCode")))).Append("</td><td><small>")
                    .Append(Enc(NORMHelper.Str(row, "LineCode"))).Append("</small><strong>").Append(Enc(NORMHelper.Str(row, "LineLabel")))
                    .Append("</strong></td><td class=\"norm-amount\">").Append(NORMHelper.Dec(row, "Amount").ToString("N0"))
                    .Append("</td><td>").Append(Enc(NORMHelper.Str(row, "SourceLocator"))).Append("</td><td><span class=\"norm-chip\">")
                    .Append(NORMHelper.Dec(row, "MatchConfidence").ToString("N0")).Append("%</span></td></tr>");
            }
            html.Append("</tbody></table></div>");
            return html.ToString();
        }

        private string EntityCode()
        {
            object value = NORMHelper.Scalar("SELECT TOP 1 EntityCode FROM dbo.tblNORM_ConfigurationRelease WHERE StatusCode='Approved' AND IsDeactivated=0 ORDER BY FinancialYear DESC,ConfigurationReleaseId DESC");
            return value == null ? "DEPT" : Convert.ToString(value);
        }

        private static string StatementName(string code)
        {
            switch (code) { case "SOCI": return "Comprehensive income"; case "SOFP": return "Financial position"; case "SOCE": return "Changes in equity"; case "CASH": return "Cash flow"; default: return code; }
        }

        private void ShowMessage(string html) { MessagePanel.Visible = true; MessageText.Text = html; }
        private void ShowError(Exception error) { ErrorPanel.Visible = true; ErrorText.Text = "<span>" + Enc(error.Message) + "</span>"; }
        private void ClearMessages() { MessagePanel.Visible = false; ErrorPanel.Visible = false; }
        private static string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
