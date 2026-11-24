using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using CPlatform.NORM;
using OfficeOpenXml;

internal static partial class PilotTests
{
    private const string EvidenceColumns = "CalculationRunId CheckCode ResultCode ActualValue ExpectedValue DifferenceValue DetailText";

    private static void CheckDisclosureCompleteness()
    {
        const int run = 42, prior = 43;
        var note = new NORMReportingFramework.Disclosure { Code = "R1", Required = true, CompletionStatus = "Validated",
            Narrative = "Template text", NarrativeStatus = "Approved" };
        var notes = new List<NORMReportingFramework.Disclosure> { note };
        var checks = new[] {
            new NORMDisclosureCompleteness.Check("A", "1", "R1", "Required balance", "Retain the balance evidence.", "CHECK_A"),
            new NORMDisclosureCompleteness.Check("B", "1", "", "Required comparative", "Retain the comparative evidence.", "CHECK_B", comparative: true)
        };
        DataTable evidence = Table(EvidenceColumns, new object[] { run, "CHECK_A", "Pass", 0m, 0m, 0m, "Verified balance" },
            new object[] { prior, "CHECK_B", "Pass", 12m, 12m, 0m, "Verified comparative" });
        Func<List<NORMDisclosureCompleteness.Finding>> assess = () =>
            NORMDisclosureCompleteness.Assess(checks, notes, evidence, run, prior, true);
        Check(assess().All(check => check.Passed) && note.CompletionStatus == NORMDisclosureCompleteness.Complete,
            "All applicable checks must complete the disclosure, including an evidenced zero.");
        NORMHelper.DraftReviewRun = true;
        try
        {
            var context = new System.Web.HttpContext(new System.Web.HttpRequest("", "http://localhost/review", "run=42"),
                new System.Web.HttpResponse(new StringWriter()));
            context.User = new System.Security.Principal.GenericPrincipal(
                new System.Security.Principal.GenericIdentity("test", "fixture"), new string[0]);
            new NORM_ReviewPack().ProcessRequest(context);
            Check(context.Response.StatusCode == 403 && note.CompletionStatus == NORMDisclosureCompleteness.Complete,
                "Complete disclosure checks must not open draft review-pack access.");
        }
        finally { NORMHelper.DraftReviewRun = false; }

        evidence.Rows[1]["ResultCode"] = "Fail";
        Check(assess()[1].Result == "Fail" && note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
            "Failed mandatory check must prevent completion.");
        evidence.Rows[1]["ResultCode"] = "Pass";
        evidence.Rows[1]["DifferenceValue"] = 1m;
        Check(!assess()[1].Passed, "A nonzero difference was accepted as reconciled.");
        evidence.Rows[1]["DifferenceValue"] = 0m;
        evidence.Rows[1]["DetailText"] = DBNull.Value;
        Check(assess()[1].Result == "Not Run" && note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
            "A pass without mandatory evidence was accepted.");
        evidence.Rows[1]["DetailText"] = "Verified comparative";
        evidence.Rows[1]["CalculationRunId"] = run;
        Check(assess()[1].Result == "Not Run" && note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
            "A result from the wrong period was accepted.");
        evidence.Rows[1]["CalculationRunId"] = prior;
        Check(NORMDisclosureCompleteness.Assess(checks, notes, evidence, run, prior, false).All(check => !check.Passed) &&
            note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
            "Superseded source or configuration evidence was accepted.");
        evidence.Rows.RemoveAt(1);
        Check(assess()[1].Result == "Not Run" && note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
            "A check without a run result was accepted.");

        foreach (NORMDisclosureCompleteness.Check missing in new[] {
            new NORMDisclosureCompleteness.Check("C", "1", "", "Conditional assessment", "Confirm applicability.", awaitingDecision: true),
            new NORMDisclosureCompleteness.Check("D", "1", "", "Unsupported schedule", "Supply the schedule.", unsupported: true)
        })
        {
            var findings = NORMDisclosureCompleteness.Assess(new[] { checks[0], missing }, notes, evidence, run, prior, true);
            Check(findings[0].Passed && !findings[1].Passed && note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
                "An unresolved applicability or unsupported item was masked by a pass.");
        }
        var missingRule = NORMDisclosureCompleteness.Assess(checks, new List<NORMReportingFramework.Disclosure>(), evidence, run, prior, true);
        Check(!missingRule[0].Passed && missingRule[0].Detail.Contains("link R1 is missing"), "Missing configuration was accepted.");
        NORMDisclosureCompleteness.Assess(new NORMDisclosureCompleteness.Check[0], notes, evidence, run, prior, true);
        Check(note.CompletionStatus == NORMDisclosureCompleteness.Incomplete && note.CompletenessDetail.Contains("No mandatory"),
            "Approved narrative created completeness without a validation basis.");

        NORMHelper.Reset();
        NORMHelper.ScalarCallResponse = call => 1;
        NORMHelper.QueryCallResponse = call => Table("DisclosureCode InputLabel InputTypeCode AmountCurrent AmountPrior StatusCode",
            new object[] { "R1", "Supported component", "Disclosure", 12m, 10m, "Validated" });
        NORMStatementEnhancements.ApplyManualInputs(run, notes);
        Check(note.Lines.Single().Amount == 12m && note.Amount == 12m && note.CompletionStatus == NORMDisclosureCompleteness.Incomplete,
            "One validated manual input must retain the amount without validating the note.");
        NORMHelper.Reset();
        Console.WriteLine("PASS disclosure policy, zero balance, missing evidence, stale and cross-period results, manual overlay and draft pack restriction");

        CheckAcceptedDisclosureFixture(run, prior);
    }

