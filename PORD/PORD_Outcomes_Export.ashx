<%@ WebHandler Language="C#" Class="CPlatform.PORD.PORD_Outcomes_Export" %>

using System;
using System.Linq;
using System.Text;
using System.Web;

namespace CPlatform.PORD
{
    /// <summary>CSV downloads for the Outcomes page. kind=outcomes | bp. Admin only.</summary>
    public class PORD_Outcomes_Export : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            if (!PORDHelper.HasAccess()) { ctx.Response.StatusCode = 403; return; }
            var store = PORDHelper.Store;
            var sb = new StringBuilder();
            string name;

            if (ctx.Request.QueryString["kind"] == "bp")
            {
                name = "PO_NSPT_BP_MasterData_";
                sb.AppendLine("BP Number,BP Name,BP Payment Term,Net Days,Open POs,Still to Deliver,Status");
                foreach (var b in store.GetBpIssues())
                    sb.AppendLine(string.Join(",", new[] { b.BpNumber, Q(b.BpName), b.BpTermKey, Convert.ToString(b.BpTermDays),
                        b.OpenPoCount.ToString(), b.StillToDeliver.ToString("0.00"), b.Status }));
            }
            else
            {
                name = "PO_NSPT_Outcomes_";
                var reasons = store.GetReasonCodes(false).ToDictionary(r => r.Code, r => r.Description);
                sb.AppendLine("Program,Package,Package Status,PO Number,BP Number,BP Name,PO Term,BP Term,Currency,Issue,Review Nbr,Still to Deliver,PO Contact,Response,Reason Code,Reason,Evidence Ref,Amend By,Comments,Responded By,Responded");
                foreach (var p in store.GetPackages(false))
                    foreach (var po in store.GetPos(p.PackageID))
                    {
                        string rdesc;
                        reasons.TryGetValue(po.ReasonCode ?? "", out rdesc);
                        sb.AppendLine(string.Join(",", new[]
                        {
                            Q(p.DmProgram), p.PackageID.ToString(), p.Status, po.PoNumber, po.BpNumber, Q(po.BpName), po.PoTermKey, po.BpTermKey,
                            po.Currency, Q(PORDRules.CategoryLabel(po.Category)), po.ReviewNbr.ToString(), po.StillToDeliver.ToString("0.00"),
                            po.PocEmail, Q(PORDRules.ResponseLabel(po.Response)), po.ReasonCode, Q(rdesc), Q(po.EvidenceRef),
                            PORDHelper.Date(po.TargetDate), Q(po.Comments), Q(po.ReviewedBy), PORDHelper.DateTimeShort(po.ReviewedDate)
                        }));
                    }
            }

            ctx.Response.ContentType = "text/csv; charset=utf-8";
            ctx.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + LPPI.LPPIHelper.EnvironmentFileTag + name + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv\"");
            ctx.Response.BinaryWrite(new UTF8Encoding(true).GetPreamble());
            ctx.Response.Write(sb.ToString());
        }

        private static string Q(string s)
        {
            s = s ?? "";
            // Neutralise spreadsheet formula injection, then quote.
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
