using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.UI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Dashboard — read-only overview of the current review cycle.
    ///
    /// Tabs: "All checks" (default) shows a card per registered check;
    /// ?check=KEY scopes every figure on the page to one check. Packages are
    /// shared by all checks, so the package table always shows a count per
    /// check. Figures come from in-flight packages (NotSent → Finalised),
    /// matching the LPPI Dashboard's "current cycle" convention.
    /// </summary>
    public partial class PORD_Admin : PORDBasePage
    {
        protected string   CycleLabel, LoadedText, ScopeLabel, CheckTabsHtml, CheckCardsHtml = "", IssueCardsHtml = "",
                           SixthTileHtml, CheckColumnHeads;
        protected int      FlaggedCount, ReviewedCount, PackageCount, SentPackages, FinalisedPackages;
        protected int      OpenPackages, DueSoon, Overdue, RepeatCount, ActiveExclusions, DistinctBps, ResolvedLastCycle;
        protected decimal  ValueStillToDeliver;
        protected bool     IsAllView;
        protected PordCheckDef Check;

        private IList<PordPo> _all, _pos;
        private IList<PordCheckDef> _active;
        private Dictionary<string, PordAsFinGroup> _groups;

        protected void Page_Load(object sender, EventArgs e)
        {
            btnReset.Visible = PORDHelper.DemoMode;
            if (!IsPostBack) Bind();
        }

        protected void btnReset_Click(object sender, EventArgs e)
        {
            PORDHelper.Store.Reset();
            phMsg.Controls.Add(new LiteralControl("<div class=\"alert ok\">Demo data reset to its starting state.</div>"));
            Bind();
        }

        private void Bind()
        {
            var store = PORDHelper.Store;
            _active = PORDChecks.Active;
            string key = Request.QueryString["check"];
            Check = string.IsNullOrEmpty(key) ? null : _active.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
            IsAllView = Check == null;
            ScopeLabel = IsAllView ? "all checks" : Check.ShortName;

            var pkgs = store.GetPackages(true);
            _all = store.GetCurrentCyclePos();
            _pos = IsAllView ? _all : _all.Where(p => p.CheckType == Check.Key).ToList();
            _groups = store.GetAsFinGroups().ToDictionary(g => g.DmProgram);

            var batches = store.GetBatches().Where(b => IsAllView || b.CheckType == Check.Key).ToList();
            var latest = batches.FirstOrDefault();
            CycleLabel = (latest != null ? latest.LoadedDate : DateTime.Today).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-AU"));
            var thisCycle = batches.Where(b => latest != null && b.LoadedDate > latest.LoadedDate.AddDays(-20)).ToList();
            LoadedText = thisCycle.Count == 0 ? "no file loaded yet"
                : (thisCycle.Count == 1 ? "from " + PORDHelper.Num(thisCycle[0].RowsInFile) + " rows · " + PORDHelper.Date(thisCycle[0].LoadedDate)
                                        : thisCycle.Count + " extracts · " + PORDHelper.Num(thisCycle.Sum(b => b.RowsInFile)) + " rows");
            ResolvedLastCycle = thisCycle.Sum(b => b.Resolved);

            FlaggedCount = _pos.Count;
            ReviewedCount = _pos.Count(p => p.IsReviewed);
            ValueStillToDeliver = _pos.Sum(p => p.StillToDeliver);
            DistinctBps = _pos.Select(p => p.BpNumber).Distinct().Count();

            PackageCount = pkgs.Count;
            SentPackages = pkgs.Count(p => p.Status != PordStatus.NotSent);
            FinalisedPackages = pkgs.Count(p => p.Status == PordStatus.Finalised);
            OpenPackages = pkgs.Count(p => p.Status != PordStatus.Finalised);
            int win = PORDHelper.ReminderWindowDays;
            Overdue = pkgs.Count(p => (p.Status == PordStatus.Sent || p.Status == PordStatus.InReview) && p.DueDate.Date < DateTime.Today);
            DueSoon = pkgs.Count(p => (p.Status == PordStatus.Sent || p.Status == PordStatus.InReview)
                                   && p.DueDate.Date >= DateTime.Today && p.DueDate.Date <= DateTime.Today.AddDays(win));

            var repeat = _pos.Where(p => p.ReviewNbr >= PORDHelper.EscalateAtReview)
                             .OrderByDescending(p => p.ReviewNbr).ThenByDescending(p => p.StillToDeliver).ToList();
            RepeatCount = repeat.Count;
            ActiveExclusions = store.GetExclusions().Count(x => !x.IsRevoked && x.ExpiryDate >= DateTime.Today && (IsAllView || x.CheckType == Check.Key));

            BuildTabs();
            if (IsAllView) BuildCheckCards(); else BuildIssueCards();
            BuildSixthTile();
            CheckColumnHeads = string.Concat(_active.Select(c =>
                "<th class=\"num\"" + (!IsAllView && c.Key == Check.Key ? " style=\"color:var(--orange-deep)\"" : "") + ">" + PORDHelper.Enc(c.ShortName) + "</th>"));

            rptPackages.DataSource = pkgs;
            rptPackages.DataBind();
            rptRepeat.DataSource = repeat.Take(12).ToList();
            rptRepeat.DataBind();
            phNoRepeat.Visible = repeat.Count == 0;
            rptBatches.DataSource = batches;
            rptBatches.DataBind();
        }

        // ------------------------------------------------------------------ tabs & cards

        private void BuildTabs()
        {
            var sb = new StringBuilder();
            sb.Append("<a href=\"PORD_Admin.aspx\" class=\"").Append(IsAllView ? "active" : "").Append("\">All checks <span class=\"cnt\">")
              .Append(_all.Count).Append("</span></a>");
            foreach (var c in _active)
            {
                sb.Append("<a href=\"PORD_Admin.aspx?check=").Append(c.Key).Append("\" class=\"").Append(!IsAllView && c.Key == Check.Key ? "active" : "").Append("\">")
                  .Append(PORDHelper.Enc(c.ShortName)).Append(" <span class=\"cnt\">").Append(_all.Count(p => p.CheckType == c.Key)).Append("</span>")
                  .Append(c.Status == "Example" ? " <span class=\"tag\">example</span>" : "").Append("</a>");
            }
            int planned = PORDChecks.All.Count(c => !c.IsActive);
            if (planned > 0) sb.Append("<span class=\"planned\" title=\"Checks listed in the Decision Brief, not yet built\">+ ").Append(planned).Append(" planned</span>");
            CheckTabsHtml = sb.ToString();
        }

        private void BuildCheckCards()
        {
            var sb = new StringBuilder();
            foreach (var c in PORDChecks.All)
            {
                var mine = _all.Where(p => p.CheckType == c.Key).ToList();
                bool live = c.IsActive;
                sb.Append(live ? "<a class=\"pord-checkcard\" href=\"PORD_Admin.aspx?check=" + c.Key + "\">" : "<div class=\"pord-checkcard planned\">");
                sb.Append("<div class=\"top\"><span class=\"nm\">").Append(PORDHelper.Enc(c.Name)).Append("</span>").Append(PORDChecks.CheckBadge(c)).Append("</div>");
                sb.Append("<div class=\"desc\">").Append(PORDHelper.Enc(c.Description)).Append("</div>");
                if (live)
                {
                    int done = mine.Count(p => p.IsReviewed), pct = PORDHelper.Pct(done, mine.Count);
                    sb.Append("<div class=\"nums\"><div><b>").Append(mine.Count).Append("</b><span>flagged</span></div>")
                      .Append("<div><b>").Append(pct).Append("%</b><span>answered</span></div>")
                      .Append("<div><b>").Append(mine.Count(p => p.ReviewNbr >= PORDHelper.EscalateAtReview)).Append("</b><span>repeat</span></div>")
                      .Append("<div><b>$").Append(PORDHelper.MoneyShort(mine.Sum(p => p.StillToDeliver))).Append("</b><span>open value</span></div></div>");
                    sb.Append("<div class=\"issues\">");
                    foreach (var i in c.Issues)
                    {
                        int n = mine.Count(p => p.IssueKey == i.Key);
                        if (n > 0) sb.Append(PORDChecks.IssuePill(c.Key, i.Key)).Append(" <b>").Append(n).Append("</b> ");
                    }
                    sb.Append("</div><span class=\"go\">Open &rarr;</span></a>");
                }
                else
                {
                    sb.Append("<div class=\"nums muted\">Plugs into the same load → review → finalise cycle once BODS provides the extract.</div></div>");
                }
            }
            CheckCardsHtml = sb.ToString();
        }

        private void BuildIssueCards()
        {
            var sb = new StringBuilder();
            foreach (var i in Check.Issues)
            {
                var inIssue = _pos.Where(p => p.IssueKey == i.Key).ToList();
                int pct = PORDHelper.Pct(inIssue.Count, _pos.Count);
                sb.Append("<div class=\"pord-catcard ").Append(i.Css).Append("\">")
                  .Append("<div><div class=\"lbl\">").Append(PORDHelper.Enc(i.Label)).Append("</div>")
                  .Append("<div class=\"desc\">").Append(PORDHelper.Enc(i.Description)).Append("</div></div>")
                  .Append("<div><div class=\"val\">").Append(inIssue.Count).Append("<small>$").Append(PORDHelper.MoneyShort(inIssue.Sum(p => p.StillToDeliver))).Append("</small></div>")
                  .Append("<div class=\"bar\"><i style=\"width:").Append(pct).Append("%\"></i></div></div></div>");
            }
            IssueCardsHtml = sb.ToString();
        }

        private void BuildSixthTile()
        {
            if (!IsAllView && Check.Key == PORDChecks.Nspt)
            {
                int n = _pos.Count(p => !PORDRules.IsForeignCurrency(p.Currency) && PORDRules.IsPeppolTerm(p.PoTermDays) && !PORDRules.IsPeppolTerm(p.BpTermDays));
                SixthTileHtml = "<div class=\"stat warn\"><div class=\"lbl\">5-day, not PEPPOL</div><div class=\"val\">" + n + "</div><div class=\"sub\">paying faster than required</div></div>";
            }
            else
            {
                SixthTileHtml = "<div class=\"stat\"><div class=\"lbl\">Open value</div><div class=\"val\">$" + PORDHelper.MoneyShort(ValueStillToDeliver)
                              + "</div><div class=\"sub\">still to deliver</div></div>";
            }
        }

        // ---------------- typed accessors for the repeaters ----------------
        protected PordPackage   P(object o)  { return (PordPackage)o; }
        protected PordPo        Po(object o) { return (PordPo)o; }
        protected PordLoadBatch B(object o)  { return (PordLoadBatch)o; }

        protected int RepeatFor(int packageId) { return _pos.Count(p => p.PackageID == packageId && p.ReviewNbr >= PORDHelper.EscalateAtReview); }

        protected string CheckColumnCells(int packageId)
        {
            var sb = new StringBuilder();
            foreach (var c in _active)
            {
                int n = _all.Count(p => p.PackageID == packageId && p.CheckType == c.Key);
                sb.Append("<td class=\"num\"").Append(n == 0 ? " style=\"color:var(--ink-4)\"" : "").Append(">").Append(n == 0 ? "—" : n.ToString()).Append("</td>");
            }
            return sb.ToString();
        }

        protected string ProgressCell(int packageId)
        {
            var mine = _pos.Where(p => p.PackageID == packageId).ToList();
            if (mine.Count == 0) return "<span class=\"muted\">—</span>";
            int done = mine.Count(p => p.IsReviewed);
            int pct = PORDHelper.Pct(done, mine.Count);
            return "<div class=\"pord-prog\"><div class=\"t\"><i class=\"" + (pct == 100 ? "full" : "") + "\" style=\"width:" + pct + "%\"></i></div>"
                 + "<span class=\"n\">" + done + " / " + mine.Count + "</span></div>";
        }

        protected string AsFinCell(string program)
        {
            PordAsFinGroup g;
            if (_groups.TryGetValue(program, out g) && g.IsConfigured)
                return PORDHelper.Enc(g.DisplayName) + "<span class=\"sub\">" + PORDHelper.Enc(g.Email) + "</span>";
            return "<a href=\"PORD_AsFin.aspx\" class=\"pill overdue\" title=\"No AS Fin mailbox — packages cannot be sent\">Not configured</a>";
        }

        protected string RenderResponseMix()
        {
            int total = Math.Max(1, _pos.Count);
            var parts = new[]
            {
                new { Cls = "amend",    Label = "Will fix",     N = _pos.Count(p => p.Response == PordResponse.Amend) },
                new { Cls = "reason",   Label = "Valid reason", N = _pos.Count(p => p.Response == PordResponse.Reason) },
                new { Cls = "reassign", Label = "Reassign",     N = _pos.Count(p => p.Response == PordResponse.Reassign) },
                new { Cls = "noresp",   Label = "No response",  N = _pos.Count(p => p.Response == PordResponse.NoResponse) }
            };
            int awaiting = _pos.Count(p => !p.IsReviewed);
            var sb = new StringBuilder("<div class=\"pord-mix-bar\">");
            foreach (var p in parts)
                sb.Append("<i class=\"").Append(p.Cls).Append("\" style=\"width:").Append((p.N * 100.0 / total).ToString("0.##", CultureInfo.InvariantCulture))
                  .Append("%\" title=\"").Append(p.Label).Append(": ").Append(p.N).Append("\"></i>");
            sb.Append("</div><div class=\"pord-mix-legend\">");
            foreach (var p in parts)
                sb.Append("<span class=\"it\"><span class=\"dot ").Append(p.Cls).Append("\"></span>").Append(p.Label)
                  .Append(" <span class=\"n\">").Append(p.N).Append("</span></span>");
            sb.Append("<span class=\"it\"><span class=\"dot awaiting\"></span>Awaiting <span class=\"n\">").Append(awaiting).Append("</span></span>");
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
