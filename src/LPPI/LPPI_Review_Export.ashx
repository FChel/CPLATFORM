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
    /// All Lines tab — one row per LINE, exactly matching what is shown in
    /// the read-only detail table. Authenticates via the same token as the
    /// reviewer page itself; no admin gate.
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

            // Resolve the package from the token. Any non-invalid token is
            // accepted — the reviewer page already renders for every status,
            // and read-only export is harmless on Complete/Cancelled.
            DataTable pkg = LPPIHelper.ExecuteTable(@"
                SELECT p.PackageID, p.Status, cm.Program
                FROM tblLPPI_ReviewPackages p
                INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                WHERE p.Token = @t",
                LPPIHelper.P("@t", token));

            if (pkg.Rows.Count != 1)
            {
                WriteError(ctx, "Invalid link.");
                return;
            }

            int    packageId = Convert.ToInt32(pkg.Rows[0]["PackageID"]);
            string program   = Convert.ToString(pkg.Rows[0]["Program"]);

            // Pull the same per-line dataset the All Lines tab renders.
            // Mirrors the SQL in LPPI_Review.aspx.cs.LoadDocuments (detail).
            DataTable detail = LPPIHelper.ExecuteTable(@"
                SELECT
                    d.DocNoAccounting,
                    d.ItemSequence,
                    d.CompanyCode,
                    d.VendorName,
                    d.VendorNum,
                    d.PoNumber,
                    d.ClearingMonth,
                    d.WbsElement,
                    d.WbsDesc,
                    d.GlAccount,
                    d.ProfitCentre,
                    d.TaxCode,
                    d.DeliveryManager,
                    d.DeliveryManagerName,
                    d.DeliveryManagerProgram,
                    d.PocEmail,
                    d.PaymentRunDate,
                    d.DaysVariance,
                    d.InterestPayable,
                    r.Comments,
                    r.ObjectiveReference,
                    r.ReviewedByName,
                    r.ReviewedDate,
                    rc.Code AS ReasonCode,
                    rc.Outcome AS ReasonOutcome
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                LEFT  JOIN tblLPPI_Reviews r
                        ON r.DocumentID = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc
                        ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE pd.PackageID = @p
                ORDER BY
                    (SELECT SUM(d3.InterestPayable)
                       FROM tblLPPI_Documents d3
                      WHERE d3.DocNoAccounting = d.DocNoAccounting) DESC,
                    d.DocNoAccounting,
                    d.ItemSequence",
                LPPIHelper.P("@p", packageId));

            byte[] bytes = BuildWorkbook(detail);

            string fileName = string.Format(CultureInfo.InvariantCulture,
                "LPPI_Review_{0}_{1}.xlsx",
                SafeFileToken(program),
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
        // -------------------------------------------------------------------

        private static byte[] BuildWorkbook(DataTable dt)
        {
            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("All Lines");

                // Header row
                string[] headers = new[]
                {
                    "Document No.",
                    "Line",
                    "Company Code",
                    "Vendor Name",
                    "Vendor Number",
                    "PO Number",
                    "Clearing Month",
                    "WBS Element",
                    "WBS Description",
                    "GL Account",
                    "Profit Centre",
                    "Tax Code",
                    "Delivery Manager",
                    "Delivery Manager Name",
                    "Delivery Manager Program",
                    "POC Email",
                    "Payment Run Date",
                    "Days Late",
                    "Interest Payable",
                    "Reason Code",
                    "Outcome",
                    "Comments",
                    "Objective Reference",
                    "Reviewed By",
                    "Reviewed Date"
                };
                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cells[1, i + 1].Value = headers[i];
                }
                using (var hdr = ws.Cells[1, 1, 1, headers.Length])
                {
                    hdr.Style.Font.Bold = true;
                    hdr.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hdr.Style.Fill.BackgroundColor.SetColor(System.Drawing.ColorTranslator.FromHtml("#fff7ed"));
                    hdr.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                }

                // Data rows
                int row = 2;
                foreach (DataRow r in dt.Rows)
                {
                    int col = 1;
                    ws.Cells[row, col++].Value = AsString(r["DocNoAccounting"]);

                    // ItemSequence rendered as zero-padded 3-digit string for
                    // consistency with the screen view.
                    int seq = AsInt(r["ItemSequence"]);
                    ws.Cells[row, col++].Value = string.Format(CultureInfo.InvariantCulture, "{0:000}", seq);

                    ws.Cells[row, col++].Value = AsString(r["CompanyCode"]);
                    ws.Cells[row, col++].Value = AsString(r["VendorName"]);
                    ws.Cells[row, col++].Value = AsString(r["VendorNum"]);
                    ws.Cells[row, col++].Value = AsString(r["PoNumber"]);
                    ws.Cells[row, col++].Value = AsString(r["ClearingMonth"]);
                    ws.Cells[row, col++].Value = AsString(r["WbsElement"]);
                    ws.Cells[row, col++].Value = AsString(r["WbsDesc"]);
                    ws.Cells[row, col++].Value = AsString(r["GlAccount"]);
                    ws.Cells[row, col++].Value = AsString(r["ProfitCentre"]);
                    ws.Cells[row, col++].Value = AsString(r["TaxCode"]);
                    ws.Cells[row, col++].Value = AsString(r["DeliveryManager"]);
                    ws.Cells[row, col++].Value = AsString(r["DeliveryManagerName"]);
                    ws.Cells[row, col++].Value = AsString(r["DeliveryManagerProgram"]);
                    ws.Cells[row, col++].Value = AsString(r["PocEmail"]);

                    // Payment Run Date — write as DateTime when valid so Excel
                    // sees a real date, otherwise leave blank.
                    DateTime? prd = AsDate(r["PaymentRunDate"]);
                    if (prd.HasValue)
                    {
                        ws.Cells[row, col].Value = prd.Value;
                        ws.Cells[row, col].Style.Numberformat.Format = "dd/mm/yyyy";
                    }
                    col++;

                    // Days Late — numeric
                    object daysObj = r["DaysVariance"];
                    if (daysObj != DBNull.Value)
                        ws.Cells[row, col].Value = AsInt(daysObj);
                    col++;

                    // Interest Payable — numeric, AU money format
                    decimal? interest = AsDecimal(r["InterestPayable"]);
                    if (interest.HasValue)
                    {
                        ws.Cells[row, col].Value = interest.Value;
                        ws.Cells[row, col].Style.Numberformat.Format = "#,##0.00";
                    }
                    col++;

                    ws.Cells[row, col++].Value = AsString(r["ReasonCode"]);
                    ws.Cells[row, col++].Value = AsString(r["ReasonOutcome"]);
                    ws.Cells[row, col++].Value = AsString(r["Comments"]);
                    ws.Cells[row, col++].Value = AsString(r["ObjectiveReference"]);
                    ws.Cells[row, col++].Value = AsString(r["ReviewedByName"]);

                    DateTime? rdt = AsDate(r["ReviewedDate"]);
                    if (rdt.HasValue)
                    {
                        ws.Cells[row, col].Value = rdt.Value;
                        ws.Cells[row, col].Style.Numberformat.Format = "dd/mm/yyyy hh:mm";
                    }
                    col++;

                    row++;
                }

                // Freeze the header row and auto-size columns for readability.
                ws.View.FreezePanes(2, 1);
                ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 60);

                return pkg.GetAsByteArray();
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static string AsString(object v)
        {
            if (v == null || v == DBNull.Value) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static int AsInt(object v)
        {
            if (v == null || v == DBNull.Value) return 0;
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        private static decimal? AsDecimal(object v)
        {
            if (v == null || v == DBNull.Value) return null;
            if (v is decimal) return (decimal)v;
            decimal d;
            if (decimal.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;
            return null;
        }

        private static DateTime? AsDate(object v)
        {
            if (v == null || v == DBNull.Value) return null;
            if (v is DateTime) return (DateTime)v;
            DateTime d;
            if (DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out d))
                return d;
            return null;
        }

        private static string SafeFileToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return "package";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }
            return sb.Length == 0 ? "package" : sb.ToString();
        }

        private static void WriteError(HttpContext ctx, string message)
        {
            ctx.Response.Clear();
            ctx.Response.ContentType = "text/plain";
            ctx.Response.StatusCode  = 400;
            ctx.Response.Write(message);
            ctx.Response.End();
        }
    }
}
