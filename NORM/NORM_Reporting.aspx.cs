using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;

namespace CPlatform.NORM
{
    public partial class NORM_Reporting : NORMBasePage
    {
        protected bool PlatformInstalled;
        protected int SelectedRunId;
        protected int SelectedReleaseId;
        protected string RunLabel = "No completed run";
        protected string EntityTypeOptions = "";
        protected string ReportingBasisOptions = "";
        protected string DisclosureTierOptions = "";
        protected string MaterialityBasis = "";
        protected string CapabilityHtml = "";
        protected string DisclosureHtml = "";
        protected string NarrativeHtml = "";
        protected string WorkflowHtml = "";
        protected string BudgetFigureHtml = "";
        protected string ManualInputHtml = "";
        protected string CashFlowJournalHtml = "";
        protected bool EnhancementsInstalled;
        protected int RequiredCount;
        protected int GeneratedCount;
        protected int NeedsInputCount;
        protected int NotApplicableCount;
        protected string ReadinessLabel = "Select a completed calculation run";

        protected void Page_Load(object sender, EventArgs e)
        {
            SelectedRunId = ResolveRunId();
            PlatformInstalled = NORMReportingFramework.IsInstalled();
            if (SelectedRunId <= 0 || !PlatformInstalled) { return; }
            LoadContext();
            EnhancementsInstalled = NORMStatementEnhancements.IsInstalled();
            NORMStatementEnhancements.EnsureRunTemplates(SelectedRunId, NORMHelper.CurrentUserId());
            NORMStatementEnhancements.EnsureBudgetTemplates(SelectedRunId, SelectedReleaseId, NORMHelper.CurrentUserId());
            NORMReportingFramework.EnsureWorkflow(SelectedRunId, NORMHelper.CurrentUserId());
            BuildPage();
        }

        protected void Save_Click(object sender, EventArgs e)
        {
            SelectedRunId = ResolveRunId();
            if (SelectedRunId <= 0) { throw new InvalidOperationException("Choose a completed calculation run first."); }
            LoadContext();
            Dictionary<string, bool> requirements = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<NORMReportingFramework.CapabilityDefinition> capabilities = NORMReportingFramework.CapabilityDefinitions();
            for (int i = 0; i < capabilities.Count; i++)
            {
                string code = capabilities[i].Code;
                requirements[code] = String.Equals(Request.Form["cap_" + code], "1", StringComparison.Ordinal);
            }
            string user = NORMHelper.CurrentUserId();
            NORMReportingFramework.SaveProfile(SelectedReleaseId, Request.Form["entityType"], Request.Form["reportingBasis"],
                Request.Form["disclosureTier"], Request.Form["materialityBasis"], requirements, user);

            NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(SelectedReleaseId);
            List<NORMReportingFramework.Disclosure> disclosures =
                NORMReportingFramework.LoadDisclosures(SelectedRunId, SelectedReleaseId, profile);
            for (int i = 0; i < disclosures.Count; i++)
            {
                NORMReportingFramework.Disclosure item = disclosures[i];
                string textKey = "narrative_" + item.Code;
                if (Request.Form[textKey] == null) { continue; }
                string type = String.IsNullOrWhiteSpace(item.NarrativeType) ? "AccountingPolicy" : item.NarrativeType;
                NORMReportingFramework.SaveNarrative(SelectedRunId, item.Code, type, Request.Form[textKey],
                    Request.Form["narrativeStatus_" + item.Code], user);
            }

            DataTable workflow = NORMReportingFramework.LoadWorkflow(SelectedRunId);
            for (int i = 0; i < workflow.Rows.Count; i++)
            {
                long id = NORMHelper.Long(workflow.Rows[i], "WorkflowItemId");
                string key = id.ToString();
                NORMReportingFramework.SaveWorkflowItem(id, SelectedRunId, Request.Form["workflowOwner_" + key],
                    Request.Form["workflowReviewer_" + key], Request.Form["workflowStatus_" + key],
                    Request.Form["workflowComment_" + key], user);
            }

            if (NORMStatementEnhancements.IsInstalled())
            {
                DataTable budgetFigures = NORMStatementEnhancements.LoadBudgetRegister(SelectedRunId);
                for (int i = 0; i < budgetFigures.Rows.Count; i++)
                {
                    long id = NORMHelper.Long(budgetFigures.Rows[i], "BudgetFigureId");
                    string key = id.ToString(CultureInfo.InvariantCulture);
                    NORMStatementEnhancements.SaveBudgetFigure(id, SelectedRunId,
                        DecimalValue(Request.Form["budgetAmount_" + key]), Request.Form["budgetSystem_" + key],
                        Request.Form["budgetReference_" + key], Request.Form["budgetStatus_" + key], user);
                }
                DataTable manualInputs = NORMStatementEnhancements.LoadManualInputs(SelectedRunId);
                for (int i = 0; i < manualInputs.Rows.Count; i++)
                {
                    long id = NORMHelper.Long(manualInputs.Rows[i], "ManualInputId");
                    string key = id.ToString(CultureInfo.InvariantCulture);
                    NORMStatementEnhancements.SaveManualInput(id, SelectedRunId,
                        DecimalValue(Request.Form["manualCurrent_" + key]),
                        DecimalValue(Request.Form["manualPrior_" + key]),
                        Request.Form["manualEvidence_" + key], Request.Form["manualCommentary_" + key],
                        Request.Form["manualStatus_" + key], user);
                }

                DataTable journals = NORMStatementEnhancements.LoadCashFlowJournals(SelectedRunId);
                for (int i = 0; i < journals.Rows.Count; i++)
                {
                    long id = NORMHelper.Long(journals.Rows[i], "CashFlowJournalId");
                    string key = id.ToString(CultureInfo.InvariantCulture);
                    NORMStatementEnhancements.SaveCashFlowJournal(id, SelectedRunId,
                        Request.Form["cfReference_" + key], Request.Form["cfDescription_" + key],
                        Request.Form["cfClass_" + key], DecimalValue(Request.Form["cfAmount_" + key]) ?? 0m,
                        Request.Form["cfEvidence_" + key], Request.Form["cfStatus_" + key], user);
                }
                if (!String.IsNullOrWhiteSpace(Request.Form["cfReference_new"]))
                {
                    NORMStatementEnhancements.SaveCashFlowJournal(0, SelectedRunId,
                        Request.Form["cfReference_new"], Request.Form["cfDescription_new"],
                        Request.Form["cfClass_new"], DecimalValue(Request.Form["cfAmount_new"]) ?? 0m,
                        Request.Form["cfEvidence_new"], Request.Form["cfStatus_new"], user);
                }
            }
            Response.Redirect("NORM_Reporting.aspx?run=" + SelectedRunId.ToString() + "&saved=1", true);
        }

