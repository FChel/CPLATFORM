using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

public static class NORMDisclosureCompleteness
{
    public const string Complete = "Complete (checks)";
    public const string Incomplete = "Needs input";

    public sealed class Check
    {
        public readonly string Id, Destination, Rule, Title, OpenDetail, ValidationCode;
        internal readonly string Statement, Line;
        public readonly bool Comparative, AwaitingDecision, Unsupported;

        internal Check(string id, string destination, string rule, string title, string openDetail,
            string validationCode = null, bool comparative = false, bool awaitingDecision = false, bool unsupported = false,
            string statement = null, string line = null)
        {
            Id = id;
            Destination = destination;
            Rule = rule;
            Title = title;
            OpenDetail = openDetail;
            ValidationCode = statement == null ? validationCode : "NOTE_TIE_" + id;
            Comparative = comparative;
            AwaitingDecision = awaitingDecision;
            Unsupported = unsupported;
            Statement = statement;
            Line = line;
        }
    }

    public sealed class Finding
    {
        public Check Check { get; internal set; }
        public string Result { get; internal set; }
        public string Detail { get; internal set; }
        public bool Passed { get { return Result == "Pass"; } }
    }

    internal static List<Finding> Load(int runId, int releaseId, string entity, int year, int priorRun,
        List<NORMReportingFramework.Disclosure> disclosures)
    {
        IEnumerable<Check> checks = String.Equals(entity, NORMNihracsDisclosureCatalogue.Entity, StringComparison.OrdinalIgnoreCase) &&
            year == NORMNihracsDisclosureCatalogue.FinancialYear
            ? NORMNihracsDisclosureCatalogue.Checks : new Check[0];
        bool current = checks.Any() && priorRun > 0 && HasCurrentEvidence(runId, priorRun, releaseId, entity, year);
        DataTable evidence = current ? NORMHelper.Query(
            "SELECT CalculationRunId,CheckCode,ResultCode,ActualValue,ExpectedValue,DifferenceValue,DetailText " +
            "FROM dbo.tblNORM_ValidationResult WHERE CalculationRunId IN (@run,@prior)",
            NORMHelper.P("@run", runId), NORMHelper.P("@prior", priorRun)) : new DataTable();
        if (current)
        {
            DataTable lines = NORMHelper.Query(
                "SELECT CalculationRunId,StatementCode,LineCode,ReportedAmount FROM dbo.tblNORM_LineResult " +
                "WHERE CalculationRunId IN (@run,@prior) AND IsDeactivated=0 AND ReportedAmount IS NOT NULL",
                NORMHelper.P("@run", runId), NORMHelper.P("@prior", priorRun));
            AddNoteTieEvidence(checks, disclosures, evidence, lines, runId, priorRun);
        }
        return Assess(checks, disclosures, evidence, runId, priorRun, current);
    }

    private static bool HasCurrentEvidence(int runId, int priorRun, int releaseId, string entity, int year)
    {
        try
        {
            return NORMStatementEngine.HasCurrentAnnualDisclosureEvidence(runId, priorRun, releaseId,
                NORMImportService.FindCurrentAnnualDepartmentalImport(entity, year),
                NORMImportService.FindAnnualDepartmentalImport(entity, year, year - 1),
                () => NORMImportService.FindAnnualDepartmentalImport(entity, year, year - 2, cashFlowOpening: true));
        }
        catch (InvalidOperationException)
        {
            // An unconfirmed or superseded source or release leaves the checks open.
            return false;
        }
    }

    internal static void AddNoteTieEvidence(IEnumerable<Check> checks, List<NORMReportingFramework.Disclosure> disclosures,
        DataTable evidence, DataTable lines, int runId, int priorRun)
    {
        foreach (Check check in checks.Where(item => item.Statement != null))
        {
            NORMReportingFramework.Disclosure note = disclosures.Find(item => item.Code == check.Rule);
            if (note == null || !note.UsesHierarchyAmounts || !note.ReportedAmount.HasValue ||
                !String.Equals(note.NoteRef, check.Destination, StringComparison.Ordinal)) continue;
            decimal? amount = check.Comparative ? note.PriorAmount : note.ReportedAmount;
            if (!amount.HasValue) continue;
            int periodRun = check.Comparative ? priorRun : runId;
            DataRow face = lines.AsEnumerable().SingleOrDefault(row => NORMHelper.Int(row, "CalculationRunId") == periodRun &&
                NORMHelper.Str(row, "StatementCode") == check.Statement && NORMHelper.Str(row, "LineCode") == check.Line);
            if (face == null || face.IsNull("ReportedAmount")) continue;
            decimal expected = NORMHelper.Dec(face, "ReportedAmount");
            decimal difference = amount.Value - expected;
            evidence.Rows.Add(periodRun, check.ValidationCode, difference == 0m ? "Pass" : "Fail", amount.Value, expected,
                difference, String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Reported note subtotal {0:0.###} and {1} line {2} amount {3:0.###} (AUD thousands) on calculation run #{4}.",
                    amount.Value, check.Statement, check.Line, expected, periodRun));
        }
    }

