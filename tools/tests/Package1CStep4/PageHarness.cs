using System;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.UI;

namespace CPlatform.NORM
{
    public partial class NORM_SourceHistoryPage
    {
        public void RenderForTest(string query)
        {
            HttpContext context=new HttpContext(new HttpRequest("","http://localhost/NORM/NORM_SourceHistory.aspx",query),new HttpResponse(new StringWriter()));
            HttpContext.Current=context; typeof(Page).GetField("_context",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context);
            typeof(Page).GetField("_request",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context.Request);
            typeof(Page).GetField("_response",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context.Response);
            Page_Load(this,EventArgs.Empty);
        }
        public string HistoryForTest { get { return HistoryHtml; } }
        public string ComparisonForTest { get { return ComparisonHtml; } }
        public string PopulationsForTest { get { return PopulationHtml; } }
        public string MissingForTest { get { return MissingHtml; } }
        public string FiltersForTest { get { return FiltersHtml; } }
        public string ContextForTest { get { return ContextHtml; } }
        public string ErrorForTest { get { return ErrorHtml; } }
        public string ReturnForTest { get { return ReturnUrl; } }
        public int StatusForTest { get { return Response.StatusCode; } }
    }
    public partial class NORM_Workspace
    {
        public string SourceActivityForTest() { BuildLatestActivity(100,10); return LatestActivityHtml; }
    }
    public partial class NORM_Import
    {
        public string PrepareContextForTest(string query)
        {
            HttpContext context=new HttpContext(new HttpRequest("","http://localhost/NORM/NORM_Import.aspx",query),new HttpResponse(new StringWriter()));
            System.Web.SessionState.SessionStateUtility.AddHttpSessionStateToContext(context,new System.Web.SessionState.HttpSessionStateContainer("context-session",new System.Web.SessionState.SessionStateItemCollection(),new HttpStaticObjectsCollection(),20,true,HttpCookieMode.UseCookies,System.Web.SessionState.SessionStateMode.InProc,false));
            HttpContext.Current=context; typeof(Page).GetField("_context",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context);
            typeof(Page).GetField("_request",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context.Request);
            typeof(Page).GetField("_response",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context.Response);
            Page_Load(this,EventArgs.Empty); return ReleaseList.SelectedValue;
        }
    }
}
