using System;
using System.Data;
using System.IO;
using System.Security.Principal;
using System.Web;

internal static class ComparativeLocalDbTests
{
    private const string User = "Comparative LocalDB fixture";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Main()
    {
        Check(Convert.ToInt32(NORMHelper.Scalar("SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'))")) == 1 &&
            Convert.ToString(NORMHelper.Scalar("SELECT DB_NAME()")).StartsWith("NORM_SeedTest_", StringComparison.Ordinal),
            "Comparative integration tests require the disposable LocalDB fixture.");
        HttpContext.Current = new HttpContext(new HttpRequest("", "http://localhost/NORM/", ""), new HttpResponse(TextWriter.Null));
        HttpContext.Current.User = new GenericPrincipal(new GenericIdentity(User, "Fixture"), new string[0]);
        int current = NORMStatementEngine.RunAnnualDraftReview(25, 24, 11, User);
        int comparative = NORMStatementEngine.ComparativeRunId(current);
        Check(current != comparative && comparative > 0, "The engine did not persist a comparative run link.");
        Check(NORMStatementEngine.RunAnnualDraftReview(25, 24, 11, User) == current,
            "The same retained sources and revision should reuse the persisted pair.");
        CheckPeriod(current, 25, 2026, 2.998m, 2m, "99926");
        CheckPeriod(comparative, 24, 2025, 1m, 1m, "99925");
        Check(!NORMHelper.CanAccessCompletedRun(current) && !NORMHelper.CanAccessCompletedRun(comparative) &&
            NORMHelper.CanAccessStatementRun(current) && !NORMHelper.CanAccessStatementRun(comparative),
            "One period bypassed the existing draft or controlled access rules.");
        Check(NORMHelper.CanAccessCompletedRun(28) && NORMHelper.LatestAccessibleCompletedRun() == 28 &&
            NORMStatementEngine.ComparativeRunId(28) == 0, "Existing approved run access or reproduction changed.");
        HttpContext.Current.User = new GenericPrincipal(new GenericIdentity("Unassigned fixture", "Fixture"), new string[0]);
        Check(!NORMHelper.CanAccessStatementRun(current) && !NORMHelper.CanAccessStatementRun(comparative),
            "Unassigned user could access comparative review.");
        Console.WriteLine("PASS LocalDB: production engine persisted both source years and their lineage under FY2026 release 11.");
        Console.WriteLine("PASS LocalDB: both periods retain mapping failures, comparative review is isolated, and approved run 28 remains available.");
    }

    private static void CheckPeriod(int run, int import, int year, decimal computed, decimal reported, string unmapped)
    {
        const string results = "SELECT i.ImportId,i.FinancialYear,r.ConfigurationReleaseId,c.FinancialYear AS ReleaseYear," +
            "v.ComputedAmount,v.ReportedAmount,r.ReportingEntityName " +
            "FROM dbo.tblNORM_CalculationRun r JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId " +
            "JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=r.ConfigurationReleaseId " +
            "JOIN dbo.tblNORM_LineResult v ON v.CalculationRunId=r.CalculationRunId " +
            "WHERE r.CalculationRunId=@run AND r.StatusCode='Complete' AND v.StatementCode='SOCI' AND v.LineCode='Employee benefits'";
        DataRow period = NORMHelper.Query(results, NORMHelper.P("@run", run)).Rows[0];
        Check(NORMHelper.Int(period, "ImportId") == import && NORMHelper.Int(period, "FinancialYear") == year &&
            NORMHelper.Int(period, "ConfigurationReleaseId") == 11 && NORMHelper.Int(period, "ReleaseYear") == 2026 &&
            NORMHelper.Str(period, "ReportingEntityName") == "NIHRACS FY2026" &&
            NORMHelper.Dec(period, "ComputedAmount") == computed && NORMHelper.Dec(period, "ReportedAmount") == reported,
            "The stored results do not match the source year, reporting-year release or Approach A totals.");
        const string lineage = "SELECT COUNT(*) FROM dbo.tblNORM_Lineage l JOIN dbo.tblNORM_TrialBalanceRow t ON t.TbRowId=l.TbRowId " +
            "LEFT JOIN dbo.tblNORM_AccountMap m ON m.AccountMapId=l.AccountMapId WHERE l.CalculationRunId=@run AND " +
            "(t.ImportId<>@import OR l.SourceAmount<>t.AccumBalance OR l.ConfigurationReleaseId<>11 OR " +
            "(l.DerivationCode<>'UNMAPPED' AND (m.AccountMapId IS NULL OR m.ConfigurationReleaseId<>11)))";
        Check(Convert.ToInt32(NORMHelper.Scalar(lineage, NORMHelper.P("@run", run), NORMHelper.P("@import", import))) == 0 &&
            Convert.ToInt32(NORMHelper.Scalar("SELECT COUNT(DISTINCT TbRowId) FROM dbo.tblNORM_Lineage WHERE CalculationRunId=@run",
                NORMHelper.P("@run", run))) == 3, "Lineage did not reconcile to the three original rows and FY2026 mappings.");
        string detail = Convert.ToString(NORMHelper.Scalar("SELECT DetailText FROM dbo.tblNORM_ValidationResult " +
            "WHERE CalculationRunId=@run AND CheckCode='UNMAPPED_DISPOSITION' AND SeverityCode='Blocking' AND ResultCode='Fail' AND ActualValue=1",
            NORMHelper.P("@run", run)));
        Check(detail.Contains(unmapped), "The correct unresolved account was not persisted for FY" + year + ".");
    }
}
