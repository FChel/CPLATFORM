using System;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
namespace CPlatform.NORM
{
    public class NORMBasePage : Page { }
    public partial class NORM_YearSetup
    {
        protected Panel InstallPanel=new Panel(), MessagePanel=new Panel(), ErrorPanel=new Panel();
        protected Literal MessageText=new Literal(), ErrorText=new Literal();
        protected Button SaveYearButton=new Button(),UploadPriorButton=new Button(),UploadBudgetButton=new Button(),SaveNameButton=new Button();
        protected TextBox CurrentFinancialYear=new TextBox(),ReportingName=new TextBox(),PriorYearStartPage=new TextBox(),BudgetStartPage=new TextBox();
        protected DropDownList EntitySelection=new DropDownList(),SetupSelection=new DropDownList();
        protected FileUpload PriorYearFile=new FileUpload(),BudgetFile=new FileUpload();
        public void Open()
        {
            var context=new HttpContext(new HttpRequest("","http://localhost/NORM/NORM_YearSetup.aspx",""),new HttpResponse(new StringWriter()));
            HttpContext.Current=context;
            typeof(Page).GetField("_context",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context);
            typeof(Page).GetField("_response",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context.Response);
            typeof(Page).GetField("_request",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(this,context.Request);
            Page_Load(this,EventArgs.Empty);
        }
        public void Save(string year) { CurrentFinancialYear.Text=year; SaveYearButton_Click(this,EventArgs.Empty); }
        public void Name(string name) { ReportingName.Text=name; SaveNameButton_Click(this,EventArgs.Empty); }
        public void RenderForTest() { OnPreRender(EventArgs.Empty); }
        public void NoFileUpload() { UploadPriorButton_Click(this,EventArgs.Empty); }
        public void SelectEntity(string e) { EntitySelection.SelectedValue=e; EntitySelection_Changed(this,EventArgs.Empty); }
        public void SelectYear(int id) { SetupSelection.SelectedValue=id.ToString(); SetupSelection_Changed(this,EventArgs.Empty); }
        public void ForgeSetup(int id) { SetupSelection.Items.Add(new ListItem("forged",id.ToString())); SetupSelection.SelectedValue=id.ToString(); }
        public string ErrorResult { get {return ErrorText.Text;} }
        public string Message { get {return MessageText.Text;} }
        public string DocumentsHtml {get {return PriorDocumentHtml+BudgetDocumentHtml;} }
        public int EntityCount {get {return EntitySelection.Items.Count;} }
    }
}
