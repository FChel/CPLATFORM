<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Summary_Export" %>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Admin-auth export handler for the Summary page. Streams an .xlsx
    /// containing every line of every document in the currently-selected
    /// scope — the same 53-column layout as LPPI_Review_Export.ashx,
    /// expanded to all in-scope documents instead of one package.
    ///
    /// Distinct from LPPI_Review_Export.ashx by design:
    ///   - This handler is admin-only (LPPIHelper.IsAdminUser gate).
    ///   - The reviewer-page handler is token-only (AS Fin or POC).
    ///   - Filename, scope semantics, and query shape differ.
    /// The two handlers happen to share an output column layout. Per the
    /// "no shims" rule the column list and workbook builder are
    /// duplicated here rather than refactored across both. Diverge the
    /// shape in one handler without ceremony.
    ///
    /// QUERY STRING
    ///   s = scope value matching the LPPI_Summary.aspx dropdown:
    ///         "active"        — Current cycle (default if missing/invalid)
    ///         "all"           — All active
    ///         "B<batchId>"    — Batch #<batchId>
    ///
    /// RESPONSE
    ///   200 — Content-Type: xlsx, body = file bytes.
    ///   403 — caller is not an admin.
    ///   500 — build / SQL failure (rare; surfaces as a plain-text page).
    ///
    /// Uses EPPlus 4.5.3.3 (LGPL). Do NOT swap to ClosedXML.
    /// </summary>
    public class LPPI_Summary_Export : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        private const string XlsxMimeType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private const string ScopeValueActive       = "active";
        private const string ScopeValueAll          = "all";
        private const string ScopeValueBatchPrefix  = "B";

        public void ProcessRequest(HttpContext ctx)
        {
            // Admin gate. Handlers are not pages so they cannot inherit
            // LPPIBasePage — we replicate the same gate via the helper.
            // Same pattern as LPPI_Export_Download.ashx.
            if (!LPPIHelper.IsAdminUser())
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Forbidden — admin access required.");
                ctx.Response.End();
                return;
            }

            // Resolve the scope from the query string.
            string scopeValue = (ctx.Request.QueryString["s"] ?? "").Trim();
            LPPIHelper.SummaryScope scope = ParseScope(scopeValue);
            string scopeToken = ScopeFilenameToken(scopeValue, scope);

            // Resolve scope -> concrete PackageID list. This protects the
            // export against scope drift mid-build (an active package
            // flipping to Exported between the resolve and the SELECT
            // would otherwise change the row set), and gives us a clean
            // IN-list for the data query.
            List<int> packageIds;
            try
            {
                packageIds = LPPIHelper.GetSummaryScopePackageIds(scope);
            }
            catch (Exception ex)
            {
                WriteError(ctx, "Could not resolve scope: " + ex.Message);
                return;
            }

            // Empty scope still produces a header-only workbook so the
            // operator gets a clear "no rows" file rather than a
            // failure — same UX as a CSV export with no matches.
            DataTable dt;
            try
            {
                dt = LoadData(packageIds);
            }
            catch (Exception ex)
            {
                WriteError(ctx, "Export query failed: " + ex.Message);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = BuildWorkbook(dt);
            }
            catch (Exception ex)
            {
                WriteError(ctx, "Workbook build failed: " + ex.Message);
                return;
            }

            string fileName = string.Format(CultureInfo.InvariantCulture,
                "LPPI_Summary_{0}_{1}.xlsx",
                scopeToken,
                DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture));

            ctx.Response.Clear();
            ctx.Response.ContentType = XlsxMimeType;
            ctx.Response.AppendHeader("Content-Disposition",
                "attachment; filename=\"" + fileName + "\"");
            ctx.Response.AppendHeader("Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture));
            ctx.Response.BinaryWrite(bytes);
            ctx.Response.End();
        }

        // -------------------------------------------------------------------
        // Scope parsing — accept the same dropdown values as LPPI_Summary.aspx.
        // Unknown or missing values fall back to Active.
        // -------------------------------------------------------------------
        private static LPPIHelper.SummaryScope ParseScope(string value)
        {
            if (string.IsNullOrEmpty(value)) return LPPIHelper.SummaryScope.CurrentCycle();

            if (string.Equals(value, ScopeValueAll, StringComparison.OrdinalIgnoreCase))
                return LPPIHelper.SummaryScope.AllActive();

            if (value.StartsWith(ScopeValueBatchPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int batchId;
                if (int.TryParse(value.Substring(ScopeValueBatchPrefix.Length),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out batchId)
                    && batchId > 0)
                {
                    return LPPIHelper.SummaryScope.ForBatch(batchId);
                }
            }

            return LPPIHelper.SummaryScope.CurrentCycle();
        }

        private static string ScopeFilenameToken(string rawValue, LPPIHelper.SummaryScope scope)
        {
            switch (scope.Kind)
            {
                case LPPIHelper.SummaryScopeKind.Batch:
                    return "Batch" + (scope.BatchID.HasValue
                        ? scope.BatchID.Value.ToString(CultureInfo.InvariantCulture)
                        : "0");
                case LPPIHelper.SummaryScopeKind.All:
                    return "AllActive";
                case LPPIHelper.SummaryScopeKind.Active:
                default:
                    return "ActiveCycle";
            }
        }

        // -------------------------------------------------------------------
        // Data query — same 53-column projection as LPPI_Review_Export.ashx
        // but the WHERE filter is a variable-length IN-list of PackageIDs
        // rather than a single @p, and there's no POC filter branch.
        //
        // First-line review pattern + IsDeactivated = 0 on every document
        // reference, same as the rest of the codebase.
        // -------------------------------------------------------------------
        private static DataTable LoadData(List<int> packageIds)
        {
            if (packageIds == null || packageIds.Count == 0)
            {
                // Return an empty DataTable with the right columns so the
                // workbook builder can still emit a header-only file.
                return BuildEmptyShape();
            }

            // Build the IN clause — one ? placeholder per package id.
            var inPlaceholders = new StringBuilder();
            for (int i = 0; i < packageIds.Count; i++)
            {
                if (i > 0) inPlaceholders.Append(",");
                inPlaceholders.Append("@P").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            string sql = @"
SELECT
    d.DocumentID,
    d.DocNoAccounting,
    d.ItemSequence,
    d.CompanyCode,
    d.FiscalYear,
    d.BatchID,

    d.VendorNum,
    d.VendorName,
    d.VendorAcct,
    d.VendorInvoiceNo,
    d.PoNumber,
    d.MaterialPo,
    d.ContractNo,
    d.VimDocumentId,

    d.WbsElement,
    d.WbsDesc,
    d.Capex,
    d.ProfitCentre,
    d.GlAccount,
    d.TaxCode,
    d.CapabilityManager,
    d.CapabilityManagerName,
    d.CapabilityManagerProgram,
    d.DeliveryManager,
    d.DeliveryManagerName,
    d.DeliveryManagerProgram,
    d.PocEmail,

    d.Currency,
    d.GlLineValueInclGst,
    d.InvoiceValueInclGst,
    d.ContractValueLocExGst,
    d.PaymentTerms,
    d.ExclusionFlag,
    d.ExclusionTest,
    d.ExclusionDescriptor,
    d.PossiblePayment,
    d.PossibleDuplicateClearing,

    d.InvoiceReceivedDate,
    d.InvoiceDate,
    d.GrCreateDateLatest,
    d.PaymentRunDate,
    d.BodsPaymtBaselineDate,
    d.ClearingMonth,

    d.DaysVariance,
    d.DailyRate,
    d.InvoiceInterestAmount,
    d.InterestPayable,

    d.SourceSystem,
    d.PaymentChannel,
    d.DocumentType,

    d.FirstSeenDate,
    d.ExportedDate,
    d.ExportedBy,

    rc.Code         AS ReasonCode,
    rc.Outcome      AS ReasonOutcome,
    rc.Description  AS ReasonDescription,
    r.Comments,
    r.ObjectiveReference,
    r.ReviewedByName,
    r.ReviewedDate
  FROM dbo.tblLPPI_ReviewPackageDocuments pd
  INNER JOIN dbo.tblLPPI_Documents d
          ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                    FROM dbo.tblLPPI_Documents d2
                                   WHERE d2.DocumentID = pd.DocumentID)
         AND d.IsDeactivated   = 0
  LEFT  JOIN dbo.tblLPPI_Reviews r
          ON r.DocumentID = (SELECT MIN(d3.DocumentID)
                               FROM dbo.tblLPPI_Documents d3
                              WHERE d3.DocNoAccounting = d.DocNoAccounting
                                AND d3.IsDeactivated   = 0)
  LEFT  JOIN dbo.tblLPPI_ReasonCodes rc
          ON rc.ReasonCodeID = r.ReasonCodeID
 WHERE pd.PackageID IN (" + inPlaceholders.ToString() + @")
 ORDER BY
    (SELECT SUM(d4.InterestPayable)
       FROM dbo.tblLPPI_Documents d4
      WHERE d4.DocNoAccounting = d.DocNoAccounting
        AND d4.IsDeactivated   = 0) DESC,
    d.DocNoAccounting,
    d.ItemSequence;";

            var parms = new List<OleDbParameter>(packageIds.Count);
            for (int i = 0; i < packageIds.Count; i++)
            {
                parms.Add(LPPIHelper.P("@P" + i.ToString(CultureInfo.InvariantCulture), packageIds[i]));
            }

            // De-duplicate. A document can be linked to a package through
            // multiple pd rows (one per line in the package's snapshot at
            // load time); the join above already collapses to live lines
            // via the DocNoAccounting predicate, but the same (live)
            // DocumentID can still be hit through more than one pd row
            // when several lines of one document are all in scope across
            // overlapping packages. Distinct in SQL is cleaner here.
            sql = "WITH Raw AS (" + sql.TrimEnd(';') + ") SELECT DISTINCT * FROM Raw ORDER BY DocNoAccounting, ItemSequence;";

            return LPPIHelper.ExecuteTable(sql, parms.ToArray());
        }

        private static DataTable BuildEmptyShape()
        {
            // Cheap way to materialise an empty DataTable with the right
            // columns — the workbook builder only needs Columns to write
            // a header row.
            var dt = new DataTable();
            foreach (string c in DataColumns) dt.Columns.Add(c, typeof(object));
            return dt;
        }

        // -------------------------------------------------------------------
        // Workbook construction — mirrors LPPI_Review_Export.ashx's layout.
        // -------------------------------------------------------------------

        private static readonly string[] Headers = new[]
        {
            // Identity
            "Document ID",
            "Document No.",
            "Item Sequence",
            "Company Code",
            "Fiscal Year",
            "Batch ID",

            // Vendor & PO
            "Vendor Number",
            "Vendor Name",
            "Vendor Account",
            "Vendor Invoice No",
            "PO Number",
            "Material PO",
            "Contract No",
            "VIM Document ID",

            // Account assignment
            "WBS Element",
            "WBS Description",
            "Capex",
            "Profit Centre",
            "GL Account",
            "Tax Code",
            "Capability Manager",
            "Capability Manager Name",
            "Capability Manager Program",
            "Delivery Manager",
            "Delivery Manager Name",
            "Delivery Manager Program",
            "POC Email",

            // Values & flags
            "Currency",
            "GL Line Value (Incl GST)",
            "Invoice Value (Incl GST)",
            "Contract Value (Loc Ex GST)",
            "Payment Terms",
            "Exclusion Flag",
            "Exclusion Test",
            "Exclusion Descriptor",
            "Possible Payment",
            "Possible Duplicate Clearing",

            // Dates
            "Invoice Received Date",
            "Invoice Date",
            "GR Create Date (Latest)",
            "Payment Run Date",
            "BODS Payment Baseline Date",
            "Clearing Month",

            // Late-payment numbers
            "Days Late",
            "Daily Rate",
            "Invoice Interest Amount",
            "Interest Payable",

            // Source system
            "Source System",
            "Payment Channel",
            "Document Type",

            // Loaded / exported audit
            "First Seen (FinHub)",
            "Exported Date",
            "Exported By",

            // Review
            "Reason Code",
            "Outcome",
            "Reason Description",
            "Comments",
            "Objective Reference",
            "Reviewed By",
            "Reviewed Date"
        };

        // Column names in the same order as Headers — used by
        // BuildEmptyShape and the data-row loop in BuildWorkbook.
        private static readonly string[] DataColumns = new[]
        {
            "DocumentID", "DocNoAccounting", "ItemSequence", "CompanyCode", "FiscalYear", "BatchID",
            "VendorNum", "VendorName", "VendorAcct", "VendorInvoiceNo", "PoNumber", "MaterialPo", "ContractNo", "VimDocumentId",
            "WbsElement", "WbsDesc", "Capex", "ProfitCentre", "GlAccount", "TaxCode",
            "CapabilityManager", "CapabilityManagerName", "CapabilityManagerProgram",
            "DeliveryManager", "DeliveryManagerName", "DeliveryManagerProgram", "PocEmail",
            "Currency", "GlLineValueInclGst", "InvoiceValueInclGst", "ContractValueLocExGst",
            "PaymentTerms", "ExclusionFlag", "ExclusionTest", "ExclusionDescriptor",
            "PossiblePayment", "PossibleDuplicateClearing",
            "InvoiceReceivedDate", "InvoiceDate", "GrCreateDateLatest", "PaymentRunDate", "BodsPaymtBaselineDate", "ClearingMonth",
            "DaysVariance", "DailyRate", "InvoiceInterestAmount", "InterestPayable",
            "SourceSystem", "PaymentChannel", "DocumentType",
            "FirstSeenDate", "ExportedDate", "ExportedBy",
            "ReasonCode", "ReasonOutcome", "ReasonDescription", "Comments", "ObjectiveReference", "ReviewedByName", "ReviewedDate"
        };

        private static byte[] BuildWorkbook(DataTable dt)
        {
            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("All Lines");

                // Header row.
                for (int i = 0; i < Headers.Length; i++)
                    ws.Cells[1, i + 1].Value = Headers[i];

                using (var hdr = ws.Cells[1, 1, 1, Headers.Length])
                {
                    hdr.Style.Font.Bold = true;
                    hdr.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hdr.Style.Fill.BackgroundColor.SetColor(System.Drawing.ColorTranslator.FromHtml("#fff7ed"));
                    hdr.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    hdr.Style.Border.Bottom.Color.SetColor(System.Drawing.ColorTranslator.FromHtml("#d75b07"));
                }

                ws.View.FreezePanes(2, 3);

                int row = 2;
                foreach (DataRow r in dt.Rows)
                {
                    int col = 1;

                    // --- Identity ---
                    ws.Cells[row, col++].Value = AsInt(r, "DocumentID");
                    ws.Cells[row, col++].Value = AsString(r, "DocNoAccounting");
                    ws.Cells[row, col++].Value = AsItemSequenceText(r);
                    ws.Cells[row, col++].Value = AsString(r, "CompanyCode");
                    ws.Cells[row, col++].Value = AsString(r, "FiscalYear");
                    ws.Cells[row, col++].Value = AsInt(r, "BatchID");

                    // --- Vendor & PO ---
                    ws.Cells[row, col++].Value = AsString(r, "VendorNum");
                    ws.Cells[row, col++].Value = AsString(r, "VendorName");
                    ws.Cells[row, col++].Value = AsString(r, "VendorAcct");
                    ws.Cells[row, col++].Value = AsString(r, "VendorInvoiceNo");
                    ws.Cells[row, col++].Value = AsString(r, "PoNumber");
                    ws.Cells[row, col++].Value = AsString(r, "MaterialPo");
                    ws.Cells[row, col++].Value = AsString(r, "ContractNo");
                    ws.Cells[row, col++].Value = AsString(r, "VimDocumentId");

                    // --- Account assignment ---
                    ws.Cells[row, col++].Value = AsString(r, "WbsElement");
                    ws.Cells[row, col++].Value = AsString(r, "WbsDesc");
                    ws.Cells[row, col++].Value = AsString(r, "Capex");
                    ws.Cells[row, col++].Value = AsString(r, "ProfitCentre");
                    ws.Cells[row, col++].Value = AsString(r, "GlAccount");
                    ws.Cells[row, col++].Value = AsString(r, "TaxCode");
                    ws.Cells[row, col++].Value = AsString(r, "CapabilityManager");
                    ws.Cells[row, col++].Value = AsString(r, "CapabilityManagerName");
                    ws.Cells[row, col++].Value = AsString(r, "CapabilityManagerProgram");
                    ws.Cells[row, col++].Value = AsString(r, "DeliveryManager");
                    ws.Cells[row, col++].Value = AsString(r, "DeliveryManagerName");
                    ws.Cells[row, col++].Value = AsString(r, "DeliveryManagerProgram");
                    ws.Cells[row, col++].Value = AsString(r, "PocEmail");

                    // --- Values & flags ---
                    ws.Cells[row, col++].Value = AsString(r, "Currency");
                    PutMoney(ws, row, col++, r, "GlLineValueInclGst");
                    PutMoney(ws, row, col++, r, "InvoiceValueInclGst");
                    PutMoney(ws, row, col++, r, "ContractValueLocExGst");
                    ws.Cells[row, col++].Value = AsString(r, "PaymentTerms");
                    ws.Cells[row, col++].Value = AsString(r, "ExclusionFlag");
                    ws.Cells[row, col++].Value = AsString(r, "ExclusionTest");
                    ws.Cells[row, col++].Value = AsString(r, "ExclusionDescriptor");
                    ws.Cells[row, col++].Value = AsString(r, "PossiblePayment");
                    ws.Cells[row, col++].Value = AsString(r, "PossibleDuplicateClearing");

                    // --- Dates ---
                    PutDateTime(ws, row, col++, r, "InvoiceReceivedDate");
                    PutDateTime(ws, row, col++, r, "InvoiceDate");
                    PutDateTime(ws, row, col++, r, "GrCreateDateLatest");
                    PutDateTime(ws, row, col++, r, "PaymentRunDate");
                    PutDateTime(ws, row, col++, r, "BodsPaymtBaselineDate");
                    ws.Cells[row, col++].Value = AsString(r, "ClearingMonth");

                    // --- Late-payment numbers ---
                    ws.Cells[row, col++].Value = AsNullableInt(r, "DaysVariance");
                    PutDecimal(ws, row, col++, r, "DailyRate", "#,##0.00000000");
                    PutMoney(ws, row, col++, r, "InvoiceInterestAmount");
                    PutMoney(ws, row, col++, r, "InterestPayable");

                    // --- Source system ---
                    ws.Cells[row, col++].Value = AsString(r, "SourceSystem");
                    ws.Cells[row, col++].Value = AsString(r, "PaymentChannel");
                    ws.Cells[row, col++].Value = AsString(r, "DocumentType");

                    // --- Loaded / exported audit ---
                    PutDateTime(ws, row, col++, r, "FirstSeenDate");
                    PutDateTime(ws, row, col++, r, "ExportedDate");
                    ws.Cells[row, col++].Value = AsString(r, "ExportedBy");

                    // --- Review ---
                    ws.Cells[row, col++].Value = AsString(r, "ReasonCode");
                    ws.Cells[row, col++].Value = AsString(r, "ReasonOutcome");
                    ws.Cells[row, col++].Value = AsString(r, "ReasonDescription");
                    ws.Cells[row, col++].Value = AsString(r, "Comments");
                    ws.Cells[row, col++].Value = AsString(r, "ObjectiveReference");
                    ws.Cells[row, col++].Value = AsString(r, "ReviewedByName");
                    PutDateTime(ws, row, col++, r, "ReviewedDate");

                    row++;
                }

                if (ws.Dimension != null)
                {
                    ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 60);
                }

                ws.Cells[1, 1, 1, Headers.Length].AutoFilter = true;

                using (var ms = new MemoryStream())
                {
                    pkg.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        // -------------------------------------------------------------------
        // Cell writers — keep numbers / dates as native Excel types.
        // -------------------------------------------------------------------

        private static void PutMoney(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            decimal? d = AsDecimal(r, column);
            if (d.HasValue)
            {
                ws.Cells[row, col].Value = d.Value;
                ws.Cells[row, col].Style.Numberformat.Format = "#,##0.00";
            }
        }

        private static void PutDecimal(ExcelWorksheet ws, int row, int col, DataRow r, string column, string format)
        {
            decimal? d = AsDecimal(r, column);
            if (d.HasValue)
            {
                ws.Cells[row, col].Value = d.Value;
                ws.Cells[row, col].Style.Numberformat.Format = format;
            }
        }

        private static void PutDateTime(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return;
            DateTime d;
            object v = r[column];
            if (v is DateTime) d = (DateTime)v;
            else if (!DateTime.TryParse(Convert.ToString(v), CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out d)) return;
            ws.Cells[row, col].Value = d;
            ws.Cells[row, col].Style.Numberformat.Format = "yyyy-mm-dd";
        }

        private static string AsString(DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return "";
            return Convert.ToString(r[column]);
        }

        private static int? AsInt(DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return null;
            return Convert.ToInt32(r[column]);
        }

        private static int? AsNullableInt(DataRow r, string column)
        {
            return AsInt(r, column);
        }

        private static decimal? AsDecimal(DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return null;
            return Convert.ToDecimal(r[column]);
        }

        private static string AsItemSequenceText(DataRow r)
        {
            if (!r.Table.Columns.Contains("ItemSequence") || r["ItemSequence"] == DBNull.Value) return "";
            return string.Format(CultureInfo.InvariantCulture, "{0:000}", Convert.ToInt32(r["ItemSequence"]));
        }

        // -------------------------------------------------------------------
        // Error page — plain text rather than the WriteError HTML used by
        // the token-auth handler, because admin users will be looking at
        // it through normal browser UI rather than an emailed link.
        // -------------------------------------------------------------------
        private static void WriteError(HttpContext ctx, string message)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "text/html";
            ctx.Response.Write("<html><body style=\"font-family:Arial,sans-serif;padding:24px;color:#a31b1b;\">");
            ctx.Response.Write("<h2>Summary export failed</h2><p>" + HttpUtility.HtmlEncode(message) + "</p>");
            ctx.Response.Write("</body></html>");
            ctx.Response.End();
        }
    }
}
