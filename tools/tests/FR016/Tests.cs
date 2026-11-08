using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using OfficeOpenXml;

public static class Tests
{
    static int passed;
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Test(string name, Action action) { NORMHelper.Reset(); action(); passed++; Console.WriteLine("PASS " + name); }
    static bool Contains(string sql) { return NORMHelper.Committed.Exists(delegate(NORMHelper.Call c) { return c.Sql.Contains(sql); }); }
    static string Audit(string code)
    {
        NORMHelper.Call c = NORMHelper.Committed.Find(delegate(NORMHelper.Call x) { return x.Sql.Contains("AuditEvent") && (string)x["@event"] == code; });
        return c == null ? "" : (string)c["@detail"];
    }
    static byte[] Workbook(decimal[] values, bool missing, decimal? openingOverride)
    {
        using (ExcelPackage p = new ExcelPackage())
        {
            var s = p.Workbook.Worksheets.Add("TB");
            s.Cells[1,1].Value="Fiscal Year"; s.Cells[1,2].Value=2026;
            s.Cells[2,1].Value="Posting Date From"; s.Cells[2,2].Value="01.07.2025";
            s.Cells[3,1].Value="Posting Date To"; s.Cells[3,2].Value="30.06.2026";
            s.Cells[4,1].Value="Company Code"; s.Cells[4,3].Value="G/L Account";
            s.Cells[4,8].Value="Ending Balance in Company Code Currency";
            for(int i=0;i<values.Length;i++)
            {
                int r=i+5; s.Cells[r,1].Value="1000"; s.Cells[r,3].Value="GL/"+(100+i);
                // Strings preserve the synthetic decimal exactly through XLSX.
                s.Cells[r,5].Value=(openingOverride.HasValue && i==0 ? openingOverride.Value : values[i]).ToString(CultureInfo.InvariantCulture);
                s.Cells[r,6].Value="0.00"; s.Cells[r,7].Value="0.00";
                if(!(missing && i==0)) s.Cells[r,8].Value=values[i].ToString(CultureInfo.InvariantCulture);
            }
            return p.GetAsByteArray();
        }
    }
    static byte[] Roman(string first, string second)
    {
        return Encoding.UTF8.GetBytes("Carryforward Periods 00-00 2026 Reporting Periods 01-12 2026\n"+
            "|1000 100 Cash AUD "+first+"|\n|1000 200 Equity AUD "+second+"|\n");
    }
#if SERVICE
    static Exception ImportFailure(byte[] bytes, string type)
    {
        try { NORMImportService.Import(bytes, "synthetic."+(type=="ERP"?"xlsx":"txt"), type, 1, "synthetic-user"); }
        catch(Exception e) { return e; }
        throw new Exception("Expected rejection");
    }
    static void NoReporting()
    {
        Assert(!Contains("INSERT dbo.tblNORM_Import ") && !Contains("INSERT dbo.tblNORM_ImportFile") && !Contains("INSERT dbo.tblNORM_TrialBalanceRow") && NORMHelper.Runs==0,"Rejected data reached reporting/calculation");
    }
    static void Balanced(decimal[] amounts)
    {
        var result=NORMImportService.Import(Workbook(amounts,false,null),"synthetic.xlsx","ERP",1,"synthetic-user");
        Assert(result.NetBalance==0m && result.RowCount==amounts.Length && result.CalculationRunId==24,"Outcome regression");
        Assert(Contains("INSERT dbo.tblNORM_Import ") && Contains("INSERT dbo.tblNORM_ImportFile") && Contains("INSERT dbo.tblNORM_TrialBalanceRow") && NORMHelper.Runs==1,"Successful path missing");
        Assert(Audit("IMPORT_BALANCE_VALIDATION").Contains("Result=Pass") && Audit("IMPORT_STORAGE_VALIDATION").Contains("Result=Pass"),"Successful evidence missing");
    }
    static void ServiceTests()
    {
        Test("ERP balanced signed ending balances",delegate { Balanced(new decimal[]{100m,-100m}); });
        Test("multiple debit and credit rows",delegate { Balanced(new decimal[]{10.01m,20.02m,-5.01m,-25.02m}); });
        Test("lossless trailing decimal zeroes",delegate { Balanced(new decimal[]{1.2300m,-1.23m}); });
        Test("zero-valued rows accepted",delegate { Balanced(new decimal[]{0m,0m}); });
        Test("maximum lossless SQL amount accepted",delegate { Balanced(new decimal[]{99999999999999999.99m,-99999999999999999.99m}); });
        foreach(decimal difference in new decimal[]{0.01m,-0.01m,0.99m,1m,-1m,0.001m,-0.001m})
        {
            decimal d=difference;
            Test("reject difference "+d,delegate {
                Exception e=ImportFailure(Workbook(new decimal[]{100m,-100m+d},false,null),"ERP");
                Assert(e is InvalidDataException && e.Message.Contains("Rejection evidence saved"),"Wrong rejection"); NoReporting();
                Assert(Audit("IMPORT_BALANCE_VALIDATION").Contains("Result=Fail") && Audit("IMPORT_BALANCE_VALIDATION").Contains("Difference="+d.ToString("0.00##########################",CultureInfo.InvariantCulture)),"Exact difference missing");
                Assert(Audit("IMPORT_REJECTED_SOURCE").Contains("SHA256="),"Source evidence missing");
            });
        }
        Test("balanced sub-cent amounts reject storage loss",delegate {
            ImportFailure(Workbook(new decimal[]{1.001m,-1.001m},false,null),"ERP"); NoReporting();
            Assert(Audit("IMPORT_BALANCE_VALIDATION").Contains("Result=Pass") && Audit("IMPORT_STORAGE_VALIDATION").Contains("Result=Fail"),"Checks conflated");
        });
        Test("opening precision checked despite balanced endings",delegate {
            ImportFailure(Workbook(new decimal[]{1m,-1m},false,1.001m),"ERP"); NoReporting();
            Assert(Audit("IMPORT_STORAGE_VALIDATION").Contains("Field=OpeningBalance"),"Opening not checked");
        });
        foreach(string field in new string[]{"DebitMovement","CreditMovement"})
        {
            string f=field;
            Test(f+" precision rejects before reporting",delegate {
                byte[] b=Workbook(new decimal[]{1m,-1m},false,null);
                using(var p=new ExcelPackage(new MemoryStream(b))) { p.Workbook.Worksheets[1].Cells[5,f=="DebitMovement"?6:7].Value="0.001"; b=p.GetAsByteArray(); }
                ImportFailure(b,"ERP"); NoReporting(); Assert(Audit("IMPORT_STORAGE_VALIDATION").Contains("Field="+f),"Movement precision unchecked");
            });
        }
        Test("minimum non-zero decimal difference rejected",delegate { ImportFailure(Workbook(new decimal[]{0.0000000000000000000000000001m},false,null),"ERP"); NoReporting(); Assert(Audit("IMPORT_BALANCE_VALIDATION").Contains("Difference=0.0000000000000000000000000001"),"Small difference concealed"); });
        Test("amount outside SQL range rejected",delegate { ImportFailure(Workbook(new decimal[]{100000000000000000m,-100000000000000000m},false,null),"ERP"); NoReporting(); Assert(Audit("IMPORT_STORAGE_VALIDATION").Contains("Result=Fail"),"Range unchecked"); });
        Test("totals outside SQL range rejected",delegate { ImportFailure(Workbook(new decimal[]{60000000000000000m,60000000000000000m,-60000000000000000m,-60000000000000000m},false,null),"ERP"); NoReporting(); Assert(Audit("IMPORT_STORAGE_VALIDATION").Contains("Result=Fail"),"Totals unchecked"); });
        Test("decimal aggregate overflow rejects with truthful evidence",delegate { var e=ImportFailure(Workbook(new decimal[]{Decimal.MaxValue,Decimal.MaxValue},false,null),"ERP"); NoReporting(); Assert(e.Message.Contains("evidence saved") && Audit("IMPORT_BALANCE_VALIDATION").Contains("Result=NotRun") && Audit("IMPORT_STORAGE_VALIDATION").Contains("Result=Fail"),"Overflow lost audit evidence"); });
        Test("missing ERP ending balance",delegate { var e=ImportFailure(Workbook(new decimal[]{1m,-1m},true,null),"ERP"); Assert(e.Message.Contains("invalid ending balance"),"Parser changed"); NoReporting(); });
        Test("malformed workbook",delegate { ImportFailure(new byte[]{1,2,3},"ERP"); NoReporting(); });
        Test("ROMAN trailing minus accepted",delegate { var o=NORMImportService.Import(Roman("100.00","100.00-"),"synthetic.txt","ROMAN",1,"synthetic-user"); Assert(o.NetBalance==0 && NORMHelper.Runs==1,"ROMAN convention changed"); });
        Test("ROMAN imbalance rejected",delegate { ImportFailure(Roman("100.01","100.00-"),"ROMAN"); NoReporting(); Assert(Audit("IMPORT_BALANCE_VALIDATION").Contains("Result=Fail"),"ROMAN gate missing"); });
        Test("ROMAN missing amount",delegate { var e=ImportFailure(Roman("missing","100.00-"),"ROMAN"); Assert(e.Message.Contains("does not contain an accumulated balance"),"ROMAN parser changed"); NoReporting(); });
        Test("accepted duplicate behaviour preserved",delegate { NORMHelper.Duplicate=true; var e=ImportFailure(Workbook(new decimal[]{1m,-1m},false,null),"ERP"); Assert(e.Message.Contains("already been imported as import 7"),"Duplicate changed"); NoReporting(); Assert(NORMHelper.Opens==0,"Duplicate wrote evidence"); });
        Test("repeated rejections have separate attempt identities",delegate {
            byte[] b=Workbook(new decimal[]{1m,-0.99m},false,null); ImportFailure(b,"ERP"); ImportFailure(b,"ERP"); NoReporting();
            var ids=new HashSet<string>(); foreach(var c in NORMHelper.Committed) if(c.Sql.Contains("AuditEvent") && (string)c["@event"]=="IMPORT_REJECTED") ids.Add((string)c["@id"]);
            Assert(ids.Count==2,"Rejected attempt blocked or overwritten");
        });
        Test("bounded Unicode filename audit is recoverable without chunks",delegate {
            byte[] bytes=Workbook(new decimal[]{1m,-0.99m},false,null);
            var encodedNames=new HashSet<string>();
            foreach(string filename in new string[]{"Trial balance FY2026.xlsx",new string('\u4e2d',240)+" ;x=100%3B.xlsx","a;b.xlsx","a%3Bb.xlsx",new string(';',260)})
            {
                NORMHelper.Reset();
                try { NORMImportService.Import(bytes,filename,"ERP",1,"synthetic-user"); throw new Exception("Expected rejection"); }
                catch(InvalidDataException e) { Assert(e.Message.Contains("Rejection evidence saved"),"Supported filename lost audit evidence"); }
                NoReporting(); int sourceEvents=0; string encoded=null;
                foreach(var c in NORMHelper.Committed) if(c.Sql.Contains("AuditEvent")) {
                    string detail=(string)c["@detail"]; Assert(detail.Length<=2000,"Audit truncation risk");
                    Assert((string)c["@event"]!="IMPORT_REJECTED_FILENAME","Unnecessary filename event");
                    if((string)c["@event"]=="IMPORT_REJECTED_SOURCE") {
                        sourceEvents++; string marker=";FileName=";
                        encoded=detail.Substring(detail.IndexOf(marker,StringComparison.Ordinal)+marker.Length);
                    }
                }
                Assert(sourceEvents==1 && encoded!=null && encodedNames.Add(encoded),"Filename event duplicated or names conflated");
                string decoded=encoded.Replace("%0D","\r").Replace("%0A","\n").Replace("%3B",";").Replace("%3D","=").Replace("%25","%");
                Assert(decoded==filename,"Filename evidence lost or ambiguous");
                if(filename.IndexOf('\u4e2d')>=0) Assert(encoded.IndexOf('\u4e2d')>=0 && encoded.Contains(" "),"Unicode or spaces expanded unnecessarily");
            }
            foreach(string unsupported in new string[]{new string('x',261),""})
            {
                NORMHelper.Reset();
                try { NORMImportService.Import(bytes,unsupported,"ERP",1,"synthetic-user"); throw new Exception("Expected rejection"); }
                catch(InvalidDataException e) { Assert(e.Message.Contains("could not be saved") && !e.Message.Contains("Rejection evidence saved"),"Unsupported filename silently truncated"); }
                NoReporting(); Assert(NORMHelper.Committed.Count==0,"Incomplete filename evidence committed");
            }
        });
        Test("rejection audit failure stays rejected with safe message",delegate { NORMHelper.FailAudit=true; var e=ImportFailure(Workbook(new decimal[]{1m,-0.99m},false,null),"ERP"); NoReporting(); Assert(NORMHelper.Committed.Count==0 && e.Message.Contains("could not be saved") && !e.Message.Contains("private connection detail") && !e.Message.Contains("evidence saved"),"False audit claim"); });
        Test("rejection commit failure stays rejected",delegate { NORMHelper.FailCommit=true; var e=ImportFailure(Workbook(new decimal[]{1m,-0.99m},false,null),"ERP"); NoReporting(); Assert(NORMHelper.Committed.Count==0 && e.Message.Contains("could not be saved"),"False commit claim"); });
        foreach(string code in new string[]{"IMPORT_BALANCE_VALIDATION","IMPORT_STORAGE_VALIDATION"})
        {
            string failCode=code;
            Test("successful "+failCode+" failure rolls back accepted writes",delegate { NORMHelper.FailAuditCode=failCode; var e=ImportFailure(Workbook(new decimal[]{1m,-1m},false,null),"ERP"); NoReporting(); Assert(NORMHelper.Committed.Count==0 && !e.Message.Contains("private connection detail") && e.Message.Contains("could not be saved"),"Partial acceptance or unsafe error"); });
        }
        Test("late rejection audit failure rolls back earlier evidence",delegate { NORMHelper.FailAuditCode="IMPORT_REJECTED_SOURCE"; var e=ImportFailure(Workbook(new decimal[]{1m,-0.99m},false,null),"ERP"); NoReporting(); Assert(NORMHelper.Committed.Count==0 && e.Message.Contains("could not be saved"),"Partial evidence committed"); });
        Test("successful transaction commit failure creates no calculation",delegate { NORMHelper.FailCommit=true; ImportFailure(Workbook(new decimal[]{1m,-1m},false,null),"ERP"); NoReporting(); Assert(NORMHelper.Committed.Count==0,"Partial acceptance"); });
    }
#else
    static void EngineTests()
    {
        foreach(decimal net in new decimal[]{0m,0.01m,-0.01m,0.99m,1m,-1m,0.001m,-0.001m,0.0000000000000000000000000001m})
        {
            decimal value=net;
            Test("StatementEngine exact total "+value,delegate {
                MethodInfo method=typeof(NORMStatementEngine).GetMethod("WriteValidations",BindingFlags.NonPublic|BindingFlags.Static);
                Type linesType=method.GetParameters()[15].ParameterType;
                using(var c=NORMHelper.OpenConnection()) using(var t=c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    method.Invoke(null,new object[]{c,t,1,2,value,200m,200m,0,0m,0m,0m,0m,0m,0m,0,Activator.CreateInstance(linesType),new Dictionary<string,decimal>()});
                    t.Commit();
                }
                var check=NORMHelper.Committed.Find(delegate(NORMHelper.Call x) {return x.Values.ContainsKey("@code") && (string)x["@code"]=="DEBITS_EQUAL_CREDITS";});
                Assert(check!=null && (string)check["@result"]==(value==0m?"Pass":"Fail") && (decimal)check["@tolerance"]==0m,"Incorrect TB validation");
                Assert(((string)check["@detail"]).Contains(value.ToString("0.00##########################",CultureInfo.InvariantCulture)),"Exact detail missing");
                var sofp=NORMHelper.Committed.Find(delegate(NORMHelper.Call x) {return x.Values.ContainsKey("@code") && (string)x["@code"]=="SOFP_BALANCES";});
                Assert((decimal)sofp["@tolerance"]==0.001m,"Unrelated validation changed");
            });
        }
    }
#endif
    public static int Main()
    {
        try {
#if SERVICE
            ServiceTests();
#else
            EngineTests();
#endif
            Console.WriteLine("TOTAL PASS "+passed); return 0;
        } catch(Exception e) { Console.WriteLine("FAIL "+e); return 1; }
    }
}
