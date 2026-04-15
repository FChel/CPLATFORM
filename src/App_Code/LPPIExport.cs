using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Text;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Builds the tab-delimited return-to-BODS file that matches the column
    /// layout of LPPI_upload_template.xlsx Sheet 1.
    /// </summary>
    public static class LPPIExport
    {
        // Output column order, with friendly headers as expected by BODS.
        // Each entry: (FriendlyHeader, SqlSelect)
        public static readonly Tuple<string, string>[] OutputColumns = new[]
        {
            Tuple.Create("Company Code",                 "d.CompanyCode"),
            Tuple.Create("PO Number",                    "d.PoNumber"),
            Tuple.Create("Vendor Number",                "d.VendorNum"),
            Tuple.Create("Vendor Name",                  "d.VendorName"),
            Tuple.Create("Vendor Account",               "d.VendorAcct"),
            Tuple.Create("WBS Element",                  "d.WbsElement"),
            Tuple.Create("WBS Description",              "d.WbsDesc"),
            Tuple.Create("CAPEX",                        "d.Capex"),
            Tuple.Create("Profit Centre",                "d.ProfitCentre"),
            Tuple.Create("Capability Manager",           "d.CapabilityManager"),
            Tuple.Create("Capability Manager Name",      "d.CapabilityManagerName"),
            Tuple.Create("Capability Manager Program",   "d.CapabilityManagerProgram"),
            Tuple.Create("Delivery Manager",             "d.DeliveryManager"),
            Tuple.Create("Delivery Manager Name",        "d.DeliveryManagerName"),
            Tuple.Create("Delivery Manager Program",     "d.DeliveryManagerProgram"),
            Tuple.Create("POC Email",                    "d.PocEmail"),
            Tuple.Create("GL Account",                   "d.GlAccount"),
            Tuple.Create("Contract No",                  "d.ContractNo"),
            Tuple.Create("VIM Document ID",              "d.VimDocumentId"),
            Tuple.Create("Doc No Accounting",            "d.DocNoAccounting"),
            Tuple.Create("Invoice Received Date",        "d.InvoiceReceivedDate"),
            Tuple.Create("Invoice Date",                 "d.InvoiceDate"),
            Tuple.Create("GR Create Date Latest",        "d.GrCreateDateLatest"),
            Tuple.Create("Currency",                     "d.Currency"),
            Tuple.Create("GL Line Value Incl GST",       "d.GlLineValueInclGst"),
            Tuple.Create("Invoice Value Incl GST",       "d.InvoiceValueInclGst"),
            Tuple.Create("Payment Terms",                "d.PaymentTerms"),
            Tuple.Create("Material PO",                  "d.MaterialPo"),
            Tuple.Create("Contract Value Loc Ex GST",    "d.ContractValueLocExGst"),
            Tuple.Create("Payment Run Date",             "d.PaymentRunDate"),
            Tuple.Create("BODS Payment Baseline Date",   "d.BodsPaymtBaselineDate"),
            Tuple.Create("Days Variance",                "d.DaysVariance"),
            Tuple.Create("Daily Rate",                   "d.DailyRate"),
            Tuple.Create("Invoice Interest Amount",      "d.InvoiceInterestAmount"),
            Tuple.Create("Interest Payable",             "d.InterestPayable"),
            Tuple.Create("Source System",                "d.SourceSystem"),
            Tuple.Create("Payment Channel",              "d.PaymentChannel"),
            Tuple.Create("Document Type",                "d.DocumentType"),
            Tuple.Create("Vendor Invoice No",            "d.VendorInvoiceNo"),
            Tuple.Create("Clearing Month",               "d.ClearingMonth"),
            Tuple.Create("Reason Code",                  "rc.Description"),
            Tuple.Create("Comments",                     "r.Comments"),
            Tuple.Create("Objective Reference",          "r.ObjectiveReference"),
            Tuple.Create("Reviewed By",                  "r.ReviewedByName")
        };

        public class ExportResult
        {
            public int RowCount;
            public string FileName;
            public string FullPath;
            public byte[] Bytes;
        }

        /// <summary>
        /// Build a BODS export covering all reviewed (non-null reason code) documents
        /// loaded between fromDate and toDate (inclusive).
        /// </summary>
        public static ExportResult BuildExport(DateTime fromDate, DateTime toDate, bool includeAlreadyExported,
                                                int? batchId, bool markExported)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            for (int i = 0; i < OutputColumns.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(OutputColumns[i].Item2);
            }
            sb.Append(@", d.DocumentID
FROM dbo.tblLPPI_Documents d
LEFT JOIN dbo.tblLPPI_Reviews r ON r.DocumentID = d.DocumentID
LEFT JOIN dbo.tblLPPI_ReasonCodes rc ON rc.ReasonCodeID = r.ReasonCodeID
WHERE r.ReasonCodeID IS NOT NULL
  AND d.FirstSeenDate >= @From
  AND d.FirstSeenDate <  DATEADD(day, 1, @To)");
            if (!includeAlreadyExported) sb.Append(" AND d.ExportedDate IS NULL");
            if (batchId.HasValue)        sb.Append(" AND d.BatchID = @Batch");
            sb.Append(" ORDER BY d.DocNoAccounting;");

            var parms = new List<OleDbParameter>
            {
                LPPIHelper.P("@From", fromDate.Date),
                LPPIHelper.P("@To",   toDate.Date)
            };
            if (batchId.HasValue) parms.Add(LPPIHelper.P("@Batch", batchId.Value));

            var dt = LPPIHelper.ExecuteTable(sb.ToString(), parms.ToArray());

            // Build the tab-delimited file
            var output = new StringBuilder();
            for (int i = 0; i < OutputColumns.Length; i++)
            {
                if (i > 0) output.Append('\t');
                output.Append(OutputColumns[i].Item1);
            }
            output.Append("\r\n");

            var docIds = new List<int>();
            foreach (DataRow row in dt.Rows)
            {
                for (int i = 0; i < OutputColumns.Length; i++)
                {
                    if (i > 0) output.Append('\t');
                    output.Append(FormatField(row[i]));
                }
                output.Append("\r\n");
                docIds.Add(Convert.ToInt32(row["DocumentID"]));
            }

            var fileName = string.Format("LATEPMT_INTEREST_INCLUDE_{0}_{1}.txt",
                DateTime.Now.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
                DateTime.Now.Year);

            var bytes = Encoding.UTF8.GetBytes(output.ToString());
            var result = new ExportResult { RowCount = docIds.Count, FileName = fileName, Bytes = bytes };

            // Persist to export folder if configured
            try
            {
                var dir = LPPIHelper.ExportPath;
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    var full = Path.Combine(dir, fileName);
                    File.WriteAllBytes(full, bytes);
                    result.FullPath = full;
                }
            }
            catch { /* swallow — caller still gets the bytes */ }

            // Mark exported
            if (markExported && docIds.Count > 0)
                MarkExported(docIds);

            return result;
        }

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

        private static string FormatField(object o)
        {
            if (o == null || o == DBNull.Value) return "";
            if (o is DateTime)
                return ((DateTime)o).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            if (o is decimal)
                return ((decimal)o).ToString("0.##", CultureInfo.InvariantCulture);
            if (o is double || o is float)
                return Convert.ToDecimal(o).ToString("0.##", CultureInfo.InvariantCulture);

            var s = Convert.ToString(o);
            // Strip tabs and CR/LF to keep TSV integrity
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
