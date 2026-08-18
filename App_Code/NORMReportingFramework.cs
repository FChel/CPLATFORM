using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;

/// <summary>
/// Entity profile and PRIMA disclosure planner. The catalogue is stored against
/// the FY configuration release; a calculation run supplies figures, narratives
/// and workflow evidence without changing the approved accounting mapping.
/// </summary>
public static class NORMReportingFramework
{
    public class CapabilityDefinition
    {
        public string Code;
        public string Label;
        public string Detail;
    }

    public class ReportingProfile
    {
        public int ReleaseId;
        public string EntityType;
        public string ReportingBasis;
        public string DisclosureTier;
        public string MaterialityBasis;
        public decimal? OverallMateriality;
        public decimal? PerformanceMateriality;
        public decimal? ClearlyTrivialThreshold;
        public decimal? BudgetVarianceThreshold;
        public string QualitativeConsiderations;
        public Dictionary<string, bool> Requirements = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Rationales = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class NoteLine
    {
        public string Label;
        public decimal Amount;
        public decimal? Prior;
        public int SourceCount;
        public string SourceLineCode;
    }

    public class Disclosure
    {
        public string Code;
        public string SectionCode;
        public string SectionTitle;
        public string NoteRef;
        public string Title;
        public string TriggerCode;
        public string Guidance;
        public bool Required;
        public bool RequiresNarrative;
        public int SortOrder;
        public int SourceCount;
        public decimal Amount;
        public decimal? PriorAmount;
        public string NarrativeType;
        public string Narrative;
        public string NarrativeStatus;
        public string CompletionStatus;
        public bool Suggested;
        public bool PotentiallyImmaterial;
        public string RequirementReason;
        public List<NoteLine> Lines = new List<NoteLine>();
    }

    private class SourceBucket
    {
        public string NoteRef;
        public string LineCode;
        public string SubLine;
        public decimal Amount;
        public int SourceCount;
    }

    public static bool IsInstalled()
    {
        object value = NORMHelper.Scalar(
            "SELECT CASE WHEN OBJECT_ID('dbo.tblNORM_ReportingProfile','U') IS NOT NULL " +
            "AND OBJECT_ID('dbo.tblNORM_DisclosureRule','U') IS NOT NULL THEN 1 ELSE 0 END");
        return value != null && Convert.ToInt32(value) == 1;
    }

    public static List<CapabilityDefinition> CapabilityDefinitions()
    {
        List<CapabilityDefinition> values = new List<CapabilityDefinition>();
        AddCapability(values, "APPROPRIATIONS", "Appropriations", "Annual and special appropriations, including agent disclosures.");
        AddCapability(values, "ADMINISTERED_ACTIVITIES", "Administered activities", "Schedules and notes for items administered on behalf of Government.");
        AddCapability(values, "SPECIAL_ACCOUNTS", "Special accounts", "Special account balances, receipts and payments.");
        AddCapability(values, "INVESTMENTS", "Investments", "Investments, financial assets, interest and dividend disclosures.");
        AddCapability(values, "CONSOLIDATION", "Controlled entities", "Consolidated financial statements and controlled-entity disclosures.");
        AddCapability(values, "HERITAGE_ASSETS", "Heritage assets", "Heritage and cultural asset classes and movements.");
        AddCapability(values, "INTANGIBLE_ASSETS", "Intangible assets", "Software and other intangible asset classes and movements.");
        AddCapability(values, "MILITARY_ASSETS", "Specialist military assets", "Specialist military equipment and related movement tables.");
        AddCapability(values, "BIOLOGICAL_ASSETS", "Biological assets", "Biological asset measurement and movement disclosures.");
        AddCapability(values, "SERVICE_CONCESSIONS", "Service concession arrangements", "Grantor accounting and service concession disclosures.");
        AddCapability(values, "LEASES", "Leases", "Right-of-use assets, lease liabilities, rental income and maturity tables.");
        AddCapability(values, "GRANTS", "Grants", "Grant expenses, income and accounting policies.");
        AddCapability(values, "CONCESSIONAL_LOANS", "Concessional loans", "Concessional loan valuation and discount disclosures.");
        AddCapability(values, "FINANCIAL_INSTRUMENTS", "Financial instruments", "Classification, expected credit loss and financial risk disclosures.");
        AddCapability(values, "INVENTORIES", "Inventories", "Inventory classes, measurement and write-downs.");
        AddCapability(values, "CASH_ADMINISTERED", "Cash held for Government", "Cash administered or held in trust on behalf of Government.");
        AddCapability(values, "EMPLOYEE_BENEFITS", "Employee benefits", "Salary, superannuation, leave expense and provision disclosures.");
        AddCapability(values, "FAIR_VALUE", "Fair value measurement", "Fair value hierarchy and level 3 reconciliations.");
        AddCapability(values, "CONTINGENCIES", "Contingencies", "Contingent asset and liability assessment.");
        AddCapability(values, "OUTCOMES_REPORTING", "Outcome reporting", "Annual report net cost of outcome delivery.");
        return values;
    }

