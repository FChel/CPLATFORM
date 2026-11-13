using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Web;

namespace CPlatform.NORM
{
    public partial class NORM_SourceHistoryPage : NORMBasePage
    {
        protected string FiltersHtml="",ContextHtml="",PopulationHtml="",HistoryHtml="",ComparisonHtml="",MissingHtml="",ErrorHtml="",ReturnUrl="NORM_Import.aspx";
        protected void Page_Load(object sender,EventArgs e)
        {
            try { LoadHistory(); }
            catch(UnauthorizedAccessException error) { Fail(403,error.Message); }
            catch(InvalidOperationException error) { Fail(400,error.Message); }
        }
        private void Fail(int status,string message)
        {
            FiltersHtml=ContextHtml=PopulationHtml=HistoryHtml=ComparisonHtml=MissingHtml="";
            Response.StatusCode=status; Response.TrySkipIisCustomErrors=true;
            ErrorHtml="<p role=\"alert\">"+H(message)+"</p>";
        }
        private void LoadHistory()
        {
            DataTable releases=NORMSourceHistory.ApprovedReleases();
            int releaseId=OptionalId("release"), importId=OptionalId("import");
            string filter=Request.QueryString["viewStream"] ?? "";
            if(filter!="" && filter!="Departmental" && filter!="Administered") { throw new InvalidOperationException("Choose a supported stream filter."); }
            NORMSourceScope selected=null;
            if(importId>0)
            {
                string company=Request.QueryString["company"],ledger=Request.QueryString["ledger"],stream=Request.QueryString["stream"],absent=Request.QueryString["ledgerNull"];
                bool explicitScope=company!=null || ledger!=null || stream!=null || absent!=null;
                if(explicitScope && (String.IsNullOrWhiteSpace(company) || String.IsNullOrWhiteSpace(stream) ||
                    (absent=="1")== (ledger!=null) || (absent!=null && absent!="1") || ledger==""))
                { throw new InvalidOperationException("Select the complete company, optional ledger and stream identity."); }
                selected=NORMSourceHistory.ScopeForImport(importId,company,ledger,stream,explicitScope);
            }
            DataRow context=null;
            foreach(DataRow row in releases.Rows) { if(NORMHelper.Int(row,"ConfigurationReleaseId")==releaseId) { context=row; } }
            if(releaseId>0 && context==null) { throw new UnauthorizedAccessException("Select an accessible active approved release for the completeness context."); }
            if(context!=null && selected!=null && (!String.Equals(NORMHelper.Str(context,"EntityCode"),selected.Entity,StringComparison.OrdinalIgnoreCase) || NORMHelper.Int(context,"FinancialYear")!=selected.FinancialYear))
            { throw new InvalidOperationException("The source does not belong to the selected agency/year context."); }
            IList<NORMSourceVersion> versions=selected==null ? new List<NORMSourceVersion>() : NORMSourceHistory.Versions(selected);
            if(selected!=null && releaseId==0)
            {
                foreach(NORMSourceVersion version in versions)
                {
                    if(version.Scope.ImportId!=importId) { continue; }
                    foreach(DataRow row in releases.Rows) { if(NORMHelper.Int(row,"ConfigurationReleaseId")==version.ReleaseId) { context=row; releaseId=version.ReleaseId; } }
                }
            }
            FiltersHtml=Filters(releases,releaseId,filter);
            ReturnUrl="NORM_Import.aspx?release="+releaseId+"&viewStream="+U(filter);
            string entity=context!=null ? NORMHelper.Str(context,"EntityCode") : selected==null ? null : selected.Entity;
            int year=context!=null ? NORMHelper.Int(context,"FinancialYear") : selected==null ? 0 : selected.FinancialYear;
            if(entity==null)
            {
                ContextHtml="<p>Select an agency/year approved release. No newest release or calendar year is assumed.</p>";
                PopulationHtml="<p>Select a preparation context above.</p>";
            }
            else
            {
                ContextHtml="<p>Agency "+H(entity)+" / FY"+year+" / "+H(filter=="" ? "All streams" : filter)+"</p>";
                PopulationHtml=PopulationTable(NORMSourceHistory.CurrentPopulations(entity,year,filter),releaseId,filter);
            }
            if(selected==null) { HistoryHtml="<p>Select a current population to view its history.</p>"; ComparisonHtml="<p>Select an accepted source version.</p>"; }
            else
            {
                HistoryHtml="<p>"+H(ScopeLabel(selected))+"</p>"+HistoryTable(versions,importId,releaseId,filter);
                ComparisonHtml=Comparison(NORMSourceHistory.Compare(selected,importId));
            }
            if(context==null) { MissingHtml="<p>Select an active approved completeness release. Source history remains available independently of that selection.</p>"; }
            else
            {
                StringBuilder missing=new StringBuilder("<p>Completeness context: "+H(entity)+" / FY"+year+" / release "+releaseId+". Missing populations do not change accepted-source status.</p><ul>");
                int count=0;
                foreach(NORMSourceScope scope in NORMImportService.MissingRequiredPopulations(releaseId))
                { if(filter=="" || filter==scope.ReportingStream) { missing.Append("<li>").Append(H(ScopeLabel(scope))).Append("</li>"); count++; } }
                missing.Append("</ul>"); if(count==0) { missing.Append("<p>No required population is missing in this context.</p>"); } MissingHtml=missing.ToString();
            }
        }
        private static string Filters(DataTable releases,int selected,string stream)
        {
            StringBuilder html=new StringBuilder("<label for=\"release\">Approved completeness context</label><select class=\"norm-input\" name=\"release\" id=\"release\"><option value=\"0\">Select an approved agency/year release</option>");
            foreach(DataRow row in releases.Rows)
            {
                int id=NORMHelper.Int(row,"ConfigurationReleaseId");
                html.Append("<option value=\"").Append(id).Append(id==selected ? "\" selected>" : "\">").Append(H(NORMHelper.Str(row,"EntityCode")+" FY"+NORMHelper.Int(row,"FinancialYear")+" "+NORMHelper.Str(row,"VersionCode")+" (release "+id+")")).Append("</option>");
            }
            html.Append("</select><label for=\"viewStream\">Reporting stream</label><select class=\"norm-input\" name=\"viewStream\" id=\"viewStream\">");
            foreach(string value in new[] { "","Departmental","Administered" }) { html.Append("<option value=\"").Append(value).Append(value==stream ? "\" selected>" : "\">").Append(value=="" ? "All streams" : value).Append("</option>"); }
            return html.Append("</select>").ToString();
        }
        private static string PopulationTable(IList<NORMSourceScope> scopes,int release,string filter)
        {
            if(scopes.Count==0) { return "<p>No classified accepted population is present in this agency/year/stream context.</p>"; }
            StringBuilder html=new StringBuilder("<ul>");
            foreach(NORMSourceScope scope in scopes) { html.Append("<li><a href=\"").Append(H(HistoryUrl(scope,scope.ImportId,release,filter))).Append("\">").Append(H(ScopeLabel(scope))).Append(" — current import ").Append(scope.ImportId).Append("</a></li>"); }
            return html.Append("</ul>").ToString();
        }
        private static string HistoryTable(IList<NORMSourceVersion> versions,int selected,int release,string filter)
        {
            StringBuilder html=new StringBuilder("<div style=\"overflow-x:auto\"><table class=\"norm-table\"><thead><tr><th>Import / status</th><th>Release</th><th>Source / profile</th><th>Coverage</th><th>Accepted by / UTC</th><th>Predecessor</th><th>Original / SHA-256</th></tr></thead><tbody>");
            foreach(NORMSourceVersion version in versions)
            {
                html.Append("<tr><td><a href=\"").Append(H(HistoryUrl(version.Scope,version.Scope.ImportId,release,filter))).Append("\">Import ").Append(version.Scope.ImportId).Append("</a> ").Append(version.IsCurrent ? "Current" : "Superseded").Append(version.Scope.ImportId==selected ? " (selected)" : "").Append("</td><td>")
                    .Append(version.ReleaseId).Append(" ").Append(H(version.ConfigurationVersion)).Append("</td><td>").Append(H(version.SourceType+" / "+version.Profile)).Append("</td><td>").Append(H(NORMSourceHistory.Coverage(version))).Append("</td><td>").Append(H(version.AcceptedBy)).Append("<br>").Append(H(version.Scope.ImportedUtc.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture))).Append("</td><td>");
                if(version.PredecessorImportId>0) { html.Append("<a href=\"").Append(H(HistoryUrl(version.Scope,version.PredecessorImportId,release,filter))).Append("\">Import ").Append(version.PredecessorImportId).Append("</a>"); } else { html.Append("None"); }
                if(!String.IsNullOrEmpty(version.ReplacementEvidence)) { html.Append("<details><summary>Replacement audit</summary>").Append(H(version.ReplacementEvidence)).Append("</details>"); }
                html.Append("</td><td>");
                if(version.Files.Count==0) { html.Append("No retained file metadata available"); }
                foreach(NORMSourceFileEvidence file in version.Files) { html.Append("<a href=\"NORM_SourceFile.ashx?import=").Append(version.Scope.ImportId).Append("&amp;file=").Append(file.FileId).Append("\">").Append(H(file.FileName)).Append("</a><br><code>").Append(H(file.Hash)).Append("</code><br>"); }
                html.Append("</td></tr>");
            }
            return html.Append("</tbody></table></div>").ToString();
        }
        private static string Comparison(NORMSourceComparison comparison)
        {
            if(comparison.Previous==null) { return "<p>This is the first accepted original of this exact population; there is no predecessor to compare.</p>"; }
            StringBuilder html=new StringBuilder("<p>Import "+comparison.Current.Scope.ImportId+" versus immediate predecessor "+comparison.Previous.Scope.ImportId+". Signed movement = current balance minus previous balance.</p><h3>Structural changes</h3>");
            html.Append(ChangeTable(comparison.Structural,true));
            if(comparison.MetadataChanges.Count>0) { html.Append("<ul>"); foreach(string change in comparison.MetadataChanges) { html.Append("<li>").Append(H(change)).Append("</li>"); } html.Append("</ul>"); }
            html.Append("<h3>Financial movements</h3>").Append(ChangeTable(comparison.Financial,false));
            html.Append("<p>Previous net: ").Append(Amount(comparison.PreviousNet)).Append("; current net: ").Append(Amount(comparison.CurrentNet)).Append("; net movement: ").Append(Amount(comparison.NetMovement))
                .Append("; positive movements: ").Append(Amount(comparison.PositiveMovements)).Append("; negative movements: ").Append(Amount(comparison.NegativeMovements)).Append(".</p>");
            return html.ToString();
        }
        private static string ChangeTable(IList<NORMSourceAccountChange> changes,bool structural)
        {
            if(changes.Count==0) { return structural ? "<p>No account structure or description changes.</p>" : "<p>No financial balance movements.</p>"; }
            StringBuilder html=new StringBuilder("<div style=\"overflow-x:auto\"><table class=\"norm-table\"><thead><tr><th>Account</th>");
            html.Append(structural ? "<th>Change</th><th>Previous description</th><th>Current description</th>" : "<th>Previous balance</th><th>Current balance</th><th>Signed movement</th>").Append("<th>Retained source rows</th></tr></thead><tbody>");
            foreach(NORMSourceAccountChange change in changes)
            {
                html.Append("<tr><td>").Append(H(change.Account)).Append("</td><td>");
                if(structural) { html.Append(H(change.StructuralChange)).Append("</td><td>").Append(H(change.Previous==null ? "Not present" : change.Previous.Description)).Append("</td><td>").Append(H(change.Current==null ? "Not present" : change.Current.Description)); }
                else { html.Append(change.Previous==null ? "Not present" : Amount(change.Previous.Balance)).Append("</td><td>").Append(change.Current==null ? "Not present" : Amount(change.Current.Balance)).Append("</td><td>").Append(Amount(change.Difference)); }
                html.Append("</td><td>").Append(H(RowLabel(change.Previous,"Previous")+"; "+RowLabel(change.Current,"Current"))).Append("</td></tr>");
            }
            return html.Append("</tbody></table></div>").ToString();
        }
        private static string RowLabel(NORMSourceAccount row,string label) { return row==null ? label+": not present" : label+": source row "+row.SourceRowNo+" (TB row "+row.RowId+")"; }
        private int OptionalId(string name)
        { string value=Request.QueryString[name]; int id; if(String.IsNullOrEmpty(value)) { return 0; } if(!Int32.TryParse(value,out id) || id<0) { throw new InvalidOperationException("Select a valid "+name+" identifier."); } return id; }
        private static string ScopeLabel(NORMSourceScope scope) { return scope.Entity+" / FY"+scope.FinancialYear+" / company "+scope.Company+" / ledger "+(scope.Ledger ?? "Not applicable (NULL)")+" / "+scope.ReportingStream; }
        private static string HistoryUrl(NORMSourceScope scope,int import,int release,string filter)
        { return "NORM_SourceHistory.aspx?import="+import+"&company="+U(scope.Company)+"&stream="+U(scope.ReportingStream)+(scope.Ledger==null ? "&ledgerNull=1" : "&ledger="+U(scope.Ledger))+"&release="+release+"&viewStream="+U(filter); }
        private static string H(string text) { return HttpUtility.HtmlEncode(text ?? ""); }
        private static string U(string text) { return HttpUtility.UrlEncode(text ?? ""); }
        private static string Amount(decimal amount) { return amount.ToString("0.00",CultureInfo.InvariantCulture); }
    }
}