        private int ResolveRunId()
        {
            int requested;
            if (Int32.TryParse(Request.QueryString["run"], out requested) && requested > 0)
            {
                object exists = NORMHelper.Scalar(
                    "SELECT COUNT(1) FROM dbo.tblNORM_CalculationRun WHERE CalculationRunId=@run AND StatusCode='Complete' AND IsDeactivated=0",
                    NORMHelper.P("@run", requested));
                if (exists != null && Convert.ToInt32(exists) > 0) { return requested; }
            }
            object latest = NORMHelper.Scalar(
                "SELECT TOP 1 r.CalculationRunId FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId " +
                "WHERE r.StatusCode='Complete' AND r.IsDeactivated=0 AND i.IsDeactivated=0 AND i.IsTestBreak=0 " +
                "ORDER BY r.CalculationRunId DESC");
            return latest == null ? 0 : Convert.ToInt32(latest);
        }

        private void LoadContext()
        {
            DataTable context = NORMHelper.Query(
                "SELECT r.ConfigurationReleaseId,i.FinancialYear,i.EntityCode,e.EntityName,c.VersionCode " +
                "FROM dbo.tblNORM_CalculationRun r INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=r.ConfigurationReleaseId " +
                "LEFT JOIN dbo.tblNORM_ReportingEntity e ON e.FinancialYear=i.FinancialYear AND e.EntityCode=i.EntityCode AND e.IsDeactivated=0 " +
                "WHERE r.CalculationRunId=@run AND r.StatusCode='Complete' AND r.IsDeactivated=0",
                NORMHelper.P("@run", SelectedRunId));
            if (context.Rows.Count == 0) { throw new InvalidOperationException("The calculation run is not available."); }
            SelectedReleaseId = NORMHelper.Int(context.Rows[0], "ConfigurationReleaseId");
            string entity = NORMHelper.Str(context.Rows[0], "EntityName") ?? NORMHelper.Str(context.Rows[0], "EntityCode");
            RunLabel = entity + " · FY" + NORMHelper.Int(context.Rows[0], "FinancialYear").ToString() + " · Run #" + SelectedRunId.ToString();
        }