    private static void AddCapability(List<CapabilityDefinition> values, string code, string label, string detail)
    {
        CapabilityDefinition item = new CapabilityDefinition();
        item.Code = code;
        item.Label = label;
        item.Detail = detail;
        values.Add(item);
    }

    public static ReportingProfile LoadProfile(int releaseId)
    {
        ReportingProfile profile = new ReportingProfile();
        profile.ReleaseId = releaseId;
        profile.EntityType = "NCE";
        profile.ReportingBasis = "GPFS";
        profile.DisclosureTier = "FULL";
        profile.MaterialityBasis = "Materiality is assessed for each class of transactions, account balance and disclosure.";
        if (!IsInstalled()) { return profile; }

        DataTable header = NORMHelper.Query(
            "SELECT EntityTypeCode,ReportingBasisCode,DisclosureTierCode,MaterialityBasis,OverallMateriality," +
            "PerformanceMateriality,ClearlyTrivialThreshold,BudgetVarianceThreshold,QualitativeConsiderations " +
            "FROM dbo.tblNORM_ReportingProfile WHERE ConfigurationReleaseId=@release AND IsDeactivated=0",
            NORMHelper.P("@release", releaseId));
        if (header.Rows.Count > 0)
        {
            profile.EntityType = NORMHelper.Str(header.Rows[0], "EntityTypeCode");
            profile.ReportingBasis = NORMHelper.Str(header.Rows[0], "ReportingBasisCode");
            profile.DisclosureTier = NORMHelper.Str(header.Rows[0], "DisclosureTierCode");
            profile.MaterialityBasis = NORMHelper.Str(header.Rows[0], "MaterialityBasis");
            profile.OverallMateriality = NullableDecimal(header.Rows[0], "OverallMateriality");
            profile.PerformanceMateriality = NullableDecimal(header.Rows[0], "PerformanceMateriality");
            profile.ClearlyTrivialThreshold = NullableDecimal(header.Rows[0], "ClearlyTrivialThreshold");
            profile.BudgetVarianceThreshold = NullableDecimal(header.Rows[0], "BudgetVarianceThreshold");
            profile.QualitativeConsiderations = NORMHelper.Str(header.Rows[0], "QualitativeConsiderations");
        }

        DataTable requirements = NORMHelper.Query(
            "SELECT CapabilityCode,IsRequired,Rationale FROM dbo.tblNORM_RequirementSelection " +
            "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY CapabilityCode",
            NORMHelper.P("@release", releaseId));
        for (int i = 0; i < requirements.Rows.Count; i++)
        {
            DataRow row = requirements.Rows[i];
            string code = NORMHelper.Str(row, "CapabilityCode");
            profile.Requirements[code] = Convert.ToBoolean(row["IsRequired"]);
            profile.Rationales[code] = NORMHelper.Str(row, "Rationale");
        }
        return profile;
    }

