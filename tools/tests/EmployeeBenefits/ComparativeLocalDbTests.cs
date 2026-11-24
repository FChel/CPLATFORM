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
        int cash = NORMStatementEngine.RunAnnualDraftReview(32, 33, 11, User, 34);
        int priorCash = NORMStatementEngine.ComparativeRunId(cash);
        CheckCash(cash, 32, 33, 1.499m, 1m, -1.5m, -2m, -0.501m, -2m, 13m, 10m, 1m, 1.500m, "1500.01");
        CheckCash(priorCash, 33, 34, 1.5m, 2m, 1.5m, 2m, 3.5m, 5m, 10m, 13m, 2m, 1m, "1000.00");
        Check(NORMHelper.CanAccessStatementRun(cash) && !NORMHelper.CanAccessStatementRun(priorCash) &&
            !NORMHelper.CanAccessCompletedRun(cash) && !NORMHelper.CanAccessCompletedRun(priorCash) &&
            NORMHelper.LatestAccessibleCompletedRun() == 28, "Cash-flow review escaped the controlled reporting gate.");
        Check(NORMStatementEngine.RunAnnualDraftReview(32, 33, 11, User, 34) == cash &&
            NORMStatementEngine.RunAnnualDraftReview(32, 33, 11, User) != cash,
            "Cash-flow fingerprints failed to distinguish existing comparative runs.");
        HttpContext.Current.User = new GenericPrincipal(new GenericIdentity("Unassigned fixture", "Fixture"), new string[0]);
        Check(!NORMHelper.CanAccessStatementRun(current) && !NORMHelper.CanAccessStatementRun(comparative),
            "Unassigned user could access comparative review.");
        Console.WriteLine("PASS LocalDB: production engine persisted both source years and their lineage under FY2026 release 11.");
        Console.WriteLine("PASS LocalDB: both periods retain mapping failures, comparative review is isolated, and approved run 28 remains available.");
        Console.WriteLine("PASS LocalDB: production cash-flow amounts, both source identities, exact and reported differences, and draft-only access.");
    }

    private static void CheckCash(int run, int import, int preceding, decimal exactReceipts, decimal receipts,
        decimal exactSuppliers, decimal suppliers, decimal exactNet, decimal net, decimal opening, decimal closing,
        decimal difference, decimal storedDifference, string exactDifferenceAud)
    {
        CashLine(run, "CF_OPA_REC_RAG", exactReceipts, receipts);
        CashLine(run, "CF_OPA_USE_SUP", exactSuppliers, suppliers);
        CashLine(run, "CF_INV", import == 32 ? 0m : 0.5m, import == 32 ? 0m : 1m);
        CashLine(run, "CF_FIN", import == 32 ? -0.5m : 0m, import == 32 ? -1m : 0m);
        CashLine(run, "CF_CSH_NET", exactNet, net);
        CashLine(run, "CF_CSH_OPEN", import == 32 ? 12.5m : 10m, opening);
        CashLine(run, "CF_CSH_CLOSE", import == 32 ? 10.499m : 12.5m, closing);
        CashLine(run, "CF_CSH_RECON", storedDifference, difference);
        const string edges = "SELECT t.GlAccount,t.ImportId,t.AccumBalance,l.SourceAmount,l.PresentedContribution," +
            "l.ConfigurationReleaseId,m.ConfigurationReleaseId AS MapRelease,l.CashFlowClassSnapshot " +
            "FROM dbo.tblNORM_Lineage l JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
            "JOIN dbo.tblNORM_TrialBalanceRow t ON t.TbRowId=l.TbRowId JOIN dbo.tblNORM_AccountMap m ON m.AccountMapId=l.AccountMapId " +
            "WHERE r.CalculationRunId=@run AND r.StatementCode='SCF' AND r.LineCode='CF_CSH_NET' AND l.DerivationCode='CASH_FLOW'";
        DataTable lineage = NORMHelper.Query(edges, NORMHelper.P("@run", run));
        decimal total = 0m;
        foreach (DataRow row in lineage.Rows)
        {
            int source = NORMHelper.Int(row, "ImportId");
            string gl = NORMHelper.Str(row, "GlAccount");
            decimal original = NORMHelper.Dec(row, "AccumBalance");
            decimal contribution = NORMHelper.Dec(row, "PresentedContribution");
            Check((source == import || source == preceding) && (gl != "59999" || source == import) &&
                gl != "58140" && gl != "70000" && gl != "99000" &&
                NORMHelper.Dec(row, "SourceAmount") == original && contribution == (source == import ? -original : original) / 1000m &&
                NORMHelper.Int(row, "ConfigurationReleaseId") == 11 && NORMHelper.Int(row, "MapRelease") == 11 &&
                !String.IsNullOrEmpty(NORMHelper.Str(row, "CashFlowClassSnapshot")), "Cash source lineage changed.");
            total += contribution;
        }
        Check(lineage.Rows.Count == 4 && total == exactNet, "Cash net does not reconcile to its retained source rows.");
        DataTable cashBalances = NORMHelper.Query(
            "SELECT r.LineCode,t.ImportId,l.SourceAmount FROM dbo.tblNORM_Lineage l " +
            "JOIN dbo.tblNORM_LineResult r ON r.LineResultId=l.LineResultId " +
            "JOIN dbo.tblNORM_TrialBalanceRow t ON t.TbRowId=l.TbRowId " +
            "WHERE r.CalculationRunId=@run AND r.StatementCode='SCF' AND r.LineCode IN ('CF_CSH_CLOSE','CF_CSH_OPEN') " +
            "AND t.GlAccount='70000' AND l.SourceAmount=t.AccumBalance ORDER BY r.LineCode", NORMHelper.P("@run", run));
        Check(cashBalances.Rows.Count == 2 &&
            NORMHelper.Int(cashBalances.Rows[0], "ImportId") == import &&
            NORMHelper.Dec(cashBalances.Rows[0], "SourceAmount") == (import == 32 ? 10498.99m : 12500m) &&
            NORMHelper.Int(cashBalances.Rows[1], "ImportId") == preceding &&
            NORMHelper.Dec(cashBalances.Rows[1], "SourceAmount") == (import == 32 ? 12500m : 10000m),
            "Opening or closing cash lost its source identity or original cents.");
        string detail = Convert.ToString(NORMHelper.Scalar("SELECT DetailText FROM dbo.tblNORM_ValidationResult WHERE " +
            "CalculationRunId=@run AND CheckCode='CASH_FLOW_RECONCILIATION' AND SeverityCode='Blocking' AND ResultCode='Fail' " +
            "AND ActualValue=@net AND ExpectedValue=@movement AND DifferenceValue=@difference",
            NORMHelper.P("@run", run), NORMHelper.P("@net", net), NORMHelper.P("@movement", closing - opening),
            NORMHelper.P("@difference", difference)));
        Check(detail.Contains(exactDifferenceAud), "The exact AUD difference or the reported blocking difference was lost.");
        string unresolved = Convert.ToString(NORMHelper.Scalar("SELECT DetailText FROM dbo.tblNORM_ValidationResult WHERE " +
            "CalculationRunId=@run AND CheckCode='CASH_FLOW_CLASSIFICATION' AND ActualValue=1 AND ResultCode='Fail'",
            NORMHelper.P("@run", run)));
        Check(unresolved.Contains("58140 (CF_OPA_USE_SUP1)") && !unresolved.Contains("70000"), "Unknown or optional keys changed.");
    }

    private static void CashLine(int run, string code, decimal computed, decimal reported)
    {
        DataTable rows = NORMHelper.Query("SELECT v.ComputedAmount,v.ReportedAmount FROM dbo.tblNORM_LineResult v " +
            "JOIN dbo.tblNORM_CalculationRun r ON r.CalculationRunId=v.CalculationRunId " +
            "WHERE r.StatusCode='Complete' AND r.ConfigurationReleaseId=11 AND r.CalculationRunId=@run " +
            "AND v.StatementCode='SCF' AND v.LineCode=@code", NORMHelper.P("@run", run), NORMHelper.P("@code", code));
        Check(rows.Rows.Count == 1 && NORMHelper.Dec(rows.Rows[0], "ComputedAmount") == computed &&
            NORMHelper.Dec(rows.Rows[0], "ReportedAmount") == reported, "Unexpected stored cash line: " + code);
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
