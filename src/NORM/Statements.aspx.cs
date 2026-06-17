using System;
using System.Collections.Generic;
using System.Data;
using System.Web.UI;
using System.Web.Script.Serialization;

public partial class Statements : Page
{
    /// <summary>Emitted into the page as window.NORM_DATA.</summary>
    protected string NormDataJson = "{}";

    private const int FY = 2024;
    private const string ENTITY = "DEPT";

    protected void Page_Load(object sender, EventArgs e)
    {
        int importId = ResolveImportId();
        if (importId <= 0)
        {
            NormDataJson = "{}";
            return;
        }

        Dictionary<string, object> payload = BuildPayload(importId);
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = 16 * 1024 * 1024;
        NormDataJson = js.Serialize(payload);
    }

    /// <summary>?import=N, else the most recent import for FY2024 Departmental.</summary>
    private int ResolveImportId()
    {
        string q = Request.QueryString["import"];
        int qid;
        if (!String.IsNullOrEmpty(q) && Int32.TryParse(q, out qid)) { return qid; }

        object o = NORMHelper.Scalar(
            "SELECT TOP 1 ImportId FROM dbo.tblNORM_Import " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND IsDeactivated = 0 " +
            "ORDER BY ImportId DESC",
            NORMHelper.P("FinancialYear", FY),
            NORMHelper.P("EntityCode", ENTITY));
        if (o == null || o == DBNull.Value) { return 0; }
        return Convert.ToInt32(o);
    }

    private Dictionary<string, object> BuildPayload(int importId)
    {
        DataRow imp = NORMHelper.Query(
            "SELECT FinancialYear, EntityCode, SourceFileName, MapVersion FROM dbo.tblNORM_Import WHERE ImportId = ?",
            NORMHelper.P("ImportId", importId)).Rows[0];

        Dictionary<string, DataRow> results = LoadResults(importId);
        Dictionary<string, decimal[]> published = LoadPublished();   // [current, prior]
        Dictionary<string, string> ruleByLine = LoadRuleDescriptions();
        Dictionary<string, List<Dictionary<string, object>>> lineage = LoadLineage(importId);

        Dictionary<string, object> payload = new Dictionary<string, object>();

        // meta
        decimal totalAbs = ScalarDec("SELECT SUM(ABS(AccumBalance)) FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId = ?", importId);
        decimal exclAbs = ScalarDec(
            "SELECT SUM(ABS(tb.AccumBalance)) FROM dbo.tblNORM_TrialBalanceRow tb " +
            "INNER JOIN dbo.tblNORM_LineResultSource s ON s.TbRowId = tb.TbRowId " +
            "INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId = s.LineResultId " +
            "WHERE r.ImportId = ? AND r.LineCode IN ('UNMAPPED','ELIMINATION')", importId);
        int accounts = (int)ScalarDec("SELECT COUNT(*) FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId = ?", importId);
        decimal coverage = (totalAbs == 0m) ? 0m : Math.Round(100m * (totalAbs - exclAbs) / totalAbs, 1);

        Dictionary<string, object> meta = new Dictionary<string, object>();
        meta["entity"] = "Department of Defence \u2014 Departmental";
        meta["fy"] = "2023\u201324";
        meta["basis"] = "ROMAN ledgers 1000 + 4100 (combined)";
        meta["source"] = NORMHelper.Str(imp, "SourceFileName");
        meta["mapVersion"] = NORMHelper.Str(imp, "MapVersion");
        meta["coverage"] = coverage;
        meta["accounts"] = accounts;
        payload["meta"] = meta;

        // validations
        payload["validations"] = LoadValidations(importId);

        // statements
        payload["soci"] = BuildStatement("SOCI", results, published, ruleByLine, lineage);
        payload["sofp"] = BuildStatement("SOFP", results, published, ruleByLine, lineage);

        // pools
        payload["unmapped"] = BuildPool("UNMAPPED", results, lineage);
        payload["elimination"] = BuildPool("ELIMINATION", results, lineage);

        return payload;
    }

