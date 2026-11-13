using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using CPlatform.NORM;

public static partial class Tests
{
    static void PartitionSetup()
    {
        Find(1).Populations.Clear();
        Find(1).Populations.Add(new[] { "1000", null, "Departmental" });
        Find(1).Populations.Add(new[] { "2000", null, "Departmental" });
    }
    static byte[] Modify(byte[] bytes, Action<ExcelWorksheet> change)
    {
        using (ExcelPackage p = new ExcelPackage(new MemoryStream(bytes)))
        { change(p.Workbook.Worksheets[1]); return p.GetAsByteArray(); }
    }
    static List<NORMImportPreview> FilePopulations(byte[] bytes)
    { return NORMImportService.InspectFileForAgency(bytes, "CONTROLLED-population-mechanics.xlsx", 1, new int[0], "isolated-test"); }
    static bool ErrorContains(NORMImportPreview p, string text)
    { return p.Errors.Exists(delegate(string error) { return error.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0; }); }
    static void AssertRows(List<NORMImportPreview> populations, int count)
    {
        NORMImportFileContext file = populations[0].SourceFile;
        Assert(file.RowsReconciled && file.DetailRowCount == count && file.PopulationCount == populations.Count, "File totals do not reconcile");
        HashSet<int> assigned = new HashSet<int>(); int rows = 0;
        foreach (NORMImportPreview population in populations)
        {
            Assert(Object.ReferenceEquals(file, population.SourceFile) && Object.ReferenceEquals(populations[0].Content, population.Content), "Original evidence duplicated/changed");
            Assert(population.SourceRows.Count == population.RowCount && population.RowCount == population.InvalidRowCount + population.ValidatedRowCount, "Population disposition does not reconcile");
            foreach (NORMTrialBalanceRow row in population.SourceRows) { Assert(assigned.Add(row.SourceRowNo), "Row belongs to two populations"); rows++; }
        }
        Assert(rows == count && assigned.Count == count, "Not all detail assigned exactly once");
        NORMImportService.ValidateBatch(populations);
    }
    static void NoWrites()
    {
        NoAcceptance();
        Assert(NORMHelper.Committed.TrueForAll(delegate(NORMHelper.Call c) {
            return c.Values.ContainsKey("@event") && (string)c["@event"] == "IMPORT_REJECTED_SOURCE";
        }), "Unsupported acceptance wrote non-rejection evidence");
    }
    static void RunStep2(string fixtures)
    {
        int before = passed;
        Test("Step2 controlled multi-company file partitions with independent contexts and counts", delegate {
            PartitionSetup(); byte[] bytes = ControlledLegacy(true, true); List<NORMImportPreview> p = FilePopulations(bytes);
            Assert(p.Count == 2, "Expected two populations");
            Context(p[0], 2020, 1, "1000", null, "Departmental"); Context(p[1], 2020, 1, "2000", null, "Departmental");
            AssertRows(p, 4); Assert(p[0].SourceFile.RecordCount == 8 && p[0].SourceFile.MetadataRowCount == 4 && p[0].SourceFile.ControlRowCount == 0, "Unexpected row exclusions");
            Assert(p[0].FileHash == NORMCrypto.Sha256(bytes) && p[0].Debit == 100m && p[1].Credit == 100m && p[0].UnmappedAccounts == 2, "Wrong independent totals/original"); NoWrites();
        });
        Test("Step2 same GL across populations is permitted", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.Cells[7,3].Value = "GL/5"; s.Cells[8,3].Value = "GL/6"; });
            List<NORMImportPreview> p = FilePopulations(bytes); AssertRows(p, 4); Assert(p[0].CanAccept && p[1].CanAccept, "Cross-population account falsely duplicated");
        });
        Test("Step2 company evidence can bind two uniquely configured streams without GL inference", delegate {
            PartitionSetup(); Find(1).Populations[1][2] = "Administered";
            List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true)); AssertRows(p,4);
            Assert(p[0].Population.ReportingStream == "Departmental" && p[1].Population.ReportingStream == "Administered" && p[0].CanAccept && p[1].CanAccept, "Streams not resolved from unique company configuration");
        });
        Test("Step2 duplicate account within population fails only affected account rows", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.Cells[6,3].Value = "GL/5"; });
            List<NORMImportPreview> p = FilePopulations(bytes); AssertRows(p, 4);
            Assert(!p[0].CanAccept && ErrorContains(p[0], "Duplicate source account") && p[0].InvalidRowCount == 2 && p[1].CanAccept && p[1].ValidatedRowCount == 2, "Duplicate disposition wrong");
        });
        Test("Step2 opposite nonzero populations cannot cancel", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) {
                s.Cells[6,5].Value = -90m; s.Cells[6,8].Value = -90m; s.Cells[8,5].Value = -110m; s.Cells[8,8].Value = -110m;
            });
            List<NORMImportPreview> p = FilePopulations(bytes); AssertRows(p, 4);
            Assert(p[0].Net == 10m && p[1].Net == -10m && !p[0].CanAccept && !p[1].CanAccept && ErrorContains(p[0], "exactly zero") && ErrorContains(p[1], "exactly zero") && p[0].InvalidRowCount == 2 && p[1].InvalidRowCount == 2, "Combined zero concealed bad populations");
            Fails(delegate { NORMImportService.ConfirmBatch(p, "isolated-test", true); }, "invalid"); NoWrites();
        });
        Test("Step2 balanced precision failure invalidates batch without hiding valid population", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) {
                s.Cells[7,5].Value = "100.005"; s.Cells[7,8].Value = "100.005"; s.Cells[8,5].Value = "-100.005"; s.Cells[8,8].Value = "-100.005";
            });
            List<NORMImportPreview> p = FilePopulations(bytes); AssertRows(p, 4);
            Assert(p[0].CanAccept && !p[1].CanAccept && !p[1].PrecisionValid && p[1].Net == 0m && p[1].InvalidRowCount == 2, "Precision not validated independently");
            NORM_Import page = new NORM_Import(); page.StageForTest(p[0], true, p[1]);
            Assert(!page.ConfirmationEnabledForTest && page.PreviewForTest.Contains("Invalid rows: 2") && page.PreviewForTest.Contains("valid rows withheld: 2"), "Page disposition hides invalid/withheld rows");
            page.ConfirmForTest(); Assert(page.ErrorForTest.Contains("invalid"), "Forged page confirmation permitted"); NoWrites();
        });
        Test("Step2 unauthorised company retained as invalid rows rather than filtered", delegate {
            PartitionSetup(); Find(1).Populations.RemoveAt(1); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true));
            AssertRows(p, 4); Assert(p[0].CanAccept && !p[1].CanAccept && p[1].InvalidRowCount == 2 && ErrorContains(p[1], "not authorised"), "Unauthorised rows discarded"); NoWrites();
        });
        Test("Step2 ambiguous stream is invalid and never guessed", delegate {
            PartitionSetup(); Find(1).Populations.Add(new[] { "1000", null, "Administered" }); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true));
            AssertRows(p, 4); Assert(!p[0].CanAccept && p[0].Population.ReportingStream == null && ErrorContains(p[0], "ambiguous") && p[1].CanAccept, "Ambiguous source guessed");
        });
        Test("Step2 ambiguous release is rejected without newest choice", delegate {
            PartitionSetup(); Find(2).Populations[0][0] = "1000"; Add(9, 2019, "1000", null, "Departmental");
            byte[] bytes = Modify(ControlledLegacy(false, true), delegate(ExcelWorksheet s) { s.Cells[1,2].Value = 2019; s.Cells[2,2].Value = "01.07.2018"; s.Cells[3,2].Value = "30.06.2019"; });
            Fails(delegate { FilePopulations(bytes); }, "More than one approved"); NoWrites();
        });
        Test("Step2 non-current partial member invalidates a batch", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true)); Find(2).Populations[0][0] = "1000";
            byte[] partial = Modify(ControlledLegacy(false, true), delegate(ExcelWorksheet s) { s.Cells[1,2].Value = 2019; s.Cells[2,2].Value = "01.07.2018"; s.Cells[3,2].Value = "31.12.2018"; });
            List<NORMImportPreview> invalid = FilePopulations(partial); Assert(!invalid[0].CanAccept && invalid[0].InvalidRowCount == 2 && ErrorContains(invalid[0], "complete financial year"), "Invalid member coverage accepted");
            p.AddRange(invalid); NORMImportService.ValidateBatch(p); Fails(delegate { NORMImportService.ConfirmBatch(p, "isolated-test", true); }, "invalid"); NoWrites();
        });
        Test("Step2 out-of-year source boundaries reject entire file", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.Cells[2,2].Value = "01.07.2018"; });
            Fails(delegate { FilePopulations(bytes); }, "posting-date range"); NoWrites();
        });
        Test("Step2 metadata year contradiction rejects entire file", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.InsertRow(4,1); s.Cells[4,1].Value = "Fiscal Year"; s.Cells[4,2].Value = 2019; });
            Fails(delegate { FilePopulations(bytes); }, "conflicting"); NoWrites();
        });
        Test("Step2 blank records are counted separately and original row numbers retained", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.InsertRow(7,1); });
            List<NORMImportPreview> p = FilePopulations(bytes); AssertRows(p, 4);
            Assert(p[0].SourceFile.BlankRowCount == 1 && p[0].SourceFile.RecordCount == 9 && p[1].SourceRows[0].SourceRowNo == 8, "Blank row/detail source indices lost");
        });
        Test("Step2 unrecognised financial detail is rejected not skipped", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.Cells[9,3].Value = "GL/9"; s.Cells[9,8].Value = 20m; });
            Fails(delegate { FilePopulations(bytes); }, "Unsupported nonblank legacy"); NoWrites();
        });
        Test("Step2 undocumented subtotal or control is not manufactured", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.Cells[9,1].Value = "Total"; s.Cells[9,8].Value = 0m; });
            Fails(delegate { FilePopulations(bytes); }, "undocumented subtotal/control"); NoWrites();
        });
        Test("Step2 financial detail before header cannot masquerade as metadata", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.InsertRow(4,1); s.Cells[4,1].Value = "1000"; s.Cells[4,3].Value = "GL/99"; s.Cells[4,8].Value = 0m; });
            Fails(delegate { FilePopulations(bytes); }, "before the legacy ERP header"); NoWrites();
        });
        Test("Step2 pre-header amounts without company cannot silently become metadata", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.InsertRow(4,1); s.Cells[4,3].Value = "GL/99"; s.Cells[4,8].Value = 20m; });
            Fails(delegate { FilePopulations(bytes); }, "before the legacy ERP header"); NoWrites();
        });
        Test("Step2 unsupported extra legacy detail columns reject", delegate {
            PartitionSetup(); byte[] bytes = Modify(ControlledLegacy(true, true), delegate(ExcelWorksheet s) { s.Cells[5,9].Value = "new ledger column"; });
            Fails(delegate { FilePopulations(bytes); }, "Unsupported legacy ERP detail field"); NoWrites();
        });
        Test("Step2 financial rows on an extra legacy worksheet cannot be skipped", delegate {
            PartitionSetup(); byte[] bytes;
            using (ExcelPackage p = new ExcelPackage(new MemoryStream(ControlledLegacy(true, true))))
            {
                ExcelWorksheet hidden = p.Workbook.Worksheets.Add("Unsupported financial rows"); hidden.Cells[1,1].Value = "1000"; hidden.Cells[1,3].Value = "GL/99"; hidden.Cells[1,8].Value = 20m;
                bytes = p.GetAsByteArray();
            }
            Fails(delegate { FilePopulations(bytes); }, "additional legacy worksheet"); NoWrites();
        });
        Test("Step2 native records outside recognised detail cannot pass row accounting", delegate {
            Add(5,2026,"1000","0L","Departmental"); NORMHelper.CurrentYear = 2026;
            byte[] bytes = Modify(defence, delegate(ExcelWorksheet s) { s.Cells[s.Dimension.End.Row + 1,9].Value = "Unrecognised record"; });
            Fails(delegate { FilePopulations(bytes); }, "record counts do not reconcile"); NoWrites();
        });
        Test("Step2 native financial detail before header cannot be counted as metadata", delegate {
            Add(5,2026,"1000","0L","Departmental"); NORMHelper.CurrentYear = 2026;
            byte[] bytes = Modify(defence, delegate(ExcelWorksheet s) { s.Cells[26,1].Value = "1000"; s.Cells[26,3].Value = "DCOA/999999"; s.Cells[26,8].Value = 0m; });
            Fails(delegate { FilePopulations(bytes); }, "before the recognised ERP report header"); NoWrites();
        });
        Test("Step2 source row cannot belong to two populations", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true)); p[1].PopulationRows[0].SourceRowNo = p[0].PopulationRows[0].SourceRowNo;
            Fails(delegate { NORMImportService.ValidateBatch(p); }, "assigned more than once"); NoWrites();
        });
        Test("Step2 omitted population fails file reconciliation", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true));
            Fails(delegate { NORMImportService.ValidateBatch(new[] { p[0] }); }, "Not every source detail"); NoWrites();
        });
        Test("Step2 population count cannot disagree with its own source rows", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true)); p[0].RowCount--;
            Fails(delegate { NORMImportService.ValidateBatch(p); }, "Population detail row count"); NoWrites();
        });
        Test("Step2 duplicate five-part identity across selected files rejects", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true)); p.AddRange(FilePopulations(ControlledLegacy(false, true)));
            Fails(delegate { NORMImportService.ValidateBatch(p); }, "same agency, financial year"); NoWrites();
        });
        Test("Step2 NULL and 0L remain distinct with evidenced ledger in separate file", delegate {
            PartitionSetup(); Find(1).Populations.Add(new[] { "1000", "0L", "Departmental" }); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(true, true));
            byte[] bytes = Modify(ControlledDefence("1000", "0L", "Departmental"), delegate(ExcelWorksheet s) {
                for (int row = 1; row < 26; row++) {
                    if (s.Cells[row,1].Text == "Funds of Flow Ledger Fiscal Year") { s.Cells[row,2].Value = 2020; }
                    if (s.Cells[row,1].Text == "Posting Date From") { s.Cells[row,2].Value = "01.07.2019"; }
                    if (s.Cells[row,1].Text == "Posting Date To") { s.Cells[row,2].Value = "30.06.2020"; }
                }
            });
            List<NORMImportPreview> other = FilePopulations(bytes); Assert(p[0].Population.Ledger == null && other[0].Population.Ledger == "0L", "NULL became 0L");
            p.AddRange(other); NORMImportService.ValidateBatch(p);
        });
        Test("Step2 complete multi-population page confirmation enabled and subset service call rejected", delegate {
            PartitionSetup(); NORM_Import page = new NORM_Import(); byte[] bytes = ControlledLegacy(true, true);
            page.InspectUploadsForTest(1, new int[0], new[] { bytes }, new[] { "CONTROLLED-multi-company.xlsx" });
            Assert(page.ErrorForTest == "" && page.PreviewsForTest.Count == 2 && page.ConfirmationEnabledForTest && page.PreviewForTest.Contains("File row accounting: 8") && page.PreviewForTest.Contains("Company</dt><dd>1000") && page.PreviewForTest.Contains("Company</dt><dd>2000") && page.PreviewForTest.Contains("Accepted rows: 0"), "Population preview missing: " + page.ErrorForTest);
            NORMImportPreview subset = FilePopulations(bytes)[0];
            Fails(delegate { NORMImportService.Confirm(subset, "isolated-test"); }, "complete original file"); NoWrites();
            int commitsBefore = NORMHelper.Commits;
            page.ClearUploadedFilesForTest(); page.ConfirmForTest();
            Assert(page.ErrorForTest == "" && page.ResultForTest.Contains("Source accepted") && NORMHelper.Commits == commitsBefore + 1, "Whole file page confirmation failed: " + page.ErrorForTest);
        });
        Test("Step2 original single-population acceptance stays on existing transaction", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(false, true)); AssertRows(p, 2);
            NORMImportService.Confirm(p[0], "isolated-test");
            Assert(NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow"); }).Count == 2, "Single source acceptance regressed");
        });
        Test("Step2 invalid member rejects same-release batch independently of feature safety guards", delegate {
            PartitionSetup(); List<NORMImportPreview> p = FilePopulations(ControlledLegacy(false, true));
            byte[] invalid = Modify(ControlledLegacy(false, true), delegate(ExcelWorksheet s) {
                s.Cells[5,1].Value = "2000"; s.Cells[6,1].Value = "2000";
                s.Cells[5,5].Value = "100.005"; s.Cells[5,8].Value = "100.005"; s.Cells[6,5].Value = "-100.005"; s.Cells[6,8].Value = "-100.005";
            });
            p.AddRange(FilePopulations(invalid)); NORMImportService.ValidateBatch(p);
            Assert(p[0].CanAccept && !p[1].CanAccept && NORMImportService.BatchConfirmationBlockReason(p) == null, "Did not exercise ordinary invalid-member path");
            NORM_Import page = new NORM_Import(); page.StageForTest(p[0], true, p[1]);
            Assert(!page.ConfirmationEnabledForTest && page.PreviewForTest.Contains("valid rows withheld: 2"), "Invalid batch not visibly withheld");
            Fails(delegate { NORMImportService.ConfirmBatch(p, "isolated-test", true); }, "invalid"); NoWrites();
        });
        Test("Step2 invalid coverage member blocks a same-release valid source before writes", delegate {
            PartitionSetup(); NORMHelper.CurrentYear = 2019; List<NORMImportPreview> p = FilePopulations(ControlledLegacy(false, true));
            byte[] partial = Modify(ControlledLegacy(false, true), delegate(ExcelWorksheet s) { s.Cells[5,1].Value = "2000"; s.Cells[6,1].Value = "2000"; s.Cells[3,2].Value = "31.12.2019"; });
            p.AddRange(FilePopulations(partial)); NORMImportService.ValidateBatch(p);
            Assert(p[0].CanAccept && !p[1].CanAccept && ErrorContains(p[1], "complete financial year") && p[1].InvalidRowCount == 2, "Coverage not independently invalid");
            Fails(delegate { NORMImportService.ConfirmBatch(p, "isolated-test", true); }, "invalid"); NoWrites();
        });
        Test("Step2 mixed-release batch now accepts through Step3 transaction", delegate {
            List<NORMImportPreview> p = FilePopulations(aag2020); p.AddRange(FilePopulations(aag2019)); NORMImportService.ValidateBatch(p);
            Assert(NORMImportService.ConfirmBatch(p, "isolated-test", false).Count == 2 && NORMHelper.Commits == 1, "Mixed-release acceptance failed");
        });
        Test("Step2 genuine native profiles keep one population and reconcile documented records", delegate {
            string[] names = { "AAG_TrialBalanceFinStats150_201718.csv", "AAG_TrialBalanceFinStats150_201819.csv", "AAG_TrialBalanceFinStats150.csv", "coy 86.txt", "coy 87.txt", "coy 80.txt", "Defence TB - ERP Format FY26.xlsx" };
            int[] years = { 2018,2019,2020,2016,2016,2016,2026 }; string[] companies = { "150","150","150","86","87","80","1000" }; int[] rows = { 379,386,409,18,17,412,984 };
            for (int i = 0; i < names.Length; i++)
            {
                releases.Clear(); Add(1, years[i], companies[i], i == 6 ? "0L" : null, "Departmental"); NORMHelper.CurrentYear = years[i];
                byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, names[i])); List<NORMImportPreview> p = FilePopulations(bytes);
                AssertRows(p, rows[i]); Assert(p.Count == 1 && p[0].CanAccept && p[0].Net == 0m && p[0].FileHash == NORMCrypto.Sha256(bytes), "Native source interpretation/hash changed: " + names[i]);
                Assert(p[0].SourceFile.ControlRowCount == (i < 3 ? 0 : i == 6 ? 2 : 1), "Native source controls not distinguished");
            }
        });
        Console.WriteLine("Package 1C Step 2: " + (passed - before) + " Passed / 0 Failed; combined Step 1+2: " + passed);
    }
}