        private void BuildPage()
        {
            NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(SelectedReleaseId);
            MaterialityBasis = profile.MaterialityBasis;
            EntityTypeOptions = Options(new string[,] { { "NCE", "Non-corporate Commonwealth entity" }, { "CCE", "Corporate Commonwealth entity" }, { "COMMONWEALTH_COMPANY", "Commonwealth company" } }, profile.EntityType);
            ReportingBasisOptions = Options(new string[,] { { "GPFS", "General purpose financial statements" }, { "SPFS", "Special purpose financial statements" } }, profile.ReportingBasis);
            DisclosureTierOptions = Options(new string[,] { { "FULL", "Full disclosures" }, { "REDUCED", "Reduced disclosures (where permitted)" } }, profile.DisclosureTier);
            CapabilityHtml = BuildCapabilities(profile);
            List<NORMReportingFramework.Disclosure> disclosures =
                NORMReportingFramework.LoadDisclosures(SelectedRunId, SelectedReleaseId, profile);
            DisclosureHtml = BuildDisclosures(disclosures);
            NarrativeHtml = BuildNarratives(disclosures);
            BudgetFigureHtml = BuildBudgetFigures(NORMStatementEnhancements.LoadBudgetRegister(SelectedRunId));
            ManualInputHtml = BuildManualInputs(NORMStatementEnhancements.LoadManualInputs(SelectedRunId));
            CashFlowJournalHtml = BuildCashFlowJournals(NORMStatementEnhancements.LoadCashFlowJournals(SelectedRunId));
            WorkflowHtml = BuildWorkflow(NORMReportingFramework.LoadWorkflow(SelectedRunId));
            ReadinessLabel = NeedsInputCount == 0 ? "Disclosure plan is ready for review" : NeedsInputCount.ToString() + " required items need input";
        }

        private string BuildCapabilities(NORMReportingFramework.ReportingProfile profile)
        {
            StringBuilder html = new StringBuilder();
            List<NORMReportingFramework.CapabilityDefinition> values = NORMReportingFramework.CapabilityDefinitions();
            for (int i = 0; i < values.Count; i++)
            {
                NORMReportingFramework.CapabilityDefinition item = values[i];
                bool selected = profile.Requirements.ContainsKey(item.Code) && profile.Requirements[item.Code];
                html.Append("<label class=\"norm-capability\"><input type=\"checkbox\" name=\"cap_")
                    .Append(Enc(item.Code)).Append("\" value=\"1\"")
                    .Append(selected ? " checked" : "").Append("><span><strong>")
                    .Append(Enc(item.Label)).Append("</strong><small>").Append(Enc(item.Detail))
                    .Append("</small></span><i></i></label>");
            }
            return html.ToString();
        }

        private string BuildDisclosures(List<NORMReportingFramework.Disclosure> values)
        {
            StringBuilder html = new StringBuilder();
            string section = null;
            for (int i = 0; i < values.Count; i++)
            {
                NORMReportingFramework.Disclosure item = values[i];
                if (!String.Equals(section, item.SectionTitle, StringComparison.Ordinal))
                {
                    if (section != null) { html.Append("</div></section>"); }
                    section = item.SectionTitle;
                    html.Append("<section><h3>").Append(Enc(section)).Append("</h3><div>");
                }
                string statusClass = CssStatus(item.CompletionStatus);
                html.Append("<article class=\"norm-disclosure-row ").Append(item.Required ? "required" : "not-applicable").Append("\">")
                    .Append("<span class=\"norm-disclosure-ref\">").Append(Enc(item.NoteRef ?? item.SectionCode)).Append("</span><div><strong>")
                    .Append(Enc(item.Title)).Append("</strong><small>").Append(Enc(item.Guidance)).Append("</small></div>")
                    .Append("<span class=\"norm-disclosure-trigger\">").Append(item.Required ? "Required" : "Not applicable").Append("</span>")
                    .Append("<span class=\"norm-disclosure-status ").Append(statusClass).Append("\">").Append(Enc(item.CompletionStatus)).Append("</span></article>");
                if (item.Required)
                {
                    RequiredCount++;
                    if (item.CompletionStatus == "Needs input") { NeedsInputCount++; }
                    else { GeneratedCount++; }
                }
                else { NotApplicableCount++; }
            }
            if (section != null) { html.Append("</div></section>"); }
            return html.ToString();
        }

