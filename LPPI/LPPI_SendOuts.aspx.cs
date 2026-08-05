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
    /// Finalised) and lets the operator issue them, send reminders, or
    /// notify the responsible AS Fin officer when finalised.
    ///
    /// Packages are NOT created here — they are created by the file-load
    /// reconcile step.
    ///
    /// POC fan-out
    /// -------------------------------------------------------------------
    /// Each Send / Reminder click dispatches:
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
    ///     useful for visibility ("which CMs have finished"). The
    ///     "Notify AS Fin" button is rendered on Finalised rows; it sends
    ///     a courtesy summary email to a typed recipient with the CM team
    ///     mailbox on CC and LPPI support on BCC. Reminders refuse on
    ///     Finalised packages. Finalisation is self-service on the
    ///     reviewer page; there is no Unfinalise button here.
    ///   - Exported / Cancelled are out of scope and only surface on the
    ///     dashboard / batches page.
    ///
    /// In test mode (LPPIEmail.ProductionMode = false), a "Mark as sent /
    /// remind" button is visible. It branches by current package status:
    /// NotSent packages get the same treatment as a real initial send
    /// (NotSent -> Sent, due date stamped) and Sent / InReview packages
    /// get a simulated reminder (no status change). Either way, per-
    /// audience audit rows are written and no SMTP is touched. The two
    /// buttons are mutually exclusive: in PROD only Send is enabled, in
    /// test mode only Mark as sent / remind is. The single
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
                "Use <em>Preview AS Fin</em> or <em>Preview POC</em> to see the formatted email. " +
                "<em>Mark as sent / remind (test)</em> simulates the dispatch: NotSent packages " +
                "transition to Sent (and stamp the due date); Sent / InReview packages get a " +
                "simulated reminder. Either way, no email is dispatched and per-audience audit " +
                "rows are written so the end-to-end flow can be exercised. The <em>Notify AS Fin</em> " +
                "button on Finalised rows is disabled in test mode — it is a real-send-only action " +
                "with no lifecycle side effects to simulate." +
                "</div>" +
                "</div>"));
        }

        // -------------------------------------------------------------------
        // Unconfigured-CM warning
        //
        // Driven from LPPIHelper.GetUnconfiguredPrograms() which reads the
        // single-email model on tblLPPI_CapabilityManagers.
        // -------------------------------------------------------------------

        private void BindUnconfigured()
        {
            phUnconfigured.Controls.Clear();

            var unconfigured = LPPIHelper.GetUnconfiguredPrograms();
            if (unconfigured.Count == 0) return;

            var msg = "<div class=\"alert alert-warn\"><div><strong>" + unconfigured.Count +
                      " Capability Manager program" + (unconfigured.Count == 1 ? "" : "s") +
                      " without an AS Fin email configured</strong>: " +
                      LPPIHelper.Enc(string.Join(", ", unconfigured)) +
                      ". Go to <a href=\"LPPI_CapabilityManagers.aspx\">Capability Managers</a> to add the email and display name. " +
                      "Affected packages cannot be sent or notified until the configuration is complete.</div></div>";

            phUnconfigured.Controls.Add(new LiteralControl(msg));
        }

        // -------------------------------------------------------------------
        // BindPackages — open packages (NotSent / Sent / InReview / Finalised)
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
                         INNER JOIN tblLPPI_Documents d
                                 ON d.DocumentID = pd.DocumentID
                                AND d.IsDeactivated = 0
                         WHERE pd.PackageID = p.PackageID) AS DocCount,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Documents d
                                 ON d.DocumentID = pd.DocumentID
                                AND d.IsDeactivated = 0
                         INNER JOIN tblLPPI_Reviews r ON r.DocumentID = pd.DocumentID
                         WHERE pd.PackageID = p.PackageID
                           AND r.ReasonCodeID IS NOT NULL) AS ReviewedCount,
                       (SELECT COUNT(DISTINCT d.DocNoAccounting)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Documents d
                                 ON d.DocumentID = pd.DocumentID
                         WHERE pd.PackageID = p.PackageID
                           AND d.IsDeactivated = 1
                           AND d.SupersededByDocumentID IS NULL) AS RcRlAwaitingCount,
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
                         INNER JOIN tblLPPI_Documents d
                                 ON d.DocumentID = pd.DocumentID
                                AND d.IsDeactivated = 0
                         WHERE pd.PackageID = p.PackageID) AS TotalDocs,
                       (SELECT COUNT(*)
                          FROM tblLPPI_ReviewPackageDocuments pd
                         INNER JOIN tblLPPI_Documents d
                                 ON d.DocumentID = pd.DocumentID
                                AND d.IsDeactivated = 0
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
        /// Reload-eligible pill for the Open packages list. Shows when a
        /// package still carries documents flagged RC-RL and deactivated at
        /// finalise that have not yet been superseded by a corrected reload.
        /// Reconciles with the Deactivated watch-list for the same package.
        /// Renders nothing when the count is zero.
        /// </summary>
        protected string RenderReloadPill(object countObj)
        {
            int n = (countObj == null || countObj == DBNull.Value) ? 0 : Convert.ToInt32(countObj);
            if (n <= 0) return "";
            string title = n + " document" + (n == 1 ? "" : "s") +
                " flagged reload-eligible (RC-RL), deactivated at finalise and awaiting a corrected reload." +
                " Open the Deactivated watch-list for detail.";
            return "<a href=\"LPPI_Deactivated.aspx\" class=\"pill\" title=\"" +
                System.Web.HttpUtility.HtmlAttributeEncode(title) +
                "\" style=\"margin-left:6px;background:#fff1d6;color:#7a4f00;border:1px solid #e6c478;text-decoration:none;\">*" +
                n + "</a>";
        }

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

            bool active = string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase);

            string statusLabel;
            string statusClass;
            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":   statusLabel = "Not sent";  statusClass = "notsent";   break;
                case "sent":      statusLabel = "Sent";      statusClass = "sent";      break;
                case "inreview":  statusLabel = "In review"; statusClass = "inreview";  break;
                case "finalised": statusLabel = "Finalised"; statusClass = "finalised"; break;
                case "exported":  statusLabel = "Exported";  statusClass = "exported";  break;
                case "cancelled": statusLabel = "Cancelled"; statusClass = "cancelled"; break;
                default:          statusLabel = status;     statusClass = "";          break;
            }

            var sb = new StringBuilder();
            sb.AppendFormat("<span class=\"pill {0}\">{1}</span>", statusClass, LPPIHelper.Enc(statusLabel));

            if (active && due < DateTime.Today)
                sb.Append(" <span class=\"pill overdue\">Overdue</span>");
            else if (active && due <= DateTime.Today.AddDays(LPPIHelper.ReminderWindowDays))
                sb.Append(" <span class=\"pill duesoon\">Due soon</span>");

            return sb.ToString();
        }

        /// <summary>
        /// Status pill for the Recent send-outs table. Same shape as
        /// RenderStatusPill but takes a raw status value (the markup binds
        /// via Eval directly, not via Container.DataItem).
        /// </summary>
        protected string RenderStatusPillFromStatus(object statusObj)
        {
            string status = statusObj != null && statusObj != DBNull.Value
                            ? Convert.ToString(statusObj) : "";

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
        /// Capability Manager cell renderer. The program name is the primary
        /// label. When the CM has no AS Fin email configured a secondary
        /// "Not configured" pill is rendered below it, linking to the
        /// Capability Managers page so the admin can fix the gap in one click.
        ///
        /// The configured email and display name are deliberately not shown
        /// here — they live on the Capability Managers page, change rarely,
        /// and would crowd this table. When they need verifying, the
        /// "Not configured" link plus the configured-state list on the
        /// Capability Managers page cover that need.
        /// </summary>
        protected string RenderCmCell(object dataItem)
        {
            var row         = (DataRowView)dataItem;
            bool configured = row["EmailConfigured"] != DBNull.Value
                              && Convert.ToBoolean(row["EmailConfigured"]);
            string program  = row["Program"] == DBNull.Value ? "" : Convert.ToString(row["Program"]);

            var sb = new StringBuilder();
            sb.Append("<div class=\"cm-cell\">");
            sb.Append("<div class=\"cm-program\"><strong>")
              .Append(LPPIHelper.Enc(program))
              .Append("</strong></div>");
            if (!configured)
            {
                sb.Append("<div class=\"cm-config\">")
                  .Append("<a href=\"LPPI_CapabilityManagers.aspx\" class=\"pill-not-configured\" ")
                  .Append("title=\"AS Fin email not configured. Click to fix.\">Not configured</a>")
                  .Append("</div>");
            }
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Actions cell for Open packages rows. Buttons rendered:
        ///   - "Open review" (any status with a token) — opens the reviewer
        ///     page in a new tab via the AS Fin token. Read-only when the
        ///     package is terminal.
        ///   - "Preview AS Fin" + "Preview POC" — suppressed on Finalised
        ///     because reminders / initial sends are meaningless once
        ///     finalised.
        ///   - "Notify AS Fin" — visible only on Finalised. Triggers the
        ///     recipient prompt modal which on submit fires btnNotify_Click.
        ///     Disabled in test mode (the action only makes sense as a real
        ///     send — there is no lifecycle side effect to simulate).
        /// </summary>
        protected string RenderPackageActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (packageIdObj == null || packageIdObj == DBNull.Value) return "";

            int    packageId = Convert.ToInt32(packageIdObj);
            string status    = statusObj != null && statusObj != DBNull.Value
                               ? Convert.ToString(statusObj) : "";

            var sb = new StringBuilder();

            // Open review — available for every status with a token.
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

            // Previews — suppressed on Finalised. POC preview uses
            // placeholder values (handled in LPPIEmail.BuildEmailHtml) so
            // no per-POC selection is needed at the UI level.
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
            else
            {
                // Notify AS Fin — Finalised only.
                bool notifyAllowed = LPPIEmail.ProductionMode;
                string disabledAttr = notifyAllowed ? "" : "disabled=\"disabled\" ";
                string tip          = notifyAllowed
                    ? "Send a courtesy email with the package summary to a typed recipient (CM team mailbox on CC, LPPI support on BCC)."
                    : "Notify AS Fin is a real-send-only action. Set LPPI.ProductionMode = true to enable.";

                // Preview — side-effect-free, so it renders in all modes even
                // though the notify send itself is real-send-only. Reuses the
                // shared preview modal via the kind=notify path.
                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-primary\" {0}" +
                    "title=\"{1}\" " +
                    "onclick=\"openNotify({2});\">Notify AS Fin</button>  ",
                    disabledAttr,
                    LPPIHelper.Enc(tip),
                    packageId);

                sb.AppendFormat(
                    "<button type=\"button\" class=\"btn btn-sm btn-ghost\" " +
                    "onclick=\"openNotifyPreview({0})\">Notify Preview</button>",
                    packageId);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Actions cell for Recent send-outs rows. This is a "what just
        /// happened" log sorted by last email date — the natural follow-up
        /// from here is to click through and verify the send landed, so an
        /// Open review link is offered. Previews are not rendered here; they
        /// belong on the Open packages table where the send button lives.
        /// Notify lives on Finalised rows in the Open packages table for the
        /// same reason (action surface, not log surface).
        ///
        /// packageIdObj is accepted for signature compatibility with the
        /// markup but is not used directly — the Open review link is built
        /// purely from the token.
        /// </summary>
        protected string RenderRecentActions(object packageIdObj, object tokenObj, object statusObj)
        {
            if (tokenObj == null || tokenObj == DBNull.Value) return "";

            string token   = LPPIHelper.Enc(tokenObj);
            string baseUrl = LPPIHelper.Enc(LPPIHelper.Setting("LPPI.BaseUrl", ""));

            return string.Format(
                "<button type=\"button\" class=\"btn btn-sm btn-secondary\" " +
                "onclick=\"openReviewLink('{0}','{1}');\">Open review &rarr;</button>",
                token, baseUrl);
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
        //   WarningMessage  — non-fatal note about the POC fan-out
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
                ShowMessage("Real sending is disabled in test mode. Use Mark as sent / remind (test) instead.", "err");
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
        /// list. Shared between btnSend_Click's initial and reminder branches
        /// and the test-mode equivalents in btnMarkSent_Click.
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
        //
        // Branches on the current package status:
        //   NotSent           -> MarkAsSent     (simulates initial send;
        //                                        transitions NotSent -> Sent,
        //                                        stamps SentDate, uses the
        //                                        due date input)
        //   Sent / InReview   -> MarkAsReminded (simulates reminder;
        //                                        no status change, no
        //                                        due date change)
        //   anything else     -> skipped with a clear per-package message
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

            int initialOk = 0, reminderOk = 0, failed = 0;
            int totalPocsDispatched = 0, totalPocsSkipped = 0, totalPocsFailed = 0;
            int totalControlDispatched = 0, totalControlFailed = 0;
            var perPackageNotes = new StringBuilder();

            foreach (int pid in selectedPackageIds)
            {
                object statusObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM tblLPPI_ReviewPackages WHERE PackageID = @P",
                    LPPIHelper.P("@P", pid));
                string status = statusObj == null || statusObj == DBNull.Value
                              ? "" : Convert.ToString(statusObj);

                if (string.Equals(status, "NotSent", StringComparison.OrdinalIgnoreCase))
                {
                    // Initial path. Due date input is the operator's last
                    // chance to set the package due date before the status
                    // transitions to Sent.
                    LPPIHelper.ExecuteNonQuery(
                        "UPDATE tblLPPI_ReviewPackages SET DueDate = @D WHERE PackageID = @P AND Status = 'NotSent'",
                        LPPIHelper.P("@D", due),
                        LPPIHelper.P("@P", pid));

                    var res = LPPIEmail.MarkAsSent(pid);
                    AccumulateResult(res, perPackageNotes, pid, "initial",
                        ref initialOk, ref failed,
                        ref totalPocsDispatched, ref totalPocsSkipped, ref totalPocsFailed,
                        ref totalControlDispatched, ref totalControlFailed);
                }
                else if (string.Equals(status, "Sent",     StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "InReview", StringComparison.OrdinalIgnoreCase))
                {
                    // Reminder path. Due date input is ignored — reminders
                    // never change the package due date.
                    var res = LPPIEmail.MarkAsReminded(pid);
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
            msg.Append(initialOk).Append(" initial").Append(initialOk == 1 ? "" : "s")
               .Append(" marked as sent, ")
               .Append(reminderOk).Append(" reminder").Append(reminderOk == 1 ? "" : "s")
               .Append(" marked as reminded (test mode — no email dispatched).");
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
            if (failed > 0)
                msg.Append(" ").Append(failed).Append(" package failure").Append(failed == 1 ? "" : "s").Append(".");
            if (perPackageNotes.Length > 0)
                msg.Append("<ul>").Append(perPackageNotes).Append("</ul>");

            ShowMessageRaw(msg.ToString(), kind);
            BindPackages();
            BindRecent();
        }

        // -------------------------------------------------------------------
        // Notify AS Fin — admin-initiated email on Finalised packages
        //
        // Triggered by the JS notify modal which collects the recipient
        // email, writes the PackageID + recipient into two hidden fields,
        // then submits via btnNotify (which calls this handler).
        //
        // All validation is server-side. The modal does a client-side
        // sanity check on the regex but that is purely for snappy UX —
        // the real authority is LPPIEmail.NotifyAsFin which delegates to
        // LPPIHelper.ValidateDefenceEmail.
        // -------------------------------------------------------------------

        protected void btnNotify_Click(object sender, EventArgs e)
        {
            if (!LPPIEmail.ProductionMode)
            {
                ShowMessage("Notify AS Fin is not available in test mode. Set LPPI.ProductionMode = true to enable.", "err");
                return;
            }

            int packageId;
            if (!int.TryParse(hfNotifyPackageId.Value, out packageId))
            {
                ShowMessage("No package was selected for the notify action.", "err");
                return;
            }

            string recipient = (hfNotifyRecipient.Value ?? "").Trim();
            if (recipient.Length == 0)
            {
                ShowMessage("A recipient email address is required.", "err");
                return;
            }

            var res = LPPIEmail.NotifyAsFin(packageId, recipient);

            // Clear the hidden fields whether success or failure so the
            // next notify click starts fresh.
            hfNotifyPackageId.Value = "";
            hfNotifyRecipient.Value = "";

            if (res.Success)
            {
                ShowMessage(string.Format(
                    "Notification sent to {0} for package #{1}. CC: CM team mailbox. BCC: LPPI support.",
                    LPPIHelper.Enc(recipient), packageId), "ok");
            }
            else
            {
                ShowMessage(res.ErrorMessage ?? "Notification failed (no detail).", "err");
            }

            BindPackages();
            BindRecent();
        }

        // -------------------------------------------------------------------
        // Message helpers
        // -------------------------------------------------------------------

        private void ShowMessage(string msg, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\"><div>")
              .Append(LPPIHelper.Enc(msg))
              .Append("</div></div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }

        private void ShowMessageRaw(string html, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\"><div>")
              .Append(html)
              .Append("</div></div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
