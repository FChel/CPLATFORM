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
    /// Builds the ERP Payment Request bulk-upload workbooks (.xlsx) for
    /// reviewed, payable LPPI documents — ONE FILE PER COMPANY CODE. Layout
    /// matches the SAP Payment Request bulk-upload template exactly: worksheet
    /// named "SAPUI5 Export", 27 columns, title-case headers on a frozen row
    /// with an autofilter across the data extent (General format, no bold).
    /// The SAP importer reads the sheet BY NAME, so the sheet name is
    /// load-bearing and must not change.
    ///
    /// The export is driven by a LIST OF PACKAGES. The Export page presents a
    /// picker of Finalised packages; the operator selects one or more; this
    /// helper pulls the payable documents belonging to those packages and
    /// builds one workbook per company code across the whole selection,
    /// regardless of how many Capability Managers are selected. ERP loads one
    /// file per company code, so the file count equals the number of distinct
    /// company codes in scope.
    ///
    /// Row model: ONE ROW PER DOCUMENT (DocNoAccounting). A document's whole
    /// interest is summed across every live line and paid from a SINGLE row,
    /// charged to the first-line Delivery Manager cost centre. Where a
    /// document's lines span different cost centres the first-line wins — this
    /// is accepted. Every payable document carries the Defective
    /// Administration GL (282000); no WBS is emitted. The reason code lives at
    /// DOCUMENT level (the reviewer codes the first/dominant line), and the
    /// payable gate is read from that first-line review.
    ///
    /// Payment reference quotes the vendor invoice number from the late
    /// payment, suffixed INT — one reference per document.
    ///
    /// Tax code: always "P5". Finance confirmed interest payments are not
    /// tax-input or tax-output relevant, so the DB TAX_CODE is informational
    /// only and not propagated to the output.
    ///
    /// Marking and stamping is done by the caller (LPPI_Export.aspx.cs) so the
    /// helper stays a pure builder. The caller wraps the build + stamp in a
    /// single transaction-shaped sequence.
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
            "Company Code",          // 1
            "Payment Type",          // 2
            "Payment Sub Type",      // 3
            "Document type",         // 4  — template keeps lowercase 't'
            "Financial Delegation",  // 5
            "Vendor Number",         // 6
            "GL Account Code",       // 7
            "Cost Centre Code",      // 8
            "WBS Element",           // 9
            "Internal Order",        // 10
            "Amount Paid (GST Incl)",// 11
            "Currency",              // 12
            "Tax Code",              // 13
            "Payment Reference",     // 14
            "Header text",           // 15  — template keeps lowercase 't'
            "Item Text",             // 16
            "Title",                 // 17
            "Name",                  // 18
            "Street",                // 19
            "City",                  // 20
            "Post Code",             // 21
            "Country",               // 22
            "Region",                // 23
            "E-mail",                // 24
            "Bank Key",              // 25
            "Bank Account",          // 26
            "Bank Country"           // 27
        };

        /// <summary>
        /// One generated workbook — covers a single company code.
        /// </summary>
        public class ExportFile
        {
            /// <summary>Company code this file covers.</summary>
            public string CompanyCode;

            /// <summary>Document rows in this file (one per DocNoAccounting).</summary>
            public int DocumentCount;

            /// <summary>Sum of document interest in this file.</summary>
            public decimal TotalAmount;

            /// <summary>The .xlsx payload for this company code.</summary>
            public byte[] Bytes;
        }

        /// <summary>
        /// Result from <see cref="BuildExport"/>. Caller persists each file
        /// and the audit rows, and stamps the included documents/packages with
        /// the resulting ExportBatchID.
        /// </summary>
        public class ExportResult
        {
            /// <summary>Distinct document count (DocNoAccounting) across all files.</summary>
            public int DocumentCount;

            /// <summary>Distinct package count represented across all files.</summary>
            public int PackageCount;

            /// <summary>Sum of document interest across all files.</summary>
            public decimal TotalAmount;

            /// <summary>Every live-line DocumentID included — stamped Exported by the caller.</summary>
            public List<int> DocumentIds;

            /// <summary>The selected in-scope PackageIDs.</summary>
            public List<int> PackageIds;

            /// <summary>One file per company code. Empty when nothing is payable.</summary>
            public List<ExportFile> Files;
        }

        // Per-document accumulator — collapses the line-level result set to
        // one row per document.
        private class DocAccumulator
        {
            public string  DocNoAccounting;
            public string  CompanyCode;
            public string  VendorNum;
            public string  DeliveryManager;
            public string  VendorInvoiceNo;
            public decimal DocInterest;
        }

        /// <summary>
        /// Build one Excel bulk-upload file per company code covering the
        /// payable documents of the supplied Finalised packages. Returns the
        /// files and detailed counts; does NOT persist anything.
        /// </summary>
        /// <param name="packageIds">PackageIDs to include. Must all be Finalised.</param>
        public static ExportResult BuildExport(IList<int> packageIds)
        {
            if (packageIds == null || packageIds.Count == 0)
            {
                return new ExportResult
                {
                    DocumentCount = 0,
                    PackageCount  = 0,
                    TotalAmount   = 0m,
                    DocumentIds   = new List<int>(),
                    PackageIds    = new List<int>(),
                    Files         = new List<ExportFile>()
                };
            }

            // -----------------------------------------------------------------
            // 1. Pull the source rows. A document can be a live member of more
            //    than one selected package, so select the DISTINCT payable
            //    DocNoAccounting in scope first (same shape as
            //    GetSummaryScopeHeader: pd -> live line, payable per the
            //    first-line review on MIN(live DocumentID)), then expand each
            //    document to its live lines exactly once. Summing once per
            //    document, and the split into one file per company code, both
            //    happen in C# below. Export totals reconcile to the Summary
            //    Payable figure by construction.
            //
            //    OLE DB requires positional ? placeholders. The IN clause is
            //    built manually with one placeholder per package id.
            // -----------------------------------------------------------------
            var inPlaceholders = new StringBuilder();
            for (int i = 0; i < packageIds.Count; i++)
            {
                if (i > 0) inPlaceholders.Append(",");
                inPlaceholders.Append("@P").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            string sql =
                "WITH PayableDocs AS ( " +
                "  SELECT DISTINCT d.DocNoAccounting " +
                "    FROM dbo.tblLPPI_ReviewPackageDocuments pd " +
                "    INNER JOIN dbo.tblLPPI_Documents d " +
                "            ON d.DocumentID    = pd.DocumentID " +
                "           AND d.IsDeactivated = 0 " +
                "    INNER JOIN dbo.tblLPPI_Reviews r " +
                "            ON r.DocumentID = (SELECT MIN(d2.DocumentID) " +
                "                                 FROM dbo.tblLPPI_Documents d2 " +
                "                                WHERE d2.DocNoAccounting = d.DocNoAccounting " +
                "                                  AND d2.IsDeactivated   = 0) " +
                "    INNER JOIN dbo.tblLPPI_ReasonCodes rc " +
                "            ON rc.ReasonCodeID = r.ReasonCodeID " +
                "   WHERE pd.PackageID IN (" + inPlaceholders.ToString() + ") " +
                "     AND rc.Outcome = 'Payable' " +
                ") " +
                "SELECT d.DocumentID, d.CompanyCode, d.VendorNum, d.DeliveryManager, " +
                "       d.InterestPayable, d.DocNoAccounting, d.VendorInvoiceNo " +
                "  FROM PayableDocs pdoc " +
                "  INNER JOIN dbo.tblLPPI_Documents d " +
                "          ON d.DocNoAccounting = pdoc.DocNoAccounting " +
                "         AND d.IsDeactivated   = 0 " +
                " ORDER BY d.CompanyCode, d.DocNoAccounting, d.DocumentID;";

            var parms = new List<OleDbParameter>(packageIds.Count);
            for (int i = 0; i < packageIds.Count; i++)
                parms.Add(LPPIHelper.P("@P" + i.ToString(CultureInfo.InvariantCulture), packageIds[i]));

            DataTable dt = LPPIHelper.ExecuteTable(sql, parms.ToArray());

            // -----------------------------------------------------------------
            // 2. Collapse to document level. Each document pays its WHOLE
            //    interest (summed across every live line) from a single row,
            //    charged to the first-line Delivery Manager cost centre. The
            //    query orders by DocumentID ascending within each document, so
            //    the first row seen for a document IS its first line.
            // -----------------------------------------------------------------
            var allLineDocIds  = new List<int>();
            var distinctPkgIds = new HashSet<int>(packageIds);
            var docOrder       = new List<string>();
            var docMap         = new Dictionary<string, DocAccumulator>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow row in dt.Rows)
            {
                int      docId   = AsInt(row["DocumentID"]);
                string   docNo   = AsString(row["DocNoAccounting"]);
                decimal? linePay = AsDecimal(row["InterestPayable"]);

                allLineDocIds.Add(docId);

                DocAccumulator acc;
                if (!docMap.TryGetValue(docNo, out acc))
                {
                    acc = new DocAccumulator
                    {
                        DocNoAccounting = docNo,
                        CompanyCode     = AsString(row["CompanyCode"]),
                        VendorNum       = AsString(row["VendorNum"]),
                        DeliveryManager = AsString(row["DeliveryManager"]),
                        VendorInvoiceNo = AsString(row["VendorInvoiceNo"])
                    };
                    docMap[docNo] = acc;
                    docOrder.Add(docNo);
                }

                if (linePay.HasValue) acc.DocInterest += linePay.Value;
            }

            // -----------------------------------------------------------------
            // 3. Group documents by company code (first-seen order) and build
            //    one workbook per company code.
            // -----------------------------------------------------------------
            var companyOrder = new List<string>();
            var byCompany    = new Dictionary<string, List<DocAccumulator>>(StringComparer.OrdinalIgnoreCase);
            foreach (string docNo in docOrder)
            {
                DocAccumulator acc = docMap[docNo];
                List<DocAccumulator> list;
                if (!byCompany.TryGetValue(acc.CompanyCode, out list))
                {
                    list = new List<DocAccumulator>();
                    byCompany[acc.CompanyCode] = list;
                    companyOrder.Add(acc.CompanyCode);
                }
                list.Add(acc);
            }

            var files     = new List<ExportFile>();
            decimal grand = 0m;

            foreach (string cc in companyOrder)
            {
                List<DocAccumulator> docs = byCompany[cc];
                decimal fileTotal = 0m;
                byte[] bytes;

                using (var pkg = new ExcelPackage())
                {
                    ExcelWorksheet ws = pkg.Workbook.Worksheets.Add("SAPUI5 Export");

                    // Row 1: headers, plain General format to match the real
                    // template.
                    for (int c = 0; c < OutputHeaders.Length; c++)
                        ws.Cells[1, c + 1].Value = OutputHeaders[c];

                    // Row 2+: one row per DOCUMENT.
                    int excelRow = 2;
                    foreach (DocAccumulator acc in docs)
                    {
                        // Payment reference quotes the vendor invoice number,
                        // suffixed INT, capped at the ERP 16-char limit (keep
                        // the rightmost 16 — the most distinguishing part).
                        string paymentRef = acc.VendorInvoiceNo + "INT";
                        if (paymentRef.Length > 16)
                            paymentRef = paymentRef.Substring(paymentRef.Length - 16);

                        string itemText = "Late Payment Interest for " + acc.VendorInvoiceNo;

                        ws.Cells[excelRow, 1].Value  = acc.CompanyCode;     // Company code
                        ws.Cells[excelRow, 2].Value  = "INTEREST";          // Payment type
                        ws.Cells[excelRow, 3].Value  = "INTEREST";          // Payment sub type
                        ws.Cells[excelRow, 4].Value  = "NP";                // Document type
                        ws.Cells[excelRow, 5].Value  = "0023";              // Financial Delegation
                        ws.Cells[excelRow, 6].Value  = acc.VendorNum;       // Vendor Number
                        ws.Cells[excelRow, 7].Value  = "282000";            // GL Account Code — Defective Administration
                        ws.Cells[excelRow, 8].Value  = acc.DeliveryManager; // Cost Centre Code — first-line Delivery Manager charge cost centre
                        // Col 9 WBS Element — blank; LPPI interest carries no WBS
                        // Col 10 Internal Order — blank

                        ws.Cells[excelRow, 11].Value = acc.DocInterest;     // Amount Paid (GST Incl) — whole-document interest
                        fileTotal += acc.DocInterest;

                        ws.Cells[excelRow, 12].Value = "AUD";               // Currency
                        ws.Cells[excelRow, 13].Value = "P5";                // Tax code — interest is not tax-relevant
                        ws.Cells[excelRow, 14].Value = paymentRef;          // Payment reference — {VendorInvoiceNo}INT
                        ws.Cells[excelRow, 15].Value = acc.DocNoAccounting; // Header text — FI accounting document number
                        ws.Cells[excelRow, 16].Value = itemText;            // Item text
                        // Col 17–27 all blank (Title, Name, address, bank fields)

                        // SAP's importer reads cells positionally, not by cell
                        // reference. EPPlus omits blank cells, so a row with gaps
                        // (WBS, Internal Order, Title..Bank Country) ships fewer
                        // than 27 cells and the importer misaligns every column
                        // after the first gap. Write an explicit empty value into
                        // any unset column so every row carries all 27 cells,
                        // matching an Excel-saved file.
                        for (int c = 1; c <= OutputHeaders.Length; c++)
                        {
                            if (ws.Cells[excelRow, c].Value == null)
                                ws.Cells[excelRow, c].Value = string.Empty;
                        }

                        excelRow++;
                    }

                    // Freeze the header row; filter on the header only (A1:AA1),
                    // matching an Excel-saved template. Filtering the full data
                    // extent is unnecessary and drifts from the known-good file.
                    ws.View.FreezePanes(2, 1);
                    ws.Cells[1, 1, 1, OutputHeaders.Length].AutoFilter = true;

                    bytes = pkg.GetAsByteArray();
                }

                files.Add(new ExportFile
                {
                    CompanyCode   = cc,
                    DocumentCount = docs.Count,
                    TotalAmount   = fileTotal,
                    Bytes         = bytes
                });
                grand += fileTotal;
            }

            return new ExportResult
            {
                DocumentCount = docMap.Count,
                PackageCount  = distinctPkgIds.Count,
                TotalAmount   = grand,
                DocumentIds   = allLineDocIds,
                PackageIds    = new List<int>(distinctPkgIds),
                Files         = files
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