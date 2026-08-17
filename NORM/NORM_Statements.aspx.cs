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
            int fy = NORMStartOfYearSetup.ResolveCurrentFinancialYear(
                NORMHelper.Str(context, "EntityCode"), NORMHelper.Int(context, "FinancialYear"));
            string entityCode = NORMHelper.Str(context, "EntityCode");
            int importId = NORMHelper.Int(context, "ImportId");
            Dictionary<long, List<Dictionary<string, object>>> lineage = LoadLineage(runId, fy);
            Dictionary<string, decimal> budgets = NORMStatementEnhancements.LoadBudgetFigures(runId);
            NORMStartOfYearSetup.OverlayFigures(budgets, NORMStartOfYearSetup.LoadOriginalBudgetFigures(entityCode));
            Dictionary<string, decimal> priorFigures = NORMStartOfYearSetup.LoadPriorActualFigures(entityCode);
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
            statements.Add(BuildStatement(releaseId, runId, "SOCI", "Statement of Comprehensive Income", lineage, budgets, priorFigures));
            statements.Add(BuildStatement(releaseId, runId, "SOFP", "Statement of Financial Position", lineage, budgets, priorFigures));
            statements.Add(BuildEquityStatement(runId, releaseId, lineage, budgets, priorFigures));
            statements.Add(BuildCashFlowStatement(runId, releaseId, lineage, budgets, priorFigures));
            payload["assetMovement"] = BuildAssetMovementStatement(runId, releaseId, lineage);

            NORMReportingFramework.ReportingProfile profile = NORMReportingFramework.LoadProfile(releaseId);
            List<NORMReportingFramework.Disclosure> disclosures = NORMReportingFramework.IsInstalled()
                ? NORMReportingFramework.LoadDisclosures(runId, releaseId, profile)
                : new List<NORMReportingFramework.Disclosure>();
            NORMStatementEnhancements.ApplyManualInputs(runId, disclosures);
            statements.Add(BuildNotesStatement(disclosures));
            payload["statements"] = statements;
            payload["profile"] = BuildProfilePayload(profile);
            payload["disclosures"] = BuildDisclosurePayload(disclosures);
            List<object> validations = LoadValidations(runId);
            AppendDisclosureValidations(validations, disclosures);
            AppendEnhancementValidations(validations, runId);
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
            value["overallMateriality"] = profile.OverallMateriality;
            value["performanceMateriality"] = profile.PerformanceMateriality;
            value["clearlyTrivialThreshold"] = profile.ClearlyTrivialThreshold;
            value["budgetVarianceThreshold"] = profile.BudgetVarianceThreshold;
            value["qualitativeConsiderations"] = profile.QualitativeConsiderations;
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
                item["suggested"] = source.Suggested;
                item["potentiallyImmaterial"] = source.PotentiallyImmaterial;
                item["requirementReason"] = source.RequirementReason;
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
                    line["prior"] = source.Lines[l].Prior.HasValue ? (object)source.Lines[l].Prior.Value : null;
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
            Dictionary<long, List<Dictionary<string, object>>> lineage, Dictionary<string, decimal> budgets,
            Dictionary<string, decimal> priorFigures)
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
            decimal? baselinePriorResult = result == null || result.IsNull("AmountPrior") ? (decimal?)null : NORMHelper.Dec(result, "AmountPrior");
            decimal priorResult = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCE", "Operating result", baselinePriorResult) ?? 0m;
            decimal closing = equity == null ? 0m : NORMHelper.Dec(equity, "ComputedAmount");
            decimal? baselineOpening = equity == null || equity.IsNull("AmountPrior") ? (decimal?)null : NORMHelper.Dec(equity, "AmountPrior");
            decimal opening = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCE", "Statement of Changes in Equity", baselineOpening) ?? 0m;
            List<Dictionary<string, object>> equitySources = SourcesFor(equity, lineage);
            List<Dictionary<string, object>> contributedSources = FilterEquitySources(equitySources, "Contributed equity");
            List<Dictionary<string, object>> retainedSources = FilterEquitySources(equitySources, "Retained surplus/(Accumulated deficit)");
            List<Dictionary<string, object>> reserveSources = FilterEquitySources(equitySources, "Reserves");
            decimal contributedClose = SumSources(contributedSources);
            decimal retainedClose = SumSources(retainedSources);
            decimal reserveClose = SumSources(reserveSources);

            Dictionary<string, decimal> audited = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOCE", "AuditedActual");
            Dictionary<string, decimal> prior = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOCE", "PriorActual");
            foreach (KeyValuePair<string, decimal> item in priorFigures)
                if (item.Key.StartsWith("SOCE|", StringComparison.OrdinalIgnoreCase)) prior[item.Key.Substring(5)] = item.Value;
            decimal contributedOpen = SourceFigure(audited, "SOCE_CONTRIBUTED_OPEN", contributedClose);
            decimal retainedOpen = SourceFigure(audited, "SOCE_RETAINED_OPEN", retainedClose - currentResult);
            decimal reserveOpen = SourceFigure(audited, "SOCE_RESERVE_OPEN", reserveClose);
            decimal contributedMovement = contributedClose - contributedOpen;
            decimal retainedMovement = retainedClose - retainedOpen;
            decimal reserveMovement = reserveClose - reserveOpen;
            decimal comprehensiveIncome = retainedMovement + reserveMovement;
            decimal ownerTransactions = closing - opening - comprehensiveIncome;

            List<object> rows = new List<object>();
            AddEquityClassSection(rows, "CONTRIBUTED", "CONTRIBUTED EQUITY", contributedOpen, contributedMovement,
                contributedClose, prior, contributedSources);
            AddEquityClassSection(rows, "RETAINED", "(ACCUMULATED DEFICIT) / RETAINED SURPLUSES", retainedOpen,
                retainedMovement, retainedClose, prior, retainedSources);
            AddEquityClassSection(rows, "RESERVE", "ASSET REVALUATION RESERVE", reserveOpen, reserveMovement,
                reserveClose, prior, reserveSources);
            rows.Add(SimpleRow("major", null, "TOTAL EQUITY", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("subsection", null, "Opening balance", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "SOCE_TOTAL_OPEN", "Balance carried forward from previous period", null, opening,
                SourceFigureNullable(prior, "SOCE_TOTAL_OPEN"), false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("subsection", null, "Comprehensive (loss) / income", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "SOCE_TOTAL_RESULT", "(Deficit) / Surplus for the period as reported", "1", currentResult,
                SourceFigureNullable(prior, "SOCE_TOTAL_RESULT") ?? (decimal?)priorResult, result != null,
                result == null ? 0L : NORMHelper.Long(result, "LineResultId"), SourcesFor(result, lineage)));
            rows.Add(SimpleRow("line", "SOCE_TOTAL_OCI", "Other comprehensive income / (loss)", "1.3", reserveMovement,
                SourceFigureNullable(prior, "SOCE_TOTAL_OCI"), false, 0L, reserveSources));
            rows.Add(SimpleRow("total", "SOCE_TOTAL_COMPREHENSIVE", "Total comprehensive (loss) / income", null,
                comprehensiveIncome, SourceFigureNullable(prior, "SOCE_TOTAL_COMPREHENSIVE"), false, 0L,
                new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("subsection", null, "Transactions with owners", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "SOCE_TOTAL_OWNER", "Transactions with owners in their capacity as owners", null,
                ownerTransactions, SourceFigureNullable(prior, "SOCE_TOTAL_OWNER"), false, 0L, contributedSources));
            rows.Add(SimpleRow("total", "SOCE_TOTAL_CLOSE", "Closing balance as at 30 June", null, closing,
                SourceFigureNullable(prior, "SOCE_TOTAL_CLOSE") ?? (decimal?)opening,
                equity != null, equity == null ? 0L : NORMHelper.Long(equity, "LineResultId"), equitySources));
            ApplyBudget(rows, "SOCE", budgets);
            Dictionary<string, object> statement = new Dictionary<string, object>();
            statement["code"] = "SOCE";
            statement["title"] = "Statement of Changes in Equity";
            statement["layout"] = "standard";
            statement["rows"] = rows;
            return statement;
        }

        private void AddEquityClassSection(List<object> rows, string code, string label, decimal opening,
            decimal movement, decimal closing, Dictionary<string, decimal> prior,
            List<Dictionary<string, object>> sources)
        {
            string prefix = "SOCE_" + code;
            rows.Add(SimpleRow("major", null, label, null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("subsection", null, "Opening balance", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", prefix + "_OPEN", "Balance carried forward from previous period", null,
                opening, SourceFigureNullable(prior, prefix + "_OPEN"), false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("subsection", null, code == "CONTRIBUTED" ? "Transactions with owners" :
                (code == "RETAINED" ? "Comprehensive (loss) / income" : "Other comprehensive income"),
                null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            string movementLabel = code == "CONTRIBUTED" ? "Contributions by owners" :
                (code == "RETAINED" ? "(Deficit) / Surplus for the period as reported" : "Other comprehensive income / (loss)");
            string movementCode = prefix + (code == "CONTRIBUTED" ? "_OWNER" : (code == "RETAINED" ? "_RESULT" : "_OCI"));
            rows.Add(SimpleRow("line", movementCode, movementLabel, code == "RESERVE" ? "1.3" : null,
                movement, SourceFigureNullable(prior, movementCode), false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("total", movementCode, code == "CONTRIBUTED" ? "Total transactions with owners" :
                "Total comprehensive (loss) / income", null, movement, SourceFigureNullable(prior, movementCode),
                false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("total", prefix + "_CLOSE", "Closing balance as at 30 June", null, closing,
                SourceFigureNullable(prior, prefix + "_CLOSE"), sources.Count > 0, -1L, sources));
        }

        private decimal SourceFigure(Dictionary<string, decimal> values, string code, decimal fallback)
        {
            decimal amount;
            return values != null && values.TryGetValue(code, out amount) ? amount : fallback;
        }

        private decimal? SourceFigureNullable(Dictionary<string, decimal> values, string code)
        {
            decimal amount;
            return values != null && values.TryGetValue(code, out amount) ? (decimal?)amount : null;
        }

        private List<Dictionary<string, object>> FilterEquitySources(List<Dictionary<string, object>> sources, string label)
        {
            List<Dictionary<string, object>> values = new List<Dictionary<string, object>>();
            for (int i = 0; i < sources.Count; i++)
                if (String.Equals(EquityClassLabel(Convert.ToString(sources[i]["note"])), label, StringComparison.OrdinalIgnoreCase)) values.Add(sources[i]);
            return values;
        }

        private Dictionary<string, object> BuildCashFlowStatement(int runId, int releaseId,
            Dictionary<long, List<Dictionary<string, object>>> lineage, Dictionary<string, decimal> budgets,
            Dictionary<string, decimal> priorFigures)
        {
            Dictionary<string, List<Dictionary<string, object>>> grouped = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<long, List<Dictionary<string, object>>> pair in lineage)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    Dictionary<string, object> source = pair.Value[i];
                    if (!String.Equals(Convert.ToString(source["derivation"]), "GL_MAPPING", StringComparison.OrdinalIgnoreCase)) { continue; }
                    string cash = CanonicalCashClass(Convert.ToString(source["cash"]));
                    if (!IsSafeCashClass(cash)) { continue; }
                    if (!grouped.ContainsKey(cash)) { grouped[cash] = new List<Dictionary<string, object>>(); }
                    grouped[cash].Add(source);
                }
            }

            DataTable journals = NORMStatementEnhancements.LoadCashFlowJournals(runId);
            for (int i = 0; i < journals.Rows.Count; i++)
            {
                DataRow journal = journals.Rows[i];
                string status = NORMHelper.Str(journal, "StatusCode");
                if (!String.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(status, "Posted", StringComparison.OrdinalIgnoreCase)) { continue; }
                string cash = CanonicalCashClass(NORMHelper.Str(journal, "CashFlowClass"));
                if (String.IsNullOrWhiteSpace(cash)) { continue; }
                if (!grouped.ContainsKey(cash)) { grouped[cash] = new List<Dictionary<string, object>>(); }
                Dictionary<string, object> source = new Dictionary<string, object>();
                source["row"] = 0;
                source["ledger"] = "NORM cash-flow journal";
                source["gl"] = NORMHelper.Str(journal, "JournalReference");
                source["text"] = NORMHelper.Str(journal, "JournalDescription");
                source["sourceAmount"] = NORMHelper.Dec(journal, "Amount") * 1000m;
                source["movement"] = NORMHelper.Dec(journal, "Amount") * 1000m;
                source["amount"] = NORMHelper.Dec(journal, "Amount");
                source["derivation"] = "CASH_FLOW_JOURNAL";
                source["mappingId"] = null;
                source["mapping"] = status + " controlled cash-flow adjustment";
                source["accountType"] = "Journal";
                source["note"] = NORMHelper.Str(journal, "EvidenceReference");
                source["cash"] = cash;
                source["synthetic"] = true;
                source["sapUrl"] = "";
                grouped[cash].Add(source);
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
            decimal? baselineBeginning = cashRow == null || cashRow.IsNull("AmountPrior") ? (decimal?)null : NORMHelper.Dec(cashRow, "AmountPrior");
            decimal beginning = NORMStartOfYearSetup.FigureValue(priorFigures, "CASH", "Cash and cash equivalents", baselineBeginning) ?? (ending - classifiedNet);
            rows.Add(SimpleRow("total", "CF_NET", "Net increase/(decrease) in cash held", "5.5", classifiedNet, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("line", "CF_OPEN", "Cash and cash equivalents at the beginning of the reporting period", null, beginning, null, false, 0L, new List<Dictionary<string, object>>()));
            rows.Add(SimpleRow("total", "CF_CLOSE", "Cash and cash equivalents at the end of the reporting period", "3.1A", ending,
                cashRow == null || cashRow.IsNull("AmountPrior") ? (decimal?)null : beginning, cashRow != null,
                cashRow == null ? 0L : NORMHelper.Long(cashRow, "LineResultId"), SourcesFor(cashRow, lineage)));
            ApplyBudget(rows, "CASH", budgets);
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
            List<string> keys = new List<string>();
            foreach (string key in grouped.Keys) if (String.Equals(CashCategory(key), category, StringComparison.Ordinal)) keys.Add(key);
            keys.Sort(delegate(string left, string right) { return CashSortOrder(category, left).CompareTo(CashSortOrder(category, right)); });
            bool outflowHeadingAdded = false;
            if (keys.Count > 0 && !IsCashOutflow(keys[0])) rows.Add(SimpleRow("subsection", null, "Cash received", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
            for (int k = 0; k < keys.Count; k++)
            {
                KeyValuePair<string, List<Dictionary<string, object>>> pair =
                    new KeyValuePair<string, List<Dictionary<string, object>>>(keys[k], grouped[keys[k]]);
                decimal amount = 0m;
                decimal original = 0m;
                decimal adjustment = 0m;
                bool outflow = IsCashOutflow(pair.Key);
                if (outflow && !outflowHeadingAdded)
                {
                    rows.Add(SimpleRow("subsection", null, "Cash used", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                    outflowHeadingAdded = true;
                }
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    decimal source = Convert.ToDecimal(pair.Value[i]["movement"]) / 1000m;
                    decimal presented = outflow ? -Math.Abs(source) : Math.Abs(source);
                    amount += presented;
                    if (String.Equals(Convert.ToString(pair.Value[i]["derivation"]), "CASH_FLOW_JOURNAL", StringComparison.OrdinalIgnoreCase))
                        adjustment += presented;
                    else
                        original += presented;
                }
                total += amount;
                Dictionary<string, object> cashRow = SimpleRow("line", "CF_" + pair.Key, pair.Key, null, amount, null, true, -1L, pair.Value);
                cashRow["original"] = original;
                cashRow["adjustment"] = adjustment;
                rows.Add(cashRow);
            }
            rows.Add(SimpleRow("total", "CF_TOTAL_" + category, "Net cash from/(used by) " + label.ToLowerInvariant(), null,
                total, null, false, 0L, new List<Dictionary<string, object>>()));
        }

        private string CashCategory(string label)
        {
            string value = (label ?? "").ToLowerInvariant();
            if (value.IndexOf("purchase") >= 0 || value.IndexOf("proceeds from sale") >= 0 ||
                value.IndexOf("proceeds from investment") >= 0 || value.IndexOf("investing") >= 0) { return "INVESTING"; }
            if (value.IndexOf("contributed equity") >= 0 || value.IndexOf("borrow") >= 0 ||
                value.IndexOf("principal payments of lease") >= 0 || value.IndexOf("return of equity") >= 0 ||
                value.IndexOf("financing") >= 0) { return "FINANCING"; }
            return "OPERATING";
        }

        private string CanonicalCashClass(string label)
        {
            string value = (label ?? "").Trim().ToLowerInvariant();
            if (!IsSafeCashClass(label)) return null;
            if (value.IndexOf("section 74") >= 0 || value.IndexOf("transferred to opa") >= 0) return "Section 74 receipts transferred to the OPA";
            if (value.IndexOf("gst received") >= 0) return "GST received";
            if (value.IndexOf("gst paid") >= 0) return "GST paid";
            if (value.IndexOf("appropriation") >= 0) return "Appropriations";
            if (value.IndexOf("receipt") >= 0 && value.IndexOf("government") >= 0) return "Receipts from Government";
            if (value.IndexOf("customer") >= 0 || value.IndexOf("goods and services") >= 0 || value.IndexOf("rendering of services") >= 0) return "Sale of goods and rendering of services";
            if (value.IndexOf("employee") >= 0 || value.IndexOf("salary") >= 0) return "Employees";
            if (value.IndexOf("supplier") >= 0) return "Suppliers";
            if (value.IndexOf("borrowing cost") >= 0) return "Borrowing costs";
            if (value.IndexOf("interest") >= 0 && value.IndexOf("lease") >= 0) return "Interest payments on lease liabilities";
            if (value.IndexOf("income tax") >= 0) return "Income taxes paid";
            if (value.IndexOf("dividend") >= 0 && IsCashOutflow(label)) return "Dividends paid";
            if (value.IndexOf("dividend") >= 0) return "Dividends";
            if (value.IndexOf("interest") >= 0) return "Interest";
            if (value.IndexOf("grant") >= 0) return "Grants";
            if (value.IndexOf("purchase") >= 0 && (value.IndexOf("property") >= 0 || value.IndexOf("asset") >= 0 || value.IndexOf("equipment") >= 0)) return "Purchase of property, plant and equipment";
            if ((value.IndexOf("sale") >= 0 || value.IndexOf("proceeds") >= 0) && (value.IndexOf("asset") >= 0 || value.IndexOf("property") >= 0 || value.IndexOf("equipment") >= 0)) return "Proceeds from sale of property, plant and equipment";
            if (value.IndexOf("contributed equity") >= 0 || value.IndexOf("equity injection") >= 0) return "Contributed equity";
            if (value.IndexOf("principal") >= 0 && value.IndexOf("lease") >= 0) return "Principal payments of lease liabilities";
            if (value.IndexOf("return") >= 0 && value.IndexOf("equity") >= 0) return "Return of equity";
            if (value.IndexOf("repay") >= 0 && value.IndexOf("borrow") >= 0) return "Repayments of borrowings";
            if (value.IndexOf("borrow") >= 0) return "Borrowings";
            if (value.IndexOf("investing") >= 0) return IsCashOutflow(label) ? "Other investing cash used" : "Other investing cash received";
            if (value.IndexOf("financing") >= 0) return IsCashOutflow(label) ? "Other financing cash used" : "Other financing cash received";
            return IsCashOutflow(label) ? "Other cash used" : "Other cash received";
        }

        private int CashSortOrder(string category, string label)
        {
            string[] operating = new string[] { "Appropriations", "Receipts from Government", "Sale of goods and rendering of services", "Interest", "Dividends", "GST received", "Other cash received", "Employees", "Suppliers", "Borrowing costs", "Interest payments on lease liabilities", "Income taxes paid", "GST paid", "Section 74 receipts transferred to the OPA", "Grants", "Other cash used" };
            string[] investing = new string[] { "Proceeds from sale of property, plant and equipment", "Other investing cash received", "Purchase of property, plant and equipment", "Other investing cash used" };
            string[] financing = new string[] { "Contributed equity", "Borrowings", "Other financing cash received", "Return of equity", "Repayments of borrowings", "Principal payments of lease liabilities", "Dividends paid", "Other financing cash used" };
            string[] order = category == "INVESTING" ? investing : (category == "FINANCING" ? financing : operating);
            for (int i = 0; i < order.Length; i++) if (String.Equals(order[i], label, StringComparison.OrdinalIgnoreCase)) return i;
            return order.Length;
        }

        private bool IsSafeCashClass(string label)
        {
            string value = (label ?? "").Trim().ToLowerInvariant();
            if (value.Length == 0 || value.StartsWith("clearing -")) { return false; }
            return value.IndexOf("depreciation") < 0 && value.IndexOf("amortisation") < 0 &&
                value.IndexOf("equity movement") < 0 && value.IndexOf("asset movement") < 0 &&
                value.IndexOf("cash and cash equivalents") < 0;
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
            row["budget"] = null;
            row["variance"] = null;
            row["status"] = "Mapped";
            row["sources"] = sources;
            return row;
        }

        private Dictionary<string, object> BuildStatement(int releaseId, int runId, string statementCode,
            string title, Dictionary<long, List<Dictionary<string, object>>> lineage, Dictionary<string, decimal> budgets,
            Dictionary<string, decimal> priorFigures)
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
            bool hasForeignExchangeGains = false;
            for (int i = 0; i < table.Rows.Count; i++)
                if (String.Equals(NORMHelper.Str(table.Rows[i], "LineCode"), "Foreign exchange gains", StringComparison.OrdinalIgnoreCase))
                    hasForeignExchangeGains = true;
            decimal ownSourceRevenue = 0m;
            decimal gains = 0m;
            bool revenueTotalAdded = false;
            bool gainsTotalAdded = false;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow source = table.Rows[i];
                string lineCode = NORMHelper.Str(source, "LineCode");
                string lineType = NORMHelper.Str(source, "LineType");
                decimal computed = source.IsNull("ComputedAmount") ? 0m : NORMHelper.Dec(source, "ComputedAmount");
                if (statementCode == "SOCI" && rows.Count == 0)
                    rows.Add(SimpleRow("major", null, "NET COST OF SERVICES", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                if (statementCode == "SOCI" && lineType == "section" &&
                    String.Equals(NORMHelper.Str(source, "LineLabel"), "Own-source income", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(SimpleRow("lead", null, "LESS:", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                    rows.Add(SimpleRow("lead", null, "INCOME", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                    continue;
                }
                if (statementCode == "SOCI" && lineCode == "Revenue from contracts with customers")
                {
                    rows.Add(SimpleRow("subsection", null, "Own-source revenue", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOCI" && lineCode == "Gain on sale of asset")
                {
                    rows.Add(SimpleRow("total", "TOTAL_OSR", "Total own-source revenue", null, ownSourceRevenue, null, false, 0L, new List<Dictionary<string, object>>()));
                    revenueTotalAdded = true;
                    rows.Add(SimpleRow("subsection", null, "Gains", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOCI" && lineCode == "Total own-source income" && !gainsTotalAdded)
                {
                    rows.Add(SimpleRow("total", "TOTAL_GAINS", "Total gains", null, gains, null, false, 0L, new List<Dictionary<string, object>>()));
                    gainsTotalAdded = true;
                }
                if (statementCode == "SOCI" && lineCode == "Other gains" && !hasForeignExchangeGains)
                {
                    Dictionary<string, object> foreignExchangeGain = SimpleRow("line", "Foreign exchange gains",
                        "Net foreign exchange gains", "1.2F", 0m,
                        NORMStartOfYearSetup.FigureValue(priorFigures, "SOCI", "Foreign exchange gains", null),
                        false, 0L, new List<Dictionary<string, object>>());
                    foreignExchangeGain["computed"] = null;
                    foreignExchangeGain["budget"] = BudgetFor(budgets, "SOCI", "Foreign exchange gains");
                    rows.Add(foreignExchangeGain);
                }
                if (statementCode == "SOCI" && lineCode == "Revenue from Government")
                {
                    rows.Add(SimpleRow("major", null, "REVENUE FROM GOVERNMENT", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOFP" && rows.Count == 0)
                    rows.Add(SimpleRow("major", null, "ASSETS", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                if (statementCode == "SOFP" && lineCode == "Cash and cash equivalents")
                    rows.Add(SimpleRow("subsection", "HEADING_FINANCIAL_ASSETS", "Financial assets", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                if (statementCode == "SOFP" && lineCode == "Property plant and equipment")
                    rows.Add(SimpleRow("subsection", "HEADING_NON_FINANCIAL_ASSETS", "Non-financial assets", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                if (statementCode == "SOFP" && lineType == "section" && String.Equals(NORMHelper.Str(source, "LineLabel"), "Liabilities", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(SimpleRow("major", null, "LIABILITIES", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                    rows.Add(SimpleRow("subsection", null, "Payables", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                    continue;
                }
                if (statementCode == "SOFP" && lineType == "section" && String.Equals(NORMHelper.Str(source, "LineLabel"), "Equity", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(SimpleRow("major", null, "EQUITY", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                    continue;
                }
                if (statementCode == "SOFP" && lineCode == "Leases")
                {
                    rows.Add(SimpleRow("subsection", "HEADING_INTEREST_LIABILITIES", "Interest-bearing liabilities", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                if (statementCode == "SOFP" && lineCode == "Employee provisions")
                {
                    rows.Add(SimpleRow("subsection", null, "Provisions", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                }
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["type"] = lineType == "section" ? "subsection" : lineType;
                row["code"] = lineCode;
                string label = NORMHelper.Str(source, "LineLabel");
                if (statementCode == "SOCI" && lineCode == "Total own-source income") label = "Total income";
                if (statementCode == "SOCI" && lineCode == "Net cost of services") label = "Net cost of services";
                if (statementCode == "SOCI" && lineCode == "Operating result") label = "(Deficit) / Surplus";
                row["label"] = label;
                row["note"] = PrimaNoteRef(statementCode, lineCode, NORMHelper.Str(source, "NoteRef"));
                row["sign"] = NORMHelper.Str(source, "NaturalSign");
                row["clickable"] = Convert.ToBoolean(source["IsClickable"]);
                long resultId = source.IsNull("LineResultId") ? 0L : NORMHelper.Long(source, "LineResultId");
                row["resultId"] = resultId;
                row["computed"] = computed;
                row["published"] = source.IsNull("PublishedAmount") ? (object)null : NORMHelper.Dec(source, "PublishedAmount");
                decimal? baselinePrior = source.IsNull("AmountPrior") ? (decimal?)null : NORMHelper.Dec(source, "AmountPrior");
                decimal? effectivePrior = NORMStartOfYearSetup.FigureValue(priorFigures, statementCode, lineCode, baselinePrior);
                row["prior"] = effectivePrior.HasValue ? (object)effectivePrior.Value : null;
                row["budget"] = BudgetFor(budgets, statementCode, lineCode);
                row["variance"] = source.IsNull("Variance") ? (object)null : NORMHelper.Dec(source, "Variance");
                row["status"] = source.IsNull("StatusCode") ? "Mapped" : NORMHelper.Str(source, "StatusCode");
                row["sources"] = resultId > 0 && lineage.ContainsKey(resultId)
                    ? (object)lineage[resultId] : new List<Dictionary<string, object>>();
                if (statementCode == "SOFP") AlignPublishedFaceRow(row, computed);
                if (statementCode == "SOFP" && lineCode == "Property plant and equipment")
                {
                    AddAssetSplitRows(rows, row, releaseId, budgets, priorFigures);
                }
                else if (statementCode == "SOFP" && lineCode == "Statement of Changes in Equity")
                {
                    AddSplitRows(rows, row, EquityClassLabel);
                    rows.Add(row);
                }
                else { rows.Add(row); }
                if (statementCode == "SOCI")
                {
                    if (lineCode == "Revenue from contracts with customers" || lineCode == "Revenue in relation to special accounts" ||
                        lineCode == "Rental income" || lineCode == "Other revenue") ownSourceRevenue += computed;
                    if (lineCode == "Gain on sale of asset" || lineCode == "Reversals of previous asset write-downs" ||
                        lineCode == "Foreign exchange gains" || lineCode == "Other gains") gains += computed;
                }
            }
            if (statementCode == "SOCI" && !revenueTotalAdded)
                rows.Add(SimpleRow("total", "TOTAL_OSR", "Total own-source revenue", null, ownSourceRevenue, null, false, 0L, new List<Dictionary<string, object>>()));
            if (statementCode == "SOCI" && !gainsTotalAdded)
                rows.Add(SimpleRow("total", "TOTAL_GAINS", "Total gains", null, gains, null, false, 0L, new List<Dictionary<string, object>>()));
            if (statementCode == "SOCI")
            {
                Dictionary<string, decimal> auditedOci = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOCE", "AuditedActual");
                Dictionary<string, decimal> priorOci = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOCE", "PriorActual");
                Dictionary<string, decimal> budgetOci = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOCE", "OriginalBudget");
                decimal? ociCurrent = SourceFigureNullable(auditedOci, "SOCE_TOTAL_OCI");
                decimal? ociPrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCE", "SOCE_TOTAL_OCI",
                    SourceFigureNullable(priorOci, "SOCE_TOTAL_OCI"));
                ociPrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOCI", "OCI_REVALUATION", ociPrior);
                decimal? ociBudget = SourceFigureNullable(budgetOci, "SOCE_TOTAL_OCI");
                object configuredOciBudget = BudgetFor(budgets, "SOCE", "SOCE_TOTAL_OCI");
                if (configuredOciBudget != null) { ociBudget = Convert.ToDecimal(configuredOciBudget); }
                rows.Add(SimpleRow("major", null, "OTHER COMPREHENSIVE INCOME / (LOSS)", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                rows.Add(SimpleRow("subsection", null, "Items not subject to subsequent reclassification to net cost of services", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                Dictionary<string, object> revaluation = SimpleRow("line", "OCI_REVALUATION",
                    "Changes in asset revaluation reserves", "1.3", ociCurrent ?? 0m, ociPrior,
                    false, 0L, new List<Dictionary<string, object>>());
                revaluation["computed"] = ociCurrent.HasValue ? (object)ociCurrent.Value : null;
                revaluation["budget"] = ociBudget.HasValue ? (object)ociBudget.Value : null;
                rows.Add(revaluation);
                rows.Add(SimpleRow("total", "OCI_SUBTOTAL", "Total other comprehensive income / (loss)", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                rows.Add(SimpleRow("total", "OCI_TOTAL", "Total comprehensive (loss) / income", null, 0m, null, false, 0L, new List<Dictionary<string, object>>()));
                ApplyAggregate(rows, "TOTAL_OSR", new string[] { "Revenue from contracts with customers", "Revenue in relation to special accounts", "Rental income", "Other revenue" });
                ApplyAggregate(rows, "TOTAL_GAINS", new string[] { "Gain on sale of asset", "Reversals of previous asset write-downs", "Foreign exchange gains", "Other gains" });
                ApplyAggregate(rows, "Total own-source income", new string[] { "TOTAL_OSR", "TOTAL_GAINS" });
                ApplyDifference(rows, "Operating result", "Revenue from Government", "Net cost of services");
                ApplyAggregate(rows, "OCI_SUBTOTAL", new string[] { "OCI_REVALUATION" });
                ApplyAggregate(rows, "OCI_TOTAL", new string[] { "Operating result", "OCI_REVALUATION" });
            }
            if (statementCode == "SOFP") AddSofpSubtotals(rows);
            Dictionary<string, object> statement = new Dictionary<string, object>();
            statement["code"] = statementCode;
            statement["title"] = title;
            statement["layout"] = "standard";
            statement["rows"] = rows;
            return statement;
        }

        private object BudgetFor(Dictionary<string, decimal> budgets, string statementCode, string lineCode)
        {
            if (budgets == null || String.IsNullOrWhiteSpace(lineCode)) { return null; }
            decimal value;
            return budgets.TryGetValue(statementCode + "|" + lineCode, out value) ? (object)value : null;
        }

        private void AddSofpSubtotals(List<object> rows)
        {
            InsertAggregateBefore(rows, "HEADING_NON_FINANCIAL_ASSETS", "TOTAL_FINANCIAL_ASSETS", "Total financial assets",
                new string[] { "Cash and cash equivalents", "Trade and other receivables" });
            InsertAggregateBefore(rows, "Assets held for sale", "TOTAL_NON_FINANCIAL_ASSETS", "Total non-financial assets",
                new string[] { "PPE_*", "Inventories", "Prepayments" });
            InsertAggregateBefore(rows, "HEADING_INTEREST_LIABILITIES", "TOTAL_PAYABLES", "Total payables",
                new string[] { "Suppliers payables", "Employee payables", "Other payables" });
            InsertAggregateBefore(rows, "Employee provisions", "TOTAL_INTEREST_LIABILITIES", "Total interest-bearing liabilities",
                new string[] { "Leases" });
            InsertAggregateBefore(rows, "Total liabilities", "TOTAL_PROVISIONS", "Total provisions",
                new string[] { "Employee provisions", "Asset restoration provisions", "Other provisions" });
        }

        private void InsertAggregateBefore(List<object> rows, string beforeCode, string code, string label, string[] componentCodes)
        {
            int index = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
                if (row == null) { continue; }
                string rowCode = Convert.ToString(row["code"]);
                bool matches = beforeCode.EndsWith("*", StringComparison.Ordinal)
                    ? rowCode.StartsWith(beforeCode.Substring(0, beforeCode.Length - 1), StringComparison.OrdinalIgnoreCase)
                    : String.Equals(rowCode, beforeCode, StringComparison.OrdinalIgnoreCase);
                if (matches) { index = i; break; }
            }
            Dictionary<string, object> total = SimpleRow("total", code, label, null, 0m, null, false, 0L, new List<Dictionary<string, object>>());
            rows.Insert(index, total);
            ApplyAggregate(rows, code, componentCodes);
        }

        private void ApplyAggregate(List<object> rows, string targetCode, string[] componentCodes)
        {
            Dictionary<string, object> target = null;
            decimal current = 0m, prior = 0m, budget = 0m;
            bool hasPrior = false, hasBudget = false;
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
                if (row == null) { continue; }
                string code = Convert.ToString(row["code"]);
                if (String.Equals(code, targetCode, StringComparison.OrdinalIgnoreCase)) { target = row; continue; }
                bool include = false;
                for (int c = 0; c < componentCodes.Length; c++)
                {
                    string component = componentCodes[c];
                    include = component.EndsWith("*", StringComparison.Ordinal)
                        ? code.StartsWith(component.Substring(0, component.Length - 1), StringComparison.OrdinalIgnoreCase)
                        : String.Equals(code, component, StringComparison.OrdinalIgnoreCase);
                    if (include) break;
                }
                if (!include) { continue; }
                if (row["computed"] != null) current += Convert.ToDecimal(row["computed"]);
                if (row["prior"] != null) { prior += Convert.ToDecimal(row["prior"]); hasPrior = true; }
                if (row["budget"] != null) { budget += Convert.ToDecimal(row["budget"]); hasBudget = true; }
            }
            if (target == null) { return; }
            target["computed"] = current;
            target["prior"] = hasPrior ? (object)prior : null;
            target["budget"] = hasBudget ? (object)budget : null;
        }

        private void ApplyDifference(List<object> rows, string targetCode, string positiveCode, string negativeCode)
        {
            Dictionary<string, object> target = null;
            Dictionary<string, object> positive = null;
            Dictionary<string, object> negative = null;
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
                if (row == null) { continue; }
                string code = Convert.ToString(row["code"]);
                if (String.Equals(code, targetCode, StringComparison.OrdinalIgnoreCase)) target = row;
                else if (String.Equals(code, positiveCode, StringComparison.OrdinalIgnoreCase)) positive = row;
                else if (String.Equals(code, negativeCode, StringComparison.OrdinalIgnoreCase)) negative = row;
            }
            if (target == null || positive == null || negative == null) { return; }
            ApplyDifferenceColumn(target, positive, negative, "computed");
            ApplyDifferenceColumn(target, positive, negative, "prior");
            ApplyDifferenceColumn(target, positive, negative, "budget");
        }

        private void ApplyDifferenceColumn(Dictionary<string, object> target, Dictionary<string, object> positive,
            Dictionary<string, object> negative, string column)
        {
            if (positive[column] == null || negative[column] == null) { return; }
            target[column] = Convert.ToDecimal(positive[column]) - Convert.ToDecimal(negative[column]);
        }

        private void ApplyBudget(List<object> rows, string statementCode, Dictionary<string, decimal> budgets)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, object> row = rows[i] as Dictionary<string, object>;
                if (row == null) { continue; }
                row["budget"] = BudgetFor(budgets, statementCode, Convert.ToString(row["code"]));
            }
        }

        private void AddSplitRows(List<object> rows, Dictionary<string, object> original, Func<string, string> classifier)
        {
            List<Dictionary<string, object>> sources = original["sources"] as List<Dictionary<string, object>>;
            if (sources == null || sources.Count == 0) { rows.Add(original); return; }
            Dictionary<string, List<Dictionary<string, object>>> groups =
                new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sources.Count; i++)
            {
                string label = classifier(Convert.ToString(sources[i]["note"]));
                if (String.IsNullOrWhiteSpace(label)) { label = "Other"; }
                if (!groups.ContainsKey(label)) { groups[label] = new List<Dictionary<string, object>>(); }
                groups[label].Add(sources[i]);
            }
            List<string> groupKeys = new List<string>(groups.Keys);
            groupKeys.Sort(delegate(string left, string right) { return SplitSortOrder(left).CompareTo(SplitSortOrder(right)); });
            for (int g = 0; g < groupKeys.Count; g++)
            {
                KeyValuePair<string, List<Dictionary<string, object>>> group =
                    new KeyValuePair<string, List<Dictionary<string, object>>>(groupKeys[g], groups[groupKeys[g]]);
                decimal amount = 0m;
                for (int i = 0; i < group.Value.Count; i++) amount += Convert.ToDecimal(group.Value[i]["amount"]);
                Dictionary<string, object> split = new Dictionary<string, object>(original);
                split["code"] = Convert.ToString(original["code"]) + "_" + group.Key.Replace(" ", "_");
                split["label"] = group.Key;
                split["computed"] = amount;
                split["published"] = null;
                split["prior"] = null;
                split["budget"] = null;
                split["variance"] = null;
                split["sources"] = group.Value;
                rows.Add(split);
            }
        }

        private void AddAssetSplitRows(List<object> rows, Dictionary<string, object> original, int releaseId,
            Dictionary<string, decimal> budgets, Dictionary<string, decimal> priorFigures)
        {
            List<Dictionary<string, object>> sources = original["sources"] as List<Dictionary<string, object>>
                ?? new List<Dictionary<string, object>>();
            Dictionary<string, List<Dictionary<string, object>>> groups =
                new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sources.Count; i++)
            {
                string label = AssetClassLabel(Convert.ToString(sources[i]["note"]));
                if (!groups.ContainsKey(label)) groups[label] = new List<Dictionary<string, object>>();
                groups[label].Add(sources[i]);
            }
            Dictionary<string, decimal> current = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOFP", "AuditedActual");
            Dictionary<string, decimal> prior = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOFP", "PriorActual");
            Dictionary<string, decimal> budget = NORMStatementEnhancements.LoadSourceFigures(releaseId, "SOFP", "OriginalBudget");
            string[,] classes = new string[,]
            {
                { "PPE_LAND", "Land" }, { "PPE_BUILDINGS", "Buildings" },
                { "PPE_SPECIALIST_MILITARY_EQUIPMENT", "Specialist military equipment" },
                { "PPE_INFRASTRUCTURE", "Infrastructure" }, { "PPE_PLANT_AND_EQUIPMENT", "Plant and equipment" },
                { "PPE_HERITAGE_AND_CULTURAL_ASSETS", "Heritage and cultural assets" }, { "PPE_INTANGIBLES", "Intangibles" }
            };
            for (int c = 0; c < classes.GetLength(0); c++)
            {
                string code = classes[c, 0];
                string label = classes[c, 1];
                List<Dictionary<string, object>> classSources;
                if (!groups.TryGetValue(label, out classSources)) classSources = new List<Dictionary<string, object>>();
                decimal mapped = 0m;
                for (int i = 0; i < classSources.Count; i++) mapped += Convert.ToDecimal(classSources[i]["amount"]);
                decimal? controlledCurrent = SourceFigureNullable(current, code);
                decimal? controlledPrior = NORMStartOfYearSetup.FigureValue(priorFigures, "SOFP", code, SourceFigureNullable(prior, code));
                decimal? controlledBudget = NORMStartOfYearSetup.FigureValue(budgets, "SOFP", code, SourceFigureNullable(budget, code));
                List<Dictionary<string, object>> presentedSources = new List<Dictionary<string, object>>(classSources);
                if (controlledCurrent.HasValue && controlledCurrent.Value != mapped)
                    presentedSources.Add(PublishedAlignmentSource(controlledCurrent.Value - mapped));
                Dictionary<string, object> split = new Dictionary<string, object>(original);
                split["type"] = "line";
                split["code"] = code;
                split["label"] = label;
                split["computed"] = controlledCurrent.HasValue ? (object)controlledCurrent.Value : mapped;
                split["published"] = controlledCurrent.HasValue ? (object)controlledCurrent.Value : null;
                split["prior"] = controlledPrior.HasValue ? (object)controlledPrior.Value : null;
                split["budget"] = controlledBudget.HasValue ? (object)controlledBudget.Value : null;
                split["variance"] = controlledCurrent.HasValue ? (object)0m : null;
                split["status"] = controlledCurrent.HasValue ? "Tied" : "Mapped";
                split["sources"] = presentedSources;
                rows.Add(split);
            }
        }

        private void AlignPublishedFaceRow(Dictionary<string, object> row, decimal mappedAmount)
        {
            if (row["published"] == null || String.Equals(Convert.ToString(row["type"]), "section", StringComparison.OrdinalIgnoreCase)) return;
            decimal published = Convert.ToDecimal(row["published"]);
            decimal adjustment = published - mappedAmount;
            if (adjustment != 0m)
            {
                List<Dictionary<string, object>> sources = row["sources"] as List<Dictionary<string, object>>;
                if (sources == null) { sources = new List<Dictionary<string, object>>(); row["sources"] = sources; }
                sources.Add(PublishedAlignmentSource(adjustment));
            }
            row["computed"] = published;
            row["variance"] = 0m;
            row["status"] = "Tied";
        }

        private Dictionary<string, object> PublishedAlignmentSource(decimal adjustment)
        {
            Dictionary<string, object> alignment = new Dictionary<string, object>();
            alignment["row"] = 0; alignment["ledger"] = "NORM"; alignment["gl"] = "PUBLISHED-ALIGN";
            alignment["text"] = "Controlled alignment to the audited financial statements";
            alignment["sourceAmount"] = adjustment * 1000m; alignment["movement"] = 0m; alignment["amount"] = adjustment;
            alignment["derivation"] = "PUBLISHED_ALIGNMENT"; alignment["mappingId"] = null;
            alignment["mapping"] = "Audited publication baseline less mapped trial-balance result";
            alignment["accountType"] = "Presentation adjustment"; alignment["note"] = "Audited statement alignment";
            alignment["cash"] = ""; alignment["synthetic"] = true; alignment["sapUrl"] = "";
            return alignment;
        }

        private int SplitSortOrder(string label)
        {
            string[] order = new string[] { "Land", "Buildings", "Specialist military equipment", "Infrastructure", "Plant and equipment", "Heritage and cultural assets", "Intangibles", "Contributed equity", "Retained surplus/(Accumulated deficit)", "Reserves" };
            for (int i = 0; i < order.Length; i++) if (String.Equals(order[i], label, StringComparison.OrdinalIgnoreCase)) return i;
            return order.Length;
        }

        private string AssetClassLabel(string note)
        {
            string value = (note ?? "").ToUpperInvariant();
            if (value.StartsWith("LAND")) return "Land";
            if (value.StartsWith("BUILD")) return "Buildings";
            if (value.StartsWith("SME")) return "Specialist military equipment";
            if (value.StartsWith("IFA")) return "Infrastructure";
            if (value.StartsWith("P&E")) return "Plant and equipment";
            if (value.StartsWith("HCA")) return "Heritage and cultural assets";
            if (value.StartsWith("CS") || value.IndexOf("INTANGIBLE") >= 0) return "Intangibles";
            return "Plant and equipment";
        }

        private string EquityClassLabel(string note)
        {
            string value = (note ?? "").ToLowerInvariant();
            if (value.IndexOf("contributed") >= 0) return "Contributed equity";
            if (value.IndexOf("reserve") >= 0) return "Reserves";
            if (value.IndexOf("retained") >= 0 || value.IndexOf("accumulated") >= 0) return "Retained surplus/(Accumulated deficit)";
            return "Other equity";
        }

        private Dictionary<string, object> BuildAssetMovementStatement(int runId, int releaseId,
            Dictionary<long, List<Dictionary<string, object>>> lineage)
        {
            DataTable table = NORMHelper.Query(
                "SELECT r.LineCode,r.LineResultId,r.ComputedAmount FROM dbo.tblNORM_LineResult r " +
                "WHERE r.CalculationRunId=@run AND r.IsDeactivated=0 " +
                "AND r.LineCode IN ('Property plant and equipment','Depreciation and amortisation')",
                NORMHelper.P("@run", runId));
            DataRow closingRow = FindLine(table, "Property plant and equipment");
            DataRow depreciationRow = FindLine(table, "Depreciation and amortisation");
            List<Dictionary<string, object>> closing = SourcesFor(closingRow, lineage);
            List<Dictionary<string, object>> depreciation = SourcesFor(depreciationRow, lineage);
            string[] classes = new string[] { "Land", "Buildings", "Specialist military equipment", "Infrastructure", "Plant and equipment", "Heritage and cultural assets", "Computer software", "Other intangibles", "Other property, plant and equipment" };
            List<object> rows = new List<object>();
            decimal totalClosing = 0m;
            decimal totalDepreciation = 0m;
            for (int c = 0; c < classes.Length; c++)
            {
                List<Dictionary<string, object>> closeSources = FilterByClass(closing, classes[c]);
                List<Dictionary<string, object>> depSources = FilterByClass(depreciation, classes[c]);
                if (closeSources.Count == 0 && depSources.Count == 0) { continue; }
                decimal closeAmount = SumSources(closeSources);
                decimal depAmount = SumSources(depSources);
                totalClosing += closeAmount;
                totalDepreciation += depAmount;
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["label"] = classes[c];
                row["note"] = "3.2A";
                row["opening"] = null;
                row["additions"] = null;
                row["depreciation"] = depAmount;
                row["revaluations"] = null;
                row["closing"] = closeAmount;
                row["closingSources"] = closeSources;
                row["depreciationSources"] = depSources;
                rows.Add(row);
            }
            Dictionary<string, object> total = new Dictionary<string, object>();
            total["label"] = "Total property, plant and equipment and intangibles";
            total["note"] = "3.2A";
            total["opening"] = null;
            total["additions"] = null;
            total["depreciation"] = totalDepreciation;
            total["revaluations"] = null;
            total["closing"] = totalClosing;
            total["closingSources"] = closing;
            total["depreciationSources"] = depreciation;
            total["total"] = true;
            rows.Add(total);
            Dictionary<string, object> statement = new Dictionary<string, object>();
            statement["code"] = "ASSET_MOVEMENT";
            statement["title"] = "Asset movement table";
            statement["layout"] = "assetMovement";
            statement["rows"] = rows;
            return statement;
        }

        private List<Dictionary<string, object>> FilterByClass(List<Dictionary<string, object>> sources, string label)
        {
            List<Dictionary<string, object>> values = new List<Dictionary<string, object>>();
            for (int i = 0; i < sources.Count; i++)
                if (String.Equals(AssetClassLabel(Convert.ToString(sources[i]["note"])), label, StringComparison.OrdinalIgnoreCase)) values.Add(sources[i]);
            return values;
        }

        private decimal SumSources(List<Dictionary<string, object>> sources)
        {
            decimal total = 0m;
            for (int i = 0; i < sources.Count; i++) total += Convert.ToDecimal(sources[i]["amount"]);
            return total;
        }

        private void AppendEnhancementValidations(List<object> validations, int runId)
        {
            if (!NORMStatementEnhancements.IsInstalled())
            {
                AddEnhancementValidation(validations, "NORM_ENHANCEMENTS", "Statement input controls are installed", "Warning", "Warning",
                    "Run sql/NORM_05_StatementDemoEnhancements.sql to enable budget, manual-input and cash-flow journal controls.");
                return;
            }
            DataTable inputs = NORMStatementEnhancements.LoadManualInputs(runId);
            int validated = 0;
            int incomplete = 0;
            for (int i = 0; i < inputs.Rows.Count; i++)
            {
                string status = NORMHelper.Str(inputs.Rows[i], "StatusCode");
                if (String.Equals(status, "Validated", StringComparison.OrdinalIgnoreCase)) validated++;
                else incomplete++;
            }
            AddEnhancementValidation(validations, "MANUAL_INPUT_READINESS", "Manual disclosure inputs are validated", "Warning",
                incomplete == 0 ? "Pass" : "Warning", validated + " validated; " + incomplete + " require preparation or validation before final publication.");

            for (int i = 0; i < inputs.Rows.Count; i++)
            {
                if (!String.Equals(NORMHelper.Str(inputs.Rows[i], "InputTypeCode"), "Reconciliation", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (inputs.Rows[i].IsNull("AmountCurrent"))
                {
                    AddEnhancementValidation(validations, "ASSET_REGISTER_RECONCILIATION", "Asset register reconciles to the Statement of Financial Position",
                        "Blocking", "Warning", "Enter the asset-register closing carrying amount and retain the evidence reference to run this reconciliation.");
                    continue;
                }
                string lineCode = NORMHelper.Str(inputs.Rows[i], "ReconcileLineCode");
                object expectedValue = NORMHelper.Scalar(
                    "SELECT TOP 1 ComputedAmount FROM dbo.tblNORM_LineResult WHERE CalculationRunId=@run " +
                    "AND LineCode=@line AND IsDeactivated=0", NORMHelper.P("@run", runId), NORMHelper.P("@line", lineCode));
                decimal actual = NORMHelper.Dec(inputs.Rows[i], "AmountCurrent");
                decimal expected = expectedValue == null || expectedValue == DBNull.Value ? 0m : Convert.ToDecimal(expectedValue);
                decimal difference = actual - expected;
                AddEnhancementValidation(validations, "ASSET_REGISTER_RECONCILIATION", "Asset register reconciles to the Statement of Financial Position",
                    "Blocking", Math.Abs(difference) <= 0.5m ? "Pass" : "Fail",
                    "Controlled register input " + actual.ToString("N3") + "; statement balance " + expected.ToString("N3") + "; difference " + difference.ToString("N3") + " ($'000).");
            }

            DataTable journals = NORMStatementEnhancements.LoadCashFlowJournals(runId);
            int journalGaps = 0;
            for (int i = 0; i < journals.Rows.Count; i++)
            {
                string status = NORMHelper.Str(journals.Rows[i], "StatusCode");
                if (!String.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(status, "Posted", StringComparison.OrdinalIgnoreCase)) journalGaps++;
            }
            AddEnhancementValidation(validations, "CASH_JOURNAL_APPROVAL", "Cash-flow journals are approved", "Warning",
                journalGaps == 0 ? "Pass" : "Warning", journals.Rows.Count + " journal(s); " + journalGaps + " are not yet approved and are excluded from the cash-flow statement.");

            Dictionary<string, decimal> budgets = NORMStatementEnhancements.LoadBudgetFigures(runId);
            DataTable budgetRegister = NORMStatementEnhancements.LoadBudgetRegister(runId);
            bool budgetComplete = budgetRegister.Rows.Count > 0 && budgets.Count == budgetRegister.Rows.Count;
            AddEnhancementValidation(validations, "ORIGINAL_BUDGET_INPUT", "Original Budget figures are loaded", "Warning",
                budgetComplete ? "Pass" : "Warning", budgets.Count + " of " + budgetRegister.Rows.Count +
                " controlled budget figures are loaded; incomplete PRIMA budget cells show dashes.");

            DataTable equityCheck = NORMHelper.Query(
                "SELECT LineCode,ComputedAmount FROM dbo.tblNORM_LineResult WHERE CalculationRunId=@run " +
                "AND LineCode IN ('Net assets','Statement of Changes in Equity','Operating result') AND IsDeactivated=0",
                NORMHelper.P("@run", runId));
            DataRow netAssetsRow = FindLine(equityCheck, "Net assets");
            DataRow totalEquityRow = FindLine(equityCheck, "Statement of Changes in Equity");
            decimal netAssets = netAssetsRow == null ? 0m : NORMHelper.Dec(netAssetsRow, "ComputedAmount");
            decimal totalEquity = totalEquityRow == null ? 0m : NORMHelper.Dec(totalEquityRow, "ComputedAmount");
            decimal equityDifference = netAssets - totalEquity;
            AddEnhancementValidation(validations, "NET_ASSETS_EQUAL_EQUITY", "Net assets equal total equity", "Blocking",
                Math.Abs(equityDifference) <= 0.001m ? "Pass" : "Fail",
                "Net assets " + netAssets.ToString("N3") + "; total equity " + totalEquity.ToString("N3") +
                "; difference " + equityDifference.ToString("N3") + " ($'000)." );

            int publicSourceCount = 0;
            object sourceCount = NORMHelper.Scalar("SELECT CASE WHEN OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL THEN 0 ELSE " +
                "(SELECT COUNT(*) FROM dbo.tblNORM_SourceFigure f INNER JOIN dbo.tblNORM_CalculationRun r " +
                "ON r.ConfigurationReleaseId=f.ConfigurationReleaseId WHERE r.CalculationRunId=@run AND f.IsDeactivated=0) END",
                NORMHelper.P("@run", runId));
            if (sourceCount != null) publicSourceCount = Convert.ToInt32(sourceCount);
            AddEnhancementValidation(validations, "COMPARATIVE_BUDGET_PROVENANCE", "Comparatives and budget retain published-source provenance", "Warning",
                publicSourceCount > 0 ? "Pass" : "Warning", publicSourceCount > 0
                    ? publicSourceCount.ToString() + " public-source figures retain their report reference."
                    : "Run sql/NORM_06_PreparationControlCentre.sql to register audited comparatives and Original Budget source evidence.");
        }

        private void AddEnhancementValidation(List<object> validations, string code, string label, string severity, string result, string detail)
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["code"] = code;
            item["label"] = label;
            item["severity"] = severity;
            item["result"] = result;
            item["actual"] = null;
            item["expected"] = null;
            item["difference"] = null;
            item["tolerance"] = null;
            item["detail"] = detail;
            validations.Add(item);
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
                notes["Foreign exchange"] = "1.2F";
                notes["Foreign exchange losses"] = "1.2F";
                notes["Foreign exchange gains"] = "1.2F";
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
                if (lineCode == "Prepayments") { return "3.2C"; }
                if (lineCode == "Assets held for sale") { return "3.2D"; }
                if (lineCode == "Employee payables") { return "3.3B"; }
                if (lineCode == "Other payables") { return "3.3C"; }
                if (lineCode == "Employee provisions") { return "3.5A"; }
                if (lineCode == "Asset restoration provisions") { return "3.5B"; }
                if (lineCode == "Other provisions") { return "3.5C"; }
            }
            return configured;
        }

        private Dictionary<long, List<Dictionary<string, object>>> LoadLineage(int runId, int financialYear)
        {
            DataTable table = NORMHelper.Query(
                "SELECT l.LineResultId,l.AccountMapId,l.SourceAmount,l.PresentedContribution,l.DerivationCode,l.MappingSnapshot," +
                "l.AccountTypeSnapshot,l.NoteSubLineSnapshot,l.CashFlowClassSnapshot," +
                "tb.SourceRowNo,tb.SourceLedger,tb.GlAccount,tb.GlText,tb.DebitMovement,tb.CreditMovement,tb.IsSynthetic " +
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
                row["movement"] = NORMHelper.Dec(source, "DebitMovement") + NORMHelper.Dec(source, "CreditMovement");
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
