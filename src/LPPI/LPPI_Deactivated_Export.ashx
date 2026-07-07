<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Deactivated_Export" %>

using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Web;
using OfficeOpenXml;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Admin-auth export of the Deactivated lines watch-list. Streams an
    /// .xlsx of every deactivated, not-yet-superseded line — the same rows
    /// and columns shown on LPPI_Deactivated.aspx.
    ///
    /// The query is identical to LPPI_Deactivated.aspx.cs so the file and
    /// the screen always agree: IsDeactivated = 1 AND
    /// SupersededByDocumentID IS NULL, first-line review and finalised/
    /// exported package joined via the deactivated-group MIN.
    ///
    /// Admin-only (LPPIHelper.IsAdminUser gate) — same pattern as
    /// LPPI_Summary_Export.ashx. Handlers are not pages so they replicate
    /// the gate rather than inheriting it. EPPlus 4.5.3.3 (LGPL); do NOT
    /// swap to ClosedXML.
    /// </summary>
    public class LPPI_Deactivated_Export : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        private const string XlsxMimeType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private static readonly string[] Headers = new[]
        {
            "Capability Manager Program",
            "Document No.",
            "Line",
            "Vendor",
            "PO",
            "Interest ($)",
            "Reviewer Comments",
            "Obj Ref",
            "Proposed Baseline Date",
            "Reviewed By",
            "Package ID",
            "Package Finalised"
        };

        public void ProcessRequest(HttpContext ctx)
        {
            // Admin gate. Handlers are not pages so they cannot inherit
            // LPPIBasePage — replicate the same gate via the helper.
            if (!LPPIHelper.IsAdminUser())
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("Forbidden — admin access required.");
                ctx.Response.End();
                return;
            }

            DataTable dt = LoadData();
            byte[] bytes = BuildWorkbook(dt);

            string fileName = string.Format(CultureInfo.InvariantCulture,
                "{0}LPPI_Deactivated_{1}.xlsx",
                LPPIHelper.EnvironmentFileTag,
                DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture));

            ctx.Response.Clear();
            ctx.Response.ContentType = XlsxMimeType;
            ctx.Response.AppendHeader("Content-Disposition",
                "attachment; filename=\"" + fileName + "\"");
            ctx.Response.AppendHeader("Content-Length",
                bytes.Length.ToString(CultureInfo.InvariantCulture));
            ctx.Response.BinaryWrite(bytes);
            ctx.Response.End();
        }

        // Watch-list query — identical to LPPI_Deactivated.aspx.cs so the
        // export and the on-screen list never diverge.
        private static DataTable LoadData()
        {
            const string sql = @"
SELECT  d.DocNoAccounting,
        d.ItemSequence,
        d.CapabilityManagerProgram,
        d.VendorName,
        d.PoNumber,
        d.InterestPayable,
        r.Comments,
        r.ObjectiveReference,
        r.ReloadBaselineDate,
        r.ReviewedByName,
        p.PackageID,
        p.FinalisedDate
  FROM  dbo.tblLPPI_Documents d
  LEFT JOIN dbo.tblLPPI_Reviews r
         ON r.DocumentID = (SELECT MIN(d2.DocumentID)
                              FROM dbo.tblLPPI_Documents d2
                             WHERE d2.DocNoAccounting          = d.DocNoAccounting
                               AND d2.IsDeactivated             = 1
                               AND d2.SupersededByDocumentID IS NULL)
  LEFT JOIN dbo.tblLPPI_ReviewPackageDocuments pd
         ON pd.DocumentID = (SELECT MIN(d3.DocumentID)
                               FROM dbo.tblLPPI_Documents d3
                              WHERE d3.DocNoAccounting          = d.DocNoAccounting
                                AND d3.IsDeactivated             = 1
                                AND d3.SupersededByDocumentID IS NULL)
  LEFT JOIN dbo.tblLPPI_ReviewPackages p
         ON p.PackageID = pd.PackageID
        AND p.Status   IN ('Finalised','Exported')
 WHERE  d.IsDeactivated = 1
   AND  d.SupersededByDocumentID IS NULL
 ORDER BY d.CapabilityManagerProgram, d.DocNoAccounting, d.ItemSequence;";
            return LPPIHelper.ExecuteTable(sql);
        }

        private static byte[] BuildWorkbook(DataTable dt)
        {
            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Deactivated lines");

                for (int c = 0; c < Headers.Length; c++)
                    ws.Cells[1, c + 1].Value = Headers[c];

                int row = 2;
                foreach (DataRow r in dt.Rows)
                {
                    int col = 1;
                    ws.Cells[row, col++].Value = AsString(r, "CapabilityManagerProgram");
                    ws.Cells[row, col++].Value = AsString(r, "DocNoAccounting");
                    ws.Cells[row, col++].Value = AsItemSequenceText(r);
                    ws.Cells[row, col++].Value = AsString(r, "VendorName");
                    ws.Cells[row, col++].Value = AsString(r, "PoNumber");
                    PutMoney(ws, row, col++, r, "InterestPayable");
                    ws.Cells[row, col++].Value = AsString(r, "Comments");
                    ws.Cells[row, col++].Value = AsString(r, "ObjectiveReference");
                    PutDate(ws, row, col++, r, "ReloadBaselineDate");
                    ws.Cells[row, col++].Value = AsString(r, "ReviewedByName");
                    ws.Cells[row, col++].Value = AsInt(r, "PackageID");
                    PutDateTime(ws, row, col++, r, "FinalisedDate");
                    row++;
                }

                if (ws.Dimension != null)
                    ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 60);

                ws.Cells[1, 1, 1, Headers.Length].AutoFilter = true;

                using (var ms = new MemoryStream())
                {
                    pkg.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static void PutMoney(ExcelWorksheet ws, int row, int col, DataRow r, string column)
        {
            decimal? d = AsDecimal(r, column);
            if (d.HasValue)
            {
                ws.Cells[row, col].Value = d.Value;
                ws.Cells[row, col].Style.Numberformat.Format = "#,##0.00";
            }
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
            DateTime d;
            object v = r[column];
            if (v is DateTime) d = (DateTime)v;
            else if (!DateTime.TryParse(Convert.ToString(v), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out d)) return;
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
    }
}