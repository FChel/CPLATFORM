<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Review_Export" %>

using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Web;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Streams an Excel (.xlsx) export of every line in the reviewer's
    /// All Lines tab — one row per LINE in the package. Authenticates via
    /// the same token as the reviewer page itself; no admin gate.
    ///
    /// April 2026: expanded to include EVERY column on tblLPPI_Documents
    /// plus the full review audit (ReasonCode, Outcome, Comments, Objective
    /// Reference, Reviewed By, Reviewed Date) so reviewers can recompute
    /// baseline date, interest accrual etc. independently of the system.
    ///
    /// May 2026 — POC token support
    /// -------------------------------------------------------------------
    /// Accepts both AS Fin (package-level) and POC (POC-scoped) tokens.
    /// POC-token exports are filtered to documents whose first-line
    /// PocEmail matches the POC's email — same filter the reviewer page
    /// uses for the on-screen lines tab. The Excel file therefore mirrors
    /// what the POC sees, not the whole package.
    ///
    /// The reason code lives at DOCUMENT level (the reviewer codes only
    /// the first/dominant line, via the smallest-ItemSequence row), and
    /// every line of the same document inherits that code — driven via a
    /// correlated sub-query that maps each line to its first-line
    /// DocumentID and joins the review there. Same convention as
    /// LPPI_Review.aspx.cs detail query.
    ///
    /// Uses EPPlus 4.5.3.3 (LGPL). Do NOT swap this out for ClosedXML —
    /// it has caused dependency problems on the CPLATFORM server in the
    /// past.
    ///
    /// May 2026 — supersession model: tblLPPI_Documents can now hold
    /// multiple historical rows for the same (DocNoAccounting,
    /// ItemSequence) after RC-RL → reload cycles. The data-line INNER
    /// JOIN, the first-line MIN(DocumentID) review lookup, and the
    /// ORDER BY per-doc total subquery are all filtered to live rows
    /// (IsDeactivated = 0) so the export reflects current data only.
    ///
    /// Usage from client: window.location = 'LPPI_Review_Export.ashx?t=' + token;
    /// </summary>
    public class LPPI_Review_Export : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        private const string XlsxMimeType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public void ProcessRequest(HttpContext ctx)
        {
            string token = (ctx.Request.QueryString["t"] ?? "").Trim();
            if (token.Length == 0)
            {
                WriteError(ctx, "Missing token.");
                return;
            }

            // Resolve the token. Both AS Fin and POC tokens are accepted —
            // read-only export is harmless on every status, and a POC
            // export is just a filtered version of the same data.
            LPPIHelper.ReviewTokenInfo tokenInfo = LPPIHelper.ResolveReviewToken(token);
            if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.None)
            {
                WriteError(ctx, "Invalid link.");
                return;
            }

            int    packageId = tokenInfo.PackageID;
            bool   isPocView = (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.Poc);
            string pocEmail  = isPocView ? (tokenInfo.PocEmail ?? "") : null;

            // Look up the program name for the file name. Independent of
            // token kind.
            object progObj = LPPIHelper.ExecuteScalar(@"
                SELECT cm.Program
                FROM tblLPPI_ReviewPackages p
                INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                WHERE p.PackageID = @p",
                LPPIHelper.P("@p", packageId));
            if (progObj == null || progObj == DBNull.Value)
            {
                WriteError(ctx, "Invalid link.");
                return;
            }
            string program = Convert.ToString(progObj);

            // -----------------------------------------------------------------
            // Pull every column on tblLPPI_Documents for every line in the
            // package, joined to the per-document review (via the first-line
            // DocumentID) and the reason code lookup. ORDER BY total
            // interest DESC then DocNoAccounting/ItemSequence so the export
            // mirrors the screen ordering.
            //
            // POC view: an EXISTS clause restricts to documents whose
            // first-line PocEmail matches the POC. This filters at
            // document granularity — every line of an in-scope document
            // is included.
            // -----------------------------------------------------------------
            string sql = @"
                SELECT
                    -- Identity
                    d.DocumentID,
                    d.DocNoAccounting,
                    d.ItemSequence,
                    d.CompanyCode,
                    d.FiscalYear,
                    d.BatchID,

                    -- Vendor & PO
                    d.VendorNum,
                    d.VendorName,
                    d.VendorAcct,
                    d.VendorInvoiceNo,
                    d.PoNumber,
                    d.MaterialPo,
                    d.ContractNo,
                    d.VimDocumentId,

                    -- Account assignment
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

                    -- Values & flags
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

                    -- Dates
                    d.InvoiceReceivedDate,
                    d.InvoiceDate,
                    d.GrCreateDateLatest,
                    d.PaymentRunDate,
                    d.BodsPaymtBaselineDate,
                    d.ClearingMonth,

                    -- Late-payment numbers
                    d.DaysVariance,
                    d.DailyRate,
                    d.InvoiceInterestAmount,
                    d.InterestPayable,

                    -- Source system
                    d.SourceSystem,
                    d.PaymentChannel,
                    d.DocumentType,

                    -- Loaded / exported audit
                    d.FirstSeenDate,
                    d.ExportedDate,
                    d.ExportedBy,

                    -- Review (per document, inherited via first-line review)
                    rc.Code                             AS ReasonCode,
                    rc.Outcome                          AS ReasonOutcome,
                    rc.Description                      AS ReasonDescription,
                    r.Comments,
                    r.ObjectiveReference,
                    r.ReloadBaselineDate,
                    r.ReviewedByName,
                    r.ReviewedDate
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                       AND d.IsDeactivated  = 0
                LEFT  JOIN tblLPPI_Reviews r
                        ON r.DocumentID = (SELECT MIN(d3.DocumentID)
                                             FROM tblLPPI_Documents d3
                                            WHERE d3.DocNoAccounting = d.DocNoAccounting
                                              AND d3.IsDeactivated   = 0)
                LEFT  JOIN tblLPPI_ReasonCodes rc
                        ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE pd.PackageID = @p"
                + (isPocView
                    ? "  AND EXISTS (SELECT 1 FROM tblLPPI_Documents dPoc " +
                      "WHERE dPoc.DocNoAccounting = d.DocNoAccounting " +
                      "  AND dPoc.ItemSequence = 1 " +
                      "  AND LTRIM(RTRIM(dPoc.PocEmail)) = LTRIM(RTRIM(@poc)))"
                    : "")
                + @"
                ORDER BY
                    (SELECT SUM(d4.InterestPayable)
                       FROM tblLPPI_Documents d4
                      WHERE d4.DocNoAccounting = d.DocNoAccounting
                        AND d4.IsDeactivated   = 0) DESC,
                    d.DocNoAccounting,
                    d.ItemSequence";

            DataTable detail;
            if (isPocView)
                detail = LPPIHelper.ExecuteTable(sql,
                    LPPIHelper.P("@p",   packageId),
                    LPPIHelper.P("@poc", pocEmail));
            else
                detail = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@p", packageId));

            byte[] bytes = BuildWorkbook(detail);

            // File name reflects the scope so a POC's download does not
            // collide with the AS Fin one in the user's downloads folder.
            string scopeToken = isPocView
                ? SafeFileToken(program) + "_POC_" + SafeFileToken(pocEmail)
                : SafeFileToken(program);

            // EnvironmentFileTag returns e.g. "UAT_" / "PROD_" — prefixed
            // so admins can tell environments apart in their Downloads folder.
            string fileName = string.Format(CultureInfo.InvariantCulture,
                "{0}LPPI_Review_{1}_{2}.xlsx",
                LPPIHelper.EnvironmentFileTag,
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
        // Workbook construction (EPPlus 4.5.x LGPL)
        //
        // Column layout — 53 columns, grouped logically. Order matches the
        // SELECT projection above so the data-row loop can iterate the
        // DataRow's columns in DataTable order without re-mapping.
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
            "Proposed Baseline Date",
            "Reviewed By",
            "Reviewed Date"
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

                // Freeze the header row + first two columns (DocumentID and
                // DocNoAccounting) so the export remains scannable as
                // reviewers scroll through 50+ columns.
                ws.View.FreezePanes(2, 3);

                // Data rows. Project column-by-column so type-specific
                // formatting (dates, money, integers) is applied
                // consistently without per-column branches.
                int row = 2;
                foreach (DataRow r in dt.Rows)
                {
                    int col = 1;

                    // --- Identity ---
                    ws.Cells[row, col++].Value = AsInt(r, "DocumentID");
                    ws.Cells[row, col++].Value = AsString(r, "DocNoAccounting");
                    // ItemSequence rendered as zero-padded 3-digit string for
                    // consistency with the screen view ("001", "002"...).
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
                    PutDate(ws, row, col++, r, "InvoiceReceivedDate");
                    PutDate(ws, row, col++, r, "InvoiceDate");
                    PutDate(ws, row, col++, r, "GrCreateDateLatest");
                    PutDate(ws, row, col++, r, "PaymentRunDate");
                    PutDate(ws, row, col++, r, "BodsPaymtBaselineDate");
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
                    PutDate(ws, row, col++, r, "ReloadBaselineDate");
                    ws.Cells[row, col++].Value = AsString(r, "ReviewedByName");
                    PutDateTime(ws, row, col++, r, "ReviewedDate");

                    row++;
                }

                // Auto-fit columns. Cap the width so a stray long comment
                // does not blow the layout out to hundreds of pixels.
                if (ws.Dimension != null)
                {
                    ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 60);
                }

                // AutoFilter on the header row so reviewers can sort/filter
                // any column natively in Excel.
                ws.Cells[1, 1, 1, Headers.Length].AutoFilter = true;

                using (var ms = new MemoryStream())
                {
                    pkg.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        // -------------------------------------------------------------------
        // Type-aware cell writers — keep numbers/dates as native Excel types
        // so reviewers can sort, filter and re-calc against them.
        // -------------------------------------------------------------------

        private static void PutMoney(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return;
            ws.Cells[row, col].Value = Convert.ToDecimal(r[column]);
            ws.Cells[row, col].Style.Numberformat.Format = "#,##0.00";
        }

        private static void PutDecimal(ExcelWorksheet ws, int row, int col, DataRow r, string column, string format)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return;
            ws.Cells[row, col].Value = Convert.ToDecimal(r[column]);
            ws.Cells[row, col].Style.Numberformat.Format = format;
        }

        private static void PutDate(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return;
            ws.Cells[row, col].Value = Convert.ToDateTime(r[column]);
            ws.Cells[row, col].Style.Numberformat.Format = "dd/mm/yyyy";
        }

        private static void PutDateTime(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column) || r[column] == DBNull.Value) return;
            ws.Cells[row, col].Value = Convert.ToDateTime(r[column]);
            ws.Cells[row, col].Style.Numberformat.Format = "dd/mm/yyyy hh:mm";
        }

        private static string AsString(DataRow r, string column)
        {
            if (!r.Table.Columns.Contains(column)) return "";
            if (r[column] == DBNull.Value) return "";
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

        private static string AsItemSequenceText(DataRow r)
        {
            if (!r.Table.Columns.Contains("ItemSequence") || r["ItemSequence"] == DBNull.Value) return "";
            return string.Format(CultureInfo.InvariantCulture, "{0:000}", Convert.ToInt32(r["ItemSequence"]));
        }

        // Writes a DATE-only value (e.g. the RC-RL proposed baseline date) as
        // a native Excel date so it sorts and recalculates. Blank when null.
        private static void PutDate(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            object v = r[column];
            if (v == null || v == DBNull.Value) return;
            DateTime dt;
            if (v is DateTime) dt = (DateTime)v;
            else if (!DateTime.TryParse(Convert.ToString(v), out dt)) return;
            ws.Cells[row, col].Value = dt;
            ws.Cells[row, col].Style.Numberformat.Format = "yyyy-mm-dd";
        }

        // -------------------------------------------------------------------
        // Filename helper — sanitise the program / email for use in the
        // file name. Strips anything that is not alphanumeric / dash /
        // underscore and trims to 32 chars. "AIR FORCE" -> "AIRFORCE",
        // "name@defence.gov.au" -> "name_defence_gov_au".
        // -------------------------------------------------------------------
        private static string SafeFileToken(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "package";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string t = sb.ToString().Trim('_');
            return t.Length == 0 ? "package" : (t.Length > 32 ? t.Substring(0, 32) : t);
        }

        // -------------------------------------------------------------------
        // Error pages — same minimal HTML used by the legacy version.
        // -------------------------------------------------------------------
        private static void WriteError(HttpContext ctx, string message)
        {
            ctx.Response.Clear();
            ctx.Response.ContentType = "text/html";
            ctx.Response.Write("<html><body style=\"font-family:Arial,sans-serif;padding:24px;color:#a31b1b;\">");
            ctx.Response.Write("<h2>Export not available</h2><p>" + HttpUtility.HtmlEncode(message) + "</p>");
            ctx.Response.Write("</body></html>");
            ctx.Response.End();
        }
    }
}
