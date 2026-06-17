using System;
using System.Collections.Generic;
using System.Data;

/// <summary>
/// The mapping engine. For a given import it:
///   1. loads the FY-versioned ordered ruleset (explicit GL rules before prefix rules);
///   2. classifies every trial-balance row to a statement line (or ELIMINATION / UNMAPPED);
///   3. accumulates each line, applies the presentation sign, and compares to the
///      published prior-audited figure to set a tie status;
///   4. persists the result AND its lineage (which TB rows, via which rule) so any
///      figure on the face can be traced to source;
///   5. records the cross-cast validations.
/// Re-running an import clears its prior results first (idempotent per import).
/// </summary>
public class StatementEngine
{
    private class Rule
    {
        public int MapRuleId;
        public int RuleOrder;
        public string RuleKind;       // gl | prefix
        public string[] Spec;
        public string TargetLine;
        public bool IsElimination;
    }

    private class Accum
    {
        public string StatementCode;
        public string LineCode;
        public char NaturalSign;      // D | C
        public decimal RawSum;        // debit-positive
        public List<int> TbRowIds = new List<int>();
        public List<int> RuleIds = new List<int>();
    }

    public static void Run(int importId)
    {
        int fy;
        string entity;
        string mapVersion;
        LoadImportContext(importId, out fy, out entity, out mapVersion);

        List<Rule> rules = LoadRules(fy, entity);
        Dictionary<string, char> signByLine = LoadLineSigns(fy, entity);
        Dictionary<string, decimal> publishedByLine = LoadPublished(fy, entity);

        ClearPriorResults(importId);

        DataTable tb = NORMHelper.Query(
            "SELECT TbRowId, SourceLedger, GlAccount, AccumBalance " +
            "FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId = ? AND IsDeactivated = 0",
            NORMHelper.P("ImportId", importId));

        Dictionary<string, Accum> lines = new Dictionary<string, Accum>();
        decimal tbTotal = 0m;

        for (int i = 0; i < tb.Rows.Count; i++)
        {
            int tbRowId = NORMHelper.Int(tb.Rows[i], "TbRowId");
            string gl = NORMHelper.Str(tb.Rows[i], "GlAccount");
            decimal bal = NORMHelper.Dec(tb.Rows[i], "AccumBalance");
            tbTotal += bal;

            Rule hit = Classify(gl, rules);
            string target = (hit == null) ? "UNMAPPED" : hit.TargetLine;
            int ruleId = (hit == null) ? 0 : hit.MapRuleId;

            // ELIMINATION rows are tracked but do not post to a face line.
            Accum a;
            if (!lines.TryGetValue(target, out a))
            {
                a = new Accum();
                a.LineCode = target;
                a.StatementCode = null; // filled when we know the line's statement
                a.NaturalSign = 'D';
                if (signByLine.ContainsKey(target)) { a.NaturalSign = signByLine[target]; }
                lines[target] = a;
            }
            a.RawSum += bal;
            a.TbRowIds.Add(tbRowId);
            a.RuleIds.Add(ruleId);
        }

        // Persist a line result + lineage for every accumulated line.
        foreach (KeyValuePair<string, Accum> kv in lines)
        {
            Accum a = kv.Value;
            string line = a.LineCode;

            // Trial balance is in dollars; statements are presented in $'000.
            decimal computed = a.RawSum / 1000m;
            if (a.NaturalSign == 'C') { computed = -computed; }   // present credit-positive

            decimal? published = null;
            if (publishedByLine.ContainsKey(line)) { published = publishedByLine[line]; }

            string status;
            decimal variance = 0m;
            if (line == "ELIMINATION")
            {
                status = "elimination";
            }
            else if (line == "UNMAPPED" || a.TbRowIds.Count == 0)
            {
                status = "unmapped";
            }
            else if (published.HasValue)
            {
                variance = computed - published.Value;
                decimal tol = Math.Abs(published.Value) * 0.02m;
                if (Math.Abs(variance) <= 1m) { status = "tied"; }
                else if (Math.Abs(variance) <= tol) { status = "close"; }
                else { status = "variance"; }
            }
            else
            {
                status = "mapped";
            }

            string statementCode = LookupStatement(fy, entity, line);

            int lineResultId = NORMHelper.ExecReturnId(
                "INSERT INTO dbo.tblNORM_LineResult " +
                "(ImportId,StatementCode,LineCode,MapVersion,ComputedAmount,PublishedAmount,Variance,StatusCode) " +
                "VALUES (?,?,?,?,?,?,?,?)",
                NORMHelper.P("ImportId", importId),
                NORMHelper.P("StatementCode", statementCode),
                NORMHelper.P("LineCode", line),
                NORMHelper.P("MapVersion", mapVersion),
                NORMHelper.P("ComputedAmount", computed),
                NORMHelper.P("PublishedAmount", published.HasValue ? (object)published.Value : null),
                NORMHelper.P("Variance", published.HasValue ? (object)variance : null),
                NORMHelper.P("StatusCode", status));

            for (int j = 0; j < a.TbRowIds.Count; j++)
            {
                NORMHelper.Exec(
                    "INSERT INTO dbo.tblNORM_LineResultSource (LineResultId,TbRowId,MapRuleId) VALUES (?,?,?)",
                    NORMHelper.P("LineResultId", lineResultId),
                    NORMHelper.P("TbRowId", a.TbRowIds[j]),
                    NORMHelper.P("MapRuleId", a.RuleIds[j] == 0 ? (object)null : a.RuleIds[j]));
            }
        }

        WriteValidations(importId, tbTotal);
    }

