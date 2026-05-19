using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;

namespace CPlatform.LPPI
{
    /// <summary>
    /// Reviewer page. Token-authenticated; opts out of admin gate.
    ///
    /// May 2026 — POC token support
    /// -------------------------------------------------------------------
    /// The page now accepts TWO token types via ?t=&lt;token&gt;:
    ///
    ///   - AS Fin token (from tblLPPI_ReviewPackages.Token) — full package
    ///     view. Can save, can finalise, can unfinalise.
    ///
    ///   - POC token (from tblLPPI_PackagePocs.Token) — POC-scoped view.
    ///     Filters every visible row to documents where d.PocEmail matches
    ///     the POC's email. Can save (the save handler enforces the same
    ///     scope guard server-side), but CANNOT finalise or unfinalise —
    ///     those actions belong to AS Fin.
    ///
    /// LPPIHelper.ResolveReviewToken inspects both tables and returns a
    /// typed result. From the user's perspective the URL works the same
    /// regardless of token kind; the page reshapes itself based on which
    /// kind of token resolved.
    ///
    /// IIS Windows Authentication is still active so HttpContext carries
    /// the SSO identity, recorded as ChangedByName / FinalisedBy in the
    /// audit tables.
    ///
    /// Note on roles for AS Fin tokens: there is no separate "reviewer"
    /// vs "AS Fin" role. The token-holder population for the AS Fin link
    /// IS the AS Fin team responsible for that CM program. They review,
    /// finalise and unfinalise self-service. The admin-only checkpoint
    /// is the ERP export.
    /// </summary>
    public partial class LPPI_Review : LPPIBasePage
    {
        protected override bool RequiresAdminAccess { get { return false; } }

        protected string TokenForClient = "";
        protected string ProgramName    = "";
        protected DateTime DueDate;
        protected int ReviewedCount;
        protected int TotalCount;
        protected int ProgressPercent
        {
            get { return TotalCount == 0 ? 0 : (int)Math.Round(100.0 * ReviewedCount / TotalCount); }
        }
        protected string DueCountdownText;
        protected string DueCssClass;

        // Read-only mode. Finalised, Exported and Cancelled all render
        // read-only ON THE FORM FIELDS, but Finalised is editable in the
        // sense that the user can click Unfinalise to return it to InReview.
        // The IsReadOnly flag drives the form-field disable hooks; the
        // toolbar action button is rendered separately based on status.
        protected bool IsReadOnly;
        protected string StatusBannerHtml = "";
        protected string CurrentStatus    = "";

        // POC-view flag. True when the resolved token is a POC token. The
        // markup uses this to:
        //   - render the "POC view" banner above the review header
        //   - hide the Finalise / Unfinalise action button (POCs cannot
        //     finalise or unfinalise; that is AS Fin's responsibility)
        //
        // Document-list, exposure and detail queries are all filtered by
        // PocEmail when this flag is set.
        protected bool   IsPocView;
        protected string PocEmail = "";
        protected string PocBannerHtml = "";

        // Toolbar action button gating. There is one button slot in the
        // toolbar; its label and colour depend on status.
        //
        //   AS Fin token, editable (NotSent / Sent / InReview) -> green "Finalise"
        //   AS Fin token, Finalised                            -> orange "Unfinalise"
        //   AS Fin token, Exported / Cancelled                 -> no button (terminal)
        //   POC token, any status                              -> no button
        //
        // The empty-package case (TotalCount == 0) suppresses the button
        // in all states — there is nothing to finalise.
        protected bool ShowActionButton;
        protected bool IsFinalised;

        // "Ready to finalise" hint banner — shown when every doc has been
        // coded but the package is still in flight (NotSent/Sent/InReview).
        // AS Fin only — POCs get IsPocAllReviewed instead.
        protected bool IsAllReviewed;

        // POC "all done" banner — POC equivalent of IsAllReviewed. Same
        // banner slot, different copy: confirms completion without
        // referencing a Finalise button the POC does not have.
        protected bool IsPocAllReviewed;

        // Exposure (dollar) figures — scoped to this package only (or to
        // the POC's subset in POC view). Driven into the head-row
        // "Exposure" cell. The three component values sum to the total.
        protected string ExposureTotalText            = "0.00";
        protected string ExposurePayableTextShort     = "0";
        protected string ExposureNotPayableTextShort  = "0";
        protected string ExposureAwaitingTextShort    = "0";
        protected int    ExposurePayablePct;
        protected int    ExposureNotPayablePct;
        protected int    ExposureAwaitingPct;