    public static void SaveProfile(int releaseId, string entityType, string reportingBasis, string disclosureTier,
        string materialityBasis, decimal? overallMateriality, decimal? performanceMateriality,
        decimal? clearlyTrivialThreshold, decimal? budgetVarianceThreshold, string qualitativeConsiderations,
        Dictionary<string, bool> requirements, string updatedBy)
    {
        if (!IsInstalled()) { throw new InvalidOperationException("Install NORM_04_GovernmentReportingPlatform.sql first."); }
        entityType = Allowed(entityType, new string[] { "NCE", "CCE", "COMMONWEALTH_COMPANY" }, "NCE");
        reportingBasis = Allowed(reportingBasis, new string[] { "GPFS", "SPFS" }, "GPFS");
        disclosureTier = Allowed(disclosureTier, new string[] { "FULL", "REDUCED" }, "FULL");
        materialityBasis = Limit(materialityBasis, 1000);
        qualitativeConsiderations = Limit(qualitativeConsiderations, 2000);

        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                int changed = NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_ReportingProfile SET EntityTypeCode=@type,ReportingBasisCode=@basis," +
                    "DisclosureTierCode=@tier,MaterialityBasis=@materiality,OverallMateriality=@overall," +
                    "PerformanceMateriality=@performance,ClearlyTrivialThreshold=@trivial,BudgetVarianceThreshold=@budget," +
                    "QualitativeConsiderations=@qualitative,UpdatedBy=@user,UpdatedUtc=SYSUTCDATETIME() " +
                    "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0",
                    NORMHelper.P("@type", entityType), NORMHelper.P("@basis", reportingBasis),
                    NORMHelper.P("@tier", disclosureTier), NORMHelper.P("@materiality", materialityBasis),
                    NORMHelper.P("@overall", overallMateriality), NORMHelper.P("@performance", performanceMateriality),
                    NORMHelper.P("@trivial", clearlyTrivialThreshold), NORMHelper.P("@budget", budgetVarianceThreshold),
                    NORMHelper.P("@qualitative", qualitativeConsiderations),
                    NORMHelper.P("@user", updatedBy), NORMHelper.P("@release", releaseId));
                if (changed == 0)
                {
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_ReportingProfile " +
                        "(ConfigurationReleaseId,EntityTypeCode,ReportingBasisCode,DisclosureTierCode,MaterialityBasis," +
                        "OverallMateriality,PerformanceMateriality,ClearlyTrivialThreshold,BudgetVarianceThreshold,QualitativeConsiderations,UpdatedBy) " +
                        "VALUES (@release,@type,@basis,@tier,@materiality,@overall,@performance,@trivial,@budget,@qualitative,@user)",
                        NORMHelper.P("@release", releaseId), NORMHelper.P("@type", entityType),
                        NORMHelper.P("@basis", reportingBasis), NORMHelper.P("@tier", disclosureTier),
                        NORMHelper.P("@materiality", materialityBasis),
                        NORMHelper.P("@overall", overallMateriality), NORMHelper.P("@performance", performanceMateriality),
                        NORMHelper.P("@trivial", clearlyTrivialThreshold), NORMHelper.P("@budget", budgetVarianceThreshold),
                        NORMHelper.P("@qualitative", qualitativeConsiderations), NORMHelper.P("@user", updatedBy));
                }

