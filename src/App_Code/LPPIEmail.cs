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
    /// </summary>
    public static class LPPIEmail
    {
        private const string OrangeHex = "#d75b07";

        // Support mailbox addresses — read from config.
        // LPPI.SupportMailboxTo  defaults to dfg.dfspi@defence.gov.au
        // LPPI.SupportMailboxCc  defaults to LPPI.report@resources.defence.gov.au
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
        /// When false, Send* methods are blocked — only preview is available.
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
            if (row == null) return "<p>Package not found.</p>";

            var dueDate       = Convert.ToDateTime(row["DueDate"]);
            var program       = Convert.ToString(row["DisplayName"]);
            if (string.IsNullOrWhiteSpace(program)) program = Convert.ToString(row["Program"]);
            var token         = Convert.ToString(row["Token"]);
            var docCount      = Convert.ToInt32(row["DocCount"]);
            var reviewedCount = Convert.ToInt32(row["ReviewedCount"]);

            return BuildBody(type, program, dueDate, token, docCount, reviewedCount);
        }

        /// <summary>
        /// Returns a preview HTML email for a CM group that has no open package yet.
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
            if (dt.Rows.Count == 0) return "<p>Capability Manager group not found.</p>";

            var row     = dt.Rows[0];
            var program = Convert.ToString(row["DisplayName"]);
            if (string.IsNullOrWhiteSpace(program)) program = Convert.ToString(row["Program"]);
            var docCount = Convert.ToInt32(row["UnreviewedDocs"]);

            // Use a placeholder token and a representative due date for preview.
            var due   = DateTime.Today.AddDays(LPPIHelper.DefaultDueDays);
            var token = "PREVIEW";

            return BuildBody("Initial", program, due, token, docCount, 0);
        }

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

            var dueDate       = Convert.ToDateTime(row["DueDate"]);
            var program       = Convert.ToString(row["DisplayName"]);
            if (string.IsNullOrWhiteSpace(program)) program = Convert.ToString(row["Program"]);
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
        // Data loader — shared between Send and Preview
        // -------------------------------------------------------------------

        private static System.Data.DataRow LoadPackageRow(int packageId)
        {
            const string sql = @"
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
            sb.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f4f4f4;font-family:Arial,sans-serif;\">");

            // Hidden preheader
            sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;visibility:hidden;mso-hide:all;font-size:1px;line-height:1px;color:#f4f4f4;\">")
              .Append(HttpUtility.HtmlEncode(preheader))
              .Append("&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;")
              .Append("</div>");

            sb.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"><tr><td align=\"center\" style=\"padding:24px 0;\">");
            sb.Append("<table width=\"600\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#fff;border-radius:6px;overflow:hidden;\">");

            // Header band
            sb.AppendFormat("<tr><td style=\"background:{0};padding:20px 32px;\">", OrangeHex);
            sb.Append("<span style=\"color:#fff;font-size:18px;font-weight:bold;\">LPPI Review</span></td></tr>");

            // Body
            sb.Append("<tr><td style=\"padding:28px 32px;color:#1a1a1a;font-size:14px;line-height:1.6;\">");

            if (isOverdue)
                sb.Append("<p style=\"color:#b45309;margin-top:0;\">This is a reminder — your review is now overdue.</p>");
            else if (isReminder)
                sb.Append("<p style=\"color:#b45309;margin-top:0;\">This is a reminder — your review is due soon.</p>");

            // Opening paragraph — bold: program name, doc count
            sb.AppendFormat(
                "<p>You have been provided with access to the LPPI (Late Payment Penalty Interest) review package for <span style=\"font-weight:bold\">{0}</span>. This package contains <span style=\"font-weight:bold\">{1}</span> documents for payments that were made late and incurred LPPI.</p>",
                programEnc, docCount);

            // Progress line — reminders only
            if (isReminder)
            {
                sb.AppendFormat(
                    "<p>{0} of {1} document{2} {3} been reviewed. <span style=\"font-weight:bold\">{4}</span> still require{5} a decision.</p>",
                    reviewedCount,
                    docCount,
                    docCount == 1 ? "" : "s",
                    docCount == 1 ? "has" : "have",
                    outstanding,
                    outstanding == 1 ? "s" : "");
            }

            // Bold: "Reason Code", due date
            sb.Append("<p>For each document, please select the appropriate <span style=\"font-weight:bold\">Reason Code</span> to indicate whether the LPPI is payable or not payable, and click the link below to begin your review.</p>");

            sb.AppendFormat(
                "<p>Please complete your review by <span style=\"font-weight:bold\">{0}</span>.</p>",
                dueDateEnc);

            // RMG-417 policy reference
            sb.Append("<p>For background, refer to the Department of Finance&#8217;s Supplier Pay On-Time or Pay Interest Policy (RMG 417): ")
              .Append("<a href=\"https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417\" style=\"color:")
              .Append(OrangeHex)
              .Append(";\">https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417</a>.</p>");

            // Review link button
            sb.AppendFormat(
                "<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:24px 0;\"><tr><td align=\"center\"><a href=\"{0}\" target=\"_blank\" style=\"background:{1};color:#fff;font-weight:bold;text-decoration:none;padding:12px 28px;border-radius:4px;display:inline-block;\">Begin Review</a></td></tr></table>",
                reviewUrlAtt, OrangeHex);

            sb.AppendFormat(
                "<p>If the button above does not work, copy and paste this link into your browser:<br/><a href=\"{0}\" target=\"_blank\" style=\"color:{1};word-break:break-all;\">{2}</a></p>",
                reviewUrlAtt, OrangeHex, reviewUrlTxt);

            sb.Append("<p>Once the review page is open, select the appropriate Reason Code for each document. Your selections are saved automatically.</p>");

            // "Please note:" callout
            sb.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:8px 0 16px 0;\"><tr>")
              .AppendFormat("<td style=\"background:#fff7ed;border-left:4px solid {0};padding:12px 16px;color:#1a1a1a;font-size:14px;line-height:1.5;\">",
                  OrangeHex)
              .Append("<span style=\"font-weight:bold;color:#b45309;\">Please note:</span> if no response is received by the due date, payment will be automatically processed from the responsible cost centre.")
              .Append("</td></tr></table>");

            // Support / feedback line
            sb.AppendFormat(
                "<p>If you have any questions or require assistance with the review, please contact us at <a href=\"{0}\" style=\"color:{1};\">{2}</a>.</p>",
                HttpUtility.HtmlAttributeEncode(supportHref), OrangeHex, supportTxt);

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