        private string BuildNarratives(List<NORMReportingFramework.Disclosure> values)
        {
            StringBuilder html = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                NORMReportingFramework.Disclosure item = values[i];
                if (!item.Required || !item.RequiresNarrative) { continue; }
                string narrative = item.Narrative ?? "";
                html.Append("<details class=\"norm-policy-card\"")
                    .Append(item.Code == "OVERVIEW" || item.CompletionStatus == "Needs input" ? " open" : "")
                    .Append("><summary><span><b>").Append(Enc(item.NoteRef ?? item.SectionCode)).Append("</b><strong>")
                    .Append(Enc(item.Title)).Append("</strong></span><em class=\"").Append(CssStatus(item.CompletionStatus)).Append("\">")
                    .Append(Enc(item.CompletionStatus)).Append("</em></summary><div><label><span>Entity wording</span><textarea rows=\"7\" name=\"narrative_")
                    .Append(Enc(item.Code)).Append("\">").Append(Enc(narrative)).Append("</textarea></label>")
                    .Append("<label class=\"norm-policy-status\"><span>Preparation status</span><select name=\"narrativeStatus_")
                    .Append(Enc(item.Code)).Append("\">").Append(StatusOptions(item.NarrativeStatus)).Append("</select></label>")
                    .Append("<p>").Append(Enc(item.Guidance)).Append("</p></div></details>");
            }
            return html.Length == 0 ? "<div class=\"norm-empty\">No narrative disclosures are required by the selected profile.</div>" : html.ToString();
        }

