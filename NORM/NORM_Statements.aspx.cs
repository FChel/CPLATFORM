using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Script.Serialization;

namespace CPlatform.NORM
{
    public partial class NORM_Statements : NORMBasePage
    {
        protected override bool RequiresPrepareAccess { get { return false; } }
        protected string NormDataJson = "{}";
        protected bool CanPrepare = false;
        protected int selectedRunId;

        protected void Page_Load(object sender, EventArgs e)
        {
            CanPrepare = NORMHelper.HasPrepareAccess();
            selectedRunId = ResolveRunId();
            if (selectedRunId > 0) { NormDataJson = Serialise(BuildPayload(selectedRunId)); }
        }

        protected void CreateBreak_Click(object sender, EventArgs e)
        {
            if (!NORMHelper.HasPrepareAccess()) { throw new InvalidOperationException("Preparer access is required."); }
            int runId = ResolveRunId();
            DataTable source = NORMHelper.Query(
                "SELECT r.ImportId,i.IsTestBreak,i.ParentImportId FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "WHERE r.CalculationRunId = @run AND r.StatusCode = 'Complete' AND r.IsDeactivated = 0",
                NORMHelper.P("@run", runId));
            if (source.Rows.Count == 0) { throw new InvalidOperationException("Choose a completed calculation run first."); }
            int importId = NORMHelper.Int(source.Rows[0], "ImportId");
            if (Convert.ToBoolean(source.Rows[0]["IsTestBreak"]) && !source.Rows[0].IsNull("ParentImportId"))
            {
                importId = NORMHelper.Int(source.Rows[0], "ParentImportId");
            }
            NORMImportOutcome outcome = NORMImportService.CreateTestBreak(importId, 48250m, NORMHelper.CurrentUserId());
            Response.Redirect("NORM_Statements.aspx?run=" + outcome.CalculationRunId.ToString(), true);
        }

        private int ResolveRunId()
        {
            int requested;
            if (Int32.TryParse(Request.QueryString["run"], out requested) && requested > 0)
            {
                object exists = NORMHelper.Scalar(
                    "SELECT COUNT(1) FROM dbo.tblNORM_CalculationRun WHERE CalculationRunId = @run " +
                    "AND StatusCode = 'Complete' AND IsDeactivated = 0", NORMHelper.P("@run", requested));
                if (exists != null && Convert.ToInt32(exists) > 0) { return requested; }
            }
            object latest = NORMHelper.Scalar(
                "SELECT TOP 1 r.CalculationRunId FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "WHERE r.StatusCode = 'Complete' AND r.IsDeactivated = 0 AND i.IsDeactivated = 0 AND i.IsTestBreak = 0 " +
                "ORDER BY r.CalculationRunId DESC");
            return latest == null ? 0 : Convert.ToInt32(latest);
        }

