using System;
using System.Collections.Generic;
using System.IO;
using System.Data;
using System.Linq;
using OfficeOpenXml;

public static partial class Tests
{
    static void RunNihracs(string workbook)
    {
        byte[] bytes = File.ReadAllBytes(workbook);
        int before = passed;
        HTest("annual source years derive from reporting year and prior-year count", delegate {
            Assert(NORMAnnualSourceConfiguration.RequiredYears(2026, 0).SequenceEqual(new[] { 2026 }), "Current year missing");
            Assert(NORMAnnualSourceConfiguration.RequiredYears(2026, 1).SequenceEqual(new[] { 2026, 2025 }), "One prior year incorrect");
            Assert(NORMAnnualSourceConfiguration.RequiredYears(2026, 2).SequenceEqual(new[] { 2026, 2025, 2024 }), "Two prior years incorrect");
            Assert(NORMAnnualSourceConfiguration.RequiredYears(2027, 1).SequenceEqual(new[] { 2027, 2026 }), "Rollover derivation incorrect");
        });
        HTest("annual NIHRACS accepts three source years independently of competing mapping releases", delegate {
            string snapshot = AnnualFixture();
            var missing = NORMImportService.MissingAnnualRequiredPopulations("NIHRACS");
            Assert(missing.Select(m => m.FinancialYear).SequenceEqual(new[] { 2026, 2025 }), "FY2024 should be optional");
            var page = new CPlatform.NORM.NORM_Import();
            page.InspectUploadsForTest(25, new[] { 26 }, new[] { bytes }, new[] { "Consolidated TBs.xlsx" }, "NIHRACS");
            var previews = page.PreviewsForTest;
            Assert(page.ErrorForTest == "" && page.ConfirmationEnabledForTest && previews != null, "Annual upload failed through the page");
            Assert(previews.Select(p => p.RowCount).SequenceEqual(new[] { 413, 427, 778 }) &&
                previews.All(p => p.CanAccept && p.ReleaseId == 0 && p.AnnualApprovalSnapshot == snapshot), "Incorrect annual context");
            Assert(previews.All(p => p.SourceRows.Select(r => Tuple.Create(r.GlAccount, r.SourceCostCentre)).Distinct().Count() == p.RowCount &&
                p.SourceRows.All(r => r.SourceDebit.HasValue && r.SourceCredit.HasValue && r.AccumBalance == r.SourceDebit - r.SourceCredit)),
                "The annual path changed identifying dimensions or source amounts");
            NORMHelper.ScalarCallResponse = call => call.Sql.StartsWith("SELECT EntityName") ? "NIHRACS" : HistoryBlob(call);
            page.ClearUploadedFilesForTest();
            page.ConfirmForTest();
            Assert(page.ErrorForTest == "" && page.ResultForTest.Contains("Source accepted") && acceptanceCommits == 1 && NORMHelper.Runs == 0,
                "Annual sources did not commit atomically through the page");
            Assert(NORMHelper.Committed.Count(c => c.Values.ContainsKey("@annualSnapshot") &&
                (string)c["@annualSnapshot"] == snapshot && c["@release"] == DBNull.Value) == 3, "Approved evidence was not persisted");
            Assert(NORMImportService.MissingAnnualRequiredPopulations("NIHRACS").Count == 0, "Accepted required sources remain missing");
            foreach (int year in new[] { 2024, 2025, 2026 })
            {
                int id = Current(Key("NIHRACS", year, "NIHRA", null, "Departmental"));
                Assert(NORMCrypto.Sha256(ImportDownload(id)) == NORMCrypto.Sha256(bytes), "Annual original changed");
            }
        });
        HTest("annual context denies unauthorised agency before inspection", delegate {
            AnnualFixture();
            NORMHelper.EntityAccess = entity => entity != "NIHRACS";
            Assert(!NORMImportService.ImportAgencies().Contains("NIHRACS"), "Denied agency leaked into the selector");
            RejectUnchanged(delegate { NORMImportService.InspectFileForAnnualContext(bytes, "Consolidated TBs.xlsx", "NIHRACS", "isolated-test"); }, "Entity access denied");
        });
        HTest("agency selector is scoped and release query parameters cannot set the context", delegate {
            AnnualFixture();
            NORMHelper.ScalarCallResponse = call => call.Sql.StartsWith("SELECT EntityName") ? "NI Health & Residential Aged Care" : HistoryBlob(call);
            NORMHelper.EntityAccess = entity => entity == "NIHRACS";
            var single = new CPlatform.NORM.NORM_Import();
            Assert(single.PrepareContextForTest("agency=CONTROLLED&release=25") == "NIHRACS" &&
                !single.AgencySelectorForTest && single.AgencyContextForTest.Contains("Reporting FY2026"), "Single agency implied selection failed");
            Add(27, 2026, "200", null, "Departmental").Entity = "DENIED";
            NORMHelper.EntityAccess = entity => entity != "DENIED";
            var multi = new CPlatform.NORM.NORM_Import();
            Assert(multi.PrepareContextForTest("agency=NIHRACS&release=25") == "NIHRACS" && multi.AgencySelectorForTest &&
                multi.HasAgencyForTest("CONTROLLED") && !multi.HasAgencyForTest("DENIED"), "Multi-agency access filtering failed");
        });
        HTest("changed annual approval invalidates the staged workbook", delegate {
            AnnualFixture();
            var previews = NORMImportService.InspectFileForAnnualContext(bytes, "Consolidated TBs.xlsx", "NIHRACS", "isolated-test");
            NORMHelper.CurrentYear = 2027;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(previews, "isolated-test", false); }, "Configuration changed");
        });
        HTest("NIHRACS three-year preview, atomic source-only confirmation and byte-identical retrieval", delegate {
            List<NORMImportPreview> previews = InspectNihracsFixture(bytes);
            int[] rows = { 413, 427, 778 };
            int[] gls = { 413, 427, 250 };
            decimal[] totals = { 33540798.38m, 38135604.94m, 42233019.17m };
            Assert(previews.Count == 3 && previews[0].SourceFile.DetailRowCount == 1618, "Incorrect file accounting");
            for (int i = 0; i < 3; i++)
            {
                NORMImportPreview preview = previews[i];
                Assert(preview.CanAccept && preview.FinancialYear == 2024 + i && preview.ReleaseId == 24 + i &&
                    preview.RowCount == rows[i] && preview.UnmappedAccounts == gls[i] && preview.IsFullYear == true &&
                    preview.Debit == totals[i] && preview.Credit == totals[i] && preview.Net == 0m, "Incorrect annual preview");
                Assert(preview.SourceRows.Select(r => Tuple.Create(r.GlAccount, r.SourceCostCentre)).Distinct().Count() == rows[i],
                    "GL/centre detail changed");
            }
            string hash = NORMCrypto.Sha256(bytes);
            int storedRows = 0;
            NORMHelper.OnExecute = delegate(NORMHelper.Call call) {
                if (!call.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow")) { return; }
                Assert(call["@sourceDebit"] is decimal && call["@sourceCredit"] is decimal &&
                    (decimal)call["@sourceDebit"] - (decimal)call["@sourceCredit"] == (decimal)call["@balance"],
                    "Source amount sides were not passed to storage");
                storedRows++;
            };
            NORMImportService.ValidateBatch(previews);
            List<NORMImportOutcome> outcomes = NORMImportService.ConfirmBatch(previews, "isolated-test", false);
            Assert(acceptanceCommits == 1 && storedRows == 1618 && outcomes.Count == 3 && NORMHelper.Runs == 0,
                "Confirmation did not commit three complete source-only imports");
            for (int i = 0; i < 3; i++)
            {
                int importId = outcomes[i].ImportId;
                Assert(store[importId].Rows.Count == rows[i] && NORMCrypto.Sha256(ImportDownload(importId)) == hash,
                    "Accepted detail or original differs");
                Assert(NORMSourceHistory.Versions(Selected(importId))[0].Profile == NihracsTrialBalanceImporter.Profile,
                    "Accepted profile audit missing");
            }
        });
        HTest("NIHRACS one unbalanced year blocks the entire original", delegate {
            byte[] invalid = AlterNihracs(bytes, "2024", 8, 7, 0.01d);
            List<NORMImportPreview> previews = InspectNihracsFixture(invalid);
            Assert(!previews[0].CanAccept && previews[1].CanAccept && previews[2].CanAccept, "Annual validation was not independent");
            Assert(previews[0].Net == 0.01m, "Exact-cent imbalance was not preserved");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(previews, "isolated-test", false); }, "invalid");
        });
        HTest("NIHRACS rejects substantive sub-cent values, including the prior FY2025 case", delegate {
            byte[] invalid = AlterNihracs(bytes, "2025", 179, 7, 3384570.865d);
            RejectUnchanged(delegate { InspectNihracsFixture(invalid); }, "sub-cent");
        });
        HTest("NIHRACS duplicate account and blank centre blocks confirmation", delegate {
            byte[] invalid = AlterNihracs(bytes, "2024", 9, 2, 709);
            List<NORMImportPreview> previews = InspectNihracsFixture(invalid);
            Assert(previews[0].Errors.Exists(e => e.Contains("Duplicate GL 709")) &&
                !previews[0].CanAccept, "Duplicate pair escaped validation");
        });
        HTest("NIHRACS failure during final year persistence rolls back earlier years", delegate {
            var previews = InspectNihracsFixture(bytes);
            int rows = 0;
            NORMHelper.OnExecute = delegate(NORMHelper.Call call) {
                if (call.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow") && ++rows == 841)
                { throw new InvalidOperationException("Injected final year row failure"); }
            };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(previews, "isolated-test", false); }, "final year row failure");
            Assert(rows == 841 && NORMHelper.Rollbacks > 0, "Persistence did not reach the final year and roll back");
        });
        HTest("NIHRACS replacement history compares repeated GLs by centre including blanks", delegate {
            Chain();
            foreach (int id in new[] { 20, 30 })
            {
                profiles[id] = "Profile=" + NihracsTrialBalanceImporter.Profile;
                SetAccounts(id, new object[] { "100", "Blank centre", 100m },
                    new object[] { "100", "Populated centre", id == 20 ? 25m : 30m });
                DataTable detail = accountEvidence[id];
                detail.Columns.Add("SourceCostCentre", typeof(string));
                detail.Rows[1]["SourceCostCentre"] = "350";
            }
            NORMSourceComparison comparison = NORMSourceHistory.Compare(Selected(30), 30);
            Assert(comparison.Financial.Count == 1 && comparison.Financial[0].Account == "100 / 350" &&
                comparison.Financial[0].Difference == 5m && comparison.Structural.Count == 0,
                "A centre change did not remain separate from the blank-centre row");
        });
        Console.WriteLine("NIHRACS multi-year: " + (passed - before) + " Passed / 0 Failed");
    }

    static string AnnualFixture()
    {
        Add(25, 2026, "NIHRA", null, "Departmental").Entity = "NIHRACS";
        Add(26, 2026, "OTHER", null, "Departmental").Entity = "NIHRACS";
        NORMHelper.CurrentYear = 2026;
        string snapshot = "<annual agency=\"NIHRACS\" year=\"2026\" prior=\"1\"><population company=\"NIHRA\" stream=\"Departmental\" required=\"1\" /></annual>";
        NORMHelper.QueryCallResponse = delegate(NORMHelper.Call call) {
            if (call.Sql.Contains("SourceApprovalSnapshot") && call.Sql.Contains("FROM dbo.tblNORM_ReportingEntity"))
            {
                DataTable table = Table("EntityCode,EntityName,FinancialYear,PriorYearsRequired,SourceApprovalSnapshot,SourceApprovalHash");
                if ((string)call["@entity"] == "NIHRACS" && (int)call["@fy"] == 2026)
                { table.Rows.Add("NIHRACS", "NIHRACS", 2026, 1, snapshot, NORMCrypto.Sha256(System.Text.Encoding.Unicode.GetBytes(snapshot))); }
                return table;
            }
            return HistoryQuery(call);
        };
        return snapshot;
    }

    static List<NORMImportPreview> InspectNihracsFixture(byte[] bytes)
    {
        for (int year = 2024; year <= 2026; year++)
        { Add(year - 2000, year, "NIHRA", null, "Departmental").Entity = "NIHRACS"; }
        return NORMImportService.InspectFileForAgency(bytes, "Consolidated TBs.xlsx", 26, new int[0], "isolated-test");
    }

    static byte[] AlterNihracs(byte[] bytes, string worksheet, int row, int column, object value)
    {
        using (MemoryStream input = new MemoryStream(bytes, false))
        using (ExcelPackage package = new ExcelPackage(input))
        {
            package.Workbook.Worksheets[worksheet].Cells[row, column].Value = value;
            return package.GetAsByteArray();
        }
    }
}