        private DataTable _reasonCodes;
        private DataTable _mainTable;

        protected void Page_Load(object sender, EventArgs e)
        {
            string token = (Request.QueryString["t"] ?? "").Trim();
            if (token.Length == 0) { ShowError(); return; }

            // Resolve the token — could be an AS Fin (package-level) token
            // or a POC (POC-scoped) token. ResolveReviewToken queries both
            // tables and returns a kind discriminator.
            LPPIHelper.ReviewTokenInfo tokenInfo = LPPIHelper.ResolveReviewToken(token);
            if (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.None) { ShowError(); return; }

            IsPocView = (tokenInfo.Kind == LPPIHelper.ReviewTokenKind.Poc);
            PocEmail  = IsPocView ? (tokenInfo.PocEmail ?? "") : "";
            int packageId = tokenInfo.PackageID;

            // Fetch package metadata (status, due date, program, etc.) —
            // shared between both token kinds.
            DataTable pkg = LPPIHelper.ExecuteTable(@"
                SELECT p.PackageID, p.CmID, p.DueDate, p.Status,
                       p.FinalisedDate, p.FinalisedBy,
                       cm.Program
                FROM tblLPPI_ReviewPackages p
                INNER JOIN tblLPPI_CapabilityManagers cm ON cm.CmID = p.CmID
                WHERE p.PackageID = @p",
                LPPIHelper.P("@p", packageId));

            if (pkg.Rows.Count != 1) { ShowError(); return; }

            DataRow pr     = pkg.Rows[0];
            string  status = Convert.ToString(pr["Status"]);
            CurrentStatus  = status;

            // Form-field read-only when Finalised / Exported / Cancelled.
            // Even though Finalised is reversible (via Unfinalise), the
            // form fields stay locked — to edit data you must Unfinalise
            // first. This keeps the audit trail clean: every save happens
            // against an editable status.
            bool readOnly =
                string.Equals(status, LPPIHelper.StatusFinalised, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, LPPIHelper.StatusExported,  StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, LPPIHelper.StatusCancelled, StringComparison.OrdinalIgnoreCase);
            IsReadOnly = readOnly;
            IsFinalised = string.Equals(status, LPPIHelper.StatusFinalised, StringComparison.OrdinalIgnoreCase);

            // Build the banner. For Finalised, include the FinalisedBy / Date
            // so the user can see who closed it off and when.
            string finalisedBy   = pr["FinalisedBy"]   == DBNull.Value ? "" : Convert.ToString(pr["FinalisedBy"]);
            DateTime? finalisedAt = pr["FinalisedDate"] == DBNull.Value
                ? (DateTime?)null
                : Convert.ToDateTime(pr["FinalisedDate"]);
            StatusBannerHtml = BuildStatusBanner(status, finalisedBy, finalisedAt);

            // POC-view banner — rendered above the review header so the
            // person knows immediately that they are looking at their own
            // scoped subset, not the whole package.
            if (IsPocView)
                PocBannerHtml = BuildPocBanner(PocEmail);

            TokenForClient = token;
            ProgramName    = Convert.ToString(pr["Program"]);
            DueDate        = Convert.ToDateTime(pr["DueDate"]);
            SetDueCountdown();

            phReview.Visible = true;
            phError.Visible  = false;

            _reasonCodes = LPPIHelper.GetReasonCodes(activeOnly: true);
            LoadDocuments(packageId);
            LoadExposure(packageId);

            // Toolbar action-button gating, computed after LoadDocuments so
            // we have ReviewedCount / TotalCount available.
            //
            //   - AS Fin token, editable states show Finalise.
            //   - AS Fin token, Finalised shows Unfinalise.
            //   - AS Fin token, Exported / Cancelled show no button.
            //   - POC token, any status shows no button.
            //   - Empty package suppresses the button entirely.
            //
            // POC view never shows the action button — finalise/unfinalise
            // are AS Fin's call. The save handler enforces this server-side
            // too; this is just the UX layer.
            bool editable =
                string.Equals(status, LPPIHelper.StatusNotSent,  StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, LPPIHelper.StatusSent,     StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, LPPIHelper.StatusInReview, StringComparison.OrdinalIgnoreCase);
            ShowActionButton = !IsPocView && (editable || IsFinalised) && TotalCount > 0;
            IsAllReviewed    = !IsPocView && editable && TotalCount > 0 && ReviewedCount >= TotalCount;
            // POC equivalent of the "ready" state: every doc assigned to the
            // POC is coded, but the POC has no Finalise button (and must not
            // be told to click one — that is AS Fin's job).
            IsPocAllReviewed = IsPocView && editable && TotalCount > 0 && ReviewedCount >= TotalCount;
        }

