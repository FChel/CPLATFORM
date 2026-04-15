using System;
using System.Data;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    public partial class LPPI_ReasonCodes : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) { Bind(); }
        }

        private void Bind()
        {
            rptCodes.DataSource = LPPIHelper.GetReasonCodes(activeOnly: false);
            rptCodes.DataBind();
        }

        protected void btnSave_Click(object sender, EventArgs e)
        {
            string code = (txtCode.Text ?? "").Trim();
            string desc = (txtDesc.Text ?? "").Trim();
            if (code.Length == 0 || desc.Length == 0)
            {
                ShowMessage("Code and description are both required.", "err");
                return;
            }

            int order;
            if (!int.TryParse((txtOrder.Text ?? "").Trim(), out order)) { order = 999; }

            int id;
            if (int.TryParse(hfId.Value, out id) && id > 0)
            {
                LPPIHelper.ExecuteNonQuery(@"
                    UPDATE tblLPPI_ReasonCodes
                    SET Code = @code, [Description] = @desc, Outcome = @out,
                        DisplayOrder = @ord, RequiresComments = @req, IsActive = @act
                    WHERE ReasonCodeID = @id",
                    LPPIHelper.P("@code", code),
                    LPPIHelper.P("@desc", desc),
                    LPPIHelper.P("@out", ddlOutcome.SelectedValue),
                    LPPIHelper.P("@ord", order),
                    LPPIHelper.P("@req", chkRequires.Checked ? 1 : 0),
                    LPPIHelper.P("@act", chkActive.Checked ? 1 : 0),
                    LPPIHelper.P("@id", id));
                ShowMessage("Reason code updated.", "ok");
            }
            else
            {
                LPPIHelper.ExecuteNonQuery(@"
                    INSERT INTO tblLPPI_ReasonCodes (Code, [Description], Outcome, DisplayOrder, RequiresComments, IsActive)
                    VALUES (@code, @desc, @out, @ord, @req, @act)",
                    LPPIHelper.P("@code", code),
                    LPPIHelper.P("@desc", desc),
                    LPPIHelper.P("@out", ddlOutcome.SelectedValue),
                    LPPIHelper.P("@ord", order),
                    LPPIHelper.P("@req", chkRequires.Checked ? 1 : 0),
                    LPPIHelper.P("@act", chkActive.Checked ? 1 : 0));
                ShowMessage("Reason code added.", "ok");
            }
            ClearForm();
            Bind();
        }

        protected void btnClear_Click(object sender, EventArgs e)
        {
            ClearForm();
        }

        private void ClearForm()
        {
            txtCode.Text = "";
            txtDesc.Text = "";
            txtOrder.Text = "";
            ddlOutcome.SelectedValue = "Payable";
            chkRequires.Checked = false;
            chkActive.Checked = true;
            hfId.Value = "";
        }

        protected void rptCodes_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            int id = Convert.ToInt32(e.CommandArgument);
            if (e.CommandName == "Toggle")
            {
                LPPIHelper.ExecuteNonQuery(
                    "UPDATE tblLPPI_ReasonCodes SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE ReasonCodeID = @id",
                    LPPIHelper.P("@id", id));
                Bind();
            }
            else if (e.CommandName == "Edit")
            {
                DataTable dt = LPPIHelper.ExecuteTable(
                    "SELECT * FROM tblLPPI_ReasonCodes WHERE ReasonCodeID = @id",
                    LPPIHelper.P("@id", id));
                if (dt.Rows.Count == 1)
                {
                    DataRow r = dt.Rows[0];
                    hfId.Value = id.ToString();
                    txtCode.Text = Convert.ToString(r["Code"]);
                    txtDesc.Text = Convert.ToString(r["Description"]);
                    ddlOutcome.SelectedValue = Convert.ToString(r["Outcome"]);
                    txtOrder.Text = Convert.ToString(r["DisplayOrder"]);
                    chkRequires.Checked = Convert.ToBoolean(r["RequiresComments"]);
                    chkActive.Checked = Convert.ToBoolean(r["IsActive"]);
                }
            }
        }

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
