using System;
using System.IO;
using OfficeOpenXml;
public static class Fixtures
{
    static void Row(ExcelWorksheet s,int r,string company,string gl,string description,decimal amount)
    { s.Cells[r,1].Value=company;s.Cells[r,3].Value=gl;s.Cells[r,4].Value=description;s.Cells[r,5].Value=amount;s.Cells[r,6].Value=0m;s.Cells[r,7].Value=0m;s.Cells[r,8].Value=amount; }
    static void Legacy(string dir,string name,int year,string version,bool multi,bool invalid,string company)
    {
        using(ExcelPackage p=new ExcelPackage())
        {
            p.Workbook.Properties.Title="CONTROLLED UAT ONLY - Package 1C mechanics";
            ExcelWorksheet s=p.Workbook.Worksheets.Add("CONTROLLED mechanics only");
            s.Cells[1,1].Value="Fiscal Year";s.Cells[1,2].Value=year;
            s.Cells[2,1].Value="Posting Date From";s.Cells[2,2].Value="01.07."+(year-1);
            s.Cells[3,1].Value="Posting Date To";s.Cells[3,2].Value="30.06."+year;
            s.Cells[4,1].Value="Company Code";s.Cells[4,3].Value="G/L Account";s.Cells[4,8].Value="Ending Balance in Company Code Currency";
            decimal amount=company=="2000"?225m:year==2025?50m:version=="B"?125m:version=="C"?150m:100m;
            Row(s,5,company,"GL100","Controlled positive",amount);Row(s,6,company,"GL200","Controlled negative",-amount);
            if(company=="1000" && year==2026)
            {
                Row(s,7,company,"GL300",version=="A"?"Original description":"Revised description",0m);
                Row(s,8,company,version=="A"?"GL400":"GL500",version=="A"?"Zero removed in B":"Zero added in B",0m);
            }
            if(multi) { Row(s,9,"2000","GL100","Administered positive",200m);Row(s,10,"2000","GL200","Administered negative",invalid?-199.99m:-200m); }
            p.SaveAs(new FileInfo(Path.Combine(dir,name)));
        }
    }
    public static int Main(string[] args)
    {
        string dir=args[0];Directory.CreateDirectory(dir);
        Legacy(dir,"01_CONTROLLED_A_FY2026_multi-company.xlsx",2026,"A",true,false,"1000");
        Legacy(dir,"02_CONTROLLED_FY2025_company1000.xlsx",2025,"A",false,false,"1000");
        Legacy(dir,"05_CONTROLLED_B_FY2026_company1000.xlsx",2026,"B",false,false,"1000");
        Legacy(dir,"06_CONTROLLED_C_FY2026_company1000.xlsx",2026,"C",false,false,"1000");
        Legacy(dir,"07_CONTROLLED_INVALID_FY2026_multi-company.xlsx",2026,"A",true,true,"1000");
        File.Copy(Path.Combine(dir,"01_CONTROLLED_A_FY2026_multi-company.xlsx"),Path.Combine(dir,"08_CONTROLLED_DUPLICATE_A.xlsx"),false);
        Legacy(dir,"09_CONTROLLED_STALE_FY2026_company2000.xlsx",2026,"A",false,false,"2000");
        foreach(string path in Directory.GetFiles(dir,"*.xlsx"))
        {
            NORMParsedImport parsed=ErpTrialBalanceImporter.DiscoverForInspection(File.ReadAllBytes(path));
            decimal net=0m;foreach(NORMTrialBalanceRow row in parsed.Rows){net+=row.AccumBalance;}
            Console.WriteLine(Path.GetFileName(path)+" | FY"+parsed.FinancialYear+" | "+parsed.ProfileVersion+" | rows="+parsed.Rows.Count+" | net="+net+" | "+NORMCrypto.Sha256(File.ReadAllBytes(path)));
        }
        return 0;
    }
}
