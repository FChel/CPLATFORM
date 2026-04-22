using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using OfficeOpenXml;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Builds the ERP Payment Request bulk-upload workbook (.xlsx) for reviewed,
    /// payable LPPI documents. Layout matches Payment_Request_Bulk_Upload_Template.xlsx
    /// exactly: 27 columns, Sheet1, plain headers (General format, no bold).
    ///
    /// Row model (April 2026): ONE ROW PER LINE in tblLPPI_Documents. BODS now
    /// supplies an ITEM_SEQUENCE so a single DocNoAccounting may have many lines
    /// and Finance wants each line paid separately against its own GL / WBS /
    /// Profit Centre. The reason code lives at DOCUMENT level (the reviewer
    /// codes only the first/dominant line, via the smallest-ItemSequence row),
    /// and every line of the same document inherits that code — this is done
    /// via a correlated sub-query that maps each document row to its first-line
    /// DocumentID and joins the review there.
    ///
    /// Payment reference is made unique per line with a -NNN suffix so the bulk
    /// upload cannot collide on duplicate references when a document has
    /// multiple lines.
    ///
    /// Tax code: always "P5". After TAX_CODE landed in the BODS extract Finance
    /// confirmed interest payments are not tax-input or tax-output relevant, so
    /// the DB value is informational only and not propagated to the output.
    ///
    /// Uses EPPlus 4.5.3.3 (LGPL). Do NOT swap this out for ClosedXML — it has
    /// caused dependency problems on the CPLATFORM server in the past.
    /// </summary>
    public static class LPPIExport
    {
        // -------------------------------------------------------------------
        // Output layout — 27 columns, in order, matching the bulk-upload
        // template exactly.
        // -------------------------------------------------------------------
        public static readonly string[] OutputHeaders = new[]
        {
            "Company code",          // 1
            "Payment type",          // 2
            "Payment sub type",      // 3
            "Document type",         // 4
            "Financial Delegation",  // 5
            "Vendor Number",         // 6
            "GL Account Code",       // 7
            "Cost Centre Code",      // 8
            "WBS Element",           // 9
            "Internal Order",        // 10
            "Amount Paid (GST Incl)",// 11
            "Currency",              // 12
            "Tax code",              // 13
            "Payment reference",     // 14
            "Header text",           // 15
            "Item text",             // 16
            "Title",                 // 17
            "Name",                  // 18
            "Street",                // 19
            "City",                  // 20
            "Post code",             // 21
            "Country",               // 22
            "Region",                // 23
            "E-mail",                // 24
            "Bank Key",              // 25
            "Bank account",          // 26
            "Bank Country"           // 27
        };

        public class ExportResult
        {
            public int RowCount;
            public string FileName;
            public byte[] Bytes;
        }

        /// <summary>
        /// Build the Excel bulk-upload file covering reviewed documents whose
        /// reason code has Outcome = 'Payable'. Signature unchanged from the
        /// legacy version so the aspx.cs caller does not need to change.
        /// </summary>
        public static ExportResult BuildExport(DateTime fromDate, DateTime toDate, bool includeAlreadyExported,
                                                int? batchId, bool markExported)
        {
            // -----------------------------------------------------------------
            // 1. Pull the source rows — one row per tblLPPI_Documents row (i.e.
            //    per LINE, not per DocNoAccounting). The review is joined via
            //    the DOCUMENT's first-line DocumentID so that every line of the
            //    same document inherits the single reason code assigned by the
            //    CM reviewer. Only lines whose owning document has a review and
            //    whose reason code is Payable are included.
            // -----------------------------------------------------------------
            const string selectCols =
                " d.DocumentID, d.CompanyCode, d.VendorNum, d.GlAccount, d.ProfitCentre, " +
                " d.WbsElement, d.InterestPayable, d.DocNoAccounting, d.ItemSequence, " +
                " d.VendorInvoiceNo, d.ClearingMonth, d.FiscalYear ";

            // Correlated sub-query: "the DocumentID of the first line of this doc".
            // Join the review to THAT id. Any line whose document's first line
            // has a Payable review will be included.
            const string joinFirstLineReview =
                " INNER JOIN dbo.tblLPPI_Reviews r " +
                "   ON r.DocumentID = (" +
                "        SELECT MIN(d2.DocumentID) " +
                "          FROM dbo.tblLPPI_Documents d2 " +
                "         WHERE d2.DocNoAccounting = d.DocNoAccounting) " +
                " INNER JOIN dbo.tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID ";

            var sql =
                "SELECT " + selectCols +
                " FROM dbo.tblLPPI_Documents d" +
                joinFirstLineReview +
                " WHERE r.ReasonCodeID IS NOT NULL" +
                "   AND rc.Outcome = 'Payable'" +
                "   AND d.FirstSeenDate >= @From" +
                "   AND d.FirstSeenDate <  DATEADD(day, 1, @To)";

            if (!includeAlreadyExported) sql += " AND d.ExportedDate IS NULL";
            if (batchId.HasValue)        sql += " AND d.BatchID = @Batch";
            sql += " ORDER BY d.DocNoAccounting, d.ItemSequence;";

            var parms = new List<OleDbParameter>
            {
                LPPIHelper.P("@From", fromDate.Date),
                LPPIHelper.P("@To",   toDate.Date)
            };
            if (batchId.HasValue) parms.Add(LPPIHelper.P("@Batch", batchId.Value));

            DataTable dt = LPPIHelper.ExecuteTable(sql, parms.ToArray());

            // -----------------------------------------------------------------
            // 2. Build the workbook.
            // -----------------------------------------------------------------
            var docIds = new List<int>();
            byte[] bytes;

            using (var pkg = new ExcelPackage())
            {
                ExcelWorksheet ws = pkg.Workbook.Worksheets.Add("Sheet1");

                // Row 1: headers, plain General format (no bold, no fill) to
                // match the real template.
                for (int c = 0; c < OutputHeaders.Length; c++)
                {
                    ws.Cells[1, c + 1].Value = OutputHeaders[c];
                }

                // Row 2+: one row per LINE.
                int excelRow = 2;
                foreach (DataRow row in dt.Rows)
                {
                    string companyCode    = AsString(row["CompanyCode"]);
                    string vendorNum      = AsString(row["VendorNum"]);
                    string glAccount      = AsString(row["GlAccount"]);
                    string profitCentre   = AsString(row["ProfitCentre"]);  // used as Cost Centre placeholder
                    string wbsElement     = AsString(row["WbsElement"]);
                    decimal? interestPay  = AsDecimal(row["InterestPayable"]);
                    string docNoAcct      = AsString(row["DocNoAccounting"]);
                    int    itemSeq        = AsInt(row["ItemSequence"]);
                    string vendorInvoice  = AsString(row["VendorInvoiceNo"]);
                    string clearingMonth  = AsString(row["ClearingMonth"]);
                    string fiscalYearRaw  = AsString(row["FiscalYear"]);

                    // FY: prefer the dedicated FISCAL_YEAR column from BODS,
                    // fall back to deriving from ClearingMonth for any legacy
                    // rows where the column is empty.
                    int fy;
                    if (!int.TryParse(fiscalYearRaw, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out fy) || fy <= 0)
                    {
                        fy = DeriveAuFiscalYear(clearingMonth);
                    }

                    // Payment reference must be unique per LINE so the bulk
                    // upload does not reject duplicates when a document has
                    // multiple lines. Format: {CC}{FY}{DOC}-{SEQ:000}.
                    string paymentRef = string.Format(CultureInfo.InvariantCulture,
                        "{0}{1}{2}-{3:000}",
                        companyCode,
                        fy,
                        docNoAcct,
                        itemSeq);

                    string itemText = "Late Payment Interest for " + vendorInvoice;

                    // Col 1–10
                    ws.Cells[excelRow, 1].Value  = companyCode;     // Company code
                    ws.Cells[excelRow, 2].Value  = "INTEREST";      // Payment type
                    ws.Cells[excelRow, 3].Value  = "INTEREST";      // Payment sub type
                    ws.Cells[excelRow, 4].Value  = "NP";            // Document type
                    ws.Cells[excelRow, 5].Value  = "0023";          // Financial Delegation
                    ws.Cells[excelRow, 6].Value  = vendorNum;       // Vendor Number
                    ws.Cells[excelRow, 7].Value  = glAccount;       // GL Account Code
                    ws.Cells[excelRow, 8].Value  = profitCentre;    // Cost Centre Code (placeholder)
                    ws.Cells[excelRow, 9].Value  = wbsElement;      // WBS Element
                    // Col 10 Internal Order — blank

                    // Col 11 Amount Paid (GST Incl) — per-line InterestPayable
                    if (interestPay.HasValue)
                        ws.Cells[excelRow, 11].Value = interestPay.Value;
                    // (leave blank if null — same behaviour as the old TSV)

                    ws.Cells[excelRow, 12].Value = "AUD";           // Currency
                    ws.Cells[excelRow, 13].Value = "P5";            // Tax code — interest is not tax-relevant
                    ws.Cells[excelRow, 14].Value = paymentRef;      // Payment reference
                    ws.Cells[excelRow, 15].Value = docNoAcct;       // Header text
                    ws.Cells[excelRow, 16].Value = itemText;        // Item text
                    // Col 17–27 all blank (Title, Name, address fields, bank fields)

                    docIds.Add(Convert.ToInt32(row["DocumentID"]));
                    excelRow++;
                }

                // Headers and all cells stay at the default "General" number
                // format — matches the real template. No bold, no fill.
                bytes = pkg.GetAsByteArray();
            }

            // -----------------------------------------------------------------
            // 3. Build file name.
            // -----------------------------------------------------------------
            var fileName = string.Format(CultureInfo.InvariantCulture,
                "LPPI_Payment_Bulk_Upload_{0}_{1}.xlsx",
                DateTime.Now.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
                DateTime.Now.Year);

            var result = new ExportResult
            {
                RowCount = docIds.Count,
                FileName = fileName,
                Bytes    = bytes
            };

            // -----------------------------------------------------------------
            // 4. Flip ExportedDate/ExportedBy on the lines actually included.
            //    One UPDATE per line — correct under the per-line row model.
            // -----------------------------------------------------------------
            if (markExported && docIds.Count > 0)
                MarkExported(docIds);

            return result;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static void MarkExported(List<int> docIds)
        {
            var by = LPPIHelper.CurrentUserDisplayName();
            const string sql =
                "UPDATE dbo.tblLPPI_Documents SET ExportedDate = SYSDATETIME(), ExportedBy = @By WHERE DocumentID = @ID";
            foreach (var id in docIds)
            {
                LPPIHelper.ExecuteNonQuery(sql,
                    LPPIHelper.P("@By", by),
                    LPPIHelper.P("@ID", id));
            }
        }

        /// <summary>
        /// Derive the Australian fiscal year from a ClearingMonth string of the
        /// form "M.YYYY" (e.g. "7.2025" -> FY 2026, "4.2025" -> FY 2025).
        /// Jul–Dec roll forward; Jan–Jun stay on the calendar year.
        /// Falls back to today's FY if the value is missing or malformed.
        /// Retained only for legacy rows where the FISCAL_YEAR column is empty —
        /// fresh BODS extracts supply FY directly.
        /// </summary>
        internal static int DeriveAuFiscalYear(string clearingMonth)
        {
            int month, year;
            if (!TryParseClearingMonth(clearingMonth, out month, out year))
            {
                // Fallback: derive from today
                var today = DateTime.Today;
                month = today.Month;
                year  = today.Year;
            }
            return (month >= 7) ? year + 1 : year;
        }

        private static bool TryParseClearingMonth(string s, out int month, out int year)
        {
            month = 0; year = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Trim().Split('.');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out month)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out year))  return false;

            if (month < 1 || month > 12) return false;
            if (year  < 1900 || year > 2999) return false;
            return true;
        }

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
    }
}
