using System;
using System.Data;
using System.IO;
using System.Web;
using System.Web.UI.WebControls;

namespace CPlatform.NORM
{
    public partial class NORM_Import : NORMBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) { BindReleases(); }
        }

        protected void ImportButton_Click(object sender, EventArgs e)
        {
            ErrorPanel.Visible = false;
            try
            {
                int releaseId;
                if (!Int32.TryParse(ReleaseList.SelectedValue, out releaseId))
                {
                    throw new InvalidOperationException("Select an approved configuration release.");
                }
                object financialYearValue = NORMHelper.Scalar(
                    "SELECT FinancialYear FROM dbo.tblNORM_ConfigurationRelease " +
                    "WHERE ConfigurationReleaseId = @release AND StatusCode = 'Approved' AND IsDeactivated = 0",
                    NORMHelper.P("@release", releaseId));
                if (financialYearValue == null)
                {
                    throw new InvalidOperationException("Select an approved configuration release.");
                }
                int financialYear = Convert.ToInt32(financialYearValue);
                if (!TrialBalanceFile.HasFile) { throw new InvalidDataException("Choose the authoritative trial balance file for FY" + financialYear.ToString() + "."); }
                string sourceType = SourceTypeList.SelectedValue;
                if (sourceType == "ERP")
                {
                    ValidateExtension(TrialBalanceFile.FileName, ".xlsx", "The ERP trial balance must be an .xlsx workbook.");
                }
                else if (sourceType == "ROMAN")
                {
                    ValidateExtension(TrialBalanceFile.FileName, ".txt", "The historical ROMAN trial balance must be a .txt file.");
                }
                NORMImportOutcome outcome = NORMImportService.Import(TrialBalanceFile.FileBytes,
                    TrialBalanceFile.FileName, sourceType, releaseId, NORMHelper.CurrentUserId());
                Response.Redirect("NORM_Statements.aspx?run=" + outcome.CalculationRunId.ToString(), true);
            }
            catch (System.Threading.ThreadAbortException) { throw; }
            catch (Exception error)
            {
                ErrorPanel.Visible = true;
                ErrorMessage.Text = "<span>" + HttpUtility.HtmlEncode(error.Message) + "</span>";
            }
        }

        private void BindReleases()
        {
            DataTable releases = NORMHelper.Query(
                "SELECT ConfigurationReleaseId,FinancialYear,CAST(FinancialYear AS VARCHAR(4)) + ' ' + EntityCode + ' ' + VersionCode + ' - ' + ReleaseLabel AS DisplayLabel " +
                "FROM dbo.tblNORM_ConfigurationRelease WHERE StatusCode = 'Approved' AND IsDeactivated = 0 " +
                "ORDER BY FinancialYear DESC,EntityCode,VersionCode DESC");
            ReleaseList.Items.Clear();
            for (int i = 0; i < releases.Rows.Count; i++)
            {
                DataRow row = releases.Rows[i];
                ListItem item = new ListItem(NORMHelper.Str(row, "DisplayLabel"),
                    NORMHelper.Int(row, "ConfigurationReleaseId").ToString());
                item.Attributes["data-financial-year"] = NORMHelper.Int(row, "FinancialYear").ToString();
                ReleaseList.Items.Add(item);
            }
            if (ReleaseList.Items.Count == 0)
            {
                ReleaseList.Items.Add(new ListItem("No approved configuration release is installed", ""));
                ImportButton.Enabled = false;
            }
        }

        private static void ValidateExtension(string fileName, string requiredExtension, string message)
        {
            if (!String.Equals(Path.GetExtension(fileName), requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