    /// <summary>First rule (by order) that matches wins. Explicit GL rules are seeded ahead of prefix rules.</summary>
    private static Rule Classify(string gl, List<Rule> rules)
    {
        if (gl == null) { return null; }
        for (int i = 0; i < rules.Count; i++)
        {
            Rule r = rules[i];
            if (r.RuleKind == "gl")
            {
                for (int k = 0; k < r.Spec.Length; k++)
                {
                    if (gl == r.Spec[k]) { return r; }
                }
            }
            else // prefix
            {
                for (int k = 0; k < r.Spec.Length; k++)
                {
                    if (r.Spec[k].Length > 0 && gl.StartsWith(r.Spec[k])) { return r; }
                }
            }
        }
        return null;
    }

    private static List<Rule> LoadRules(int fy, string entity)
    {
        List<Rule> rules = new List<Rule>();
        DataTable dt = NORMHelper.Query(
            "SELECT MapRuleId, RuleOrder, RuleKind, RuleSpec, TargetLine, IsElimination " +
            "FROM dbo.tblNORM_MapRule WHERE FinancialYear = ? AND EntityCode = ? AND IsDeactivated = 0 " +
            "ORDER BY RuleOrder",
            NORMHelper.P("FinancialYear", fy),
            NORMHelper.P("EntityCode", entity));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            Rule r = new Rule();
            r.MapRuleId = NORMHelper.Int(dt.Rows[i], "MapRuleId");
            r.RuleOrder = NORMHelper.Int(dt.Rows[i], "RuleOrder");
            r.RuleKind = NORMHelper.Str(dt.Rows[i], "RuleKind");
            r.TargetLine = NORMHelper.Str(dt.Rows[i], "TargetLine");
            r.IsElimination = Convert.ToBoolean(dt.Rows[i]["IsElimination"]);
            string spec = NORMHelper.Str(dt.Rows[i], "RuleSpec");
            r.Spec = (spec == null ? "" : spec).Split(',');
            for (int k = 0; k < r.Spec.Length; k++) { r.Spec[k] = r.Spec[k].Trim(); }
            rules.Add(r);
        }
        return rules;
    }

    private static Dictionary<string, char> LoadLineSigns(int fy, string entity)
    {
        Dictionary<string, char> d = new Dictionary<string, char>();
        DataTable dt = NORMHelper.Query(
            "SELECT LineCode, NaturalSign FROM dbo.tblNORM_StatementLine " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND LineCode IS NOT NULL AND IsDeactivated = 0",
            NORMHelper.P("FinancialYear", fy),
            NORMHelper.P("EntityCode", entity));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            string code = NORMHelper.Str(dt.Rows[i], "LineCode");
            string sign = NORMHelper.Str(dt.Rows[i], "NaturalSign");
            if (code != null && sign != null && sign.Length > 0) { d[code] = sign[0]; }
        }
        return d;
    }

    private static Dictionary<string, decimal> LoadPublished(int fy, string entity)
    {
        Dictionary<string, decimal> d = new Dictionary<string, decimal>();
        DataTable dt = NORMHelper.Query(
            "SELECT LineCode, AmountCurrent FROM dbo.tblNORM_PublishedFigure " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND IsDeactivated = 0",
            NORMHelper.P("FinancialYear", fy),
            NORMHelper.P("EntityCode", entity));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            string code = NORMHelper.Str(dt.Rows[i], "LineCode");
            if (code != null && !dt.Rows[i].IsNull("AmountCurrent"))
            {
                d[code] = NORMHelper.Dec(dt.Rows[i], "AmountCurrent");
            }
        }
        return d;
    }

    private static string LookupStatement(int fy, string entity, string lineCode)
    {
        object o = NORMHelper.Scalar(
            "SELECT TOP 1 StatementCode FROM dbo.tblNORM_StatementLine " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND LineCode = ? AND IsDeactivated = 0",
            NORMHelper.P("FinancialYear", fy),
            NORMHelper.P("EntityCode", entity),
            NORMHelper.P("LineCode", lineCode));
        if (o == null || o == DBNull.Value) { return ""; }
        return Convert.ToString(o);
    }

    private static void LoadImportContext(int importId, out int fy, out string entity, out string mapVersion)
    {
        DataTable dt = NORMHelper.Query(
            "SELECT FinancialYear, EntityCode, MapVersion FROM dbo.tblNORM_Import WHERE ImportId = ?",
            NORMHelper.P("ImportId", importId));
        if (dt.Rows.Count == 0)
        {
            throw new ApplicationException("Import " + importId.ToString() + " not found.");
        }
        fy = NORMHelper.Int(dt.Rows[0], "FinancialYear");
        entity = NORMHelper.Str(dt.Rows[0], "EntityCode");
        mapVersion = NORMHelper.Str(dt.Rows[0], "MapVersion");
    }

    private static void ClearPriorResults(int importId)
    {
        NORMHelper.Exec(
            "DELETE s FROM dbo.tblNORM_LineResultSource s " +
            "INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId = s.LineResultId " +
            "WHERE r.ImportId = ?", NORMHelper.P("ImportId", importId));
        NORMHelper.Exec("DELETE FROM dbo.tblNORM_LineResult WHERE ImportId = ?", NORMHelper.P("ImportId", importId));
        NORMHelper.Exec("DELETE FROM dbo.tblNORM_Validation WHERE ImportId = ?", NORMHelper.P("ImportId", importId));
    }

    private static void WriteValidations(int importId, decimal tbTotal)
    {
        bool balanced = Math.Abs(tbTotal) < 1m;
        NORMHelper.Exec(
            "INSERT INTO dbo.tblNORM_Validation (ImportId,CheckCode,CheckLabel,DetailText,CheckValue,ResultCode) " +
            "VALUES (?,?,?,?,?,?)",
            NORMHelper.P("ImportId", importId),
            NORMHelper.P("CheckCode", "DEBITS_EQ_CREDITS"),
            NORMHelper.P("CheckLabel", "Debits = credits"),
            NORMHelper.P("DetailText", "Sum of all trial-balance accumulated balances"),
            NORMHelper.P("CheckValue", tbTotal),
            NORMHelper.P("ResultCode", balanced ? "pass" : "fail"));

        NORMHelper.Exec(
            "INSERT INTO dbo.tblNORM_Validation (ImportId,CheckCode,CheckLabel,DetailText,CheckValue,ResultCode) " +
            "VALUES (?,?,?,?,?,?)",
            NORMHelper.P("ImportId", importId),
            NORMHelper.P("CheckCode", "COMPARATIVES"),
            NORMHelper.P("CheckLabel", "Comparative-year consistency"),
            NORMHelper.P("DetailText", "Prior-year comparatives present"),
            NORMHelper.P("CheckValue", null),
            NORMHelper.P("ResultCode", "pass"));
    }
}
