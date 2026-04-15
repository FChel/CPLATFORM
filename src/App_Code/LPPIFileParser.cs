using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Parses a BODS LATEPMT_INTEREST_REVIEW_*.xls extract.
    /// Despite the .xls extension, these files are tab-delimited UTF-8 text.
    /// </summary>
    public static class LPPIFileParser
    {
        public static readonly string[] ExpectedHeaders = new[]
        {
            "COMPANY_CODE","PO_NUMBER","VENDOR_NUM","VENDOR_NAME","VENDOR_ACCT",
            "WBS_ELEMENT","WBS_DESC","CAPEX","PROFIT_CENTRE",
            "CAPABILITY_MANAGER","CAPABILITY_MANAGER_NAME","CAPABILITY_MANAGER_PROGRAM",
            "DELIVERY_MANAGER","DELIVERY_MANAGER_NAME","DELIVERY_MANAGER_PROGRAM",
            "POC_EMAIL","GL_ACCOUNT","CONTRACT_NO","VIM_DOCUMENT_ID","DOC_NO_ACCOUNTING",
            "INVOICE_RECEIVED_DATE","INVOICE_DATE","GR_CREATE_DATE_LATEST","CURRENCY",
            "GL_LINE_VALUE_INCL_GST","INVOICE_VALUE_INCL_GST","PAYMENT_TERMS","MATERIAL_PO",
            "EXCLUSION_FLAG","EXCLUSION_TEST","EXCLUSION_DESCRIPTOR","POSSIBLE_PAYMENT",
            "POSSIBLE_DUPLICATE_CLEARING","CONTRACT_VALUE_LOC_EX_GST","PAYMENT_RUN_DATE",
            "BODS_PAYMT_BASELINE_DATE","DAYS_VARIANCE","DAILY_RATE","INVOICE_INTEREST_AMOUNT",
            "INTEREST_PAYABLE","SOURCE_SYSTEM","PAYMENT_CHANNEL","DOCUMENT_TYPE",
            "VENDOR_INVOICE_NO","CLEARING_MONTH"
        };

        [Serializable]
        public class ParsedRow
        {
            public Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int LineNumber;
            public string DocNoAccounting { get { string v; return Fields.TryGetValue("DOC_NO_ACCOUNTING", out v) ? v : null; } }
        }

        [Serializable]
        public class ParseResult
        {
            public List<ParsedRow> Rows = new List<ParsedRow>();
            public List<string> Headers = new List<string>();
            public List<string> HeaderErrors = new List<string>();
            public bool HeaderValid { get { return HeaderErrors.Count == 0; } }
        }

        /// <summary>
        /// Parse a tab-delimited BODS file from disk.
        /// </summary>
        public static ParseResult Parse(string fullPath)
        {
            using (var fs = File.OpenRead(fullPath))
                return Parse(fs);
        }

        public static ParseResult Parse(Stream stream)
        {
            var result = new ParseResult();

            using (var sr = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                int lineNo = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    if (lineNo == 1)
                    {
                        result.Headers = SplitTab(line);
                        ValidateHeaders(result);
                        if (!result.HeaderValid) return result;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = SplitTab(line);
                    var row = new ParsedRow { LineNumber = lineNo };
                    for (int i = 0; i < result.Headers.Count; i++)
                    {
                        var key = result.Headers[i];
                        var val = i < parts.Count ? parts[i] : "";
                        row.Fields[key] = val;
                    }
                    result.Rows.Add(row);
                }
            }
            return result;
        }

        private static List<string> SplitTab(string line)
        {
            // Simple tab split — BODS extracts are guaranteed not to contain
            // embedded tabs in fields. Trailing empties preserved.
            return line.Split('\t').ToList();
        }

        private static void ValidateHeaders(ParseResult r)
        {
            var found = new HashSet<string>(r.Headers, StringComparer.OrdinalIgnoreCase);
            foreach (var h in ExpectedHeaders)
                if (!found.Contains(h))
                    r.HeaderErrors.Add("Missing column: " + h);
        }

        // -------------------------------------------------------------------
        // Commit parsed rows into tblLPPI_Documents and create a load batch.
        // Skip-and-warn duplicates by DOC_NO_ACCOUNTING.
        // -------------------------------------------------------------------

        public class CommitResult
        {
            public int BatchID;
            public int RowsInFile;
            public int RowsInserted;
            public int RowsSkipped;
            public int RowsFailed;
            public List<string> SkippedDocNumbers = new List<string>();
            public List<string> FailedRows = new List<string>();
        }

        public static CommitResult Commit(ParseResult parsed, string fileName, string sourcePath,
                                          long fileSize, DateTime? modifiedDate)
        {
            var res = new CommitResult { RowsInFile = parsed.Rows.Count };
            var loadedBy = LPPIHelper.CurrentUserId();
            var loadedByName = LPPIHelper.CurrentUserDisplayName();

            // Create batch row and capture identity via OUTPUT inserted.
            object newId = LPPIHelper.ExecuteScalar(@"
INSERT INTO dbo.tblLPPI_LoadBatches
   (FileName, SourcePath, FileSizeBytes, FileModifiedDate, LoadedByUserId, LoadedByName, RowsInFile)
OUTPUT inserted.BatchID
VALUES (@FileName, @SourcePath, @FileSize, @Modified, @UserId, @UserName, @RowsInFile);",
                LPPIHelper.P("@FileName",   fileName),
                LPPIHelper.P("@SourcePath", (object)sourcePath ?? DBNull.Value),
                LPPIHelper.P("@FileSize",   fileSize),
                LPPIHelper.P("@Modified",   (object)modifiedDate ?? DBNull.Value),
                LPPIHelper.P("@UserId",     loadedBy),
                LPPIHelper.P("@UserName",   loadedByName),
                LPPIHelper.P("@RowsInFile", parsed.Rows.Count));
            res.BatchID = Convert.ToInt32(newId);

            // Insert each row, skipping duplicates.
            foreach (var row in parsed.Rows)
            {
                var docNo = LPPIHelper.CleanString(row.DocNoAccounting);
                if (string.IsNullOrEmpty(docNo))
                {
                    res.RowsFailed++;
                    res.FailedRows.Add(string.Format("Line {0}: missing DOC_NO_ACCOUNTING", row.LineNumber));
                    continue;
                }

                object exists = LPPIHelper.ExecuteScalar(
                    "SELECT 1 FROM dbo.tblLPPI_Documents WHERE DocNoAccounting = @D",
                    LPPIHelper.P("@D", docNo));
                if (exists != null)
                {
                    res.RowsSkipped++;
                    res.SkippedDocNumbers.Add(docNo);
                    continue;
                }

                try
                {
                    InsertDocument(res.BatchID, docNo, row);
                    res.RowsInserted++;
                }
                catch (Exception ex)
                {
                    res.RowsFailed++;
                    res.FailedRows.Add(string.Format("Line {0} (doc {1}): {2}",
                        row.LineNumber, docNo, ex.Message));
                }
            }

            // Update batch totals.
            LPPIHelper.ExecuteNonQuery(@"
UPDATE dbo.tblLPPI_LoadBatches
   SET RowsInserted = @I, RowsSkipped = @S, RowsFailed = @F
 WHERE BatchID = @B",
                LPPIHelper.P("@I", res.RowsInserted),
                LPPIHelper.P("@S", res.RowsSkipped),
                LPPIHelper.P("@F", res.RowsFailed),
                LPPIHelper.P("@B", res.BatchID));

            return res;
        }

        private static void InsertDocument(int batchId, string docNo, ParsedRow row)
        {
            const string sql = @"
INSERT INTO dbo.tblLPPI_Documents
( DocNoAccounting, BatchID, CompanyCode, PoNumber, VendorNum, VendorName, VendorAcct,
  WbsElement, WbsDesc, Capex, ProfitCentre,
  CapabilityManager, CapabilityManagerName, CapabilityManagerProgram,
  DeliveryManager, DeliveryManagerName, DeliveryManagerProgram,
  PocEmail, GlAccount, ContractNo, VimDocumentId,
  InvoiceReceivedDate, InvoiceDate, GrCreateDateLatest, Currency,
  GlLineValueInclGst, InvoiceValueInclGst, PaymentTerms, MaterialPo,
  ExclusionFlag, ExclusionTest, ExclusionDescriptor,
  PossiblePayment, PossibleDuplicateClearing, ContractValueLocExGst,
  PaymentRunDate, BodsPaymtBaselineDate, DaysVariance, DailyRate,
  InvoiceInterestAmount, InterestPayable, SourceSystem, PaymentChannel,
  DocumentType, VendorInvoiceNo, ClearingMonth )
VALUES
( @DocNo, @BatchID, @CompanyCode, @PoNumber, @VendorNum, @VendorName, @VendorAcct,
  @WbsElement, @WbsDesc, @Capex, @ProfitCentre,
  @CapabilityManager, @CapabilityManagerName, @CapabilityManagerProgram,
  @DeliveryManager, @DeliveryManagerName, @DeliveryManagerProgram,
  @PocEmail, @GlAccount, @ContractNo, @VimDocumentId,
  @InvoiceReceivedDate, @InvoiceDate, @GrCreateDateLatest, @Currency,
  @GlLineValueInclGst, @InvoiceValueInclGst, @PaymentTerms, @MaterialPo,
  @ExclusionFlag, @ExclusionTest, @ExclusionDescriptor,
  @PossiblePayment, @PossibleDuplicateClearing, @ContractValueLocExGst,
  @PaymentRunDate, @BodsPaymtBaselineDate, @DaysVariance, @DailyRate,
  @InvoiceInterestAmount, @InterestPayable, @SourceSystem, @PaymentChannel,
  @DocumentType, @VendorInvoiceNo, @ClearingMonth );";

            Func<string, string>    S = k => LPPIHelper.CleanString(row.Fields.ContainsKey(k) ? row.Fields[k] : null);
            Func<string, DateTime?> D = k => LPPIHelper.ParseDate(S(k));
            Func<string, decimal?>  M = k => LPPIHelper.ParseDecimal(S(k));
            Func<string, int?>      I = k => LPPIHelper.ParseInt(S(k));

            LPPIHelper.ExecuteNonQuery(sql,
                LPPIHelper.P("@DocNo",                     docNo),
                LPPIHelper.P("@BatchID",                   batchId),
                LPPIHelper.P("@CompanyCode",               (object)S("COMPANY_CODE")                 ?? DBNull.Value),
                LPPIHelper.P("@PoNumber",                  (object)S("PO_NUMBER")                    ?? DBNull.Value),
                LPPIHelper.P("@VendorNum",                 (object)S("VENDOR_NUM")                   ?? DBNull.Value),
                LPPIHelper.P("@VendorName",                (object)S("VENDOR_NAME")                  ?? DBNull.Value),
                LPPIHelper.P("@VendorAcct",                (object)S("VENDOR_ACCT")                  ?? DBNull.Value),
                LPPIHelper.P("@WbsElement",                (object)S("WBS_ELEMENT")                  ?? DBNull.Value),
                LPPIHelper.P("@WbsDesc",                   (object)S("WBS_DESC")                     ?? DBNull.Value),
                LPPIHelper.P("@Capex",                     (object)S("CAPEX")                        ?? DBNull.Value),
                LPPIHelper.P("@ProfitCentre",              (object)S("PROFIT_CENTRE")                ?? DBNull.Value),
                LPPIHelper.P("@CapabilityManager",         (object)S("CAPABILITY_MANAGER")           ?? DBNull.Value),
                LPPIHelper.P("@CapabilityManagerName",     (object)S("CAPABILITY_MANAGER_NAME")      ?? DBNull.Value),
                LPPIHelper.P("@CapabilityManagerProgram",  (object)S("CAPABILITY_MANAGER_PROGRAM")   ?? DBNull.Value),
                LPPIHelper.P("@DeliveryManager",           (object)S("DELIVERY_MANAGER")             ?? DBNull.Value),
                LPPIHelper.P("@DeliveryManagerName",       (object)S("DELIVERY_MANAGER_NAME")        ?? DBNull.Value),
                LPPIHelper.P("@DeliveryManagerProgram",    (object)S("DELIVERY_MANAGER_PROGRAM")     ?? DBNull.Value),
                LPPIHelper.P("@PocEmail",                  (object)S("POC_EMAIL")                    ?? DBNull.Value),
                LPPIHelper.P("@GlAccount",                 (object)S("GL_ACCOUNT")                   ?? DBNull.Value),
                LPPIHelper.P("@ContractNo",                (object)S("CONTRACT_NO")                  ?? DBNull.Value),
                LPPIHelper.P("@VimDocumentId",             (object)S("VIM_DOCUMENT_ID")              ?? DBNull.Value),
                LPPIHelper.P("@InvoiceReceivedDate",       (object)D("INVOICE_RECEIVED_DATE")        ?? DBNull.Value),
                LPPIHelper.P("@InvoiceDate",               (object)D("INVOICE_DATE")                 ?? DBNull.Value),
                LPPIHelper.P("@GrCreateDateLatest",        (object)D("GR_CREATE_DATE_LATEST")        ?? DBNull.Value),
                LPPIHelper.P("@Currency",                  (object)S("CURRENCY")                     ?? DBNull.Value),
                LPPIHelper.P("@GlLineValueInclGst",        (object)M("GL_LINE_VALUE_INCL_GST")       ?? DBNull.Value),
                LPPIHelper.P("@InvoiceValueInclGst",       (object)M("INVOICE_VALUE_INCL_GST")       ?? DBNull.Value),
                LPPIHelper.P("@PaymentTerms",              (object)S("PAYMENT_TERMS")                ?? DBNull.Value),
                LPPIHelper.P("@MaterialPo",                (object)S("MATERIAL_PO")                  ?? DBNull.Value),
                LPPIHelper.P("@ExclusionFlag",             (object)S("EXCLUSION_FLAG")               ?? DBNull.Value),
                LPPIHelper.P("@ExclusionTest",             (object)S("EXCLUSION_TEST")               ?? DBNull.Value),
                LPPIHelper.P("@ExclusionDescriptor",       (object)S("EXCLUSION_DESCRIPTOR")         ?? DBNull.Value),
                LPPIHelper.P("@PossiblePayment",           (object)S("POSSIBLE_PAYMENT")             ?? DBNull.Value),
                LPPIHelper.P("@PossibleDuplicateClearing", (object)S("POSSIBLE_DUPLICATE_CLEARING")  ?? DBNull.Value),
                LPPIHelper.P("@ContractValueLocExGst",     (object)M("CONTRACT_VALUE_LOC_EX_GST")    ?? DBNull.Value),
                LPPIHelper.P("@PaymentRunDate",            (object)D("PAYMENT_RUN_DATE")             ?? DBNull.Value),
                LPPIHelper.P("@BodsPaymtBaselineDate",     (object)D("BODS_PAYMT_BASELINE_DATE")     ?? DBNull.Value),
                LPPIHelper.P("@DaysVariance",              (object)I("DAYS_VARIANCE")                ?? DBNull.Value),
                LPPIHelper.P("@DailyRate",                 (object)M("DAILY_RATE")                   ?? DBNull.Value),
                LPPIHelper.P("@InvoiceInterestAmount",     (object)M("INVOICE_INTEREST_AMOUNT")      ?? DBNull.Value),
                LPPIHelper.P("@InterestPayable",           (object)M("INTEREST_PAYABLE")             ?? DBNull.Value),
                LPPIHelper.P("@SourceSystem",              (object)S("SOURCE_SYSTEM")                ?? DBNull.Value),
                LPPIHelper.P("@PaymentChannel",            (object)S("PAYMENT_CHANNEL")              ?? DBNull.Value),
                LPPIHelper.P("@DocumentType",              (object)S("DOCUMENT_TYPE")                ?? DBNull.Value),
                LPPIHelper.P("@VendorInvoiceNo",           (object)S("VENDOR_INVOICE_NO")            ?? DBNull.Value),
                LPPIHelper.P("@ClearingMonth",             (object)S("CLEARING_MONTH")               ?? DBNull.Value));
        }
    }
}