    private static void CheckAcceptedDisclosureFixture(int run, int prior)
    {
        var employee = new NORMReportingFramework.Disclosure { Code = "N1_1A", NoteRef = "1.1A", Required = true,
            ReportedAmount = 8291m, PriorAmount = 7524m, UsesHierarchyAmounts = true };
        var cash = new NORMReportingFramework.Disclosure { Code = "NI_BS_ASS_FIA_CSH", NoteRef = "2.1A", Required = true,
            ReportedAmount = 1504m, PriorAmount = 2070m, UsesHierarchyAmounts = true };
        var catalogue = NORMNihracsDisclosureCatalogue.Checks;
        var notes = catalogue.Where(check => !String.IsNullOrEmpty(check.Rule)).Select(check => check.Rule).Distinct()
            .Select(rule => rule == employee.Code ? employee : rule == cash.Code ? cash :
                new NORMReportingFramework.Disclosure { Code = rule, Required = true }).ToList();
        DataTable evidence = Table(EvidenceColumns,
            new object[] { run, NORMStatementEngine.CashFlowReconciliationCheckCode, "Fail", -98m, -566m, 468m, "Exact AUD difference: 467295.60." },
            new object[] { prior, NORMStatementEngine.CashFlowReconciliationCheckCode, "Fail", 1494m, 1063m, 431m, "Exact AUD difference: 431911.95." });
        DataTable lines = Table("CalculationRunId StatementCode LineCode ReportedAmount",
            new object[] { run, "SOCI", "Employee benefits", 8291m }, new object[] { prior, "SOCI", "Employee benefits", 7524m },
            new object[] { run, "SCF", "CF_CSH_CLOSE", 1504m }, new object[] { prior, "SCF", "CF_CSH_CLOSE", 2070m });
        NORMDisclosureCompleteness.AddNoteTieEvidence(catalogue, notes, evidence, lines, run, prior);
        var findings = NORMDisclosureCompleteness.Assess(catalogue, notes, evidence, run, prior, true);
        Check(findings.Count == 52 && findings.Count(value => value.Passed) == 4 && findings.Count(value => value.Result == "Fail") == 2 &&
            findings.Count(value => value.Result == "Not Run" && !value.Check.AwaitingDecision) == 40 &&
            findings.Count(value => value.Check.AwaitingDecision) == 6 && findings.Count(value => value.Check.Unsupported) == 7,
            "The accepted fixture dispositions were not reproduced.");
        Check(employee.CompletionStatus == NORMDisclosureCompleteness.Incomplete &&
            cash.CompletionStatus == NORMDisclosureCompleteness.Incomplete && employee.CompletenessDetail.Contains("EMP-CONTENT") &&
            cash.CompletenessDetail.Contains("CASH-SUPPORT"), "Fixture subtotal passes must not complete either disclosure.");
        var validation = new List<object>();
        Invoke(new NORM_Statements(), typeof(NORM_Statements), "AppendDisclosureValidations", validation, notes, findings);
        Check(validation.Cast<Dictionary<string, object>>().Any(row => (string)row["code"] == "DISCLOSURE_SCF25" &&
            (string)row["result"] == "Fail" && ((string)row["detail"]).Contains("431911.95")),
            "Comparative cash-flow failure is absent from the Statements review.");
        string html = (string)Invoke(new NORM_Reporting(), typeof(NORM_Reporting), "BuildDisclosureChecks", findings);
        Check(html.Contains("467295.60") && html.Contains("Applicability decision required"), "Reporting register omitted pending checks.");
        using (var package = new ExcelPackage())
        {
            NORM_ReviewPack.AddDisclosureChecks(package, findings);
            using (var opened = new ExcelPackage(new MemoryStream(package.GetAsByteArray())))
            {
                ExcelWorksheet sheet = opened.Workbook.Worksheets["Disclosure checks"];
                Check(sheet.Dimension.End.Row == 56 && sheet.Cells[8, 5].Text == "Fail" &&
                    sheet.Cells[8, 6].Text.Contains("467295.60") && sheet.Cells[51, 4].Text == "Awaiting decision",
                    "The exported workbook omitted fixture failures or pending applicability.");
            }
        }
        Console.WriteLine("PASS NIHRACS fixture: 4 passes, 2 failures, 40 required checks not run, 6 pending decisions, both notes remain open");
        Console.WriteLine("PASS Statements and Reporting render paths and review-pack workbook round trip");
    }

