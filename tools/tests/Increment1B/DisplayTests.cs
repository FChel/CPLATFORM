using System;
using System.Data;

public static class DisplayTests
{
    static void Assert(bool condition, string message) { if (!condition) { throw new Exception(message); } }
    public static int Main()
    {
        try
        {
            DataTable table = new DataTable();
            foreach (string field in new string[] { "SourceCompanyCode", "SourceLedger", "SourceType" }) { table.Columns.Add(field); }
            DataRow modern = table.Rows.Add("1000", "0L", "ERP");
            DataRow legacy = table.Rows.Add(DBNull.Value, "1000", "ERP");
            DataRow roman = table.Rows.Add(DBNull.Value, "1000", "ROMAN");
            Assert(NORMHelper.SourceCompany(modern) == "1000" && NORMHelper.SourceLedgerLabel(modern) == "0L", "Modern identities lost");
            Assert(NORMHelper.SourceCompany(legacy) == "1000" && NORMHelper.SourceLedgerLabel(legacy).Contains("unknown"), "Legacy identity invented");
            Assert(NORMHelper.SourceCompany(roman) == "" && NORMHelper.SourceLedgerLabel(roman).Contains("unknown"), "ROMAN identity invented");
            string url = NORMHelper.SapGlLineItemsLink("100000", NORMHelper.SourceCompany(modern), 2026);
            Assert(url.Contains("CompanyCode=1000") && !url.Contains("CompanyCode=0L"), "Wrong SAP company");
            Assert(NORMHelper.SapGlLineItemsLink("100000", NORMHelper.SourceCompany(roman), 2025) == "", "Unknown ROMAN company linked");
            Assert(legacy.IsNull("SourceCompanyCode") && (string)legacy["SourceLedger"] == "1000", "Historical row mutated");
            Console.WriteLine("PASS six real-helper retrieval/display/SAP/legacy assertions (in-memory rows, no connection)"); return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
