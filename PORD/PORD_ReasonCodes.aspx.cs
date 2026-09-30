using System;
using System.Collections.Generic;
using System.Linq;

namespace CPlatform.PORD
{
    public partial class PORD_ReasonCodes : PORDBasePage
    {
        private IList<PordPo> _pos;

        protected void Page_Load(object sender, EventArgs e)
        {
            _pos = PORDHelper.Store.GetCurrentCyclePos();
            rpt.DataSource = PORDHelper.Store.GetReasonCodes(false);
            rpt.DataBind();
        }

        protected PordReasonCode R(object o) { return (PordReasonCode)o; }
        protected int Used(string code) { return _pos.Count(p => p.Response == PordResponse.Reason && p.ReasonCode == code); }
        protected string Tick(bool on, string label)
        {
            return on ? "<span style=\"color:var(--ok);font-weight:600;\">&#10003; " + PORDHelper.Enc(label) + "</span>" : "<span class=\"muted\">—</span>";
        }
    }
}