    private static void CheckDraftDisclosureEvidence(int run, int prior)
    {
        var read = NORMHelper.QueryCallResponse;
        bool wrongPrior = false, editedRelease = false;
        NORMHelper.QueryCallResponse = call =>
        {
            if (call.Sql.Contains("FROM dbo.tblNORM_ReportingProfile") || call.Sql.Contains("FROM dbo.tblNORM_RequirementSelection"))
                return new DataTable();
            DataTable data = read(call);
            if (wrongPrior && call.Sql.StartsWith("SELECT r.CalculationRunId,r.ImportId,r.InputFingerprint,c.StatusCode"))
                data.AsEnumerable().Single(row => (int)row["CalculationRunId"] == prior)["InputFingerprint"] = "different-run";
            if (editedRelease && call.Sql.StartsWith("SELECT i.ImportId,c.ConfigurationReleaseId"))
                data.Rows[0]["DraftRevision"] = 256L;
            return data;
        };
        try
        {
            List<NORMDisclosureCompleteness.Finding> findings;
            var notes = NORMReportingFramework.LoadDisclosures(run, 19, new NORMReportingFramework.ReportingProfile(), out findings);
            Check(findings.Count(value => value.Passed) == 4 && findings.Count(value => value.Result == "Fail") == 2 &&
                notes.Where(note => note.Code == "N1_1A" || note.Code == "NI_BS_ASS_FIA_CSH")
                    .All(note => note.CompletionStatus == NORMDisclosureCompleteness.Incomplete),
                "Application loading did not consume current run evidence or left either note prematurely complete.");
            using (var package = new ExcelPackage())
            {
                Invoke(null, typeof(NORM_ReviewPack), "AddDisclosureRegister", package, run, 19);
                ExcelWorksheet register = package.Workbook.Worksheets["Disclosure register"];
                Check(register.Cells[5, 2].Text == "1.1A" &&
                    register.Cells[5, 6].Text == NORMDisclosureCompleteness.Incomplete &&
                    register.Cells[5, 10].Text.Contains("EMP-CONTENT") &&
                    package.Workbook.Worksheets["Disclosure checks"].Cells[8, 5].Text == "Fail",
                    "The review-pack register and check detail disagree with the current calculation.");
            }
            foreach (string changed in new[] { "current source", "opening source", "comparative fingerprint", "configuration revision" })
            {
                NORMImportService.CurrentAnnualImport = changed == "current source" ? 25 : 99;
                NORMImportService.OpeningAnnualImport = changed == "opening source" ? 0 : 23;
                wrongPrior = changed == "comparative fingerprint";
                editedRelease = changed == "configuration revision";
                Check(!NORMStatementEngine.HasCurrentAnnualDisclosureEvidence(run, prior, 19,
                    NORMImportService.CurrentAnnualImport, 24, () => NORMImportService.OpeningAnnualImport),
                    "Run loading accepted evidence after changing the " + changed + ".");
            }
            Console.WriteLine("PASS application loads note ties and cash failures from the calculated pair and rejects stale sources and configuration");
        }
        finally
        {
            NORMHelper.QueryCallResponse = read;
            NORMImportService.CurrentAnnualImport = 99;
            NORMImportService.OpeningAnnualImport = 23;
        }
    }
}