    private List<object> BuildStatement(string statementCode,
        Dictionary<string, DataRow> results, Dictionary<string, decimal[]> published,
        Dictionary<string, string> ruleByLine, Dictionary<string, List<Dictionary<string, object>>> lineage)
    {
        List<object> rows = new List<object>();
        DataTable tmpl = NORMHelper.Query(
            "SELECT SeqNo, LineType, LineCode, LineLabel, NoteRef, NaturalSign FROM dbo.tblNORM_StatementLine " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND StatementCode = ? AND IsDeactivated = 0 ORDER BY SeqNo",
            NORMHelper.P("FinancialYear", FY),
            NORMHelper.P("EntityCode", ENTITY),
            NORMHelper.P("StatementCode", statementCode));

        for (int i = 0; i < tmpl.Rows.Count; i++)
        {
            DataRow t = tmpl.Rows[i];
            string lineType = NORMHelper.Str(t, "LineType");
            if (lineType == "section")
            {
                Dictionary<string, object> sec = new Dictionary<string, object>();
                sec["type"] = "section";
                sec["label"] = NORMHelper.Str(t, "LineLabel");
                rows.Add(sec);
                continue;
            }

            string code = NORMHelper.Str(t, "LineCode");
            Dictionary<string, object> row = new Dictionary<string, object>();
            row["type"] = lineType;
            row["label"] = NORMHelper.Str(t, "LineLabel");
            row["note"] = NORMHelper.Str(t, "NoteRef");
            row["code"] = code;
            row["sign"] = NORMHelper.Str(t, "NaturalSign");

            long computed = 0;
            string status = "unmapped";
            long diff = 0;
            if (code != null && results.ContainsKey(code))
            {
                DataRow rr = results[code];
                computed = (long)Math.Round(NORMHelper.Dec(rr, "ComputedAmount"));
                status = NORMHelper.Str(rr, "StatusCode");
                if (!rr.IsNull("Variance")) { diff = (long)Math.Round(NORMHelper.Dec(rr, "Variance")); }
            }
            row["computed"] = computed;
            row["status"] = status;
            row["diff"] = diff;

            long pub24 = 0; long pub23 = 0;
            if (code != null && published.ContainsKey(code))
            {
                pub24 = (long)Math.Round(published[code][0]);
                pub23 = (long)Math.Round(published[code][1]);
            }
            row["pub24"] = pub24;
            row["pub23"] = pub23;

            row["rule"] = (code != null && ruleByLine.ContainsKey(code)) ? ruleByLine[code] : null;

            List<Dictionary<string, object>> rl =
                (code != null && lineage.ContainsKey(code)) ? lineage[code] : new List<Dictionary<string, object>>();
            row["rows"] = rl;
            row["n"] = rl.Count;
            rows.Add(row);
        }
        return rows;
    }

    private Dictionary<string, object> BuildPool(string code,
        Dictionary<string, DataRow> results, Dictionary<string, List<Dictionary<string, object>>> lineage)
    {
        Dictionary<string, object> pool = new Dictionary<string, object>();
        long net = 0;
        if (results.ContainsKey(code)) { net = (long)Math.Round(NORMHelper.Dec(results[code], "ComputedAmount")); }
        List<Dictionary<string, object>> rl = lineage.ContainsKey(code) ? lineage[code] : new List<Dictionary<string, object>>();
        pool["n"] = rl.Count;
        pool["net"] = net;
        pool["rows"] = rl;
        return pool;
    }

