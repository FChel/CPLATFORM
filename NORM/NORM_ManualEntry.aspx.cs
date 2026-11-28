using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;

namespace CPlatform.NORM
{
    public partial class NORM_ManualEntry : NORMBasePage
    {
        protected string StatementUrl = "NORM_Statements.aspx";
        protected string ContextLabel = "", SelectorHtml = "", ScheduleHtml = "", EvidenceHtml = "", Notice = "";
        protected string FormToken = "", ScheduleToken = "";
        private NORMManualEntry.Schedule selected;
        private long evidenceId;
        private bool preservePostedValues;

        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            string requested = Request.QueryString["run"];
            int run;
            if (Request.HttpMethod == "POST" && requested != null &&
                (!Int32.TryParse(requested, out run) || !NORMHelper.CanAccessStatementRun(run)))
                throw new UnauthorizedAccessException();
            const string key = "NORM.ManualEntryFormToken";
            if (Session[key] == null) Session[key] = Guid.NewGuid().ToString("N");
            FormToken = (string)Session[key];
            ViewStateUserKey = Session.SessionID + "|" + NORMHelper.CurrentUserId();
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!NORMManualEntry.Installed()) { Notice = "Manual Entry is unavailable until its database migration is applied."; return; }
            List<DataRow> runs = NORMManualEntry.Runs();
            int run;
            if (Request.QueryString["run"] == null)
                run = runs.Count == 0 ? 0 : NORMHelper.Int(runs[0], "CalculationRunId");
            else if (!Int32.TryParse(Request.QueryString["run"], out run) ||
                !runs.Any(item => NORMHelper.Int(item, "CalculationRunId") == run))
                throw new UnauthorizedAccessException();
            if (run <= 0) { Notice = "No eligible calculation is available for a configured Manual Entry schedule."; return; }
            List<NORMManualEntry.Schedule> schedules = NORMManualEntry.Read(run);
            string code = Request.QueryString["schedule"];
            selected = code == null ? schedules.FirstOrDefault() : schedules.SingleOrDefault(item => item.Code == code);
            if (selected == null) { Notice = "Select an available schedule."; return; }
            if (Request.QueryString["evidence"] != null &&
                (!Int64.TryParse(Request.QueryString["evidence"], out evidenceId) || evidenceId <= 0))
                throw new HttpException(404, "Evidence was not found.");

