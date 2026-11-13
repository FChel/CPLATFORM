using System;
using System.Collections.Generic;
using System.Data;
using OfficeOpenXml;
using CPlatform.NORM;

public static partial class Tests
{
    static void T(string name, Action action) { Test("Step3 " + name, delegate { StoreSetup(); action(); }); }
    static List<NORMHelper.Call> ImportCalls()
    { return NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Sql.Contains("INSERT dbo.tblNORM_Import "); }); }
    static void RolledBack() { Assert(acceptanceCommits==0 && NORMHelper.Rollbacks==1 && NORMHelper.Runs==0,"Batch not fully rolled back"); }
    static byte[] Defence2020(string company,string ledger,string stream)
    {
        return Modify(ControlledDefence(company,ledger,stream),delegate(ExcelWorksheet s) {
            for(int row=1;row<26;row++) {
                if(s.Cells[row,1].Text=="Funds of Flow Ledger Fiscal Year") { s.Cells[row,2].Value=2020; }
                if(s.Cells[row,1].Text=="Posting Date From") { s.Cells[row,2].Value="01.07.2019"; }
                if(s.Cells[row,1].Text=="Posting Date To") { s.Cells[row,2].Value="30.06.2020"; }
            }
        });
    }
    static void RunStep3(string fixtures)
    {
        int before=passed;
        RunRejectionAudit();
        T("mixed file/year/release/new/replacement populations commit once with own context",delegate {
            string originals=StoreFingerprint(true); List<NORMImportPreview> p=MixedBatch();
            List<NORMImportOutcome> results=NORMImportService.ConfirmBatch(p,"isolated-test",true);
            Assert(results.Count==3 && acceptanceCommits==1 && NORMHelper.Commits==1 && NORMHelper.Runs==3,"Not one complete acceptance commit");
            for(int i=0;i<p.Count;i++) {
                StoredPopulation stored=store[results[i].ImportId];
                Assert(stored.Release==p[i].ReleaseId && stored.Year==p[i].FinancialYear && stored.Company==p[i].Population.Company && stored.Ledger==p[i].Population.Ledger && stored.Stream==p[i].Population.ReportingStream,"First member context inherited");
                Assert(stored.Rows.Count==2 && stored.Start==p[i].CoverageStart && stored.End==p[i].CoverageEnd && stored.Full==p[i].IsFullYear && stored.Hash==p[i].FileHash,"Population rows/coverage/original lost");
                Assert(Current(stored.Identity)==stored.Id,"Accepted population is not current");
            }
            Assert(StoreFingerprint(true)==originals && Current(Key("CONTROLLED",2020,"9900","0L","Administered"))==11,"Unrelated/history sources changed");
            Assert(acceptedAudits.FindAll(delegate(NORMHelper.Call c) { return (string)c["@event"]=="IMPORT_SOURCE_REPLACED"; }).Count==2,"Missing predecessor links");
        });
        T("shared original is retained unchanged for every population and downloadable",delegate {
            byte[] bytes=ControlledLegacy(true,true); List<NORMImportPreview> p=FilePopulations(bytes); string history=StoreFingerprint(true);
            var results=NORMImportService.ConfirmBatch(p,"isolated-test",true);
            foreach(var result in results) { Assert(NORMCrypto.Sha256(store[result.ImportId].Bytes)==NORMCrypto.Sha256(bytes) && NORMCrypto.Sha256(Download(result.ImportId))==NORMCrypto.Sha256(bytes),"Original split/altered or retrieval failed"); }
            Assert(NORMCrypto.Sha256(Download(10))==store[10].Hash && downloads==3 && StoreFingerprint(true)==history,"Predecessor original/history lost");
        });
        T("upload size counts shared file once while existing-compatible storage retains both originals",delegate {
            byte[] bytes=ControlledLegacy(true,true); NORMHelper.MaxUploadBytes=bytes.Length;
            var results=NORMImportService.ConfirmBatch(FilePopulations(bytes),"isolated-test",true);
            Assert(results.Count==2 && store[results[0].ImportId].Bytes.Length+store[results[1].ImportId].Bytes.Length==2*bytes.Length,"Shared file charged twice or storage not preserved");
        });
        T("Departmental Administered NULL 0L and additional ledger retain separate scopes",delegate {
            Find(1).Populations.Add(new[] { "1000","0L","Departmental" }); Find(1).Populations.Add(new[] { "2000","2L","Administered" });
            Seed(13,2020,"1000","0L","Departmental"); string originals=StoreFingerprint(true);
            List<NORMImportPreview> p=FilePopulations(ControlledLegacy(true,true)); p.AddRange(FilePopulations(Defence2020("1000","0L","Departmental"))); p.AddRange(FilePopulations(Defence2020("2000","2L","Administered")));
            var results=NORMImportService.ConfirmBatch(p,"isolated-test",true);
            Assert(results.Count==4 && Current(Key("CONTROLLED",2020,"1000",null,"Departmental"))!=Current(Key("CONTROLLED",2020,"1000","0L","Departmental")),"NULL and 0L collapsed");
            Assert(store[results[2].ImportId].Rows.Count==984 && store[results[3].ImportId].Ledger=="2L" && StoreFingerprint(true)==originals,"Independent native scope/history lost");
        });
        T("new approved release replaces same source identity across releases",delegate {
            Add(3,2020,"1000",null,"Departmental"); Find(3).Populations.Add(new[] { "2000",null,"Administered" }); string history=StoreFingerprint(true);
            List<NORMImportPreview> p=NORMImportService.InspectFileForAgency(ControlledLegacy(true,true),"CONTROLLED-new-release.xlsx",3,new int[0],"isolated-test");
            Assert(p[0].HasReplacement,"Release incorrectly became source identity"); var results=NORMImportService.ConfirmBatch(p,"isolated-test",true);
            Assert(store[results[0].ImportId].Release==3 && Current(Key("CONTROLLED",2020,"1000",null,"Departmental"))==results[0].ImportId && StoreFingerprint(true)==history,"Release transition lost scoped predecessor");
        });
        T("current-year partial replacement supersedes coverage without period append",delegate {
            string history=StoreFingerprint(true); byte[] partial=Modify(ControlledLegacy(true,true),delegate(ExcelWorksheet s) { s.Cells[3,2].Value="31.03.2020"; });
            var results=NORMImportService.ConfirmBatch(FilePopulations(partial),"isolated-test",true);
            foreach(var result in results) { Assert(store[result.ImportId].Full==false && store[result.ImportId].End==new DateTime(2020,3,31) && store[result.ImportId].Rows.Count==2,"Coverage appended/relabelled"); }
            Assert(store[10].Full==true && StoreFingerprint(true)==history,"Previous accepted full coverage changed");
        });
        T("missing required population and unmapped accounts do not block valid acceptance",delegate {
            Find(1).Populations.Add(new[] { "7000",null,"Departmental" }); var p=FilePopulations(ControlledLegacy(false,true));
            Assert(p[0].UnmappedAccounts==2 && p[0].CanAccept,"Unmapped rows blocked source"); NORMImportService.ConfirmBatch(p,"isolated-test",true);
            Assert(NORMImportService.MissingRequiredPopulations(1).Count==2,"Missing required sources incorrectly accepted/blocked");
        });
        T("missing replacement acknowledgement consumes no members and permits explicit retry",delegate {
            var p=MixedBatch(); RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",false); },"replacement confirmation");
            foreach(var member in p) { Assert(!member.Consumed,"Unchecked acknowledgement partially consumed batch"); }
            Assert(NORMHelper.Opens==0,"Unchecked acknowledgement opened storage"); Assert(NORMImportService.ConfirmBatch(p,"isolated-test",true).Count==3,"Acknowledgement retry failed");
        });
        T("successful confirmation cannot be replayed",delegate {
            var p=MixedBatch(); NORMImportService.ConfirmBatch(p,"isolated-test",true);
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"already consumed");
        });
        T("changed original bytes reject entire batch",delegate {
            var p=MixedBatch(); p[0].Content[0]=0; RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"bytes changed");
        });
        T("changed population amounts cannot hide behind unchanged RowHash",delegate {
            var p=MixedBatch(); p[0].PopulationRows[0].AccumBalance+=1m;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"retained source identity");
        });
        T("changed five-part identity rejects",delegate {
            var p=MixedBatch(); p[1].Population.Ledger="0L";
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"retained source identity");
        });
        T("changed member year or release rejects rather than inheriting first context",delegate {
            var p=MixedBatch(); p[2].ReleaseId=1; p[2].FinancialYear=2020;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"retained source identity");
        });
        T("changed record exclusions cannot forge complete row reconciliation",delegate {
            var p=MixedBatch(); p[0].SourceFile.MetadataRowCount++; p[0].SourceFile.RecordCount++;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"retained source identity");
        });
        T("source bytes changed after reinspection are rechecked inside transaction",delegate {
            var p=MixedBatch(); transactionQueryOverride=delegate(NORMHelper.Call call) {
                if(call.Sql.Contains("FROM dbo.tblNORM_ConfigurationRelease")) { p[0].Content[0]=0; } return null;
            };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"bytes changed"); RolledBack();
        });
        T("retained rows changed after reinspection are rejected inside transaction",delegate {
            var p=MixedBatch(); transactionQueryOverride=delegate(NORMHelper.Call call) {
                if(call.Sql.Contains("FROM dbo.tblNORM_ConfigurationRelease")) { p[0].PopulationRows[0].GlText="changed inside transaction"; } return null;
            };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"retained source identity"); RolledBack();
        });
        T("subset confirmation reparses whole original and rejects omitted sibling",delegate {
            var p=MixedBatch(); reinspectionReads=0;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(new[] { p[0] },"isolated-test",true); },"complete original file");
            Assert(reinspectionReads==1 && acceptanceCommits==0,"Whole-file reinspection did not run before subset rejection");
        });
        T("valid member cannot bypass invalid sibling by submitting extracted subset",delegate {
            byte[] bytes=Modify(ControlledLegacy(true,true),delegate(ExcelWorksheet s) { s.Cells[7,5].Value="100.005"; s.Cells[7,8].Value="100.005"; s.Cells[8,5].Value="-100.005"; s.Cells[8,8].Value="-100.005"; });
            var p=FilePopulations(bytes); Assert(p[0].CanAccept && !p[1].CanAccept,"Fixture did not isolate invalid sibling"); reinspectionReads=0;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(new[] { p[0] },"isolated-test",true); },"complete original file"); Assert(reinspectionReads==1,"Complete original not revalidated");
        });
        T("forged file population count cannot authorise extracted subset",delegate {
            var p=MixedBatch(); p[0].SourceFile.PopulationCount=1;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(new[] { p[0] },"isolated-test",true); },"retained source identity");
        });
        T("substituted population from another inspection does not complete an original file",delegate {
            var a=FilePopulations(ControlledLegacy(true,true)); var b=FilePopulations(ControlledLegacy(true,true));
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(new[] { a[0], b[1] },"isolated-test",true); },"complete original file");
        });
        T("omitting an entire staged file rejects the altered batch membership",delegate {
            var p=MixedBatch(); NORMImportService.ValidateBatch(p);
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(new[] { p[0],p[1] },"isolated-test",true); },"staged batch population set");
        });
        T("same identity in different releases remains a duplicate batch",delegate {
            Add(3,2020,"1000",null,"Departmental"); var p=FilePopulations(ControlledLegacy(false,true));
            p.AddRange(NORMImportService.InspectFileForAgency(ControlledLegacy(false,true),"CONTROLLED-duplicate-release.xlsx",3,new int[0],"isolated-test"));
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"same agency, financial year");
        });
        T("expired non-first preview rejects without writes",delegate {
            var p=MixedBatch(); p[2].ExpiresUtc=DateTime.UtcNow.AddSeconds(-1);
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"expired");
        });
        T("withdrawn non-first release rejects during reinspection",delegate {
            var p=MixedBatch(); Find(2).Status="Draft";
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"approved configuration"); Assert(acceptanceCommits==0,"Invalid release reached acceptance commit");
        });
        T("changed non-first release population configuration rolls back locked transaction",delegate {
            var p=MixedBatch(); transactionQueryOverride=delegate(NORMHelper.Call call) {
                if(call.Sql.Contains("FROM dbo.tblNORM_ConfigurationRelease") && (int)call["@release"]==2) { Find(2).Populations[0][2]="Administered"; } return null;
            };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Configuration changed before acceptance");
            Assert(releaseLocks.Count==2 && releaseLocks[0]==1 && releaseLocks[1]==2,"Non-first release not locked/rechecked in deterministic order"); RolledBack();
        });
        T("changed Year Setup before confirmation invalidates even full-year source",delegate {
            var p=MixedBatch(); NORMHelper.CurrentYear=2021;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"changed"); Assert(acceptanceCommits==0,"Stale year accepted");
        });
        T("changed Year Setup inside transaction rejects and rolls back",delegate {
            var p=MixedBatch(); NORMHelper.ReadYearCall=delegate(NORMHelper.Call call) { return call.InTransaction ? (int?)2021 : 2020; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Configuration changed before acceptance"); RolledBack();
        });
        T("missing designated year inside transaction cannot fall back to release",delegate {
            var p=MixedBatch(); NORMHelper.ReadYearCall=delegate(NORMHelper.Call call) { return call.InTransaction ? null : (int?)2020; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"explicit current financial year"); RolledBack();
        });
        T("changed non-first predecessor rejects all locked sources",delegate {
            var p=MixedBatch(); NORMHelper.CurrentSource=delegate(NORMHelper.Call call) { int id=StoreCurrent(call); return call.InTransaction && (string)call["@company"]=="3000" ? id+1 : id; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"accepted source changed"); RolledBack();
        });
        T("real stale sequence preserves sources accepted by another preview",delegate {
            var stale=MixedBatch(); var current=MixedBatch(); NORMImportService.ConfirmBatch(current,"isolated-test",true);
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(stale,"isolated-test",true); },"accepted source changed");
        });
        T("second original-file persistence failure rolls back previous population",delegate {
            var p=MixedBatch(); int files=0; NORMHelper.OnExecute=delegate(NORMHelper.Call call) { if(call.Sql.Contains("INSERT dbo.tblNORM_ImportFile") && ++files==2) { throw new InvalidOperationException("Injected second file failure"); } };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"second file failure"); Assert(files==2,"Did not reach later file"); RolledBack();
        });
        T("later source-row failure rolls back complete batch",delegate {
            var p=MixedBatch(); int rows=0; NORMHelper.OnExecute=delegate(NORMHelper.Call call) { if(call.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow") && ++rows==3) { throw new InvalidOperationException("Injected later row failure"); } };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"later row failure"); RolledBack();
        });
        T("later replacement audit failure rolls back new and replacement sources",delegate {
            var p=MixedBatch(); int audits=0; NORMHelper.OnExecute=delegate(NORMHelper.Call call) { if(call.Values.ContainsKey("@event") && (string)call["@event"]=="IMPORT_SOURCE_REPLACED" && ++audits==2) { throw new InvalidOperationException("Injected later replacement audit failure"); } };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"replacement audit failure"); Assert(audits==2,"Did not reach later audit"); RolledBack();
        });
        T("later validation audit failure rolls back originals rows and acceptance audits",delegate {
            var p=MixedBatch(); int audits=0; NORMHelper.OnExecute=delegate(NORMHelper.Call call) { if(call.Values.ContainsKey("@event") && (string)call["@event"]=="IMPORT_STORAGE_VALIDATION" && ++audits==2) { throw new InvalidOperationException("Injected later validation audit failure"); } };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"validation evidence could not be saved"); RolledBack();
        });
        T("transaction commit failure preserves all accepted data",delegate {
            var p=MixedBatch(); NORMHelper.FailCommit=true;
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Rejection audit evidence could not be saved");
            Assert(acceptanceCommits==0 && NORMHelper.Rollbacks==2 && NORMHelper.Runs==0 && NORMHelper.Committed.Count==0,"Failed source/audit commits changed accepted data or evidence");
        });
        T("revoked entity authority before confirmation rejects",delegate {
            var p=MixedBatch(); NORMHelper.EntityAccess=delegate { return false; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Entity access denied");
        });
        T("revoked entity authority inside transaction rejects",delegate {
            var p=MixedBatch(); NORMHelper.TransactionEntityAccess=delegate { return false; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Entity access denied"); RolledBack();
        });
        T("batch ownership and actual authenticated actor must both match",delegate {
            var p=MixedBatch(); p[1].Owner="another-actor";
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"invalid");
            var fresh=MixedBatch(); RejectUnchanged(delegate { NORMImportService.ConfirmBatch(fresh,"another-actor",true); },"authenticated user");
        });
        T("multipart page confirms whole mixed batch only after separate replacement acknowledgement",delegate {
            var prior=Modify(ControlledLegacy(false,true),delegate(ExcelWorksheet s) { s.Cells[1,2].Value=2019; s.Cells[2,2].Value="01.07.2018"; s.Cells[3,2].Value="30.06.2019"; s.Cells[5,1].Value="3000"; s.Cells[6,1].Value="3000"; });
            NORM_Import page=new NORM_Import(); page.InspectUploadsForTest(1,new int[0],new[] { ControlledLegacy(true,true),prior },new[] { "CONTROLLED & mixed.xlsx","CONTROLLED prior.xlsx" });
            Assert(page.ErrorForTest=="" && page.PreviewsForTest.Count==3 && page.ConfirmationEnabledForTest && page.ReplacementControlsForTest,"Mixed page failed inspection: "+page.ErrorForTest);
            Assert(page.PreviewForTest.Contains("New source population") && page.PreviewForTest.Contains("Replacement of accepted population") && page.PreviewForTest.Contains("&amp; mixed.xlsx") && page.PreviewForTest.Contains("release 2"),"Population status/context not visible or encoded");
            page.ClearUploadedFilesForTest(); string initial=StoreFingerprint(false); page.ConfirmForTest();
            Assert(page.PendingForTest && page.ReplacementErrorForTest.Contains("tick the checkbox") && StoreFingerprint(false)==initial,"Unchecked acknowledgement changed state/preview");
            page.CheckReplacementForTest(); page.ConfirmForTest();
            Assert(page.ErrorForTest=="" && page.ResultForTest.Contains("Source accepted") && page.ResultForTest.Contains("&amp; mixed.xlsx") && acceptanceCommits==1 && NORMHelper.Runs==3,"Mixed page confirmation failed: "+page.ErrorForTest);
        });
        T("calculation failure after commit retains all accepted and downloadable originals",delegate {
            var p=MixedBatch(); NORMHelper.FailCalculation=true; string history=StoreFingerprint(true);
            var results=NORMImportService.ConfirmBatch(p,"isolated-test",true);
            foreach(var result in results) {
                Assert(result.CalculationRunId==0 && result.CalculationError.Contains("source was accepted") && store[result.ImportId].RunStatus=="Failed","Calculation failure relabelled acceptance");
                Assert(NORMCrypto.Sha256(Download(result.ImportId))==store[result.ImportId].Hash,"Failed calculation blocked original");
            }
            Assert(acceptanceCommits==1 && StoreFingerprint(true)==history && downloads==3,"Calculation failure lost batch/history");
        });
        T("release and scope locking order is stable under reversed preview order",delegate {
            var p=MixedBatch(); NORMImportService.ValidateBatch(p); p.Reverse(); NORMImportService.ConfirmBatch(p,"isolated-test",true);
            string releasesBefore=String.Join(",",releaseLocks), scopesBefore=String.Join(",",scopeLocks);
            releaseLocks.Clear(); scopeLocks.Clear(); var second=MixedBatch(); NORMImportService.ConfirmBatch(second,"isolated-test",true);
            Assert(releasesBefore=="1,2" && String.Join(",",releaseLocks)==releasesBefore && String.Join(",",scopeLocks)==scopesBefore,"Lock acquisition depends on submission order");
        });
        T("source acceptance audits identify shared original reference and partition counts",delegate {
            var p=MixedBatch(); NORMImportService.ConfirmBatch(p,"isolated-test",true);
            var audits=acceptedAudits.FindAll(delegate(NORMHelper.Call call) { return (string)call["@event"]=="IMPORT_CREATED"; });
            Assert(audits.Count==3,"Missing source acceptance audit"); int shared=0;
            foreach(var audit in audits) { string detail=(string)audit["@detail"]; Assert(detail.Contains("batch=") && detail.Contains("populationAccepted=2;populationInvalid=0") && detail.Contains("release="),"Audit lacks population/batch context"); if(detail.Contains("fileReference="+p[0].SourceFile.Token)) { shared++; Assert(detail.Contains("filePopulations=2;fileRecords=8;fileDetail=4;metadata=4;controls=0;blank=0"),"File exclusions not retained"); } }
            Assert(shared==2,"Shared-original populations lost correlation");
        });
        Console.WriteLine("Package 1C Step 3: "+(passed-before)+" Passed / 0 Failed; combined Step 1+2+3: "+passed);
    }

    static List<NORMHelper.Call> Rejections()
    { return NORMHelper.Committed.FindAll(delegate(NORMHelper.Call c) { return c.Values.ContainsKey("@event") && (string)c["@event"]=="IMPORT_REJECTED_SOURCE"; }); }
    static string AuditDetail(NORMHelper.Call call)
    { return Uri.UnescapeDataString(Uri.UnescapeDataString((string)call["@detail"])); }
    static void AssertIndependentAudit()
    {
        string events=String.Join(",",NORMHelper.TransactionEvents);
        Assert(events.Contains("Rollback:1,Dispose:1,Begin:2,Commit:2,Dispose:2"),"Failure audit did not independently commit after source rollback/disposal: "+events);
    }
    static void RunRejectionAudit()
    {
        T("audit Legacy stale batch correlates populations without duplicate file events",delegate {
            var stale=FilePopulations(ControlledLegacy(true,true)); NORMImportService.ValidateBatch(stale);
            Seed(40,2020,"2000",null,"Administered");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(stale,"isolated-test",true); },"accepted source changed");
            var audits=Rejections(); Assert(audits.Count==1,"Multi-population original duplicated rejection events");
            var audit=audits[0]; string detail=AuditDetail(audit); Guid id;
            Assert(Guid.TryParse((string)audit["@id"],out id) && (string)audit["@type"]=="ImportAttempt" && (string)audit["@user"]=="isolated-test","Wrong actor/attempt");
            foreach(string required in new[]{"Phase=Acceptance;Outcome=Rejected","SOURCE_PREDECESSOR_CHANGED","ExpectedImport=0","ObservedImport=40","Batch=","ScopeCount=2","Company=1000","Company=2000","Ledger=(NULL)","Profile=LEGACY-ERP/1","SHA256="+NORMCrypto.Sha256(stale[0].Content),"Coverage=2019-07-01/2020-06-30"})
            { Assert(detail.Contains(required),"Missing audit detail: "+required+" in "+detail); }
            AssertIndependentAudit();
        });
        T("audit multi-file later-member conflict shares one attempt with own contexts",delegate {
            var stale=MixedBatch(); Seed(40,2019,"3000",null,"Departmental");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(stale,"isolated-test",true); },"accepted source changed");
            var audits=Rejections(); Assert(audits.Count==2 && (string)audits[0]["@id"]==(string)audits[1]["@id"],"Originals lost shared attempt or duplicate events");
            Assert(AuditDetail(audits[0]).Contains("FY=2020") && AuditDetail(audits[1]).Contains("FY=2019") && AuditDetail(audits[1]).Contains("Release=2"),"Member context inherited"); AssertIndependentAudit();
        });
        T("audit Defence governed stale confirmation",delegate {
            Find(1).Populations.Add(new[]{"1000","0L","Departmental"});
            var stale=FilePopulations(Defence2020("1000","0L","Departmental")); Seed(40,2020,"1000","0L","Departmental");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(stale,"isolated-test",true); },"accepted source changed");
            Assert(Rejections().Count==1 && AuditDetail(Rejections()[0]).Contains("DEFENCE-ERP/1"),"Defence rejection missing"); AssertIndependentAudit();
        });
        foreach(bool icon in new[]{false,true})
        {
            bool fixedWidth=icon;
            T("audit "+(icon?"Icon Water":"ActewAGL CSV")+" stale confirmation",delegate {
                int year=fixedWidth?2016:2020; string company=fixedWidth?"86":"150";
                Add(4,year,company,null,"Departmental"); NORMHelper.CurrentYear=year;
                byte[] bytes=fixedWidth?System.IO.File.ReadAllBytes(System.IO.Path.Combine(root,"docs/Trial Balance Examples/coy 86.txt")):aag2020;
                var stale=NORMImportService.InspectFileForAgency(bytes,"original.native",4,new int[0],"isolated-test"); Seed(40,year,company,null,"Departmental");
                RejectUnchanged(delegate { NORMImportService.ConfirmBatch(stale,"isolated-test",true); },"accepted source changed");
                Assert(Rejections().Count==1 && AuditDetail(Rejections()[0]).Contains(fixedWidth?"ICON-WATER-FIXED/1":"ACTEWAGL-FINSTATS/1"),"Native rejection missing"); AssertIndependentAudit();
            });
        }
        T("audit late row failure rolls back sources then persists rejection",delegate {
            var p=MixedBatch(); int rows=0;
            NORMHelper.OnExecute=delegate(NORMHelper.Call c) { if(c.Sql.Contains("INSERT dbo.tblNORM_TrialBalanceRow") && ++rows==3) { throw new InvalidOperationException("Controlled late row rejection"); } };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"late row rejection");
            Assert(rows==3 && Rejections().Count==2 && ImportCalls().Count==0,"Late failure lost audit or accepted partial rows"); AssertIndependentAudit();
        });
        T("audit failure rolls back all independent rejection events",delegate {
            var p=MixedBatch(); Seed(40,2019,"3000",null,"Departmental"); int audits=0;
            NORMHelper.OnExecute=delegate(NORMHelper.Call c) { if(c.Values.ContainsKey("@event") && (string)c["@event"]=="IMPORT_REJECTED_SOURCE" && ++audits==2) { throw new Exception("private audit detail"); } };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Rejection audit evidence could not be saved");
            Assert(audits==2 && Rejections().Count==0 && NORMHelper.Rollbacks==2 && acceptanceCommits==0,"Partial audit/source state committed");
        });
        T("audit expiry during locked confirmation remains routine",delegate {
            var p=MixedBatch(); transactionQueryOverride=delegate(NORMHelper.Call c) { p[0].ExpiresUtc=DateTime.UtcNow.AddSeconds(-1); return null; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"expired");
            Assert(Rejections().Count==0 && NORMHelper.Transactions==1,"Routine expiry emitted audit");
        });
        T("audit true conflict remains recorded if expiry occurs during rollback",delegate {
            var p=MixedBatch(); Seed(40,2019,"3000",null,"Departmental");
            NORMHelper.CurrentSource=delegate(NORMHelper.Call c) { int current=StoreCurrent(c); if(c.InTransaction && (string)c["@company"]=="3000") { p[0].ExpiresUtc=DateTime.UtcNow.AddSeconds(-1); } return current; };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"accepted source changed");
            Assert(Rejections().Count==2,"Expiry suppressed actual conflict");
        });
        T("audit expired entry unchecked acknowledgement and cancellation remain quiet",delegate {
            var p=MixedBatch(); RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",false); },"replacement confirmation");
            p[0].ExpiresUtc=DateTime.UtcNow.AddSeconds(-1); RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"expired");
            NORM_Import page=new NORM_Import(); var fresh=FilePopulations(ControlledLegacy(true,true)); page.StageForTest(fresh[0],true,fresh[1]); page.CancelForTest();
            Assert(Rejections().Count==0 && NORMHelper.Transactions==0,"Routine outcome audited");
        });
        T("audit long escaped filename preserves reason scope and hash within schema limit",delegate {
            string name=new string('\u00e9',250)+";=%.xlsx";
            var p=NORMImportService.InspectFileForAgency(ControlledLegacy(true,true),name,1,new int[0],"isolated-test");
            Seed(40,2020,"2000",null,"Administered");
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"accepted source changed");
            string raw=(string)Rejections()[0]["@detail"],detail=AuditDetail(Rejections()[0]);
            Assert(raw.Length<=2000 && raw.Contains("[truncated]") && detail.Contains("Company=1000") && detail.Contains("Company=2000") && detail.Contains("SOURCE_PREDECESSOR_CHANGED") && detail.Contains("SHA256="+p[0].FileHash),"Bounded metadata lost required evidence");
        });
        T("audit independent commit failure retains no partial evidence or sources",delegate {
            var p=MixedBatch(); Seed(40,2019,"3000",null,"Departmental");
            NORMHelper.OnCommit=delegate(System.Collections.Generic.IList<NORMHelper.Call> calls) { throw new Exception("private commit detail"); };
            RejectUnchanged(delegate { NORMImportService.ConfirmBatch(p,"isolated-test",true); },"Rejection audit evidence could not be saved");
            Assert(NORMHelper.Committed.Count==0 && NORMHelper.Rollbacks==2 && acceptanceCommits==0,"Failed audit commit changed source/evidence");
        });
        T("audit successful acceptance calculation failure and replacement unchanged",delegate {
            NORMHelper.FailCalculation=true; NORMImportService.ConfirmBatch(MixedBatch(),"isolated-test",true);
            Assert(acceptanceCommits==1 && Rejections().Count==0 && acceptedAudits.Exists(delegate(NORMHelper.Call c){return (string)c["@event"]=="IMPORT_SOURCE_REPLACED";}),"Committed source misreported as rejected");
        });
    }
}