    internal static List<Finding> Assess(IEnumerable<Check> checks, List<NORMReportingFramework.Disclosure> disclosures,
        DataTable evidence, int runId, int priorRun, bool current)
    {
        var rules = new HashSet<string>(disclosures.Select(item => item.Code), StringComparer.OrdinalIgnoreCase);
        var results = checks.Select(check => Evaluate(check, rules, evidence, runId, priorRun, current)).ToList();
        var byDestination = results.ToLookup(result => result.Check.Destination);
        var destinationByRule = results.Where(result => !String.IsNullOrWhiteSpace(result.Check.Rule))
            .ToLookup(result => result.Check.Rule, result => result.Check.Destination, StringComparer.OrdinalIgnoreCase);
        foreach (NORMReportingFramework.Disclosure item in disclosures)
        {
            var relevant = destinationByRule[item.Code].Distinct().SelectMany(destination => byDestination[destination]).ToList();
            if (!item.Required && relevant.Count == 0) continue;
            item.CompletionStatus = IsComplete(relevant) ? Complete : Incomplete;
            item.CompletenessDetail = relevant.Count == 0
                ? "No mandatory validation basis is configured for this disclosure."
                : String.Join(" ", relevant.Where(result => !result.Passed).Select(OutstandingDetail));
        }
        return results;
    }

    public static string OutstandingDetail(Finding finding)
    {
        return finding.Check.Id + " " + finding.Result + ". " + finding.Detail;
    }

    private static bool IsComplete(List<Finding> findings)
    {
        return findings.Count > 0 && findings.All(finding => finding.Passed);
    }

    private static Finding Evaluate(Check check, HashSet<string> rules, DataTable evidence,
        int runId, int priorRun, bool current)
    {
        var finding = new Finding { Check = check, Result = "Not Run", Detail = check.OpenDetail };
        if (check.AwaitingDecision) { finding.Detail = "Applicability decision required. " + check.OpenDetail; return finding; }
        if (check.Unsupported) { finding.Detail = "Unsupported in NORM. " + check.OpenDetail; return finding; }
        if (!String.IsNullOrEmpty(check.Rule) && !rules.Contains(check.Rule))
        {
            finding.Detail = "The configured disclosure link " + check.Rule + " is missing. " + check.OpenDetail;
            return finding;
        }
        if (String.IsNullOrEmpty(check.ValidationCode)) return finding;
        if (!current)
        {
            finding.Detail = "Current source and configuration versions could not be verified for this run. " + check.OpenDetail;
            return finding;
        }
        int evidenceRun = check.Comparative ? priorRun : runId;
        DataRow row = evidence.AsEnumerable().SingleOrDefault(value =>
            NORMHelper.Int(value, "CalculationRunId") == evidenceRun &&
            String.Equals(NORMHelper.Str(value, "CheckCode"), check.ValidationCode, StringComparison.Ordinal));
        if (row == null) return finding;
        string status = NORMHelper.Str(row, "ResultCode");
        string detail = NORMHelper.Str(row, "DetailText");
        if (status == "Fail")
        {
            finding.Result = status;
            if (!String.IsNullOrWhiteSpace(detail)) finding.Detail = detail + " " + check.OpenDetail;
        }
        else if (status == "Pass" && !String.IsNullOrWhiteSpace(detail) &&
            !row.IsNull("ActualValue") && !row.IsNull("ExpectedValue") && !row.IsNull("DifferenceValue") &&
            NORMHelper.Dec(row, "ActualValue") == NORMHelper.Dec(row, "ExpectedValue") &&
            NORMHelper.Dec(row, "DifferenceValue") == 0m)
        {
            finding.Result = status;
            finding.Detail = detail;
        }
        return finding;
    }
}