                List<CapabilityDefinition> definitions = CapabilityDefinitions();
                for (int i = 0; i < definitions.Count; i++)
                {
                    string code = definitions[i].Code;
                    bool selected = requirements != null && requirements.ContainsKey(code) && requirements[code];
                    int updated = NORMHelper.Exec(connection, transaction,
                        "UPDATE dbo.tblNORM_RequirementSelection SET IsRequired=@required,UpdatedBy=@user," +
                        "UpdatedUtc=SYSUTCDATETIME(),IsDeactivated=0 WHERE ConfigurationReleaseId=@release AND CapabilityCode=@code",
                        NORMHelper.P("@required", selected), NORMHelper.P("@user", updatedBy),
                        NORMHelper.P("@release", releaseId), NORMHelper.P("@code", code));
                    if (updated == 0)
                    {
                        NORMHelper.Exec(connection, transaction,
                            "INSERT dbo.tblNORM_RequirementSelection " +
                            "(ConfigurationReleaseId,CapabilityCode,IsRequired,UpdatedBy) VALUES (@release,@code,@required,@user)",
                            NORMHelper.P("@release", releaseId), NORMHelper.P("@code", code),
                            NORMHelper.P("@required", selected), NORMHelper.P("@user", updatedBy));
                    }
                }
                NORMHelper.Exec(connection, transaction,
                    "INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
                    "VALUES ('REPORTING_PROFILE_UPDATED','ConfigurationRelease',@id,@detail,@user)",
                    NORMHelper.P("@id", releaseId.ToString(CultureInfo.InvariantCulture)),
                    NORMHelper.P("@detail", entityType + "; " + reportingBasis + "; " + disclosureTier + "."),
                    NORMHelper.P("@user", updatedBy));
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public static List<Disclosure> LoadDisclosures(int runId, int releaseId, ReportingProfile profile)
    {
        List<Disclosure> values = new List<Disclosure>();
        if (!IsInstalled()) { return values; }
        List<SourceBucket> sources = LoadSourceBuckets(runId);
        string entityCode = ResolveRunEntity(runId);
        Dictionary<string, decimal> priorNoteFigures = NORMStartOfYearSetup.LoadPriorNoteFigures(entityCode);
        Dictionary<string, string[]> narratives = LoadNarratives(runId, releaseId);
        DataTable table = NORMHelper.Query(
            "SELECT DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,TriggerCode," +
            "IsBaseRequired,RequiresNarrative,SortOrder,GuidanceText FROM dbo.tblNORM_DisclosureRule " +
            "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY SortOrder,DisclosureRuleId",
            NORMHelper.P("@release", releaseId));
        for (int i = 0; i < table.Rows.Count; i++)
        {
            DataRow row = table.Rows[i];
            Disclosure item = new Disclosure();
            item.Code = NORMHelper.Str(row, "DisclosureCode");
            item.SectionCode = NORMHelper.Str(row, "SectionCode");
            item.SectionTitle = NORMHelper.Str(row, "SectionTitle");
            item.NoteRef = NORMHelper.Str(row, "NoteRef");
            item.Title = NORMHelper.Str(row, "DisclosureTitle");
            item.TriggerCode = NORMHelper.Str(row, "TriggerCode");
            item.Guidance = NORMHelper.Str(row, "GuidanceText");
            item.RequiresNarrative = Convert.ToBoolean(row["RequiresNarrative"]);
            item.SortOrder = NORMHelper.Int(row, "SortOrder");
            bool baseRequired = Convert.ToBoolean(row["IsBaseRequired"]);
            bool triggerSelected = String.Equals(item.TriggerCode, "ALWAYS", StringComparison.OrdinalIgnoreCase) ||
                (profile.Requirements.ContainsKey(item.TriggerCode) && profile.Requirements[item.TriggerCode]);
            item.Required = baseRequired || triggerSelected;

            string[] narrative;
            if (narratives.TryGetValue(item.Code, out narrative))
            {
                item.NarrativeType = narrative[0];
                item.Narrative = narrative[1];
                item.NarrativeStatus = narrative[2];
            }
            AddMatchingSources(item, sources);
            ApplyPriorNoteFigures(item, priorNoteFigures);
            item.Suggested = item.SourceCount > 0;
            item.PotentiallyImmaterial = profile.OverallMateriality.HasValue && item.SourceCount > 0 &&
                Math.Abs(item.Amount) < profile.OverallMateriality.Value;
            if (baseRequired || String.Equals(item.TriggerCode, "ALWAYS", StringComparison.OrdinalIgnoreCase))
                item.RequirementReason = "Core PRIMA disclosure";
            else if (triggerSelected)
                item.RequirementReason = "Confirmed by the entity reporting profile";
            else if (item.Suggested)
                item.RequirementReason = item.SourceCount.ToString() + " mapped TB source rows suggest this disclosure";
            else
                item.RequirementReason = "No selected profile trigger or mapped balance";
            if (!item.Required) { item.CompletionStatus = "Not applicable"; }
            else if (item.SourceCount > 0 && (!item.RequiresNarrative || !String.IsNullOrWhiteSpace(item.Narrative)))
            {
                item.CompletionStatus = item.NarrativeStatus == "Approved" || item.NarrativeStatus == "Reviewed" ? item.NarrativeStatus : "Generated";
            }
            else if (!String.IsNullOrWhiteSpace(item.Narrative)) { item.CompletionStatus = "Draft"; }
            else { item.CompletionStatus = "Needs input"; }
            values.Add(item);
        }
        return values;
    }

