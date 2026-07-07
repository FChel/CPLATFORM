<%@ WebHandler Language="C#" Class="CPlatform.LPPI.LPPI_Review_Save" %>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using System.Web;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Save handler for the reviewer page.
    ///
    /// May 2026 — POC token support
    /// -------------------------------------------------------------------
    /// Accepts both AS Fin (package-level) and POC (POC-scoped) tokens.
    /// POC tokens are subject to an additional server-side scope guard:
    /// the document being saved must be assigned to the POC (i.e. its
    /// first-line PocEmail must match the POC's email). Saves outside
    /// the POC's scope are refused with a "notInPackage" result code so
    /// the client surfaces them as a row-level error.
    ///
    /// Behaviour:
    ///   * BATCH save model. Client posts every dirty row in a single
    ///     request when the user clicks Save. Each row is processed
    ///     independently and gets its own per-row result back, so a partial
    ///     failure (e.g. one stale row, one validation error) does not
    ///     prevent the other rows from saving.
    ///   * OPTIMISTIC LOCKING. Each row carries the ReviewedDate value the
    ///     client loaded with. The handler refuses the row if the current
    ///     ReviewedDate in the database does not match — i.e. someone else
    ///     has saved this document since it was loaded. The client surfaces
    ///     this as a clear "already updated by someone else" message and
    ///     offers a Reload option that picks up the latest values.
    ///   * NO-CHANGE SHORT-CIRCUIT. If the posted values match the current
    ///     stored values exactly, the row is treated as a no-op: no
    ///     UPDATE, no history insert, ok=true with errorCode "noChange".
    ///   * HISTORY. Every row that actually changes state writes one row
    ///     to tblLPPI_ReviewHistory capturing the new state. UPDATE +
    ///     history insert are wrapped in a single transaction per row.
    ///
    /// Authoritative gate: writes are rejected when the package is in any
    /// of these states: Finalised, Exported, Cancelled. Editable states
    /// are NotSent, Sent and InReview.
    ///
    /// Lifecycle transition:
    ///   * If the package was Sent and any row in this batch saved, flip
    ///     Sent -> InReview. Editing a NotSent package does NOT flip
    ///     its status — the package only becomes Sent when the operator
    ///     hits Send on the Send-outs page, which stamps SentDate at the
    ///     same time. POC-token saves participate in this flip identically;
    ///     the first POC who saves any row flips Sent -> InReview just
    ///     like an AS Fin save would.
    ///
    /// There is NO automatic flip to Finalised. Reaching Finalised is an
    /// explicit AS Fin action via the Finalise button on the reviewer page
    /// (separate handler — LPPIHelper.FinalisePackage), and POC tokens are
    /// refused there.
    ///
    /// Posted form fields:
    ///   token              the package or POC token (required)
    ///   action             "save" (required)
    ///   rowCount           number of rows posted, e.g. "3"
    ///   rows[i].docNo      DocNoAccounting for row i
    ///   rows[i].reasonCodeId  optional reason code id; empty means clear
    ///   rows[i].comments      free-text comments
    ///   rows[i].objref        objective reference
    ///   rows[i].version       loaded ReviewedDate (ISO 8601), or "" if
    ///                         no review row had existed at load time
    ///
    /// Response shape:
    ///   {
    ///     "ok":          true | false,
    ///     "error":       "...",         // top-level error if applicable
    ///     "packageStatus": "InReview",  // current package status after the batch
    ///     "results": [
    ///       {
    ///         "docNo":               "5100366318",
    ///         "ok":                  true | false,
    ///         "errorCode":           "noChange" | "stale" | "validation" | "notInPackage" | "server",
    ///         "error":               "Human-readable message (only when ok=false)",
    ///         "newVersion":          "2026-05-05 12:34:56.789", // updated row version
    ///         "newReasonCodeId":     17,
    ///         "newReasonCode":       "RC07",
    ///         "newComments":         "...",
    ///         "newObjectiveReference":"...",
    ///         "newReviewedByName":   "Smith, Jane"
    ///       },
    ///       ...
    ///     ]
    ///   }
    /// </summary>
    public class LPPI_Review_Save : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Cache.SetCacheability(HttpCacheability.NoCache);

            try
            {
                string token = (ctx.Request.Form["token"] ?? "").Trim();
                if (token.Length == 0)
                {
                    WriteTopLevel(ctx, false, "Missing token.", null, null);
                    return;
                }

                // Resolve the token — could be AS Fin or POC. POC tokens
                // get an extra scope guard inside ProcessRow.
                LPPIHelper.ReviewTokenInfo tokenInfo = LPPIHelper.ResolveReviewToken(token);
                if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.None)
                {
                    WriteTopLevel(ctx, false, "Invalid link.", null, null);
                    return;
                }

                int    packageId = tokenInfo.PackageID;
                bool   isPocView = (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.Poc);
                string pocEmail  = isPocView ? (tokenInfo.PocEmail ?? "") : null;

                // Fetch package status for the read-only gate.
                object stObj = LPPIHelper.ExecuteScalar(
                    "SELECT Status FROM tblLPPI_ReviewPackages WHERE PackageID = @p",
                    LPPIHelper.P("@p", packageId));
                if (stObj == null || stObj == DBNull.Value)
                {
                    WriteTopLevel(ctx, false, "Invalid link.", null, null);
                    return;
                }
                string status = Convert.ToString(stObj);

                // Read-only gate: Finalised, Exported and Cancelled all
                // reject writes. Reviewer page renders these with a banner
                // that disables the form, but this is the authoritative
                // server-side check and matches the helper constant so a
                // future status rename only needs to change one place.
                bool readOnly =
                    string.Equals(status, LPPIHelper.StatusFinalised, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, LPPIHelper.StatusExported,  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, LPPIHelper.StatusCancelled, StringComparison.OrdinalIgnoreCase);
                if (readOnly)
                {
                    string msg;
                    if (string.Equals(status, LPPIHelper.StatusFinalised, StringComparison.OrdinalIgnoreCase))
                        msg = "This review package has been finalised by AS Fin and cannot be changed. Contact the LPPI administrator if you believe a change is needed.";
                    else if (string.Equals(status, LPPIHelper.StatusExported, StringComparison.OrdinalIgnoreCase))
                        msg = "This review package has been included in an ERP payment file and is locked.";
                    else
                        msg = "This review package has been cancelled and cannot be changed.";

                    WriteTopLevel(ctx, false, msg, status, null);
                    return;
                }

                // Parse posted rows. We use indexed form keys so this stays
                // form-encoded and consistent with the rest of the codebase
                // — no JSON parser needed.
                int rowCount;
                if (!int.TryParse((ctx.Request.Form["rowCount"] ?? "").Trim(), out rowCount) || rowCount <= 0)
                {
                    WriteTopLevel(ctx, false, "No rows to save.", status, null);
                    return;
                }

                var results = new List<RowResult>(rowCount);
                bool anyRowSaved = false;

                for (int i = 0; i < rowCount; i++)
                {
                    string prefix       = "rows[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                    string docNo        = (ctx.Request.Form[prefix + "docNo"]        ?? "").Trim();
                    string reasonRaw    = (ctx.Request.Form[prefix + "reasonCodeId"] ?? "").Trim();
                    string comments     = (ctx.Request.Form[prefix + "comments"]     ?? "").Trim();
                    string objref       = (ctx.Request.Form[prefix + "objref"]       ?? "").Trim();
                    string reloadRaw    = (ctx.Request.Form[prefix + "reloadBaselineDate"] ?? "").Trim();
                    string loadedVer    = (ctx.Request.Form[prefix + "version"]      ?? "").Trim();

                    if (docNo.Length == 0)
                    {
                        // No way to surface this against a row — skip silently.
                        continue;
                    }

                    RowResult rr = ProcessRow(packageId, isPocView, pocEmail,
                        docNo, reasonRaw, comments, objref, reloadRaw, loadedVer);
                    results.Add(rr);
                    if (rr.Ok && rr.ErrorCode != "noChange") anyRowSaved = true;
                }

                // ----------------------------------------------------------
                // Lifecycle transition — run once at end of batch.
                //
                // First save by reviewer flips Sent -> InReview. The
                // WHERE Status = 'Sent' guard means editing a NotSent
                // package will NOT trip this update.
                //
                // POC saves participate identically — the first POC who
                // saves any row flips Sent -> InReview, the same as if
                // AS Fin had saved the row themselves.
                //
                // There is NO automatic flip to Finalised — that is an
                // explicit AS Fin action via the Finalise button (handled
                // by LPPI_Review_Finalise.ashx + LPPIHelper.FinalisePackage).
                // ----------------------------------------------------------
                string finalStatus = status;
                if (anyRowSaved)
                {
                    LPPIHelper.ExecuteNonQuery(@"
                        UPDATE tblLPPI_ReviewPackages
                           SET Status = 'InReview'
                         WHERE PackageID = @p
                           AND Status   = 'Sent';",
                        LPPIHelper.P("@p", packageId));

                    // Re-read status so the client UI can react if the
                    // package has flipped to InReview.
                    object newStatus = LPPIHelper.ExecuteScalar(
                        "SELECT Status FROM tblLPPI_ReviewPackages WHERE PackageID = @p",
                        LPPIHelper.P("@p", packageId));
                    if (newStatus != null) finalStatus = Convert.ToString(newStatus);
                }

                // ----------------------------------------------------------
                // Determine top-level ok and write the JSON.
                // ----------------------------------------------------------
                bool topOk = true;
                foreach (var r in results)
                {
                    if (!r.Ok) { topOk = false; break; }
                }

                WriteResults(ctx, topOk, null, finalStatus, results);
            }
            catch (Exception ex)
            {
                WriteTopLevel(ctx, false, "Server error: " + ex.Message, null, null);
            }
        }

        // -------------------------------------------------------------------
        // Process a single posted row. Returns the per-row result object.
        // Does not throw — any exception is captured into RowResult.
        //
        // POC scope: when isPocView is true, the document's first-line
        // PocEmail must match pocEmail. Otherwise the row is rejected with
        // errorCode "notInPackage". This is the authoritative server-side
        // scope guard; the reviewer page already filters its visible rows
        // by POC, so a normal client never hits this — but a hostile or
        // out-of-sync client cannot bypass it.
        // -------------------------------------------------------------------
        private static RowResult ProcessRow(int packageId, bool isPocView, string pocEmail,
                                            string docNo, string reasonRaw,
                                            string comments, string objref,
                                            string reloadRaw, string loadedVersion)
        {
            var rr = new RowResult { DocNo = docNo };

            // Normalised RC-RL believed-correct baseline date (yyyy-MM-dd) or
            // null. Set in the validation block once we confirm the code is
            // RC-RL; passed straight into the review write, NULL otherwise.
            string reloadBaselineIso = null;

            try
            {
                // 1) Resolve first-line DocumentID from the package itself.
                //    This single query both confirms the document is in
                //    this package AND returns the correct id. In POC view,
                //    we additionally constrain by the first-line PocEmail
                //    so a POC token cannot save documents outside their
                //    own scope.
                object flObj;
                if (isPocView)
                {
                    flObj = LPPIHelper.ExecuteScalar(@"
                        SELECT pd.DocumentID
                        FROM   tblLPPI_ReviewPackageDocuments pd
                        INNER JOIN tblLPPI_Documents d  ON d.DocumentID = pd.DocumentID
                        INNER JOIN tblLPPI_Documents d1 ON d1.DocNoAccounting = d.DocNoAccounting
                                                       AND d1.ItemSequence    = 1
                        WHERE  pd.PackageID     = @p
                        AND    d.DocNoAccounting = @dn
                        AND    LTRIM(RTRIM(d1.PocEmail)) = LTRIM(RTRIM(@poc))",
                        LPPIHelper.P("@p",   packageId),
                        LPPIHelper.P("@dn",  docNo),
                        LPPIHelper.P("@poc", pocEmail ?? ""));
                }
                else
                {
                    flObj = LPPIHelper.ExecuteScalar(@"
                        SELECT pd.DocumentID
                        FROM   tblLPPI_ReviewPackageDocuments pd
                        INNER JOIN tblLPPI_Documents d ON d.DocumentID = pd.DocumentID
                        WHERE  pd.PackageID     = @p
                        AND    d.DocNoAccounting = @dn",
                        LPPIHelper.P("@p",  packageId),
                        LPPIHelper.P("@dn", docNo));
                }

                if (flObj == null || flObj == DBNull.Value)
                {
                    rr.Ok = false;
                    rr.ErrorCode = "notInPackage";
                    rr.Error = isPocView
                        ? "Document is not in your assigned scope."
                        : "Document is not in this package.";
                    return rr;
                }
                int firstLineDocId = Convert.ToInt32(flObj);

                // 2) Parse the posted reason code into a nullable int.
                int? reasonId = null;
                if (reasonRaw.Length > 0)
                {
                    int v;
                    if (int.TryParse(reasonRaw, out v) && v > 0) reasonId = v;
                }

                // 3) Server-side validation — mirror the client rules.
                if (reasonId.HasValue)
                {
                    DataTable rcRow = LPPIHelper.ExecuteTable(
                        "SELECT Code, Outcome, RequiresComments FROM tblLPPI_ReasonCodes WHERE ReasonCodeID = @r",
                        LPPIHelper.P("@r", reasonId.Value));
                    if (rcRow.Rows.Count == 1)
                    {
                        string code     = Convert.ToString(rcRow.Rows[0]["Code"]);
                        string outcome  = Convert.ToString(rcRow.Rows[0]["Outcome"]);
                        bool   requires = Convert.ToBoolean(rcRow.Rows[0]["RequiresComments"]);
                        bool   notPay   = string.Equals(outcome, "NotPayable", StringComparison.OrdinalIgnoreCase);
                        bool   isReload = string.Equals(code, LPPIHelper.ReloadReasonCode, StringComparison.OrdinalIgnoreCase);

                        if (requires && comments.Length == 0)
                        {
                            rr.Ok = false; rr.ErrorCode = "validation";
                            rr.Error = "Comments are required for this reason code.";
                            return rr;
                        }
                        if (notPay && comments.Length == 0)
                        {
                            rr.Ok = false; rr.ErrorCode = "validation";
                            rr.Error = "Not-Payable requires both a Comment and an Objective Reference.";
                            return rr;
                        }
                        if (notPay && objref.Length == 0)
                        {
                            rr.Ok = false; rr.ErrorCode = "validation";
                            rr.Error = "Objective Reference is required when the outcome is Not Payable.";
                            return rr;
                        }

                        // RC-RL — the believed-correct baseline date is
                        // mandatory and must be a real date. Authoritative
                        // server-side gate behind the client modal.
                        if (isReload)
                        {
                            DateTime rbl;
                            if (reloadRaw.Length == 0
                                || !DateTime.TryParseExact(reloadRaw, "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out rbl))
                            {
                                rr.Ok = false; rr.ErrorCode = "validation";
                                rr.Error = "Reload-eligible (RC-RL) requires a believed baseline date.";
                                return rr;
                            }
                            reloadBaselineIso = rbl.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        }
                    }
                }

                // 4) Read current state of the review row. Used for both
                //    the optimistic-lock check and the no-change short-
                //    circuit.
                DataTable curT = LPPIHelper.ExecuteTable(@"
                    SELECT ReasonCodeID, Comments, ObjectiveReference, ReloadBaselineDate, ReviewedDate
                    FROM tblLPPI_Reviews
                    WHERE DocumentID = @d",
                    LPPIHelper.P("@d", firstLineDocId));

                bool reviewExists = curT.Rows.Count == 1;
                int?   curReasonId    = null;
                string curComments    = "";
                string curObjref      = "";
                string curReloadIso   = null;
                string curVersionIso  = "";

                if (reviewExists)
                {
                    DataRow cur = curT.Rows[0];
                    if (cur["ReasonCodeID"] != DBNull.Value) curReasonId = Convert.ToInt32(cur["ReasonCodeID"]);
                    curComments    = cur["Comments"]           == DBNull.Value ? "" : Convert.ToString(cur["Comments"]);
                    curObjref      = cur["ObjectiveReference"] == DBNull.Value ? "" : Convert.ToString(cur["ObjectiveReference"]);
                    curReloadIso   = cur["ReloadBaselineDate"] == DBNull.Value
                                        ? null
                                        : Convert.ToDateTime(cur["ReloadBaselineDate"]).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    curVersionIso  = FormatVersion(cur["ReviewedDate"]);
                }

                // 5) Optimistic-lock check.
                //    - If a review row exists, the loaded version must match
                //      the current ReviewedDate exactly. Mismatch = stale.
                //    - If no review row exists, the loaded version must be
                //      empty. A non-empty version means the client thought a
                //      review row existed, but someone else has since
                //      deleted it (not a normal flow, but treated as stale).
                if (reviewExists)
                {
                    if (!string.Equals(loadedVersion, curVersionIso, StringComparison.Ordinal))
                    {
                        FillStale(rr, curReasonId, curComments, curObjref, curVersionIso, firstLineDocId);
                        return rr;
                    }
                }
                else
                {
                    if (loadedVersion.Length != 0)
                    {
                        FillStale(rr, null, "", "", "", firstLineDocId);
                        return rr;
                    }
                }

                // 6) No-change short-circuit. If posted values exactly match
                //    the current stored values, do nothing — no UPDATE, no
                //    history insert. Return ok=true with errorCode noChange
                //    so the client can clear the dirty flag without a
                //    misleading "saved" flash.
                bool sameReason   = (reviewExists ? curReasonId : null) == reasonId
                                    || (curReasonId == null && !reasonId.HasValue);
                bool sameComments = string.Equals(curComments, comments, StringComparison.Ordinal);
                bool sameObjref   = string.Equals(curObjref,   objref,   StringComparison.Ordinal);
                bool sameReload   = string.Equals(curReloadIso ?? "", reloadBaselineIso ?? "", StringComparison.Ordinal);
                if (reviewExists && sameReason && sameComments && sameObjref && sameReload)
                {
                    rr.Ok = true;
                    rr.ErrorCode = "noChange";
                    rr.NewVersion = curVersionIso;
                    rr.NewReasonCodeId = curReasonId;
                    rr.NewReasonCode = LookupReasonCodeText(curReasonId);
                    rr.NewComments = curComments;
                    rr.NewObjectiveReference = curObjref;
                    rr.NewReviewedByName = LookupReviewedByName(firstLineDocId);
                    return rr;
                }

                // 7) Compute the new ReviewedDate so we can write it both
                //    into tblLPPI_Reviews and into the response. SQL Server
                //    SYSDATETIME() in two separate UPDATE/INSERT statements
                //    would produce two slightly different times; we want
                //    the version the response advertises to be exactly the
                //    one that was written. So we generate it client-side
                //    (here, in C#) and pass it into both statements as a
                //    parameter.
                DateTime nowUtc = DateTime.Now;   // server local time (matches SYSDATETIME convention)
                string newVerIso = nowUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

                string changedById   = LPPIHelper.CurrentUserId();
                string changedByName = LPPIHelper.CurrentUserDisplayName();

                // 8) Apply the change inside a transaction so a failure in
                //    the history insert rolls back the review write.
                using (var cn = new OleDbConnection(LPPIHelper.ConnectionString))
                {
                    cn.Open();
                    using (OleDbTransaction tx = cn.BeginTransaction())
                    {
                        try
                        {
                            if (reviewExists)
                            {
                                // Version-guarded UPDATE. If 0 rows affected,
                                // someone slipped in between our SELECT and
                                // UPDATE — treat as stale.
                                int affected = ExecNonQueryTx(cn, tx, @"
                                    UPDATE tblLPPI_Reviews
                                       SET ReasonCodeID       = @rc,
                                           Comments           = @cm,
                                           ObjectiveReference = @obj,
                                           ReloadBaselineDate = @rbl,
                                           ReviewedByUserId   = @uid,
                                           ReviewedByName     = @uname,
                                           ReviewedDate       = @nv
                                     WHERE DocumentID    = @d
                                       AND (ReviewedDate = @lv
                                            OR (ReviewedDate IS NULL AND @lv IS NULL));",
                                    LPPIHelper.P("@rc",    (object)reasonId ?? DBNull.Value),
                                    LPPIHelper.P("@cm",    comments),
                                    LPPIHelper.P("@obj",   objref),
                                    LPPIHelper.P("@rbl",   (object)reloadBaselineIso ?? DBNull.Value),
                                    LPPIHelper.P("@uid",   changedById),
                                    LPPIHelper.P("@uname", changedByName),
                                    LPPIHelper.P("@nv",    newVerIso),
                                    LPPIHelper.P("@d",     firstLineDocId),
                                    LPPIHelper.P("@lv",    string.IsNullOrEmpty(loadedVersion)
                                                              ? (object)DBNull.Value
                                                              : (object)loadedVersion));
                                if (affected == 0)
                                {
                                    tx.Rollback();
                                    // Re-read latest state for the response.
                                    DataTable lateT = LPPIHelper.ExecuteTable(@"
                                        SELECT ReasonCodeID, Comments, ObjectiveReference, ReviewedDate
                                        FROM tblLPPI_Reviews
                                        WHERE DocumentID = @d",
                                        LPPIHelper.P("@d", firstLineDocId));
                                    if (lateT.Rows.Count == 1)
                                    {
                                        DataRow lr = lateT.Rows[0];
                                        int?   lateRc = lr["ReasonCodeID"] == DBNull.Value
                                                            ? (int?)null
                                                            : Convert.ToInt32(lr["ReasonCodeID"]);
                                        string lateCm = lr["Comments"]           == DBNull.Value ? "" : Convert.ToString(lr["Comments"]);
                                        string lateOb = lr["ObjectiveReference"] == DBNull.Value ? "" : Convert.ToString(lr["ObjectiveReference"]);
                                        FillStale(rr, lateRc, lateCm, lateOb, FormatVersion(lr["ReviewedDate"]), firstLineDocId);
                                    }
                                    else
                                    {
                                        FillStale(rr, null, "", "", "", firstLineDocId);
                                    }
                                    return rr;
                                }
                            }
                            else
                            {
                                // INSERT path — first review for this doc.
                                // The unique constraint on DocumentID will
                                // catch a concurrent insert; we translate
                                // that into a stale result.
                                try
                                {
                                    ExecNonQueryTx(cn, tx, @"
                                        INSERT INTO tblLPPI_Reviews
                                            (DocumentID, ReasonCodeID, Comments, ObjectiveReference,
                                             ReloadBaselineDate, ReviewedByUserId, ReviewedByName,
                                             ReviewedDate, IsFinal)
                                        VALUES
                                            (@d, @rc, @cm, @obj, @rbl, @uid, @uname, @nv, 0);",
                                        LPPIHelper.P("@d",     firstLineDocId),
                                        LPPIHelper.P("@rc",    (object)reasonId ?? DBNull.Value),
                                        LPPIHelper.P("@cm",    comments),
                                        LPPIHelper.P("@obj",   objref),
                                        LPPIHelper.P("@rbl",   (object)reloadBaselineIso ?? DBNull.Value),
                                        LPPIHelper.P("@uid",   changedById),
                                        LPPIHelper.P("@uname", changedByName),
                                        LPPIHelper.P("@nv",    newVerIso));
                                }
                                catch (OleDbException ox)
                                {
                                    // Unique key violation = concurrent insert by another session.
                                    // SQL Server unique-key violation is error 2627; primary key 2627.
                                    // OLE DB surfaces these as ErrorCode -2147467259 (E_FAIL) typically;
                                    // safer to inspect the message.
                                    if (ox.Message != null
                                        && (ox.Message.IndexOf("UQ_tblLPPI_Reviews_DocumentID", StringComparison.OrdinalIgnoreCase) >= 0
                                            || ox.Message.IndexOf("duplicate key", StringComparison.OrdinalIgnoreCase) >= 0
                                            || ox.Message.IndexOf("violation of UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        tx.Rollback();
                                        FillStale(rr, null, "", "", "", firstLineDocId);
                                        return rr;
                                    }
                                    throw;
                                }
                            }

                            // 9) Append the history snapshot.
                            ExecNonQueryTx(cn, tx, @"
                                INSERT INTO tblLPPI_ReviewHistory
                                    (DocumentID, PackageID, ReasonCodeID, Comments,
                                     ObjectiveReference, ReloadBaselineDate, ChangedByUserId,
                                     ChangedByName, ChangedDate)
                                VALUES (@d, @p, @rc, @cm, @obj, @rbl, @uid, @uname, @nv);",
                                LPPIHelper.P("@d",     firstLineDocId),
                                LPPIHelper.P("@p",     packageId),
                                LPPIHelper.P("@rc",    (object)reasonId ?? DBNull.Value),
                                LPPIHelper.P("@cm",    comments),
                                LPPIHelper.P("@obj",   objref),
                                LPPIHelper.P("@rbl",   (object)reloadBaselineIso ?? DBNull.Value),
                                LPPIHelper.P("@uid",   changedById),
                                LPPIHelper.P("@uname", changedByName),
                                LPPIHelper.P("@nv",    newVerIso));

                            tx.Commit();
                        }
                        catch
                        {
                            try { tx.Rollback(); } catch { /* swallow */ }
                            throw;
                        }
                    }
                }

                // Success.
                rr.Ok = true;
                rr.ErrorCode = "ok";
                rr.NewVersion = newVerIso;
                rr.NewReasonCodeId = reasonId;
                rr.NewReasonCode = LookupReasonCodeText(reasonId);
                rr.NewComments = comments;
                rr.NewObjectiveReference = objref;
                rr.NewReviewedByName = changedByName;
                return rr;
            }
            catch (Exception ex)
            {
                rr.Ok = false;
                rr.ErrorCode = "server";
                rr.Error = "Server error: " + ex.Message;
                return rr;
            }
        }

        private static void FillStale(RowResult rr, int? curReasonId, string curComments,
                                      string curObjref, string curVersion, int firstLineDocId)
        {
            rr.Ok = false;
            rr.ErrorCode = "stale";
            rr.Error = "This document has been updated by someone else since you opened the page. Reload to see the latest values.";
            rr.NewReasonCodeId = curReasonId;
            rr.NewReasonCode = LookupReasonCodeText(curReasonId);
            rr.NewComments = curComments ?? "";
            rr.NewObjectiveReference = curObjref ?? "";
            rr.NewVersion = curVersion ?? "";
            rr.NewReviewedByName = LookupReviewedByName(firstLineDocId);
        }

        private static string LookupReasonCodeText(int? reasonId)
        {
            if (!reasonId.HasValue) return "";
            object o = LPPIHelper.ExecuteScalar(
                "SELECT Code FROM tblLPPI_ReasonCodes WHERE ReasonCodeID = @r",
                LPPIHelper.P("@r", reasonId.Value));
            return o == null ? "" : Convert.ToString(o);
        }

        private static string LookupReviewedByName(int firstLineDocId)
        {
            object o = LPPIHelper.ExecuteScalar(
                "SELECT ReviewedByName FROM tblLPPI_Reviews WHERE DocumentID = @d",
                LPPIHelper.P("@d", firstLineDocId));
            return o == null ? "" : Convert.ToString(o);
        }

        private static string FormatVersion(object reviewedDate)
        {
            if (reviewedDate == null || reviewedDate == DBNull.Value) return "";
            DateTime dt;
            if (reviewedDate is DateTime) dt = (DateTime)reviewedDate;
            else if (!DateTime.TryParse(Convert.ToString(reviewedDate),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return "";
            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        // -------------------------------------------------------------------
        // Tx helper — same @-name -> ? rewrite as LPPIHelper.BuildCommand,
        // but bound to an external connection + transaction so several
        // statements can share a transaction.
        // -------------------------------------------------------------------
        private static int ExecNonQueryTx(OleDbConnection cn, OleDbTransaction tx,
                                          string sql, params OleDbParameter[] parameters)
        {
            var byName = new Dictionary<string, OleDbParameter>(StringComparer.OrdinalIgnoreCase);
            if (parameters != null)
            {
                foreach (var p in parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.ParameterName)) continue;
                    byName[p.ParameterName] = p;
                }
            }

            var rewritten = new StringBuilder(sql.Length);
            var ordered   = new List<OleDbParameter>();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                if (c == '\'')
                {
                    int end = i + 1;
                    while (end < sql.Length)
                    {
                        if (sql[end] == '\'')
                        {
                            if (end + 1 < sql.Length && sql[end + 1] == '\'') { end += 2; continue; }
                            end++;
                            break;
                        }
                        end++;
                    }
                    rewritten.Append(sql, i, end - i);
                    i = end;
                    continue;
                }

                if (c == '@' && i + 1 < sql.Length && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
                {
                    int j = i + 1;
                    while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) j++;
                    string name = sql.Substring(i, j - i);

                    OleDbParameter src;
                    if (!byName.TryGetValue(name, out src))
                        throw new InvalidOperationException(
                            "LPPI_Review_Save: SQL references parameter " + name + " but no value was supplied.");

                    var clone = new OleDbParameter();
                    clone.ParameterName = "?";
                    clone.OleDbType     = src.OleDbType;
                    clone.Size          = src.Size;
                    clone.Precision     = src.Precision;
                    clone.Scale         = src.Scale;
                    clone.Value         = src.Value ?? DBNull.Value;
                    ordered.Add(clone);
                    rewritten.Append('?');
                    i = j;
                    continue;
                }

                rewritten.Append(c);
                i++;
            }

            using (var cmd = new OleDbCommand(rewritten.ToString(), cn, tx))
            {
                cmd.CommandType = CommandType.Text;
                foreach (var p in ordered) cmd.Parameters.Add(p);
                return cmd.ExecuteNonQuery();
            }
        }

        // -------------------------------------------------------------------
        // JSON output — minimal, hand-rolled (matches the rest of the
        // codebase, which avoids JSON dependencies).
        // -------------------------------------------------------------------
        private static void WriteTopLevel(HttpContext ctx, bool ok, string err, string status, List<RowResult> results)
        {
            WriteResults(ctx, ok, err, status, results ?? new List<RowResult>());
        }

        private static void WriteResults(HttpContext ctx, bool ok, string topErr, string status, List<RowResult> results)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            if (!string.IsNullOrEmpty(topErr))
                sb.Append(",\"error\":").Append(JsonStr(topErr));
            if (!string.IsNullOrEmpty(status))
                sb.Append(",\"packageStatus\":").Append(JsonStr(status));
            sb.Append(",\"results\":[");
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendRow(sb, results[i]);
                }
            }
            sb.Append("]}");
            ctx.Response.Write(sb.ToString());
        }

        private static void AppendRow(StringBuilder sb, RowResult r)
        {
            sb.Append('{');
            sb.Append("\"docNo\":").Append(JsonStr(r.DocNo));
            sb.Append(",\"ok\":").Append(r.Ok ? "true" : "false");
            sb.Append(",\"errorCode\":").Append(JsonStr(r.ErrorCode ?? ""));
            if (!string.IsNullOrEmpty(r.Error))
                sb.Append(",\"error\":").Append(JsonStr(r.Error));
            sb.Append(",\"newVersion\":").Append(JsonStr(r.NewVersion ?? ""));
            sb.Append(",\"newReasonCodeId\":");
            if (r.NewReasonCodeId.HasValue)
                sb.Append(r.NewReasonCodeId.Value.ToString(CultureInfo.InvariantCulture));
            else
                sb.Append("null");
            sb.Append(",\"newReasonCode\":").Append(JsonStr(r.NewReasonCode ?? ""));
            sb.Append(",\"newComments\":").Append(JsonStr(r.NewComments ?? ""));
            sb.Append(",\"newObjectiveReference\":").Append(JsonStr(r.NewObjectiveReference ?? ""));
            sb.Append(",\"newReviewedByName\":").Append(JsonStr(r.NewReviewedByName ?? ""));
            sb.Append('}');
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 4);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Per-row response object.
        // -------------------------------------------------------------------
        private class RowResult
        {
            public string DocNo;
            public bool   Ok;
            public string ErrorCode;
            public string Error;
            public string NewVersion;
            public int?   NewReasonCodeId;
            public string NewReasonCode;
            public string NewComments;
            public string NewObjectiveReference;
            public string NewReviewedByName;
        }
    }
}
