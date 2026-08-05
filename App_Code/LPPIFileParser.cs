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
    ///
    /// File format v2 (April 2026): 48 columns. TAX_CODE, ITEM_SEQUENCE and
    /// FISCAL_YEAR were added by BODS so one DOC_NO_ACCOUNTING may now have
    /// multiple rows (one per item/line).
    ///
    /// Line-level uniqueness model (May 2026): tblLPPI_Documents allows
    /// multiple historical rows for the same (DocNoAccounting, ItemSequence)
    /// as long as only one is live. The filtered unique index
    /// UX_tblLPPI_Documents_Live_DocNoAccounting_ItemSequence enforces:
    /// at most one row per (DocNoAccounting, ItemSequence) where
    /// IsDeactivated = 0. Deactivated history rows are exempt.
    ///
    /// May 2026: after package reconciliation, ReconcilePocs populates
    /// tblLPPI_PackagePocs with one row per (PackageID, PocEmail) pair
    /// across the new package's documents, so every distinct POC has their
    /// own unguessable reviewer token. Only acts on NotSent packages —
    /// once a package transitions to Sent its POC set is frozen alongside
    /// its document set.
    /// </summary>
    public static class LPPIFileParser
    {
        public static readonly string[] ExpectedHeaders = new[]
        {
            "COMPANY_CODE","PO_NUMBER","VENDOR_NUM","VENDOR_NAME","VENDOR_ACCT",
            "WBS_ELEMENT","WBS_DESC","CAPEX","PROFIT_CENTRE",
            "CAPABILITY_MANAGER","CAPABILITY_MANAGER_NAME","CAPABILITY_MANAGER_PROGRAM",
            "DELIVERY_MANAGER","DELIVERY_MANAGER_NAME","DELIVERY_MANAGER_PROGRAM",
            "POC_EMAIL","GL_ACCOUNT","TAX_CODE","CONTRACT_NO","VIM_DOCUMENT_ID",
            "DOC_NO_ACCOUNTING","ITEM_SEQUENCE","FISCAL_YEAR",
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
        // Skip-and-warn duplicates by (DOC_NO_ACCOUNTING, ITEM_SEQUENCE).
        // Auto-creates any CM groups seen in the file that do not yet exist.
        // After insert, reconciles documents into review packages, then
        // populates POC token rows for any NotSent package whose document
        // set just changed.
        // -------------------------------------------------------------------

        public class CommitResult
        {
            public int BatchID;
            public int RowsInFile;
            public int RowsInserted;
            public int RowsSkipped;
            public int RowsFailed;
            // Skipped identifiers now include item sequence, e.g. "5100366318 / 002"
            public List<string> SkippedDocNumbers = new List<string>();
            public List<string> FailedRows = new List<string>();
            // Programs that were created automatically during this commit.
            public List<string> NewPrograms = new List<string>();
            // Package reconciliation outcomes.
            public int PackagesCreated;
            public int DocumentsAddedToExistingPackages;
            // POC token rows created during ReconcilePocs. New rows only —
            // pre-existing (PackageID, PocEmail) pairs are not counted.
            public int PocTokensCreated;
            // Rows that superseded a previously-deactivated line via the
            // RC-RL reload path. Subset of RowsInserted; surfaced separately
            // so the load-result panel can tell the operator how many
            // corrections came through.
            public int RowsSuperseded;
        }

        public static CommitResult Commit(ParseResult parsed, string fileName, string sourcePath,
                                          long fileSize, DateTime? modifiedDate)
        {
            var res = new CommitResult { RowsInFile = parsed.Rows.Count };
            var loadedBy = LPPIHelper.CurrentUserId();
            var loadedByName = LPPIHelper.CurrentUserDisplayName();

            // ------------------------------------------------------------------
            // Auto-create CM groups for any program codes in the file that do
            // not yet exist in tblLPPI_CapabilityManagers.
            // ------------------------------------------------------------------
            AutoCreateCapabilityManagers(parsed, res);

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

            // Insert each row, skipping plain duplicates by the
            // (DocNoAccounting, ItemSequence) live key and superseding
            // reload-eligible (RC-RL) deactivated rows where they exist.
            foreach (var row in parsed.Rows)
            {
                var docNo = LPPIHelper.CleanString(row.DocNoAccounting);
                if (string.IsNullOrEmpty(docNo))
                {
                    res.RowsFailed++;
                    res.FailedRows.Add(string.Format("Line {0}: missing DOC_NO_ACCOUNTING", row.LineNumber));
                    continue;
                }

                // ITEM_SEQUENCE is NOT NULL in tblLPPI_Documents. If it is
                // blank or non-numeric the row cannot be inserted — fail it
                // up-front with a clear message rather than letting the
                // INSERT crash downstream.
                string rawSeq = null;
                if (row.Fields.ContainsKey("ITEM_SEQUENCE"))
                    rawSeq = LPPIHelper.CleanString(row.Fields["ITEM_SEQUENCE"]);

                int? seq = LPPIHelper.ParseInt(rawSeq);
                if (!seq.HasValue)
                {
                    res.RowsFailed++;
                    res.FailedRows.Add(string.Format(
                        "Line {0} (doc {1}): ITEM_SEQUENCE is blank or not numeric",
                        row.LineNumber, docNo));
                    continue;
                }

                // Look up the current state of (DocNoAccounting, ItemSequence).
                // We want the row at the END of the supersession chain
                // (SupersededByDocumentID IS NULL). That is at most one row:
                //   - the only matching live row, OR
                //   - the most recently deactivated row that has not yet been
                //     superseded by a later load.
                //
                // Why filter on SupersededByDocumentID IS NULL? After multiple
                // RC-RL → reload cycles, several historical rows can exist for
                // the same (DocNoAccounting, ItemSequence). Without the filter,
                // SQL Server is free to return any of them. With the filter,
                // we always get the chain-terminating row, which is the only
                // one the load logic should make decisions against.
                DataTable existsRow = LPPIHelper.ExecuteTable(
                    @"SELECT DocumentID, IsDeactivated, SupersededByDocumentID
                        FROM dbo.tblLPPI_Documents
                       WHERE DocNoAccounting          = @D
                         AND ItemSequence             = @Seq
                         AND SupersededByDocumentID IS NULL",
                    LPPIHelper.P("@D",   docNo),
                    LPPIHelper.P("@Seq", seq.Value));

                if (existsRow.Rows.Count > 0)
                {
                    DataRow er = existsRow.Rows[0];
                    int  existingId    = Convert.ToInt32(er["DocumentID"]);
                    bool isDeactivated = (er["IsDeactivated"] != DBNull.Value)
                                         && Convert.ToBoolean(er["IsDeactivated"]);

                    if (isDeactivated)
                    {
                        // (a) Reload-eligible. Insert the new row AND stamp
                        //     the old one's SupersededByDocumentID in a
                        //     single atomic SQL batch so the two writes
                        //     either both commit or both roll back.
                        //
                        //     The filtered unique index on (DocNoAccounting,
                        //     ItemSequence) WHERE IsDeactivated = 0 permits
                        //     this insert — the existing row is deactivated
                        //     and therefore exempt from the live uniqueness
                        //     check.
                        try
                        {
                            int newDocId = InsertDocumentSupersedingExisting(
                                res.BatchID, docNo, seq.Value, row, existingId);
                            res.RowsInserted++;
                            res.RowsSuperseded++;
                        }
                        catch (Exception ex)
                        {
                            res.RowsFailed++;
                            res.FailedRows.Add(string.Format(
                                "Line {0} (doc {1} / {2:000}): superseding reload-eligible row failed: {3}",
                                row.LineNumber, docNo, seq.Value, ex.Message));
                        }
                        continue;
                    }

                    // (b) Plain live duplicate — skip and warn. This is the
                    //     normal "the same file was loaded twice" path.
                    res.RowsSkipped++;
                    res.SkippedDocNumbers.Add(string.Format("{0} / {1:000}", docNo, seq.Value));
                    continue;
                }

                // No existing row at the end of the chain — insert fresh.
                try
                {
                    InsertDocument(res.BatchID, docNo, seq.Value, row);
                    res.RowsInserted++;
                }
                catch (Exception ex)
                {
                    res.RowsFailed++;
                    res.FailedRows.Add(string.Format("Line {0} (doc {1} / {2:000}): {3}",
                        row.LineNumber, docNo, seq.Value, ex.Message));
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

            // ------------------------------------------------------------------
            // Reconcile packages — add unreviewed docs that are not already
            // in any non-Cancelled package into either the CM's existing
            // NotSent package or a fresh NotSent package.
            // ------------------------------------------------------------------
            ReconcilePackages(res);

            // ------------------------------------------------------------------
            // Reconcile POC tokens — for every NotSent package, ensure there
            // is a tblLPPI_PackagePocs row for each distinct PocEmail across
            // its documents. Idempotent and additive: only inserts new
            // (PackageID, PocEmail) pairs. Sent / InReview / Finalised /
            // Exported / Cancelled packages are left alone.
            // ------------------------------------------------------------------
            ReconcilePocs(res);

            return res;
        }

        /// <summary>
        /// Collects every distinct non-empty CAPABILITY_MANAGER_PROGRAM value
        /// from the parsed rows, then upserts each one via UpsertCapabilityManager.
        /// Programs that did not already exist are recorded in res.NewPrograms.
        /// </summary>
        private static void AutoCreateCapabilityManagers(ParseResult parsed, CommitResult res)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in parsed.Rows)
            {
                string prog = LPPIHelper.CleanString(
                    row.Fields.ContainsKey("CAPABILITY_MANAGER_PROGRAM")
                        ? row.Fields["CAPABILITY_MANAGER_PROGRAM"] : null);
                if (string.IsNullOrEmpty(prog) || seen.Contains(prog)) continue;
                seen.Add(prog);

                // Check whether this program already exists (any active state).
                object existing = LPPIHelper.ExecuteScalar(
                    "SELECT CmID FROM dbo.tblLPPI_CapabilityManagers WHERE Program = @P",
                    LPPIHelper.P("@P", prog));

                if (existing == null || existing == DBNull.Value)
                {
                    LPPIHelper.UpsertCapabilityManager(prog, true);
                    res.NewPrograms.Add(prog);
                }
                // If it already exists, leave it alone — do not overwrite
                // the admin-maintained email or active flag. The per-line
                // CAPABILITY_MANAGER_NAME from BODS still goes onto
                // tblLPPI_Documents.CapabilityManagerName for the reviewer
                // page's cost-centre tooltip.
            }
        }

        // -------------------------------------------------------------------
        // Package reconciliation
        //
        // Rule:
        //   For each CM, identify documents that are unreviewed AND not
        //   already attached to any non-Cancelled package. Those are the
        //   "loose" documents that need a home.
        //
        //   - If the CM has a NotSent package, add the loose docs to it.
        //   - Otherwise, create a fresh NotSent package and add them.
        //
        //   Docs already in any Sent / InReview / Complete package are
        //   ignored — those packages are frozen.
        //   Docs in Cancelled packages are eligible to be repackaged.
        //
        // We use the document's first-line DocumentID as the package
        // membership key, matching the rest of the LPPI codebase.
        //
        // CMs are processed alphabetically by Program name so that PackageIDs
        // come out in CM order. This keeps the Send-outs and Dashboard
        // tables (both alpha-sorted) tidy after a fresh load.
        // -------------------------------------------------------------------

        private static void ReconcilePackages(CommitResult res)
        {
            // Pull the candidate first-line DocumentIDs grouped by CmID.
            // A document is a candidate iff:
            //   - it is unreviewed (no row in tblLPPI_Reviews with a reason code), AND
            //   - it is not already in any non-Cancelled package.
            //
            // Working at first-line DocumentID matches everything else in
            // the codebase (Reviews, ReviewPackageDocuments).
            //
            // cm.Program is projected so the dispatch loop can iterate
            // alphabetically by Program name.
            //
            // Restricted to LIVE rows (IsDeactivated = 0). Deactivated history
            // rows must never be packaged — they live in tblLPPI_Documents only
            // for the audit chain and the deactivated watch-list.
            const string sql = @"
SELECT cm.CmID,
       cm.Program,
       (SELECT MIN(d.DocumentID)
          FROM dbo.tblLPPI_Documents d
         WHERE d.DocNoAccounting = dn.DocNoAccounting
           AND d.IsDeactivated   = 0) AS FirstLineDocId
  FROM dbo.tblLPPI_CapabilityManagers cm
  INNER JOIN (
        SELECT DISTINCT d2.CapabilityManagerProgram, d2.DocNoAccounting
          FROM dbo.tblLPPI_Documents d2
         WHERE d2.IsDeactivated = 0
       ) dn ON dn.CapabilityManagerProgram = cm.Program
  WHERE cm.IsActive = 1
    AND NOT EXISTS (
            -- A review on a DEACTIVATED (RC-RL-stamped) row in a prior cycle
            -- must not block the corrected live row from being packaged.
            -- Filter the join target to live rows only.
            SELECT 1 FROM dbo.tblLPPI_Reviews r
            INNER JOIN dbo.tblLPPI_Documents dx ON dx.DocumentID = r.DocumentID
            WHERE dx.DocNoAccounting = dn.DocNoAccounting
              AND dx.IsDeactivated   = 0
              AND r.ReasonCodeID IS NOT NULL)
    AND NOT EXISTS (
            -- Package membership of the OLD (deactivated) row in an earlier
            -- Finalised package must not block the NEW corrected row from
            -- being packaged. Filter the join target to live rows only.
            SELECT 1 FROM dbo.tblLPPI_ReviewPackageDocuments pd
            INNER JOIN dbo.tblLPPI_ReviewPackages p ON p.PackageID = pd.PackageID
            INNER JOIN dbo.tblLPPI_Documents dx2 ON dx2.DocumentID = pd.DocumentID
            WHERE dx2.DocNoAccounting = dn.DocNoAccounting
              AND dx2.IsDeactivated   = 0
              AND p.Status <> 'Cancelled')";
            var dt = LPPIHelper.ExecuteTable(sql);

            // Group by CmID, preserving Program for ordering.
            var byCm = new Dictionary<int, CmBucket>();
            foreach (DataRow r in dt.Rows)
            {
                int cmId   = Convert.ToInt32(r["CmID"]);
                string prog = Convert.ToString(r["Program"]);
                if (r["FirstLineDocId"] == DBNull.Value) continue;
                int docId  = Convert.ToInt32(r["FirstLineDocId"]);

                CmBucket bucket;
                if (!byCm.TryGetValue(cmId, out bucket))
                {
                    bucket = new CmBucket { CmID = cmId, Program = prog ?? "" };
                    byCm[cmId] = bucket;
                }
                bucket.DocIds.Add(docId);
            }

            // Iterate alphabetically by Program. Case-insensitive ordinal
            // ordering matches how the Send-outs and Dashboard tables sort
            // (SQL Server default collation, which is case-insensitive on
            // CPLATFORM).
            var ordered = byCm.Values
                .OrderBy(b => b.Program, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int defaultDueDays = LPPIHelper.DefaultDueDays;
            string createdBy   = LPPIHelper.CurrentUserDisplayName();

            foreach (var bucket in ordered)
            {
                int cmId = bucket.CmID;
                List<int> docIds = bucket.DocIds;
                if (docIds.Count == 0) continue;

                // Find an existing NotSent package for this CM, if any.
                object existingPkgIdObj = LPPIHelper.ExecuteScalar(@"
SELECT TOP 1 PackageID
  FROM dbo.tblLPPI_ReviewPackages
 WHERE CmID = @cm AND Status = 'NotSent'
 ORDER BY CreatedDate DESC",
                    LPPIHelper.P("@cm", cmId));

                int packageId;
                bool isNewPackage = (existingPkgIdObj == null || existingPkgIdObj == DBNull.Value);

                if (isNewPackage)
                {
                    string token = LPPIHelper.GenerateToken();
                    DateTime due = DateTime.Today.AddDays(defaultDueDays);
                    object newPkgId = LPPIHelper.ExecuteScalar(@"
INSERT INTO dbo.tblLPPI_ReviewPackages
    (CmID, Token, CreatedDate, CreatedBy, DueDate, Status)
OUTPUT inserted.PackageID
VALUES (@cm, @tok, SYSDATETIME(), @by, @due, 'NotSent')",
                        LPPIHelper.P("@cm",  cmId),
                        LPPIHelper.P("@tok", token),
                        LPPIHelper.P("@by",  createdBy),
                        LPPIHelper.P("@due", due));
                    packageId = Convert.ToInt32(newPkgId);
                    res.PackagesCreated++;
                }
                else
                {
                    packageId = Convert.ToInt32(existingPkgIdObj);
                }

                // Add each candidate document to the package. Use INSERT WHERE
                // NOT EXISTS to be safe against any racing duplicates — the
                // primary key would catch them anyway, but this is cleaner.
                int added = 0;
                foreach (int docId in docIds)
                {
                    int rows = LPPIHelper.ExecuteNonQuery(@"
INSERT INTO dbo.tblLPPI_ReviewPackageDocuments (PackageID, DocumentID)
SELECT @pkg, @doc
 WHERE NOT EXISTS (
    SELECT 1 FROM dbo.tblLPPI_ReviewPackageDocuments
     WHERE PackageID = @pkg AND DocumentID = @doc
 )",
                        LPPIHelper.P("@pkg", packageId),
                        LPPIHelper.P("@doc", docId));
                    if (rows > 0) added++;
                }

                if (!isNewPackage)
                {
                    res.DocumentsAddedToExistingPackages += added;
                }
            }
        }

        // -------------------------------------------------------------------
        // POC token reconciliation
        //
        // For every NotSent package, ensure there is a tblLPPI_PackagePocs
        // row for each distinct non-blank PocEmail across the package's
        // documents. New rows get a freshly generated unguessable Token.
        //
        // Idempotent: looks up existing (PackageID, PocEmail) pairs and
        // skips them. Safe to re-run; a re-load of the same file does not
        // create duplicate POC rows.
        //
        // Frozen-set rule: Sent / InReview / Finalised / Exported / Cancelled
        // packages are deliberately untouched. Their POC set is whatever was
        // committed at first send time — POCs that turn up on a later load
        // do not retroactively get added to a package that has already been
        // dispatched.
        //
        // POC email format is not validated here. The send pipeline filters
        // out malformed addresses at dispatch time and logs them; row-level
        // validation at load would be too heavy-handed since BODS occasionally
        // emits placeholder values like "TBA" that do not parse.
        // -------------------------------------------------------------------

        private static void ReconcilePocs(CommitResult res)
        {
            // One-shot SQL: insert POC rows that don't already exist for any
            // NotSent package, generating a token in T-SQL via NEWID-derived
            // hex (URL-safe, ~32 chars). Single round-trip.
            //
            // Why not loop in C# and call GenerateToken()? Because for a
            // 30-CM x 30-POC load that's 900 round-trips. The token shape
            // here is the same character class as GenerateToken() output
            // and the unique constraint catches the (statistically
            // impossible) collision case.
            const string sql = @"
INSERT INTO dbo.tblLPPI_PackagePocs (PackageID, PocEmail, Token)
SELECT n.PackageID,
       n.PocEmail,
       LOWER(REPLACE(REPLACE(REPLACE(
           CONVERT(NVARCHAR(40),
               CAST(CAST(NEWID() AS BINARY(16)) AS VARBINARY(16)), 2),
           '+', '-'), '/', '_'), '=', ''))
  FROM (
        SELECT DISTINCT
               p.PackageID,
               LTRIM(RTRIM(d.PocEmail)) AS PocEmail
          FROM dbo.tblLPPI_ReviewPackages       p
          INNER JOIN dbo.tblLPPI_ReviewPackageDocuments pd ON pd.PackageID = p.PackageID
          INNER JOIN dbo.tblLPPI_Documents             d  ON d.DocumentID = pd.DocumentID
         WHERE p.Status = 'NotSent'
           AND d.PocEmail IS NOT NULL
           AND LTRIM(RTRIM(d.PocEmail)) <> ''
       ) n
 WHERE NOT EXISTS (
       SELECT 1 FROM dbo.tblLPPI_PackagePocs ex
        WHERE ex.PackageID = n.PackageID
          AND ex.PocEmail  = n.PocEmail);";

            int created = LPPIHelper.ExecuteNonQuery(sql);
            res.PocTokensCreated = created;
        }

        /// <summary>
        /// Per-CM bucket used by ReconcilePackages so that iteration can be
        /// ordered by Program name while the package INSERT still has the
        /// CmID it needs.
        /// </summary>
        private class CmBucket
        {
            public int    CmID;
            public string Program;
            public List<int> DocIds = new List<int>();
        }

        // -------------------------------------------------------------------
        // INSERT INTO tblLPPI_Documents — column list shared by both the
        // simple-insert and the supersede-and-insert paths. Defined here as
        // a constant so the two callers can not drift apart.
        // -------------------------------------------------------------------
        private const string DocumentInsertColumns = @"
( DocNoAccounting, ItemSequence, BatchID, CompanyCode, PoNumber, VendorNum, VendorName, VendorAcct,
  WbsElement, WbsDesc, Capex, ProfitCentre,
  CapabilityManager, CapabilityManagerName, CapabilityManagerProgram,
  DeliveryManager, DeliveryManagerName, DeliveryManagerProgram,
  PocEmail, GlAccount, TaxCode, ContractNo, VimDocumentId,
  InvoiceReceivedDate, InvoiceDate, GrCreateDateLatest, Currency,
  GlLineValueInclGst, InvoiceValueInclGst, PaymentTerms, MaterialPo,
  ExclusionFlag, ExclusionTest, ExclusionDescriptor,
  PossiblePayment, PossibleDuplicateClearing, ContractValueLocExGst,
  PaymentRunDate, BodsPaymtBaselineDate, DaysVariance, DailyRate,
  InvoiceInterestAmount, InterestPayable, SourceSystem, PaymentChannel,
  DocumentType, VendorInvoiceNo, ClearingMonth, FiscalYear )";

        private const string DocumentInsertValues = @"
( @DocNo, @ItemSequence, @BatchID, @CompanyCode, @PoNumber, @VendorNum, @VendorName, @VendorAcct,
  @WbsElement, @WbsDesc, @Capex, @ProfitCentre,
  @CapabilityManager, @CapabilityManagerName, @CapabilityManagerProgram,
  @DeliveryManager, @DeliveryManagerName, @DeliveryManagerProgram,
  @PocEmail, @GlAccount, @TaxCode, @ContractNo, @VimDocumentId,
  @InvoiceReceivedDate, @InvoiceDate, @GrCreateDateLatest, @Currency,
  @GlLineValueInclGst, @InvoiceValueInclGst, @PaymentTerms, @MaterialPo,
  @ExclusionFlag, @ExclusionTest, @ExclusionDescriptor,
  @PossiblePayment, @PossibleDuplicateClearing, @ContractValueLocExGst,
  @PaymentRunDate, @BodsPaymtBaselineDate, @DaysVariance, @DailyRate,
  @InvoiceInterestAmount, @InterestPayable, @SourceSystem, @PaymentChannel,
  @DocumentType, @VendorInvoiceNo, @ClearingMonth, @FiscalYear )";

        /// <summary>
        /// Simple insert. Returns the new DocumentID. Used when there is no
        /// existing row at the end of the supersession chain for this
        /// (DocNoAccounting, ItemSequence).
        /// </summary>
        private static int InsertDocument(int batchId, string docNo, int itemSequence, ParsedRow row)
        {
            string sql =
                "INSERT INTO dbo.tblLPPI_Documents " + DocumentInsertColumns +
                " OUTPUT inserted.DocumentID VALUES " + DocumentInsertValues + ";";

            object newId = LPPIHelper.ExecuteScalar(sql, BuildDocumentParams(batchId, docNo, itemSequence, row));
            return Convert.ToInt32(newId);
        }

        /// <summary>
        /// Insert a corrected row AND stamp the deactivated predecessor's
        /// SupersededByDocumentID, in a single atomic batch.
        ///
        /// Why atomic? If the INSERT succeeded but the UPDATE failed, the
        /// chain would be broken: a live row exists with no back-pointer
        /// from the deactivated predecessor. The next load would then
        /// treat the new (live) row as a plain duplicate and skip the
        /// corrected data on subsequent files. SET XACT_ABORT ON +
        /// explicit BEGIN TRAN/COMMIT gives us all-or-nothing semantics
        /// in one ExecuteScalar round-trip without needing to plumb a
        /// transaction object out from LPPIHelper.
        /// </summary>
        private static int InsertDocumentSupersedingExisting(
            int batchId, string docNo, int itemSequence, ParsedRow row, int oldDocumentId)
        {
            // SCOPE_IDENTITY() captures the new row's DocumentID without
            // needing a T-SQL table variable. We can't use a DECLARE @x
            // table or scalar variable here because LPPIHelper.BuildCommand
            // scans the SQL for @-prefixed tokens and treats every one as
            // an OleDbParameter reference — DECLARE @foo would throw a
            // "no value supplied" error at parameter-rewrite time.
            //
            // SCOPE_IDENTITY() returns the most recent identity generated
            // in the current batch / scope. The INSERT is the only
            // identity-generating statement here; the subsequent UPDATE
            // does not reset it, so it is safe to reference after the
            // UPDATE.
            string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;

INSERT INTO dbo.tblLPPI_Documents " + DocumentInsertColumns + @"
VALUES " + DocumentInsertValues + @";

UPDATE dbo.tblLPPI_Documents
   SET SupersededByDocumentID = SCOPE_IDENTITY()
 WHERE DocumentID = @OldDocId;

COMMIT;

SELECT CAST(SCOPE_IDENTITY() AS INT);";

            // Append @OldDocId to the parameter list built for the INSERT.
            var insertParams = BuildDocumentParams(batchId, docNo, itemSequence, row);
            var all = new OleDbParameter[insertParams.Length + 1];
            Array.Copy(insertParams, all, insertParams.Length);
            all[insertParams.Length] = LPPIHelper.P("@OldDocId", oldDocumentId);

            object newId = LPPIHelper.ExecuteScalar(sql, all);
            return Convert.ToInt32(newId);
        }

        /// <summary>
        /// Build the OleDbParameter[] for the document INSERT column set.
        /// Shared between the simple-insert and supersede-and-insert paths
        /// so the two cannot drift.
        /// </summary>
        private static OleDbParameter[] BuildDocumentParams(int batchId, string docNo, int itemSequence, ParsedRow row)
        {
            Func<string, string>    S = k => LPPIHelper.CleanString(row.Fields.ContainsKey(k) ? row.Fields[k] : null);
            Func<string, DateTime?> D = k => LPPIHelper.ParseDate(S(k));
            Func<string, decimal?>  M = k => LPPIHelper.ParseDecimal(S(k));
            Func<string, int?>      I = k => LPPIHelper.ParseInt(S(k));

            return new[]
            {
                LPPIHelper.P("@DocNo",                     docNo),
                LPPIHelper.P("@ItemSequence",              itemSequence),
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
                LPPIHelper.P("@TaxCode",                   (object)S("TAX_CODE")                     ?? DBNull.Value),
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
                LPPIHelper.P("@ClearingMonth",             (object)S("CLEARING_MONTH")               ?? DBNull.Value),
                LPPIHelper.P("@FiscalYear",                (object)S("FISCAL_YEAR")                  ?? DBNull.Value)
            };
        }
    }
}