        private Dictionary<string, object> BuildPayload(int runId)
        {
            DataTable contextTable = NORMHelper.Query(
                "SELECT r.CalculationRunId,r.RunGuid,r.InputFingerprint,r.CompletedUtc," +
                "i.ImportId,i.FinancialYear,i.EntityCode,i.SourceType,i.SourceFileName,i.SourceFileHash,i.[RowCount] AS [RowCount]," +
                "i.TotalDebit,i.TotalCredit,i.NetBalance,i.ImportedUtc,i.IsTestBreak,i.ParentImportId," +
                "c.ConfigurationReleaseId,c.VersionCode,c.ReleaseLabel,e.EntityName " +
                "FROM dbo.tblNORM_CalculationRun r " +
                "INNER JOIN dbo.tblNORM_Import i ON i.ImportId = r.ImportId " +
                "INNER JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId = r.ConfigurationReleaseId " +
                "LEFT JOIN dbo.tblNORM_ReportingEntity e ON e.FinancialYear = i.FinancialYear AND e.EntityCode = i.EntityCode AND e.IsDeactivated = 0 " +
                "WHERE r.CalculationRunId = @run AND r.StatusCode = 'Complete' AND r.IsDeactivated = 0",
                NORMHelper.P("@run", runId));
            if (contextTable.Rows.Count == 0) { return new Dictionary<string, object>(); }
            DataRow context = contextTable.Rows[0];
            int releaseId = NORMHelper.Int(context, "ConfigurationReleaseId");

            Dictionary<string, object> payload = new Dictionary<string, object>();
            Dictionary<string, object> meta = new Dictionary<string, object>();
            int fy = NORMHelper.Int(context, "FinancialYear");
            int importId = NORMHelper.Int(context, "ImportId");
            Dictionary<long, List<Dictionary<string, object>>> lineage = LoadLineage(runId, fy);
            meta["runId"] = runId;
            meta["runGuid"] = Convert.ToString(context["RunGuid"]);
            meta["fingerprint"] = NORMHelper.Str(context, "InputFingerprint");
            meta["importId"] = importId;
            meta["entity"] = NORMHelper.Str(context, "EntityName") ?? NORMHelper.Str(context, "EntityCode");
            meta["fy"] = (fy - 1).ToString() + "\u2013" + (fy % 100).ToString("D2");
            meta["yearCurrent"] = fy;
            meta["yearPrior"] = fy - 1;
            meta["release"] = NORMHelper.Str(context, "VersionCode");
            meta["releaseLabel"] = NORMHelper.Str(context, "ReleaseLabel");
            meta["file"] = NORMHelper.Str(context, "SourceFileName");
            meta["fileHash"] = NORMHelper.Str(context, "SourceFileHash");
            meta["sourceType"] = NORMHelper.Str(context, "SourceType");
            meta["rowCount"] = NORMHelper.Int(context, "RowCount");
            meta["netBalance"] = NORMHelper.Dec(context, "NetBalance");
            meta["isTestBreak"] = Convert.ToBoolean(context["IsTestBreak"]);
            meta["parentImportId"] = context.IsNull("ParentImportId") ? (object)null : NORMHelper.Int(context, "ParentImportId");
            meta["completedUtc"] = Convert.ToString(context["CompletedUtc"]);
            meta["canPrepare"] = CanPrepare;
            meta["reportingUrl"] = CanPrepare ? "NORM_Reporting.aspx?run=" + runId.ToString() : "";
            payload["meta"] = meta;
            payload["sourceFiles"] = LoadSourceFiles(importId, runId);

            List<object> statements = new List<object>();
            statements.Add(BuildStatement(releaseId, runId, "SOCI", "Statement of Comprehensive Income", lineage));
            statements.Add(BuildStatement(releaseId, runId, "SOFP", "Statement of Financial Position", lineage));
            statements.Add(BuildEquityStatement(runId, releaseId, lineage));
            statements.Add(BuildCashFlowStatement(runId, releaseId, lineage));

            NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(releaseId);
            List<NORMReportingFramework.Disclosure> disclosures = NORMReportingFramework.IsInstalled()
                ? NORMReportingFramework.LoadDisclosures(runId, releaseId, profile)
                : new List<NORMReportingFramework.Disclosure>();
            statements.Add(BuildNotesStatement(disclosures));
            payload["statements"] = statements;
            payload["profile"] = BuildProfilePayload(profile);
            payload["disclosures"] = BuildDisclosurePayload(disclosures);
            List<object> validations = LoadValidations(runId);
            AppendDisclosureValidations(validations, disclosures);
            payload["validations"] = validations;
            payload["unmapped"] = BuildUnmapped(runId, lineage);
            return payload;
        }

        private Dictionary<string, object> BuildProfilePayload(NORMReportingFramework.ReportingProfile profile)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["entityType"] = profile.EntityType;
            value["reportingBasis"] = profile.ReportingBasis;
            value["disclosureTier"] = profile.DisclosureTier;
            value["materiality"] = profile.MaterialityBasis;
            List<object> requirements = new List<object>();
            List<NORMReportingFramework.CapabilityDefinition> definitions = NORMReportingFramework.CapabilityDefinitions();
            for (int i = 0; i < definitions.Count; i++)
            {
                NORMReportingFramework.CapabilityDefinition definition = definitions[i];
                if (!profile.Requirements.ContainsKey(definition.Code) || !profile.Requirements[definition.Code]) { continue; }
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["code"] = definition.Code;
                item["label"] = definition.Label;
                requirements.Add(item);
            }
            value["requirements"] = requirements;
            value["configured"] = NORMReportingFramework.IsInstalled();
            return value;
        }

