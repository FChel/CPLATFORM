using System;
using System.Data;
using System.Text;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Capability Managers admin page.
    ///
    /// May 2026 — recipient model is a single AS Fin email + display name
    /// on tblLPPI_CapabilityManagers. The legacy free-text DisplayName
    /// column on tblLPPI_CapabilityManagers has been retired pre-launch:
    /// the AS Fin display name is the only name now, the program code is
    /// the user-facing identifier everywhere else.
    ///
    /// A banner across the top of the page surfaces the count of CMs that
    /// are missing email configuration, with a deep-link to the first one
    /// that needs attention. Drives the "high UX bar" call from the brief.
    /// </summary>
    public partial class LPPI_CapabilityManagers : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindCms();
                BindMissingBanner();

                // Optional deep-link: ?cm=<id> opens that group's Manage panel
                // directly (used by "Configure now" links from the Load page
                // and the Send-outs warning banner).
                string cmArg = Request.QueryString["cm"];
                int cmId;
                if (!string.IsNullOrEmpty(cmArg) && int.TryParse(cmArg, out cmId))
                {
                    ShowManagePanel(cmId);
                }
            }
        }

        // -------------------------------------------------------------------
        // Top-of-page missing-configuration banner.
        //
        // Visible when there is at least one ACTIVE CM with documents in the
        // system AND missing email configuration. Hidden otherwise.
        //
        // The Send-outs page has its own per-row warning at send time; this
        // banner is the proactive nudge so admins land on the page already
        // knowing there is setup work to do.
        // -------------------------------------------------------------------
        private void BindMissingBanner()
        {
            var unconfigured = LPPIHelper.GetUnconfiguredPrograms();
            if (unconfigured == null || unconfigured.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-warn\" style=\"margin-bottom:18px;\">");
            sb.Append("<strong>");
            sb.Append(unconfigured.Count);
            sb.Append(" Capability Manager group");
            sb.Append(unconfigured.Count == 1 ? "" : "s");
            sb.Append(unconfigured.Count == 1 ? " has" : " have");
            sb.Append(" no email configured.</strong> ");
            sb.Append("Sends to these groups are blocked until both the AS Fin email and display name are set: ");
            sb.Append(LPPIHelper.Enc(string.Join(", ", unconfigured)));
            sb.Append(".</div>");
            phMissingBanner.Controls.Add(new LiteralControl(sb.ToString()));
        }

        // -------------------------------------------------------------------
        // CM group list
        // -------------------------------------------------------------------

        private void BindCms()
        {
            // Columns consumed by rptCms Eval() bindings:
            //   CmID, Program, Email, EmailDisplayName,
            //   EmailConfigured (bit), OpenDocs.
            //
            // EmailConfigured is the gate the Send page relies on. It is
            // surfaced here as a status pill so admins can see at a glance
            // which groups are ready and which are not.
            const string sql = @"
                SELECT cm.CmID,
                       cm.Program,
                       ISNULL(cm.Email, '')            AS Email,
                       ISNULL(cm.EmailDisplayName, '') AS EmailDisplayName,
                       CASE WHEN cm.Email IS NOT NULL
                             AND LTRIM(RTRIM(cm.Email)) <> ''
                             AND cm.EmailDisplayName IS NOT NULL
                             AND LTRIM(RTRIM(cm.EmailDisplayName)) <> ''
                            THEN 1 ELSE 0 END          AS EmailConfigured,
                       (SELECT COUNT(DISTINCT d.DocNoAccounting)
                          FROM tblLPPI_Documents d
                          LEFT JOIN tblLPPI_Reviews r
                                 ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                                                      FROM tblLPPI_Documents d2
                                                     WHERE d2.DocNoAccounting = d.DocNoAccounting)
                         WHERE d.CapabilityManagerProgram = cm.Program
                           AND r.ReasonCodeID IS NULL) AS OpenDocs
                  FROM tblLPPI_CapabilityManagers cm
                 ORDER BY cm.Program";
            rptCms.DataSource = LPPIHelper.ExecuteTable(sql);
            rptCms.DataBind();
        }

        // -------------------------------------------------------------------
        // rptCms event handlers
        // -------------------------------------------------------------------

        protected void rptCms_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int cmId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out cmId)) return;

            if (e.CommandName == "Manage")
            {
                ShowManagePanel(cmId);
            }
        }

        /// <summary>
        /// Applies the is-editing row highlight to the CM currently open in the
        /// Manage panel.
        /// </summary>
        protected void rptCms_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item &&
                e.Item.ItemType != ListItemType.AlternatingItem)
                return;

            int editingCmId;
            if (!pnlManage.Visible || !int.TryParse(hfCmId.Value, out editingCmId))
                return;

            var row = e.Item.DataItem as DataRowView;
            if (row == null) return;

            int thisCmId = Convert.ToInt32(row["CmID"]);
            if (thisCmId != editingCmId) return;

            var tr = e.Item.FindControl("trRow") as HtmlTableRow;
            if (tr != null) tr.Attributes["class"] = "is-editing";

            var flag = e.Item.FindControl("litEditFlag") as Literal;
            if (flag != null) flag.Text = "<span class=\"edit-flag\">(editing)</span>";
        }

        // -------------------------------------------------------------------
        // Manage panel — open / close
        // -------------------------------------------------------------------

        private void ShowManagePanel(int cmId)
        {
            pnlManage.Visible = true;
            hfCmId.Value      = cmId.ToString();

            var cm = LPPIHelper.GetCmEmail(cmId);
            if (cm != null)
            {
                litCmProgram.Text        = LPPIHelper.Enc(cm.Program);
                txtEmail.Text            = cm.Email ?? "";
                txtEmailDisplayName.Text = cm.EmailDisplayName ?? "";
            }
            else
            {
                // Defensive — should not happen given the row was just clicked.
                litCmProgram.Text        = "";
                txtEmail.Text            = "";
                txtEmailDisplayName.Text = "";
            }

            // Re-bind the main list so the row-highlight logic picks up the
            // new hfCmId.
            BindCms();
        }

        protected void btnCloseManage_Click(object sender, EventArgs e)
        {
            pnlManage.Visible        = false;
            hfCmId.Value             = "";
            txtEmail.Text            = "";
            txtEmailDisplayName.Text = "";
            BindCms();
            BindMissingBanner();
        }

        // -------------------------------------------------------------------
        // AS Fin email save / clear
        //
        // Both go through LPPIHelper.SaveCmEmail which handles validation
        // (must be @defence.gov.au, both fields together). Clear path
        // calls SaveCmEmail with two empty strings — the helper accepts
        // that as a clear.
        // -------------------------------------------------------------------

        protected void btnSaveEmail_Click(object sender, EventArgs e)
        {
            int cmId;
            if (!int.TryParse(hfCmId.Value, out cmId))
            {
                ShowMessage("No group selected.", "err");
                return;
            }

            string em = (txtEmail.Text ?? "").Trim();
            string dn = (txtEmailDisplayName.Text ?? "").Trim();

            string err;
            bool ok = LPPIHelper.SaveCmEmail(cmId, em, dn, out err);
            if (!ok)
            {
                ShowMessage(err ?? "Could not save email.", "err");
                ShowManagePanel(cmId);
                return;
            }

            ShowMessage("AS Fin email saved.", "ok");
            ShowManagePanel(cmId);
            BindMissingBanner();
        }

        protected void btnClearEmail_Click(object sender, EventArgs e)
        {
            int cmId;
            if (!int.TryParse(hfCmId.Value, out cmId))
            {
                ShowMessage("No group selected.", "err");
                return;
            }

            string err;
            // Two empty strings -> clear path in SaveCmEmail.
            LPPIHelper.SaveCmEmail(cmId, "", "", out err);

            txtEmail.Text            = "";
            txtEmailDisplayName.Text = "";

            ShowMessage("AS Fin email cleared.", "warn");
            ShowManagePanel(cmId);
            BindMissingBanner();
        }

        // -------------------------------------------------------------------
        // Shared helpers
        // -------------------------------------------------------------------

        private void ShowMessage(string msg, string kind)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\">")
              .Append(LPPIHelper.Enc(msg))
              .Append("</div>");
            phMessage.Controls.Add(new LiteralControl(sb.ToString()));
        }
    }
}
