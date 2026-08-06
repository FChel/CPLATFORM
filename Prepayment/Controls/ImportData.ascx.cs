using System;
using System.Web.UI;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// Import tab — uploads the real Excel workbook and full-replaces the prepayment data.
    /// The control is presentation-only; the upload is handled client-side (importRun() in
    /// dashboard.js) which POSTs to Services/PPMImportHandler.ashx and renders the result summary.
    /// </summary>
    public partial class PPMImportData : UserControl
    {
        protected void Page_Load(object sender, EventArgs e) { }
    }
}
