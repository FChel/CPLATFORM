using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace CPlatform.PORD
{
    /// <summary>
    /// Builds the AS Fin and POC notification emails.
    ///
    /// Outlook desktop renders with Word and does not inherit font-family, so
    /// EVERY text-bearing element carries an inline font declaration (same
    /// rule as LPPIEmail — see README "Outlook email rendering"). Layout is
    /// table-based for the same reason.
    ///
    /// In demo mode these are only rendered for preview; nothing is sent.
    /// </summary>
    public static class PORDEmail
    {
        private const string F = "font-family:'Segoe UI',Arial,sans-serif;";
        private const string Orange = "#d75b07";

        public static string Subject(PordPackage pkg, bool poc, bool reminder)
        {
            return (reminder ? "Reminder: " : "Action required: ")
                 + "PO compliance review — " + pkg.DmProgram + " — due " + PORDHelper.Date(pkg.DueDate);
        }

        public static string BuildAsFin(PordPackage pkg, IList<PordPo> pos, IList<PordPackagePoc> pocs, string baseUrl, bool reminder)
        {
            string link = baseUrl + "PORD/" + PORDHelper.ReviewUrl(pkg.Token);
            var sb = Open(Subject(pkg, false, reminder));

            P(sb, "Good morning,");
            P(sb, "The monthly <b>PO compliance review</b> for <b>" + E(pkg.DmProgram) + "</b> is ready. "
                + Plural(pos.Count, "open purchase order exception") + " in your area were flagged by the checks below. "
                + "One review page covers every check, with a tab for each.");
            P(sb, "Each PO contact has been sent a link to their own POs. You have the full list, and you finalise the package once responses are in.");

            Kpis(sb, pos);
            Button(sb, link, "Open the " + pkg.DmProgram + " review");

            H(sb, "Responses by PO contact");
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;border-collapse:collapse;margin:0 0 18px;\">");
            Row(sb, true, "PO contact", "POs", "Responded", "Repeat (Review Nbr ≥ 2)");
            foreach (var c in pocs)
            {
                var mine = pos.Where(p => string.Equals(p.PocEmail, c.PocEmail, StringComparison.OrdinalIgnoreCase)).ToList();
                if (mine.Count == 0) continue;
                Row(sb, false, E(c.PocName), mine.Count.ToString(), mine.Count(p => p.IsReviewed).ToString(),
                    mine.Count(p => p.ReviewNbr >= PORDHelper.EscalateAtReview).ToString());
            }
            sb.Append("</table>");

            HowTo(sb);
            P(sb, "Please finalise by <b>" + PORDHelper.Date(pkg.DueDate) + "</b>. POs with no response at finalise carry into next month’s review and are escalated.");
            return Close(sb);
        }

        public static string BuildPoc(PordPackage pkg, PordPackagePoc poc, IList<PordPo> pos, string baseUrl, bool reminder)
        {
            string link = baseUrl + "PORD/" + PORDHelper.ReviewUrl(poc.Token);
            var mine = pos.Where(p => string.Equals(p.PocEmail, poc.PocEmail, StringComparison.OrdinalIgnoreCase)).ToList();
            var sb = Open(Subject(pkg, true, reminder));

            P(sb, "Hi " + E(poc.PocName.Split(' ')[0]) + ",");
            P(sb, "You are listed as the contact for " + Plural(mine.Count, "purchase order exception") + " flagged by this month’s PO compliance checks. "
                + "For each one, either commit to fixing the PO or record the valid reason it should stay as it is.");

            Button(sb, link, "Review my POs");

            H(sb, "Your POs");
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;border-collapse:collapse;margin:0 0 18px;\">");
            Row(sb, true, "PO", "Supplier", "Check", "Issue");
            foreach (var p in mine.Take(12))
            {
                Row(sb, false, E(p.PoNumber) + (p.ReviewNbr > 1 ? " <span style=\"" + F + "color:#a31b1b;font-weight:700;\">×" + p.ReviewNbr + "</span>" : ""),
                    E(p.BpName), E(PORDChecks.Get(p.CheckType).ShortName), E(PORDChecks.IssueOf(p).Label)
                    + (p.CheckType == PORDChecks.Nspt ? " <span style=\"" + F + "color:#6b6b72;\">(" + E(p.PoTermKey) + " vs " + E(p.BpTermKey) + ")</span>" : ""));
            }
            sb.Append("</table>");
            if (mine.Count > 12) P(sb, "…and " + (mine.Count - 12) + " more on the review page.");

            HowTo(sb);
            P(sb, "Please respond by <b>" + PORDHelper.Date(pkg.DueDate) + "</b>. Replies to this email go to the "
                + E(pkg.DmProgram) + " AS Fin team.");
            return Close(sb);
        }

        // ------------------------------------------------------------------

        private static StringBuilder Open(string title)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append("</title>");
            sb.Append("<style>body,td,p,a,span,b,li{" + F + "}</style></head>");
            sb.Append("<body style=\"margin:0;padding:0;background:#f4f4f6;" + F + "\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f4f6;\"><tr><td align=\"center\" style=\"padding:24px 12px;" + F + "\">");
            sb.Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;width:100%;background:#ffffff;border:1px solid #e6e6ea;border-top:4px solid " + Orange + ";\">");
            sb.Append("<tr><td style=\"padding:22px 28px 6px;" + F + "\">");
            sb.Append("<div style=\"" + F + "font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:" + Orange + ";font-weight:700;\">PO Review · Financial Operations Compliance</div>");
            sb.Append("<div style=\"" + F + "font-size:20px;font-weight:700;color:#1a1a1c;margin-top:6px;\">").Append(E(title)).Append("</div>");
            sb.Append("</td></tr><tr><td style=\"padding:10px 28px 24px;" + F + "\">");
            return sb;
        }

        private static string Close(StringBuilder sb)
        {
            sb.Append("<p style=\"" + F + "font-size:12px;color:#6b6b72;margin:18px 0 0;border-top:1px solid #e6e6ea;padding-top:12px;\">")
              .Append("Why am I getting this? Defence Finance Group runs monthly compliance checks over open purchase orders as part of the ")
              .Append("Financial Operations Compliance Program. The payment terms check applies the ")
              .Append("<a href=\"").Append(PORDHelper.PolicyUrl).Append("\" style=\"" + F + "color:" + Orange + ";\">Supplier Pay On-Time or Pay Interest Policy (RMG-417)</a>.</p>");
            sb.Append("</td></tr></table></td></tr></table></body></html>");
            return sb.ToString();
        }

        private static void P(StringBuilder sb, string html)
        {
            sb.Append("<p style=\"" + F + "font-size:14px;line-height:1.55;color:#3a3a3e;margin:0 0 12px;\">").Append(html).Append("</p>");
        }

        private static void H(StringBuilder sb, string text)
        {
            sb.Append("<p style=\"" + F + "font-size:12px;letter-spacing:.05em;text-transform:uppercase;color:#6b6b72;font-weight:700;margin:18px 0 8px;\">")
              .Append(E(text)).Append("</p>");
        }

        private static void Button(StringBuilder sb, string href, string label)
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 18px;\"><tr>")
              .Append("<td style=\"background:" + Orange + ";border-radius:6px;" + F + "\">")
              .Append("<a href=\"").Append(HttpUtility.HtmlAttributeEncode(href)).Append("\" style=\"" + F + "display:inline-block;padding:12px 22px;color:#ffffff;font-size:14px;font-weight:700;text-decoration:none;\">")
              .Append(E(label)).Append(" &rarr;</a></td></tr></table>");
        }

        /// <summary>One tile per check present in the package: count, then issue breakdown.</summary>
        private static void Kpis(StringBuilder sb, IList<PordPo> pos)
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;border-collapse:separate;border-spacing:6px 0;margin:4px -6px 14px;\"><tr>");
            foreach (var c in PORDChecks.Active)
            {
                var mine = pos.Where(p => p.CheckType == c.Key).ToList();
                if (mine.Count == 0) continue;
                var detail = string.Join(" · ", c.Issues.Where(i => mine.Any(p => p.IssueKey == i.Key))
                                                  .Select(i => i.Label + " " + mine.Count(p => p.IssueKey == i.Key)).ToArray());
                sb.Append("<td style=\"background:#fff4ec;border:1px solid #ffe4d2;padding:10px 12px;vertical-align:top;" + F + "\">")
                  .Append("<div style=\"" + F + "font-size:12px;font-weight:700;color:#3a3a3e;\">").Append(E(c.Name)).Append("</div>")
                  .Append("<div style=\"" + F + "font-size:22px;font-weight:700;color:#8a3801;\">").Append(mine.Count).Append("</div>")
                  .Append("<div style=\"" + F + "font-size:11px;color:#6b6b72;\">").Append(E(detail)).Append("</div></td>");
            }
            sb.Append("</tr></table>");
        }

        private static void HowTo(StringBuilder sb)
        {
            H(sb, "How to respond");
            sb.Append("<ol style=\"" + F + "font-size:14px;line-height:1.55;color:#3a3a3e;margin:0 0 12px;padding-left:20px;\">");
            sb.Append("<li style=\"" + F + "\"><b>Will fix</b> — you will correct the PO in ERP (for payment terms, amend them to standard); give the date you expect it done.</li>");
            sb.Append("<li style=\"" + F + "\"><b>Valid reason</b> — pick the reason (clear and direct benefit to Defence, legislative requirement, contract before 1 July 2022, or other) and add the Objective reference for your evidence. Accepted reasons exclude the PO from future reviews.</li>");
            sb.Append("<li style=\"" + F + "\"><b>Not mine</b> — tell us who owns the PO and we will redirect it.</li>");
            sb.Append("</ol>");
        }

        private static void Row(StringBuilder sb, bool head, params string[] cells)
        {
            sb.Append("<tr>");
            foreach (var c in cells)
            {
                if (head)
                    sb.Append("<td style=\"" + F + "font-size:11px;text-transform:uppercase;letter-spacing:.04em;color:#6b6b72;font-weight:700;padding:6px 8px;border-bottom:2px solid #e6e6ea;background:#f7f7f9;\">").Append(E(c)).Append("</td>");
                else
                    sb.Append("<td style=\"" + F + "font-size:13px;color:#1a1a1c;padding:7px 8px;border-bottom:1px solid #f0f0f3;\">").Append(c).Append("</td>");
            }
            sb.Append("</tr>");
        }

        private static string Plural(int n, string noun)
        {
            return "<b>" + n + "</b> " + noun + (n == 1 ? "" : "s");
        }

        private static string E(string s) { return HttpUtility.HtmlEncode(s ?? ""); }
    }
}