            ContextLabel = selected.Entity + " \u00b7 " + selected.Stream + " \u00b7 FY" + selected.Year + " \u00b7 Run #" + run;
            StatementUrl = "NORM_Statements.aspx?run=" + run + "&manual=1#note-" +
                System.Text.RegularExpressions.Regex.Replace(selected.Layout.Note.ToLowerInvariant(), "[^a-z0-9]+", "-");
            var selectors = new StringBuilder();
            selectors.Append("<p><label for=\"manualRun\">Reporting year and calculation</label> <select id=\"manualRun\" onchange=\"location.href=this.value\">");
            foreach (DataRow item in runs)
            {
                int id = NORMHelper.Int(item, "CalculationRunId");
                Option(selectors, "NORM_ManualEntry.aspx?run=" + id, NORMHelper.Str(item, "EntityCode") + " \u00b7 FY" +
                    NORMHelper.Int(item, "FinancialYear") + " \u00b7 " + NORMHelper.Str(item, "ReportingStream") + " \u00b7 Run #" + id, id == run);
            }
            selectors.Append("</select> <label for=\"manualSchedule\">Schedule</label> <select id=\"manualSchedule\" onchange=\"location.href=this.value\">");
            foreach (NORMManualEntry.Schedule item in schedules)
                Option(selectors, Url(item), "Note " + item.Layout.Note + " \u00b7 " + item.Layout.Title, item.Code == selected.Code);
            selectors.Append("</select></p>");
            SelectorHtml = selectors.ToString();
        }

        protected void SaveSchedule_Click(object sender, EventArgs e)
        {
            if (selected == null || evidenceId > 0 || !String.Equals(FormToken, Request.Form["manualToken"], StringComparison.Ordinal) ||
                !NORMHelper.CanPrepareEntity(selected.Entity))
                throw new UnauthorizedAccessException();
            try
            {
                long recorded = NORMManualEntry.Save(selected.Run, selected.Code, Request.Form["manualContext"],
                    Request.Form["manualReason"], PostedEntry);
                Response.Redirect(Url(selected) + "&saved=" + recorded, false);
                Context.ApplicationInstance.CompleteRequest();
            }
            catch (NORMManualEntry.InputException error)
            {
                Notice = "Not saved. " + error.Message;
                preservePostedValues = true;
            }
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            if (selected == null) return;
            NORMManualEntry.Schedule displayed = selected;
            if (evidenceId > 0)
            {
                displayed = NORMManualEntry.Evidence(selected, evidenceId);
                Notice = "Recorded evidence #" + evidenceId + " \u00b7 Run #" + displayed.Run + ". Showing the values and reconciliation at capture.";
            }
            var history = new StringBuilder("<details><summary>Recorded evidence for this reporting year</summary><ul>");
            foreach (DataRow row in NORMManualEntry.Evidence(selected).Rows)
            {
                long id = NORMHelper.Long(row, "EvidenceId");
                history.Append("<li><a href=\"").Append(Enc(Url(selected) + "&evidence=" + id)).Append("\">Evidence #").Append(id)
                    .Append("</a> \u00b7 Run #").Append(NORMHelper.Int(row, "CalculationRunId")).Append(" \u00b7 ").Append(Enc(NORMHelper.Str(row, "RecordedBy"))).Append(" \u00b7 ")
                    .Append(Enc(Convert.ToDateTime(row["RecordedUtc"], CultureInfo.InvariantCulture).ToString("u", CultureInfo.InvariantCulture)));
                if (NORMHelper.Str(row, "EvidenceHash") != selected.ContextToken) history.Append(" \u00b7 Earlier inputs or results");
                history.Append("</li>");
                if (evidenceId == 0 && Notice.Length == 0 && Request.HttpMethod == "GET" &&
                    NORMHelper.Int(row, "CalculationRunId") == selected.Run &&
                    Request.QueryString["saved"] == id.ToString(CultureInfo.InvariantCulture))
                {
                    NORMManualEntry.Schedule saved = NORMManualEntry.Evidence(selected, id);
                    var outcome = (NORMManualEntry.Outcome)Math.Max(
                        (int)NORMManualEntry.Result(saved.Current), (int)NORMManualEntry.Result(saved.Prior));
                    Notice = "Saved \u2014 " + OutcomeLabel(outcome) + ". Recorded evidence #" + id + ".";
                }
            }
            history.Append("</ul></details>");
            if (evidenceId > 0) history.Append("<p><a href=\"").Append(Enc(Url(selected))).Append("\">Return to current schedule</a></p>");
            EvidenceHtml = history.ToString();
            ScheduleToken = preservePostedValues ? Request.Form["manualContext"] : selected.ContextToken;
            EditControls.Visible = evidenceId == 0;
            ScheduleHtml = RenderSchedule(displayed, EditControls.Visible);
        }

        private string RenderSchedule(NORMManualEntry.Schedule schedule, bool editable)
        {
            var layout = schedule.Layout;
            var current = preservePostedValues ? NORMManualEntry.Preview(schedule, false, PostedEntry) : schedule.Current;
            var prior = preservePostedValues ? NORMManualEntry.Preview(schedule, true, PostedEntry) : schedule.Prior;
            var html = new StringBuilder("<h2>Note ");
            html.Append(Enc(layout.Note)).Append(" \u00b7 ").Append(Enc(layout.Title)).Append("</h2><p>Entry unit ")
                .Append(Enc(layout.Unit)).Append(layout.Precision == 0 ? " \u00b7 Whole numbers" : " \u00b7 Up to " + layout.Precision + " decimal places")
                .Append(".</p><table class=\"norm-note-table norm-manual-table\"><thead><tr><th scope=\"col\">Description</th><th scope=\"col\">")
                .Append(Enc(layout.CurrentColumn)).Append(" FY").Append(schedule.Year).Append("</th><th scope=\"col\">")
                .Append(Enc(layout.PriorColumn)).Append(" FY").Append(schedule.Year - 1).Append("</th></tr></thead><tbody>");
            foreach (NORMManualEntry.Row row in layout.Rows)
            {
                html.Append(row.Total ? "<tr class=\"total\">" : "<tr>").Append("<th scope=\"row\">").Append(Enc(row.Label))
                    .Append("<small>").Append(Enc(row.Kind)).Append("</small></th>");
                RenderCell(html, current[row.Key], schedule.Year, row, false, editable);
                RenderCell(html, prior[row.Key], schedule.Year - 1, row, true, editable);
                html.Append("</tr>");
            }
            html.Append("</tbody></table>");
            if (layout.Splits != null && layout.Splits.Length != 0)
            {
                html.Append("<p><strong>Classification reconciliation</strong> \u00b7 FY").Append(schedule.Year).Append(" ")
                    .Append(ReconciliationLabel(current)).Append(" \u00b7 FY").Append(schedule.Year - 1).Append(" ").Append(ReconciliationLabel(prior))
                    .Append(". Supporting evidence is required for the classification.</p>");
            }
            return html.ToString();
        }

        private void RenderCell(StringBuilder html, NORMManualEntry.Cell cell, int year, NORMManualEntry.Row row, bool prior, bool editable)
        {
            html.Append("<td>");
            if (row.Kind == "Manual" && editable)
            {
                string amount = Field("amount", row.Key, prior);
                string source = Field("source", row.Key, prior);
                string label = row.Label + " FY" + year;
                string displayed = cell.Entered.HasValue ? Format(cell.Entered) : "";
                Input(html, amount, label + " amount", preservePostedValues ? Request.Form[amount] : displayed, 24, "decimal");
                Input(html, source, label + " supporting reference", preservePostedValues ? Request.Form[source] : cell.Source, 500, null);
            }
            else html.Append(Enc(Format(cell.Presented)));
            if (cell.Revision > 0)
            {
                string provenance = prior ? (cell.Amendment ? "Comparative amendment" : "Previous-year dataset") : "Current-year input";
                html.Append("<small>").Append(provenance)
                    .Append(cell.Issue == null && cell.Entered.HasValue ? " \u00b7 Prepared" : "")
                    .Append(" \u00b7 Revision #").Append(cell.Revision).Append(" \u00b7 ").Append(Enc(cell.User)).Append(" \u00b7 ").Append(Enc(cell.UpdatedUtc));
                if (cell.OriginalPriorRevision > 0) html.Append(" \u00b7 Original FY").Append(year).Append(" revision #").Append(cell.OriginalPriorRevision);
                if (!String.IsNullOrEmpty(cell.Reason)) html.Append("<br />Reason: ").Append(Enc(cell.Reason));
                if (!editable && !String.IsNullOrEmpty(cell.Source)) html.Append("<br />Source: ").Append(Enc(cell.Source));
                html.Append("</small>");
            }
            if (cell.Issue != null) html.Append("<small>").Append(Enc(cell.Issue)).Append("</small>");
            if (cell.Difference != null) html.Append("<small>").Append(Enc(cell.Difference)).Append("</small>");
            html.Append("</td>");
        }

        private static string ReconciliationLabel(Dictionary<string, NORMManualEntry.Cell> cells)
        {
            var outcome = NORMManualEntry.Result(cells);
            return outcome == NORMManualEntry.Outcome.Failed ? "failed" : OutcomeLabel(outcome);
        }

        private static string OutcomeLabel(NORMManualEntry.Outcome outcome)
        {
            return outcome == NORMManualEntry.Outcome.Failed ? "validation failed" : outcome.ToString().ToLowerInvariant();
        }

        private NORMManualEntry.Entry PostedEntry(string key, bool prior)
        {
            return new NORMManualEntry.Entry {
                Amount = Request.Form[Field("amount", key, prior)],
                Source = Request.Form[Field("source", key, prior)]
            };
        }
        private static void Option(StringBuilder html, string url, string label, bool selected)
        {
            html.Append("<option value=\"").Append(Enc(url)).Append(selected ? "\" selected>" : "\">").Append(Enc(label)).Append("</option>");
        }

        private static void Input(StringBuilder html, string key, string label, string value, int maximum, string mode)
        {
            html.Append("<input type=\"text\" name=\"").Append(Enc(key)).Append("\" aria-label=\"").Append(Enc(label))
                .Append("\" maxlength=\"").Append(maximum).Append("\" value=\"").Append(Enc(value));
            if (mode != null) html.Append("\" inputmode=\"").Append(Enc(mode));
            else html.Append("\" placeholder=\"Supporting reference");
            html.Append("\" />");
        }

        private static string Field(string kind, string key, bool prior) { return kind + (prior ? "_prior_" : "_current_") + key; }
        private static string Format(decimal? value) { return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "\u2014"; }
        private static string Url(NORMManualEntry.Schedule schedule)
        { return "NORM_ManualEntry.aspx?run=" + schedule.Run + "&schedule=" + HttpUtility.UrlEncode(schedule.Code); }
        protected static string Enc(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
    }
}
