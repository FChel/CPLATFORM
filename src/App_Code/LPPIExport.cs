using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using OfficeOpenXml;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Builds the ERP Payment Request bulk-upload workbook (.xlsx) for reviewed,
    /// payable LPPI documents. Layout matches Payment_Request_Bulk_Upload_Template.xlsx
    /// exactly: 27 columns, Sheet1, plain headers (General format, no bold).
    ///
    /// The export is driven by a LIST OF PACKAGES, Export page presents a picker of Finalised
    /// packages; the operator selects one or more; this helper pulls the
    /// payable lines belonging to those packages and builds the workbook.
    /// Date-range filtering, batch-id filtering and the include-already-
    /// exported toggle are all gone — Finalised is the gate (you cannot
    /// finalise without coding every doc), and Exported is terminal so a
    /// package cannot be re-shipped.
    ///
    /// Row model: ONE ROW PER LINE in tblLPPI_Documents. BODS supplies an
    /// ITEM_SEQUENCE so a single DocNoAccounting may have many lines and
    /// Finance pays each line separately. Every payable line carries the
    /// Defective Administration GL (282000) and the document's Delivery
    /// Manager charge cost centre; no WBS is emitted. The reason code lives
    /// at DOCUMENT level (the reviewer codes only the first/dominant line,
    /// via the smallest-ItemSequence row), and every line of the same
    /// document inherits that code — this is done via a correlated
    /// sub-query that maps each document row to its first-line DocumentID
    /// and joins the review there.
    ///
    /// Payment reference quotes the vendor invoice number from the late
    /// payment, suffixed INT. The ERP payment run de-duplicates references
    /// across multiple lines of the same document.
    ///
    /// Tax code: always "P5". After TAX_CODE landed in the BODS extract
    /// Finance confirmed interest payments are not tax-input or tax-output
    /// relevant, so the DB value is informational only and not propagated
    /// to the output.
    ///
    /// Marking and stamping is done by the caller (LPPI_Export.aspx.cs) so
    /// the helper stays a pure builder. The caller wraps the build + stamp
    /// in a single transaction.
    ///
    /// Uses EPPlus 4.5.3.3 (LGPL). Do NOT swap this out for ClosedXML — it
    /// has caused dependency problems on the CPLATFORM server in the past.
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

        /// <summary>
        /// Result from <see cref="BuildExport"/>. Caller is responsible for
        /// persisting the bytes and the audit row, and for stamping the
        /// included documents/packages with the resulting ExportBatchID.
        /// </summary>
        public class ExportResult
        {
            /// <summary>Total line rows in the file (excluding header).</summary>
            public int LineCount;

            /// <summary>Distinct document count (DocNoAccounting) included.</summary>
            public int DocumentCount;

            /// <summary>Distinct package count actually represented in the file.</summary>
            public int PackageCount;

            /// <summary>Sum of InterestPayable across the included lines.</summary>
            public decimal TotalAmount;

            /// <summary>Distinct DocumentIDs included — one per LINE.</summary>
            public List<int> DocumentIds;

            /// <summary>Distinct PackageIDs whose docs ended up in the file.</summary>
            public List<int> PackageIds;

            /// <summary>The .xlsx payload.</summary>
            public byte[] Bytes;
        }

        /// <summary>
        /// Build the Excel bulk-upload file covering payable lines of the
        /// supplied Finalised packages. Returns the bytes and detailed counts;
        /// does NOT persist anything. Caller is responsible for inserting
        /// the tblLPPI_ExportBatches row, stamping ExportBatchID on
        /// documents/packages, and flipping package status to Exported —
        /// all in a single transaction.
        /// </summary>
        /// <param name="packageIds">PackageIDs to include. Must all be Finalised.</param>
        public static ExportResult BuildExport(IList<int> packageIds)
        {
            if (packageIds == null || packageIds.Count == 0)
            {
                return new ExportResult
                {
                    LineCount     = 0,
                    DocumentCount = 0,
                    PackageCount  = 0,
                    TotalAmount   = 0m,
                    DocumentIds   = new List<int>(),
                    PackageIds    = new List<int>(),
                    Bytes         = new byte[0]
                };
            }

            // -----------------------------------------------------------------
            // 1. Pull the source rows — one row per tblLPPI_Documents row
            //    (i.e. per LINE, not per DocNoAccounting), restricted to
            //    documents that are members of the selected packages AND
            //    whose DOCUMENT-level review (joined via first-line
            //    DocumentID) carries a Payable outcome.
            //
            //    OLE DB requires positional ? placeholders. The IN clause is
            //    built manually with one placeholder per package id and the
            //    parameter list passed in matching order — the same pattern
            //    used elsewhere for variable-length lists.
            //
            //    Both the INNER JOIN and the WHERE clause assert
            //    d.IsDeactivated = 0 — deactivated lines never ship to ERP.
            //    The two are redundant by design: if a future edit removes
            //    one, the other still keeps deactivated rows out.
            // -----------------------------------------------------------------
            var inPlaceholders = new StringBuilder();
            for (int i = 0; i < packageIds.Count; i++)
            {
                if (i > 0) inPlaceholders.Append(",");
                inPlaceholders.Append("@P").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            string sql =
                "SELECT d.DocumentID, d.CompanyCode, d.VendorNum, d.DeliveryManager, " +
                "       d.InterestPayable, d.DocNoAccounting, d.VendorInvoiceNo, " +
                "       pd.PackageID " +
                "  FROM dbo.tblLPPI_ReviewPackageDocuments pd " +
                "  INNER JOIN dbo.tblLPPI_Documents d " +
                "          ON d.DocNoAccounting = (SELECT d2.DocNoAccounting " +
                "                                    FROM dbo.tblLPPI_Documents d2 " +
                "                                   WHERE d2.DocumentID = pd.DocumentID) " +
                "         AND d.IsDeactivated  = 0 " +
                "  INNER JOIN dbo.tblLPPI_Reviews r " +
                "          ON r.DocumentID = pd.DocumentID " +
                "  INNER JOIN dbo.tblLPPI_ReasonCodes rc " +
                "          ON rc.ReasonCodeID = r.ReasonCodeID " +
                " WHERE pd.PackageID IN (" + inPlaceholders.ToString() + ") " +
                "   AND rc.Outcome      = 'Payable' " +
                "   AND d.IsDeactivated = 0 " +
                " ORDER BY pd.PackageID, d.DocNoAccounting, d.ItemSequence;";

            var parms = new List<OleDbParameter>(packageIds.Count);
            for (int i = 0; i < packageIds.Count; i++)
            {
                parms.Add(LPPIHelper.P("@P" + i.ToString(CultureInfo.InvariantCulture), packageIds[i]));
            }

            DataTable dt = LPPIHelper.ExecuteTable(sql, parms.ToArray());

            // -----------------------------------------------------------------
            // 2. Build the workbook.
            // -----------------------------------------------------------------
            var docIds          = new List<int>();
            var distinctDocNos  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var distinctPkgIds  = new HashSet<int>();
            decimal total       = 0m;
            byte[]  bytes;

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
                    string   companyCode       = AsString(row["CompanyCode"]);
                    string   vendorNum         = AsString(row["VendorNum"]);
                    string   deliveryManager   = AsString(row["DeliveryManager"]);
                    decimal? interestPay       = AsDecimal(row["InterestPayable"]);
                    string   docNoAcct         = AsString(row["DocNoAccounting"]);
                    string   vendorInvoice     = AsString(row["VendorInvoiceNo"]);
                    int      pkgIdRow          = AsInt(row["PackageID"]);

                    // Payment reference quotes the vendor invoice number from
                    // the late payment, suffixed INT. The ERP payment run
                    // de-duplicates references across multiple lines of the
                    // same document.
                    string paymentRef = vendorInvoice + "INT";

                    string itemText = "Late Payment Interest for " + vendorInvoice;

                    // Col 1–10
                    ws.Cells[excelRow, 1].Value  = companyCode;        // Company code
                    ws.Cells[excelRow, 2].Value  = "INTEREST";         // Payment type
                    ws.Cells[excelRow, 3].Value  = "INTEREST";         // Payment sub type
                    ws.Cells[excelRow, 4].Value  = "NP";               // Document type
                    ws.Cells[excelRow, 5].Value  = "0023";             // Financial Delegation
                    ws.Cells[excelRow, 6].Value  = vendorNum;          // Vendor Number
                    ws.Cells[excelRow, 7].Value  = "282000";           // GL Account Code — Defective Administration
                    ws.Cells[excelRow, 8].Value  = deliveryManager;    // Cost Centre Code — Delivery Manager charge cost centre
                    // Col 9 WBS Element — intentionally blank; LPPI interest carries no WBS
                    // Col 10 Internal Order — blank

                    // Col 11 Amount Paid (GST Incl) — per-line InterestPayable
                    if (interestPay.HasValue)
                    {
                        ws.Cells[excelRow, 11].Value = interestPay.Value;
                        total += interestPay.Value;
                    }

                    ws.Cells[excelRow, 12].Value = "AUD";              // Currency
                    ws.Cells[excelRow, 13].Value = "P5";               // Tax code — interest is not tax-relevant
                    ws.Cells[excelRow, 14].Value = paymentRef;         // Payment reference — {VendorInvoiceNo}INT
                    ws.Cells[excelRow, 15].Value = docNoAcct;          // Header text — FI accounting document number
                    ws.Cells[excelRow, 16].Value = itemText;          // Item text
                    // Col 17–27 all blank (Title, Name, address, bank fields)

                    docIds.Add(Convert.ToInt32(row["DocumentID"]));
                    distinctDocNos.Add(docNoAcct);
                    distinctPkgIds.Add(pkgIdRow);
                    excelRow++;
                }

                // Headers and all cells stay at the default "General" number
                // format — matches the real template. No bold, no fill.
                bytes = pkg.GetAsByteArray();
            }

            return new ExportResult
            {
                LineCount     = docIds.Count,
                DocumentCount = distinctDocNos.Count,
                PackageCount  = distinctPkgIds.Count,
                TotalAmount   = total,
                DocumentIds   = docIds,
                PackageIds    = new List<int>(distinctPkgIds),
                Bytes         = bytes
            };
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
    }
}
