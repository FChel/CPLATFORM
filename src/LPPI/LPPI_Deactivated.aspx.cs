using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Admin watch-list of deactivated document lines.
    ///
    /// A line lands here when:
    ///   1. AS Fin or a POC selects RC-RL (Incorrect data, eligible for
    ///      reload) on a document in a package.
    ///   2. The package is finalised, at which point every line of that
    ///      document is stamped IsDeactivated = 1 by FinalisePackage.
    ///   3. No subsequent file load has yet supplied a row with the same
    ///      (DocNoAccounting, ItemSequence) — i.e. SupersededByDocumentID
    ///      is still NULL.
    ///
    /// Once a line is superseded by a fresh load, it is filtered out of
    /// this list (the supersession means the data has been corrected and
    /// re-loaded; the new row is now driving the workflow).
    ///
    /// Read-only — there are no write actions on this page. The intended
    /// admin response is to chase the data fix upstream (BODS / source
    /// system) and re-extract; the next file load handles the supersession
    /// transparently.
    /// </summary>
    public partial class LPPI_Deactivated : LPPIBasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                Bind();
            }
        }

        // -------------------------------------------------------------------
        // Group totals — projected during ItemDataBound so the per-CM
        // banner row can render the count and dollar total for that group
        // alongside the program code.
        // -------------------------------------------------------------------
        private Dictionary<string, int>     _groupCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, decimal> _groupAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        private string _lastGroup = null;

        private void Bind()
        {
            // Every deactivated, un-superseded line, joined to:
            //   - its document-level review (via the document's first-line
            //     DocumentID) for reviewer name / comments / obj ref
            //   - its current package for FinalisedDate / PackageID
            //
            // Filter rules:
            //   d.IsDeactivated = 1                          — must be deactivated
            //   d.SupersededByDocumentID IS NULL             — must not yet be superseded
            //
            // Sort: CM group (alpha), then DocNoAccounting, then ItemSequence
            // — same convention as the rest of the codebase.
            const string sql = @"
SELECT  d.DocumentID,
        d.DocNoAccounting,
        d.ItemSequence,
        d.CapabilityManagerProgram,
        d.VendorName,
        d.PoNumber,
        d.InterestPayable,
        r.Comments,
        r.ObjectiveReference,
        r.ReviewedByName,
        p.PackageID,
        p.FinalisedDate
  FROM  dbo.tblLPPI_Documents d
  LEFT JOIN dbo.tblLPPI_Reviews r
         ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                              FROM dbo.tblLPPI_Documents d2
                             WHERE d2.DocNoAccounting = d.DocNoAccounting)
  LEFT JOIN dbo.tblLPPI_ReviewPackageDocuments pd
         ON pd.DocumentID = (SELECT MIN(d3.DocumentID)
                               FROM dbo.tblLPPI_Documents d3
                              WHERE d3.DocNoAccounting = d.DocNoAccounting)
  LEFT JOIN dbo.tblLPPI_ReviewPackages p
         ON p.PackageID = pd.PackageID
        AND p.Status   IN ('Finalised','Exported')
 WHERE  d.IsDeactivated = 1
   AND  d.SupersededByDocumentID IS NULL
 ORDER BY d.CapabilityManagerProgram, d.DocNoAccounting, d.ItemSequence;";

            DataTable dt = LPPIHelper.ExecuteTable(sql);

            if (dt.Rows.Count == 0)
            {
                phEmpty.Visible   = true;
                phResults.Visible = false;
                phStats.Visible   = false;
                return;
            }

            // -----------------------------------------------------------------
            // Headline stats and per-CM totals.
            // -----------------------------------------------------------------
            int     lineCount = dt.Rows.Count;
            decimal total     = 0m;
            var     distinctDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var     distinctCms  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow r in dt.Rows)
            {
                string cm = AsString(r["CapabilityManagerProgram"]);
                decimal amt = AsDecimal(r["InterestPayable"]);

                total += amt;
                distinctDocs.Add(AsString(r["DocNoAccounting"]));
                if (!string.IsNullOrEmpty(cm)) distinctCms.Add(cm);

                if (!_groupCounts.ContainsKey(cm)) _groupCounts[cm]  = 0;
                if (!_groupAmounts.ContainsKey(cm)) _groupAmounts[cm] = 0m;
                _groupCounts[cm]  += 1;
                _groupAmounts[cm] += amt;
            }

            litLineCount.Text    = lineCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litDocCount.Text     = distinctDocs.Count.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litCmCount.Text      = distinctCms.Count.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            litTotalDollars.Text = LPPIHelper.FormatMoney(total);

            phStats.Visible   = true;
            phResults.Visible = true;
            phEmpty.Visible   = false;

            rptLines.DataSource = dt;
            rptLines.DataBind();
        }

        // -------------------------------------------------------------------
        // Per-row binding — emits the per-CM group banner row above the
        // first line of each new CM group.
        // -------------------------------------------------------------------
        protected void rptLines_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            if (e.Item.ItemType != ListItemType.Item &&
                e.Item.ItemType != ListItemType.AlternatingItem)
            {
                return;
            }

            DataRowView drv = e.Item.DataItem as DataRowView;
            if (drv == null) return;

            string cm = AsString(drv["CapabilityManagerProgram"]);

            // Show the banner only on the first row of each group.
            bool isFirstOfGroup = !string.Equals(cm, _lastGroup, StringComparison.OrdinalIgnoreCase);
            _lastGroup = cm;

            var phGroup = e.Item.FindControl("phGroup") as PlaceHolder;
            if (phGroup == null) return;

            if (!isFirstOfGroup)
            {
                phGroup.Visible = false;
                return;
            }

            phGroup.Visible = true;

            int     groupCount  = _groupCounts.ContainsKey(cm)  ? _groupCounts[cm]  : 0;
            decimal groupAmount = _groupAmounts.ContainsKey(cm) ? _groupAmounts[cm] : 0m;

            var litGroupCount   = phGroup.FindControl("litGroupCount")   as Literal;
            var litGroupPlural  = phGroup.FindControl("litGroupPlural")  as Literal;
            var litGroupAmount  = phGroup.FindControl("litGroupAmount")  as Literal;

            if (litGroupCount  != null) litGroupCount.Text  = groupCount.ToString("N0", CultureInfo.GetCultureInfo("en-AU"));
            if (litGroupPlural != null) litGroupPlural.Text = groupCount == 1 ? "" : "s";
            if (litGroupAmount != null) litGroupAmount.Text =
                groupAmount.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));
        }

        // -------------------------------------------------------------------
        // Tiny local conversion helpers — duplicated here rather than
        // hoisted into LPPIHelper because they are read-only DataRow
        // accessors that only this page uses.
        // -------------------------------------------------------------------
        private static string AsString(object v)
        {
            if (v == null || v == DBNull.Value) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static decimal AsDecimal(object v)
        {
            if (v == null || v == DBNull.Value) return 0m;
            return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        }
    }
}
