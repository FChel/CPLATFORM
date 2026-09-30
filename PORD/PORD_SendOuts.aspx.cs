using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.UI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Send-outs. Issue NotSent packages (stamping the due date) and remind
    /// Sent / InReview ones. In demo mode (and in UAT, mirroring LPPI's
    /// ProductionMode flag) the button records the send without emailing;
    /// the preview shows exactly what recipients would receive.
    /// </summary>
    public partial class PORD_SendOuts : PORDBasePage
    {
        protected string DueDateValue;
        private IList<PordPo> _pos;
        private Dictionary<string, PordAsFinGroup> _groups;
        private IList<PordPackagePoc> _pocs;

        protected void Page_Load(object sender, EventArgs e)
        {
            DueDateValue = IsPostBack && !string.IsNullOrEmpty(Request.Form["dueDate"])
                ? PORDHelper.Attr(Request.Form["dueDate"])
                : DateTime.Today.AddDays(PORDHelper.DefaultDueDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            litSendNote.Text = PORDHelper.DemoMode
                ? "Demo: sends are recorded (status and date) but no email leaves the server. Use Preview to see the emails."
                : "Emails go to the AS Fin mailbox and every PO contact in the selected packages.";
            Bind();
        }

        private void Bind()
        {
            var store = PORDHelper.Store;
            _pos = store.GetCurrentCyclePos();
            _groups = store.GetAsFinGroups().ToDictionary(g => g.DmProgram);
            var pkgs = store.GetPackages(true);
            _pocs = pkgs.SelectMany(p => store.GetPocs(p.PackageID)).ToList();
            rptPkgs.DataSource = pkgs;
            rptPkgs.DataBind();
        }

        protected void btnSend_Click(object sender, EventArgs e)
        {
            var raw = Request.Form.GetValues("pick") ?? new string[0];
            var ids = new List<int>();
            foreach (var r in raw) { int id; if (int.TryParse(r, out id)) ids.Add(id); }
            DateTime due;
            if (!DateTime.TryParseExact(Request.Form["dueDate"] ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out due) || due.Date <= DateTime.Today)
            {
                Msg("err", "Choose a respond-by date in the future.");
                return;
            }
            if (ids.Count == 0) { Msg("warn", "Tick at least one package."); return; }

            // Re-check sendability server-side (the checkbox gate is only a hint).
            var store = PORDHelper.Store;
            var groups = store.GetAsFinGroups().ToDictionary(g => g.DmProgram);
            ids = ids.Where(id =>
            {
                var p = store.GetPackage(id);
                PordAsFinGroup g;
                return p != null && CanSendStatus(p.Status) && groups.TryGetValue(p.DmProgram, out g) && g.IsConfigured;
            }).ToList();

            int n = store.MarkSent(ids, due, CurrentUser);
            Msg("ok", n + " package" + (n == 1 ? "" : "s") + " " + (PORDHelper.DemoMode ? "marked as sent (demo — no email sent)." : "sent."));
            Bind();
        }

        private void Msg(string cls, string text)
        {
            phMsg.Controls.Add(new LiteralControl("<div class=\"alert " + cls + "\">" + PORDHelper.Enc(text) + "</div>"));
        }

        // ---------------- repeater helpers ----------------
        protected PordPackage P(object o) { return (PordPackage)o; }

        private static bool CanSendStatus(string s)
        {
            return s == PordStatus.NotSent || s == PordStatus.Sent || s == PordStatus.InReview;
        }

        protected bool CanSend(PordPackage p)
        {
            PordAsFinGroup g;
            return CanSendStatus(p.Status) && _groups.TryGetValue(p.DmProgram, out g) && g.IsConfigured;
        }

        protected string PickTitle(PordPackage p)
        {
            if (!CanSendStatus(p.Status)) return "Finalised packages cannot be re-sent.";
            if (!CanSend(p)) return "Set up the AS Fin mailbox for this program first (AS Fin groups).";
            return p.Status == PordStatus.NotSent ? "Select to issue this package." : "Select to send a reminder.";
        }

        protected string AsFinCell(string program)
        {
            PordAsFinGroup g;
            if (_groups.TryGetValue(program, out g) && g.IsConfigured) return PORDHelper.Enc(g.Email);
            return "<a href=\"PORD_AsFin.aspx\" class=\"pill overdue\">Set up mailbox</a>";
        }

        protected int PocCount(int id) { return _pocs.Count(p => p.PackageID == id && _pos.Any(x => x.PackageID == id && x.PocEmail == p.PocEmail)); }
        protected int PoCount(int id)  { return _pos.Count(p => p.PackageID == id); }

        protected string Progress(int id)
        {
            var mine = _pos.Where(p => p.PackageID == id).ToList();
            int done = mine.Count(p => p.IsReviewed), pct = PORDHelper.Pct(done, mine.Count);
            return "<div class=\"pord-prog\"><div class=\"t\"><i class=\"" + (pct == 100 ? "full" : "") + "\" style=\"width:" + pct + "%\"></i></div><span class=\"n\">" + pct + "%</span></div>";
        }

        protected string Actions(PordPackage p)
        {
            var pocs = _pocs.Where(x => x.PackageID == p.PackageID && _pos.Any(po => po.PackageID == p.PackageID && po.PocEmail == x.PocEmail)).ToList();
            var js = new StringBuilder("[");
            for (int i = 0; i < pocs.Count; i++)
            {
                if (i > 0) js.Append(',');
                js.Append("{n:").Append(PORDHelper.Js(pocs[i].PocName)).Append(",e:").Append(PORDHelper.Js(pocs[i].PocEmail)).Append('}');
            }
            js.Append(']');

            var sb = new StringBuilder();
            sb.Append("<button type=\"button\" class=\"btn btn-sm btn-ghost\" onclick=\"pvOpen(").Append(p.PackageID).Append(',')
              .Append(PORDHelper.Attr(PORDHelper.Js(p.DmProgram))).Append(',').Append(PORDHelper.Attr(js.ToString())).Append(")\">Preview emails</button> ");
            sb.Append("<details class=\"links\"><summary class=\"btn btn-sm btn-secondary\">Open review &#9662;</summary><div class=\"menu\">");
            sb.Append("<div class=\"h\">AS Fin — full package</div>");
            sb.Append("<a href=\"").Append(PORDHelper.Attr(PORDHelper.ReviewUrl(p.Token))).Append("\" target=\"_blank\" rel=\"noopener\">")
              .Append(PORDHelper.Enc(p.DmProgram)).Append(" AS Fin view<span class=\"muted\">").Append(PoCount(p.PackageID)).Append(" POs</span></a>");
            sb.Append("<div class=\"h\">PO contacts — their POs only</div>");
            foreach (var c in pocs)
            {
                sb.Append("<a href=\"").Append(PORDHelper.Attr(PORDHelper.ReviewUrl(c.Token))).Append("\" target=\"_blank\" rel=\"noopener\">")
                  .Append(PORDHelper.Enc(c.PocName)).Append("<span class=\"muted\">")
                  .Append(_pos.Count(x => x.PackageID == p.PackageID && x.PocEmail == c.PocEmail)).Append(" POs</span></a>");
            }
            sb.Append("</div></details>");
            return sb.ToString();
        }
    }
}
