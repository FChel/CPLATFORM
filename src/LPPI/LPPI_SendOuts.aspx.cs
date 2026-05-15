using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Send-outs page. Lists packages in flight (NotSent / Sent / InReview /
    /// Finalised) and lets the operator issue them or send reminders.
    /// Packages are NOT created here — they are created by the file-load
    /// reconcile step.
    ///
    /// May 2026 — POC fan-out
    /// -------------------------------------------------------------------
    /// Each Send / Reminder click now dispatches:
    ///   1. AS Fin email (group summary, sent to the CM team mailbox)
    ///   2. One per-POC email per distinct invoice POC in the package
    /// LPPIEmail.SendResult carries the per-package fan-out outcome
    /// (PocsDispatched / PocsSkipped / PocsFailed + WarningMessage), which
    /// is surfaced in the per-package result line.
    ///
    /// The "AS Fin recipient" column shows the single CM email + display
    /// name configured on tblLPPI_CapabilityManagers, or a "Not configured"
    /// pill when the CM has no email yet. The picker checkbox is gated on
    /// EmailConfigured, not on POC count — empty-POC packages can still be
    /// sent (AS Fin gets the group summary; the send pipeline reports the
    /// missing fan-out as a warning).
    ///
    /// Lifecycle visibility:
    ///   - NotSent / Sent / InReview show in Open packages and are
    ///     actionable (can be sent / reminded).
    ///   - Finalised shows in Open packages too, as a read-only row —
    ///     useful for visibility ("which CMs have finished") even though
    ///     no email action is meaningful any more. Reminders refuse on
    ///     Finalised packages (status guard in LPPIEmail). Finalisation
    ///     is self-service on the reviewer page; there is no Unfinalise
    ///     button here.
    ///   - Exported / Cancelled are out of scope and only surface on the
    ///     dashboard / batches page.
    ///
    /// In test mode (LPPIEmail.ProductionMode = false), a "Mark as sent"
    /// button is visible. It performs the same status transition as a real
    /// initial send (NotSent -> Sent, stamps SentDate) plus per-audience
    /// audit rows (one for AS Fin, one per POC) without dispatching any
    /// email. The two buttons are mutually exclusive: in PROD only Send is
    /// enabled, in test mode only Mark as sent is. The single
    /// LPPIEmail.ProductionMode flag drives both.
    /// </summary>
    public partial class LPPI_SendOuts : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            BindUnconfigured();

            if (!IsPostBack)
            {
                txtDueDate.Text = DateTime.Today.AddDays(LPPIHelper.DefaultDueDays).ToString("yyyy-MM-dd");
                BindPackages();
                BindRecent();
            }

            // Mutually exclusive button visibility, gated on ProductionMode:
            //   PROD: btnSend enabled, btnMarkSent hidden.
            //   Test: btnSend disabled (kept visible so its presence is obvious),
            //         btnMarkSent visible and enabled.
            btnSend.Enabled    = LPPIEmail.ProductionMode;
            btnMarkSent.Visible = !LPPIEmail.ProductionMode;

            RenderUatBanner();
        }

        // -------------------------------------------------------------------
        // Test-mode banner
        // -------------------------------------------------------------------

        private void RenderUatBanner()
        {
            phUatBanner.Controls.Clear();
            if (LPPIEmail.ProductionMode) return;

            phUatBanner.Controls.Add(new LiteralControl(
                "<div class=\"alert alert-warn\">" +
                "<div><strong>Test mode.</strong> Real email sending is disabled. " +
                "Use <em>Preview AS Fin</em> or <em>Preview POC</em> to see the formatted email and " +
                "<em>Mark as sent (test)</em> to simulate email sending " +
                "and set package(s) status to [Sent]." +
                "</div>" +
                "</div>"));
        }

        // -------------------------------------------------------------------
        // Unconfigured-CM warning
        //
        // Driven from LPPIHelper.GetUnconfiguredPrograms() which now reads
        // the new single-email model on tblLPPI_CapabilityManagers.
        // -------------------------------------------------------------------

        private void BindUnconfigured()
        {
            phUnconfigured.Controls.Clear();

            var unconfigured = LPPIHelper.GetUnconfiguredPrograms();
            if (unconfigured.Count == 0) return;

            var msg = "<div class=\"alert alert-warn\"><div><strong>" + unconfigured.Count +
                      " Capability Manager program" + (unconfigured.Count == 1 ? "" : "s") +
                      "</strong> in your loaded data have no AS Fin email configured. " +
                      "You will not be able to send these out for review until they are added.<br/>" +
                      "Missing: " + string.Join(", ", unconfigured.Select(p => "<code>" + System.Web.HttpUtility.HtmlEncode(p) + "</code>")) +
                      " &nbsp; <a href=\"LPPI_CapabilityManagers.aspx\">Configure now &rarr;</a></div></div>";
            phUnconfigured.Controls.Add(new LiteralControl(msg));
        }

        // -------------------------------------------------------------------
        // Data binding — open packages table
        //
        // Scope expanded to include Finalised so users can see which
        // packages have closed off without having to switch to the
        // dashboard. Finalised rows are visually distinct (green pill) and
        // their checkbox is suppressed in the markup since they have no
        // valid send/remind action.
        //
        // May 2026 — single CM email + POC count.
        //   Replaces the legacy join to tblLPPI_CapabilityManagerEmails
        //   (dropped) with a direct projection of cm.Email and
        //   cm.EmailDisplayName, plus an EmailConfigured bit derived in SQL
        //   so the markup can data-bind to it via Eval("EmailConfigured").
        //   Also surfaces PocCount from tblLPPI_PackagePocs so the operator
        //   sees the fan-out scope at a glance.
        // -------------------------------------------------------------------

        private void BindPackages()
        {
            // Columns required by rptPackages Eval():
            //   PackageID, Token, Program, Status, DueDate, SentDate,
            //   Email, EmailDisplayName, EmailConfigured (bit),
            //   PocCount, DocCount, ReviewedCount, LastEmailDate
            const string sql = @"
                SELECT p.PackageID,
                       p.Token,
                       p.Status,
                       p.DueDate,
                       p.SentDate,
                       cm.CmID,
                       cm.Program,
                       cm.Email,
                       cm.EmailDisplayName,
                       CASE
                           WHEN cm.Email             IS NOT NULL AND LEN(LTRIM(RTRIM(cm.Email))) > 0
                            AND cm.EmailDisplayName IS NOT NULL AND LEN(LTRIM(RTRIM(cm.EmailDisplayName))) > 0
                           THEN CAST(1 AS BIT)
                           ELSE CAST(0 AS BIT)
                       END AS EmailConfigured,
                       (SELECT COUNT(*)
                          FROM tblLPPI_PackagePocs pp
                         WHERE pp.PackageID = p.PackageID) AS PocCount,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         WHERE pd.PackageID = p.PackageID) AS DocCount,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                         WHERE pd.PackageID = p.PackageID
                           AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount,
                       (SELECT MAX(el.SentDate)
                          FROM tblLPPI_EmailLog el
                         WHERE el.PackageID = p.PackageID) AS LastEmailDate
                  FROM tblLPPI_ReviewPackages p
                 INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                 WHERE p.Status IN ('NotSent','Sent','InReview','Finalised')
                 ORDER BY cm.Program, p.PackageID";

            DataTable dt = LPPIHelper.ExecuteTable(sql);
            rptPackages.DataSource = dt;
            rptPackages.DataBind();
            phNoPackages.Visible = dt.Rows.Count == 0;
        }

        private void BindRecent()
        {
            // Columns required by rptRecent Eval():
            //   PackageID, Token, Program, CreatedDate, DueDate,
            //   TotalDocs, ReviewedDocs, Status, LastEmailDate
            //
            // Filtered to packages that have actually been sent so this
            // table only shows real send-out history, not pre-launch
            // NotSent packages.
            const string sql = @"
                SELECT TOP 50
                       p.PackageID,
                       p.Token,
                       cm.Program,
                       p.CreatedDate,
                       p.DueDate,
                       p.Status,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         WHERE pd.PackageID = p.PackageID) AS TotalDocs,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                         WHERE pd.PackageID = p.PackageID
                           AND r.ReasonCodeID IS NOT NULL) AS ReviewedDocs,
                       (SELECT MAX(el.SentDate)
                          FROM tblLPPI_EmailLog el
                         WHERE el.PackageID = p.PackageID) AS LastEmailDate
                  FROM tblLPPI_ReviewPackages p
                 INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                 WHERE p.SentDate IS NOT NULL
                 ORDER BY (SELECT MAX(el.SentDate)
                            FROM tblLPPI_EmailLog el
                           WHERE el.PackageID = p.PackageID) DESC,
                          p.PackageID DESC";
            rptRecent.DataSource = LPPIHelper.ExecuteTable(sql);
            rptRecent.DataBind();
        }

        // -------------------------------------------------------------------
        // Render helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Status pill for the Open packages table. Uses package status as
        /// authoritative, but augments with overdue/due-soon for active
        /// statuses (Sent / InReview only — NotSent is yet to be sent so
        /// the due date isn't relevant; Finalised is closed off, no
        /// "overdue" concern).
        /// </summary>
        protected string RenderStatusPill(object dataItem)
        {
            var row    = (DataRowView)dataItem;
            var status = Convert.ToString(row["Status"]);
            var due    = row["DueDate"] == DBNull.Value ? DateTime.MaxValue : Convert.ToDateTime(row["DueDate"]);

            // Overdue / due-soon augmentation only applies to Sent / InReview.
            bool active = string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase);

            string statusLabel;
            string statusClass;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":   statusLabel = "Not sent";   statusClass = "notsent";   break;
                case "sent":      statusLabel = "Sent";       statusClass = "sent";      break;
                case "inreview":  statusLabel = "In review";  statusClass = "inreview";  break;
                case "finalised": statusLabel = "Finalised";  statusClass = "finalised"; break;
                case "exported":  statusLabel = "Exported";   statusClass = "exported";  break;
                case "cancelled": statusLabel = "Cancelled";  statusClass = "cancelled"; break;
                default:          statusLabel = status;       statusClass = "";          break;
            }

            var sb = new StringBuilder();
            sb.AppendFormat("<span class=\"pill {0}\">{1}</span>", statusClass, LPPIHelper.Enc(statusLabel));

            if (active && due < DateTime.Today)
            {
                sb.Append(" <span class=\"pill overdue\">Overdue</span>");
            }
            else if (active && due <= DateTime.Today.AddDays(LPPIHelper.ReminderWindowDays))
            {
                sb.Append(" <span class=\"pill duesoon\">Due soon</span>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Status pill that takes a raw status value (used by Recent send-outs
        /// table where overdue augmentation is not needed).
        /// </summary>
        protected string RenderStatusPillFromStatus(object statusObj)
        {
            string status = statusObj == null || statusObj == DBNull.Value
                          ? "" : Convert.ToString(statusObj);
            string label;
            string cls;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":   label = "Not sent";  cls = "notsent";   break;
                case "sent":      label = "Sent";      cls = "sent";      break;
                case "inreview":  label = "In review"; cls = "inreview";  break;
                case "finalised": label = "Finalised"; cls = "finalised"; break;
                case "exported":  label = "Exported";  cls = "exported";  break;
                case "cancelled": label = "Cancelled"; cls = "cancelled"; break;
                default:          label = status;     cls = "";          break;
            }
            return string.Format("<span class=\"pill {0}\">{1}</span>", cls, LPPIHelper.Enc(label));
        }

        /// <summary>
        /// AS Fin recipient cell. Shows the single CM email and display name
        /// when configured, or a "Not configured" pill linking to the
        /// Capability Managers page when blank.
        /// </summary>
        protected string RenderRecipientCell(object dataItem)
        {
            var row = (DataRowView)dataItem;
            bool configured = row["EmailConfigured"] != DBNull.Value
                              && Convert.ToBoolean(row["EmailConfigured"]);

            if (!configured)
            {
                return "<a href=\"LPPI_CapabilityManagers.aspx\" class=\"pill-not-configured\" " +
                       "title=\"Click to configure\">Not configured</a>";
            }

            string email = row["Email"]             == DBNull.Value ? "" : Convert.ToString(row["Email"]);
            string name  = row["EmailDisplayName"] == DBNull.Value ? "" : Convert.ToString(row["EmailDisplayName"]);

            var sb = new StringBuilder();
            sb.Append("<div class=\"recipient-cell\">");
            sb.Append("<div class=\"recipient-email\">").Append(LPPIHelper.Enc(email)).Append("</div>");
            if (name.Length > 0)
                sb.Append("<div class=\"recipient-name\">").Append(LPPIHelper.Enc(name)).Append("</div>");
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Actions cell on the Open packages table. Every package gets an
        /// "Open review" link (admin QA / visibility into Finalised packages).
        /// Preview buttons (AS Fin and POC) are offered for any non-terminal
        /// package — pointless on Finalised since the email cycle is over,
        /// so we suppress them there to reduce clutter.
        /// </summary>
        protected string RenderPackageActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int    packageId = Convert.ToInt32(packageIdObj);
            string status    = statusObj != null && statusObj != DBNull.Value
                               ? Convert.ToString(statusObj) : "";

            var sb = new StringBuilder();

            // Open review link — admin QA. Available for any package with a token.
            if (tokenObj != null && tokenObj != DBNull.Value)
            {
                string token   = LPPIHelper.Enc(tokenObj);
                string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                    "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button> ",
                    token, baseUrl);
            }

            // Preview buttons — useful while the email cycle is still
            // relevant. Suppressed on Finalised since reminders have no
            // meaning at that point. POC preview uses placeholder values
            // (handled in LPPIEmail.BuildEmailHtml) so no per-POC selection
            // is needed at the UI level.
            bool isFinalised = string.Equals(status, "Finalised", StringComparison.OrdinalIgnoreCase);
            if (!isFinalised)
            {
                string emailType = string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase)
                                   ? "Initial" : "Reminder";
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                    "onclick=\"openPreview({0},'{1}','asfin')\">Preview AS Fin</button> ",
                    packageId, emailType);
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                    "onclick=\"openPreview({0},'{1}','poc')\">Preview POC</button>",
                    packageId, emailType);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Actions column for rptRecent rows — same model: review link
        /// available for any package, previews suppressed on terminal states.
        /// </summary>
        protected string RenderRecentActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int    packageId = Convert.ToInt32(packageIdObj);
            string status    = statusObj != null && statusObj != DBNull.Value
                               ? Convert.ToString(statusObj) : "";

            var sb = new StringBuilder();

            if (tokenObj != null && tokenObj != DBNull.Value)
            {
                string token   = LPPIHelper.Enc(tokenObj);
                string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                    "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button> ",
                    token, baseUrl);
            }

            bool isFinalised = string.Equals(status, "Finalised", StringComparison.OrdinalIgnoreCase);
            bool isExported  = string.Equals(status, "Exported",  StringComparison.OrdinalIgnoreCase);
            bool isCancelled = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            if (!isFinalised && !isExported && !isCancelled)
            {
                string emailType = string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase)
                                   ? "Initial" : "Reminder";
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                    "onclick=\"openPreview({0},'{1}','asfin')\">Preview AS Fin</button> ",
                    packageId, emailType);
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                    "onclick=\"openPreview({0},'{1}','poc')\">Preview POC</button>",
                    packageId, emailType);
            }

            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Selection helper — shared between Send and Mark-as-sent.
        //
        // The picker only enables the checkbox on rows whose status is one
        // of NotSent / Sent / InReview AND whose CM has email configured
        // (markup gates this via Eval). Even so, the server re-checks at
        // action time so a Finalised package racing through cannot get sent.
        // -------------------------------------------------------------------

        private List<int> CollectSelectedPackageIds()
        {
            var ids = new List<int>();
            foreach (RepeaterItem item in rptPackages.Items)
            {
                // chkPick is a plain HTML checkbox with runat=server (not
                // asp:CheckBox), so it materialises as HtmlInputCheckBox.
                // See the comment in the .aspx for the JS-class-on-input
                // reason behind this choice.
                var chk = item.FindControl("chkPick") as System.Web.UI.HtmlControls.HtmlInputCheckBox;
                var hf  = item.FindControl("hfPackageId") as HiddenField;
                if (chk != null && hf != null && chk.Checked)
                {
                    int id;
                    if (int.TryParse(hf.Value, out id)) ids.Add(id);
                }
            }
            return ids;
        }

        // -------------------------------------------------------------------
        // Send / remind selected packages — real send (PROD only)
        //
        // Each LPPIEmail.SendInitial / SendReminder call dispatches to two
        // audiences (AS Fin + per-POC). The SendResult carries the per-package
        // outcome:
        //   Success         — true when the AS Fin send succeeded
        //   ErrorMessage    — populated when Success is false
        //   WarningMessage  — non-fatal note about the POC fan-out (e.g.
        //                     "12 POCs sent, 1 skipped" or "No POCs configured")
        //   PocsDispatched / PocsSkipped / PocsFailed — counts used to build
        //                     the per-package result line below.
        //
        // Per-POC failures do NOT roll the overall result to failure — AS Fin
        // still has visibility via the reviewer page, and the per-POC issues
        // are surfaced as a warning so the operator can chase them up.
        // -------------------------------------------------------------------

        protected void btnSend_Click(object sender, EventArgs e)
        {
            if (!LPPIEmail.ProductionMode)
            {
                ShowMessage("Email sending is disabled in test mode. Use Mark as sent (test) instead, or set LPPI.ProductionMode = true in web.config.", "err");
                return;
            }

            DateTime due;
            if (!DateTime.TryParse(txtDueDate.Text, out due))
            {
                ShowMessage("A valid due date is required.", "err");
                return;
            }

            var selectedPackageIds = CollectSelectedPackageIds();
            if (selectedPackageIds.Count == 0)
            {
                ShowMessage("Select at least one package to send.", "err");
                return;
            }

            int initialOk = 0, reminderOk = 0, failed = 0;
            int totalPocsDispatched = 0, totalPocsSkipped = 0, totalPocsFailed = 0;
            int totalControlDispatched = 0, totalControlFailed = 0;
            var perPackageNotes = new StringBuilder();

            foreach (int pid in selectedPackageIds)
            {
                // Look up current status to decide initial vs reminder, and to
                // apply the user-specified due date for first sends only.
                object statusObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM tblLPPI_ReviewPackages WHERE PackageID = @P",
                    LPPIHelper.P("@P", pid));
                string status = statusObj == null || statusObj == DBNull.Value
                              ? "" : Convert.ToString(statusObj);

                if (string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase))
                {
                    LPPIHelper.ExecuteNonQuery(
                        "UPDATE tblLPPI_ReviewPackages SET DueDate = @D WHERE PackageID = @P AND Status = 'NotSent'",
                        LPPIHelper.P("@D", due),
                        LPPIHelper.P("@P", pid));

                    var res = LPPIEmail.SendInitial(pid);
                    AccumulateResult(res, perPackageNotes, pid, "initial",
                        ref initialOk, ref failed,
                        ref totalPocsDispatched, ref totalPocsSkipped, ref totalPocsFailed,
                        ref totalControlDispatched, ref totalControlFailed);
                }
                else if (string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase))
                {
                    var res = LPPIEmail.SendReminder(pid);
                    AccumulateResult(res, perPackageNotes, pid, "reminder",
                        ref reminderOk, ref failed,
                        ref totalPocsDispatched, ref totalPocsSkipped, ref totalPocsFailed,
                        ref totalControlDispatched, ref totalControlFailed);
                }
                else
                {
                    failed++;
                    perPackageNotes.Append("<li>Package #").Append(pid)
                                    .Append(": skipped — status is ").Append(LPPIHelper.Enc(status))
                                    .Append(", which is not eligible for send/remind.</li>");
                }
            }

            string kind = (failed == 0) ? "ok" : "warn";
            var msg = new StringBuilder();
            msg.Append(initialOk).Append(" initial email").Append(initialOk == 1 ? "" : "s")
               .Append(" sent, ")
               .Append(reminderOk).Append(" reminder").Append(reminderOk == 1 ? "" : "s")
               .Append(" sent.");
            if (totalPocsDispatched + totalPocsSkipped + totalPocsFailed > 0)
            {
                msg.Append(" POC fan-out: ")
                   .Append(totalPocsDispatched).Append(" sent");
                if (totalPocsSkipped > 0) msg.Append(", ").Append(totalPocsSkipped).Append(" skipped");
                if (totalPocsFailed  > 0) msg.Append(", ").Append(totalPocsFailed).Append(" failed");
                msg.Append(".");
            }
            if (totalControlDispatched + totalControlFailed > 0)
            {
                msg.Append(" Control notice: ")
                   .Append(totalControlDispatched).Append(" sent");
                if (totalControlFailed > 0) msg.Append(", ").Append(totalControlFailed).Append(" failed");
                msg.Append(".");
            }
            if (failed > 0)
                msg.Append(" ").Append(failed).Append(" package failure").Append(failed == 1 ? "" : "s").Append(".");
            if (perPackageNotes.Length > 0)
                msg.Append("<ul>").Append(perPackageNotes).Append("</ul>");

            ShowMessageRaw(msg.ToString(), kind);
            BindPackages();
            BindRecent();
        }

        /// <summary>
        /// Folds one SendResult into the running totals and per-package note
        /// list. Shared between btnSend_Click's initial and reminder branches.
        /// </summary>
        private void AccumulateResult(LPPIEmail.SendResult res, StringBuilder perPackageNotes,
                                      int pid, string label,
                                      ref int okCounter, ref int failed,
                                      ref int totalPocsDispatched, ref int totalPocsSkipped, ref int totalPocsFailed,
                                      ref int totalControlDispatched, ref int totalControlFailed)
        {
            totalPocsDispatched    += res.PocsDispatched;
            totalPocsSkipped       += res.PocsSkipped;
            totalPocsFailed        += res.PocsFailed;
            totalControlDispatched += res.ControlDispatched;
            totalControlFailed     += res.ControlFailed;

            if (res.Success)
            {
                okCounter++;
                if (!string.IsNullOrEmpty(res.WarningMessage))
                {
                    perPackageNotes.Append("<li>Package #").Append(pid)
                                    .Append(" (").Append(label).Append("): ")
                                    .Append(LPPIHelper.Enc(res.WarningMessage)).Append("</li>");
                }
            }
            else
            {
                failed++;
                perPackageNotes.Append("<li>Package #").Append(pid)
                                .Append(" (").Append(label).Append("): ")
                                .Append(LPPIHelper.Enc(res.ErrorMessage ?? "(unknown error)"))
                                .Append("</li>");
            }
        }

        // -------------------------------------------------------------------
        // Mark as sent (test mode only) — drive the lifecycle without sending email
        // -------------------------------------------------------------------

        protected void btnMarkSent_Click(object sender, EventArgs e)
        {
            // Defence in depth — the button is hidden in PROD via Visible,
            // but a server-side gate protects against any direct postback.
            if (LPPIEmail.ProductionMode)
            {
                ShowMessage("Mark as sent is not available in production. Use Send / remind selected.", "err");
                return;
            }

            DateTime due;
            if (!DateTime.TryParse(txtDueDate.Text, out due))
            {
                ShowMessage("A valid due date is required.", "err");
                return;
            }

            var selectedPackageIds = CollectSelectedPackageIds();
            if (selectedPackageIds.Count == 0)
            {
                ShowMessage("Select at least one package to mark as sent.", "err");
                return;
            }

            int markedOk = 0, skipped = 0, failed = 0;
            int totalPocsDispatched = 0, totalPocsSkipped = 0, totalPocsFailed = 0;
            int totalControlDispatched = 0, totalControlFailed = 0;
            var perPackageNotes = new StringBuilder();

            foreach (int pid in selectedPackageIds)
            {
                // Mark as sent only operates on NotSent packages — anything
                // else is skipped with a clear message rather than failing.
                object statusObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM tblLPPI_ReviewPackages WHERE PackageID = @P",
                    LPPIHelper.P("@P", pid));
                string status = statusObj == null || statusObj == DBNull.Value
                              ? "" : Convert.ToString(statusObj);

                if (!string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    perPackageNotes.Append("<li>Package #").Append(pid)
                                    .Append(": skipped — already ").Append(LPPIHelper.Enc(status)).Append(".</li>");
                    continue;
                }

                // Apply the chosen due date before marking — same as the real
                // send. Mark-as-sent is the operator's last chance to set the
                // due date in test mode.
                LPPIHelper.ExecuteNonQuery(
                    "UPDATE tblLPPI_ReviewPackages SET DueDate = @D WHERE PackageID = @P AND Status = 'NotSent'",
                    LPPIHelper.P("@D", due),
                    LPPIHelper.P("@P", pid));

                var res = LPPIEmail.MarkAsSent(pid);

                totalPocsDispatched    += res.PocsDispatched;
                totalPocsSkipped       += res.PocsSkipped;
                totalPocsFailed        += res.PocsFailed;
                totalControlDispatched += res.ControlDispatched;
                totalControlFailed     += res.ControlFailed;

                if (res.Success)
                {
                    markedOk++;
                    if (!string.IsNullOrEmpty(res.WarningMessage))
                    {
                        perPackageNotes.Append("<li>Package #").Append(pid).Append(": ")
                                        .Append(LPPIHelper.Enc(res.WarningMessage)).Append("</li>");
                    }
                }
                else
                {
                    failed++;
                    perPackageNotes.Append("<li>Package #").Append(pid).Append(": ")
                                    .Append(LPPIHelper.Enc(res.ErrorMessage ?? "(unknown error)"))
                                    .Append("</li>");
                }
            }

            string kind = (failed == 0 && skipped == 0) ? "ok" : "warn";
            var msg = new StringBuilder();
            msg.Append(markedOk).Append(" package").Append(markedOk == 1 ? "" : "s")
               .Append(" marked as sent (test mode — no email dispatched).");
            if (totalPocsDispatched + totalPocsSkipped + totalPocsFailed > 0)
            {
                msg.Append(" Simulated POC fan-out: ")
                   .Append(totalPocsDispatched).Append(" logged");
                if (totalPocsSkipped > 0) msg.Append(", ").Append(totalPocsSkipped).Append(" skipped");
                if (totalPocsFailed  > 0) msg.Append(", ").Append(totalPocsFailed).Append(" failed");
                msg.Append(".");
            }
            if (totalControlDispatched + totalControlFailed > 0)
            {
                msg.Append(" Simulated control notice: ")
                   .Append(totalControlDispatched).Append(" logged");
                if (totalControlFailed > 0) msg.Append(", ").Append(totalControlFailed).Append(" failed");
                msg.Append(".");
            }
            if (skipped > 0)
                msg.Append(" ").Append(skipped).Append(" not NotSent.");
            if (failed > 0)
                msg.Append(" ").Append(failed).Append(" failure").Append(failed == 1 ? "" : "s").Append(".");
            if (perPackageNotes.Length > 0)
                msg.Append("<ul>").Append(perPackageNotes).Append("</ul>");

            ShowMessageRaw(msg.ToString(), kind);
            BindPackages();
            BindRecent();
        }

        // -------------------------------------------------------------------
        // Message helpers
        // -------------------------------------------------------------------

        private void ShowMessage(string text, string kind)
        {
            ShowMessageRaw(System.Web.HttpUtility.HtmlEncode(text), kind);
        }

        private void ShowMessageRaw(string html, string kind)
        {
            phMessage.Controls.Clear();
            phMessage.Controls.Add(new LiteralControl(
                "<div class=\"alert alert-" + kind + "\">" + html + "</div>"));
        }
    }
}
