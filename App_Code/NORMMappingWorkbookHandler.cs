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
            bool blank = context.Request.QueryString["blank"] == "1";
            int releaseId = 0;
            if (!blank && (!Int32.TryParse(context.Request.QueryString["release"], out releaseId) || releaseId <= 0))
            {
                context.Response.StatusCode = 400; context.Response.TrySkipIisCustomErrors = true; context.Response.Write("Select a draft mapping release."); return;
            }
            try
            {
                byte[] content = blank ? NORMMappingManagement.BuildBlankWorkbook() : NORMMappingManagement.BuildEditableWorkbook(releaseId);
                context.Response.Clear(); context.Response.Buffer = true;
                context.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"NORM_GL_Mapping.xlsx\"");
                context.Response.AddHeader("Content-Length", content.Length.ToString(CultureInfo.InvariantCulture));
                context.Response.BinaryWrite(content); context.ApplicationInstance.CompleteRequest();
            }
            catch (Exception error)
            {
                NORMErrorHandling.HandlerFailure(context, error, "MappingWorkbook");
            }
        }
    }
}