        /// <summary>
        /// Builds the status banner shown above the review header.
        ///   NotSent   — informational; editable but not yet sent.
        ///   Sent      — no banner; the page looks "normal".
        ///   InReview  — no banner; same as Sent.
        ///   Finalised — banner naming who finalised and when. The banner
        ///               text does not need to mention Unfinalise — the
        ///               toolbar button takes care of that.
        ///   Exported  — locked, in an ERP file. Terminal.
        ///   Cancelled — read-only; documents may be re-bundled. Terminal.
        /// </summary>
        private static string BuildStatusBanner(string status, string finalisedBy, DateTime? finalisedAt)
        {
            string title;
            string body;
            string kind;

            switch ((status ?? "").ToLowerInvariant())
            {
                case "notsent":
                    kind  = "info";
                    title = "Not yet sent";
                    body  = "This package has not been emailed to recipients yet. You can edit reason codes here for testing or QA — your changes will be visible to the reviewer once the package is issued from Send-outs.";
                    break;

                case "finalised":
                    kind  = "ok";
                    var who  = string.IsNullOrEmpty(finalisedBy) ? "AS Fin" : finalisedBy;
                    var when = finalisedAt.HasValue
                        ? finalisedAt.Value.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-AU"))
                        : "";
                    title = "Finalised";
                    if (when.Length > 0)
                        body = "Finalised by " + who + " on " + when + ". The form fields are locked. Click Unfinalise above to reopen this package for further edits.";
                    else
                        body = "Finalised by " + who + ". The form fields are locked. Click Unfinalise above to reopen this package for further edits.";
                    break;

                case "exported":
                    kind  = "ok";
                    title = "Exported";
                    body  = "This package has been included in an ERP payment file and is locked. No further changes are possible.";
                    break;

                case "cancelled":
                    kind  = "warn";
                    title = "Cancelled";
                    body  = "This package has been cancelled. It is read-only and the documents are eligible for repackaging on the next file load.";
                    break;

                case "sent":
                case "inreview":
                    return "";

                default:
                    kind  = "warn";
                    title = "Unknown status";
                    body  = "This package is in an unrecognised state.";
                    break;
            }

            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-").Append(kind).Append("\" style=\"margin:0 0 16px 0;\">")
              .Append("<div><strong>").Append(LPPIHelper.Enc(title)).Append("</strong> &mdash; ")
              .Append(LPPIHelper.Enc(body))
              .Append("</div></div>");
            return sb.ToString();
        }

        /// <summary>
        /// Banner displayed above the review header in POC view. Tells the
        /// person they are looking at their own subset of the package, not
        /// the full thing.
        /// </summary>
        private static string BuildPocBanner(string pocEmail)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"alert alert-info\" style=\"margin:0 0 16px 0;\">")
              .Append("<div><strong>POC view</strong> &mdash; ")
              .Append("Showing only documents assigned to ")
              .Append(LPPIHelper.Enc(pocEmail))
              .Append(". Reason codes recorded here apply to the same documents in the AS Fin view. ")
              .Append("Finalising the package is AS Fin's responsibility.")
              .Append("</div></div>");
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Exposure totals — scoped to this package
        //
        // Mirrors LPPIHelper.GetExposureSummary's logic but constrained to the
        // package via tblLPPI_ReviewPackageDocuments. Per-document totals are
        // computed from every line of each DocNoAccounting (BODS multi-line),
        // then classified by the first-line review's outcome.
        //
        // POC view: documents are further filtered to those whose first-line
        // record's PocEmail matches the POC's address. The dollar totals
        // therefore reconcile against the POC's filtered document list, not
        // the full package.
        // -------------------------------------------------------------------
        private void LoadExposure(int packageId)
        {
            string sql;
            if (IsPocView)
            {
                sql = @"
WITH PkgDocs AS (
    SELECT pd.DocumentID AS FirstLineDocumentID,
           dx.DocNoAccounting,
           dx.PocEmail
      FROM tblLPPI_ReviewPackageDocuments pd
      INNER JOIN tblLPPI_Documents dx ON dx.DocumentID = pd.DocumentID
     WHERE pd.PackageID = @p
       AND LTRIM(RTRIM(dx.PocEmail)) = LTRIM(RTRIM(@poc))
),
DocTotals AS (
    SELECT pkd.FirstLineDocumentID,
           SUM(d.InterestPayable) AS DocInterest
      FROM PkgDocs pkd
      INNER JOIN tblLPPI_Documents d
              ON d.DocNoAccounting = pkd.DocNoAccounting
             AND d.IsDeactivated   = 0
     GROUP BY pkd.FirstLineDocumentID
)
SELECT
    ISNULL(SUM(dt.DocInterest), 0)                                                        AS TotalExposure,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'Payable'    THEN dt.DocInterest ELSE 0 END), 0) AS PayableExposure,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'NotPayable' THEN dt.DocInterest ELSE 0 END), 0) AS NotPayableExposure,
    ISNULL(SUM(CASE WHEN rc.ReasonCodeID IS NULL   THEN dt.DocInterest ELSE 0 END), 0) AS AwaitingExposure
