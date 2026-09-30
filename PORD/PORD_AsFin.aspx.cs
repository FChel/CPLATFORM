using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.PORD
{
    /// <summary>AS Fin mailbox per Delivery Manager program (LPPI's Capability Managers page equivalent).</summary>
    public partial class PORD_AsFin : PORDBasePage
    {
        private IList<PordPo> _pos;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            _pos = PORDHelper.Store.GetCurrentCyclePos();
            rptGroups.DataSource = PORDHelper.Store.GetAsFinGroups();
            rptGroups.DataBind();
        }

        protected PordAsFinGroup G(object o) { return (PordAsFinGroup)o; }
        protected int PoCount(string program) { return _pos.Count(p => p.DmProgram == program); }

        protected void rptGroups_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "save") return;
            var email = ((TextBox)e.Item.FindControl("txtEmail")).Text;
            var name = ((TextBox)e.Item.FindControl("txtName")).Text;
            string error;
            string program = Convert.ToString(e.CommandArgument);
            if (PORDHelper.Store.SaveAsFinGroup(program, email, name, out error))
                phMsg.Controls.Add(new LiteralControl("<div class=\"alert ok\">Saved " + PORDHelper.Enc(program) + ".</div>"));
            else
                phMsg.Controls.Add(new LiteralControl("<div class=\"alert err\">" + PORDHelper.Enc(program) + ": " + PORDHelper.Enc(error) + "</div>"));
            Bind();
        }
    }
}
