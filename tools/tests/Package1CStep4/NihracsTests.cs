using System;
using System.Collections.Generic;
using System.IO;
using System.Data;
using System.Linq;
using OfficeOpenXml;

public static partial class Tests
{
    static void RunNihracs(string workbook, string replacementWorkbook)
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
            int current = Current(Key("NIHRACS", 2026, "NIHRA", null, "Departmental"));
            int prior = Current(Key("NIHRACS", 2025, "NIHRA", null, "Departmental"));
            int expectedImport = current;
            NORMHelper.ScalarCallResponse = call =>
            {
                Assert(call.Sql.Contains("AnnualSourceApprovalSnapshot") && (int)call["@id"] == expectedImport &&
                    (string)call["@snapshot"] == snapshot && (string)call["@company"] == "NIHRA" &&
                    (string)call["@ledger"] == "", "Retained calculation scope changed");
                return 1;
            };
            Assert(NORMImportService.FindCurrentAnnualDepartmentalImport("NIHRACS", 2026) == current,
                "The calculation did not select the current original");
            expectedImport = prior;
            Assert(NORMImportService.FindAnnualDepartmentalImport("NIHRACS", 2026, 2025) == prior,
                "The comparative did not select the prior original using the FY2026 annual approval");
            NORMHelper.ScalarCallResponse = call => 0;
            RejectUnchanged(delegate { NORMImportService.FindCurrentAnnualDepartmentalImport("NIHRACS", 2026); }, "does not match its annual approval");
            RejectUnchanged(delegate { NORMImportService.FindAnnualDepartmentalImport("NIHRACS", 2026, 2025); }, "FY2025");
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
        HTest("NIHRACS original footer is validated and excluded from source rows", delegate {
            var periods = NihracsTrialBalanceImporter.TryParse(bytes);
            Assert(periods.Select(p => p.SourceFile.ControlRowCount).SequenceEqual(new[] { 0, 0, 4 }),
                "Unexpected control counts across the original years");
            CheckNihracsPeriod(periods[2], 2026, 778, 42233019.17m);
            RejectUnchanged(delegate { NihracsTrialBalanceImporter.TryParse(AlterNihracs(bytes, "2026", 786, 6, 1d)); },
                "cell F786. Footer amount");
            RejectUnchanged(delegate { NihracsTrialBalanceImporter.TryParse(AlterNihracs(bytes, "2026", 789, 5, "Other")); },
                "cell E789. Expected the Up / (Down) footer label");
            RejectUnchanged(delegate { NihracsTrialBalanceImporter.TryParse(AlterNihracs(bytes, "2026", 790, 1, "NIHRA")); },
                "row 790. Unexpected data after the control footer");
        });
        HTest("NIHRACS individual-year workbook handles a recognised footer in any year", delegate {
            using (var package = new ExcelPackage(new MemoryStream(bytes, false)))
            {
                package.Workbook.Worksheets.Delete("2024");
                package.Workbook.Worksheets.Delete("2026");
                var sheet = package.Workbook.Worksheets["2025"];
                int first = sheet.Dimension.End.Row + 1;
                sheet.Cells[first, 5].Value = "Totals";
                sheet.Cells[first, 6].Value = 38135604.94d;
                sheet.Cells[first, 7].Value = 38135604.94d;
                sheet.Cells[first + 1, 5].Value = "Credits";
                sheet.Cells[first + 1, 6].Value = 38135604.94d;
                sheet.Cells[first + 2, 5].Value = "Debits";
                sheet.Cells[first + 2, 6].Value = 38135604.94d;
                sheet.Cells[first + 3, 5].Value = "Up / (Down)";
                sheet.Cells[first + 3, 6].Value = 0d;
                var period = NihracsTrialBalanceImporter.TryParse(package.GetAsByteArray()).Single();
                Assert(period.SourceFile.ControlRowCount == 4, "A FY2025 footer was treated as detail");
                CheckNihracsPeriod(period, 2025, 427, 38135604.94m);
            }
        });
        if (replacementWorkbook != null)
            CheckRevisedNihracs(bytes, File.ReadAllBytes(replacementWorkbook));
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

