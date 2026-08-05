using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;

namespace CPlatform.NORM
{
    public partial class NORM_Workspace : NORMBasePage
    {
        protected string SummaryHtml = "";
        protected string RunsHtml = "";
        protected string ReleasesHtml = "";
        protected string ControlStatusHtml = "";
        protected string NextStepsHtml = "";
        protected string LatestStatementsUrl = "";

        protected void Page_Load(object sender, EventArgs e)
        {
            BuildSummary();
            BuildRuns();
            BuildReleases();
        }

        private void BuildSummary()
        {
            DataTable table = NORMHelper.Query(
                "SELECT TOP 1 r.CalculationRunId,i.ImportId,i.[RowCount] AS [RowCount],i.NetBalance,i.IsTestBreak,c.VersionCode," +
                "r.CompletedUtc," +
                "SUM(CASE WHEN v.ResultCode = 'Pass' THEN 1 ELSE 0 END) AS Passed," +
                "SUM(CASE WHEN v.ResultCode = 'Fail' THEN 1 ELSE 0 END) AS Failed,COUNT(v.ValidationResultId) AS Checks " +
                "FROM dbo.tblNORM_CalculationRun r INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId = r.ConfigurationReleaseId " +
                "LEFT JOIN dbo.tblNORM_ValidationResult v ON v.CalculationRunId = r.CalculationRunId " +
                "WHERE r.StatusCode = 'Complete' AND r.IsDeactivated = 0 AND i.IsDeactivated = 0 AND i.IsTestBreak = 0 " +
                "GROUP BY r.CalculationRunId,i.ImportId,i.[RowCount],i.NetBalance,i.IsTestBreak,c.VersionCode,r.CompletedUtc " +
                "ORDER BY r.CalculationRunId DESC");
            StringBuilder html = new StringBuilder();
            if (table.Rows.Count == 0)
            {
                ControlStatusHtml = "<div class=\"norm-control-status norm-control-status-neutral\"><span class=\"norm-status\"></span><div><span class=\"norm-kicker\">Latest run</span><h2>No trial balance loaded yet</h2><p>Load the current trial balance to create the first controlled NORM run.</p></div></div>";
                NextStepsHtml = "<div class=\"norm-panel-head\"><div><span class=\"norm-kicker\">Start here</span><h2>Your next step</h2></div></div><div class=\"norm-next-step\"><b>1</b><div><strong>Load a trial balance</strong><p>NORM will validate the file, retain the original and calculate the statements.</p><a class=\"norm-button norm-button-small\" href=\"NORM_Import.aspx\">Load trial balance</a></div></div>";
                html.Append(Card("Latest source run", "Not generated", "Import the FY2025 trial balance to begin."));
                html.Append(Card("Source rows", "-", "No immutable import yet."));
                html.Append(Card("Assurance", "-", "Checks run with the statement engine."));
                html.Append(Card("Configuration", "-", "Install and approve FY2025 v1.0."));
            }
            else
            {
                DataRow row = table.Rows[0];
                int runId = NORMHelper.Int(row, "CalculationRunId");
                int failed = NORMHelper.Int(row, "Failed");
                int warnings = NORMHelper.Int(row, "Checks") - NORMHelper.Int(row, "Passed") - failed;
                LatestStatementsUrl = "NORM_Statements.aspx?run=" + runId.ToString(CultureInfo.InvariantCulture);
                string state = failed > 0 ? "fail" : (warnings > 0 ? "warn" : "pass");
                string heading = failed > 0 ? "Latest run needs attention" : "Latest run is ready for review";
                string detail = failed > 0
                    ? failed.ToString(CultureInfo.InvariantCulture) + " blocking check" + (failed == 1 ? " requires" : "s require") + " attention before accounting sign-off."
                    : (warnings > 0 ? warnings.ToString(CultureInfo.InvariantCulture) + " warning" + (warnings == 1 ? " is" : "s are") + " recorded; no blocking failures." : "All assurance checks passed; open the statements for review.");
                ControlStatusHtml = "<div class=\"norm-control-status norm-control-status-" + state + "\"><span class=\"norm-status " + state + "\"></span><div><span class=\"norm-kicker\">Latest run · " + Enc(Convert.ToString(row["CompletedUtc"])) + "</span><h2>" + heading + "</h2><p>Run " + runId.ToString(CultureInfo.InvariantCulture) + " · " + detail + "</p></div><a class=\"norm-button norm-button-small\" href=\"" + LatestStatementsUrl + "\">Review run</a></div>";
                NextStepsHtml = BuildNextSteps(runId, failed, warnings);
                html.Append(Card("Latest source run", "Run " + NORMHelper.Int(row, "CalculationRunId").ToString(),
                    "Import " + NORMHelper.Int(row, "ImportId").ToString() + " completed " + Enc(Convert.ToString(row["CompletedUtc"]))));
                html.Append(Card("Source rows", NORMHelper.Int(row, "RowCount").ToString("N0"),
                    "Net balance $" + NORMHelper.Dec(row, "NetBalance").ToString("N2")));
                html.Append(Card("Assurance", NORMHelper.Int(row, "Passed").ToString() + " of " + NORMHelper.Int(row, "Checks").ToString() + " pass",
                    NORMHelper.Int(row, "Failed") == 0 ? "No blocking failures recorded." : NORMHelper.Int(row, "Failed").ToString() + " checks require attention."));
                html.Append(Card("Configuration", Enc(NORMHelper.Str(row, "VersionCode")), "Approved FY-versioned accounting content."));
            }
            SummaryHtml = html.ToString();
        }

