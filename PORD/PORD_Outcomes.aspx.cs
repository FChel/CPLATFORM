using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.UI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Close-the-loop page (LPPI's Export equivalent). Answers the spec's open
    /// question "How do we close the loop?" with three outputs:
    ///   1. Outcomes CSV to the FSO Compliance Team, then close finalised packages.
    ///   2. BP master-data list to DFIM (non-standard master terms).
    ///   3. Verification — next load's resolved list.
    /// </summary>
    public partial class PORD_Outcomes : PORDBasePage
    {
        protected string OutcomeRowsHtml = "";
        protected int FinalisedCount, PackageCount;
        protected DateTime LastLoad;

        protected void Page_Load(object sender, EventArgs e)
        {
            Bind();
        }

        private void Bind()
        {
            var store = PORDHelper.Store;
            var pkgs = store.GetPackages(false);
            var all = pkgs.SelectMany(p => store.GetPos(p.PackageID)).ToList();
            PackageCount = pkgs.Count(p => p.Status != PordStatus.Closed && p.Status != PordStatus.Cancelled);
            FinalisedCount = pkgs.Count(p => p.Status == PordStatus.Finalised);
            var batch = store.GetBatches().FirstOrDefault();
            LastLoad = batch != null ? batch.LoadedDate : DateTime.Today;

            var sb = new StringBuilder();
            foreach (var p in pkgs)
            {
                var mine = all.Where(x => x.PackageID == p.PackageID).ToList();
                sb.Append("<tr><td><strong>").Append(PORDHelper.Enc(p.DmProgram)).Append("</strong></td><td>")
                  .Append(PORDHelper.StatusPill(p.Status, p.DueDate)).Append("</td>")
                  .Append(Num(mine.Count, false))
                  .Append(Num(mine.Count(x => x.Response == PordResponse.Amend), false))
                  .Append(Num(mine.Count(x => x.Response == PordResponse.Reason), false))
                  .Append(Num(mine.Count(x => x.Response == PordResponse.Reassign), false))
                  .Append(Num(mine.Count(x => x.Response == PordResponse.NoResponse), true))
                  .Append(Num(mine.Count(x => !x.IsReviewed), false)).Append("</tr>");
            }
            OutcomeRowsHtml = sb.ToString();
            btnClose.Enabled = FinalisedCount > 0;

            var bp = store.GetBpIssues();
            rptBp.DataSource = bp;
            rptBp.DataBind();
            phBpNone.Visible = bp.Count == 0;
            btnNotify.Visible = bp.Count > 0;
            rptResolved.DataSource = store.GetResolved();
            rptResolved.DataBind();
        }

        private static string Num(int n, bool warn)
        {
            return "<td class=\"num\"" + (warn && n > 0 ? " style=\"color:var(--err);font-weight:700\"" : "") + ">" + n + "</td>";
        }

        protected PordBpIssue  Bp(object o) { return (PordBpIssue)o; }
        protected PordResolved Rs(object o) { return (PordResolved)o; }

        protected void btnClose_Click(object sender, EventArgs e)
        {
            int n = PORDHelper.Store.CloseFinalised(CurrentUser);
            phMsg.Controls.Add(new LiteralControl("<div class=\"alert ok\">" + n + " package" + (n == 1 ? "" : "s")
                + " closed." + (PORDHelper.DemoMode ? " (Demo: the outcomes report was not emailed — use Download CSV.)" : "") + "</div>"));
            Bind();
        }

        protected void btnNotify_Click(object sender, EventArgs e)
        {
            var picked = Request.Form.GetValues("bp") ?? new string[0];
            if (picked.Length == 0) { phMsg.Controls.Add(new LiteralControl("<div class=\"alert warn\">Tick at least one supplier.</div>")); return; }
            PORDHelper.Store.MarkBpNotified(new List<string>(picked));
            phMsg.Controls.Add(new LiteralControl("<div class=\"alert ok\">" + picked.Length + " supplier record" + (picked.Length == 1 ? "" : "s") + " marked as sent to DFIM.</div>"));
            Bind();
        }
    }
}