FROM DocTotals dt
LEFT JOIN tblLPPI_Reviews r       ON r.DocumentID    = dt.FirstLineDocumentID
LEFT JOIN tblLPPI_ReasonCodes rc  ON rc.ReasonCodeID = r.ReasonCodeID;";
            }
            else
            {
                sql = @"
WITH PkgDocs AS (
    SELECT pd.DocumentID AS FirstLineDocumentID,
           (SELECT MIN(d2.DocumentID)
              FROM tblLPPI_Documents d2
             WHERE d2.DocNoAccounting = (SELECT d3.DocNoAccounting
                                           FROM tblLPPI_Documents d3
                                          WHERE d3.DocumentID = pd.DocumentID)
               AND d2.IsDeactivated   = 0) AS NormalisedFirstLine,
           (SELECT d4.DocNoAccounting
              FROM tblLPPI_Documents d4
             WHERE d4.DocumentID = pd.DocumentID) AS DocNoAccounting
      FROM tblLPPI_ReviewPackageDocuments pd
     WHERE pd.PackageID = @p
),
DocTotals AS (
    SELECT pkd.FirstLineDocumentID,
           SUM(d.InterestPayable) AS DocInterest
      FROM PkgDocs pkd
      INNER JOIN tblLPPI_Documents d
              ON d.DocNoAccounting = pkd.DocNoAccounting
             AND d.IsDeactivated   = 0
     GROUP BY pkd.FirstLineDocumentID
)
SELECT
    ISNULL(SUM(dt.DocInterest), 0)                                                        AS TotalExposure,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'Payable'    THEN dt.DocInterest ELSE 0 END), 0) AS PayableExposure,
    ISNULL(SUM(CASE WHEN rc.Outcome = 'NotPayable' THEN dt.DocInterest ELSE 0 END), 0) AS NotPayableExposure,
    ISNULL(SUM(CASE WHEN rc.ReasonCodeID IS NULL   THEN dt.DocInterest ELSE 0 END), 0) AS AwaitingExposure
