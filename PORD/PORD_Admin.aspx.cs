using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.UI;

namespace CPlatform.PORD
{
    /// <summary>
    /// Dashboard — read-only overview of the current review cycle.
    /// Every figure comes from the in-flight packages (NotSent → Finalised),
    /// matching the LPPI Dashboard's "current cycle" convention.
    /// </summary>
    public partial class PORD_Admin : PORDBasePage
    {
        protected string   CycleLabel;
        protected int      FlaggedCount, ReviewedCount, RowsInFile, PackageCount, SentPackages, FinalisedPackages;
        protected int      OpenPackages, DueSoon, Overdue, RepeatCount, ActiveExclusions, NonPeppolFiveDay, DistinctBps, ResolvedLastCycle;
        protected decimal  ValueStillToDeliver;
        protected DateTime LoadedDate;

        private IList<PordPo> _pos;
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
            var pkgs = store.GetPackages(true);
            _pos = store.GetCurrentCyclePos();
            _groups = store.GetAsFinGroups().ToDictionary(g => g.DmProgram);
            var batch = store.GetBatches().FirstOrDefault();

            LoadedDate = batch != null ? batch.LoadedDate : DateTime.Today;
            RowsInFile = batch != null ? batch.RowsInFile : 0;
            ResolvedLastCycle = batch != null ? batch.Resolved : 0;
            CycleLabel = LoadedDate.ToString("MMMM yyyy");

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
            ActiveExclusions = store.GetExclusions().Count(x => !x.IsRevoked && x.ExpiryDate >= DateTime.Today);
            NonPeppolFiveDay = _pos.Count(p => !PORDRules.IsForeignCurrency(p.Currency)
                                            && PORDRules.IsPeppolTerm(p.PoTermDays) && !PORDRules.IsPeppolTerm(p.BpTermDays));

            rptPackages.DataSource = pkgs;
            rptPackages.DataBind();
            rptRepeat.DataSource = repeat.Take(12).ToList();
            rptRepeat.DataBind();
            rptBatches.DataSource = store.GetBatches();
            rptBatches.DataBind();
        }

        // ---------------- typed accessors for the repeaters ----------------
        protected PordPackage   P(object o)  { return (PordPackage)o; }
        protected PordPo        Po(object o) { return (PordPo)o; }
        protected PordLoadBatch B(object o)  { return (PordLoadBatch)o; }

        protected int PoCount(int packageId)   { return _pos.Count(p => p.PackageID == packageId); }
        protected int RepeatFor(int packageId) { return _pos.Count(p => p.PackageID == packageId && p.ReviewNbr >= PORDHelper.EscalateAtReview); }

        protected string ProgressCell(int packageId)
        {
            var mine = _pos.Where(p => p.PackageID == packageId).ToList();
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

        protected string TermsHtml(PordPo p)
        {
            return TermsHtmlStatic(p);
        }

        public static string TermsHtmlStatic(PordPo p)
        {
            bool fx = PORDRules.IsForeignCurrency(p.Currency);
            bool poBad = fx ? (p.PoTermDays.HasValue && p.PoTermDays.Value < PORDRules.ForeignCurrencyMinDays)
                            : !PORDRules.IsStandardAud(p.PoTermDays);
            var sb = new StringBuilder("<span class=\"pord-terms\">");
            sb.Append(Chip(p.PoTermKey, p.PoTermDays, poBad));
            sb.Append("<span class=\"arrow\" title=\"PO terms vs BP master terms\">vs</span>");
            sb.Append(Chip(p.BpTermKey, p.BpTermDays, false));
            if (PORDRules.IsPeppolTerm(p.BpTermDays)) sb.Append("<span class=\"pord-peppol\" title=\"PEPPOL e-invoicing supplier: 5-day terms are standard\">PEPPOL</span>");
            sb.Append(p.TermsMatch ? "" : "<span class=\"match no\" title=\"PO terms override the BP master\">≠</span>");
            if (fx) sb.Append("<span class=\"pord-ccy fx\">").Append(PORDHelper.Enc(p.Currency)).Append("</span>");
            sb.Append("</span>");
            return sb.ToString();
        }

        private static string Chip(string key, int? days, bool bad)
        {
            return "<span class=\"pord-term" + (bad ? " bad" : "") + "\">" + PORDHelper.Enc(key)
                 + (days.HasValue ? "<span class=\"d\">" + days.Value + "d</span>" : "") + "</span>";
        }

        protected string RenderCategoryCards()
        {
            var cats = new[] { PordCategory.NonStandard, PordCategory.Override, PordCategory.NonStandardAndOverride, PordCategory.ForeignCurrency };
            var sb = new StringBuilder();
            foreach (var c in cats)
            {
                var inCat = _pos.Where(p => p.Category == c).ToList();
                int pct = PORDHelper.Pct(inCat.Count, _pos.Count);
                sb.Append("<div class=\"pord-catcard ").Append(PORDRules.CategoryCss(c)).Append("\">")
                  .Append("<div><div class=\"lbl\">").Append(PORDHelper.Enc(PORDRules.CategoryLabel(c))).Append("</div>")
                  .Append("<div class=\"desc\">").Append(PORDHelper.Enc(PORDRules.CategoryDescription(c))).Append("</div></div>")
                  .Append("<div><div class=\"val\">").Append(inCat.Count).Append("<small>$").Append(PORDHelper.MoneyShort(inCat.Sum(p => p.StillToDeliver))).Append("</small></div>")
                  .Append("<div class=\"bar\"><i style=\"width:").Append(pct).Append("%\"></i></div></div></div>");
            }
            return sb.ToString();
        }

        protected string RenderResponseMix()
        {
            int total = Math.Max(1, _pos.Count);
            var parts = new[]
            {
                new { Cls = "amend",    Label = "Will amend",   N = _pos.Count(p => p.Response == PordResponse.Amend) },
                new { Cls = "reason",   Label = "Valid reason", N = _pos.Count(p => p.Response == PordResponse.Reason) },
                new { Cls = "reassign", Label = "Reassign",     N = _pos.Count(p => p.Response == PordResponse.Reassign) },
                new { Cls = "noresp",   Label = "No response",  N = _pos.Count(p => p.Response == PordResponse.NoResponse) }
            };
            int awaiting = _pos.Count(p => !p.IsReviewed);
            var sb = new StringBuilder("<div class=\"pord-mix-bar\">");
            foreach (var p in parts)
                sb.Append("<i class=\"").Append(p.Cls).Append("\" style=\"width:").Append((p.N * 100.0 / total).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
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
