using System;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Script.Serialization;
using Prepayment.Web.Models.Dtos;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// AJAX endpoint for the Group Workflow Control page (Tab 5).
    ///
    /// GET  …/PPMGroupWorkflowHandler.ashx?action=users     → { ok, data:[ {id,displayName,roleName} ] }
    /// POST …/PPMGroupWorkflowHandler.ashx?action=reassign   body: { Groups:[code,...], PreparerUserId?, ApproverUserId? }
    ///                                                    → { ok, data:{ updated } }
    /// </summary>
    public class PPMGroupWorkflowHandler : IHttpHandler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly PPMGroupWorkflowService _service = new PPMGroupWorkflowService();

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json";
            try
            {
                string action = (context.Request.QueryString["action"] ?? "").ToLowerInvariant();

                switch (action)
                {
                    case "users":
                        WriteOk(context, _service.GetUsers()
                            .Select(u => new { id = u.Id, displayName = u.DisplayName, roleName = u.RoleName }));
                        break;

                    case "reassign":
                        {
                            int userId = PPMCurrentUser.ResolveId(context);
                            var req = Json.Deserialize<PPMGroupReassignRequest>(ReadBody(context)) ?? new PPMGroupReassignRequest();

                            var groups = (req.Groups ?? new System.Collections.Generic.List<string>())
                                .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
                            if (groups.Count == 0)
                                throw new ArgumentException("Select at least one delivery group.");

                            int updated = groups.Sum(code =>
                                _service.Reassign(code, req.PreparerUserId, req.ApproverUserId, userId));

                            WriteOk(context, new { updated });
                            break;
                        }

                    case "escalate":
                        {
                            int userId = PPMCurrentUser.ResolveId(context);
                            string group = (context.Request.QueryString["group"] ?? "").Trim();
                            long excId = _service.Escalate(group, null, userId);
                            WriteOk(context, new { exceptionId = excId });
                            break;
                        }

                    case "reminder":
                        {
                            int userId = PPMCurrentUser.ResolveId(context);
                            string group = (context.Request.QueryString["group"] ?? "").Trim();
                            long id = _service.SendReminder(group, userId);
                            WriteOk(context, new { reminderId = id });
                            break;
                        }

                    default:
                        WriteError(context, "Unknown action '" + action + "'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 400;
                context.Response.Write(Json.Serialize(new { ok = false, error = ex.Message }));
            }
        }

        private static string ReadBody(HttpContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream))
                return reader.ReadToEnd();
        }

        private static void WriteOk(HttpContext context, object data)
        {
            context.Response.Write(Json.Serialize(new { ok = true, data }));
        }

        private static void WriteError(HttpContext context, string message)
        {
            context.Response.StatusCode = 400;
            context.Response.Write(Json.Serialize(new { ok = false, error = message }));
        }
    }
}
