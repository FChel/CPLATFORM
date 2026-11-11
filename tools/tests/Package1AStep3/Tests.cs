using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Security.Principal;
using CPlatform.NORM;
using OfficeOpenXml;

public static class Tests
{
    static int passed;
    static string root;
    static byte[] original=Encoding.UTF8.GetBytes("Labelled isolated legacy source; no accounting evidence.");
    static void Check(bool ok,string why) {if(!ok)throw new Exception(why);}
    static void Test(string name,Action action) {NORMHelper.Reset();action();passed++;Console.WriteLine("PASS "+name);}
    static void Reject<T>(Action action) where T:Exception {try {action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Untouched() { Check(NORMHelper.Current==2&&NORMHelper.Documents.Count==0&&NORMHelper.Audit.Count==0,"Failed operation changed accepted state"); }
    static NORMStartOfYearSetup.UploadOutcome Upload(string type=NORMStartOfYearSetup.PriorDocumentType,long expected=0,int setup=2,string file="isolated.doc",byte[] content=null)
    {return NORMStartOfYearSetup.Upload(setup,type,content??original,file,null,expected,NORMHelper.Stamp,NORMHelper.CurrentUserId());}
    static void AddOriginal(int setup,long id,string type,bool superseded)
    {NORMHelper.Documents.Add(new NORMHelper.Document {Id=id,Setup=setup,Type=type,Name="retained.doc",Hash=NORMCrypto.Sha256(original),Content=original,Status="ReviewRequired",Superseded=superseded});}
    static void YearTests()
    {
        Test("saved current year is the only default",delegate {Check(NORMStartOfYearSetup.DefaultFinancialYear("DEPT")==2026,"Wrong default");NORMHelper.Current=1;Check(NORMStartOfYearSetup.DefaultFinancialYear("DEPT")==2025,"Release/calendar overrode saved year");});
        Test("missing designation never uses fallback",delegate {NORMHelper.Current=0;Reject<InvalidOperationException>(delegate {NORMStartOfYearSetup.ResolveCurrentFinancialYear("DEPT");});});
        Test("duplicate current designation fails closed",delegate {NORMHelper.Setups.Add(new NORMHelper.SetupRow {Id=2,Year=2025,Entity="DEPT",Updated=NORMHelper.Stamp});Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.DefaultFinancialYear("DEPT");});});
        Test("initial explicit activation requires approval and creates saved setup",delegate {NORMHelper.Current=0;NORMHelper.ApprovedYear=2027;NORMHelper.Names["DEPT2027"]="Defence 2027";NORMStartOfYearSetup.SaveYear("DEPT",2027,0,null,"test");Check(NORMHelper.Setups.Count==4&&NORMHelper.Current==4,"Initial setup not saved");});
        Test("activation and audit preserve historical setup",delegate {NORMHelper.ApprovedYear=2025;NORMStartOfYearSetup.SaveYear("DEPT",2025,2,NORMHelper.Stamp,"isolated-operator");Check(NORMHelper.Current==1&&NORMHelper.Setups.Count==3,"Wrong activation");Check(NORMHelper.Audit.Single().Contains("previous current year: 2026")&&NORMHelper.Audit[0].Contains("isolated-operator"),"Missing audit");Check(NORMHelper.Commands.All(x=>!x.Contains("UPDATE dbo.tblNORM_Import")&&!x.Contains("UPDATE dbo.tblNORM_CalculationRun")),"History modified");});
        Test("unapproved year rejected without writes",delegate {Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",2025,2,NORMHelper.Stamp,"test");});Untouched();});
        Test("unconfigured entity rejected",delegate {NORMHelper.NoConfiguredEntity=true;Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",2026,2,NORMHelper.Stamp,"test");});Untouched();});
        Test("stale designation id rejected",delegate {Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",2026,1,NORMHelper.Stamp,"test");});Untouched();});
        Test("same-year ABA timestamp change rejected",delegate {Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",2026,2,NORMHelper.Stamp.AddSeconds(-1),"test");});Untouched();});
        Test("designation revocation rechecked at write",delegate {NORMHelper.RevokedAtWrite=true;Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",2026,2,NORMHelper.Stamp,"test");});Untouched();});
        Test("designation audit failure rolls back",delegate {NORMHelper.FailAudit=true;NORMHelper.ApprovedYear=2025;Reject<Exception>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",2025,2,NORMHelper.Stamp,"test");});Untouched();});
        Test("invalid year rejected",delegate {Reject<InvalidDataException>(delegate{NORMStartOfYearSetup.SaveYear("DEPT",26,2,NORMHelper.Stamp,"test");});Untouched();});
        Test("assigned preparer entities filtered",delegate {Check(NORMStartOfYearSetup.Entities().Rows.Count==1,"Cross entity leak");Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.Setup(3);});});
        Test("Administrator all configured entities",delegate {NORMHelper.Admin=true;NORMHelper.Assigned=false;Check(NORMStartOfYearSetup.Entities().Rows.Count==2&&NORMStartOfYearSetup.Setup(3)!=null,"Admin access restricted");});
        Test("unassigned and revoked direct setup/list denied",delegate {NORMHelper.Assigned=false;Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.Setups("DEPT");});Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.LoadDocuments(2);});Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.LoadFigures(2);});});
        Test("forged setup identifier rejected",delegate {Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.Setup(999);});});
    }
    static void NameTests()
    {
        Test("name edit audited with previous/new/operator",delegate {NORMStartOfYearSetup.SaveReportingName(2,"  New Defence  ","Defence 2026","name-operator");Check(NORMHelper.Names["DEPT2026"]=="New Defence"&&NORMHelper.Names["DEPT2025"]=="Defence 2025","Wrong name scope");Check(NORMHelper.Audit.Single().Contains("previous: Defence 2026; new: New Defence")&&NORMHelper.Audit[0].Contains("name-operator"),"Missing audit");});
        foreach(string invalid in new[]{"", "   ",new string('n',201),"bad\nname"}) {string value=invalid;Test("invalid name rejected: "+invalid.Length,delegate {Reject<InvalidDataException>(delegate{NORMStartOfYearSetup.SaveReportingName(2,value,"Defence 2026","test");});Untouched();});}
        Test("stale name edit rejected",delegate {Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.SaveReportingName(2,"new","stale","test");});Untouched();});
        Test("name permission rechecked at write",delegate {NORMHelper.RevokedAtWrite=true;Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.SaveReportingName(2,"new","Defence 2026","test");});Untouched();});
        Test("name audit failure leaves name intact",delegate {NORMHelper.FailAudit=true;Reject<Exception>(delegate{NORMStartOfYearSetup.SaveReportingName(2,"new","Defence 2026","test");});Check(NORMHelper.Names["DEPT2026"]=="Defence 2026","Unaudited edit");});
    }
    static void DocumentTests()
    {
        foreach(string t in new[]{NORMStartOfYearSetup.PriorDocumentType,NORMStartOfYearSetup.BudgetDocumentType}) {
            string type=t;
            Test(type+" upload/hash/download ReviewRequired",delegate {var result=Upload(type);var d=NORMStartOfYearSetup.DownloadDocument(result.DocumentId,"downloader");Check(result.Status=="ReviewRequired"&&d.Content.SequenceEqual(original)&&d.Hash==NORMCrypto.Sha256(original)&&d.FileName=="isolated.doc","Original evidence changed");Check(NORMHelper.Current==2&&NORMHelper.Audit.Count==2,"Wrong state/audit");});
            Test(type+" superseded and historical originals",delegate {AddOriginal(1,1,type,true);AddOriginal(2,2,type,false);var old=NORMStartOfYearSetup.DownloadDocument(1,"test");Check(old.Content.SequenceEqual(original),"Historical original lost");Check(NORMStartOfYearSetup.LoadDocuments(1).Rows.Count==1,"History hidden");});
        }
        foreach(string t in new[]{NORMStartOfYearSetup.PriorDocumentType,NORMStartOfYearSetup.BudgetDocumentType}) {
            string type=t;
            Test(type+" existing XLSX extraction remains functional",delegate {
                byte[] bytes;
                using(var workbook=new ExcelPackage()) {
                    var sheet=workbook.Workbook.Worksheets.Add("isolated statements");
                    sheet.Cells[1,1].Value="Statement of Comprehensive Income";
                    sheet.Cells[2,1].Value="Year";sheet.Cells[2,2].Value=type==NORMStartOfYearSetup.PriorDocumentType?"2026":"2024-25";
                    sheet.Cells[2,3].Value=type==NORMStartOfYearSetup.PriorDocumentType?"2025":"2025-26";
                    sheet.Cells[3,1].Value="Total income";sheet.Cells[3,2].Value=456;sheet.Cells[3,3].Value=123;
                    bytes=workbook.GetAsByteArray();
                }
                var result=Upload(type,file:"labelled-isolated.xlsx",content:bytes);
                Check(result.Status=="Extracted"&&result.FigureCount>0,"Existing extraction failed");
                Check(NORMStartOfYearSetup.DownloadDocument(result.DocumentId,"test").Content.SequenceEqual(bytes),"Extracted original changed");
            });
        }
        Test("unsupported extension rejected",delegate {Reject<InvalidDataException>(delegate{Upload(file:"unsafe.exe");});Untouched();});
        Test("controlled upload replacement retains predecessor",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);var result=Upload(expected:1);Check(NORMHelper.Documents.Count==2&&NORMHelper.Documents[0].Superseded,"Replacement lost history");Check(NORMStartOfYearSetup.DownloadDocument(1,"test").Content.SequenceEqual(original),"Old bytes lost");Check(NORMStartOfYearSetup.LoadDocuments(2).Rows.Count==2,"Superseded hidden");});
        Test("historical setup upload does not activate year",delegate {NORMHelper.ApprovedYear=2025;Upload(setup:1);Check(NORMHelper.Current==2&&NORMHelper.Documents.Single().Setup==1,"Upload changed year");});
        Test("upload unapproved exact year rejected",delegate {NORMHelper.ApprovedYear=2025;Reject<InvalidOperationException>(delegate{Upload();});Untouched();});
        Test("empty upload cannot change designation",delegate {Reject<InvalidDataException>(delegate{Upload(content:new byte[0]);});Untouched();});
        Test("malformed workbook leaves no partial acceptance",delegate {Reject<Exception>(delegate{Upload(file:"bad.xlsx");});Untouched();});
        Test("invalid PDF page rejected",delegate {Reject<InvalidDataException>(delegate{Upload(file:"test.pdf");});Untouched();});
        Test("invalid document type rejected",delegate {Reject<ArgumentException>(delegate{Upload("forged");});Untouched();});
        Test("upload setup from unauthorised entity rejected",delegate {Reject<UnauthorizedAccessException>(delegate{Upload(setup:3);});Untouched();});
        Test("upload permission revoked after read",delegate {NORMHelper.RevokedAtWrite=true;Reject<UnauthorizedAccessException>(delegate{Upload();});Untouched();});
        Test("stale document upload rejected",delegate {Reject<InvalidOperationException>(delegate{Upload(expected:99);});Untouched();});
        Test("stale setup upload rejected",delegate {Reject<InvalidOperationException>(delegate{NORMStartOfYearSetup.Upload(2,NORMStartOfYearSetup.PriorDocumentType,original,"test.doc",null,0,NORMHelper.Stamp.AddSeconds(-1),"test");});Untouched();});
        Test("upload audit failure restores prior original",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);NORMHelper.FailAudit=true;Reject<Exception>(delegate{Upload(expected:1);});Check(NORMHelper.Documents.Count==1&&!NORMHelper.Documents[0].Superseded&&NORMHelper.Current==2,"Partial replacement");});
        Test("upload commit failure leaves no acceptance",delegate {NORMHelper.FailCommit=true;Reject<Exception>(delegate{Upload();});Untouched();});
        Test("forged download identifier rejected",delegate {Reject<FileNotFoundException>(delegate{NORMStartOfYearSetup.DownloadDocument(999,"test");});});
        Test("download stored entity governs access",delegate {AddOriginal(3,1,NORMStartOfYearSetup.PriorDocumentType,false);Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.DownloadDocument(1,"test");});Check(NORMHelper.Audit.Count==0,"Unauthorised download audited as success");});
        Test("download assignment revocation enforced",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);NORMHelper.RevokedAtWrite=true;Reject<UnauthorizedAccessException>(delegate{NORMStartOfYearSetup.DownloadDocument(1,"test");});});
        Test("hash mismatch prevents download",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);NORMHelper.Documents[0].Hash=new string('0',64);Reject<InvalidDataException>(delegate{NORMStartOfYearSetup.DownloadDocument(1,"test");});Check(NORMHelper.Audit.Count==0,"Corrupt file reported successful");});
        Test("download audit failure fails closed",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);NORMHelper.FailAudit=true;Reject<Exception>(delegate{NORMStartOfYearSetup.DownloadDocument(1,"test");});Check(NORMHelper.Audit.Count==0,"False download audit");});
    }
    static void PageTests()
    {
        Test("page activation uses stale state token",delegate {var p=new NORM_YearSetup();p.Open();NORMHelper.Setups[1].Updated=NORMHelper.Stamp.AddSeconds(2);p.Save("2026");Check(p.ErrorResult.Contains("designation changed"),"Page lost concurrency token");Untouched();});
        Test("page missing file preserves saved year, accepted original and audit",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);var p=new NORM_YearSetup();p.Open();p.NoFileUpload();Check(p.ErrorResult.Contains("Choose a source"),"Wrong page result");Check(NORMHelper.Current==2&&NORMHelper.Documents.Count==1&&!NORMHelper.Documents[0].Superseded&&NORMHelper.Documents[0].Content.SequenceEqual(original)&&NORMHelper.Audit.Count==0,"Failed upload changed accepted setup/evidence");});
        Test("page explicit historical selection and encoded name",delegate {AddOriginal(1,1,NORMStartOfYearSetup.PriorDocumentType,true);NORMHelper.Documents[0].Name="<script>.doc";var p=new NORM_YearSetup();p.Open();p.SelectYear(1);Check(p.DocumentsHtml.Contains("document=1")&&p.DocumentsHtml.Contains("Superseded")&&p.DocumentsHtml.Contains("&lt;script&gt;"),"Wrong listing");Check(NORMHelper.Current==2,"Selection activated year");});
        Test("page forged setup cannot edit another entity",delegate {var p=new NORM_YearSetup();p.Open();p.ForgeSetup(3);p.Name("forged");Check(p.ErrorResult.Length>0&&NORMHelper.Names["OTHER2026"]=="Other entity","Forged page write");});
        Test("page unassigned user sees no entities",delegate {NORMHelper.Assigned=false;var p=new NORM_YearSetup();p.Open();Check(p.EntityCount==0&&p.ErrorResult.Length>0,"Unassigned page leak");});
        Test("page name edit works and rechecks revocation",delegate {var p=new NORM_YearSetup();p.Open();p.Name("New name");Check(NORMHelper.Names["DEPT2026"]=="New name","Name handler failed");NORMHelper.Assigned=false;p.Name("denied");Check(NORMHelper.Names["DEPT2026"]=="New name"&&p.ErrorResult.Length>0,"Revoked write");});
        Test("page revocation clears cached protected display",delegate {AddOriginal(2,1,NORMStartOfYearSetup.PriorDocumentType,false);var p=new NORM_YearSetup();p.Open();Check(p.DocumentsHtml.Contains("retained.doc"),"No initial document");NORMHelper.Assigned=false;p.RenderForTest();Check(p.DocumentsHtml.Length==0&&p.EntityCount==0&&p.ErrorResult.Length>0,"Revoked metadata leaked in response");});
        Test("handler rejects malformed identifier",delegate {var ctx=Context("document=bad");new NORMYearSetupDocumentHandler().ProcessRequest(ctx);Check(ctx.Response.StatusCode==400,"Malformed ID accepted");});
        Test("handler rejects forged identifier",delegate {var ctx=Context("document=999");new NORMYearSetupDocumentHandler().ProcessRequest(ctx);Check(ctx.Response.StatusCode==404,"Forged ID accepted");});
        Test("handler unauthorised stored entity is forbidden",delegate {AddOriginal(3,1,NORMStartOfYearSetup.PriorDocumentType,false);var ctx=Context("document=1");new NORMYearSetupDocumentHandler().ProcessRequest(ctx);Check(ctx.Response.StatusCode==403,"Cross entity download");});
        Test("handler unauthenticated request forbidden",delegate {var ctx=Context("document=1");ctx.User=null;new NORMYearSetupDocumentHandler().ProcessRequest(ctx);Check(ctx.Response.StatusCode==403,"Anonymous download");});
    }
    static HttpContext Context(string query) {var ctx=new HttpContext(new HttpRequest("","http://localhost/NORM/NORM_YearSetupDocument.ashx",query),new HttpResponse(new StringWriter()));ctx.User=new GenericPrincipal(new GenericIdentity("isolated-user","test"),new string[0]);HttpContext.Current=ctx;return ctx;}
    static void HistoricalTests()
    {
        Test("historical comparative/budget readers use recorded year",delegate {var prior=NORMStartOfYearSetup.LoadPriorActualFigures("DEPT",2025);Check(prior["SOCI|Example"]==25m&&NORMHelper.LastReadYear==2025,"Current substituted");var budget=NORMStartOfYearSetup.LoadOriginalBudgetFigures("DEPT",2025);Check(budget["SOCI|Example"]==25m,"Wrong budget year");NORMStartOfYearSetup.LoadPriorNoteFigures("DEPT",2025);Check(NORMHelper.LastReadYear==2025,"Wrong note year");NORMStartOfYearSetup.LoadPriorAssetMovementFigures("DEPT",2025);Check(NORMHelper.LastReadYear==2025,"Wrong asset year");});
        Test("all historical header consumers read recorded name/year",delegate {foreach(string file in new[]{"App_Code/NORMFinancialStatementsExcel.cs","App_Code/NORMWordExport.cs","NORM/NORM_Statements.aspx.cs","NORM/NORM_Reporting.aspx.cs"}) {string source=File.ReadAllText(Path.Combine(root,file));Check(source.Contains("r.ReportingEntityName")&&!source.Contains("ResolveCurrentFinancialYear(")&&!source.Contains("LEFT JOIN dbo.tblNORM_ReportingEntity e"),"Mutable context in "+file);}});
    }
    public static int Main(string[] args)
    {
        root=args[0];try{YearTests();NameTests();DocumentTests();PageTests();HistoricalTests();Console.WriteLine("TOTAL PASS "+passed);return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
    }
}
