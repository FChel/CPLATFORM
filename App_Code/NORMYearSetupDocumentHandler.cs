using System;
using System.IO;
using System.Web;

namespace CPlatform.NORM
{
    public sealed class NORMYearSetupDocumentHandler : IHttpHandler
    {
        public bool IsReusable { get { return false; } }
        public void ProcessRequest(HttpContext context)
        {
            if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated || !NORMHelper.HasPrepareAccess())
            { Error(context, 403, "Authorised NORM access is required."); return; }
            long id;
            if (!Int64.TryParse(context.Request.QueryString["document"], out id) || id <= 0)
            { Error(context, 400, "A valid document identifier is required."); return; }
            NORMStartOfYearSetup.RetainedDocument document;
            try { document = NORMStartOfYearSetup.DownloadDocument(id, NORMHelper.CurrentUserId()); }
            catch (UnauthorizedAccessException) { Error(context, 403, "You do not have access to this source document."); return; }
            catch (FileNotFoundException) { Error(context, 404, "The retained original was not found."); return; }
            catch (Exception) { Error(context, 500, "The original could not be retrieved. Contact the administrator."); return; }
            string fileName = Path.GetFileName((document.FileName ?? "").Replace("\r", "").Replace("\n", "")).Replace("\"", "");
            if (String.IsNullOrWhiteSpace(fileName)) fileName = "NORM-YearSetup-source.bin";
            context.Response.Clear();
            context.Response.ContentType = MimeMapping.GetMimeMapping(fileName);
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.AddHeader("X-Content-Type-Options", "nosniff");
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
            context.Response.AddHeader("Content-Length", document.Content.LongLength.ToString());
            context.Response.OutputStream.Write(document.Content, 0, document.Content.Length);
        }
        private static void Error(HttpContext context, int status, string message)
        {
            context.Response.Clear(); context.Response.StatusCode = status; context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8"; context.Response.Write(message);
        }
    }
}
