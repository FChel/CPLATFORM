using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Web;
using CPlatform.NORM;

public static partial class Tests
{
    static byte[] ImportDownload(int id)
    {
        StoredDownloadRequest request=new StoredDownloadRequest("import="+id+"&file="+id); HttpContext context=new HttpContext(request);
        context.User=new GenericPrincipal(new GenericIdentity("isolated-test","controlled"),new string[0]);
        new NORMSourceFileHandler().ProcessRequest(context); context.Response.Flush(); Assert(context.Response.StatusCode==200,"Run-independent original retrieval failed"); return request.Body.ToArray();
    }
    static void RunStep4(string fixtures)
    {
        int before=passed;
        HTest("A B C history uses B as C predecessor across releases and unrelated imports",delegate {
            Chain(); var versions=NORMSourceHistory.Versions(Selected(30));
            Assert(versions.Count==3 && versions[0].Scope.ImportId==30 && versions[0].PredecessorImportId==20 && versions[1].PredecessorImportId==10 && versions[2].PredecessorImportId==0,"Wrong accepted predecessor sequence");
            Assert(versions[0].IsCurrent && !versions[1].IsCurrent && versions[1].ReleaseId==3 && versions[0].ReleaseId==4,"Current/release provenance lost");
        });
        HTest("history of an older selected version compares B with A rather than current C",delegate {
            Chain(); var comparison=NORMSourceHistory.Compare(Selected(20),20); Assert(comparison.Current.Scope.ImportId==20 && comparison.Previous.Scope.ImportId==10,"Older version compared to wrong predecessor");
        });
        HTest("calculation status and missing run do not determine history",delegate {
            Chain(); store[30].Run=0; store[30].RunStatus=null; store[20].RunStatus="Failed";
            Assert(NORMSourceHistory.Compare(Selected(30),30).Previous.Scope.ImportId==20,"Failed/no-run accepted original dropped");
        });
        HTest("retired configuration release remains part of accepted history",delegate {
            Chain(); Find(3).Inactive=true; Assert(NORMSourceHistory.Versions(Selected(30))[1].Scope.ImportId==20,"Retired release broke source history");
        });
        HTest("retired release source remains viewable without silently choosing another completeness release",delegate {
            Chain(); Find(3).Inactive=true; NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=20");
            Assert(page.StatusForTest==200 && page.HistoryForTest.Contains("Import 20") && page.MissingForTest.Contains("Select an active approved") && page.ReturnForTest.Contains("release=0"),"History was blocked or latest completeness release silently substituted");
        });
        HTest("different company year ledger stream or agency cannot enter source chain",delegate {
            Chain(); Seed(31,2020,"1000",null,"Departmental"); store[31].Entity="OTHER";
            var versions=NORMSourceHistory.Versions(Selected(30)); Assert(versions.Count==3,"Foreign identity entered history");
            Assert(NORMSourceHistory.Versions(Selected(27)).Count==1 && NORMSourceHistory.Versions(Selected(28)).Count==1 && NORMSourceHistory.Versions(Selected(26)).Count==1,"Other identity history merged");
        });
        HTest("NULL and actual 0L have independent current populations and histories",delegate {
            Chain(); var scopes=NORMSourceHistory.CurrentPopulations("CONTROLLED",2020,"Departmental"); bool absent=false, actual=false;
            foreach(var scope in scopes) { if(scope.Company=="1000" && scope.Ledger==null) { absent=scope.ImportId==30; } if(scope.Company=="1000" && scope.Ledger=="0L") { actual=scope.ImportId==27; } }
            Assert(absent && actual,"NULL ledger substituted with 0L");
        });
        HTest("derived test inactive and unaccepted imports are excluded",delegate {
            Chain(); for(int i=32;i<=35;i++) { Seed(i,2020,"1000",null,"Departmental"); versionFlags[i]=new VersionFlags(); }
            versionFlags[32].Parent=30; versionFlags[33].TestBreak=true; versionFlags[34].Inactive=true; versionFlags[35].Status="Importing";
            Assert(NORMSourceHistory.Versions(Selected(30)).Count==3,"Non-authoritative import became predecessor");
            var scopes=NORMSourceHistory.CurrentPopulations("CONTROLLED",2020,"Departmental"); foreach(var scope in scopes) { if(scope.Company=="1000" && scope.Ledger==null) { Assert(scope.ImportId==30,"Derived/current selection wrong"); } }
        });
        HTest("added and removed zero accounts remain visible structurally",delegate {
            Chain(); var result=NORMSourceHistory.Compare(Selected(30),30); bool added=false, removed=false;
            foreach(var change in result.Structural) { added|=change.Account=="ADDED" && change.StructuralChange=="Added account"; removed|=change.Account=="REMOVED" && change.StructuralChange=="Removed account"; }
            Assert(added && removed && result.Financial.Count==2,"Zero structure was hidden or fabricated as financial movement");
        });
        HTest("description-only changes are separate from financial movement",delegate {
            Chain(); var result=NORMSourceHistory.Compare(Selected(30),30); bool found=false;
            foreach(var change in result.Structural) { if(change.Account=="DESC") { found=change.StructuralChange=="Description changed" && change.Previous.Balance==0m && change.Current.Balance==0m; } }
            foreach(var change in result.Financial) { Assert(change.Account!="DESC","Description-only change classified financially"); } Assert(found,"Description-only change absent");
        });
        HTest("signed financial movements and totals use source signs without classifications",delegate {
            Chain(); var result=NORMSourceHistory.Compare(Selected(30),30);
            Assert(result.Financial.Count==2 && result.Financial[0].Previous.Balance==100m && result.Financial[0].Current.Balance==125m && result.Financial[0].Difference==25m && result.Financial[1].Difference==-25m,"Wrong source movement signs");
            Assert(result.PreviousNet==0m && result.CurrentNet==0m && result.NetMovement==0m && result.PositiveMovements==25m && result.NegativeMovements==-25m,"Wrong financial movement totals");
        });
        HTest("profile release coverage and period evidence changes are reproducible",delegate {
            Chain(); store[30].Full=false; store[30].End=new DateTime(2020,3,31); var result=NORMSourceHistory.Compare(Selected(30),30);
            Assert(result.MetadataChanges.Exists(delegate(string s) { return s.Contains("Configuration release: 3 -> 4"); }) && result.MetadataChanges.Exists(delegate(string s) { return s.Contains("Source profile"); }) && result.MetadataChanges.Exists(delegate(string s) { return s.Contains("2020-03-31"); }),"Retained metadata changes absent");
        });
        HTest("first accepted original has no manufactured zero predecessor",delegate {
            Chain(); var result=NORMSourceHistory.Compare(Selected(10),10); Assert(result.Previous==null && result.Structural.Count==0 && result.Financial.Count==0,"First version compared with invented baseline");
        });
        HTest("historical duplicate accounts fail clearly rather than GL aggregation or last row wins",delegate {
            Chain(); accountEvidence[20].Rows.Add(999999L,99,"100","Duplicate",0m,"1000",DBNull.Value,"Departmental");
            RejectUnchanged(delegate { NORMSourceHistory.Compare(Selected(30),30); },"not unique within this population");
        });
        HTest("account rows in an older multi-population import are filtered to exact scope",delegate {
            Chain(); accountEvidence[20].Rows.Add(999998L,99,"100","Different population",900m,"2000",DBNull.Value,"Departmental");
            var scope=NORMSourceHistory.ScopeForImport(20,"1000",null,"Departmental",true); var result=NORMSourceHistory.Compare(scope,30);
            Assert(result.Financial[0].Previous.Balance==100m,"Other population's GL amount entered comparison");
            RejectUnchanged(delegate { NORMSourceHistory.ScopeForImport(20,null,null,null,false); },"several populations");
        });
        HTest("forged source version from a different population cannot be compared",delegate {
            Chain(); RejectUnchanged(delegate { NORMSourceHistory.Compare(Selected(30),25); },"not an accepted original of this population");
        });
        HTest("unknown historical identity is not assigned a guessed stream or ledger",delegate {
            store[10].Stream=null; RejectUnchanged(delegate { Selected(10); },"No classified accepted");
        });
        HTest("authorisation blocks history comparison and source selectors",delegate {
            Chain(); var scope=Selected(30); NORMHelper.EntityAccess=delegate { return false; }; historyReads=0;
            RejectUnchanged(delegate { NORMSourceHistory.Versions(scope); },"Entity access denied"); Assert(historyReads==0,"Denied history queried rows");
            RejectUnchanged(delegate { NORMSourceHistory.CurrentPopulations("CONTROLLED",2020,""); },"Entity access denied");
            RejectUnchanged(delegate { Selected(30); },"Entity access denied");
        });
        HTest("missing preparer access denies source history before storage",delegate {
            NORMHelper.PrepareAccess=false; historyReads=0; RejectUnchanged(delegate { Selected(10); },"Preparer access"); Assert(historyReads==0,"Unprepared actor reached metadata");
        });
        HTest("current and superseded originals are retrievable with no run records",delegate {
            Chain(); store[20].Run=store[30].Run=0; store[20].RunStatus=store[30].RunStatus=null; string beforeState=StoreFingerprint(false);
            Assert(NORMCrypto.Sha256(ImportDownload(20))==store[20].Hash && NORMCrypto.Sha256(ImportDownload(30))==store[30].Hash && StoreFingerprint(false)==beforeState,"No-run download changed originals/status/history");
        });
        HTest("genuine CSV remains exactly retrievable when calculation fails before any run exists",delegate {
            Find(1).Populations.Clear(); Find(1).Populations.Add(new[] { "150",null,"Departmental" }); NORMHelper.OnCalculate=delegate { throw new InvalidOperationException("Controlled failure before run creation"); };
            var p=NORMImportService.InspectFileForAgency(aag2020,"AAG_TrialBalanceFinStats150.csv",1,new int[0],"isolated-test"); var outcome=NORMImportService.Confirm(p[0],"isolated-test");
            Assert(outcome.CalculationRunId==0 && outcome.CalculationError.Contains("source was accepted") && store[outcome.ImportId].Run==0,"Did not exercise no-run accepted source");
            Assert(NORMCrypto.Sha256(ImportDownload(outcome.ImportId))=="9d3a113b3c0ef344c7d3c419f5c8d6e241c9543b2731f4f9d53098cfae5ad631","Genuine no-run original changed");
            Assert(NORMSourceHistory.Versions(Selected(outcome.ImportId))[0].Profile=="ACTEWAGL-FINSTATS/1","Native profile provenance lost");
        });
        HTest("mixed accepted batch keeps each population's own history and original",delegate {
            var p=MixedBatch(); var results=NORMImportService.ConfirmBatch(p,"isolated-test",true); string beforeState=StoreFingerprint(false);
            for(int i=0;i<results.Count;i++) {
                int id=results[i].ImportId; var versions=NORMSourceHistory.Versions(Selected(id));
                Assert(versions[0].Scope.ImportId==id && versions[0].Scope.Company==p[i].Population.Company && versions[0].Scope.ReportingStream==p[i].Population.ReportingStream,"Accepted batch histories merged");
                Assert(NORMCrypto.Sha256(ImportDownload(id))==p[i].FileHash,"Mixed original changed");
            }
            Assert(StoreFingerprint(false)==beforeState,"Read-only history/download modified source state");
        });
        HTest("page renders selected history structure finance metadata and no-run download links",delegate {
            Chain(); store[30].Run=0; store[30].FileName="CONTROLLED <source> & C.xlsx"; NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=30&release=4&viewStream=Departmental");
            Assert(page.StatusForTest==200 && page.ErrorForTest=="" && page.HistoryForTest.Contains("Import 20") && page.HistoryForTest.Contains("Superseded") && page.HistoryForTest.Contains("retained-v4") && page.HistoryForTest.Contains(store[30].Hash),"History missing metadata: "+page.ErrorForTest);
            Assert(page.HistoryForTest.Contains("import=30&amp;file=30") && !page.HistoryForTest.Contains("?run=") && page.HistoryForTest.Contains("&lt;source&gt; &amp; C.xlsx"),"Download navigation depends on run or lacks encoding");
            Assert(page.ComparisonForTest.Contains("Structural changes") && page.ComparisonForTest.Contains("Financial movements") && page.ComparisonForTest.Contains("ADDED") && page.ComparisonForTest.Contains("REMOVED") && page.ComparisonForTest.Contains("Before &lt;description&gt;") && page.ComparisonForTest.Contains("After &amp; description") && page.ComparisonForTest.Contains("source row"),"Comparison not visible/safely encoded");
        });
        HTest("agency year stream and completeness release survive historical navigation and return",delegate {
            Chain(); NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=20&release=4&viewStream=Departmental&company=1000&ledgerNull=1&stream=Departmental");
            Assert(page.ErrorForTest=="" && page.HistoryForTest.Contains("release=4&amp;viewStream=Departmental") && page.HistoryForTest.Contains("ledgerNull=1") && page.ReturnForTest=="NORM_Import.aspx?release=4&viewStream=Departmental" && page.ContextForTest.Contains("CONTROLLED / FY2020 / Departmental"),"Preparation context lost");
            Assert(new NORM_Import().PrepareContextForTest("release=4&viewStream=Departmental")=="4","Return did not restore approved release selection");
        });
        HTest("wrong company ledger or stream in history URL is rejected",delegate {
            Chain(); foreach(string selector in new[] { "company=2000&ledgerNull=1&stream=Departmental","company=1000&ledger=0L&stream=Departmental","company=1000&ledgerNull=1&stream=Administered" }) {
                NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=30&"+selector); Assert(page.StatusForTest==400 && page.HistoryForTest=="" && page.ErrorForTest.Contains("does not belong"),"Wrong scope selected");
            }
        });
        HTest("contradictory NULL and real ledger selectors are rejected",delegate {
            Chain(); NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=30&company=1000&ledgerNull=1&ledger=0L&stream=Departmental"); Assert(page.StatusForTest==400 && page.HistoryForTest=="","NULL/real ledger contradiction accepted");
        });
        HTest("different agency or year completeness context cannot silently replace source context",delegate {
            Chain(); NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=30&release=2"); Assert(page.StatusForTest==400 && page.HistoryForTest=="" && page.ErrorForTest.Contains("agency/year"),"Wrong context silently used");
        });
        HTest("missing populations are displayed separately without changing accepted status",delegate {
            Chain(); Find(4).Populations.Add(new[] { "7000",null,"Departmental" }); string beforeState=StoreFingerprint(false); NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=30&release=4&viewStream=Departmental");
            Assert(page.StatusForTest==200 && page.HistoryForTest.Contains("Current") && page.MissingForTest.Contains("7000") && page.MissingForTest.Contains("do not change accepted-source status") && !page.HistoryForTest.Contains("7000") && StoreFingerprint(false)==beforeState,"Completeness merged into source acceptance");
        });
        HTest("no source or context selection never defaults to latest approved release",delegate {
            Chain(); NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("release=&viewStream="); Assert(page.StatusForTest==200 && page.ContextForTest.Contains("No newest release") && !page.PopulationsForTest.Contains("current import"),"Implicit release/year default returned sources");
        });
        HTest("old retained unknown coverage and profile stay unknown",delegate {
            store[10].Start=store[10].End=null; store[10].Full=null; var version=NORMSourceHistory.Versions(Selected(10))[0];
            Assert(NORMSourceHistory.Coverage(version).Contains("Unknown") && version.Profile=="Unknown (legacy evidence)","Historical metadata was fabricated");
        });
        HTest("workspace uses source-history navigation instead of release GL mapped comparison",delegate {
            NORMHelper.QueryCallResponse=delegate { throw new Exception("Unexpected old comparison query"); }; NORMHelper.ScalarCallResponse=delegate { throw new Exception("Unexpected old comparison scalar"); };
            string html=new NORM_Workspace().SourceActivityForTest(); Assert(html.Contains("NORM_SourceHistory.aspx") && !html.Contains("P&amp;L") && !html.Contains("mapped movement"),"Old accounting comparison remains active");
        });
        HTest("unauthorised page renders no source history or original metadata",delegate {
            Chain(); NORMHelper.EntityAccess=delegate { return false; }; NORM_SourceHistoryPage page=new NORM_SourceHistoryPage(); page.RenderForTest("import=30");
            Assert(page.StatusForTest==403 && page.HistoryForTest=="" && page.PopulationsForTest=="" && !page.ErrorForTest.Contains(store[30].FileName),"Unauthorised metadata exposed");
        });
        Console.WriteLine("Package 1C Step 4: "+(passed-before)+" Passed / 0 Failed; combined Step 1-4: "+passed);
    }
}