        private string BuildWorkflow(DataTable table)
        {
            if (table.Rows.Count == 0) { return "<div class=\"norm-empty\">Workflow items will appear for the selected run.</div>"; }
            StringBuilder html = new StringBuilder("<table><thead><tr><th>Module and deliverable</th><th>Preparer</th><th>Reviewer</th><th>Status</th><th>Working note</th></tr></thead><tbody>");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                string id = NORMHelper.Long(row, "WorkflowItemId").ToString();
                html.Append("<tr><td><small>").Append(Enc(NORMHelper.Str(row, "ModuleCode").Replace("_", " "))).Append("</small><strong>")
                    .Append(Enc(NORMHelper.Str(row, "ItemLabel"))).Append("</strong></td><td><input name=\"workflowOwner_").Append(id)
                    .Append("\" value=\"").Append(Enc(NORMHelper.Str(row, "OwnerUserId"))).Append("\" placeholder=\"Assign preparer\"></td><td><input name=\"workflowReviewer_")
                    .Append(id).Append("\" value=\"").Append(Enc(NORMHelper.Str(row, "ReviewerUserId"))).Append("\" placeholder=\"Assign reviewer\"></td><td><select name=\"workflowStatus_")
                    .Append(id).Append("\">").Append(WorkflowStatusOptions(NORMHelper.Str(row, "StatusCode"))).Append("</select></td><td><input name=\"workflowComment_")
                    .Append(id).Append("\" value=\"").Append(Enc(NORMHelper.Str(row, "Commentary"))).Append("\" placeholder=\"Decision, blocker or next step\"></td></tr>");
            }
            html.Append("</tbody></table>");
            return html.ToString();
        }

        private string BuildManualInputs(DataTable table)
        {
            if (!EnhancementsInstalled)
                return "<div class=\"norm-empty\">Run <code>sql/NORM_05_StatementDemoEnhancements.sql</code> to enable controlled manual schedules.</div>";
            StringBuilder html = new StringBuilder("<div class=\"norm-manual-grid\">");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                string id = NORMHelper.Long(row, "ManualInputId").ToString(CultureInfo.InvariantCulture);
                string type = NORMHelper.Str(row, "InputTypeCode");
                html.Append("<article class=\"norm-manual-card\"><header><div><span>")
                    .Append(Enc(type.Replace("Analysis", " analysis"))).Append("</span><strong>")
                    .Append(Enc(NORMHelper.Str(row, "InputLabel"))).Append("</strong></div><em>")
                    .Append(Enc(NORMHelper.Str(row, "DisclosureCode"))).Append("</em></header>");
                if (!String.Equals(type, "Commentary", StringComparison.OrdinalIgnoreCase))
                {
                    html.Append("<div class=\"norm-manual-values\"><label><span>Current year ($'000)</span><input type=\"number\" step=\"0.001\" name=\"manualCurrent_")
                        .Append(id).Append("\" value=\"").Append(NumberValue(row, "AmountCurrent")).Append("\"></label>")
                        .Append("<label><span>Comparative ($'000)</span><input type=\"number\" step=\"0.001\" name=\"manualPrior_")
                        .Append(id).Append("\" value=\"").Append(NumberValue(row, "AmountPrior")).Append("\"></label></div>");
                }
                html.Append("<label><span>Evidence / workbook reference</span><input name=\"manualEvidence_").Append(id)
                    .Append("\" value=\"").Append(Enc(NORMHelper.Str(row, "EvidenceReference"))).Append("\" placeholder=\"Workbook, tab, cell or attachment\"></label>")
                    .Append("<label><span>").Append(String.Equals(type, "Commentary", StringComparison.OrdinalIgnoreCase) ? "Commentary" : "Preparation note")
                    .Append("</span><textarea rows=\"3\" name=\"manualCommentary_").Append(id).Append("\">")
                    .Append(Enc(NORMHelper.Str(row, "Commentary"))).Append("</textarea></label>")
                    .Append("<label><span>Status</span><select name=\"manualStatus_").Append(id).Append("\">")
                    .Append(ManualStatusOptions(NORMHelper.Str(row, "StatusCode"))).Append("</select></label></article>");
            }
            html.Append("</div>");
            return html.ToString();
        }

        private string BuildBudgetFigures(DataTable table)
        {
            if (!EnhancementsInstalled)
                return "<div class=\"norm-empty\">Install the NORM statement enhancement objects to enable Original Budget inputs.</div>";
            StringBuilder html = new StringBuilder("<div class=\"norm-workflow-table norm-budget-register\"><table><thead><tr><th>Statement line</th><th>Original Budget ($'000)</th><th>Source system</th><th>Source reference / report</th><th>Status</th></tr></thead><tbody>");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                string id = NORMHelper.Long(row, "BudgetFigureId").ToString(CultureInfo.InvariantCulture);
                html.Append("<tr><td><small>").Append(Enc(NORMHelper.Str(row, "StatementCode"))).Append("</small><strong>")
                    .Append(Enc(NORMHelper.Str(row, "LineCode"))).Append("</strong></td><td><input type=\"number\" step=\"0.001\" name=\"budgetAmount_")
                    .Append(id).Append("\" value=\"").Append(NumberValue(row, "OriginalBudget")).Append("\"></td><td><input name=\"budgetSystem_")
                    .Append(id).Append("\" value=\"").Append(Enc(NORMHelper.Str(row, "SourceSystem"))).Append("\" placeholder=\"ERP / budget system\"></td><td><input name=\"budgetReference_")
                    .Append(id).Append("\" value=\"").Append(Enc(NORMHelper.Str(row, "SourceReference"))).Append("\" placeholder=\"Approved budget report or extract\"></td><td><select name=\"budgetStatus_")
                    .Append(id).Append("\">").Append(BudgetStatusOptions(NORMHelper.Str(row, "StatusCode"))).Append("</select></td></tr>");
            }
            html.Append("</tbody></table></div>");
            return html.ToString();
        }

        private string BuildCashFlowJournals(DataTable table)
        {
            if (!EnhancementsInstalled)
                return "<div class=\"norm-empty\">Install the NORM statement enhancement objects to enable cash-flow journals.</div>";
            StringBuilder html = new StringBuilder("<div class=\"norm-workflow-table norm-cf-journals\"><table><thead><tr><th>Journal and description</th><th>Cash-flow category</th><th>Amount ($'000)</th><th>Evidence</th><th>Status</th></tr></thead><tbody>");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                string id = NORMHelper.Long(row, "CashFlowJournalId").ToString(CultureInfo.InvariantCulture);
                html.Append(CashFlowJournalRow(id, NORMHelper.Str(row, "JournalReference"), NORMHelper.Str(row, "JournalDescription"),
                    NORMHelper.Str(row, "CashFlowClass"), NumberValue(row, "Amount"), NORMHelper.Str(row, "EvidenceReference"),
                    NORMHelper.Str(row, "StatusCode"), false));
            }
            html.Append(CashFlowJournalRow("new", "", "", "Payments to suppliers", "", "", "Draft", true));
            html.Append("</tbody></table></div>");
            return html.ToString();
        }

        private string CashFlowJournalRow(string id, string reference, string description, string cashClass,
            string amount, string evidence, string status, bool isNew)
        {
            return "<tr" + (isNew ? " class=\"norm-new-row\"" : "") + "><td><input name=\"cfReference_" + id + "\" value=\"" + Enc(reference) + "\" placeholder=\"" + (isNew ? "New journal reference" : "Reference") + "\"><input name=\"cfDescription_" + id + "\" value=\"" + Enc(description) + "\" placeholder=\"Non-cash adjustment or reclassification\"></td>" +
                "<td><select name=\"cfClass_" + id + "\">" + CashClassOptions(cashClass) + "</select></td>" +
                "<td><input type=\"number\" step=\"0.001\" name=\"cfAmount_" + id + "\" value=\"" + Enc(amount) + "\"></td>" +
                "<td><input name=\"cfEvidence_" + id + "\" value=\"" + Enc(evidence) + "\" placeholder=\"Working paper reference\"></td>" +
                "<td><select name=\"cfStatus_" + id + "\">" + CashStatusOptions(status) + "</select></td></tr>";
        }

        private string CashClassOptions(string selected)
        {
            return Options(new string[,] {
                { "Receipts from Government", "Operating - receipts from Government" },
                { "Receipts from customers", "Operating - customer receipts" },
                { "Payments to employees", "Operating - employees" },
                { "Payments to suppliers", "Operating - suppliers" },
                { "Purchase of property plant and equipment", "Investing - PPE purchases" },
                { "Proceeds from sales of property plant and equipment", "Investing - PPE sales" },
                { "Contributed equity", "Financing - contributed equity" },
                { "Principal payments of lease liabilities", "Financing - lease principal" }
            }, selected);
        }

        private string ManualStatusOptions(string selected)
        {
            return Options(new string[,] { { "NotStarted", "Not started" }, { "Draft", "Draft" }, { "Prepared", "Prepared" }, { "Validated", "Validated" } }, selected);
        }

        private string CashStatusOptions(string selected)
        {
            return Options(new string[,] { { "Draft", "Draft" }, { "Prepared", "Prepared" }, { "Approved", "Approved" }, { "Posted", "Posted" } }, selected);
        }

        private string BudgetStatusOptions(string selected)
        {
            return Options(new string[,] { { "Loaded", "Loaded" }, { "Prepared", "Prepared" }, { "Validated", "Validated" } }, selected);
        }

        private string NumberValue(DataRow row, string column)
        {
            return row.IsNull(column) ? "" : Convert.ToDecimal(row[column]).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private decimal? DecimalValue(string value)
        {
            decimal parsed;
            if (Decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed)) return parsed;
            if (Decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out parsed)) return parsed;
            return null;
        }

        private string Options(string[,] values, string selected)
        {
            StringBuilder html = new StringBuilder();
            for (int i = 0; i < values.GetLength(0); i++)
            {
                html.Append("<option value=\"").Append(Enc(values[i, 0])).Append("\"")
                    .Append(String.Equals(values[i, 0], selected, StringComparison.OrdinalIgnoreCase) ? " selected" : "")
                    .Append(">").Append(Enc(values[i, 1])).Append("</option>");
            }
            return html.ToString();
        }

        private string StatusOptions(string selected)
        {
            return Options(new string[,] { { "Draft", "Draft" }, { "Prepared", "Prepared" }, { "Reviewed", "Reviewed" }, { "Approved", "Approved" } }, selected);
        }

        private string WorkflowStatusOptions(string selected)
        {
            return Options(new string[,] { { "NotStarted", "Not started" }, { "InProgress", "In progress" }, { "Prepared", "Prepared" }, { "Reviewed", "Reviewed" }, { "Approved", "Approved" }, { "Blocked", "Blocked" } }, selected);
        }

        private string CssStatus(string value)
        {
            return (value ?? "").ToLowerInvariant().Replace(" ", "-");
        }

        private string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