FROM DocTotals dt
LEFT JOIN tblLPPI_Reviews r       ON r.DocumentID    = dt.FirstLineDocumentID
LEFT JOIN tblLPPI_ReasonCodes rc  ON rc.ReasonCodeID = r.ReasonCodeID;";
            }

            DataTable dt;
            if (IsPocView)
                dt = LPPIHelper.ExecuteTable(sql,
                    LPPIHelper.P("@p",   packageId),
                    LPPIHelper.P("@poc", PocEmail));
            else
                dt = LPPIHelper.ExecuteTable(sql, LPPIHelper.P("@p", packageId));

            if (dt.Rows.Count == 0) return;

            DataRow r = dt.Rows[0];
            decimal total      = AsDecimal(r, "TotalExposure");
            decimal payable    = AsDecimal(r, "PayableExposure");
            decimal notPayable = AsDecimal(r, "NotPayableExposure");
            decimal awaiting   = AsDecimal(r, "AwaitingExposure");

            ExposureTotalText           = total.ToString("N2", CultureInfo.GetCultureInfo("en-AU"));
            ExposurePayableTextShort    = FormatShortMoney(payable);
            ExposureNotPayableTextShort = FormatShortMoney(notPayable);
            ExposureAwaitingTextShort   = FormatShortMoney(awaiting);

            ExposurePayablePct    = SharePct(payable,    total);
            ExposureNotPayablePct = SharePct(notPayable, total);
            ExposureAwaitingPct   = SharePct(awaiting,   total);
        }

        /// <summary>
        /// Compact money string for the legend (e.g. $98k, $1.2M, $345). Total
        /// stays full-precision; only the per-segment legend uses this.
        /// Negative values shouldn't occur for LPPI but handled defensively.
        /// </summary>
        private static string FormatShortMoney(decimal value)
        {
            if (value == 0m) return "0";

            decimal abs = Math.Abs(value);
            string sign = value < 0m ? "-" : "";

            if (abs >= 1000000m)
                return sign + (abs / 1000000m).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (abs >= 1000m)
                return sign + (abs / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return sign + abs.ToString("0", CultureInfo.InvariantCulture);
        }

        private static decimal AsDecimal(DataRow row, string column)
        {
            if (row == null || row[column] == DBNull.Value) return 0m;
            return Convert.ToDecimal(row[column]);
        }

        private static int SharePct(decimal part, decimal whole)
        {
            if (whole <= 0m) return 0;
            decimal pct = (part / whole) * 100m;
            int rounded = (int)Math.Round(pct, MidpointRounding.AwayFromZero);
            if (rounded < 0)   rounded = 0;
            if (rounded > 100) rounded = 100;
            return rounded;
        }

        // -------------------------------------------------------------------
        // MAIN VIEW QUERY — one row per DocNoAccounting.
        //
        // Account-assignment fields are sourced exclusively from the
        // ItemSequence = 1 line (d1 lateral join), not MIN/MAX across all
        // lines. This matches the BODS extract convention where line 1 carries
        // the primary account assignment for the document.
        //
        // POC view: an extra WHERE clause filters d1.PocEmail to the POC's
        // address. d1 is the first-line record for each document, so this
        // filters at document granularity even though tblLPPI_Documents has
        // one row per line.
        //
        // Capability Manager fields (CapabilityManager number + Name) are
        // projected so the reviewer page can show the LPPI Charge Cost Centre
        // column.
        //
        // The review MERGE target is pd.DocumentID, which the reconcile
        // step writes as the package-time first-line DocumentID. The save
        // handler resolves this via the package table directly.
        //
        // r.ReviewedDate is projected as ReviewedVersion. The aspx writes it
        // onto each row as a data-version attribute. The save handler's
        // optimistic-lock check compares this against the current value at
        // save time — if they differ, the save is rejected as stale.
        //
        // Eval() bindings in LPPI_Review.aspx must match these aliases exactly.
        // -------------------------------------------------------------------
        private void LoadDocuments(int packageId)
        {
            string mainSql = @"
                SELECT
                    pd.DocumentID                           AS FirstLineDocumentID,
                    d.DocNoAccounting,
                    COUNT(*)                                AS LineCount,
                    SUM(d.InterestPayable)                  AS TotalInterest,

                    -- Uniform fields (same on every line by design)
                    MIN(d.CompanyCode)                      AS CompanyCode,
                    MIN(d.VendorNum)                        AS VendorNum,
                    MIN(d.VendorName)                       AS VendorName,
                    MIN(d.PoNumber)                         AS PoNumber,
                    MIN(d.VendorInvoiceNo)                  AS VendorInvoiceNo,
                    MIN(d.ClearingMonth)                    AS ClearingMonth,
                    MIN(d.FiscalYear)                       AS FiscalYear,
                    MIN(d.PaymentRunDate)                   AS PaymentRunDate,
                    MIN(d.DaysVariance)                     AS DaysVariance,

                    -- Account assignment from ItemSequence = 1 line only
                    d1.WbsElement,
                    d1.WbsDesc,
                    d1.GlAccount,
                    d1.ProfitCentre,
                    d1.TaxCode,
                    d1.CapabilityManager,
                    d1.CapabilityManagerName,
                    d1.CapabilityManagerProgram,
                    d1.DeliveryManager,
                    d1.DeliveryManagerName,
                    d1.DeliveryManagerProgram,
                    d1.PocEmail,

                    -- Review fields (joined to pd.DocumentID = package first-line id)
                    r.ReasonCodeID                          AS SelectedReasonCodeID,
                    r.Comments,
                    r.ObjectiveReference,
                    r.ReviewedDate                          AS ReviewedVersion,
                    rc.Code                                 AS ReasonCode,
                    rc.Outcome                              AS ReasonOutcome,
                    ISNULL(rc.RequiresComments, 0)          AS RequiresComments

                FROM tblLPPI_ReviewPackageDocuments pd

                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                       AND d.IsDeactivated  = 0

                INNER JOIN tblLPPI_Documents d1
                        ON d1.DocNoAccounting = d.DocNoAccounting
                       AND d1.ItemSequence    = 1
                       AND d1.IsDeactivated   = 0

                LEFT  JOIN tblLPPI_Reviews r
                        ON r.DocumentID = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc
                        ON rc.ReasonCodeID = r.ReasonCodeID

                WHERE pd.PackageID = @p"
                + (IsPocView ? "  AND LTRIM(RTRIM(d1.PocEmail)) = LTRIM(RTRIM(@poc))" : "")
                + @"

                GROUP BY
                    pd.DocumentID, d.DocNoAccounting,
                    d1.WbsElement, d1.WbsDesc, d1.GlAccount, d1.ProfitCentre, d1.TaxCode,
                    d1.CapabilityManager, d1.CapabilityManagerName, d1.CapabilityManagerProgram,
                    d1.DeliveryManager, d1.DeliveryManagerName, d1.DeliveryManagerProgram,
                    d1.PocEmail,
                    r.ReasonCodeID, r.Comments, r.ObjectiveReference, r.ReviewedDate,
                    rc.Code, rc.Outcome, rc.RequiresComments

                ORDER BY SUM(d.InterestPayable) DESC";

            DataTable main;
            if (IsPocView)
                main = LPPIHelper.ExecuteTable(mainSql,
                    LPPIHelper.P("@p",   packageId),
                    LPPIHelper.P("@poc", PocEmail));
            else
                main = LPPIHelper.ExecuteTable(mainSql, LPPIHelper.P("@p", packageId));

            if (!main.Columns.Contains("SearchBlob"))
                main.Columns.Add("SearchBlob", typeof(string));

            foreach (DataRow r in main.Rows)
            {
                r["SearchBlob"] = string.Join(" ", new[]
                {
                    Convert.ToString(r["VendorName"]),
                    Convert.ToString(r["DocNoAccounting"]),
                    Convert.ToString(r["PoNumber"]),
                    NullOrString(r["WbsElement"]),
                    NullOrString(r["WbsDesc"]),
                    NullOrString(r["CapabilityManager"]),
                    NullOrString(r["CapabilityManagerName"]),
                    NullOrString(r["DeliveryManager"]),
                    NullOrString(r["DeliveryManagerProgram"]),
                    NullOrString(r["DeliveryManagerName"]),
                    NullOrString(r["PocEmail"])
                });
            }

            TotalCount    = main.Rows.Count;
            ReviewedCount = 0;
            foreach (DataRow r in main.Rows)
                if (r["SelectedReasonCodeID"] != DBNull.Value) ReviewedCount++;

            _mainTable = main;

            rptMain.DataSource = main;
            rptMain.DataBind();
            phEmpty.Visible = TotalCount == 0;

            // ------------------------------------------------------------------
            // DETAIL VIEW QUERY — one row per line, read-only.
            //
            // Same POC filter applies: in POC view, only documents whose
            // PocEmail matches the POC. The filter applies at document
            // granularity — every line of an in-scope document is shown,
            // because that is consistent with how the document-level review
            // applies to all its lines.
            //
            // FiscalYear projected for the SapFiNumberHtml deep link.
            // CapabilityManager / Name / Program projected so the All Lines tab
            // and the per-row expand panel can show the LPPI Charge Cost Centre.
            // ------------------------------------------------------------------
            string detailSql = @"
                SELECT
                    d.DocumentID,
                    d.DocNoAccounting,
                    d.ItemSequence,
                    pd.DocumentID                       AS FirstLineDocumentID,
                    d.CompanyCode,
                    d.VendorName,
                    d.VendorNum,
                    d.PoNumber,
                    d.ClearingMonth,
                    d.FiscalYear,
                    d.WbsElement,
                    d.WbsDesc,
                    d.GlAccount,
                    d.ProfitCentre,
                    d.TaxCode,
                    d.CapabilityManager,
                    d.CapabilityManagerName,
                    d.CapabilityManagerProgram,
                    d.DeliveryManager,
                    d.DeliveryManagerName,
                    d.DeliveryManagerProgram,
                    d.PocEmail,
                    d.PaymentRunDate,
                    d.DaysVariance,
                    d.InterestPayable,
                    r.Comments,
                    r.ObjectiveReference,
                    rc.Code                             AS ReasonCode
                FROM tblLPPI_ReviewPackageDocuments pd
                INNER JOIN tblLPPI_Documents d
                        ON d.DocNoAccounting = (SELECT d2.DocNoAccounting
                                                  FROM tblLPPI_Documents d2
                                                 WHERE d2.DocumentID = pd.DocumentID)
                       AND d.IsDeactivated  = 0
                LEFT  JOIN tblLPPI_Reviews r
                        ON r.DocumentID = pd.DocumentID
                LEFT  JOIN tblLPPI_ReasonCodes rc
                        ON rc.ReasonCodeID = r.ReasonCodeID
                WHERE pd.PackageID = @p"
                + (IsPocView
                    ? "  AND EXISTS (SELECT 1 FROM tblLPPI_Documents dPoc " +
                      "WHERE dPoc.DocNoAccounting = d.DocNoAccounting " +
                      "  AND dPoc.ItemSequence = 1 " +
                      "  AND LTRIM(RTRIM(dPoc.PocEmail)) = LTRIM(RTRIM(@poc)))"
                    : "")
                + @"
                ORDER BY
                    (SELECT SUM(d3.InterestPayable)
                       FROM tblLPPI_Documents d3
                      WHERE d3.DocNoAccounting = d.DocNoAccounting
                        AND d3.IsDeactivated   = 0) DESC,
                    d.DocNoAccounting,
                    d.ItemSequence";

            DataTable detail;
            if (IsPocView)
                detail = LPPIHelper.ExecuteTable(detailSql,
                    LPPIHelper.P("@p",   packageId),
                    LPPIHelper.P("@poc", PocEmail));
            else
                detail = LPPIHelper.ExecuteTable(detailSql, LPPIHelper.P("@p", packageId));

            if (!detail.Columns.Contains("SearchBlob"))
                detail.Columns.Add("SearchBlob", typeof(string));

            foreach (DataRow r in detail.Rows)
            {
                r["SearchBlob"] = string.Join(" ", new[]
                {
                    Convert.ToString(r["VendorName"]),
                    Convert.ToString(r["DocNoAccounting"]),
                    Convert.ToString(r["PoNumber"]),
                    NullOrString(r["WbsElement"]),
                    NullOrString(r["WbsDesc"]),
                    NullOrString(r["CapabilityManager"]),
                    NullOrString(r["CapabilityManagerName"]),
                    NullOrString(r["DeliveryManager"]),
                    NullOrString(r["DeliveryManagerProgram"]),
                    NullOrString(r["DeliveryManagerName"]),
                    NullOrString(r["PocEmail"])
                });
            }

            rptDetail.DataSource = detail;
            rptDetail.DataBind();
        }

        // -------------------------------------------------------------------
        // Rendering helpers
        // -------------------------------------------------------------------

        private static string NullOrString(object val)
        {
            if (val == null || val == DBNull.Value) return "";
            return Convert.ToString(val);
        }

        protected string BuildReasonOptions(object selectedId)
        {
            int? sel = null;
            if (selectedId != null && selectedId != DBNull.Value)
            {
                int n;
                if (int.TryParse(Convert.ToString(selectedId), out n)) sel = n;
            }
            var sb = new StringBuilder();
            if (_reasonCodes == null) return "";

            bool selectedFound = false;
            foreach (DataRow r in _reasonCodes.Rows)
            {
                int    id      = Convert.ToInt32(r["ReasonCodeID"]);
                string code    = Convert.ToString(r["Code"]);
                string desc    = Convert.ToString(r["Description"]);
                string outcome = Convert.ToString(r["Outcome"]);
                bool   req     = Convert.ToBoolean(r["RequiresComments"]);
                sb.Append("<option value=\"").Append(id).Append("\"")
                  .Append(" data-outcome=\"").Append(LPPIHelper.Enc(outcome)).Append("\"")
                  .Append(" data-requires=\"").Append(req ? "1" : "0").Append("\"");
                if (sel.HasValue && sel.Value == id)
                {
                    sb.Append(" selected");
                    selectedFound = true;
                }
                string label = string.IsNullOrEmpty(desc) ? code : desc;
                sb.Append(">").Append(LPPIHelper.Enc(label)).Append("</option>");
            }

            // Special case: the row's currently-selected reason code is not
            // in the active list (e.g. RC-NR auto-applied at finalise; RC-NR
            // is IsActive=0 so it never appears in the dropdown). Without
            // this, the <select> would have no matching <option> and would
            // fall back to the placeholder "—", which is confusing — the row
            // *is* coded, the dropdown just can not show it.
            //
            // Look up the inactive code by ID and append it as a disabled,
            // pre-selected option. Disabled so a user (or bulk apply) cannot
            // pick it intentionally for some other row — RC-NR is system-
            // only and only ever applied via the finalise flow.
            if (sel.HasValue && !selectedFound)
            {
                DataTable inactive = LPPIHelper.ExecuteTable(@"
                    SELECT TOP 1 ReasonCodeID, Code, Description, Outcome, RequiresComments
                      FROM dbo.tblLPPI_ReasonCodes
                     WHERE ReasonCodeID = @id",
                    LPPIHelper.P("@id", sel.Value));
                if (inactive.Rows.Count > 0)
                {
                    DataRow r = inactive.Rows[0];
                    string code    = Convert.ToString(r["Code"]);
                    string desc    = Convert.ToString(r["Description"]);
                    string outcome = Convert.ToString(r["Outcome"]);
                    bool   req     = Convert.ToBoolean(r["RequiresComments"]);
                    string label   = string.IsNullOrEmpty(desc) ? code : desc;
                    sb.Append("<option value=\"").Append(Convert.ToInt32(r["ReasonCodeID"])).Append("\"")
                      .Append(" data-outcome=\"").Append(LPPIHelper.Enc(outcome)).Append("\"")
                      .Append(" data-requires=\"").Append(req ? "1" : "0").Append("\"")
                      .Append(" disabled selected>")
                      .Append(LPPIHelper.Enc(label))
                      .Append("</option>");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the &lt;option&gt; list for the toolbar facet selects. Source
        /// column varies by facet kind. For the Capability Manager facet, the
        /// option label combines the CM number and name (e.g. "50001289 — DG
        /// Air Cbt Cap-AF") so reviewers can recognise either; the option value
        /// is the bare CM number to match the data-cm attribute on rows.
        ///
        /// In POC view, the POC facet is generally degenerate (one entry —
        /// the POC themselves) but is harmless to render; the markup keeps
        /// it for layout consistency.
        /// </summary>
        protected string BuildFacetOptions(string kind)
        {
            if (_mainTable == null || _mainTable.Rows.Count == 0) return "";

            string column;
            string nameColumn = null;
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "dm":  column = "DeliveryManagerProgram"; break;
                case "poc": column = "PocEmail";               break;
                case "wbs": column = "WbsElement";             break;
                case "cm":  column = "CapabilityManager"; nameColumn = "CapabilityManagerName"; break;
                default:    return "";
            }

            // Use a dictionary so we can pair the value (number) with a
            // human-readable name where one exists. SortedDictionary keeps
            // the dropdown alphabetised by the value string.
            var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in _mainTable.Rows)
            {
                var v = r[column];
                if (v == null || v == DBNull.Value) continue;
                var s = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(s)) continue;
                s = s.Trim();

                string display = s;
                if (nameColumn != null)
                {
                    var nv = r[nameColumn];
                    if (nv != null && nv != DBNull.Value)
                    {
                        var ns = Convert.ToString(nv).Trim();
                        if (!string.IsNullOrEmpty(ns)) display = s + " \u2014 " + ns;
                    }
                }

                if (!values.ContainsKey(s)) values[s] = display;
            }

            var sb = new StringBuilder();
            foreach (var kv in values)
            {
                sb.Append("<option value=\"").Append(LPPIHelper.Enc(kv.Key)).Append("\">")
                  .Append(LPPIHelper.Enc(kv.Value)).Append("</option>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a ReviewedDate value as the ISO 8601 string the save
        /// handler expects in the version field. Empty string when the
        /// review row does not yet exist.
        /// </summary>
        protected static string FormatVersion(object reviewedDate)
        {
            if (reviewedDate == null || reviewedDate == DBNull.Value) return "";
            DateTime dt;
            if (reviewedDate is DateTime) dt = (DateTime)reviewedDate;
            else if (!DateTime.TryParse(Convert.ToString(reviewedDate),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return "";
            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private void SetDueCountdown()
        {
            TimeSpan diff = DueDate.Date - DateTime.Today;
            int days      = (int)diff.TotalDays;
            if (days < 0)       { DueCountdownText = (-days) + " day(s) overdue"; DueCssClass = "err"; }
            else if (days == 0) { DueCountdownText = "Due today";                 DueCssClass = "warn"; }
            else if (days <= LPPIHelper.ReminderWindowDays)
                                { DueCountdownText = days + " day(s) remaining";  DueCssClass = "warn"; }
            else                { DueCountdownText = days + " day(s) remaining";  DueCssClass = ""; }
        }

        private void ShowError()
        {
            phError.Visible  = true;
            phReview.Visible = false;
        }
    }
}
