using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.UI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Load page. Upload → parse → classify → preview. The parser and rule
    /// engine are the production code paths; only Commit is stubbed until the
    /// PORD schema exists (demo mode refuses it with an explanation).
    /// </summary>
    public partial class PORD_Load : PORDBasePage
    {
        protected string ColumnChecklistHtml = "", CheckOptionsHtml = "";
        protected PordCheckDef SelectedCheck;

        protected void Page_Load(object sender, EventArgs e)
        {
            SelectedCheck = PORDChecks.Get(Request.Form["checkType"] ?? PORDChecks.Nspt);
            if (!SelectedCheck.IsActive) SelectedCheck = PORDChecks.Get(PORDChecks.Nspt);
            var opts = new StringBuilder();
            foreach (var c in PORDChecks.All)
                opts.Append("<option value=\"").Append(c.Key).Append("\"").Append(c.Key == SelectedCheck.Key ? " selected" : "")
                    .Append(c.IsActive ? "" : " disabled").Append(">").Append(PORDHelper.Enc(c.Name))
                    .Append(c.Status == "Live" ? "" : " (" + c.Status.ToLowerInvariant() + ")").Append("</option>");
            CheckOptionsHtml = opts.ToString();
            BuildChecklist(null);
            rptBatches.DataSource = PORDHelper.Store.GetBatches();
            rptBatches.DataBind();
        }

        protected PordLoadBatch B(object o) { return (PordLoadBatch)o; }

        protected void btnPreview_Click(object sender, EventArgs e)
        {
            if (SelectedCheck.Key != PORDChecks.Nspt) { Msg("info", ExampleLoaderMsg); return; }
            if (!fuFile.HasFile) { Msg("warn", "Choose a file first."); return; }
            if (fuFile.PostedFile.ContentLength > 50 * 1024 * 1024) { Msg("err", "Files over 50 MB are not accepted."); return; }
            var res = PORDCsvParser.Parse(fuFile.PostedFile.InputStream, Path.GetFileName(fuFile.FileName));
            Show(res);
        }

        private const string ExampleLoaderMsg = "The Currency vs bank check is an example that shows how further checks plug in. Its loader will be built when BODS provides the extract.";

        protected void btnSample_Click(object sender, EventArgs e)
        {
            if (SelectedCheck.Key != PORDChecks.Nspt) { Msg("info", ExampleLoaderMsg); return; }
            string csv = PORDCsvParser.SampleCsv(PORDHelper.Store.GetCurrentCyclePos().Where(p => p.CheckType == PORDChecks.Nspt).ToList());
            // Add a handful of compliant rows so the check shows them being dropped.
            csv += "1000,4500799001,Z020,1000210045,Southern Cross Logistics Pty Ltd,Z020,12000.00,0.00,12000.00,0.00,P21,01/12/2026,JSMITH,AUD,ARMY,D1101,jordan.smith@defence.gov.au\n"
                 + "1000,4500799002,Z005,1000210533,Kestrel Systems Integration,Z005,8800.00,4400.00,4400.00,4400.00,P33,15/11/2026,PPATEL,AUD,NAVY,D2204,priya.patel@defence.gov.au\n"
                 + "1000,4500799003,Z030,1000300118,Atlantic Avionics Inc (US),Z030,250000.00,0.00,250000.00,0.00,P40,30/01/2027,LCHEN,USD,AIR FORCE,D3310,liam.chen@defence.gov.au\n"
                 + "1000,,Z007,1000211405,Nullarbor Freight Lines,Z007,500.00,0.00,500.00,0.00,P12,01/11/2026,BWARD,AUD,JCG,D6115,ben.ward@defence.gov.au\n";
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv)))
            {
                Show(PORDCsvParser.Parse(ms, "PO_NSPT_REVIEW_sample.csv"));
            }
        }

        protected void btnCommit_Click(object sender, EventArgs e)
        {
            if (PORDHelper.DemoMode)
            {
                Msg("info", "Commit is disabled in the demonstration build. It will be enabled once the first BODS file has been reviewed and the PORD tables are approved through the SQL change process.");
                return;
            }
            Msg("err", "Commit requires the PORD database schema, which is not installed yet.");
        }

        private void Show(PORDCsvParser.Result res)
        {
            BuildChecklist(res);
            pnlResult.Visible = true;
            litFile.Text = PORDHelper.Enc(res.FileName);

            if (res.Error != null) { Msg("err", res.Error); pnlResult.Visible = false; return; }
            if (!res.HeaderOk)
            {
                litHeaderPill.Text = "<span class=\"pill overdue\">Header check failed</span>";
                Msg("err", "Missing required columns: " + string.Join(", ", res.MissingRequired.Select(c => c.Label)) + ".");
                btnCommit.Enabled = false;
                litRows.Text = litFlagged.Text = litCompliant.Text = litExcluded.Text = litFailed.Text = "—";
                litCats.Text = ""; litPreview.Text = "";
                return;
            }

            litHeaderPill.Text = res.MissingProposed.Count == 0
                ? "<span class=\"pill finalised\">Header OK</span>"
                : "<span class=\"pill duesoon\" title=\"Missing proposed columns\">Header OK · " + res.MissingProposed.Count + " columns missing</span>";
            if (res.MissingProposed.Count > 0)
                Msg("warn", "Tell BODS these columns are missing: " + string.Join(", ", res.MissingProposed.Select(c => c.Label))
                          + ". Without them POs cannot be grouped by program or routed to a PO contact, and foreign-currency POs are treated as AUD.");

            var exclusions = PORDHelper.Store.GetExclusions().Where(x => !x.IsRevoked && x.ExpiryDate >= DateTime.Today).ToList();
            int excluded = res.Rows.Count(r => r.Category != PordCategory.None && exclusions.Any(x => x.PoNumber == r.Get("PoNumber") && x.PoTermKey == r.Get("PoTerm")));

            litRows.Text = res.Rows.Count.ToString();
            litFlagged.Text = (res.Flagged - excluded).ToString();
            litCompliant.Text = res.Compliant.ToString();
            litExcluded.Text = excluded.ToString();
            litFailed.Text = res.Failed.ToString();

            var cats = new StringBuilder();
            foreach (var kv in res.ByCategory.OrderBy(k => (int)k.Key))
                cats.Append(PORDChecks.IssuePill(PORDChecks.Nspt, PORDRules.CategoryKey(kv.Key))).Append(" <strong style=\"margin-right:14px;\">").Append(kv.Value).Append("</strong>");
            litCats.Text = cats.ToString();

            var sb = new StringBuilder("<table class=\"tbl tbl-compact\"><thead><tr><th>Line</th><th>PO</th><th>Supplier</th><th>PO term</th><th>BP term</th><th>Ccy</th><th>Program</th><th>Issue</th></tr></thead><tbody>");
            foreach (var r in res.Rows.Where(x => x.Error != null).Take(5))
                sb.Append("<tr style=\"background:var(--warn-bg)\"><td>").Append(r.LineNo).Append("</td><td colspan=\"7\"><strong>Rejected:</strong> ").Append(PORDHelper.Enc(r.Error)).Append("</td></tr>");
            foreach (var r in res.Rows.Where(x => x.Error == null && x.Category != PordCategory.None).Take(25))
            {
                sb.Append("<tr><td>").Append(r.LineNo).Append("</td><td>").Append(PORDHelper.Enc(r.Get("PoNumber")))
                  .Append("</td><td>").Append(PORDHelper.Enc(r.Get("BpName")))
                  .Append("</td><td><span class=\"pord-term\">").Append(PORDHelper.Enc(r.Get("PoTerm"))).Append("</span>")
                  .Append("</td><td><span class=\"pord-term\">").Append(PORDHelper.Enc(r.Get("BpTerm"))).Append("</span>")
                  .Append("</td><td>").Append(PORDHelper.Enc(r.Get("Currency") ?? "AUD*"))
                  .Append("</td><td>").Append(PORDHelper.Enc(r.Get("DmProgram") ?? "—"))
                  .Append("</td><td>").Append(PORDChecks.IssuePill(PORDChecks.Nspt, PORDRules.CategoryKey(r.Category))).Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
            litPreview.Text = sb.ToString();
            btnCommit.Enabled = true;
        }

        private void BuildChecklist(PORDCsvParser.Result res)
        {
            var sb = new StringBuilder();
            if (SelectedCheck.Key != PORDChecks.Nspt)
            {
                foreach (var col in SelectedCheck.ExtractColumns) sb.Append("<li class=\"todo\">").Append(PORDHelper.Enc(col)).Append("</li>");
                ColumnChecklistHtml = sb.ToString();
                return;
            }
            foreach (var c in PORDCsvParser.Columns)
            {
                string cls = "todo";
                if (res != null && res.Error == null)
                    cls = res.Found.Contains(c) ? "ok" : (c.Required ? "miss" : "warn");
                sb.Append("<li class=\"").Append(cls).Append("\">").Append(PORDHelper.Enc(c.Label))
                  .Append(c.Required ? "" : " <span class=\"tag\">needed</span>").Append("</li>");
            }
            ColumnChecklistHtml = sb.ToString();
        }

        private void Msg(string cls, string text)
        {
            phMsg.Controls.Add(new LiteralControl("<div class=\"alert " + cls + "\">" + PORDHelper.Enc(text) + "</div>"));
        }
    }
}
