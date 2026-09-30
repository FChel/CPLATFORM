<%@ WebHandler Language="C#" Class="CPlatform.PORD.PORD_SampleFile" %>

using System;
using System.Text;
using System.Web;

namespace CPlatform.PORD
{
    /// <summary>
    /// Downloads a sample extract in the proposed column layout (demo data),
    /// so BODS developers can see exactly what the loader expects. Admin only.
    /// </summary>
    public class PORD_SampleFile : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            if (!PORDHelper.HasAccess()) { ctx.Response.StatusCode = 403; return; }
            string csv = PORDCsvParser.SampleCsv(PORDHelper.Store.GetCurrentCyclePos());
            ctx.Response.ContentType = "text/csv; charset=utf-8";
            ctx.Response.AddHeader("Content-Disposition",
                "attachment; filename=\"" + LPPI.LPPIHelper.EnvironmentFileTag + "PO_NSPT_REVIEW_sample_" + DateTime.Today.ToString("yyyyMMdd") + ".csv\"");
            var bytes = new UTF8Encoding(true).GetPreamble();
            ctx.Response.BinaryWrite(bytes);
            ctx.Response.Write(csv);
        }
    }
}
