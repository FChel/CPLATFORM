using System;
using System.Web.UI;

namespace CPlatform.NORM
{
    /// <summary>Preserves the original NORM entry URL while using the rebuilt statement reader.</summary>
    public partial class Statements : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            string target = "NORM_Statements.aspx";
            if (!String.IsNullOrEmpty(Request.Url.Query)) { target += Request.Url.Query; }
            Response.Redirect(target, true);
        }
    }
}
