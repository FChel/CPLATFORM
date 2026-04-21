using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Sends LPPI review emails (initial + reminder) and logs every send attempt
    /// to tblLPPI_EmailLog. SMTP settings are read from web.config appSettings.
    ///
    /// TO-DO #4 — UseClientEmail flag.
    /// When LPPI.UseClientEmail = true in appSettings, Send* methods return a
    /// SendResult with a MailtoLink populated instead of attempting SMTP delivery.
    /// The caller (LPPI_SendOuts.aspx.cs) should check result.UseClientEmail and,
    /// when true, emit a window.location / window.open to the mailto: URL so the
    /// operator's local email client opens with the message pre-filled.
    ///
    /// This mirrors the existing EmailHelper / tblCC_SmtpConfig pattern used by
    /// the forms app, but without touching the database — LPPI reads all config
    /// from appSettings so this is just one more key. Set it to "true" in UAT's
    /// web.config and "false" (or omit it) in PROD.
    /// </summary>
    public static class LPPIEmail
    {
        private const string OrangeHex      = "#d75b07";
        private const string SupportMailbox = "LPPI.report@resources.defence.gov.au";

        public class SendResult
        {
            public bool   Success;
            public string ErrorMessage;
            /// <summary>
            /// When UseClientEmail is active, Success is true and this contains
            /// the mailto: URL. The caller should open it via JavaScript.
            /// </summary>
            public bool   UseClientEmail;
            public string MailtoLink;
        }

        // -------------------------------------------------------------------
        // Configuration
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns true when LPPI.UseClientEmail = "true" in appSettings.
        /// Defaults to false (i.e. SMTP) if the key is absent or any other value.
        /// </summary>
        public static bool IsClientEmailMode
        {
            get
            {
                return LPPIHelper.Setting("LPPI.UseClientEmail", "false")
                    .Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        // -------------------------------------------------------------------
        // Public entry points
        // -------------------------------------------------------------------

        public static SendResult SendInitial(int packageId)
        {
            return SendForPackage(packageId, "Initial");
        }

        public static SendResult SendReminder(int packageId)
        {
            return SendForPackage(packageId, "Reminder");
        }

        // -------------------------------------------------------------------
        // Core send logic
        // -------------------------------------------------------------------

        private static SendResult SendForPackage(int packageId, string type)
        {
            // Load package + CM info + counts.
            var sql = @"
SELECT p.PackageID, p.Token, p.DueDate, p.CreatedDate, p.Status,
       cm.CmID, cm.Program, cm.DisplayName,
       (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackageDocuments d WHERE d.PackageID = p.PackageID) AS DocCount,
       (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackageDocuments d
          INNER JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
          WHERE d.PackageID = p.PackageID AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount
FROM dbo.tblLPPI_ReviewPackages p
INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
WHERE p.PackageID = @P;";
            var dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@P", packageId));
            if (dt.Rows.Count == 0)
                return new SendResult { Success = false, ErrorMessage = "Package not found" };

            var row          = dt.Rows[0];
            var token        = Convert.ToString(row["Token"]);
            var dueDate      = Convert.ToDateTime(row["DueDate"]);
            var program      = Convert.ToString(row["DisplayName"]);
            if (string.IsNullOrWhiteSpace(program)) program = Convert.ToString(row["Program"]);
            var docCount     = Convert.ToInt32(row["DocCount"]);
            var reviewedCount= Convert.ToInt32(row["ReviewedCount"]);
            var cmId         = Convert.ToInt32(row["CmID"]);

            List<string> ccList;
            var toList = LPPIHelper.GetActiveRecipients(cmId, out ccList);
            if (toList.Count == 0)
                return new SendResult { Success = false, ErrorMessage = "No active recipients configured for this Capability Manager group" };

            var subject = BuildSubject(type, program, dueDate);
            var body    = BuildBody(type, program, dueDate, token, docCount, reviewedCount);

            // ------------------------------------------------------------------
            // TO-DO #4 — client email (mailto:) fallback.
            // When the flag is set we build a mailto: link from a plain-text
            // version of the message and return it to the caller. We still log
            // the attempt so the audit trail is consistent.
            // ------------------------------------------------------------------
            if (IsClientEmailMode)
            {
                var plainBody = HtmlToPlain(body);
                var mailto    = BuildMailto(toList, ccList, subject, plainBody);

                LogSend(packageId,
                    string.Join(";", toList) + (ccList.Count > 0 ? " | CC: " + string.Join(";", ccList) : ""),
                    type, subject, "[mailto — client email mode]", true, null);

                return new SendResult
                {
                    Success         = true,
                    UseClientEmail  = true,
                    MailtoLink      = mailto
                };
            }

            // SMTP path — original behaviour.
            string error = null;
            bool   ok    = false;
            try
            {
                using (var msg = new MailMessage())
                {
                    msg.From = new MailAddress(
                        LPPIHelper.Setting("LPPI.MailFrom", "noreply@defence.gov.au"),
                        LPPIHelper.Setting("LPPI.MailFromName", "LPPI Review"));
                    foreach (var to in toList) msg.To.Add(to);
                    foreach (var cc in ccList) msg.CC.Add(cc);
                    msg.Subject      = subject;
                    msg.Body         = body;
                    msg.IsBodyHtml   = true;
                    msg.BodyEncoding = Encoding.UTF8;

                    using (var smtp = BuildSmtp())
                        smtp.Send(msg);
                }
                ok = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            LogSend(packageId,
                string.Join(";", toList) + (ccList.Count > 0 ? " | CC: " + string.Join(";", ccList) : ""),
                type, subject, body, ok, error);

            return new SendResult { Success = ok, ErrorMessage = error };
        }

        // -------------------------------------------------------------------
        // SMTP client
        // -------------------------------------------------------------------

        private static SmtpClient BuildSmtp()
        {
            var host = LPPIHelper.Setting("LPPI.SmtpHost", "localhost");
            var port = LPPIHelper.SettingInt("LPPI.SmtpPort", 25);
            var smtp = new SmtpClient(host, port);

            var ssl = LPPIHelper.Setting("LPPI.SmtpEnableSsl", "false");
            smtp.EnableSsl = ssl.Equals("true", StringComparison.OrdinalIgnoreCase);

            var user = LPPIHelper.Setting("LPPI.SmtpUser", "");
            var pass = LPPIHelper.Setting("LPPI.SmtpPassword", "");
            if (!string.IsNullOrEmpty(user))
            {
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new System.Net.NetworkCredential(user, pass);
            }
            else
            {
                smtp.UseDefaultCredentials = true;
            }
            return smtp;
        }

        // -------------------------------------------------------------------
        // mailto: builder (client email mode)
        // -------------------------------------------------------------------

        private static string BuildMailto(List<string> toList, List<string> ccList,
                                          string subject, string plainBody)
        {
            var sb = new StringBuilder("mailto:");
            sb.Append(Uri.EscapeDataString(string.Join(",", toList)));
            sb.Append("?subject=").Append(Uri.EscapeDataString(subject));
            if (ccList.Count > 0)
                sb.Append("&cc=").Append(Uri.EscapeDataString(string.Join(",", ccList)));
            sb.Append("&body=").Append(Uri.EscapeDataString(plainBody));
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Email content builders
        // -------------------------------------------------------------------

        private static string BuildSubject(string type, string program, DateTime due)
        {
            if (type == "Reminder")
            {
                var days = (int)Math.Ceiling((due - DateTime.Today).TotalDays);
                if (days < 0) return string.Format("Reminder — LPPI Review for {0} is OVERDUE", program);
                return string.Format("Reminder — LPPI Review for {0} due in {1} day{2}",
                    program, days, days == 1 ? "" : "s");
            }
            return string.Format("Action required — LPPI Review for {0} (due {1})",
                program, due.ToString("d MMMM yyyy"));
        }

        private static string BuildBody(string type, string program, DateTime due,
                                        string token, int docCount, int reviewedCount)
        {
            var reviewUrl = BuildReviewUrl(token);

            // "Wednesday, 29 April 2026" — en-AU culture.
            var auCulture    = CultureInfo.GetCultureInfo("en-AU");
            var dueDateLong  = due.ToString("dddd, d MMMM yyyy", auCulture);

            var isReminder    = (type == "Reminder");
            var isOverdue     = isReminder && due.Date < DateTime.Today;
            var outstanding   = Math.Max(0, docCount - reviewedCount);

            var programEnc   = HttpUtility.HtmlEncode(program);
            var reviewUrlAtt = HttpUtility.HtmlAttributeEncode(reviewUrl);
            var reviewUrlTxt = HttpUtility.HtmlEncode(reviewUrl);

            // ----------------------------------------------------------------
            // Preheader — the first ~90 chars inbox clients show next to the
            // subject line. A hidden div at the very top of <body> lets us
            // control that preview without affecting the visible layout.
            // ----------------------------------------------------------------
            string preheader;
            if (isOverdue)
                preheader = string.Format("Reminder — {0} payments now overdue for {1}, due {2}.",
                    outstanding, program, dueDateLong);
            else if (isReminder)
                preheader = string.Format("Reminder — {0} payments still awaiting review for {1}, due {2}.",
                    outstanding, program, dueDateLong);
            else
                preheader = string.Format("Action required — {0} payments to review for {1}, due {2}.",
                    docCount, program, dueDateLong);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f4f4f4;font-family:Arial,sans-serif;\">");

            // Hidden preheader — not shown in the rendered email, only in the
            // inbox preview pane. The trailing &zwnj; + &nbsp; run stops
            // clients (notably Gmail) from appending the following visible
            // text to the preview.
            sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;visibility:hidden;mso-hide:all;font-size:1px;line-height:1px;color:#f4f4f4;\">")
              .Append(HttpUtility.HtmlEncode(preheader))
              .Append("&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;")
              .Append("</div>");

            sb.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"><tr><td align=\"center\" style=\"padding:24px 0;\">");
            sb.Append("<table width=\"600\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#fff;border-radius:6px;overflow:hidden;\">");

            // Header band — CPLATFORM orange accent.
            sb.AppendFormat(
                "<tr><td style=\"background:{0};padding:20px 32px;\">", OrangeHex);
            sb.Append("<span style=\"color:#fff;font-size:18px;font-weight:bold;\">LPPI Review</span></td></tr>");

            // Body
            sb.Append("<tr><td style=\"padding:28px 32px;color:#1a1a1a;font-size:14px;line-height:1.6;\">");

            // Reminder indicator (not bold — the four fixed bold items in the
            // initial body are program, docCount, "Reason Code" and the due
            // date; in reminders, the progress line below adds two more).
            if (isOverdue)
                sb.Append("<p style=\"color:#b45309;margin-top:0;\">This is a reminder — your review is now overdue.</p>");
            else if (isReminder)
                sb.Append("<p style=\"color:#b45309;margin-top:0;\">This is a reminder — your review is due soon.</p>");

            sb.AppendFormat(
                "<p>You have been provided with access to the LPPI (Late Payment Penalty Interest) review package for <strong>{0}</strong>. This package contains <strong>{1}</strong> documents for payments that were made late and incurred LPPI.</p>",
                programEnc, docCount);

            // Progress line — reminders only.
            if (isReminder)
            {
                sb.AppendFormat(
                    "<p>Progress so far: <strong>{0}</strong> of <strong>{1}</strong> documents reviewed.</p>",
                    reviewedCount, docCount);
            }

            sb.Append("<p>To access the package, please click the link below or copy and paste it into your web browser:</p>");

            sb.AppendFormat(
                "<p><a href=\"{0}\" style=\"color:{1};\">{2}</a></p>",
                reviewUrlAtt, OrangeHex, reviewUrlTxt);

            sb.Append("<p>Once the file is open, please review each item and select the appropriate <strong>Reason Code</strong>, adding comments or an objective reference where required.</p>");

            sb.AppendFormat(
                "<p>The completed review must be returned to the DFG LPPI inbox by <strong>{0}</strong>.</p>",
                HttpUtility.HtmlEncode(dueDateLong));

            // Auto-payment callout — the most consequential line in the
            // email. Amber tint with an orange left rule so it lands without
            // shouting. Uses a nested table for Outlook's sake — Outlook
            // desktop ignores border-radius on <div> but respects it on
            // table cells with the right CSS-first styling.
            sb.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:8px 0 16px 0;\"><tr>")
              .AppendFormat("<td style=\"background:#fff7ed;border-left:4px solid {0};padding:12px 16px;color:#1a1a1a;font-size:14px;line-height:1.5;\">",
                  OrangeHex)
              .Append("<strong style=\"color:#b45309;\">Please note:</strong> if no response is received by the due date, payment will be automatically processed from the responsible cost centre.")
              .Append("</td></tr></table>");

            sb.AppendFormat(
                "<p>If you have any questions or require assistance with the review, please contact us at <a href=\"mailto:{0}\" style=\"color:{1};\">{0}</a>.</p>",
                SupportMailbox, OrangeHex);

            sb.Append("</td></tr>");

            // Footer band
            sb.Append("<tr><td style=\"background:#1a1a1a;padding:16px 32px;\"><div style=\"color:#999;font-size:11px;\">Defence Finance Group · Late Payment Penalty Interest Review · ")
              .Append(LPPIHelper.Environment).Append("</div></td></tr>");
            sb.Append("</table>");
            sb.Append("</td></tr></table></body></html>");
            return sb.ToString();
        }

        private static string BuildReviewUrl(string token)
        {
            var baseUrl = LPPIHelper.Setting("LPPI.BaseUrl", "");
            if (string.IsNullOrEmpty(baseUrl) && HttpContext.Current != null)
            {
                var req = HttpContext.Current.Request;
                baseUrl = req.Url.GetLeftPart(UriPartial.Authority)
                    + req.ApplicationPath.TrimEnd('/');
            }
            return baseUrl.TrimEnd('/') + "/LPPI/LPPI_Review.aspx?t=" + Uri.EscapeDataString(token);
        }

        // -------------------------------------------------------------------
        // HTML → plain text (for mailto: body)
        //
        // The body is assembled from <p>, <strong>, <a> and a couple of
        // wrapper tags. Strategy: strip the document chrome, drop anchor
        // tags but keep their visible text (the URL and the email address
        // are both rendered as visible text in the new template, so nothing
        // is lost), convert block-closing tags to line breaks, and
        // collapse whitespace.
        // -------------------------------------------------------------------

        private static string HtmlToPlain(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            var opts = System.Text.RegularExpressions.RegexOptions.IgnoreCase
                     | System.Text.RegularExpressions.RegexOptions.Singleline;

            // Drop DOCTYPE.
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", opts);

            // Strip any <div> whose style sets display:none — this is how the
            // HTML body carries its hidden inbox-preview preheader. If we do
            // not remove the element wholesale the preheader text leaks into
            // the plain-text mailto body and duplicates the subject line.
            html = System.Text.RegularExpressions.Regex.Replace(html,
                @"<div\b[^>]*\bstyle\s*=\s*[""'][^""']*display\s*:\s*none[^""']*[""'][^>]*>.*?</div>",
                "", opts);

            // Drop the <html>/<head>/<body> wrappers.
            html = System.Text.RegularExpressions.Regex.Replace(html,
                @"</?(?:html|head|body)[^>]*>", "", opts);

            // Block-ending tags become paragraph breaks. Treating td/tr/table/div
            // the same as p/li is important because the HTML template lays the
            // message out in a single-column table — without this, the header
            // band text would run straight into the first paragraph.
            html = System.Text.RegularExpressions.Regex.Replace(html,
                @"</(?:p|li|h[1-6]|td|tr|table|div)>", "\n\n", opts);

            // Line-break tags.
            html = System.Text.RegularExpressions.Regex.Replace(html,
                @"<br\s*/?>", "\n", opts);

            // Any remaining tags — including <a href=...>, </a>, <strong>,
            // <span>, opening <p>, <li>, <td> etc. — strip, leaving the
            // visible text behind.
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", "", opts);

            // Decode entities (&amp;, &nbsp;, &rarr; etc.) and normalise
            // whitespace.
            html = WebUtility.HtmlDecode(html);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"[ \t]+", " ");
            html = System.Text.RegularExpressions.Regex.Replace(html, @" *\n *", "\n");
            html = System.Text.RegularExpressions.Regex.Replace(html, @"\n{3,}", "\n\n");
            return html.Trim();
        }

        // -------------------------------------------------------------------
        // Audit log
        // -------------------------------------------------------------------

        private static void LogSend(int packageId, string recipients, string type,
                                    string subject, string body, bool success, string error)
        {
            const string sql = @"
INSERT INTO dbo.tblLPPI_EmailLog
   (PackageID, RecipientEmail, EmailType, Subject, Body, SentBy, Success, ErrorMessage)
VALUES (@P, @R, @T, @S, @B, @U, @OK, @E);";
            LPPIHelper.ExecuteNonQuery(sql,
                LPPIHelper.P("@P",  packageId),
                LPPIHelper.P("@R",  recipients),
                LPPIHelper.P("@T",  type),
                LPPIHelper.P("@S",  subject),
                LPPIHelper.P("@B",  body),
                LPPIHelper.P("@U",  LPPIHelper.CurrentUserDisplayName()),
                LPPIHelper.P("@OK", success ? 1 : 0),
                LPPIHelper.P("@E",  (object)error ?? DBNull.Value));
        }
    }
}