        private string BuildNextSteps(int runId, int failed, int warnings)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<div class=\"norm-panel-head\"><div><span class=\"norm-kicker\">What to do next</span><h2>Recommended actions</h2></div></div><div class=\"norm-next-list\">");
            if (failed > 0)
            {
                html.Append("<a class=\"norm-next-step norm-next-step-priority\" href=\"NORM_Statements.aspx?run=").Append(runId).Append("\"><b>1</b><span><strong>Resolve blocking checks</strong><small>").Append(failed).Append(" blocking item").Append(failed == 1 ? " needs" : "s need").Append(" accounting review.</small></span><em>Open &rarr;</em></a>");
            }
            else
            {
                html.Append("<a class=\"norm-next-step\" href=\"NORM_Statements.aspx?run=").Append(runId).Append("\"><b>1</b><span><strong>Review the statements</strong><small>").Append(warnings > 0 ? "Review the warnings alongside the financial statements." : "Open the controlled statement output and review the figures.").Append("</small></span><em>Open &rarr;</em></a>");
            }
            html.Append("<a class=\"norm-next-step\" href=\"NORM_Statements.aspx?run=").Append(runId).Append("\"><b>2</b><span><strong>Trace a figure to source</strong><small>Select any statement amount to see its mapped accounts and retained source rows.</small></span><em>Trace &rarr;</em></a>");
            html.Append("<a class=\"norm-next-step\" href=\"NORM_Import.aspx\"><b>3</b><span><strong>Load the next trial balance</strong><small>Start a new controlled run when the next source file is ready.</small></span><em>Load &rarr;</em></a>");
            html.Append("</div>");
            return html.ToString();
        }

        private void BuildRuns()
        {
            DataTable table = NORMHelper.Query(
                "SELECT TOP 30 r.CalculationRunId,r.StatusCode,r.CompletedUtc,r.StartedBy,r.FailureDetail,i.ImportId,i.SourceFileName," +
                "i.SourceType,i.[RowCount] AS [RowCount],i.NetBalance,i.IsTestBreak,c.VersionCode," +
                "SUM(CASE WHEN v.ResultCode = 'Fail' THEN 1 ELSE 0 END) AS Failed," +
                "SUM(CASE WHEN v.ResultCode = 'Warning' THEN 1 ELSE 0 END) AS Warnings " +
                "FROM dbo.tblNORM_CalculationRun r INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId = r.ConfigurationReleaseId " +
                "LEFT JOIN dbo.tblNORM_ValidationResult v ON v.CalculationRunId = r.CalculationRunId " +
                "WHERE r.IsDeactivated = 0 AND i.IsDeactivated = 0 " +
                "GROUP BY r.CalculationRunId,r.StatusCode,r.CompletedUtc,r.StartedBy,r.FailureDetail,i.ImportId,i.SourceFileName,i.SourceType," +
                "i.[RowCount],i.NetBalance,i.IsTestBreak,c.VersionCode ORDER BY r.CalculationRunId DESC");
            if (table.Rows.Count == 0) { RunsHtml = "<div class=\"norm-empty\">No calculation runs yet.</div>"; return; }
            StringBuilder html = new StringBuilder("<div class=\"norm-run-list\">");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                bool test = Convert.ToBoolean(row["IsTestBreak"]);
                string runStatus = NORMHelper.Str(row, "StatusCode");
                bool complete = String.Equals(runStatus, "Complete", StringComparison.OrdinalIgnoreCase);
                int failed = NORMHelper.Int(row, "Failed");
                int warnings = NORMHelper.Int(row, "Warnings");
                string state = !complete || failed > 0 ? "fail" : (warnings > 0 ? "warn" : "pass");
                if (complete)
                {
                    html.Append("<a class=\"norm-run-row\" href=\"NORM_Statements.aspx?run=")
                        .Append(NORMHelper.Int(row, "CalculationRunId")).Append("\">");
                }
                else { html.Append("<div class=\"norm-run-row norm-run-row-static\">"); }
                html.Append("<span class=\"norm-status ").Append(state).Append("\"></span><span><strong>")
                    .Append(test ? "Test break" : Enc(NORMHelper.Str(row, "SourceFileName"))).Append("</strong><small>Import ")
                    .Append(NORMHelper.Int(row, "ImportId")).Append(" · ").Append(NORMHelper.Int(row, "RowCount").ToString("N0"))
                    .Append(" rows · ").Append(Enc(NORMHelper.Str(row, "VersionCode"))).Append("</small>");
                if (!complete && !String.IsNullOrWhiteSpace(NORMHelper.Str(row, "FailureDetail")))
                {
                    html.Append("<small class=\"norm-run-error\">").Append(Enc(NORMHelper.Str(row, "FailureDetail"))).Append("</small>");
                }
                html.Append("</span><span class=\"norm-run-result\">")
                    .Append(!complete ? Enc(runStatus) : (failed > 0 ? failed.ToString() + " failed" : (warnings > 0 ? warnings.ToString() + " warnings" : "All pass")))
                    .Append("<small>Run ").Append(NORMHelper.Int(row, "CalculationRunId")).Append("</small></span>")
                    .Append(complete ? "</a>" : "</div>");
            }
            html.Append("</div>");
            RunsHtml = html.ToString();
        }

        private void BuildReleases()
        {
            DataTable table = NORMHelper.Query(
                "SELECT c.ConfigurationReleaseId,c.FinancialYear,c.EntityCode,c.VersionCode,c.ReleaseLabel,c.StatusCode,c.ApprovedBy,c.ApprovedUtc," +
                "(SELECT COUNT(*) FROM dbo.tblNORM_AccountMap m WHERE m.ConfigurationReleaseId = c.ConfigurationReleaseId AND m.IsDeactivated = 0) AS MappingCount," +
                "(SELECT COUNT(*) FROM dbo.tblNORM_StatementLine s WHERE s.ConfigurationReleaseId = c.ConfigurationReleaseId AND s.IsDeactivated = 0) AS LineCount " +
                "FROM dbo.tblNORM_ConfigurationRelease c WHERE c.IsDeactivated = 0 ORDER BY c.FinancialYear DESC,c.EntityCode,c.VersionCode DESC");
            if (table.Rows.Count == 0) { ReleasesHtml = "<div class=\"norm-empty\">No configuration releases installed.</div>"; return; }
            StringBuilder html = new StringBuilder("<div class=\"norm-release-list\">");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                html.Append("<article><div><span class=\"norm-chip\">").Append(Enc(NORMHelper.Str(row, "StatusCode"))).Append("</span><strong>FY")
                    .Append(NORMHelper.Int(row, "FinancialYear")).Append(" ").Append(Enc(NORMHelper.Str(row, "EntityCode"))).Append(" ")
                    .Append(Enc(NORMHelper.Str(row, "VersionCode"))).Append("</strong></div><p>").Append(Enc(NORMHelper.Str(row, "ReleaseLabel")))
                    .Append("</p><small>").Append(NORMHelper.Int(row, "MappingCount").ToString("N0")).Append(" account mappings · ")
                    .Append(NORMHelper.Int(row, "LineCount").ToString("N0")).Append(" statement template rows</small></article>");
            }
            html.Append("</div>");
            ReleasesHtml = html.ToString();
        }

        private string Card(string label, string value, string detail)
        {
            return "<article class=\"norm-summary-card\"><span>" + Enc(label) + "</span><strong>" + Enc(value) + "</strong><small>" + Enc(detail) + "</small></article>";
        }

        private string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
