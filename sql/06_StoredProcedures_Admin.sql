/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Admin Control Tower (Tab 4)

  File   : 06_StoredProcedures_Admin.sql
  Purpose: Read and write stored procedures for the Admin Control Tower tab.
           Admin_GetKpis           - 5 KPI aggregates from live data
           Admin_GetProcessTracker - per-PO pipeline stage indicators
           Admin_GetExceptions     - open exception items
           Admin_GetPeriodSummary  - period-to-date activity counts
  Naming  : prepayment.Admin_<Action>
  Run after: 01_Schema_PrepaymentManagement.sql (data is loaded via the Import tab)
======================================================================================*/

SET NOCOUNT ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  Admin_GetKpis
  Returns one row: TotalRecognised, TotalAmortised, AwaitingApproval, ExceptionsOpen.
  OutstandingBalance = TotalRecognised - TotalAmortised, computed in C#.
  ExceptionsOpen mirrors the row count returned by Admin_GetExceptions.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_GetKpis
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ISNULL((
            SELECT SUM(s.TotalAmount)
            FROM   dbo.tblPPM_AmortisationSchedule s
            WHERE  s.IsDeleted = 0
              AND  s.Status NOT IN ('Draft')
        ), 0) AS TotalRecognised,

        ISNULL((
            SELECT SUM(p.Amount)
            FROM   dbo.tblPPM_AmortisationPeriod p
            WHERE  p.IsDeleted = 0
              AND  p.Status = 'Exported'
        ), 0) AS TotalAmortised,

        (SELECT COUNT(*)
         FROM   dbo.tblPPM_Journal j
         WHERE  j.IsDeleted = 0
           AND  j.Status = 'PendingApproval'
        ) AS AwaitingApproval,

        /* ExceptionsOpen: mirrors exactly the row count returned by Admin_GetExceptions.
           Five sources: duplicate recognition | no-reason rejections | schedule mismatch |
           export ready | unresolved GL reconciliation variances (Tab 6 -> Tab 4, 3.6). */
        (
            /* duplicate recognition - one row per invoice with 2+ active recognition journals */
            SELECT COUNT(*)
            FROM   dbo.tblPPM_Invoice i2
            WHERE  i2.IsDeleted = 0
              AND  (SELECT COUNT(*) FROM dbo.tblPPM_Journal jd
                    WHERE jd.InvoiceId = i2.Id AND jd.JournalType = 'Recognition'
                      AND jd.Status NOT IN ('Rejected') AND jd.IsDeleted = 0) > 1
        )
        +
        (
            /* approval rejection no root cause - one grouped row if any rejected journal has no reason */
            SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END
            FROM   dbo.tblPPM_Journal j
            WHERE  j.Status = 'Rejected' AND j.IsDeleted = 0
              AND  NOT EXISTS (
                       SELECT 1 FROM dbo.tblPPM_JournalAudit ja
                       WHERE  ja.JournalId = j.Id AND ja.Action = 'Rejected'
                         AND  ISNULL(ja.Comments, N'') != N''
                   )
        )
        +
        (
            /* schedule total mismatches - one row per mismatch */
            SELECT COUNT(*)
            FROM   dbo.tblPPM_AmortisationSchedule s
            JOIN   dbo.tblPPM_Invoice i ON i.Id = s.InvoiceId AND i.IsDeleted = 0
            WHERE  s.IsDeleted = 0 AND ABS(s.TotalAmount - i.Amount) > 0.01
        )
        +
        (
            /* approved journals ready for export - one grouped row if any exist */
            SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END
            FROM   dbo.tblPPM_Journal
            WHERE  Status = 'Approved' AND IsDeleted = 0
        )
        +
        (
            /* unresolved GL reconciliation variances - one row per open variance */
            SELECT COUNT(*)
            FROM   dbo.tblPPM_Reconciliation
            WHERE  IsDeleted = 0
              AND  Status IN ('Variance','NotMatched')
              AND  ResolvedDate IS NULL
        )
        AS ExceptionsOpen;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_GetProcessTracker
  One row per PO with any prepayment activity, with integer stage indicators:
    0 = not yet reached
    1 = complete / done
    2 = in progress / pending (needs attention)
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_GetProcessTracker
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        po.PoNumber,
        ISNULL(v.VendorName, po.PoNumber) AS VendorName,
        po.TotalValue,
        po.CapexOpex,

        /* PoFlag stage: 1=Prepayment-flagged line exists, 3=flagged NotPrepayment (no Prepayment line), 0=Pending/none */
        CASE
            WHEN EXISTS(
                SELECT 1 FROM dbo.tblPPM_PoDeliveryLine l
                WHERE  l.PurchaseOrderId = po.Id
                  AND  l.PrepaymentFlag  = 'Prepayment'
                  AND  l.IsDeleted       = 0
            ) THEN 1
            WHEN EXISTS(
                SELECT 1 FROM dbo.tblPPM_PoDeliveryLine l
                WHERE  l.PurchaseOrderId = po.Id
                  AND  l.PrepaymentFlag  = 'NotPrepayment'
                  AND  l.IsDeleted       = 0
            ) THEN 3
            ELSE 0
        END AS PoFlagStage,

        /* Invoice stage: 1=at least one invoice linked, 3=none */
        CASE WHEN EXISTS(
            SELECT 1 FROM dbo.tblPPM_Invoice i
            WHERE  i.PurchaseOrderId = po.Id
              AND  i.IsDeleted       = 0
        ) THEN 1 ELSE 3 END AS InvoiceStage,

        /* SetupStage: 1=Complete exists, 2=AmortisationNeeded/DraftInProgress, 0=none */
        CASE
            WHEN EXISTS(
                SELECT 1 FROM dbo.tblPPM_Invoice i
                WHERE  i.PurchaseOrderId = po.Id
                  AND  i.SetupStatus     = 'Complete'
                  AND  i.IsDeleted       = 0
            ) THEN 1
            WHEN EXISTS(
                SELECT 1 FROM dbo.tblPPM_Invoice i
                WHERE  i.PurchaseOrderId = po.Id
                  AND  i.SetupStatus IN ('AmortisationNeeded','DraftInProgress')
                  AND  i.IsDeleted       = 0
            ) THEN 2
            ELSE 0
        END AS SetupStage,

        /* RecognitionStage: 1=Approved/Exported, 2=PendingApproval, 3=Rejected, 0=none.
           Precedence: a successful (Approved/Exported) journal wins, then pending, then rejected. */
        CASE
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.JournalType     = 'Recognition'
                  AND  j.Status IN ('Approved','Exported')
                  AND  j.IsDeleted       = 0
            ) THEN 1
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.JournalType     = 'Recognition'
                  AND  j.Status          = 'PendingApproval'
                  AND  j.IsDeleted       = 0
            ) THEN 2
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.JournalType     = 'Recognition'
                  AND  j.Status          = 'Rejected'
                  AND  j.IsDeleted       = 0
            ) THEN 3
            ELSE 0
        END AS RecognitionStage,

        /* AmortisationStage: 1=at-least-one Exported, 2=PendingApproval, 3=Rejected, 0=none.
           Precedence: exported wins, then pending, then rejected. */
        CASE
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.JournalType     = 'Amortisation'
                  AND  j.Status          = 'Exported'
                  AND  j.IsDeleted       = 0
            ) THEN 1
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.JournalType     = 'Amortisation'
                  AND  j.Status          = 'PendingApproval'
                  AND  j.IsDeleted       = 0
            ) THEN 2
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.JournalType     = 'Amortisation'
                  AND  j.Status          = 'Rejected'
                  AND  j.IsDeleted       = 0
            ) THEN 3
            ELSE 0
        END AS AmortisationStage,

        /* ExportStage: 2=at-least-one Approved (ready), 1=all Exported (none left), 0=none */
        CASE
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.Status          = 'Approved'
                  AND  j.IsDeleted       = 0
            ) THEN 2
            WHEN EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.Status          = 'Exported'
                  AND  j.IsDeleted       = 0
            ) AND NOT EXISTS(
                SELECT 1
                FROM   dbo.tblPPM_Journal j
                JOIN   dbo.tblPPM_Invoice  i ON i.Id = j.InvoiceId
                WHERE  i.PurchaseOrderId = po.Id
                  AND  j.Status         NOT IN ('Exported')
                  AND  j.IsDeleted       = 0
            ) THEN 1
            ELSE 0
        END AS ExportStage

    FROM  dbo.tblPPM_PurchaseOrder po
    LEFT  JOIN dbo.tblPPM_Vendor v ON v.Id = po.VendorId
    WHERE po.IsDeleted = 0
      AND (
          EXISTS(SELECT 1 FROM dbo.tblPPM_PoDeliveryLine l
                 WHERE l.PurchaseOrderId = po.Id
                   AND l.PrepaymentFlag  = 'Prepayment'
                   AND l.IsDeleted       = 0)
          OR
          EXISTS(SELECT 1 FROM dbo.tblPPM_Invoice i
                 WHERE i.PurchaseOrderId = po.Id
                   AND i.IsDeleted       = 0)
      )
    ORDER BY po.PoDate DESC, po.PoNumber;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_GetExceptions
  Derives exceptions dynamically from Tab 1/2/3 transactional data - no manual seed.

  Sources (matches documentation Admin Control Tower screen):
    Tab 3 - Duplicate recognition journals  -> ExceptionType = 'Blocked'   (one row per invoice)
    Tab 3 - Rejected journals, no reason    -> ExceptionType = 'FollowUp'  (one grouped row)
    Tab 2 - Schedule total <> invoice amount -> ExceptionType = 'Error'     (one row per mismatch)
    Tab 3 - Approved, export-ready          -> ExceptionType = 'Ready'     (one grouped row)
    Tab 6 - Unresolved reconciliation variance -> ExceptionType = 'Variance' (one row per group/GL)
            (3.6 output: "Page 6 -> Page 4 (Admin): unresolved variances appear as exceptions")
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_GetExceptions
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id            = CAST(ROW_NUMBER() OVER (ORDER BY SortOrder, Title) AS BIGINT),
        Title,
        Detail,
        ExceptionType,
        Status        = 'Open'
    FROM (

        /* 1. Duplicate recognition blocked
              Title : "Duplicate recognition blocked"
              Detail: "PO {number} / line {number}"  - matches documentation format */
        SELECT
            SortOrder     = 1,
            Title         = N'Duplicate recognition blocked',
            Detail        = N'PO ' + po.PoNumber
                          + N' / line ' + ISNULL(CAST(l.LineNumber AS VARCHAR(10)), N'?'),
            ExceptionType = 'Blocked'
        FROM   dbo.tblPPM_Invoice i
        JOIN   dbo.tblPPM_PurchaseOrder po   ON po.Id = i.PurchaseOrderId AND po.IsDeleted = 0
        LEFT   JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = i.PoDeliveryLineId  AND l.IsDeleted = 0
        WHERE  i.IsDeleted = 0
          AND  (
                   SELECT COUNT(*)
                   FROM   dbo.tblPPM_Journal jd
                   WHERE  jd.InvoiceId   = i.Id
                     AND  jd.JournalType = 'Recognition'
                     AND  jd.Status NOT IN ('Rejected')
                     AND  jd.IsDeleted   = 0
               ) > 1

        UNION ALL

        /* 2. Approval rejection - no root cause
              Detail: "{n} journals require reason code"  - matches documentation format */
        SELECT
            SortOrder     = 2,
            Title         = N'Approval rejection - no root cause',
            Detail        = CAST(cnt AS VARCHAR(10)) + N' journals require reason code',
            ExceptionType = 'FollowUp'
        FROM (
            SELECT cnt = COUNT(*)
            FROM   dbo.tblPPM_Journal j
            WHERE  j.Status    = 'Rejected'
              AND  j.IsDeleted = 0
              AND  NOT EXISTS (
                       SELECT 1
                       FROM   dbo.tblPPM_JournalAudit ja
                       WHERE  ja.JournalId = j.Id
                         AND  ja.Action    = 'Rejected'
                         AND  ISNULL(ja.Comments, N'') != N''
                   )
            HAVING COUNT(*) > 0
        ) rej_noreason

        UNION ALL

        /* 3. Schedule total mismatch
              Title : "Schedule total mismatch"
              Detail: "PO {number} - total <> recognised amount"  - matches documentation format */
        SELECT
            SortOrder     = 3,
            Title         = N'Schedule total mismatch',
            Detail        = N'PO ' + po.PoNumber + N' - total <> recognised amount',
            ExceptionType = 'Error'
        FROM   dbo.tblPPM_AmortisationSchedule s
        JOIN   dbo.tblPPM_Invoice i        ON i.Id  = s.InvoiceId       AND i.IsDeleted = 0
        JOIN   dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId AND po.IsDeleted = 0
        WHERE  s.IsDeleted = 0
          AND  ABS(s.TotalAmount - i.Amount) > 0.01

        UNION ALL

        /* 4. Export batch ready
              Title : "Export batch PREP-{MMYY}-{seq}"  - matches documentation format
              Detail: "{n} journals ready for interface handoff"
              Seq is derived from count of approved journals (padded to 2 digits). */
        SELECT
            SortOrder     = 4,
            Title         = N'Export batch PREP-'
                          + RIGHT('0' + CAST(MONTH(SYSUTCDATETIME()) AS VARCHAR(2)), 2)
                          + RIGHT(CAST(YEAR(SYSUTCDATETIME()) AS VARCHAR(4)), 2)
                          + N'-'
                          + RIGHT('0' + CAST(cnt AS VARCHAR(2)), 2),
            Detail        = CAST(cnt AS VARCHAR(10)) + N' journals ready for interface handoff',
            ExceptionType = 'Ready'
        FROM (
            SELECT cnt = COUNT(*)
            FROM   dbo.tblPPM_Journal
            WHERE  Status    = 'Approved'
              AND  IsDeleted = 0
            HAVING COUNT(*) > 0
        ) ex_rdy

        UNION ALL

        /* 5. Unresolved GL reconciliation variance  (Tab 6 -> Tab 4 feed, 3.6)
              Title : "GL reconciliation variance"
              Detail: "{group} / GL {account} - {signed variance}"
              One row per still-open Reconciliation variance (not Mark-resolved). */
        SELECT
            SortOrder     = 5,
            Title         = N'GL reconciliation variance',
            Detail        = dg.DeliveryGroupCode
                          + N' / GL ' + ISNULL(gl.GlAccount, N'?')
                          + N' - ' + CASE WHEN r.Variance < 0 THEN N'-' ELSE N'' END
                          + N'$' + CONVERT(VARCHAR(20), CAST(ABS(r.Variance) AS MONEY), 1),
            ExceptionType = 'Variance'
        FROM   dbo.tblPPM_Reconciliation r
        JOIN   dbo.tblPPM_DeliveryGroup dg ON dg.Id = r.DeliveryGroupId AND dg.IsDeleted = 0
        LEFT   JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.Id = r.PrepaymentGlId
        WHERE  r.IsDeleted = 0
          AND  r.Status IN ('Variance','NotMatched')
          AND  r.ResolvedDate IS NULL

    ) x
    ORDER BY SortOrder, Title;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_GetPeriodSummary
  Activity counts for the current calendar month, plus a human-readable period label.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_GetPeriodSummary
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @year   INT     = YEAR(SYSUTCDATETIME());
    DECLARE @month  INT     = MONTH(SYSUTCDATETIME());
    DECLARE @period VARCHAR(7)
        = CAST(@year AS VARCHAR(4)) + '/' + RIGHT('0' + CAST(@month AS VARCHAR(2)), 2);

    SELECT
        (SELECT COUNT(*)
         FROM   dbo.tblPPM_PoDeliveryLine
         WHERE  PrepaymentFlag  = 'Prepayment'
           AND  YEAR(FlaggedDate)  = @year
           AND  MONTH(FlaggedDate) = @month
           AND  IsDeleted          = 0
        ) AS LinesFlagged,

        (SELECT COUNT(*)
         FROM   dbo.tblPPM_Invoice
         WHERE  YEAR(SourceLoadDate)  = @year
           AND  MONTH(SourceLoadDate) = @month
           AND  IsDeleted             = 0
        ) AS InvoicesAssessed,

        (SELECT COUNT(*)
         FROM   dbo.tblPPM_Journal
         WHERE  JournalType   = 'Recognition'
           AND  PostingPeriod = @period
           AND  IsDeleted     = 0
        ) AS RecognitionJournals,

        (SELECT COUNT(*)
         FROM   dbo.tblPPM_Journal
         WHERE  JournalType   = 'Amortisation'
           AND  PostingPeriod = @period
           AND  IsDeleted     = 0
        ) AS AmortisationJournals,

        /* JournalsExported: total AmortisationPeriod rows processed to ERP (all-time),
           reflecting how many discrete period-slice journals have been exported.
           PostingPeriod filter is not used here because exported amortisation periods
           accumulate across months - the running total is what operations teams track. */
        (SELECT COUNT(*)
         FROM   dbo.tblPPM_AmortisationPeriod
         WHERE  Status    = 'Exported'
           AND  IsDeleted = 0
        ) AS JournalsExported,

        /* e.g. "Jun 2026" */
        LEFT(DATENAME(MONTH, SYSUTCDATETIME()), 3) + ' ' + CAST(@year AS VARCHAR(4)) AS PeriodLabel;
END
GO

PRINT 'Admin stored procedures created (GetKpis, GetProcessTracker, GetExceptions, GetPeriodSummary).';
GO
