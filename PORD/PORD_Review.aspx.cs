using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CPlatform.PORD
{
    /// <summary>
    /// Token-authenticated reviewer page (no admin login), mirroring
    /// LPPI_Review.aspx:
    ///   AS Fin token → whole package, can finalise / reopen.
    ///   POC token    → only POs where PocEmail matches, cannot finalise.
    ///
    /// Explicit save model: edits mark rows dirty; pord.js posts all dirty
    /// rows to PORD_Review_Save.ashx in one batch. Optimistic locking uses
    /// the row's ReviewedDate as its version.
    /// </summary>
    public partial class PORD_Review : PORDBasePage
    {
        protected override bool RequiresAdminAccess { get { return false; } }

        protected string Token, PageTitle = "Review", ViewerName, ViewerEmail, DueText, DueCss;
        protected bool   IsPoc, IsReadOnly, ShowFinalise, ShowReopen, IsAllReviewed;
        protected int    TotalCount, ReviewedCount, ColumnCount;
        protected decimal OpenValue;
        protected PordPackage Package;
        protected string StatusBannerHtml = "", CategoryBarHtml = "", CategoryChipsHtml = "", PocOptionsHtml = "",
                         SummaryByCategoryHtml = "", SummarySecondHtml = "";

        private IList<PordPo> _pos;
        private IList<PordReasonCode> _reasons;

        protected void Page_Load(object sender, EventArgs e)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            var store = PORDHelper.Store;
            Token = Request.QueryString["t"] ?? "";
            int packageId; string pocEmail;
            var kind = store.ResolveToken(Token, out packageId, out pocEmail);
            if (kind == PordTokenKind.None) { phError.Visible = true; phReview.Visible = false; return; }

            Package = store.GetPackage(packageId);
            if (Package == null || Package.Status == PordStatus.Cancelled) { phError.Visible = true; phReview.Visible = false; return; }
            phError.Visible = false; phReview.Visible = true;

            IsPoc = kind == PordTokenKind.Poc;
            var all = store.GetPos(packageId);
            _pos = IsPoc ? all.Where(p => string.Equals(p.PocEmail, pocEmail, StringComparison.OrdinalIgnoreCase)).ToList() : all;
            _reasons = store.GetReasonCodes(true);

            if (IsPoc)
            {
                var poc = store.GetPocs(packageId).FirstOrDefault(x => string.Equals(x.PocEmail, pocEmail, StringComparison.OrdinalIgnoreCase));
                ViewerName = poc != null ? poc.PocName : pocEmail;
                ViewerEmail = pocEmail;
            }
            else
            {
                var g = store.GetAsFinGroups().FirstOrDefault(x => x.DmProgram == Package.DmProgram);
                ViewerName = g != null && g.IsConfigured ? g.DisplayName : "AS Fin " + Package.DmProgram;
                ViewerEmail = g != null ? g.Email : "";
            }
            PageTitle = Package.DmProgram + (IsPoc ? " · " + ViewerName : "");
            Title = "PO Review — " + PageTitle;

            bool editable = Package.Status == PordStatus.NotSent || Package.Status == PordStatus.Sent || Package.Status == PordStatus.InReview;
            IsReadOnly   = !editable;
            ShowFinalise = !IsPoc && (Package.Status == PordStatus.Sent || Package.Status == PordStatus.InReview);
            ShowReopen   = !IsPoc && Package.Status == PordStatus.Finalised;

            TotalCount    = _pos.Count;
            ReviewedCount = _pos.Count(p => p.IsReviewed);
            IsAllReviewed = TotalCount > 0 && ReviewedCount == TotalCount;
            OpenValue     = _pos.Sum(p => p.StillToDeliver);
            ColumnCount   = IsPoc ? 10 : 11;

            BuildDue();
            BuildBanner();
            BuildCategoryBits();
            BuildSummary();
            if (!IsPoc)
            {
                var sb = new StringBuilder();
                foreach (var g in _pos.GroupBy(p => p.PocEmail).OrderBy(g => g.First().PocName))
                    sb.Append("<option value=\"").Append(PORDHelper.Attr(g.Key)).Append("\">")
                      .Append(PORDHelper.Enc(g.First().PocName)).Append(" (").Append(g.Count()).Append(")</option>");
                PocOptionsHtml = sb.ToString();
            }

            rptReasonRef.DataSource = _reasons;
            rptReasonRef.DataBind();
            rptPos.DataSource = _pos;
            rptPos.DataBind();
            phEmpty.Visible = _pos.Count == 0;
        }

        private void BuildDue()
        {
            int days = (Package.DueDate.Date - DateTime.Today).Days;
            if (IsReadOnly)                 { DueText = "Package " + (Package.Status == PordStatus.Finalised ? "finalised" : "closed"); DueCss = "ok"; }
            else if (Package.Status == PordStatus.NotSent) { DueText = "Not yet issued"; DueCss = ""; }
            else if (days < 0)              { DueText = (-days) + " day" + (days == -1 ? "" : "s") + " overdue"; DueCss = "err"; }
            else if (days == 0)             { DueText = "Due today"; DueCss = "err"; }
            else if (days <= PORDHelper.ReminderWindowDays) { DueText = days + " day" + (days == 1 ? "" : "s") + " left"; DueCss = "warn"; }
            else                            { DueText = days + " days left"; DueCss = ""; }
        }

        private void BuildBanner()
        {
            if (Package.Status == PordStatus.Finalised)
                StatusBannerHtml = "<div class=\"pord-banner final\"><strong>Finalised</strong> by " + PORDHelper.Enc(Package.FinalisedBy)
                    + " on " + PORDHelper.DateTimeShort(Package.FinalisedDate) + ". Responses are locked"
                    + (IsPoc ? "." : " — use Reopen if something needs to change.") + "</div>";
            else if (Package.Status == PordStatus.Closed)
                StatusBannerHtml = "<div class=\"pord-banner closed\"><strong>Closed.</strong> Outcomes for this package have been issued. This page is read only.</div>";
            else if (Package.Status == PordStatus.NotSent)
                StatusBannerHtml = "<div class=\"pord-banner notsent\"><strong>Preview.</strong> This package has not been issued yet — recipients have not been notified.</div>";
        }

        private static readonly PordCategory[] Cats =
            { PordCategory.NonStandard, PordCategory.Override, PordCategory.NonStandardAndOverride, PordCategory.ForeignCurrency };

        private void BuildCategoryBits()
        {
            var bar = new StringBuilder();
            var chips = new StringBuilder();
            foreach (var c in Cats)
            {
                int n = _pos.Count(p => p.Category == c);
                if (n == 0) continue;
                bar.Append("<i class=\"").Append(PORDRules.CategoryKey(c)).Append("\" style=\"width:")
                   .Append((n * 100.0 / Math.Max(1, TotalCount)).ToString("0.##", CultureInfo.InvariantCulture))
                   .Append("%\" title=\"").Append(PORDHelper.Attr(PORDRules.CategoryLabel(c))).Append(": ").Append(n).Append("\"></i>");
                chips.Append("<button type=\"button\" class=\"pord-chip\" data-cat=\"").Append(PORDRules.CategoryKey(c)).Append("\">")
                     .Append(PORDHelper.Enc(PORDRules.CategoryLabel(c))).Append(" <span class=\"n\">").Append(n).Append("</span></button>");
            }
            CategoryBarHtml = bar.ToString();
            CategoryChipsHtml = chips.ToString();
        }

        private void BuildSummary()
        {
            var sb = new StringBuilder();
            foreach (var c in Cats)
            {
                var inCat = _pos.Where(p => p.Category == c).ToList();
                if (inCat.Count == 0) continue;
                sb.Append("<tr><td>").Append(PORDHelper.CategoryPill(c)).Append("</td><td class=\"num\">").Append(inCat.Count)
                  .Append("</td><td class=\"num\">$").Append(PORDHelper.Money(inCat.Sum(p => p.StillToDeliver)))
                  .Append("</td><td class=\"num\">").Append(inCat.Count(p => p.IsReviewed)).Append("</td></tr>");
            }
            SummaryByCategoryHtml = sb.ToString();

            sb.Length = 0;
            if (IsPoc)
            {
                sb.Append("<thead><tr><th>Response</th><th class=\"num\">POs</th></tr></thead><tbody>");
                foreach (var r in new[] { PordResponse.Amend, PordResponse.Reason, PordResponse.Reassign, PordResponse.NoResponse, "" })
                {
                    int n = _pos.Count(p => (p.Response ?? "") == r);
                    if (n == 0 && r == PordResponse.NoResponse) continue;
                    sb.Append("<tr><td>").Append(PORDHelper.Enc(PORDRules.ResponseLabel(r))).Append("</td><td class=\"num\">").Append(n).Append("</td></tr>");
                }
            }
            else
            {
                sb.Append("<thead><tr><th>PO contact</th><th class=\"num\">POs</th><th class=\"num\">Responded</th><th class=\"num\">Repeat</th></tr></thead><tbody>");
                foreach (var g in _pos.GroupBy(p => p.PocEmail).OrderByDescending(g => g.Count(p => !p.IsReviewed)))
                {
                    int done = g.Count(p => p.IsReviewed);
                    sb.Append("<tr><td>").Append(PORDHelper.Enc(g.First().PocName)).Append("<span class=\"sub\">").Append(PORDHelper.Enc(g.Key)).Append("</span></td>")
                      .Append("<td class=\"num\">").Append(g.Count()).Append("</td>")
                      .Append("<td class=\"num\"").Append(done == g.Count() ? " style=\"color:var(--ok);font-weight:700\"" : "").Append(">").Append(done).Append("</td>")
                      .Append("<td class=\"num\">").Append(g.Count(p => p.ReviewNbr >= PORDHelper.EscalateAtReview)).Append("</td></tr>");
                }
            }
            sb.Append("</tbody>");
            SummarySecondHtml = sb.ToString();
        }

        // ---------------- repeater helpers ----------------
        protected PordPo         Po(object o) { return (PordPo)o; }
        protected PordReasonCode R(object o)  { return (PordReasonCode)o; }

        protected string SearchBlob(PordPo p)
        {
            return (p.PoNumber + " " + p.BpName + " " + p.BpNumber + " " + p.PoTermKey + " " + p.BpTermKey + " " + p.Currency + " "
                  + p.PocName + " " + p.PocEmail + " " + p.PurchasingGroup + " " + p.ContractNumber + " " + PORDRules.CategoryLabel(p.Category)).ToLowerInvariant();
        }

        protected string ResponseOptions(string current)
        {
            var sb = new StringBuilder();
            sb.Append(Opt("", "Choose…", current));
            sb.Append(Opt(PordResponse.Amend, "Amend PO terms", current));
            sb.Append(Opt(PordResponse.Reason, "Valid reason", current));
            sb.Append(Opt(PordResponse.Reassign, "Not mine – reassign", current));
            if (current == PordResponse.NoResponse) sb.Append(Opt(PordResponse.NoResponse, "No response", current));
            return sb.ToString();
        }

        protected string ReasonOptions(string current)
        {
            var sb = new StringBuilder("<option value=\"\">Choose reason…</option>");
            foreach (var r in _reasons)
            {
                sb.Append("<option value=\"").Append(PORDHelper.Attr(r.Code)).Append("\"")
                  .Append(" data-comments=\"").Append(r.RequiresComments ? "1" : "0").Append("\"")
                  .Append(" data-evidence=\"").Append(r.RequiresEvidence ? "1" : "0").Append("\"")
                  .Append(r.Code == current ? " selected" : "").Append(">")
                  .Append(PORDHelper.Enc(r.Code + " — " + r.Description)).Append("</option>");
            }
            return sb.ToString();
        }

        private static string Opt(string v, string label, string current)
        {
            return "<option value=\"" + PORDHelper.Attr(v) + "\"" + ((current ?? "") == v ? " selected" : "") + ">" + PORDHelper.Enc(label) + "</option>";
        }

        protected bool HintVisible(PordPo p)
        {
            return p.Response != PordResponse.Amend && p.Response != PordResponse.Reason;
        }

        protected string DetailHint(PordPo p)
        {
            if (p.Response == PordResponse.Reassign) return "Say who owns it in Comments.";
            if (p.Response == PordResponse.NoResponse) return "No response recorded at finalise.";
            if (p.ContractDate.HasValue && p.ContractDate.Value < new DateTime(2022, 7, 1))
                return "Contract dated " + PORDHelper.Date(p.ContractDate) + " — may qualify as VR03.";
            return "Choose a response.";
        }

        protected string DetailPanel(PordPo p)
        {
            var sb = new StringBuilder("<div class=\"po-facts\">");
            Fact(sb, "PO number", PORDHelper.PoLinkHtml(p.PoNumber), true);
            Fact(sb, "Company code", p.CompanyCode, false);
            Fact(sb, "PO created", PORDHelper.Date(p.PoCreatedDate), false);
            Fact(sb, "PO creator", p.PoCreator, false);
            Fact(sb, "Purchasing group", p.PurchasingGroup, false);
            Fact(sb, "Delivery date", PORDHelper.Date(p.DeliveryDate), false);
            Fact(sb, "Contract", p.ContractNumber + (p.ContractDate.HasValue ? " · " + PORDHelper.Date(p.ContractDate) : ""), false);
            Fact(sb, "Delivery Manager", p.DeliveryManager + " — " + p.DeliveryManagerName, false);
            Fact(sb, "PO contact", p.PocName + " · " + p.PocEmail, false);
            Fact(sb, "Ordered", PORDHelper.Money(p.Ordered) + " " + p.Currency, false);
            Fact(sb, "Delivered", PORDHelper.Money(p.Delivered), false);
            Fact(sb, "Invoiced", PORDHelper.Money(p.Invoiced), false);
            sb.Append("</div>");

            int del = PORDHelper.Pct(p.Delivered, p.Ordered), inv = PORDHelper.Pct(p.Invoiced, p.Ordered);
            sb.Append("<div class=\"po-deliv\"><div class=\"t\"><i class=\"inv\" style=\"width:").Append(inv).Append("%\"></i><i class=\"del\" style=\"width:")
              .Append(Math.Max(0, del - inv)).Append("%\"></i></div><div class=\"lg\">").Append(inv).Append("% invoiced · ").Append(del)
              .Append("% delivered · $").Append(PORDHelper.Money(p.StillToDeliver)).Append(" still to deliver</div></div>");

            sb.Append("<div class=\"po-why\"><strong>Why flagged:</strong> ").Append(PORDHelper.Enc(PORDRules.CategoryDescription(p.Category)))
              .Append(" PO terms <code>").Append(PORDHelper.Enc(PORDRules.TermLabel(p.PoTermKey, p.PoTermDays))).Append("</code>, BP master <code>")
              .Append(PORDHelper.Enc(PORDRules.TermLabel(p.BpTermKey, p.BpTermDays))).Append("</code>.");
            if (p.ReviewNbr > 1)
                sb.Append(" <strong style=\"color:var(--err)\">This is review ").Append(p.ReviewNbr).Append(" for this PO.</strong>");
            if (!string.IsNullOrEmpty(p.ReviewedBy))
                sb.Append("<br /><span class=\"muted\">Last updated by ").Append(PORDHelper.Enc(p.ReviewedBy)).Append(" · ")
                  .Append(PORDHelper.DateTimeShort(p.ReviewedDate)).Append("</span>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private static void Fact(StringBuilder sb, string k, string v, bool rawHtml)
        {
            sb.Append("<div><div class=\"k\">").Append(PORDHelper.Enc(k)).Append("</div><div class=\"v\">")
              .Append(rawHtml ? v : PORDHelper.Enc(v)).Append("</div></div>");
        }
    }
}
