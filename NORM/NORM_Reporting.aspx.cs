using System;
using System.Collections.Generic;
using System.Data;
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