    private static List<SourceBucket> LoadSourceBuckets(int runId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT ISNULL(s.NoteRef,'') AS NoteRef,ISNULL(s.LineCode,'') AS LineCode," +
            "ISNULL(l.NoteSubLineSnapshot,'Unclassified') AS SubLine,COUNT(*) AS SourceCount," +
            "SUM(l.PresentedContribution) AS Amount " +
            "FROM dbo.tblNORM_Lineage l INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
            "LEFT JOIN dbo.tblNORM_StatementLine s ON s.StatementLineId=r.StatementLineId " +
            "WHERE l.CalculationRunId=@run AND l.DerivationCode='GL_MAPPING' GROUP BY s.NoteRef,s.LineCode,l.NoteSubLineSnapshot " +
            "ORDER BY s.NoteRef,s.LineCode,ABS(SUM(l.PresentedContribution)) DESC",
            NORMHelper.P("@run", runId));
        List<SourceBucket> values = new List<SourceBucket>();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            SourceBucket item = new SourceBucket();
            item.NoteRef = NORMHelper.Str(table.Rows[i], "NoteRef") ?? "";
            item.LineCode = NORMHelper.Str(table.Rows[i], "LineCode") ?? "";
            item.SubLine = NORMHelper.Str(table.Rows[i], "SubLine") ?? "Unclassified";
            item.SourceCount = NORMHelper.Int(table.Rows[i], "SourceCount");
            item.Amount = NORMHelper.Dec(table.Rows[i], "Amount");
            values.Add(item);
        }
        return values;
    }

    private static void AddMatchingSources(Disclosure disclosure, List<SourceBucket> sources)
    {
        Dictionary<string, NoteLine> lines = new Dictionary<string, NoteLine>(StringComparer.OrdinalIgnoreCase);
        string disclosureTitle = Normalise(disclosure.Title);
        HashSet<string> expectedLines = ExpectedSourceLines(disclosure.Code);
        for (int i = 0; i < sources.Count; i++)
        {
            SourceBucket source = sources[i];
            string line = Normalise(source.LineCode);
            bool explicitlyMapped = expectedLines != null && expectedLines.Contains(source.LineCode);
            bool noteMatches = expectedLines == null && !String.IsNullOrWhiteSpace(disclosure.NoteRef) &&
                (String.Equals(disclosure.NoteRef, source.NoteRef, StringComparison.OrdinalIgnoreCase) ||
                 (disclosure.NoteRef.IndexOf('.') >= 0 && source.NoteRef.StartsWith(disclosure.NoteRef, StringComparison.OrdinalIgnoreCase)));
            bool titleMatches = line.Length > 4 && (disclosureTitle.IndexOf(line, StringComparison.Ordinal) >= 0 ||
                line.IndexOf(disclosureTitle, StringComparison.Ordinal) >= 0);
            if (!explicitlyMapped && !noteMatches && (expectedLines != null || !titleMatches)) { continue; }
            NoteLine noteLine;
            string lineKey = source.LineCode + "|" + source.SubLine;
            if (!lines.TryGetValue(lineKey, out noteLine))
            {
                noteLine = new NoteLine();
                noteLine.Label = source.SubLine;
                noteLine.SourceLineCode = source.LineCode;
                lines[lineKey] = noteLine;
                disclosure.Lines.Add(noteLine);
            }
            noteLine.Amount += source.Amount;
            noteLine.SourceCount += source.SourceCount;
            disclosure.Amount += source.Amount;
            disclosure.SourceCount += source.SourceCount;
        }
        MoveOtherLinesToEnd(disclosure.Lines);
    }

    private static void ApplyPriorNoteFigures(Disclosure disclosure, Dictionary<string, decimal> figures)
    {
        if (disclosure == null || figures == null || figures.Count == 0) { return; }
        decimal total = 0m;
        bool hasPrior = false;
        for (int i = 0; i < disclosure.Lines.Count; i++)
        {
            NoteLine line = disclosure.Lines[i];
            decimal amount;
            if (!String.IsNullOrWhiteSpace(line.SourceLineCode) &&
                figures.TryGetValue(line.SourceLineCode + "|" + line.Label, out amount))
            {
                line.Prior = amount;
                total += amount;
                hasPrior = true;
            }
        }
        if (hasPrior) { disclosure.PriorAmount = total; }
    }

    private static string ResolveRunEntity(int runId)
    {
        object value = NORMHelper.Scalar(
            "SELECT TOP 1 i.EntityCode FROM dbo.tblNORM_CalculationRun r " +
            "INNER JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId WHERE r.CalculationRunId=@run",
            NORMHelper.P("@run", runId));
        return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static HashSet<string> ExpectedSourceLines(string disclosureCode)
    {
        string[] lines = null;
        switch (disclosureCode)
        {
            case "N1_1A": lines = new string[] { "Employee benefits" }; break;
            case "N1_1B": lines = new string[] { "Supplier expenses" }; break;
            case "N1_1C": lines = new string[] { "Grants" }; break;
            case "N1_1D": lines = new string[] { "Finance costs" }; break;
            case "N1_1E": lines = new string[] { "Impairment loss on financial instruments" }; break;
            case "N1_1F": lines = new string[] { "Write-down of non-financial assets" }; break;
            case "N1_1G": lines = new string[] { "Foreign exchange losses" }; break;
            case "N1_1H": lines = new string[] { "Other expenses" }; break;
            case "N1_2A": lines = new string[] { "Revenue from contracts with customers" }; break;
            case "N1_2B": lines = new string[0]; break;
            case "N1_2C": lines = new string[0]; break;
            case "N1_2D": lines = new string[0]; break;
            case "N1_2E": lines = new string[] { "Rental income" }; break;
            case "N1_2F": lines = new string[] { "Other revenue" }; break;
            case "N1_2G": lines = new string[] { "Foreign exchange gains" }; break;
            case "N1_2H": lines = new string[] { "Reversals of previous asset write-downs" }; break;
            case "N1_2I": lines = new string[] { "Other gains" }; break;
            case "N1_2J": lines = new string[] { "Revenue from Government" }; break;
            case "N3_1A": lines = new string[] { "Cash and cash equivalents" }; break;
            case "N3_1B": lines = new string[] { "Trade and other receivables" }; break;
            case "N3_2A": lines = new string[] { "Property plant and equipment", "Depreciation and amortisation" }; break;
            case "N3_2B": lines = new string[] { "Inventories" }; break;
            case "N3_2C": lines = new string[] { "Prepayments", "Assets held for sale" }; break;
            case "N3_3": lines = new string[] { "Suppliers payables", "Employee payables", "Other payables" }; break;
            case "N3_4": lines = new string[] { "Leases" }; break;
            case "N3_5": lines = new string[] { "Employee provisions", "Asset restoration provisions", "Other provisions" }; break;
        }
        return lines == null ? null : new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
    }

    public static void MoveOtherLinesToEnd(List<NoteLine> lines)
    {
        if (lines == null || lines.Count < 2) { return; }
        List<NoteLine> other = new List<NoteLine>();
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string label = (lines[i].Label ?? "").Trim();
            if (label.StartsWith("Other", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(label, "Unclassified", StringComparison.OrdinalIgnoreCase))
            {
                other.Insert(0, lines[i]);
                lines.RemoveAt(i);
            }
        }
        lines.AddRange(other);
    }

    private static Dictionary<string, string[]> LoadNarratives(int runId, int releaseId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT t.DisclosureCode,t.NarrativeType,COALESCE(n.NarrativeText,prior.NarrativeText,t.TemplateText) AS NarrativeText," +
            "CASE WHEN n.RunNarrativeId IS NOT NULL THEN n.StatusCode WHEN prior.NarrativeText IS NOT NULL THEN 'RolledForward' ELSE 'Template' END AS StatusCode " +
            "FROM dbo.tblNORM_NarrativeTemplate t " +
            "LEFT JOIN dbo.tblNORM_RunNarrative n ON n.CalculationRunId=@run AND n.DisclosureCode=t.DisclosureCode " +
            "AND n.NarrativeType=t.NarrativeType AND n.IsDeactivated=0 " +
            "OUTER APPLY (SELECT TOP 1 pn.NarrativeText FROM dbo.tblNORM_RunNarrative pn " +
            "INNER JOIN dbo.tblNORM_CalculationRun pr ON pr.CalculationRunId=pn.CalculationRunId " +
            "INNER JOIN dbo.tblNORM_Import pi ON pi.ImportId=pr.ImportId " +
            "WHERE pn.DisclosureCode=t.DisclosureCode AND pn.NarrativeType=t.NarrativeType AND pn.IsDeactivated=0 " +
            "AND pn.CalculationRunId<>@run AND pi.EntityCode=(SELECT TOP 1 ci.EntityCode FROM dbo.tblNORM_CalculationRun cr " +
            "INNER JOIN dbo.tblNORM_Import ci ON ci.ImportId=cr.ImportId WHERE cr.CalculationRunId=@run) " +
            "ORDER BY pi.FinancialYear DESC,pn.UpdatedUtc DESC) prior " +
            "WHERE t.ConfigurationReleaseId=@release AND t.IsDeactivated=0",
            NORMHelper.P("@run", runId), NORMHelper.P("@release", releaseId));
        Dictionary<string, string[]> values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            values[NORMHelper.Str(table.Rows[i], "DisclosureCode")] = new string[] {
                NORMHelper.Str(table.Rows[i], "NarrativeType"),
                NORMHelper.Str(table.Rows[i], "NarrativeText"),
                NORMHelper.Str(table.Rows[i], "StatusCode")
            };
        }
        return values;
    }

    public static void SaveNarrative(int runId, string disclosureCode, string narrativeType, string narrativeText,
        string statusCode, string updatedBy)
    {
        disclosureCode = Limit(disclosureCode, 40);
        narrativeType = Limit(narrativeType, 30);
        narrativeText = narrativeText ?? "";
        statusCode = Allowed(statusCode, new string[] { "Draft", "Prepared", "Reviewed", "Approved" }, "Draft");
        int changed = NORMHelper.Exec(
            "UPDATE dbo.tblNORM_RunNarrative SET NarrativeText=@text,StatusCode=@status,UpdatedBy=@user," +
            "UpdatedUtc=SYSUTCDATETIME(),IsDeactivated=0 WHERE CalculationRunId=@run AND DisclosureCode=@code AND NarrativeType=@type",
            NORMHelper.P("@text", narrativeText), NORMHelper.P("@status", statusCode),
            NORMHelper.P("@user", updatedBy), NORMHelper.P("@run", runId),
            NORMHelper.P("@code", disclosureCode), NORMHelper.P("@type", narrativeType));
        if (changed == 0)
        {
            NORMHelper.Exec(
                "INSERT dbo.tblNORM_RunNarrative (CalculationRunId,DisclosureCode,NarrativeType,NarrativeText,StatusCode,UpdatedBy) " +
                "VALUES (@run,@code,@type,@text,@status,@user)",
                NORMHelper.P("@run", runId), NORMHelper.P("@code", disclosureCode),
                NORMHelper.P("@type", narrativeType), NORMHelper.P("@text", narrativeText),
                NORMHelper.P("@status", statusCode), NORMHelper.P("@user", updatedBy));
        }
    }

    public static void EnsureWorkflow(int runId, string updatedBy)
    {
        if (!IsInstalled()) { return; }
        string[,] items = new string[,] {
            { "FINANCIAL_STATEMENTS", "DRAFT_FS", "Draft financial statements" },
            { "FINANCIAL_STATEMENTS", "MANUAL_INPUTS", "Manual disclosure schedules and reconciliations" },
            { "FINANCIAL_STATEMENTS", "CASH_FLOW_JOURNALS", "Cash-flow classification journals" },
            { "FINANCIAL_STATEMENTS", "ASSET_MOVEMENTS", "Property, plant and equipment movement table" },
            { "ANNUAL_PERFORMANCE", "APS_DRAFT", "Annual performance statements" },
            { "ANNUAL_PERFORMANCE", "APS_ASSURANCE", "Performance measure evidence and assurance" },
            { "AUDIT_COMMITTEE", "FS_SUMMARY", "Financial statement summary" },
            { "AUDIT_COMMITTEE", "JUDGEMENTS", "Significant accounting judgements" },
            { "AUDIT_COMMITTEE", "NEW_STANDARDS", "New accounting standards" },
            { "AUDIT_COMMITTEE", "MOVEMENTS", "Material movements" },
            { "AUDIT_COMMITTEE", "RISKS", "Key financial reporting risks" },
            { "AUDIT_COMMITTEE", "REPRESENTATION", "Management representation checklist" },
            { "AUDIT_COMMITTEE", "CERTIFICATION", "Internal certification status" },
            { "ANNUAL_REPORT", "OUTCOMES", "Outcome reporting tables" },
            { "ANNUAL_REPORT", "FINANCE_MODULES", "Annual report financial information" }
        };
        for (int i = 0; i < items.GetLength(0); i++)
        {
            NORMHelper.Exec(
                "IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_WorkflowItem WHERE CalculationRunId=@run AND ModuleCode=@module AND ItemCode=@code) " +
                "INSERT dbo.tblNORM_WorkflowItem (CalculationRunId,ModuleCode,ItemCode,ItemLabel,UpdatedBy) " +
                "VALUES (@run,@module,@code,@label,@user)",
                NORMHelper.P("@run", runId), NORMHelper.P("@module", items[i, 0]),
                NORMHelper.P("@code", items[i, 1]), NORMHelper.P("@label", items[i, 2]),
                NORMHelper.P("@user", updatedBy));
        }
    }

    public static DataTable LoadWorkflow(int runId)
    {
        if (!IsInstalled()) { return new DataTable(); }
        return NORMHelper.Query(
            "SELECT WorkflowItemId,ModuleCode,ItemCode,ItemLabel,OwnerUserId,ReviewerUserId,StatusCode,DueDate,Commentary,UpdatedUtc " +
            "FROM dbo.tblNORM_WorkflowItem WHERE CalculationRunId=@run AND IsDeactivated=0 " +
            "ORDER BY CASE ModuleCode WHEN 'FINANCIAL_STATEMENTS' THEN 1 WHEN 'ANNUAL_PERFORMANCE' THEN 2 " +
            "WHEN 'AUDIT_COMMITTEE' THEN 3 WHEN 'ANNUAL_REPORT' THEN 4 ELSE 5 END,WorkflowItemId",
            NORMHelper.P("@run", runId));
    }

    public static void SaveWorkflowItem(long workflowItemId, int runId, string owner, string reviewer,
        string status, string commentary, string updatedBy)
    {
        status = Allowed(status, new string[] { "NotStarted", "InProgress", "Prepared", "Reviewed", "Approved", "Blocked" }, "NotStarted");
        NORMHelper.Exec(
            "UPDATE dbo.tblNORM_WorkflowItem SET OwnerUserId=@owner,ReviewerUserId=@reviewer,StatusCode=@status," +
            "Commentary=@commentary,UpdatedBy=@user,UpdatedUtc=SYSUTCDATETIME() " +
            "WHERE WorkflowItemId=@id AND CalculationRunId=@run AND IsDeactivated=0",
            NORMHelper.P("@owner", String.IsNullOrWhiteSpace(owner) ? (object)null : Limit(owner, 256)),
            NORMHelper.P("@reviewer", String.IsNullOrWhiteSpace(reviewer) ? (object)null : Limit(reviewer, 256)),
            NORMHelper.P("@status", status), NORMHelper.P("@commentary", String.IsNullOrWhiteSpace(commentary) ? (object)null : Limit(commentary, 2000)),
            NORMHelper.P("@user", updatedBy), NORMHelper.P("@id", workflowItemId), NORMHelper.P("@run", runId));
    }

    public static void AuditWorkspaceSave(int runId, string changeReason, string updatedBy)
    {
        changeReason = Limit(changeReason, 1000);
        if (String.IsNullOrWhiteSpace(changeReason)) { changeReason = "Reporting workspace updated."; }
        NORMHelper.Exec(
            "INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
            "VALUES ('REPORTING_WORKSPACE_SAVED','CalculationRun',@id,@detail,@user)",
            NORMHelper.P("@id", runId.ToString(CultureInfo.InvariantCulture)),
            NORMHelper.P("@detail", changeReason), NORMHelper.P("@user", updatedBy));
    }

    private static string Allowed(string value, string[] allowed, string fallback)
    {
        for (int i = 0; i < allowed.Length; i++)
        {
            if (String.Equals(value, allowed[i], StringComparison.OrdinalIgnoreCase)) { return allowed[i]; }
        }
        return fallback;
    }

    private static decimal? NullableDecimal(DataRow row, string column)
    {
        return row == null || !row.Table.Columns.Contains(column) || row.IsNull(column)
            ? (decimal?)null : Convert.ToDecimal(row[column], CultureInfo.InvariantCulture);
    }

    private static string Limit(string value, int maximum)
    {
        value = (value ?? "").Trim();
        return value.Length <= maximum ? value : value.Substring(0, maximum);
    }

    private static string Normalise(string value)
    {
        value = (value ?? "").ToLowerInvariant().Replace("and", " ").Replace("other", " ").Replace("departmental", " ");
        char[] buffer = new char[value.Length];
        int length = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (Char.IsLetterOrDigit(c) || Char.IsWhiteSpace(c)) { buffer[length++] = c; }
        }
        return new string(buffer, 0, length).Trim();
    }
}
