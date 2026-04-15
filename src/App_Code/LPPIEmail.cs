using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.OleDb;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Sends LPPI review emails (initial + reminder) and logs every send attempt
    /// to tblLPPI_EmailLog. SMTP settings are read from web.config appSettings.
    /// </summary>
    public static class LPPIEmail
    {
        private const string OrangeHex = "#d75b07";

        public class SendResult
        {
            public bool Success;
            public string ErrorMessage;
        }

        public static SendResult SendInitial(int packageId)
        {
            return SendForPackage(packageId, "Initial");
        }

        public static SendResult SendReminder(int packageId)
        {
            return SendForPackage(packageId, "Reminder");
        }

        private static SendResult SendForPackage(int packageId, string type)
        {
            // Load package + CM info + counts
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

            var row = dt.Rows[0];
            var token = Convert.ToString(row["Token"]);
            var dueDate = Convert.ToDateTime(row["DueDate"]);
            var program = Convert.ToString(row["DisplayName"]);
            if (string.IsNullOrWhiteSpace(program)) program = Convert.ToString(row["Program"]);
            var docCount = Convert.ToInt32(row["DocCount"]);
            var reviewedCount = Convert.ToInt32(row["ReviewedCount"]);
            var cmId = Convert.ToInt32(row["CmID"]);

            List<string> ccList;
            var toList = LPPIHelper.GetActiveRecipients(cmId, out ccList);
            if (toList.Count == 0)
                return new SendResult { Success = false, ErrorMessage = "No active recipients configured for this Capability Manager group" };

            var subject = BuildSubject(type, program, dueDate);
            var body = BuildBody(type, program, dueDate, token, docCount, reviewedCount);

            // Attempt send
            string error = null;
            bool ok = false;
            try
            {
                using (var msg = new MailMessage())
                {
                    msg.From = new MailAddress(LPPIHelper.Setting("LPPI.MailFrom", "noreply@defence.gov.au"),
                                               LPPIHelper.Setting("LPPI.MailFromName", "LPPI Review"));
                    foreach (var to in toList) msg.To.Add(to);
                    foreach (var cc in ccList) msg.CC.Add(cc);
                    msg.Subject = subject;
                    msg.Body = body;
                    msg.IsBodyHtml = true;
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

            // Log
            LogSend(packageId, string.Join(";", toList) + (ccList.Count > 0 ? " | CC: " + string.Join(";", ccList) : ""),
                    type, subject, body, ok, error);

            return new SendResult { Success = ok, ErrorMessage = error };
        }

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
                smtp.Credentials = new NetworkCredential(user, pass);
            }
            else
            {
                smtp.UseDefaultCredentials = true;
            }
            return smtp;
        }

        private static string BuildSubject(string type, string program, DateTime due)
        {
            if (type == "Reminder")
            {
                var days = (int)Math.Ceiling((due - DateTime.Today).TotalDays);
                if (days < 0) return string.Format("Reminder — LPPI Review for {0} is OVERDUE", program);
                return string.Format("Reminder — LPPI Review for {0} due in {1} day{2}",
                    program, days, days == 1 ? "" : "s");
            }
            return string.Format("LPPI Review for {0} — due {1:dd MMM yyyy}", program, due);
        }

        private static string BuildBody(string type, string program, DateTime due, string token,
                                         int docCount, int reviewedCount)
        {
            var baseUrl = LPPIHelper.Setting("LPPI.BaseUrl", "");
            if (string.IsNullOrEmpty(baseUrl))
            {
                // Best-effort from current request
                try
                {
                    var req = HttpContext.Current.Request;
                    baseUrl = req.Url.GetLeftPart(UriPartial.Authority) +
                              VirtualPathUtility.ToAbsolute("~/");
                }
                catch { baseUrl = "/"; }
            }
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            var link = baseUrl + "LPPI/LPPI_Review.aspx?t=" + Uri.EscapeDataString(token);

            var contact = LPPIHelper.Setting("LPPI.SupportContact", "your finance support team");
            var dueText = due.ToString("dddd, dd MMMM yyyy");
            var heading = type == "Reminder"
                ? "Reminder: LPPI review due soon"
                : "LPPI review request";

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f4f4f5;font-family:-apple-system,Segoe UI,Arial,sans-serif;color:#1a1a1a;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f4f5;padding:32px 0;\"><tr><td align=\"center\">");
            sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.08);\">");
            sb.Append("<tr><td style=\"background:").Append(OrangeHex).Append(";padding:24px 32px;\">");
            sb.Append("<div style=\"color:#ffffff;font-size:13px;letter-spacing:1px;text-transform:uppercase;opacity:0.85;\">Defence Finance Group</div>");
            sb.Append("<div style=\"color:#ffffff;font-size:22px;font-weight:600;margin-top:4px;\">").Append(HttpUtility.HtmlEncode(heading)).Append("</div>");
            sb.Append("</td></tr>");
            sb.Append("<tr><td style=\"padding:32px;\">");
            sb.Append("<p style=\"margin:0 0 16px;font-size:15px;line-height:1.55;\">Hello ").Append(HttpUtility.HtmlEncode(program)).Append(" team,</p>");

            if (type == "Reminder")
            {
                sb.Append("<p style=\"margin:0 0 16px;font-size:15px;line-height:1.55;\">This is a friendly reminder that your Late Payment Penalty Interest (LPPI) review is due on <strong>")
                  .Append(HttpUtility.HtmlEncode(dueText)).Append("</strong>.</p>");
            }
            else
            {
                sb.Append("<p style=\"margin:0 0 16px;font-size:15px;line-height:1.55;\">A new Late Payment Penalty Interest (LPPI) review package has been prepared for your area. Please review each invoice and assign a reason code.</p>");
            }

            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;margin:24px 0;border-collapse:collapse;\">");
            sb.Append("<tr><td style=\"padding:12px 16px;background:#f8f8f9;border-radius:6px;\">");
            sb.Append("<div style=\"font-size:12px;color:#666;text-transform:uppercase;letter-spacing:0.5px;\">Invoices to review</div>");
            sb.Append("<div style=\"font-size:24px;font-weight:600;color:#1a1a1a;margin-top:4px;\">").Append(docCount).Append("</div>");
            if (reviewedCount > 0)
                sb.Append("<div style=\"font-size:13px;color:#666;margin-top:4px;\">").Append(reviewedCount).Append(" already reviewed</div>");
            sb.Append("</td></tr>");
            sb.Append("<tr><td style=\"padding-top:8px;\">");
            sb.Append("<div style=\"padding:12px 16px;background:#f8f8f9;border-radius:6px;\">");
            sb.Append("<div style=\"font-size:12px;color:#666;text-transform:uppercase;letter-spacing:0.5px;\">Due date</div>");
            sb.Append("<div style=\"font-size:18px;font-weight:600;color:#1a1a1a;margin-top:4px;\">").Append(HttpUtility.HtmlEncode(dueText)).Append("</div>");
            sb.Append("</div></td></tr>");
            sb.Append("</table>");

            sb.Append("<div style=\"text-align:center;margin:32px 0;\">");
            sb.Append("<a href=\"").Append(HttpUtility.HtmlEncode(link)).Append("\" style=\"display:inline-block;background:").Append(OrangeHex).Append(";color:#ffffff;text-decoration:none;padding:14px 32px;border-radius:6px;font-size:15px;font-weight:600;\">Open review page</a>");
            sb.Append("</div>");

            sb.Append("<p style=\"margin:0 0 8px;font-size:13px;line-height:1.55;color:#666;\">Or copy this link into your browser:</p>");
            sb.Append("<p style=\"margin:0 0 24px;font-size:12px;line-height:1.4;color:#666;word-break:break-all;\">").Append(HttpUtility.HtmlEncode(link)).Append("</p>");

            sb.Append("<hr style=\"border:none;border-top:1px solid #eee;margin:24px 0;\"/>");
            sb.Append("<p style=\"margin:0;font-size:13px;line-height:1.55;color:#666;\">Need help? Contact ").Append(HttpUtility.HtmlEncode(contact)).Append(".</p>");

            sb.Append("</td></tr>");
            sb.Append("<tr><td style=\"background:#1a1a1a;padding:16px 32px;\"><div style=\"color:#999;font-size:11px;\">Defence Finance Group · Late Payment Penalty Interest Review · ")
              .Append(LPPIHelper.Environment).Append("</div></td></tr>");
            sb.Append("</table>");
            sb.Append("</td></tr></table></body></html>");
            return sb.ToString();
        }

        private static void LogSend(int packageId, string recipients, string type,
                                    string subject, string body, bool success, string error)
        {
            const string sql = @"
INSERT INTO dbo.tblLPPI_EmailLog
   (PackageID, RecipientEmail, EmailType, Subject, Body, SentBy, Success, ErrorMessage)
VALUES (@P, @R, @T, @S, @B, @U, @OK, @E);";
            LPPIHelper.ExecuteNonQuery(sql,
                LPPIHelper.P("@P", packageId),
                LPPIHelper.P("@R", recipients),
                LPPIHelper.P("@T", type),
                LPPIHelper.P("@S", subject),
                LPPIHelper.P("@B", body),
                LPPIHelper.P("@U", LPPIHelper.CurrentUserDisplayName()),
                LPPIHelper.P("@OK", success ? 1 : 0),
                LPPIHelper.P("@E", (object)error ?? DBNull.Value));
        }
    }
}