        private List<object> BuildDisclosurePayload(List<NORMReportingFramework.Disclosure> disclosures)
        {
            List<object> values = new List<object>();
            for (int i = 0; i < disclosures.Count; i++)
            {
                NORMReportingFramework.Disclosure source = disclosures[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["code"] = source.Code;
                item["sectionCode"] = source.SectionCode;
                item["section"] = source.SectionTitle;
                item["note"] = source.NoteRef;
                item["title"] = source.Title;
                item["trigger"] = source.TriggerCode;
                item["guidance"] = source.Guidance;
                item["required"] = source.Required;
                item["status"] = source.CompletionStatus;
                item["sourceCount"] = source.SourceCount;
                item["amount"] = source.Amount;
                item["narrative"] = source.Narrative;
                item["narrativeStatus"] = source.NarrativeStatus;
                List<object> lines = new List<object>();
                for (int l = 0; l < source.Lines.Count; l++)
                {
                    Dictionary<string, object> line = new Dictionary<string, object>();
                    line["label"] = source.Lines[l].Label;
                    line["amount"] = source.Lines[l].Amount;
                    line["sourceCount"] = source.Lines[l].SourceCount;
                    lines.Add(line);
                }
                item["lines"] = lines;
                values.Add(item);
            }
            return values;
        }

        private Dictionary<string, object> BuildNotesStatement(List<NORMReportingFramework.Disclosure> disclosures)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["code"] = "NOTES";
            value["title"] = "Notes to and forming part of the financial statements";
            value["layout"] = "notes";
            value["rows"] = new List<object>();
            value["disclosures"] = BuildDisclosurePayload(disclosures);
            return value;
        }

        private void AppendDisclosureValidations(List<object> validations, List<NORMReportingFramework.Disclosure> disclosures)
        {
            int required = 0;
            int complete = 0;
            int missing = 0;
            for (int i = 0; i < disclosures.Count; i++)
            {
                if (!disclosures[i].Required) { continue; }
                required++;
                if (disclosures[i].CompletionStatus == "Needs input") { missing++; }
                else { complete++; }
            }
            if (required == 0) { return; }
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["code"] = "PRIMA_DISCLOSURE_COMPLETENESS";
            item["label"] = "PRIMA disclosure register is complete";
            item["severity"] = "Warning";
            item["result"] = missing == 0 ? "Pass" : "Warning";
            item["actual"] = complete;
            item["expected"] = required;
            item["difference"] = complete - required;
            item["tolerance"] = 0;
            item["detail"] = complete.ToString() + " of " + required.ToString() + " required statements and notes contain generated figures or draft wording; " + missing.ToString() + " need input.";
            validations.Add(item);
        }

