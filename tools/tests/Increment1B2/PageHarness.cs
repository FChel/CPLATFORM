using System;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.SessionState;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    // Only the hosting/identity boundary is substituted; handlers below are the
    // production NORM_Import.aspx.cs compiled unchanged by the runner.
    public class NORMBasePage : Page { }
    public partial class NORM_Import
    {
        protected Panel ErrorPanel = new Panel(), ResultPanel = new Panel(), PreviewPanel = new Panel();
        protected Literal ErrorMessage = new Literal(), ResultMessage = new Literal(), PreviewMessage = new Literal(), ReplacementError = new Literal();
        protected Button ImportButton = new Button(), ConfirmButton = new Button();
        protected DropDownList ReleaseList = new DropDownList();
        protected CheckBox ReplaceExisting = new CheckBox();
        protected FileUpload TrialBalanceFile = new FileUpload();

        public void StageForTest(NORMImportPreview preview, bool correctToken, params NORMImportPreview[] additionalFiles)
        {
            HttpContext context = new HttpContext(new HttpRequest("", "http://localhost/NORM/NORM_Import.aspx", ""), new HttpResponse(new StringWriter()));
            SessionStateUtility.AddHttpSessionStateToContext(context, new HttpSessionStateContainer("isolated-session", new SessionStateItemCollection(),
                new HttpStaticObjectsCollection(), 20, true, HttpCookieMode.UseCookies, SessionStateMode.InProc, false));
            HttpContext.Current = context;
            typeof(Page).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, context);
            var previews = new System.Collections.Generic.List<NORMImportPreview> { preview };
            previews.AddRange(additionalFiles);
            Session[PendingKey] = previews;
            ViewState["PreviewToken"] = correctToken ? preview.Token : "stale-tab-token";
            ReleaseList.Items.Add(new ListItem("Release 1", "1"));
            ReleaseList.Items.Add(new ListItem("Release 2", "2"));
            ReleaseList.SelectedValue = preview.ReleaseId.ToString();
            ShowPreview(previews);
        }
        public void CheckReplacementForTest() { ReplaceExisting.Checked = true; }
        public void ConfirmForTest() { ConfirmButton_Click(this, EventArgs.Empty); }
        public void CancelForTest() { CancelButton_Click(this, EventArgs.Empty); }
        public void SwitchReleaseForTest() { ReleaseList.SelectedValue = "2"; }
        public void ForgeSummaryForTest()
        {
            var form = HttpContext.Current.Request.Form;
            typeof(System.Collections.Specialized.NameObjectCollectionBase).GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, false, null);
            form["RowCount"] = "1"; form["SourceCompanyCode"] = "9999"; form["Net"] = "100"; form["FileHash"] = "forged";
        }
        public string ResultForTest { get { return ResultMessage.Text; } }
        public string ErrorForTest { get { return ErrorMessage.Text; } }
        public string ReplacementErrorForTest { get { return ReplacementError.Text; } }
        public bool ReplacementControlsForTest { get { return PreviewPanel.Visible && ReplaceExisting.Visible && ConfirmButton.Enabled; } }
        public string ReplacementLabelForTest { get { return ReplaceExisting.Text; } }
        public string ConfirmLabelForTest { get { return ConfirmButton.Text; } }
        public string PreviewForTest { get { return PreviewMessage.Text; } }
        public bool PendingForTest { get { return Session[PendingKey] != null; } }
    }
}
