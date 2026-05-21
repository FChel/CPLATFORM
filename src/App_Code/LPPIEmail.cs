using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Sends LPPI review emails (initial + reminder) and logs every send attempt
    /// to tblLPPI_EmailLog. SMTP settings are read from web.config appSettings.
    ///
    /// May 2026 changes — POC fan-out
    /// -------------------------------------------------------------------
    /// A single SendInitial / SendReminder call now dispatches to TWO
    /// audiences (plus an optional THIRD — see Control notice below):
    ///
    ///   1. AS Fin (the CM team mailbox configured on tblLPPI_CapabilityManagers)
    ///      — receives the existing "group summary" email with the AS Fin
    ///      review link (full package, can finalise).
    ///
    ///   2. Each distinct POC for the package — receives a POC-scoped email
    ///      with their own unguessable link from tblLPPI_PackagePocs. POC
    ///      view is filtered to docs assigned to that POC's email and has
    ///      no Finalise button.
    ///
    /// From-address rules:
    ///   - AS Fin email: From = LPPI.MailFrom / LPPI.MailFromName (web.config
    ///     — unchanged).
    ///   - POC email:    From = CM.Email / CM.EmailDisplayName, so POC replies
    ///     land in front of the AS Fin team that asked for the review.
    ///
    /// Send gate: refuses to dispatch unless the CM has both Email AND
    /// EmailDisplayName configured. Same gate applies to MarkAsSent (UAT).
    /// The gate runs once at the top of the call so neither audience is sent.
    ///
    /// Empty-POC packages: if the package has no POCs (every doc has a
    /// blank PocEmail), the AS Fin email still goes out and the SendResult
    /// reports zero POC dispatches in WarningMessage. AS Fin can review and
    /// finalise on their own.
    ///
    /// Reminder-skipping: on a reminder, POCs whose docs are all reviewed
    /// are skipped. AS Fin always gets the reminder regardless of progress
    /// because they can chase up the gaps on the reviewer page directly.
    ///
    /// Control notice (vendor of interest)
    /// -------------------------------------------------------------------
    /// When a package contains any document from a vendor listed in the
    /// LPPI.ControlVendorNumbers config setting, a heads-up email is also
    /// dispatched to LPPI.ControlMailboxTo. Purpose: the listed vendors
    /// (currently the CALLIDA / CALLEO entities) are the suppliers that
    /// built this tool, so a contract-manager-side sighting confirms no
    /// payments to the build vendor have slipped through unnoticed.
    /// POC and AS Fin still do the substantive LPPI review; this is an
    /// extra pair of eyes for conflict-of-interest hygiene only.
    ///
    /// Trigger: initial send and mark-as-sent ONLY. Skipped on reminders
    /// (the contract manager only needs to sight a package once; reminders
    /// are chase-ups for AS Fin and POCs, not for control).
    /// From:    LPPI.MailFrom (system mailbox — this is a system notice,
    ///          not part of the AS Fin conversation).
    /// To:      LPPI.ControlMailboxTo (comma-separated allowed).
    /// BCC:     LPPI.SupportMailboxTo (consistent with AS Fin/POC sends).
    /// Logged:  tblLPPI_EmailLog with Audience = 'CONTROL'.
    /// Failure: does NOT roll back the AS Fin status transition. Logged
    /// and surfaced as a warning on the Send-outs result line.
    /// Feature off when either config key is blank — no row written, no
    /// match query run.
    ///
    /// Malformed POC emails (BODS occasionally emits "TBA" etc.): caught
    /// at dispatch time by ValidateDefenceEmail. The whole-system POC fan-out
    /// continues; the malformed entry is logged with Success=false and a
    /// "skipped — invalid email" ErrorMessage.
    ///
    /// ProductionMode (LPPI.ProductionMode = true in web.config) gates whether
    /// real emails can be sent. When false, Send* methods return a failure so
    /// callers cannot accidentally send in UAT. Use BuildEmailHtml() for preview
    /// in all environments without sending.
    ///
    /// In UAT (ProductionMode = false), MarkAsSent is available as a way to
    /// drive the package lifecycle end-to-end without an actual SMTP send.
    /// It simulates the full fan-out (one log row per audience), stamps
    /// InitialSentDate on each PackagePocs row, and flips the package to
    /// Sent — without a single SMTP call.
    ///
    /// BCC: every real send (Initial and Reminder, AS Fin and POC) BCCs the
    /// LPPI support mailbox so AS Fin has an archive of every email that
    /// left the system. Recipients do not see the BCC.
    ///
    /// Status transitions (driven here, not in the database):
    ///   - SendInitial on a NotSent package: on success of the AS Fin send,
    ///     status -> Sent and SentDate is stamped. POC failures do not roll
    ///     back the status transition — AS Fin still has visibility via the
    ///     reviewer page. Per-POC failures are logged and surfaced in the
    ///     SendResult.WarningMessage.
    ///   - MarkAsSent on a NotSent package (UAT only): same status transition
    ///     plus per-audience audit rows. Distinguished by EmailType =
    ///     "Initial-MarkedSent" / "Initial-MarkedSent-Poc".
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
    /// style. Both BuildBodyAsFin and BuildBodyPoc use it.
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

        // RMG-417 policy URL — referenced from both templates.
        private const string Rmg417Url =
            "https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417";

        // Handy resource URLs from the supplied templates.
        private const string PaymentTermsIntranetUrl =
            "https://dpeintranet-dfg.defence.gov.au/policies/payment-terms-prepayments-guidance-note";
        private const string PaymentTermsIbssUrl =
            "http://ibss/PublishedWebsite/LatestFinal/%7BB571C7BA-B0ED-4794-BC4B-5678E50E1189%7D/Item/%7B6D7B447E-F827-4B1C-ACFB-206E9F21EF05%7D";

        // Audience constants — written to tblLPPI_EmailLog.Audience.
        private const string AudAsFin = "ASFIN";
        private const string AudPoc   = "POC";
        private const string AudControl = "CONTROL";

        // Support mailbox addresses — read from config.
        private static string SupportMailboxTo
        {
            get { return LPPIHelper.Setting("LPPI.SupportMailboxTo", "LPPI.report@resources.defence.gov.au"); }
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
            /// <summary>
            /// Non-fatal notes from the dispatch — typically per-POC skips
            /// (invalid email, no outstanding docs on a reminder) or POC
            /// SMTP failures. The AS Fin send succeeding is what makes
            /// Success true; POC issues do not flip Success to false.
            /// </summary>
            public string WarningMessage;
            public int    PocsDispatched;
            public int    PocsSkipped;
            public int    PocsFailed;
            /// <summary>
            /// 1 when the vendor-of-interest control notice was dispatched
            /// (or simulated in mark-as-sent), 0 otherwise. Only ever set
            /// on Initial / Initial-MarkedSent — never on reminders.
            /// </summary>
            public int    ControlDispatched;
            /// <summary>
            /// 1 when the control notice was triggered (matching vendors
            /// present + recipients configured) but the SMTP send failed.
            /// </summary>
            public int    ControlFailed;
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
        ///
        /// audience = "asfin" -> AS Fin template (full package, AS Fin token).
        /// audience = "poc"   -> POC template. Two sub-modes:
        ///                       * pocEmail null/blank -> placeholder preview.
        ///                         Renders the POC template with a placeholder
        ///                         email and the AS Fin token, so the operator
        ///                         can see the POC template shape without
        ///                         picking a specific POC. The package's full
        ///                         doc count is used as a representative figure.
        ///                       * pocEmail supplied -> resolves the POC's
        ///                         actual token + filtered outstanding count
        ///                         from tblLPPI_PackagePocs for an accurate
        ///                         preview against a specific POC.
        /// type = "Initial" or "Reminder".
        /// </summary>
        public static string BuildEmailHtml(int packageId, string type = "Initial",
                                            string audience = "asfin", string pocEmail = null)
        {
            var pkg = LoadPackageRow(packageId);
            if (pkg == null) return "<p style=\"" + FontInline + "\">Package not found.</p>";

            DateTime dueDate     = Convert.ToDateTime(pkg["DueDate"]);
            string   program     = Convert.ToString(pkg["Program"]);
            int      docCount    = Convert.ToInt32(pkg["DocCount"]);
            int      reviewedAll = Convert.ToInt32(pkg["ReviewedCount"]);
            string   asFinToken  = Convert.ToString(pkg["Token"]);

            bool wantsPoc = !string.IsNullOrEmpty(audience)
                         && audience.Equals("poc", StringComparison.OrdinalIgnoreCase);

            if (!wantsPoc)
            {
                return BuildBodyAsFin(type, program, dueDate, asFinToken, docCount, reviewedAll,
                    BuildPeriodLabel(packageId, null, dueDate));
            }

            // POC placeholder preview path — pocEmail not supplied. Render the
            // POC template with a generic email and the AS Fin token (the
            // preview is shape-only; clicking the link in the placeholder
            // preview lands the operator on the AS Fin view, which is fine
            // for a template preview). The package's full doc count is used
            // as a representative figure since we are not scoped to a real POC.
            if (string.IsNullOrWhiteSpace(pocEmail))
            {
                return BuildBodyPoc(type, program, dueDate, asFinToken,
                    "<POC_EMAIL>", docCount, reviewedAll,
                    BuildPeriodLabel(packageId, null, dueDate));
            }

            // POC specific preview path — resolve the POC's row and counts.
            DataRow pocRow = LoadPocRow(packageId, pocEmail);
            if (pocRow == null)
                return "<p style=\"" + FontInline + "\">POC not found for this package.</p>";

            string pocToken = Convert.ToString(pocRow["Token"]);

            int pocTotal, pocReviewed;
            ComputePocCounts(packageId, pocEmail, out pocTotal, out pocReviewed);

            return BuildBodyPoc(type, program, dueDate, pocToken, pocEmail, pocTotal, pocReviewed,
                BuildPeriodLabel(packageId, pocEmail, dueDate));
        }

        /// <summary>
        /// Returns a representative AS Fin preview for a CM with no package
        /// yet. Uses the CM's program name and current unreviewed-doc count
        /// across the system (not scoped to any package). No package is
        /// created.
        /// </summary>
        public static string BuildEmailHtmlByCm(int cmId)
        {
            const string sql = @"
SELECT cm.Program,
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

            // No package exists yet for this CM, so there are no documents to
            // derive a ClearingMonth span from — fall back to the due-date
            // month for the period label.
            string period = due.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-AU"));

            return BuildBodyAsFin("Initial", program, due, token, docCount, 0, period);
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
        /// has no email configured.
        ///
        /// Audit rows are written for the AS Fin send AND for each POC the
        /// real send would have dispatched to, with EmailType =
        /// "Initial-MarkedSent" and "Initial-MarkedSent-Poc" respectively,
        /// so the log makes it obvious which packages were marked-as-sent
        /// versus actually sent and reflects the full intended fan-out.
        /// InitialSentDate is stamped on each PackagePocs row dispatched.
        /// </summary>
        public static SendResult MarkAsSent(int packageId)
        {
            var result = new SendResult();

            // Refuse to run in PROD — defence in depth. The button that calls
            // this is also hidden when ProductionMode is true, but the gate
            // here protects against any direct postback.
            if (ProductionMode)
            {
                result.Success = false;
                result.ErrorMessage = "Mark as sent is not available in production. Use Send to dispatch real emails.";
                return result;
            }

            DataRow pkg = LoadPackageRow(packageId);
            if (pkg == null) { result.Success = false; result.ErrorMessage = "Package not found."; return result; }

            string status = Convert.ToString(pkg["Status"]);
            if (!string.Equals(status, LPPIHelper.StatusNotSent, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.ErrorMessage = "Mark as sent is only valid for NotSent packages (current status: " + status + ").";
                return result;
            }

            int      cmId        = Convert.ToInt32(pkg["CmID"]);
            string   program     = Convert.ToString(pkg["Program"]);
            DateTime dueDate     = Convert.ToDateTime(pkg["DueDate"]);
            int      docCount    = Convert.ToInt32(pkg["DocCount"]);
            int      reviewedAll = Convert.ToInt32(pkg["ReviewedCount"]);
            string   asFinToken  = Convert.ToString(pkg["Token"]);

            // Same recipient gate as the real send.
            var cmEmail = LPPIHelper.GetCmEmail(cmId);
            if (cmEmail == null || !cmEmail.IsConfigured)
            {
                result.Success = false;
                result.ErrorMessage = "No email configured for this Capability Manager group. Add the AS Fin email and display name on the Capability Managers page first.";
                return result;
            }

            // 1) AS Fin audit row — what the real send would have dispatched.
            string asFinPeriod = BuildPeriodLabel(packageId, null, dueDate);
            string subject = BuildSubjectAsFin("Initial", program, dueDate);
            string body    = BuildBodyAsFin("Initial", program, dueDate, asFinToken, docCount, reviewedAll, asFinPeriod);
            string asFinRecipientsLogged = FormatRecipientsForLog(cmEmail.Email, SupportMailboxTo);
            LogSend(packageId,
                    asFinRecipientsLogged,
                    "Initial-MarkedSent",
                    AudAsFin,
                    null,
                    subject,
                    "(no body — marked as sent in test mode, no email dispatched)",
                    true,
                    "MARK-AS-SENT (test mode) — no email dispatched. ProductionMode=false.");

            // 2) Per-POC audit rows — simulate the fan-out the real send would do.
            var pocs = LoadPackagePocs(packageId);
            foreach (DataRow pr in pocs.Rows)
            {
                string pocAddr  = Convert.ToString(pr["PocEmail"]);
                int    packagePocId = Convert.ToInt32(pr["PackagePocID"]);
                string pocToken = Convert.ToString(pr["Token"]);

                string verr;
                string cleaned = LPPIHelper.ValidateDefenceEmail(pocAddr, out verr);
                if (cleaned == null)
                {
                    LogSend(packageId, pocAddr ?? "", "Initial-MarkedSent-Poc", AudPoc, pocAddr,
                        "(skipped — invalid POC address)", "(no body)", false,
                        "MARK-AS-SENT (test mode) — invalid POC email: " + verr);
                    result.PocsSkipped++;
                    continue;
                }

                int pocTotal, pocReviewed;
                ComputePocCounts(packageId, cleaned, out pocTotal, out pocReviewed);

                string pocPeriod  = BuildPeriodLabel(packageId, cleaned, dueDate);
                string pocSubject = BuildSubjectPoc("Initial", program, dueDate);
                string pocBody    = BuildBodyPoc("Initial", program, dueDate, pocToken, cleaned, pocTotal, pocReviewed, pocPeriod);

                LogSend(packageId,
                        FormatRecipientsForLog(cleaned, SupportMailboxTo),
                        "Initial-MarkedSent-Poc",
                        AudPoc,
                        cleaned,
                        pocSubject,
                        "(no body — marked as sent in test mode, no email dispatched)",
                        true,
                        "MARK-AS-SENT (test mode) — no email dispatched. ProductionMode=false.");
                StampPocInitialSent(packagePocId);
                result.PocsDispatched++;
            }

            // 3) Control notice — simulated audit row only, no SMTP.
            DispatchControlIfApplicable(packageId, "Initial", program, dueDate,
                                        asFinToken, docCount, reviewedAll,
                                        simulate: true, result: result);

            // Status transition — same race-safe guard as the real send.
            LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status   = 'Sent',
       SentDate = SYSDATETIME()
 WHERE PackageID = @P
   AND Status   = 'NotSent';",
                LPPIHelper.P("@P", packageId));

            result.Success = true;
            result.WarningMessage = BuildPocWarning(result);
            return result;
        }

        // -------------------------------------------------------------------
        // Send pipeline
        // -------------------------------------------------------------------

        private static SendResult SendForPackage(int packageId, string type)
        {
            var result = new SendResult();

            if (!ProductionMode)
            {
                result.Success = false;
                result.ErrorMessage = "Email sending is disabled — LPPI.ProductionMode is not set to true in web.config.";
                return result;
            }

            DataRow pkg = LoadPackageRow(packageId);
            if (pkg == null) { result.Success = false; result.ErrorMessage = "Package not found."; return result; }

            string status = Convert.ToString(pkg["Status"]);

            // Status guard. Initial sends are only valid on NotSent. Reminders
            // are only valid on Sent / InReview.
            bool isInitial = string.Equals(type, "Initial", StringComparison.OrdinalIgnoreCase);
            if (isInitial && !string.Equals(status, LPPIHelper.StatusNotSent, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.ErrorMessage = "Initial send is only valid for NotSent packages (current status: " + status + "). Use Send reminder instead.";
                return result;
            }
            if (!isInitial &&
                !(string.Equals(status, LPPIHelper.StatusSent,     StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(status, LPPIHelper.StatusInReview, StringComparison.OrdinalIgnoreCase)))
            {
                result.Success = false;
                result.ErrorMessage = "Reminders are only valid for Sent or InReview packages (current status: " + status + ").";
                return result;
            }

            int      cmId        = Convert.ToInt32(pkg["CmID"]);
            string   program     = Convert.ToString(pkg["Program"]);
            DateTime dueDate     = Convert.ToDateTime(pkg["DueDate"]);
            int      docCount    = Convert.ToInt32(pkg["DocCount"]);
            int      reviewedAll = Convert.ToInt32(pkg["ReviewedCount"]);
            string   asFinToken  = Convert.ToString(pkg["Token"]);

            // Recipient gate — refuse if CM email is not configured.
            var cmEmail = LPPIHelper.GetCmEmail(cmId);
            if (cmEmail == null || !cmEmail.IsConfigured)
            {
                result.Success = false;
                result.ErrorMessage = "No email configured for this Capability Manager group. Add the AS Fin email and display name on the Capability Managers page first.";
                return result;
            }

            // 1) AS Fin send — From = LPPI mailbox, To = CM team mailbox.
            string asFinPeriod  = BuildPeriodLabel(packageId, null, dueDate);
            string asFinSubject = BuildSubjectAsFin(type, program, dueDate);
            string asFinBody    = BuildBodyAsFin(type, program, dueDate, asFinToken, docCount, reviewedAll, asFinPeriod);

            string asFinError;
            bool asFinOk = SendOne(
                fromAddress:  LPPIHelper.Setting("LPPI.MailFrom", "noreply@defence.gov.au"),
                fromName:     LPPIHelper.Setting("LPPI.MailFromName", "LPPI Review"),
                toAddress:    cmEmail.Email,
                bccAddress:   SupportMailboxTo,
                subject:      asFinSubject,
                htmlBody:     asFinBody,
                error:        out asFinError);

            LogSend(packageId,
                FormatRecipientsForLog(cmEmail.Email, SupportMailboxTo),
                type,
                AudAsFin,
                null,
                asFinSubject,
                asFinBody,
                asFinOk,
                asFinError);

            // If the AS Fin send failed, abort — do not flood POCs with mails
            // that AS Fin will not see in their archive. This is the
            // important send.
            if (!asFinOk)
            {
                result.Success = false;
                result.ErrorMessage = "AS Fin send failed: " + (asFinError ?? "(no detail)");
                return result;
            }

            // 2) POC fan-out. From = CM team mailbox / display name, To = POC.
            //    Reminders skip POCs whose docs are all reviewed.
            var pocs = LoadPackagePocs(packageId);
            foreach (DataRow pr in pocs.Rows)
            {
                string pocAddr      = Convert.ToString(pr["PocEmail"]);
                int    packagePocId = Convert.ToInt32(pr["PackagePocID"]);
                string pocToken     = Convert.ToString(pr["Token"]);

                string verr;
                string cleaned = LPPIHelper.ValidateDefenceEmail(pocAddr, out verr);
                if (cleaned == null)
                {
                    LogSend(packageId, pocAddr ?? "", type, AudPoc, pocAddr,
                        "(skipped — invalid POC address)", "(no body)", false,
                        "POC email did not validate: " + verr);
                    result.PocsSkipped++;
                    continue;
                }

                int pocTotal, pocReviewed;
                ComputePocCounts(packageId, cleaned, out pocTotal, out pocReviewed);

                if (!isInitial && pocReviewed >= pocTotal && pocTotal > 0)
                {
                    // Reminder skip — this POC has finished. Do not pester.
                    LogSend(packageId, cleaned, type, AudPoc, cleaned,
                        "(reminder skipped — all POC docs reviewed)", "(no body)", true,
                        "Reminder skipped — all of this POC's documents are reviewed.");
                    result.PocsSkipped++;
                    continue;
                }

                string pocPeriod  = BuildPeriodLabel(packageId, cleaned, dueDate);
                string pocSubject = BuildSubjectPoc(type, program, dueDate);
                string pocBody    = BuildBodyPoc(type, program, dueDate, pocToken, cleaned, pocTotal, pocReviewed, pocPeriod);

                string pocError;
                bool pocOk = SendOne(
                    fromAddress:  cmEmail.Email,
                    fromName:     cmEmail.EmailDisplayName,
                    toAddress:    cleaned,
                    bccAddress:   SupportMailboxTo,
                    subject:      pocSubject,
                    htmlBody:     pocBody,
                    error:        out pocError);

                LogSend(packageId,
                    FormatRecipientsForLog(cleaned, SupportMailboxTo),
                    type,
                    AudPoc,
                    cleaned,
                    pocSubject,
                    pocBody,
                    pocOk,
                    pocError);

                if (pocOk)
                {
                    if (isInitial) StampPocInitialSent(packagePocId);
                    else           StampPocLastReminder(packagePocId);
                    result.PocsDispatched++;
                }
                else
                {
                    result.PocsFailed++;
                }
            }

            // 3) Control notice fan-out. Initial only — reminders do not
            //    re-notify the contract manager. Failure does not roll the
            //    overall result; logged and surfaced as a warning.
            if (isInitial)
            {
                DispatchControlIfApplicable(packageId, type, program, dueDate,
                                            asFinToken, docCount, reviewedAll,
                                            simulate: false, result: result);
            }

            // Status transition — only on a successful initial send. Done
            // after AS Fin success, regardless of POC outcomes (per the
            // policy above).
            if (isInitial)
            {
                LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_ReviewPackages
   SET Status   = 'Sent',
       SentDate = SYSDATETIME()
 WHERE PackageID = @P
   AND Status   = 'NotSent';",
                    LPPIHelper.P("@P", packageId));
            }

            result.Success = true;
            result.WarningMessage = BuildPocWarning(result);
            return result;
        }

        /// <summary>
        /// Single-message dispatch through SmtpClient. Returns true / sets
        /// error to null on success, false / error populated on failure.
        /// Called once for the AS Fin send, then once per POC.
        /// </summary>
        private static bool SendOne(string fromAddress, string fromName,
                                    string toAddress, string bccAddress,
                                    string subject, string htmlBody, out string error)
        {
            error = null;
            try
            {
                using (var msg = new MailMessage())
                {
                    msg.From = new MailAddress(fromAddress, fromName);
                    msg.To.Add(toAddress);
                    if (!string.IsNullOrWhiteSpace(bccAddress))
                        msg.Bcc.Add(bccAddress);
                    msg.Subject      = subject;
                    msg.Body         = htmlBody;
                    msg.IsBodyHtml   = true;
                    msg.BodyEncoding = Encoding.UTF8;

                    using (var smtp = BuildSmtp())
                        smtp.Send(msg);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
        // Subject builders
        // -------------------------------------------------------------------

        private static string BuildSubjectAsFin(string type, string program, DateTime due)
        {
            // From the supplied AS Fin templates — note the "Group Summary" suffix
            // distinguishes this from the per-POC mail, which is otherwise
            // similarly worded.
            if (string.Equals(type, "Reminder", StringComparison.OrdinalIgnoreCase))
                return "REMINDER - ACTION REQUIRED: Late Payment Penalty Interest Review Group Summary";
            return "ACTION REQUIRED: Late Payment Penalty Interest Review Group Summary";
        }

        private static string BuildSubjectPoc(string type, string program, DateTime due)
        {
            // From the supplied POC templates.
            if (string.Equals(type, "Reminder", StringComparison.OrdinalIgnoreCase))
                return "REMINDER - ACTION REQUIRED: Late Payment Penalty Interest Review";
            return "ACTION REQUIRED: Late Payment Penalty Interest Review";
        }

        // -------------------------------------------------------------------
        // Body builders
        //
        // Two builders for the two audiences. They share a chrome (head/style,
        // outer table, header band, footer band, RMG-417 link, support line)
        // via the helper functions further down. The body contents are the
        // verbatim per-template structure as supplied.
        // -------------------------------------------------------------------

        private static string BuildBodyAsFin(string type, string program, DateTime due,
                                             string token, int docCount, int reviewedCount,
                                             string periodLabel)
        {
            string reviewUrl   = BuildReviewUrl(token);
            bool   isReminder  = string.Equals(type, "Reminder", StringComparison.OrdinalIgnoreCase);
            int    outstanding = Math.Max(0, docCount - reviewedCount);

            var auCulture     = CultureInfo.GetCultureInfo("en-AU");
            string monthYear  = periodLabel;
            string dueLong    = due.ToString("dddd, d MMMM yyyy", auCulture);

            string preheader  = isReminder
                ? string.Format("Reminder — {0} document(s) outstanding for {1}, due {2}.", outstanding, program, dueLong)
                : string.Format("Action required — {0} document(s) to review for {1}, due {2}.", docCount, program, dueLong);

            var sb = new StringBuilder();
            AppendDoctype(sb);
            AppendBodyOpen(sb, preheader);

            // Body text container
            sb.Append("<tr><td style=\"padding:28px 32px;color:#1a1a1a;font-size:14px;line-height:1.6;").Append(FontInline).Append("\">");

            // Lead paragraph
            if (isReminder)
            {
                sb.Append("<p style=\"").Append(FontInline).Append("\">")
                  .Append("The Late Payment Penalty Interest (LPPI) review package for ")
                  .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(monthYear)).Append("</strong>")
                  .Append(" for <strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(program)).Append("</strong>")
                  .Append(" is due for completion by <strong style=\"").Append(FontInline).Append("\">")
                  .Append(LPPIHelper.Enc(dueLong)).Append("</strong>.</p>");

                sb.Append("<p style=\"").Append(FontInline).Append("\"><strong style=\"").Append(FontInline).Append("\">")
                  .Append(outstanding).Append("</strong> document").Append(outstanding == 1 ? " remains" : "s remain")
                  .Append(" outstanding.</p>");
            }
            else
            {
                sb.Append("<p style=\"").Append(FontInline).Append("\">")
                  .Append("Please see below the Late Payment Penalty Interest (LPPI) review package for ")
                  .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(monthYear)).Append("</strong>")
                  .Append(" for <strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(program)).Append("</strong>.</p>");

                sb.Append("<p style=\"").Append(FontInline).Append("\">")
                  .Append("The package contains <strong style=\"").Append(FontInline).Append("\">").Append(docCount).Append("</strong>")
                  .Append(" document").Append(docCount == 1 ? "" : "s").Append(" for payments that were made late and have incurred LPPI in accordance with ")
                  .Append("<a href=\"").Append(Rmg417Url).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:")
                  .Append(OrangeHex).Append(";").Append(FontInline).Append("\">Supplier Pay On-Time or Pay Interest Policy (RMG 417)</a>.</p>");
            }

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Your action is required to enforce business owner obligations and endorse timely supplier compensation.</p>");

            // Action Required heading
            sb.Append("<h3 style=\"font-size:15px;margin:18px 0 8px;").Append(FontInline).Append("\">Action Required</h3>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Each invoice Point of Contact (POC) ")
              .Append(isReminder ? "will receive a reminder email" : "will receive an email")
              .Append(" for LPPI incurred for late payments.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("The POC must review each document and select the appropriate <strong style=\"").Append(FontInline).Append("\">Reason Code</strong> ")
              .Append("to indicate whether LPPI is payable or not payable.")
              .Append(isReminder ? " Selections are saved automatically." : "")
              .Append("</p>");

            AppendCallout(sb,
                "Please note: if no response is received by the due date, LPPI will be automatically paid from the responsible cost centre.");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("You are accountable for ensuring the completion of this review for ")
              .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(program)).Append("</strong>")
              .Append(" by <strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(dueLong)).Append("</strong>.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Please refer to the Instructions tab in the LPPI Dashboard for information about how to complete your review.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Click the Begin Review button below to track progress for ")
              .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(program)).Append("</strong>.</p>");

            AppendBeginReviewButton(sb, reviewUrl);
            AppendFallbackUrl(sb, reviewUrl);
            AppendHandyResources(sb);
            AppendSupportLine(sb);

            sb.Append("</td></tr>");
            AppendFooter(sb);
            AppendBodyClose(sb);
            return sb.ToString();
        }

        private static string BuildBodyPoc(string type, string program, DateTime due,
                                           string token, string pocEmail,
                                           int pocTotal, int pocReviewed,
                                           string periodLabel)
        {
            string reviewUrl  = BuildReviewUrl(token);
            bool   isReminder = string.Equals(type, "Reminder", StringComparison.OrdinalIgnoreCase);
            int    outstanding = Math.Max(0, pocTotal - pocReviewed);

            var auCulture     = CultureInfo.GetCultureInfo("en-AU");
            string monthYear  = periodLabel;
            string dueLong    = due.ToString("dddd, d MMMM yyyy", auCulture);

            string preheader = isReminder
                ? string.Format("Reminder — {0} of your document(s) outstanding, due {1}.", outstanding, dueLong)
                : string.Format("Action required — {0} of your document(s) to review, due {1}.", pocTotal, dueLong);

            var sb = new StringBuilder();
            AppendDoctype(sb);
            AppendBodyOpen(sb, preheader);

            sb.Append("<tr><td style=\"padding:28px 32px;color:#1a1a1a;font-size:14px;line-height:1.6;").Append(FontInline).Append("\">");

            if (isReminder)
            {
                sb.Append("<p style=\"").Append(FontInline).Append("\">")
                  .Append("Your Late Payment Penalty Interest (LPPI) review package for ")
                  .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(monthYear)).Append("</strong>")
                  .Append(" is due for completion by <strong style=\"").Append(FontInline).Append("\">")
                  .Append(LPPIHelper.Enc(dueLong)).Append("</strong>.")
                  .Append(" The package contains payments that were made late and have incurred LPPI in accordance with ")
                  .Append("<a href=\"").Append(Rmg417Url).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:")
                  .Append(OrangeHex).Append(";").Append(FontInline).Append("\">Supplier Pay On-Time or Pay Interest Policy (RMG 417)</a>.</p>");

                sb.Append("<p style=\"").Append(FontInline).Append("\"><strong style=\"").Append(FontInline).Append("\">")
                  .Append(outstanding).Append("</strong> document").Append(outstanding == 1 ? " remains" : "s remain")
                  .Append(" outstanding.</p>");
            }
            else
            {
                sb.Append("<p style=\"").Append(FontInline).Append("\">")
                  .Append("Please see below your Late Payment Penalty Interest (LPPI) review package for ")
                  .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(monthYear)).Append("</strong>.</p>");

                sb.Append("<p style=\"").Append(FontInline).Append("\">")
                  .Append("The package contains <strong style=\"").Append(FontInline).Append("\">").Append(pocTotal).Append("</strong>")
                  .Append(" document").Append(pocTotal == 1 ? "" : "s").Append(" for payments that were made late and have incurred LPPI in accordance with ")
                  .Append("<a href=\"").Append(Rmg417Url).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:")
                  .Append(OrangeHex).Append(";").Append(FontInline).Append("\">Supplier Pay On-Time or Pay Interest Policy (RMG 417)</a>.</p>");
            }

            // Why am I receiving this
            sb.Append("<h3 style=\"font-size:15px;margin:18px 0 8px;").Append(FontInline).Append("\">Why am I receiving this email?</h3>");
            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("You were identified as the invoice Point of Contact (POC) for the late payment.</p>");

            // Action Required
            sb.Append("<h3 style=\"font-size:15px;margin:18px 0 8px;").Append(FontInline).Append("\">Action Required</h3>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("You must review each document and select the appropriate ")
              .Append("<strong style=\"").Append(FontInline).Append("\">Reason Code</strong> ")
              .Append("to indicate whether the LPPI is payable or not payable.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Please refer to the Instructions tab in the LPPI Dashboard for information about how to complete your review.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("If you are not the POC, or would like someone else to complete the LPPI review on your behalf, please forward this email to the nominated alternate POC via Outlook.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Inaction will trigger subsequent reminders to you and your AS FIN.</p>");

            AppendCallout(sb,
                "Please note: if no response is received by the due date, payment will be automatically processed from the responsible cost centre.");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Please complete your review by ")
              .Append("<strong style=\"").Append(FontInline).Append("\">").Append(LPPIHelper.Enc(dueLong)).Append("</strong>.</p>");

            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("Click the link below to begin your review.</p>");

            AppendBeginReviewButton(sb, reviewUrl);
            AppendFallbackUrl(sb, reviewUrl);

            // Best Practice — POC-only block
            sb.Append("<h3 style=\"font-size:15px;margin:18px 0 8px;").Append(FontInline).Append("\">Best Practice</h3>");
            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("To avoid the likelihood of LPPI you must process invoices within contractual payment terms. ")
              .Append("If an invoice is received before goods or services are delivered, it must be rejected through VIM (ERP). ")
              .Append("As the POC you must clearly state that goods/services have not been received, and advise the supplier to resubmit the invoice once delivery is complete.</p>");

            AppendHandyResources(sb);
            AppendSupportLine(sb);

            sb.Append("</td></tr>");
            AppendFooter(sb);
            AppendBodyClose(sb);
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Shared body chrome
        // -------------------------------------------------------------------

        private static void AppendDoctype(StringBuilder sb)
        {
            sb.Append("<!DOCTYPE html><html><head>");
            sb.Append("<meta charset=\"utf-8\" />");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.Append("<style type=\"text/css\">");
            sb.Append("body, table, td, tr, p, span, a, div, li, ul, ol, h1, h2, h3, strong, em {");
            sb.Append(" font-family: ").Append(FontStack).Append(" !important;");
            sb.Append("}");
            sb.Append("</style></head>");
        }

        private static void AppendBodyOpen(StringBuilder sb, string preheader)
        {
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
        }

        private static void AppendBodyClose(StringBuilder sb)
        {
            sb.Append("</table>");
            sb.Append("</td></tr></table></body></html>");
        }

        /// <summary>
        /// Single-cell pale-orange callout — used for the "Please note: ..."
        /// block in both templates. Keeps the visual treatment consistent.
        /// </summary>
        private static void AppendCallout(StringBuilder sb, string message)
        {
            sb.Append("<table cellspacing=\"0\" cellpadding=\"12\" style=\"width:100%;margin:14px 0;border-collapse:collapse;\">")
              .Append("<tr><td style=\"background:#fff4ea;border:1px solid #f3c89d;border-radius:6px;color:#8a4500;font-weight:bold;font-size:13px;line-height:1.5;").Append(FontInline).Append("\">")
              .Append(LPPIHelper.Enc(message))
              .Append("</td></tr></table>");
        }

        /// <summary>
        /// Big orange "Begin Review" button, centred. Outlook (Word engine)
        /// will not centre an inline-block and ignores margin:auto, so the
        /// button is wrapped in a full-width table whose cell is
        /// align="center" + text-align:center. The button itself is a fixed
        /// inner table so padding renders consistently. align attribute is
        /// duplicated as a style for non-Outlook clients to match the
        /// browser preview exactly.
        /// </summary>
        private static void AppendBeginReviewButton(StringBuilder sb, string reviewUrl)
        {
            string href = HttpUtility.HtmlAttributeEncode(reviewUrl);
            sb.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" ")
              .Append("style=\"width:100%;border-collapse:collapse;margin:18px 0;\">")
              .Append("<tr><td align=\"center\" style=\"text-align:center;").Append(FontInline).Append("\">")
              .Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" ")
              .Append("style=\"border-collapse:collapse;margin:0 auto;\">")
              .Append("<tr><td align=\"center\" bgcolor=\"").Append(OrangeHex).Append("\" ")
              .Append("style=\"background:").Append(OrangeHex).Append(";border-radius:6px;text-align:center;").Append(FontInline).Append("\">")
              .Append("<a href=\"").Append(href).Append("\" target=\"_blank\" rel=\"noopener\" ")
              .Append("style=\"display:inline-block;padding:12px 28px;color:#ffffff;font-size:14px;font-weight:bold;text-decoration:none;").Append(FontInline).Append("\">")
              .Append("Begin Review")
              .Append("</a></td></tr></table>")
              .Append("</td></tr></table>");
        }

        /// <summary>
        /// "If the button does not work" plain-URL fallback.
        /// </summary>
        private static void AppendFallbackUrl(StringBuilder sb, string reviewUrl)
        {
            string text = HttpUtility.HtmlEncode(reviewUrl);
            string href = HttpUtility.HtmlAttributeEncode(reviewUrl);
            sb.Append("<p style=\"font-size:12px;color:#555;").Append(FontInline).Append("\">")
              .Append("If the button above does not work, copy and paste this link into your browser:<br/>")
              .Append("<a href=\"").Append(href).Append("\" style=\"color:").Append(OrangeHex).Append(";word-break:break-all;").Append(FontInline).Append("\">")
              .Append(text)
              .Append("</a></p>");
        }

        /// <summary>
        /// Handy Resources section, identical text in both templates.
        /// </summary>
        private static void AppendHandyResources(StringBuilder sb)
        {
            sb.Append("<h3 style=\"font-size:15px;margin:18px 0 8px;").Append(FontInline).Append("\">Handy Resources</h3>");
            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("<a href=\"").Append(PaymentTermsIntranetUrl).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:")
              .Append(OrangeHex).Append(";").Append(FontInline).Append("\">Payment terms and prepayments guidance note</a></p>");
            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("<a href=\"").Append(PaymentTermsIbssUrl).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:")
              .Append(OrangeHex).Append(";").Append(FontInline).Append("\">Guidance Note – Payment Terms and Prepayments</a></p>");
        }

        /// <summary>
        /// Closing support / questions paragraph. Recipient sees the LPPI
        /// support mailbox as plain text + mailto.
        /// </summary>
        private static void AppendSupportLine(StringBuilder sb)
        {
            string href = "mailto:" + HttpUtility.HtmlAttributeEncode(SupportMailboxTo);
            sb.Append("<p style=\"").Append(FontInline).Append("\">")
              .Append("If you have any questions or require assistance with the review, please contact ")
              .Append("<a href=\"").Append(href).Append("\" style=\"color:").Append(OrangeHex).Append(";").Append(FontInline).Append("\">")
              .Append(HttpUtility.HtmlEncode(SupportMailboxTo))
              .Append("</a>.</p>");
        }

        private static void AppendFooter(StringBuilder sb)
        {
            sb.Append("<tr><td style=\"background:#1a1a1a;padding:16px 32px;").Append(FontInline).Append("\">")
              .Append("<div style=\"color:#999;font-size:11px;").Append(FontInline).Append("\">")
              .Append("Defence Finance Group · Late Payment Penalty Interest Review · ")
              .Append(LPPIHelper.Environment)
              .Append("</div></td></tr>");
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
        //
        // Single insert helper, called for every send (AS Fin and POC) and
        // every UAT mark-as-sent. Audience and PocEmail distinguish the
        // audience; EmailType carries the type ("Initial" / "Reminder" /
        // "Initial-MarkedSent" / "Initial-MarkedSent-Poc").
        // -------------------------------------------------------------------

        private static void LogSend(int packageId, string recipients,
                                    string emailType, string audience, string pocEmail,
                                    string subject, string body, bool success, string error)
        {
            const string sql = @"
INSERT INTO dbo.tblLPPI_EmailLog
   (PackageID, RecipientEmail, EmailType, Audience, PocEmail,
    Subject, Body, SentBy, Success, ErrorMessage)
VALUES (@P, @R, @T, @A, @PE, @S, @B, @U, @OK, @E);";
            LPPIHelper.ExecuteNonQuery(sql,
                LPPIHelper.P("@P",  packageId),
                LPPIHelper.P("@R",  recipients ?? ""),
                LPPIHelper.P("@T",  emailType),
                LPPIHelper.P("@A",  audience),
                LPPIHelper.P("@PE", (object)pocEmail ?? DBNull.Value),
                LPPIHelper.P("@S",  subject ?? ""),
                LPPIHelper.P("@B",  body    ?? ""),
                LPPIHelper.P("@U",  LPPIHelper.CurrentUserDisplayName()),
                LPPIHelper.P("@OK", success ? 1 : 0),
                LPPIHelper.P("@E",  (object)error ?? DBNull.Value));
        }

        // -------------------------------------------------------------------
        // Recipient log formatter — single TO + BCC.
        // Format: "to | BCC: bcc"   (BCC omitted when blank.)
        // CC is no longer in the recipient model.
        // -------------------------------------------------------------------
        private static string FormatRecipientsForLog(string toAddress, string bcc)
        {
            var sb = new StringBuilder();
            sb.Append(toAddress ?? "");
            if (!string.IsNullOrWhiteSpace(bcc))
                sb.Append(" | BCC: ").Append(bcc);
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Data loaders — shared between Send and Preview
        // -------------------------------------------------------------------

        // -------------------------------------------------------------------
        // Period label — derived from the data, not the due date.
        //
        // tblLPPI_Documents.ClearingMonth is stored as the BODS text form
        // "M.YYYY" (e.g. "7.2025", "12.2025", "4.2026"). The period a package
        // covers is the span from the earliest to the latest ClearingMonth
        // across that package's LIVE first-line documents.
        //
        //   - First (catch-up) file:   July 2025 to April 2026
        //   - Subsequent monthly file: a single month, e.g. May 2026
        //
        // POC-scoped callers pass a non-null pocEmail to constrain the span
        // to that POC's documents.
        //
        // Returns a display string. Falls back to the package DueDate month
        // when no ClearingMonth can be parsed for the package (defensive —
        // the real BODS extract always populates it).
        // -------------------------------------------------------------------
        private static string BuildPeriodLabel(int packageId, string pocEmail, DateTime dueFallback)
        {
            // Parse "M.YYYY" -> sortable yyyymm integer, take MIN and MAX over
            // the package's live first-line docs. POC filter applied when
            // pocEmail is supplied. The first-line correlated subquery keeps
            // this consistent with every other review query in the codebase.
            string sql = @"
SELECT MIN(ym) AS MinYm, MAX(ym) AS MaxYm
FROM (
    SELECT TRY_CONVERT(int,
             RIGHT(d.ClearingMonth, 4)) * 100
         + TRY_CONVERT(int,
             LEFT(d.ClearingMonth, CHARINDEX('.', d.ClearingMonth) - 1)) AS ym
    FROM dbo.tblLPPI_ReviewPackageDocuments rpd
    INNER JOIN dbo.tblLPPI_Documents d
            ON d.DocumentID = rpd.DocumentID
    WHERE rpd.PackageID = @P
      AND d.IsDeactivated = 0
      AND d.ClearingMonth IS NOT NULL
      AND CHARINDEX('.', d.ClearingMonth) > 1
      AND (@E IS NULL OR EXISTS (
            SELECT 1
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0
               AND d2.PocEmail        = @E))
) q
WHERE ym IS NOT NULL;";

            DataTable dt = LPPIHelper.ExecuteTable(sql,
                LPPIHelper.P("@P", packageId),
                LPPIHelper.P("@E", (object)(string.IsNullOrWhiteSpace(pocEmail) ? null : pocEmail) ?? DBNull.Value));

            int? minYm = null, maxYm = null;
            if (dt.Rows.Count == 1)
            {
                if (dt.Rows[0]["MinYm"] != DBNull.Value) minYm = Convert.ToInt32(dt.Rows[0]["MinYm"]);
                if (dt.Rows[0]["MaxYm"] != DBNull.Value) maxYm = Convert.ToInt32(dt.Rows[0]["MaxYm"]);
            }

            if (!minYm.HasValue || !maxYm.HasValue)
            {
                // Defensive fallback — never ship a blank period.
                return dueFallback.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-AU"));
            }

            string start = YmToText(minYm.Value);
            string end   = YmToText(maxYm.Value);
            return start == end ? start : (start + " to " + end);
        }

        private static string YmToText(int ym)
        {
            int year  = ym / 100;
            int month = ym % 100;
            if (month < 1 || month > 12)
                return ym.ToString(CultureInfo.InvariantCulture);
            return new DateTime(year, month, 1)
                .ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-AU"));
        }

        private static DataRow LoadPackageRow(int packageId)
        {
            const string sql = @"
SELECT p.PackageID, p.Token, p.DueDate, p.CreatedDate, p.SentDate, p.Status,
       cm.CmID, cm.Program,
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

        private static DataTable LoadPackagePocs(int packageId)
        {
            const string sql = @"
SELECT PackagePocID, PackageID, PocEmail, Token
  FROM dbo.tblLPPI_PackagePocs
 WHERE PackageID = @P
 ORDER BY PocEmail;";
            return LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@P", packageId));
        }

        private static DataRow LoadPocRow(int packageId, string pocEmail)
        {
            const string sql = @"
SELECT PackagePocID, PackageID, PocEmail, Token
  FROM dbo.tblLPPI_PackagePocs
 WHERE PackageID = @P
   AND PocEmail  = @E;";
            var dt = LPPIHelper.ExecuteTable(sql,
                LPPIHelper.P("@P", packageId),
                LPPIHelper.P("@E", pocEmail));
            return dt.Rows.Count > 0 ? dt.Rows[0] : null;
        }

        /// <summary>
        /// For a given (PackageID, PocEmail), counts the package's documents
        /// assigned to that POC and how many of those have a reason code.
        ///
        /// "Assigned to that POC" = documents in the package whose
        /// tblLPPI_Documents.PocEmail matches at the first-line level. Uses
        /// the first-line-review join pattern used elsewhere in the codebase
        /// so multi-line documents inherit one review.
        /// </summary>
        private static void ComputePocCounts(int packageId, string pocEmail,
                                             out int total, out int reviewed)
        {
            const string sql = @"
WITH PkgDocs AS (
    SELECT DISTINCT d.DocNoAccounting,
           (SELECT MIN(d2.DocumentID)
              FROM dbo.tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = d.DocNoAccounting
               AND d2.IsDeactivated   = 0) AS FirstLineDocumentID
      FROM dbo.tblLPPI_ReviewPackageDocuments pd
      INNER JOIN dbo.tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
     WHERE pd.PackageID = @P
       AND LTRIM(RTRIM(d.PocEmail)) = LTRIM(RTRIM(@E))
)
SELECT
    (SELECT COUNT(*) FROM PkgDocs) AS Total,
    (SELECT COUNT(*) FROM PkgDocs pd
       INNER JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = pd.FirstLineDocumentID
      WHERE r.ReasonCodeID IS NOT NULL) AS Reviewed;";
            var dt = LPPIHelper.ExecuteTable(sql,
                LPPIHelper.P("@P", packageId),
                LPPIHelper.P("@E", pocEmail));
            if (dt.Rows.Count == 0) { total = 0; reviewed = 0; return; }
            total    = Convert.ToInt32(dt.Rows[0]["Total"]);
            reviewed = Convert.ToInt32(dt.Rows[0]["Reviewed"]);
        }

        // -------------------------------------------------------------------
        // POC dispatch tracking — stamps InitialSentDate / LastReminderDate
        // on tblLPPI_PackagePocs after a successful per-POC dispatch.
        // -------------------------------------------------------------------

        private static void StampPocInitialSent(int packagePocId)
        {
            LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_PackagePocs
   SET InitialSentDate = SYSDATETIME()
 WHERE PackagePocID = @id
   AND InitialSentDate IS NULL;",
                LPPIHelper.P("@id", packagePocId));
        }

        private static void StampPocLastReminder(int packagePocId)
        {
            LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_PackagePocs
   SET LastReminderDate = SYSDATETIME()
 WHERE PackagePocID = @id;",
                LPPIHelper.P("@id", packagePocId));
        }

        // -------------------------------------------------------------------
        // Build a human-readable summary of POC fan-out outcomes — used as
        // the SendResult.WarningMessage so the Send-outs page can show
        // "AS Fin sent, 12 POCs sent, 1 skipped" in a single banner.
        // -------------------------------------------------------------------
        private static string BuildPocWarning(SendResult r)
        {
            if (r.PocsDispatched == 0 && r.PocsSkipped == 0 && r.PocsFailed == 0)
                return "No POCs configured for this package — AS Fin email sent. POCs may be missing in BODS data.";

            var parts = new List<string>();
            if (r.PocsDispatched > 0)
                parts.Add(r.PocsDispatched + " POC" + (r.PocsDispatched == 1 ? "" : "s") + " sent");
            if (r.PocsSkipped > 0)
                parts.Add(r.PocsSkipped + " skipped");
            if (r.PocsFailed > 0)
                parts.Add(r.PocsFailed + " failed");
            return string.Join(", ", parts) + ".";
        }

        // -------------------------------------------------------------------
        // Control notice — vendor-of-interest heads-up
        //
        // Fires when the package contains any document with VendorNum in
        // LPPI.ControlVendorNumbers AND LPPI.ControlMailboxTo is configured.
        // Feature is off when either config value is blank — short-circuit
        // returns without touching the database.
        //
        // Called from SendForPackage for Initial sends only (skipped on
        // reminders) and from MarkAsSent in simulate mode.
        // -------------------------------------------------------------------
        private static void DispatchControlIfApplicable(int packageId, string type,
                                                       string program, DateTime dueDate,
                                                       string asFinToken,
                                                       int docCount, int reviewedCount,
                                                       bool simulate, SendResult result)
        {
            string vendorList   = LPPIHelper.Setting("LPPI.ControlVendorNumbers", "");
            string controlToRaw = LPPIHelper.Setting("LPPI.ControlMailboxTo", "");
            if (string.IsNullOrWhiteSpace(vendorList) || string.IsNullOrWhiteSpace(controlToRaw))
                return; // feature off

            var vendorNums = vendorList.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (vendorNums.Count == 0) return;

            DataTable matches = LoadMatchedControlVendors(packageId, vendorNums);
            if (matches.Rows.Count == 0)
                return; // no triggering vendors in this package — nothing to do

            string subject = BuildSubjectControl(type, program);
            string body    = BuildBodyControl(type, program, dueDate, asFinToken,
                                              docCount, reviewedCount, matches);

            string controlTo = controlToRaw.Trim();
            string recipientsLogged = FormatRecipientsForLog(controlTo, SupportMailboxTo);

            if (simulate)
            {
                LogSend(packageId, recipientsLogged, "Initial-MarkedSent", AudControl, null,
                    subject,
                    "(no body — marked as sent in test mode, no email dispatched)",
                    true,
                    "MARK-AS-SENT (test mode) — no control notice dispatched. ProductionMode=false.");
                result.ControlDispatched = 1;
                return;
            }

            string error;
            bool ok = SendOne(
                fromAddress:  LPPIHelper.Setting("LPPI.MailFrom", "noreply@defence.gov.au"),
                fromName:     LPPIHelper.Setting("LPPI.MailFromName", "LPPI Review"),
                toAddress:    controlTo,
                bccAddress:   SupportMailboxTo,
                subject:      subject,
                htmlBody:     body,
                error:        out error);

            LogSend(packageId, recipientsLogged, type, AudControl, null,
                    subject, body, ok, error);

            if (ok) result.ControlDispatched = 1;
            else    result.ControlFailed     = 1;
        }

        /// <summary>
        /// Returns distinct (VendorNum, VendorName, LineCount) for documents
        /// in the package whose VendorNum matches the configured trigger list.
        /// Exact match, trimmed.
        /// </summary>
        private static DataTable LoadMatchedControlVendors(int packageId, List<string> vendorNums)
        {
            var sb = new StringBuilder();
            sb.Append(@"
SELECT LTRIM(RTRIM(d.VendorNum)) AS VendorNum,
       MAX(d.VendorName)         AS VendorName,
       COUNT(*)                  AS LineCount
FROM dbo.tblLPPI_Documents d
INNER JOIN dbo.tblLPPI_ReviewPackageDocuments rpd
        ON rpd.DocumentID = d.DocumentID
WHERE rpd.PackageID = @P
  AND d.VendorNum IS NOT NULL
  AND LTRIM(RTRIM(d.VendorNum)) IN (");

            var paramList = new List<OleDbParameter>();
            paramList.Add(LPPIHelper.P("@P", packageId));
            for (int i = 0; i < vendorNums.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                string pname = "@V" + i;
                sb.Append(pname);
                paramList.Add(LPPIHelper.P(pname, vendorNums[i]));
            }
            sb.Append(@")
GROUP BY LTRIM(RTRIM(d.VendorNum))
ORDER BY VendorName;");

            return LPPIHelper.ExecuteTable(sb.ToString(), paramList.ToArray());
        }

        private static string BuildSubjectControl(string type, string program)
        {
            return "[CONTROL] LPPI vendor-of-interest sighting — " + program;
        }

        private static string BuildBodyControl(string type, string program, DateTime dueDate,
                                               string asFinToken,
                                               int docCount, int reviewedCount,
                                               DataTable matches)
        {
            string reviewUrl = BuildReviewUrl(asFinToken);
            var auCulture    = CultureInfo.GetCultureInfo("en-AU");
            string dueLong   = dueDate.ToString("dddd, d MMMM yyyy", auCulture);
            int outstanding  = Math.Max(0, docCount - reviewedCount);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>");
            sb.Append("body, p, td, th, li, div, span, a { font-family: ").Append(FontStack).Append("; }");
            sb.Append("</style></head><body style=\"margin:0;padding:0;background:#f4f4f4;").Append(FontInline).Append("\">");

            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"background:#f4f4f4;").Append(FontInline).Append("\"><tr><td align=\"center\" style=\"padding:24px;").Append(FontInline).Append("\">");
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"640\" style=\"background:#ffffff;border:1px solid #e3e3e3;").Append(FontInline).Append("\">");

            // Header band.
            sb.Append("<tr><td style=\"background:").Append(OrangeHex).Append(";color:#ffffff;padding:16px 20px;").Append(FontInline).Append("\">");
            sb.Append("<div style=\"font-size:18px;font-weight:600;").Append(FontInline).Append("\">LPPI vendor-of-interest sighting</div>");
            sb.Append("<div style=\"font-size:13px;opacity:0.95;").Append(FontInline).Append("\">Programme: ").Append(LPPIHelper.Enc(program)).Append("</div>");
            sb.Append("</td></tr>");

            // Body.
            sb.Append("<tr><td style=\"padding:20px;color:#222;font-size:14px;line-height:1.5;").Append(FontInline).Append("\">");
            sb.Append("<p style=\"margin:0 0 12px 0;").Append(FontInline).Append("\">An LPPI review package has been issued that contains documents from a vendor on the control-sighting list. This notice is a heads-up so the contract manager team can spot-check the package.</p>");
            sb.Append("<p style=\"margin:0 0 12px 0;").Append(FontInline).Append("\">AS Fin and the document POCs are running the substantive review — this is an extra sighting only, recorded for governance.</p>");

            // Matched vendors block.
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"margin:12px 0;border-collapse:collapse;").Append(FontInline).Append("\">");
            sb.Append("<tr><th align=\"left\" style=\"border-bottom:2px solid #e3e3e3;padding:6px 8px;font-size:13px;").Append(FontInline).Append("\">Vendor number</th>");
            sb.Append("<th align=\"left\" style=\"border-bottom:2px solid #e3e3e3;padding:6px 8px;font-size:13px;").Append(FontInline).Append("\">Vendor name</th>");
            sb.Append("<th align=\"right\" style=\"border-bottom:2px solid #e3e3e3;padding:6px 8px;font-size:13px;").Append(FontInline).Append("\">Lines</th></tr>");
            foreach (DataRow r in matches.Rows)
            {
                sb.Append("<tr><td style=\"border-bottom:1px solid #f0f0f0;padding:6px 8px;font-size:13px;").Append(FontInline).Append("\">")
                  .Append(LPPIHelper.Enc(r["VendorNum"])).Append("</td>");
                sb.Append("<td style=\"border-bottom:1px solid #f0f0f0;padding:6px 8px;font-size:13px;").Append(FontInline).Append("\">")
                  .Append(LPPIHelper.Enc(r["VendorName"])).Append("</td>");
                sb.Append("<td align=\"right\" style=\"border-bottom:1px solid #f0f0f0;padding:6px 8px;font-size:13px;").Append(FontInline).Append("\">")
                  .Append(LPPIHelper.Enc(r["LineCount"])).Append("</td></tr>");
            }
            sb.Append("</table>");

            // Package summary.
            sb.Append("<p style=\"margin:12px 0 6px 0;font-weight:600;").Append(FontInline).Append("\">Package summary</p>");
            sb.Append("<ul style=\"margin:0 0 12px 18px;padding:0;").Append(FontInline).Append("\">");
            sb.Append("<li style=\"").Append(FontInline).Append("\">Documents in package: ").Append(docCount).Append("</li>");
            sb.Append("<li style=\"").Append(FontInline).Append("\">Outstanding (not yet reviewed): ").Append(outstanding).Append("</li>");
            sb.Append("<li style=\"").Append(FontInline).Append("\">Due date: ").Append(LPPIHelper.Enc(dueLong)).Append("</li>");
            sb.Append("</ul>");

            // Link.
            sb.Append("<p style=\"margin:16px 0;").Append(FontInline).Append("\"><a href=\"").Append(HttpUtility.HtmlAttributeEncode(reviewUrl)).Append("\" style=\"background:").Append(OrangeHex).Append(";color:#ffffff;padding:10px 16px;text-decoration:none;display:inline-block;border-radius:4px;").Append(FontInline).Append("\">Open package in LPPI Review</a></p>");
            sb.Append("<p style=\"margin:0;font-size:12px;color:#666;").Append(FontInline).Append("\">This link opens the full package via the AS Fin token.</p>");

            sb.Append("</td></tr>");

            // Footer band.
            sb.Append("<tr><td style=\"background:#fafafa;border-top:1px solid #e3e3e3;padding:12px 20px;font-size:12px;color:#666;").Append(FontInline).Append("\">");
            sb.Append("LPPI Review — automated control notice. Reply to this email or contact the LPPI administrator if anything looks off.");
            sb.Append("</td></tr>");

            sb.Append("</table></td></tr></table></body></html>");
            return sb.ToString();
        }
    }
}
