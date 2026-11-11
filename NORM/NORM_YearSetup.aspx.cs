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

        protected override void OnInit(EventArgs e)
        {
            // Bind WebForms' signed state to the authenticated session for setup writes.
            ViewStateUserKey = Session.SessionID + ":" + NORMHelper.CurrentUserId();
            base.OnInit(e);
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            bool installed = NORMStartOfYearSetup.IsInstalled();
            InstallPanel.Visible = !installed;
            SaveYearButton.Enabled = UploadPriorButton.Enabled = UploadBudgetButton.Enabled = SaveNameButton.Enabled = installed;
            if (!installed || IsPostBack) return;
            try
            {
                EntitySelection.Items.Clear();
                foreach (DataRow row in NORMStartOfYearSetup.Entities().Rows)
                    EntitySelection.Items.Add(new ListItem(NORMHelper.Str(row, "EntityCode"), NORMHelper.Str(row, "EntityCode")));
                if (EntitySelection.Items.Count == 0) throw new UnauthorizedAccessException();
                LoadEntity();
            }
            catch (Exception error) { ShowError(error); }
        }

        protected void EntitySelection_Changed(object sender, EventArgs e)
        {
            ClearMessages();
            try { LoadEntity(); } catch (Exception error) { ShowError(error); }
        }

        protected void SetupSelection_Changed(object sender, EventArgs e)
        {
            ClearMessages();
            try { BuildDisplay(); } catch (Exception error) { ShowError(error); }
        }

        private void LoadEntity()
        {
            string entity = EntityCode();
            DataTable current = NORMStartOfYearSetup.CurrentDesignation(entity);
            if (current.Rows.Count > 1) throw new InvalidOperationException("The current year designation is ambiguous.");
            ViewState["Entity"] = entity;
            ViewState["CurrentId"] = current.Rows.Count == 0 ? 0 : NORMHelper.Int(current.Rows[0], "YearSetupId");
            ViewState["CurrentUpdated"] = current.Rows.Count == 0 ? null : (object)Convert.ToDateTime(current.Rows[0]["UpdatedUtc"]);
            CurrentFinancialYear.Text = current.Rows.Count == 0 ? "" : Convert.ToString(current.Rows[0]["CurrentFinancialYear"]);
            CurrentYearDisplay = CurrentFinancialYear.Text.Length == 0 ? "—" : CurrentFinancialYear.Text;
            PriorYearDisplay = current.Rows.Count == 0 ? "—" : (NORMHelper.Int(current.Rows[0], "CurrentFinancialYear") - 1).ToString();
            ViewState["CurrentDisplay"] = CurrentYearDisplay;
            ViewState["PriorDisplay"] = PriorYearDisplay;
            SetupSelection.Items.Clear();
            foreach (DataRow row in NORMStartOfYearSetup.Setups(entity).Rows)
            {
                ListItem item = new ListItem("FY" + NORMHelper.Int(row, "CurrentFinancialYear") +
                    (Convert.ToBoolean(row["IsCurrent"]) ? " (current)" : " (historical)"), NORMHelper.Int(row, "YearSetupId").ToString());
                SetupSelection.Items.Add(item);
            }
            ListItem selected = SetupSelection.Items.FindByValue(Convert.ToString(ViewState["CurrentId"]));
            if (selected != null) { SetupSelection.ClearSelection(); selected.Selected = true; }
            BuildDisplay();
        }

        protected void SaveYearButton_Click(object sender, EventArgs e)
        {
            ClearMessages();
            try
            {
                CheckEntityState();
                int year = ReadYear();
                NORMStartOfYearSetup.SaveYear(EntityCode(), year, Convert.ToInt32(ViewState["CurrentId"]),
                    ViewState["CurrentUpdated"] == null ? (DateTime?)null : (DateTime)ViewState["CurrentUpdated"], NORMHelper.CurrentUserId());
                LoadEntity();
                ShowMessage("<strong>Financial year saved.</strong><span>FY" + year + " is the configured current year. Historical runs retain their reporting year.</span>");
            }
            catch (Exception error) { ShowError(error); }
        }

        protected void SaveNameButton_Click(object sender, EventArgs e)
        {
            ClearMessages();
            try
            {
                int id = SelectedSetup();
                NORMStartOfYearSetup.SaveReportingName(id, ReportingName.Text, (string)ViewState["Name"], NORMHelper.CurrentUserId());
                BuildDisplay();
                ShowMessage("<strong>Reporting name saved.</strong><span>New calculation runs use this name. Existing runs retain their recorded name.</span>");
            }
            catch (Exception error) { ShowError(error); }
        }

        protected void UploadPriorButton_Click(object sender, EventArgs e) { Upload(PriorYearFile, PriorYearStartPage, NORMStartOfYearSetup.PriorDocumentType); }
        protected void UploadBudgetButton_Click(object sender, EventArgs e) { Upload(BudgetFile, BudgetStartPage, NORMStartOfYearSetup.BudgetDocumentType); }

        private void Upload(FileUpload upload, TextBox startPageInput, string documentType)
        {
            ClearMessages();
            try
            {
                int setupId = SelectedSetup();
                if (!upload.HasFile) throw new InvalidDataException("Choose a source document.");
                NORMStartOfYearSetup.UploadOutcome outcome = NORMStartOfYearSetup.Upload(setupId, documentType,
                    upload.FileBytes, upload.FileName, ReadStartPage(upload, startPageInput), Convert.ToInt64(ViewState[documentType]),
                    (DateTime)ViewState["SetupUpdated"], NORMHelper.CurrentUserId());
                BuildDisplay();
                ShowMessage("<strong>Source document retained and scanned.</strong><span>" + Enc(outcome.Detail) + "</span>");
            }
            catch (Exception error) { ShowError(error); }
        }

        private void CheckEntityState()
        {
            if (!String.Equals((string)ViewState["Entity"], EntityCode(), StringComparison.Ordinal))
                throw new InvalidOperationException("Reload the selected entity before saving.");
        }

        private int SelectedSetup()
        {
            CheckEntityState();
            int id;
            if (!Int32.TryParse(SetupSelection.SelectedValue, out id) || id <= 0 || Convert.ToInt32(ViewState["SetupId"]) != id)
                throw new InvalidOperationException("Select and load a saved year setup first.");
            DataRow setup = NORMStartOfYearSetup.Setup(id);
            if (NORMHelper.Str(setup, "EntityCode") != EntityCode()) throw new UnauthorizedAccessException();
            return id;
        }

        private static int? ReadStartPage(FileUpload upload, TextBox input)
        {
            string value = (input.Text ?? "").Trim();
            bool isPdf = String.Equals(Path.GetExtension(upload.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);
            if (value.Length == 0)
            {
                if (isPdf) throw new InvalidDataException("Enter the PDF page where the financial-statement tables commence.");
                return null;
            }
            int page;
            if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1 || page > 9999)
                throw new InvalidDataException("Enter a valid PDF start page between 1 and 9999.");
            return page;
        }

        private int ReadYear()
        {
            string text = (CurrentFinancialYear.Text ?? "").Trim();
            int year;
            if (!Regex.IsMatch(text, "^[0-9]{4}$") || !Int32.TryParse(text, out year) || year < 1900 || year > 2999)
                throw new InvalidDataException("Enter the financial year as exactly four digits, for example 2025.");
            return year;
        }

        protected override void OnPreRender(EventArgs e)
        {
            if (InstallPanel.Visible) { base.OnPreRender(e); return; }
            try
            {
                EntityCode();
                PriorDocumentHtml = Convert.ToString(ViewState["PriorHtml"]);
                BudgetDocumentHtml = Convert.ToString(ViewState["BudgetHtml"]);
                FigurePreviewHtml = Convert.ToString(ViewState["FiguresHtml"]);
            }
            catch (Exception error)
            {
                PriorDocumentHtml = BudgetDocumentHtml = FigurePreviewHtml = "";
                ReportingName.Text = ""; CurrentFinancialYear.Text = "";
                SetupSelection.Items.Clear(); EntitySelection.Items.Clear(); ViewState.Clear();
                SaveYearButton.Enabled = SaveNameButton.Enabled = UploadPriorButton.Enabled = UploadBudgetButton.Enabled = false;
                ShowError(error);
            }
            CurrentYearDisplay = Convert.ToString(ViewState["CurrentDisplay"] ?? "—");
            PriorYearDisplay = Convert.ToString(ViewState["PriorDisplay"] ?? "—");
            base.OnPreRender(e);
        }

        private void BuildDisplay()
        {
            CheckEntityState();
            PriorDocumentHtml = BudgetDocumentHtml = FigurePreviewHtml = "";
            int setupId;
            bool selected = Int32.TryParse(SetupSelection.SelectedValue, out setupId) && setupId > 0;
            UploadPriorButton.Enabled = UploadBudgetButton.Enabled = SaveNameButton.Enabled = selected;
            if (!selected) { ReportingName.Text = ""; ViewState["PriorHtml"] = ViewState["BudgetHtml"] = ViewState["FiguresHtml"] = ""; return; }
            DataRow setup = NORMStartOfYearSetup.Setup(setupId);
            if (NORMHelper.Str(setup, "EntityCode") != EntityCode()) throw new UnauthorizedAccessException();
            ViewState["SetupId"] = setupId;
            ViewState["SetupUpdated"] = Convert.ToDateTime(setup["UpdatedUtc"]);
            ReportingName.Text = NORMStartOfYearSetup.ReportingName(setupId);
            ViewState["Name"] = ReportingName.Text;
            DataTable documents = NORMStartOfYearSetup.LoadDocuments(setupId);
            PriorDocumentHtml = BuildDocumentStatus(documents, NORMStartOfYearSetup.PriorDocumentType);
            BudgetDocumentHtml = BuildDocumentStatus(documents, NORMStartOfYearSetup.BudgetDocumentType);
            FigurePreviewHtml = BuildFigurePreview(NORMStartOfYearSetup.LoadFigures(setupId));
            ViewState["PriorHtml"] = PriorDocumentHtml; ViewState["BudgetHtml"] = BudgetDocumentHtml; ViewState["FiguresHtml"] = FigurePreviewHtml;
        }

        private string BuildDocumentStatus(DataTable documents, string type)
        {
            StringBuilder html = new StringBuilder();
            ViewState[type] = 0L;
            foreach (DataRow row in documents.Rows)
            {
                if (!String.Equals(NORMHelper.Str(row, "DocumentTypeCode"), type, StringComparison.Ordinal)) continue;
                bool superseded = Convert.ToBoolean(row["IsDeactivated"]);
                long id = NORMHelper.Long(row, "YearSetupDocumentId");
                if (!superseded) ViewState[type] = id;
                html.Append("<div class=\"norm-year-document-status\"><strong>").Append(Enc(NORMHelper.Str(row, "SourceFileName")))
                    .Append("</strong><small>Document #").Append(id).Append(superseded ? " · Superseded · " : " · Current · ")
                    .Append(Enc(NORMHelper.Str(row, "ExtractionStatus"))).Append(" · ")
                    .Append(NORMHelper.Long(row, "SourceFileBytes")).Append(" bytes</small><p>")
                    .Append(Enc(NORMHelper.Str(row, "ExtractionDetail"))).Append("</p><small>SHA-256 ")
                    .Append(Enc(NORMHelper.Str(row, "SourceFileHash"))).Append("</small><p><a href=\"NORM_YearSetupDocument.ashx?document=")
                    .Append(id).Append("\">Download original</a></p></div>");
            }
            return html.Length == 0 ? "<p>No source document retained for this selected year.</p>" : html.ToString();
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
            string entity = EntitySelection.SelectedValue;
            NORMHelper.RequireEntityAccess(entity);
            return entity;
        }

        private static string StatementName(string code)
        {
            switch (code) { case "SOCI": return "Comprehensive income"; case "SOFP": return "Financial position"; case "SOCE": return "Changes in equity"; case "CASH": return "Cash flow"; default: return code; }
        }

        private void ShowMessage(string html) { MessagePanel.Visible = true; MessageText.Text = html; }
        private void ShowError(Exception error)
        {
            if (error is UnauthorizedAccessException) { Response.StatusCode = 403; Response.TrySkipIisCustomErrors = true; }
            string message = error is UnauthorizedAccessException ? "You do not have access to this entity's Year Setup." :
                (error is InvalidDataException || error is InvalidOperationException || error is ArgumentException) ? error.Message :
                "Year Setup could not complete this operation. Reload and try again, or contact the administrator.";
            ErrorPanel.Visible = true; ErrorText.Text = "<span>" + Enc(message) + "</span>";
        }
        private void ClearMessages() { MessagePanel.Visible = false; ErrorPanel.Visible = false; }
        private static string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