    static void CheckRevisedNihracs(byte[] original, byte[] revised)
    {
        byte[] withFormula = revised, revisedValues, only2026;
        using (var package = new ExcelPackage(new MemoryStream(revised, false)))
        {
            var amount = package.Workbook.Worksheets["2026"].Cells["F10"];
            Assert(Convert.ToDecimal(amount.Value) == 6739360.52m,
                "The supplied revised source does not match the verified F10 amount");
            if (String.IsNullOrEmpty(amount.Formula))
            {
                amount.Formula = "6739360.52";
                withFormula = package.GetAsByteArray();
            }
        }
        using (var package = new ExcelPackage(new MemoryStream(withFormula, false)))
        {
            var amount = package.Workbook.Worksheets["2026"].Cells["F10"];
            amount.Formula = null;
            amount.Value = 6739360.52d;
            revisedValues = package.GetAsByteArray();
        }
        HTest("NIHRACS revised workbook identifies the formula cell during browser inspection", delegate {
            AnnualFixture();
            var page = new CPlatform.NORM.NORM_Import();
            page.InspectUploadsForTest(25, new int[0], new[] { withFormula }, new[] { "Consolidated TBs (From Statements).xlsx" }, "NIHRACS");
            Assert(page.ErrorForTest.Contains("cell F10. Excel formulas are not accepted. Supply a fixed numeric amount.") &&
                page.PreviewsForTest == null && acceptanceCommits == 0,
                "The inspection did not reject the formula safely: " + page.ErrorForTest);
        });
        using (var package = new ExcelPackage(new MemoryStream(revisedValues, false)))
        {
            package.Workbook.Worksheets.Delete("2024");
            package.Workbook.Worksheets.Delete("2025");
            only2026 = package.GetAsByteArray();
        }
        HTest("NIHRACS revised workbook retains all three populations without a footer", delegate {
            var periods = NihracsTrialBalanceImporter.TryParse(revisedValues);
            Assert(periods.Count == 3 && periods.All(p => p.SourceFile.ControlRowCount == 0), "Unexpected detail or control count");
            CheckNihracsPeriod(periods[0], 2024, 413, 33540798.38m);
            CheckNihracsPeriod(periods[1], 2025, 427, 38135604.94m);
            CheckNihracsPeriod(periods[2], 2026, 771, 42822388.89m);
            Assert(periods[2].Rows.Skip(767).Select(r => r.GlAccount).SequenceEqual(new[] { "21020", "48030", "42350", "46620" }),
                "The final four GL detail rows were lost");
            if (!Object.ReferenceEquals(withFormula, revised))
                CheckNihracsPeriod(NihracsTrialBalanceImporter.TryParse(revised).Single(p => p.FinancialYear == 2026),
                    2026, 771, 42822388.89m);
        });
        HTest("NIHRACS single FY2026 replacement requires consent and preserves the other years", delegate {
            AnnualFixture();
            var previous = NORMImportService.ConfirmBatch(
                NORMImportService.InspectFileForAnnualContext(original, "Original.xlsx", "NIHRACS", "isolated-test"),
                "isolated-test", false, true);
            var previews = NORMImportService.InspectFileForAnnualContext(only2026, "FY2026.xlsx", "NIHRACS", "isolated-test");
            Assert(previews.Count == 1 && previews[0].CanAccept && previews[0].HasReplacement && previews[0].RowCount == 771,
                "The single FY2026 did not resolve its existing source");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(previews, "isolated-test", false, true); }, "replacement confirmation");
            int replacement = NORMImportService.ConfirmBatch(previews, "isolated-test", true, true).Single().ImportId;
            Assert(NORMHelper.Runs == 0 && store[replacement].Rows.Count == 771 &&
                NORMHelper.Committed.Any(call => call.Values.ContainsKey("@event") && (string)call["@event"] == "IMPORT_SOURCE_REPLACED" &&
                    Convert.ToString(call["@detail"]).Contains("Prior import=" + previous[2].ImportId + ";")),
                "Controlled replacement did not retain its audit or source-only behaviour");
            for (int i = 0; i < previous.Count; i++)
            {
                int id = Current(Key("NIHRACS", 2024 + i, "NIHRA", null, "Departmental"));
                Assert(id == (i == 2 ? replacement : previous[i].ImportId) &&
                    NORMCrypto.Sha256(ImportDownload(previous[i].ImportId)) == NORMCrypto.Sha256(original),
                    "The source history or an unaffected year changed");
            }
        });
    }

    static void CheckNihracsPeriod(NORMParsedImport period, int year, int rows, decimal totals)
    {
        Assert(period.FinancialYear == year && period.SourceFile.RowsReconciled && period.Rows.Count == rows &&
            period.Rows.Select(r => r.SourceRowNo).SequenceEqual(Enumerable.Range(8, rows)) &&
            period.Rows.Sum(r => r.SourceDebit.Value) == totals && period.Rows.Sum(r => r.SourceCredit.Value) == totals &&
            period.Rows.All(r => r.AccumBalance == r.SourceDebit.Value - r.SourceCredit.Value), "Source detail or totals changed for FY" + year);
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
