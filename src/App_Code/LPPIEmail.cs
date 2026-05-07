using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Sends LPPI review emails (initial + reminder) and logs every send attempt
    /// to tblLPPI_EmailLog. SMTP settings are read from web.config appSettings.
    ///
    /// ProductionMode (LPPI.ProductionMode = true in web.config) gates whether
    /// real emails can be sent. When false, Send* methods return a failure so
    /// callers cannot accidentally send in UAT. Use BuildEmailHtml() for preview
    /// in all environments without sending.
    ///
    /// In UAT (ProductionMode = false), MarkAsSent is available as a way to
    /// drive the package lifecycle end-to-end without an actual SMTP send.
    /// It is mutually exclusive with the real send: when ProductionMode is
    /// true the Mark-as-sent button is hidden, when false the Send button
    /// is disabled. One flag drives both.
    ///
    /// BCC: every real send (Initial and Reminder) BCCs the LPPI support
    /// mailbox so AS Fin has an archive of every email that left the system.
    /// The CM does not see the BCC. Driven from LPPI.SupportMailboxTo.
    ///
    /// Status transitions (driven here, not in the database):
    ///   - SendInitial on a NotSent package: on success, status -> Sent and
    ///     SentDate is stamped.
    ///   - MarkAsSent on a NotSent package (UAT only): same status transition
    ///     as SendInitial, no email sent. Audit row written with type
    ///     "Initial-MarkedSent" so the log distinguishes simulated from real.
    ///   - SendReminder: never changes status. Allowed only on Sent / InReview;
    ///     blocked on NotSent / Finalised / Exported / Cancelled.
    ///   - SendInitial on a package that has already been sent is rejected —
    ///     the caller should be using SendReminder for that case.
    ///
    /// -------------------------------------------------------------------
    /// Outlook font rendering (April 2026)
    /// -------------------------------------------------------------------
    /// Outlook on Windows uses the Word HTML rendering engine which does NOT
    /// inherit font-family from a parent element — it falls back to Times New
    /// Roman for any text-bearing element that does not declare its own
    /// font-family. The fix is two-fold:
    ///   1. A &lt;head&gt;&lt;style&gt; block covering every text element as a
    ///      defence-in-depth fallback (Outlook web, dark mode, mobile clients).
    ///   2. Inline font-family on every text-bearing element in the body
    ///      (Outlook desktop, the strict case).
    /// The FontInline constant below is appended to every existing inline
    /// style so the fix can be added surgically without rewriting layout.
    /// </summary>
    public static class LPPIEmail
    {
        private const string OrangeHex = "#d75b07";

        // Font stack used everywhere in the email. Segoe UI matches the app
        // font and renders in Outlook 2016+ on Windows. Arial is the safe
        // fallback for clients that do not have Segoe UI. sans-serif catches
        // anything else.
        private const string FontStack  = "'Segoe UI', Arial, sans-serif";
        private const string FontInline = "font-family:'Segoe UI', Arial, sans-serif;";

        // Support mailbox addresses — read from config.
        private static string SupportMailboxTo
        {
            get { return LPPIHelper.Setting("LPPI.SupportMailboxTo", "LPPI.report@resources.defence.gov.au"); }
        }
        private static string SupportMailboxCc
        {
            get { return LPPIHelper.Setting("LPPI.SupportMailboxCc", "dfg.dfspi@defence.gov.au"); }
        }

        /// <summary>
        /// Returns true when LPPI.ProductionMode = "true" in appSettings.
        /// When false, Send* methods are blocked — only preview is available
        /// and MarkAsSent can be used to drive the lifecycle for UAT testing.
        /// Defaults to false (safe) if the key is absent.
        /// </summary>
        public static bool ProductionMode
        {
            get
            {
                return LPPIHelper.Setting("LPPI.ProductionMode", "false")
                    .Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        public class SendResult
        {
            public bool   Success;
            public string ErrorMessage;
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

        /// <summary>
        /// Returns the fully-rendered HTML email body for a package without
        /// sending anything. Safe to call in all environments. Used by the
        /// preview modal on the Send-outs page.
        /// </summary>
        public static string BuildEmailHtml(int packageId, string type = "Initial")
        {
            var row = LoadPackageRow(packageId);
            if (row == null) return "<p style=\"" + FontInline + "\">Package not found.</p>";

            var dueDate       = Convert.ToDateTime(row["DueDate"]);
            var program       = Convert.ToString(row["Program"]);
            var token         = Convert.ToString(row["Token"]);
            var docCount      = Convert.ToInt32(row["DocCount"]);
            var reviewedCount = Convert.ToInt32(row["ReviewedCount"]);

            return BuildBody(type, program, dueDate, token, docCount, reviewedCount);
        }

        /// <summary>
        /// Returns a preview HTML email for a CM group that has no package yet.
        /// Uses the group's program name and current unreviewed doc count as a
        /// representative preview. No package is created.
        /// </summary>
        public static string BuildEmailHtmlByCm(int cmId)
        {
            const string sql = @"
SELECT cm.Program, cm.DisplayName,
       (SELECT COUNT(DISTINCT d.DocNoAccounting)
          FROM dbo.tblLPPI_Documents d
          LEFT JOIN dbo.tblLPPI_Reviews r
                 ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                      FROM dbo.tblLPPI_Documents d2
                                     WHERE d2.DocNoAccounting = d.DocNoAccounting)
         WHERE d.CapabilityManagerProgram = cm.Program
           AND r.ReasonCodeID IS NULL) AS UnreviewedDocs
FROM dbo.tblLPPI_CapabilityManagers cm
WHERE cm.CmID = @CmID;";
            var dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@CmID", cmId));
            if (dt.Rows.Count == 0) return "<p style=\"" + FontInline + "\">Capability Manager group not found.</p>";

            var row     = dt.Rows[0];
            var program = Convert.ToString(row["Program"]);
            var docCount = Convert.ToInt32(row["UnreviewedDocs"]);

            // Use a placeholder token and a representative due date for preview.
            var due   = DateTime.Today.AddDays(LPPIHelper.DefaultDueDays);
            var token = "PREVIEW";

            return BuildBody("Initial", program, due, token, docCount, 0);
        }

        /// <summary>
        /// UAT-only — mark a NotSent package as sent without dispatching any
        /// email. Performs the same status transition as a successful initial
        /// send (Status -> Sent, SentDate stamped) so the rest of the
        /// lifecycle (reviewer Sent -> InReview, admin Finalise) can be
        /// exercised end-to-end. Available only when ProductionMode is
        /// false; refuses to run in PROD as a defence-in-depth measure even
        /// if the caller bypasses the UI gate.
        ///
        /// Recipient validation mirrors the real send: refuses when the CM
        /// has no recipients configured, so the workflow rule "you must
        /// configure recipients before sending" is enforced in UAT too.
        ///
        /// Audit log row is written with EmailType = "Initial-MarkedSent" and
        /// ErrorMessage carrying a clear marker, so the log makes it obvious
        /// which packages were marked-as-sent versus actually sent.
        /// </summary>
        public static SendResult MarkAsSent(int packageId)
        {
            // Refuse to run in PROD — defence in depth. The button that calls
            // this is also hidden when ProductionMode is true, but the gate
            // here protects against any direct postback.
            if (ProductionMode)
                return new SendResult
                {
                    Success      = false,
                    ErrorMessage = "Mark as sent is not available in production. Use Send to dispatch real emails."
                };

            var row = LoadPackageRow(packageId);
            if (row == null)
                return new SendResult { Success = false, ErrorMessage = "Package not found." };

            var status = Convert.ToString(row["Status"]);
            if (!string.Equals(status, LPPIHelper.StatusNotSent, StringComparison.OrdinalIgnoreCase))
                return new SendResult
                {
                    Success      = false,
                    ErrorMessage = "Mark as sent is only valid for NotSent packages (current status: " + status + ")."
                };

            var program  = Convert.ToString(row["Program"]);
            var dueDate  = Convert.ToDateTime(row["DueDate"]);
            var docCount = Convert.ToInt32(row["DocCount"]);
            var cmId     = Convert.ToInt32(row["CmID"]);

            // Same recipient guard as the real send — keeps the workflow
            // rule visible and enforced in UAT.
            List<string> ccList;
            var toList = LPPIHelper.GetActiveRecipients(cmId, out ccList);
            if (toList.Count == 0)
                return new SendResult
                {
                    Success      = false,
                    ErrorMessage = "No recipients configured for this Capability Manager group. Add an email first."
                };

            // Build the same subject the real send would use, so the audit
            // row reads coherently — but no body, no SMTP call.
            var subject = BuildSubject("Initial", program, dueDate);

            // Audit row first — recipients listed exactly as the real send
            // would have used (To, CC and BCC), so the log shows what *would*
            // have happened in PROD. EmailType differentiates simulated.
            string recipientsLogged = FormatRecipientsForLog(toList, ccList, SupportMailboxTo);
            LogSend(packageId,
                    recipientsLogged,
                    "Initial-MarkedSent",
                    subject,
                    "(no body — marked as sent in test mode, no email dispatched)",
                    true,
                    "MARK-AS-SENT (test mode) — no email dispatched. ProductionMode=false.");

            // Status transition — same race-safe guard as the real send.
            LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status   = 'Sent',
       SentDate = SYSDATETIME()
 WHERE PackageID = @P
   AND Status   = 'NotSent';",
                LPPIHelper.P("@P", packageId));

            return new SendResult { Success = true, ErrorMessage = null };
        }

        // -------------------------------------------------------------------
        // Send pipeline
        // -------------------------------------------------------------------

        private static SendResult SendForPackage(int packageId, string type)
        {
            if (!ProductionMode)
                return new SendResult
                {
                    Success      = false,
                    ErrorMessage = "Email sending is disabled — LPPI.ProductionMode is not set to true in web.config."
                };

            var row = LoadPackageRow(packageId);
            if (row == null)
                return new SendResult { Success = false, ErrorMessage = "Package not found." };

            var status = Convert.ToString(row["Status"]);

            // Status guard. Initial sends are only valid on NotSent. Reminders
            // are only valid on Sent / InReview. Anything else is rejected so
            // we do not accidentally re-fire on Finalised / Exported /
            // Cancelled, or send an "initial" for a package that already had
            // its first send.
            bool isInitial = string.Equals(type, "Initial", StringComparison.OrdinalIgnoreCase);
            if (isInitial && !string.Equals(status, LPPIHelper.StatusNotSent, StringComparison.OrdinalIgnoreCase))
            {
                return new SendResult
                {
                    Success      = false,
                    ErrorMessage = "Initial send is only valid for NotSent packages (current status: " + status + "). Use Send reminder instead."
                };
            }
            if (!isInitial &&
                !(string.Equals(status, LPPIHelper.StatusSent,     StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(status, LPPIHelper.StatusInReview, StringComparison.OrdinalIgnoreCase)))
            {
                return new SendResult
                {
                    Success      = false,
                    ErrorMessage = "Reminders are only valid for Sent or InReview packages (current status: " + status + ")."
                };
            }

            var dueDate       = Convert.ToDateTime(row["DueDate"]);
            var program       = Convert.ToString(row["Program"]);
            var token         = Convert.ToString(row["Token"]);
            var docCount      = Convert.ToInt32(row["DocCount"]);
            var reviewedCount = Convert.ToInt32(row["ReviewedCount"]);
            var cmId          = Convert.ToInt32(row["CmID"]);

            List<string> ccList;
            var toList = LPPIHelper.GetActiveRecipients(cmId, out ccList);
            if (toList.Count == 0)
                return new SendResult { Success = false, ErrorMessage = "No active recipients configured for this Capability Manager group." };

            var subject = BuildSubject(type, program, dueDate);
            var body    = BuildBody(type, program, dueDate, token, docCount, reviewedCount);

            // BCC the LPPI support mailbox so AS Fin has an archive of every
            // email that leaves the system. CM does not see this address.
            // Empty string means no BCC (defensive — config might be blank).
            string bccAddress = SupportMailboxTo;

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
                    if (!string.IsNullOrWhiteSpace(bccAddress))
                        msg.Bcc.Add(bccAddress);
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
                FormatRecipientsForLog(toList, ccList, bccAddress),
                type, subject, body, ok, error);

            // Status transition — only on a successful initial send, and only
            // when the package was still NotSent (re-checked above).
            if (ok && isInitial)
            {
                LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status   = 'Sent',
       SentDate = SYSDATETIME()
 WHERE PackageID = @P
   AND Status   = 'NotSent';",
                    LPPIHelper.P("@P", packageId));
            }

            return new SendResult { Success = ok, ErrorMessage = error };
        }

        // -------------------------------------------------------------------
        // Recipient log formatter — keeps audit-row format consistent.
        // Format: "to1;to2 | CC: cc1;cc2 | BCC: bcc"
        // CC and BCC sections are omitted when empty.
        // -------------------------------------------------------------------
        private static string FormatRecipientsForLog(List<string> toList, List<string> ccList, string bcc)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(";", toList));
            if (ccList != null && ccList.Count > 0)
                sb.Append(" | CC: ").Append(string.Join(";", ccList));
            if (!string.IsNullOrWhiteSpace(bcc))
                sb.Append(" | BCC: ").Append(bcc);
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Data loader — shared between Send and Preview
        // -------------------------------------------------------------------

        private static System.Data.DataRow LoadPackageRow(int packageId)
        {
            const string sql = @"
SELECT p.PackageID, p.Token, p.DueDate, p.CreatedDate, p.SentDate, p.Status,
       cm.CmID, cm.Program, cm.DisplayName,
       (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackageDocuments d WHERE d.PackageID = p.PackageID) AS DocCount,
       (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackageDocuments d
          INNER JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
          WHERE d.PackageID = p.PackageID AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount
FROM dbo.tblLPPI_ReviewPackages p
INNER JOIN dbo.tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
WHERE p.PackageID = @P;";
            var dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@P", packageId));
            return dt.Rows.Count > 0 ? dt.Rows[0] : null;
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

            var auCulture   = CultureInfo.GetCultureInfo("en-AU");
            var dueDateLong = due.ToString("dddd, d MMMM yyyy", auCulture);

            var isReminder  = (type == "Reminder");
            var isOverdue   = isReminder && due.Date < DateTime.Today;
            var outstanding = Math.Max(0, docCount - reviewedCount);

            var programEnc   = HttpUtility.HtmlEncode(program);
            var reviewUrlAtt = HttpUtility.HtmlAttributeEncode(reviewUrl);
            var reviewUrlTxt = HttpUtility.HtmlEncode(reviewUrl);
            var dueDateEnc   = HttpUtility.HtmlEncode(dueDateLong);

            // Support mailbox mailto link — TO + CC combined.
            var supportHref = string.Format("mailto:{0}?cc={1}",
                HttpUtility.HtmlAttributeEncode(SupportMailboxTo),
                HttpUtility.HtmlAttributeEncode(SupportMailboxCc));
            var supportTxt = string.Format("{0} (cc: {1})",
                HttpUtility.HtmlEncode(SupportMailboxTo),
                HttpUtility.HtmlEncode(SupportMailboxCc));

            // Preheader
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

            // -----------------------------------------------------------------
            // <head><style> block — Outlook web, dark mode, mobile fallback.
            // Outlook desktop (Word renderer) IGNORES this for the most part,
            // which is why we ALSO declare font-family inline on every text-
            // bearing element below.
            // -----------------------------------------------------------------
            sb.Append("<!DOCTYPE html><html><head>");
            sb.Append("<meta charset=\"utf-8\" />");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.Append("<style type=\"text/css\">");
            sb.Append("body, table, td, tr, p, span, a, div, li, ul, ol, h1, h2, h3, strong, em {");
            sb.Append(" font-family: ").Append(FontStack).Append(" !important;");
            sb.Append("}");
            sb.Append("</style></head>");

            // Body — font-family inline as primary defence against Outlook's
            // Word renderer.
            sb.Append("<body style=\"margin:0;padding:0;background:#f4f4f4;").Append(FontInline).Append("\">");

            // Hidden preheader
            sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;visibility:hidden;mso-hide:all;font-size:1px;line-height:1px;color:#f4f4f4;").Append(FontInline).Append("\">")
              .Append(HttpUtility.HtmlEncode(preheader))
              .Append("&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;")
              .Append("</div>");

            sb.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"><tr><td align=\"center\" style=\"padding:24px 0;").Append(FontInline).Append("\">");
            sb.Append("<table width=\"600\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#fff;border-radius:6px;overflow:hidden;\">");

            // Header band
            sb.AppendFormat("<tr><td style=\"background:{0};padding:20px 32px;{1}\">", OrangeHex, FontInline);
            sb.Append("<span style=\"color:#fff;font-size:18px;font-weight:bold;").Append(FontInline).Append("\">LPPI Review</span></td></tr>");

            // Body
            sb.Append("<tr><td style=\"padding:28px 32px;color:#1a1a1a;font-size:14px;line-height:1.6;").Append(FontInline).Append("\">");

            if (isOverdue)
                sb.Append("<p style=\"color:#b45309;margin-top:0;").Append(FontInline).Append("\">This is a reminder — your review is now overdue.</p>");
            else if (isReminder)
                sb.Append("<p style=\"color:#b45309;margin-top:0;").Append(FontInline).Append("\">This is a reminder — your review is due soon.</p>");

            // Opening paragraph — bold: program name, doc count
            sb.AppendFormat(
                "<p style=\"{2}\">You have been provided with access to the LPPI (Late Payment Penalty Interest) review package for <span style=\"font-weight:bold;{2}\">{0}</span>. " +
                "This package contains <span style=\"font-weight:bold;{2}\">{1}</span> documents for payments that were made late and incurred LPPI.</p>",
                programEnc, docCount, FontInline);

            // Progress line — reminders only
            if (isReminder)
            {
                sb.AppendFormat(
                    "<p style=\"{6}\">{0} of {1} document{2} {3} been reviewed. <span style=\"font-weight:bold;{6}\">{4}</span> still require{5} a decision.</p>",
                    reviewedCount,
                    docCount,
                    docCount == 1 ? "" : "s",
                    docCount == 1 ? "has" : "have",
                    outstanding,
                    outstanding == 1 ? "s" : "",
                    FontInline);
            }

            // Bold: "Reason Code"
            sb.Append("<p style=\"").Append(FontInline).Append("\">For each document, please select the appropriate <span style=\"font-weight:bold;").Append(FontInline).Append("\">Reason Code</span> to indicate whether the LPPI is payable or not payable, and click the link below to begin your review.</p>");

            sb.AppendFormat(
                "<p style=\"{1}\">Please complete your review by <span style=\"font-weight:bold;{1}\">{0}</span>.</p>",
                dueDateEnc, FontInline);

            // Review link button
            sb.AppendFormat(
                "<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:24px 0;\"><tr><td align=\"center\" style=\"{2}\"><a href=\"{0}\" target=\"_blank\" style=\"background:{1};color:#fff;font-weight:bold;text-decoration:none;padding:12px 28px;border-radius:4px;display:inline-block;{2}\">Begin Review</a></td></tr></table>",
                reviewUrlAtt, OrangeHex, FontInline);

            sb.AppendFormat(
                "<p style=\"{3}\">If the button above does not work, copy and paste this link into your browser:<br/><a href=\"{0}\" target=\"_blank\" style=\"color:{1};word-break:break-all;{3}\">{2}</a></p>",
                reviewUrlAtt, OrangeHex, reviewUrlTxt, FontInline);

            sb.Append("<p style=\"").Append(FontInline).Append("\">Once the review page is open, select the appropriate Reason Code for each document. Your selections are saved automatically.</p>");

            // "Please note:" callout
            sb.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:8px 0 16px 0;\"><tr>")
              .AppendFormat("<td style=\"background:#fff7ed;border-left:4px solid {0};padding:12px 16px;color:#1a1a1a;font-size:14px;line-height:1.5;{1}\">",
                  OrangeHex, FontInline)
              .Append("<span style=\"font-weight:bold;color:#b45309;").Append(FontInline).Append("\">Please note:</span> if no response is received by the due date, payment will be automatically processed from the responsible cost centre.")
              .Append("</td></tr></table>");

            // RMG-417 policy reference — placed after the Please note callout.
            // Anchor text is short and human-readable rather than the full URL,
            // so Outlook does not split or mangle it on the way through.
            sb.Append("<p style=\"").Append(FontInline).Append("\">For background, refer to the Department of Finance&#8217;s ")
              .Append("<a href=\"https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417\"")
              .Append(" target=\"_blank\" rel=\"noopener\" style=\"color:")
              .Append(OrangeHex).Append(";").Append(FontInline)
              .Append("\">Supplier Pay On-Time or Pay Interest Policy (RMG 417)</a>.</p>");

            // Support / feedback line
            sb.AppendFormat(
                "<p style=\"{3}\">If you have any questions or require assistance with the review, please contact us at <a href=\"{0}\" style=\"color:{1};{3}\">{2}</a>.</p>",
                HttpUtility.HtmlAttributeEncode(supportHref), OrangeHex, supportTxt, FontInline);

            sb.Append("</td></tr>");

            // Footer band
            sb.Append("<tr><td style=\"background:#1a1a1a;padding:16px 32px;").Append(FontInline).Append("\"><div style=\"color:#999;font-size:11px;").Append(FontInline).Append("\">Defence Finance Group · Late Payment Penalty Interest Review · ")
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
