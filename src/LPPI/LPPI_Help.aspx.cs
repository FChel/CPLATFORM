using System;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Admin-facing help / documentation page. Pure static content rendered
    /// from the .aspx markup; no data binding, no postbacks.
    ///
    /// Inherits LPPIBasePage so the standard nav header is rendered and the
    /// admin access gate applies (non-admins are redirected to
    /// LPPI_Info.aspx, same as every other admin page).
    ///
    /// This is distinct from LPPI_Info.aspx — that page is the
    /// access-denied / public landing page (does NOT inherit LPPIBasePage,
    /// to avoid a redirect loop). Confused these two before; do not merge.
    /// </summary>
    public partial class LPPI_Help : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // No-op. Static page.
        }
    }
}