    // ---- loaders ----
    private Dictionary<string, DataRow> LoadResults(int importId)
    {
        Dictionary<string, DataRow> d = new Dictionary<string, DataRow>();
        DataTable dt = NORMHelper.Query(
            "SELECT LineCode, ComputedAmount, PublishedAmount, Variance, StatusCode " +
            "FROM dbo.tblNORM_LineResult WHERE ImportId = ? AND IsDeactivated = 0",
            NORMHelper.P("ImportId", importId));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            d[NORMHelper.Str(dt.Rows[i], "LineCode")] = dt.Rows[i];
        }
        return d;
    }

    private Dictionary<string, decimal[]> LoadPublished()
    {
        Dictionary<string, decimal[]> d = new Dictionary<string, decimal[]>();
        DataTable dt = NORMHelper.Query(
            "SELECT LineCode, AmountCurrent, AmountPrior FROM dbo.tblNORM_PublishedFigure " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND IsDeactivated = 0",
            NORMHelper.P("FinancialYear", FY),
            NORMHelper.P("EntityCode", ENTITY));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            string code = NORMHelper.Str(dt.Rows[i], "LineCode");
            decimal cur = NORMHelper.Dec(dt.Rows[i], "AmountCurrent");
            decimal pri = NORMHelper.Dec(dt.Rows[i], "AmountPrior");
            d[code] = new decimal[] { cur, pri };
        }
        return d;
    }

    private Dictionary<string, string> LoadRuleDescriptions()
    {
        // Lowest RuleOrder rule per target line gives the headline rule shown in the trace drawer.
        Dictionary<string, string> d = new Dictionary<string, string>();
        DataTable dt = NORMHelper.Query(
            "SELECT TargetLine, RuleDescription FROM dbo.tblNORM_MapRule " +
            "WHERE FinancialYear = ? AND EntityCode = ? AND IsDeactivated = 0 ORDER BY RuleOrder",
            NORMHelper.P("FinancialYear", FY),
            NORMHelper.P("EntityCode", ENTITY));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            string target = NORMHelper.Str(dt.Rows[i], "TargetLine");
            if (!d.ContainsKey(target)) { d[target] = NORMHelper.Str(dt.Rows[i], "RuleDescription"); }
        }
        return d;
    }

    private Dictionary<string, List<Dictionary<string, object>>> LoadLineage(int importId)
    {
        Dictionary<string, List<Dictionary<string, object>>> d = new Dictionary<string, List<Dictionary<string, object>>>();
        DataTable dt = NORMHelper.Query(
            "SELECT r.LineCode, tb.SourceLedger, tb.GlAccount, tb.GlText, tb.AccumBalance " +
            "FROM dbo.tblNORM_LineResultSource s " +
            "INNER JOIN dbo.tblNORM_LineResult r ON r.LineResultId = s.LineResultId " +
            "INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.TbRowId = s.TbRowId " +
            "WHERE r.ImportId = ? AND s.IsDeactivated = 0 " +
            "ORDER BY r.LineCode, ABS(tb.AccumBalance) DESC",
            NORMHelper.P("ImportId", importId));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            string code = NORMHelper.Str(dt.Rows[i], "LineCode");
            if (!d.ContainsKey(code)) { d[code] = new List<Dictionary<string, object>>(); }
            Dictionary<string, object> row = new Dictionary<string, object>();
            row["cc"] = NORMHelper.Str(dt.Rows[i], "SourceLedger");
            row["gl"] = NORMHelper.Str(dt.Rows[i], "GlAccount");
            row["text"] = NORMHelper.Str(dt.Rows[i], "GlText");
            row["amt"] = (long)Math.Round(NORMHelper.Dec(dt.Rows[i], "AccumBalance") / 1000m);
            d[code].Add(row);
        }
        return d;
    }

    private List<object> LoadValidations(int importId)
    {
        List<object> v = new List<object>();
        DataTable dt = NORMHelper.Query(
            "SELECT CheckLabel, DetailText, CheckValue, ResultCode FROM dbo.tblNORM_Validation " +
            "WHERE ImportId = ? AND IsDeactivated = 0 ORDER BY ValidationId",
            NORMHelper.P("ImportId", importId));
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["k"] = NORMHelper.Str(dt.Rows[i], "CheckLabel");
            item["detail"] = NORMHelper.Str(dt.Rows[i], "DetailText");
            if (!dt.Rows[i].IsNull("CheckValue"))
            {
                item["val"] = (long)Math.Round(NORMHelper.Dec(dt.Rows[i], "CheckValue"));
            }
            string rc = NORMHelper.Str(dt.Rows[i], "ResultCode");
            if (rc == "pass") { item["pass"] = true; }
            else if (rc == "fail") { item["pass"] = false; }
            else { item["pending"] = true; }
            v.Add(item);
        }
        // Two informational checks the seed mapping cannot yet evidence.
        v.Add(MakePending("Statement of financial position balances", "Assets \u2212 liabilities \u2212 equity (mapped)"));
        v.Add(MakePending("Note totals reconcile to face", "Awaiting note-level mapping"));
        return v;
    }

    private Dictionary<string, object> MakePending(string k, string detail)
    {
        Dictionary<string, object> item = new Dictionary<string, object>();
        item["k"] = k;
        item["detail"] = detail;
        item["pending"] = true;
        return item;
    }

    private decimal ScalarDec(string sql, int importId)
    {
        object o = NORMHelper.Scalar(sql, NORMHelper.P("ImportId", importId));
        if (o == null || o == DBNull.Value) { return 0m; }
        return Convert.ToDecimal(o);
    }
}
