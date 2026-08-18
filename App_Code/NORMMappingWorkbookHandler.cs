using System;
using System.Globalization;
using System.Web;

namespace CPlatform.NORM
{
    public class NORM_MappingWorkbook : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated || !NORMHelper.HasPrepareAccess())
            {
                context.Response.StatusCode = 403; context.Response.TrySkipIisCustomErrors = true; context.Response.Write("Preparer access is required."); return;
            }
            int releaseId;
            if (!Int32.TryParse(context.Request.QueryString["release"], out releaseId) || releaseId <= 0)
            {
                context.Response.StatusCode = 400; context.Response.TrySkipIisCustomErrors = true; context.Response.Write("Select a draft mapping release."); return;
            }
            try
            {
                byte[] content = NORMMappingManagement.BuildEditableWorkbook(releaseId);
                context.Response.Clear(); context.Response.Buffer = true;
                context.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"NORM_Mapping_Draft_" + releaseId.ToString(CultureInfo.InvariantCulture) + ".xlsx\"");
                context.Response.AddHeader("Content-Length", content.Length.ToString(CultureInfo.InvariantCulture));
                context.Response.BinaryWrite(content); context.ApplicationInstance.CompleteRequest();
            }
            catch (Exception error)
            {
                context.Response.Clear(); context.Response.StatusCode = 500; context.Response.TrySkipIisCustomErrors = true;
                context.Response.ContentType = "text/plain; charset=utf-8"; context.Response.Write(error.GetBaseException().Message);
            }
        }
    }
}