        private Dictionary<string, object> BuildEquityStatement(int runId, int releaseId,
            Dictionary<long, List<Dictionary<string, object>>> lineage)
        {
            DataTable table = NORMHelper.Query(
                "SELECT r.LineCode,r.LineResultId,r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_LineResult r " +
                "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId=@release AND p.StatementCode=r.StatementCode " +
                "AND p.LineCode=r.LineCode AND p.IsDeactivated=0 " +
                "WHERE r.CalculationRunId=@run AND r.IsDeactivated=0 AND r.LineCode IN ('Operating result','Statement of Changes in Equity')",
                NORMHelper.P("@release", releaseId), NORMHelper.P("@run", runId));
            DataRow result = FindLine(table, "Operating result");
            DataRow equity = FindLine(table, "Statement of Changes in Equity");
            decimal currentResult = result == null ? 0m : NORMHelper.Dec(result, "ComputedAmount");
            decimal priorResult = result == null || result.IsNull("AmountPrior") ? 0m : NORMHelper.Dec(result, "AmountPrior");
            decimal closing = equity == null ? 0m : NORMHelper.Dec(equity, "ComputedAmount");
            decimal opening = equity == null || equity.IsNull("AmountPrior") ? 0m : NORMHelper.Dec(equity, "AmountPrior");
            decimal ownerTransactions = closing - opening - currentResult;

            List<object> rows = new List<object>();
            rows.Add(SimpleRow("section", null, "Contributed equity, reserves and retained earnings", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "SOCE_OPEN", "Opening balance", null, opening, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "SOCE_RESULT", "Total comprehensive income/(loss)", "1", currentResult,
                priorResult, result != null, result == null ? 0L : NORMHelper.Long(result, "LineResultId"), SourcesFor(result, lineage)));
            rows.Add(SimpleRow("line", "SOCE_OWNER", "Transactions with owners in their capacity as owners", null,
                ownerTransactions, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("total", "SOCE_CLOSE", "Closing balance", null, closing, opening,
                equity != null, equity == null ? 0L : NORMHelper.Long(equity, "LineResultId"), SourcesFor(equity, lineage)));
            Dictionary<string, object> statement = new Dictionary<string, object>();
            statement["code"] = "SOCE";
            statement["title"] = "Statement of Changes in Equity";
            statement["layout"] = "standard";
            statement["rows"] = rows;
            return statement;
        }

        private Dictionary<string, object> BuildCashFlowStatement(int runId, int releaseId,
            Dictionary<long, List<Dictionary<string, object>>> lineage)
        {
            Dictionary<string, List<Dictionary<string, object>>> grouped = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<long, List<Dictionary<string, object>>> pair in lineage)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    Dictionary<string, object> source = pair.Value[i];
                    if (!String.Equals(Convert.ToString(source["derivation"]), "GL_MAPPING", StringComparison.OrdinalIgnoreCase)) { continue; }
                    string cash = Convert.ToString(source["cash"]);
                    if (String.IsNullOrWhiteSpace(cash)) { continue; }
                    if (!grouped.ContainsKey(cash)) { grouped[cash] = new List<Dictionary<string, object>>(); }
                    grouped[cash].Add(source);
                }
            }

            List<object> rows = new List<object>();
            AddCashSection(rows, grouped, "Operating activities", "OPERATING");
            AddCashSection(rows, grouped, "Investing activities", "INVESTING");
            AddCashSection(rows, grouped, "Financing activities", "FINANCING");
            decimal classifiedNet = 0m;
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, object> row = (Dictionary<string, object>)rows[i];
                if (Convert.ToString(row["type"]) == "line") { classifiedNet += Convert.ToDecimal(row["computed"]); }
            }

            DataTable cashTable = NORMHelper.Query(
                "SELECT r.LineResultId,r.ComputedAmount,p.AmountPrior FROM dbo.tblNORM_LineResult r " +
                "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId=@release AND p.StatementCode=r.StatementCode " +
                "AND p.LineCode=r.LineCode AND p.IsDeactivated=0 " +
                "WHERE r.CalculationRunId=@run AND r.LineCode='Cash and cash equivalents' AND r.IsDeactivated=0",
                NORMHelper.P("@release", releaseId), NORMHelper.P("@run", runId));
            DataRow cashRow = cashTable.Rows.Count == 0 ? null : cashTable.Rows[0];
            decimal ending = cashRow == null ? 0m : NORMHelper.Dec(cashRow, "ComputedAmount");
            decimal beginning = cashRow == null || cashRow.IsNull("AmountPrior") ? ending - classifiedNet : NORMHelper.Dec(cashRow, "AmountPrior");
            rows.Add(SimpleRow("total", "CF_NET", "Net increase/(decrease) in cash held", "5.5", classifiedNet, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "CF_OPEN", "Cash and cash equivalents at the beginning of the reporting period", null, beginning, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("total", "CF_CLOSE", "Cash and cash equivalents at the end of the reporting period", "3.1A", ending,
                cashRow == null || cashRow.IsNull("AmountPrior") ? (decimal?)null : beginning, cashRow != null,
                cashRow == null ? 0L : NORMHelper.Long(cashRow, "LineResultId"), SourcesFor(cashRow, lineage)));
            Dictionary<string, object> statement = new Dictionary<string, object>();
            statement["code"] = "CASH";
            statement["title"] = "Cash Flow Statement";
            statement["layout"] = "standard";
            statement["rows"] = rows;
            statement["classifiedNet"] = classifiedNet;
            return statement;
        }

        private void AddCashSection(List<object> rows,
            Dictionary<string, List<Dictionary<string, object>>> grouped, string label, string category)
        {
            rows.Add(SimpleRow("section", null, label, null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            decimal total = 0m;
            foreach (KeyValuePair<string, List<Dictionary<string, object>>> pair in grouped)
            {
                if (!String.Equals(CashCategory(pair.Key), category, StringComparison.Ordinal)) { continue; }
                decimal amount = 0m;
                bool outflow = IsCashOutflow(pair.Key);
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    decimal source = Convert.ToDecimal(pair.Value[i]["sourceAmount"]) / 1000m;
                    amount += outflow ? -Math.Abs(source) : Math.Abs(source);
                }
                total += amount;
                rows.Add(SimpleRow("line", "CF_" + pair.Key, pair.Key, null, amount, null, true, -1L, pair.Value));
            }
            rows.Add(SimpleRow("total", "CF_TOTAL_" + category, "Net cash from/(used by) " + label.ToLowerInvariant(), null,
                total, null, false, 0L, new List<Dictionary<string, object>>()));
        }

        private string CashCategory(string label)
        {
            string value = (label ?? "").ToLowerInvariant();
            if (value.IndexOf("purchase") >= 0 || value.IndexOf("proceeds from sale") >= 0 ||
                value.IndexOf("proceeds from investment") >= 0 || value.IndexOf("investing") >= 0) { return "INVESTING"; }
            if (value.IndexOf("appropriation") >= 0 || value.IndexOf("opa") >= 0 ||
                value.IndexOf("principal payments of lease") >= 0 || value.IndexOf("special account") >= 0) { return "FINANCING"; }
            return "OPERATING";
        }

        private bool IsCashOutflow(string label)
        {
            string value = (label ?? "").ToLowerInvariant();
            return value.IndexOf("payment") >= 0 || value.IndexOf("purchase") >= 0 || value.IndexOf("used") >= 0 ||
                value.IndexOf("paid") >= 0 || value.IndexOf("return") >= 0 || value.IndexOf("selling cost") >= 0;
        }

        private DataRow FindLine(DataTable table, string lineCode)
        {
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (String.Equals(NORMHelper.Str(table.Rows[i], "LineCode"), lineCode, StringComparison.OrdinalIgnoreCase)) { return table.Rows[i]; }
            }
            return null;
        }

        private List<Dictionary<string, object>> SourcesFor(DataRow row,
            Dictionary<long, List<Dictionary<string, object>>> lineage)
        {
            if (row == null) { return new List<Dictionary<string, object>>(); }
            long resultId = NORMHelper.Long(row, "LineResultId");
            return resultId > 0 && lineage.ContainsKey(resultId) ? lineage[resultId] : new List<Dictionary<string, object>>();
        }

        private Dictionary<string, object> SimpleRow(string type, string code, string label, string note,
            decimal computed, decimal? prior, bool clickable, long resultId, List<Dictionary<string, object>> sources)
        {
            Dictionary<string, object> row = new Dictionary<string, object>();
            row["type"] = type;
            row["code"] = code;
            row["label"] = label;
            row["note"] = note;
            row["sign"] = null;
            row["clickable"] = clickable;
            row["resultId"] = resultId;
            row["computed"] = computed;
            row["published"] = null;
            row["prior"] = prior.HasValue ? (object)prior.Value : null;
            row["variance"] = null;
            row["status"] = "Mapped";
            row["sources"] = sources;
            return row;
        }

        private Dictionary<string, object> BuildStatement(int releaseId, int runId, string statementCode,
            string title, Dictionary<long, List<Dictionary<string, object>>> lineage)
        {
            DataTable table = NORMHelper.Query(
                "SELECT t.StatementLineId,t.SeqNo,t.LineType,t.LineCode,t.LineLabel,t.NoteRef,t.NaturalSign,t.IsClickable," +
                "r.LineResultId,r.ComputedAmount,r.PublishedAmount,r.Variance,r.StatusCode,p.AmountPrior " +
                "FROM dbo.tblNORM_StatementLine t " +
                "LEFT JOIN dbo.tblNORM_LineResult r ON r.StatementLineId = t.StatementLineId AND r.CalculationRunId = @run AND r.IsDeactivated = 0 " +
                "LEFT JOIN dbo.tblNORM_PublishedFigure p ON p.ConfigurationReleaseId = t.ConfigurationReleaseId " +
                "AND p.StatementCode = t.StatementCode AND p.LineCode = t.LineCode AND p.IsDeactivated = 0 " +
                "WHERE t.ConfigurationReleaseId = @release AND t.StatementCode = @statement AND t.IsDeactivated = 0 ORDER BY t.SeqNo",
                NORMHelper.P("@run", runId), NORMHelper.P("@release", releaseId),
                NORMHelper.P("@statement", statementCode));
            List<object> rows = new List<object>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                string lineCode = NORMHelper.Str(source, "LineCode");
                if (statementCode == "SOCI" && lineCode == "Revenue from contracts with customers")
                {
                    rows.Add(SimpleRow("section", null, "Own-source revenue", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOCI" && lineCode == "Gain on sale of asset")
                {
                    rows.Add(SimpleRow("section", null, "Gains", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOCI" && lineCode == "Revenue from Government")
                {
                    rows.Add(SimpleRow("section", null, "Income from Government", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOFP" && lineCode == "Leases")
                {
                    rows.Add(SimpleRow("section", null, "Interest-bearing liabilities", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOFP" && lineCode == "Employee provisions")
                {
                    rows.Add(SimpleRow("section", null, "Provisions", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["type"] = NORMHelper.Str(source, "LineType");
                row["code"] = lineCode;
                row["label"] = NORMHelper.Str(source, "LineLabel");
                row["note"] = PrimaNoteRef(statementCode, lineCode, NORMHelper.Str(source, "NoteRef"));
                row["sign"] = NORMHelper.Str(source, "NaturalSign");
                row["clickable"] = Convert.ToBoolean(source["IsClickable"]);
                long resultId = source.IsNull("LineResultId") ? 0L : NORMHelper.Long(source, "LineResultId");
                row["resultId"] = resultId;
                row["computed"] = source.IsNull("ComputedAmount") ? 0m : NORMHelper.Dec(source, "ComputedAmount");
                row["published"] = source.IsNull("PublishedAmount") ? (object)null : NORMHelper.Dec(source, "PublishedAmount");
                row["prior"] = source.IsNull("AmountPrior") ? (object)null : NORMHelper.Dec(source, "AmountPrior");
                row["variance"] = source.IsNull("Variance") ? (object)null : NORMHelper.Dec(source, "Variance");
                row["status"] = source.IsNull("StatusCode") ? "Mapped" : NORMHelper.Str(source, "StatusCode");
                row["sources"] = resultId > 0 && lineage.ContainsKey(resultId)
                    ? (object)lineage[resultId] : new List<Dictionary<string, object>>();
                rows.Add(row);
            }
            if (statementCode == "SOCI")
            {
                rows.Add(SimpleRow("section", null, "Other comprehensive income", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                rows.Add(SimpleRow("line", "OCI_REVALUATION", "Changes in asset revaluation reserve", "1.3", 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                rows.Add(SimpleRow("line", "OCI_OTHER", "Other comprehensive income", "1.3", 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                rows.Add(SimpleRow("total", "OCI_TOTAL", "Total comprehensive income/(loss)", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            }
            Dictionary<string, object> statement = new Dictionary<string, object>();
            statement["code"] = statementCode;
            statement["title"] = title;
            statement["rows"] = rows;
            return statement;
        }

        private string PrimaNoteRef(string statementCode, string lineCode, string configured)
        {
            if (statementCode == "SOCI")
            {
                Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                notes["Employee benefits"] = "1.1A";
                notes["Supplier expenses"] = "1.1B";
                notes["Grants"] = "1.1C";
                notes["Finance costs"] = "1.1D";
                notes["Impairment loss on financial instruments"] = "1.1E";
                notes["Write-down of non-financial assets"] = "1.1F";
                notes["Foreign exchange"] = "1.1G";
                notes["Other expenses"] = "1.1H";
                notes["Revenue from contracts with customers"] = "1.2A";
                notes["Rental income"] = "1.2E";
                notes["Other revenue"] = "1.2F";
                notes["Reversals of previous asset write-downs"] = "1.2H";
                notes["Other gains"] = "1.2I";
                notes["Revenue from Government"] = "1.2J";
                string value;
                if (lineCode != null && notes.TryGetValue(lineCode, out value)) { return value; }
            }
            if (statementCode == "SOFP")
            {
                if (lineCode == "Prepayments" || lineCode == "Assets held for sale") { return "3.2C"; }
                if (lineCode == "Employee payables" || lineCode == "Other payables") { return "3.3"; }
                if (lineCode == "Employee provisions" || lineCode == "Asset restoration provisions" || lineCode == "Other provisions") { return "3.5"; }
            }
            return configured;
        }

        private Dictionary<long, List<Dictionary<string, object>>> LoadLineage(int runId, int financialYear)
        {
            DataTable table = NORMHelper.Query(
                "SELECT l.LineResultId,l.AccountMapId,l.SourceAmount,l.PresentedContribution,l.DerivationCode,l.MappingSnapshot," +
                "l.AccountTypeSnapshot,l.NoteSubLineSnapshot,l.CashFlowClassSnapshot," +
                "tb.SourceRowNo,tb.SourceLedger,tb.GlAccount,tb.GlText,tb.IsSynthetic " +
                "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.TbRowId = l.TbRowId " +
                "WHERE l.CalculationRunId = @run ORDER BY l.LineResultId,ABS(l.PresentedContribution) DESC,tb.GlAccount",
                NORMHelper.P("@run", runId));
            Dictionary<long, List<Dictionary<string, object>>> values =
                new Dictionary<long, List<Dictionary<string, object>>>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                long resultId = NORMHelper.Long(source, "LineResultId");
                if (!values.ContainsKey(resultId)) { values[resultId] = new List<Dictionary<string, object>>(); }
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["row"] = NORMHelper.Int(source, "SourceRowNo");
                row["ledger"] = NORMHelper.Str(source, "SourceLedger");
                row["gl"] = NORMHelper.Str(source, "GlAccount");
                row["text"] = NORMHelper.Str(source, "GlText");
                row["sourceAmount"] = NORMHelper.Dec(source, "SourceAmount");
                row["amount"] = NORMHelper.Dec(source, "PresentedContribution");
                row["derivation"] = NORMHelper.Str(source, "DerivationCode");
                row["mappingId"] = source.IsNull("AccountMapId") ? (object)null : NORMHelper.Int(source, "AccountMapId");
                row["mapping"] = NORMHelper.Str(source, "MappingSnapshot");
                row["accountType"] = NORMHelper.Str(source, "AccountTypeSnapshot");
                row["note"] = NORMHelper.Str(source, "NoteSubLineSnapshot");
                row["cash"] = NORMHelper.Str(source, "CashFlowClassSnapshot");
                row["synthetic"] = Convert.ToBoolean(source["IsSynthetic"]);
                row["sapUrl"] = Convert.ToBoolean(source["IsSynthetic"]) ? "" :
                    NORMHelper.SapGlLineItemsLink(
                        NORMHelper.Str(source, "GlAccount"),
                        NORMHelper.Str(source, "SourceLedger"),
                        financialYear);
                values[resultId].Add(row);
            }
            return values;
        }

        private List<object> LoadSourceFiles(int importId, int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT ImportFileId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes," +
                "PeriodStart,PeriodEnd,[RowCount],IsStatementInput,CreatedUtc " +
                "FROM dbo.tblNORM_ImportFile WHERE ImportId = @import ORDER BY PeriodStart,SourceType",
                NORMHelper.P("@import", importId));
            List<object> values = new List<object>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                int fileId = NORMHelper.Int(source, "ImportFileId");
                item["id"] = fileId;
                item["type"] = NORMHelper.Str(source, "SourceType");
                item["file"] = NORMHelper.Str(source, "SourceFileName");
                item["hash"] = NORMHelper.Str(source, "SourceFileHash");
                item["bytes"] = NORMHelper.Long(source, "SourceFileBytes");
                item["periodStart"] = source.IsNull("PeriodStart") ? (object)null : NORMHelper.Int(source, "PeriodStart");
                item["periodEnd"] = source.IsNull("PeriodEnd") ? (object)null : NORMHelper.Int(source, "PeriodEnd");
                item["rows"] = NORMHelper.Int(source, "RowCount");
                item["statementInput"] = Convert.ToBoolean(source["IsStatementInput"]);
                item["createdUtc"] = Convert.ToString(source["CreatedUtc"]);
                item["downloadUrl"] = CanPrepare
                    ? "NORM_SourceFile.ashx?run=" + runId.ToString() + "&file=" + fileId.ToString()
                    : "";
                values.Add(item);
            }
            return values;
        }

        private List<object> LoadValidations(int runId)
        {
            DataTable table = NORMHelper.Query(
                "SELECT CheckCode,CheckLabel,SeverityCode,ResultCode,ActualValue,ExpectedValue,DifferenceValue,ToleranceValue,DetailText " +
                "FROM dbo.tblNORM_ValidationResult WHERE CalculationRunId = @run " +
                "ORDER BY CASE SeverityCode WHEN 'Blocking' THEN 1 WHEN 'Warning' THEN 2 ELSE 3 END,ValidationResultId",
                NORMHelper.P("@run", runId));
            List<object> values = new List<object>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["code"] = NORMHelper.Str(source, "CheckCode");
                item["label"] = NORMHelper.Str(source, "CheckLabel");
                item["severity"] = NORMHelper.Str(source, "SeverityCode");
                item["result"] = NORMHelper.Str(source, "ResultCode");
                item["actual"] = source.IsNull("ActualValue") ? (object)null : NORMHelper.Dec(source, "ActualValue");
                item["expected"] = source.IsNull("ExpectedValue") ? (object)null : NORMHelper.Dec(source, "ExpectedValue");
                item["difference"] = source.IsNull("DifferenceValue") ? (object)null : NORMHelper.Dec(source, "DifferenceValue");
                item["tolerance"] = source.IsNull("ToleranceValue") ? (object)null : NORMHelper.Dec(source, "ToleranceValue");
                item["detail"] = NORMHelper.Str(source, "DetailText");
                values.Add(item);
            }
            return values;
        }

        private Dictionary<string, object> BuildUnmapped(int runId,
            Dictionary<long, List<Dictionary<string, object>>> lineage)
        {
            DataTable table = NORMHelper.Query(
                "SELECT TOP 1 LineResultId,ComputedAmount FROM dbo.tblNORM_LineResult " +
                "WHERE CalculationRunId = @run AND LineCode = 'UNMAPPED' AND IsDeactivated = 0",
                NORMHelper.P("@run", runId));
            Dictionary<string, object> item = new Dictionary<string, object>();
            if (table.Rows.Count == 0)
            {
                item["amount"] = 0m; item["sources"] = new List<object>(); return item;
            }
            long resultId = NORMHelper.Long(table.Rows[0], "LineResultId");
            item["amount"] = NORMHelper.Dec(table.Rows[0], "ComputedAmount");
            item["sources"] = lineage.ContainsKey(resultId)
                ? (object)lineage[resultId] : new List<Dictionary<string, object>>();
            return item;
        }

        private string Serialise(object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 50 * 1024 * 1024;
            return serializer.Serialize(value).Replace("</", "<\\/");
        }
    }
}
