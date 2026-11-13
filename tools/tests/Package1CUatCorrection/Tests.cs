using System;
using System.IO;
using System.Collections.Generic;
using System.Web.UI;
using CPlatform.NORM;
using OfficeOpenXml;

public static partial class Tests
{
    static string CorrectionFile(string name) { return Path.Combine(root,"docs/analysis/Stage 1/Package 1C/UAT Fixtures/"+name); }
    static void CorrectionSetup()
    {
        HistorySetup(); releases.Clear(); NORMHelper.CurrentYear=2026;
        Add(7,2025,"1000",null,"Departmental").Entity="UAT_P1C";
        foreach(int id in new[]{8,9})
        {
            Release r=Add(id,2026,"1000",null,"Departmental");r.Entity="UAT_P1C";
            r.Populations.Add(new[]{"2000",(string)null,"Administered"});r.Populations.Add(new[]{"1000","0L","Departmental"});
            r.Populations.Add(new[]{"1000","2L","Departmental"});r.Populations.Add(new[]{"9000",(string)null,"Administered"});
        }
    }
    static List<NORMImportPreview> CorrectionInspect(string name)
    { return NORMImportService.InspectFileForAgency(File.ReadAllBytes(CorrectionFile(name)),name,8,new int[0],"isolated-test"); }
    static void CTest(string name,Action action) { Test("Correction "+name,delegate { CorrectionSetup();action(); }); }
    static void RunUatCorrection()
    {
        int before=passed;
        CTest("actual FY2025 file02 automatically binds release7 with complete retained facts",delegate {
            var p=CorrectionInspect("02_CONTROLLED_FY2025_company1000.xlsx")[0];
            Assert(p.CanAccept && p.ReleaseId==7 && p.FinancialYear==2025 && p.Entity=="UAT_P1C" && p.Population.Company=="1000" && p.Population.Ledger==null && p.Population.ReportingStream=="Departmental","Wrong eligibility/binding");
            Assert(candidateReads==1 && p.RowCount==2 && p.Debit==50m && p.Credit==50m && p.Net==0m && p.ProfileVersion=="LEGACY-ERP/1","Wrong amounts/profile");
            Assert(p.CoverageStart==new DateTime(2024,7,1) && p.CoverageEnd==new DateTime(2025,6,30) && p.IsFullYear==true && p.PeriodStart==1 && p.PeriodEnd==12,"Wrong coverage");
            Assert(p.FileHash=="1370aa84d60096faa8e7e9c53fb716c1cddc7db06859bab6d00d6d4ee1b81322","Actual fixture changed");
        });
        CTest("actual negative mixed-year batch has only expected two invalid rows and six withheld",delegate {
            var p=CorrectionInspect("07_CONTROLLED_INVALID_FY2026_multi-company.xlsx");p.AddRange(CorrectionInspect("02_CONTROLLED_FY2025_company1000.xlsx"));NORMImportService.ValidateBatch(p);
            int invalid=0,valid=0,invalidRows=0,validRows=0;
            foreach(var member in p) { if(member.CanAccept){valid++;validRows+=member.ValidatedRowCount;}else{invalid++;invalidRows+=member.InvalidRowCount;Assert(member.Population.Company=="2000" && member.Net==0.01m,"Unexpected invalid population");} }
            Assert(valid==2 && invalid==1 && validRows==6 && invalidRows==2,"Wrong batch dispositions");
            NORM_Import page=new NORM_Import();page.StageForTest(p[0],true,p[1],p[2]);
            Assert(!page.ConfirmationEnabledForTest && page.CorrectionButtonHtml().Contains("disabled=\"disabled\"") && page.PreviewForTest.Contains("The batch cannot be accepted"),"Missing disabled semantics/explanation");
            page.ConfirmForTest();Assert(page.ErrorForTest.Contains("invalid"),"Forced page accepted invalid batch");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"invalid");
            Assert(NORMHelper.Opens==0 && NORMHelper.Transactions==0 && NORMHelper.Committed.Count==0,"Invalid confirmation reached storage");
        });
        CTest("actual four-file five-population batch inspects and commits atomically preserving originals",delegate {
            var p=CorrectionInspect("01_CONTROLLED_A_FY2026_multi-company.xlsx");p.AddRange(CorrectionInspect("02_CONTROLLED_FY2025_company1000.xlsx"));
            p.AddRange(NORMImportService.InspectFileForAgency(defence,"Defence TB - ERP Format FY26.xlsx",8,new int[0],"isolated-test"));p.AddRange(CorrectionInspect("04_CONTROLLED_FY2026_company1000_2L.xlsx"));
            NORMImportService.ValidateBatch(p);int rows=0;HashSet<string> files=new HashSet<string>(),scopes=new HashSet<string>();
            foreach(var member in p){Assert(member.CanAccept,"Positive member invalid: "+String.Join(";",member.Errors.ToArray()));rows+=member.RowCount;files.Add(member.SourceFile.Token);scopes.Add(Key(member.Entity,member.FinancialYear,member.Population.Company,member.Population.Ledger,member.Population.ReportingStream));}
            Assert(p.Count==5 && rows==1976 && files.Count==4 && scopes.Count==5,"Wrong positive batch counts/identities");
            NORM_Import page=new NORM_Import();page.StageForTest(p[0],true,p[1],p[2],p[3],p[4]);Assert(page.ConfirmationEnabledForTest,"Positive confirmation unavailable");
            string old=StoreFingerprint(true);NORMHelper.FailCalculation=true;var outcomes=NORMImportService.ConfirmBatch(p,"isolated-test",false);
            Assert(outcomes.Count==5 && acceptanceCommits==1 && NORMHelper.Commits==1 && StoreFingerprint(true)==old,"Nonatomic commit or changed historical evidence");
            for(int i=0;i<p.Count;i++){var stored=store[outcomes[i].ImportId];Assert(stored.Release==p[i].ReleaseId && stored.Year==p[i].FinancialYear && stored.Hash==p[i].FileHash && stored.Rows.Count==p[i].RowCount,"Wrong stored member context");Assert(NORMCrypto.Sha256(ImportDownload(stored.Id))==p[i].FileHash,"Accepted failed-calculation original changed");}
            Assert(store[outcomes[0].ImportId].Hash==store[outcomes[1].ImportId].Hash,"Multi-population original was split");
        });
        CTest("non-current FY2025 partial source remains rejected",delegate {
            byte[] partial=Modify(File.ReadAllBytes(CorrectionFile("02_CONTROLLED_FY2025_company1000.xlsx")),delegate(ExcelWorksheet s){s.Cells[3,2].Value="31.12.2024";});
            var p=NORMImportService.InspectFileForAgency(partial,"CONTROLLED partial FY2025.xlsx",8,new int[0],"isolated-test");
            Assert(!p[0].CanAccept && ErrorContains(p[0],"complete financial year"),"Partial completed year accepted");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",false); },"invalid");
        });
        CTest("ROMAN and historical period-pair are not introduced into governed acceptance",delegate {
            byte[] roman=System.Text.Encoding.UTF8.GetBytes("Carryforward Periods 00-00 2025 Reporting Periods 01-10 2025\n|1000 100 Cash AUD 100.00|\n|1000 200 Equity AUD 100.00-|\n");
            bool rejected=false;try { NORMImportService.InspectFileForAgency(roman,"CONTROLLED ROMAN 01-10.txt",8,new int[0],"isolated-test"); }catch(InvalidDataException){rejected=true;}
            Assert(rejected,"New ROMAN transition inspection enabled");
            byte[] erp=Modify(File.ReadAllBytes(CorrectionFile("02_CONTROLLED_FY2025_company1000.xlsx")),delegate(ExcelWorksheet s){s.Cells[2,2].Value="01.05.2025";});
            var p=NORMImportService.InspectFileForAgency(erp,"CONTROLLED ERP 11-12.xlsx",8,new int[0],"isolated-test");Assert(!p[0].CanAccept,"Transition partial bypassed full coverage");
            Fails(delegate { NORMImportService.Import(erp,"legacy.xlsx","ERP",7,"isolated-test"); },"controlled ROMAN");NoAcceptance();
        });
        CTest("standalone FY2025 eligibility has no test-agency exception",delegate {
            Find(7).Entity=Find(8).Entity=Find(9).Entity="OTHER_AUTHORISED";
            Assert(CorrectionInspect("02_CONTROLLED_FY2025_company1000.xlsx")[0].CanAccept,"Eligibility hard-coded to UAT_P1C");
        });
        CTest("ambiguous and missing historical approval still fail closed",delegate {
            Add(17,2025,"1000",null,"Departmental").Entity="UAT_P1C";
            Fails(delegate { CorrectionInspect("02_CONTROLLED_FY2025_company1000.xlsx"); },"More than one approved");
            Find(17).Inactive=true;Find(7).Inactive=true;Fails(delegate { CorrectionInspect("02_CONTROLLED_FY2025_company1000.xlsx"); },"No active approved");
        });
        CTest("history context remains server-authorised without newest fallback",delegate {
            NORM_SourceHistoryPage page=new NORM_SourceHistoryPage();page.RenderForTest("release=999&viewStream=Administered");Assert(page.StatusForTest==403,"Unknown release accepted");
            NORMHelper.EntityAccess=delegate { return false; };page=new NORM_SourceHistoryPage();page.RenderForTest("release=8");Assert(page.StatusForTest==403,"Unauthorised entity accepted");
        });
        Console.WriteLine("UAT correction: "+(passed-before)+" Passed / 0 Failed; combined Step1-4 and correction: "+passed);
    }
}
namespace CPlatform.NORM
{
    public partial class NORM_Import
    {
        public string CorrectionButtonHtml(){ConfirmButton.CssClass="norm-button";StringWriter w=new StringWriter();ConfirmButton.RenderControl(new HtmlTextWriter(w));return w.ToString();}
    }
}
