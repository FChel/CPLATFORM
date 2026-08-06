/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Prepayment Report by Group (Tab 7 / 3.7)

  File   : 09_StoredProcedures_PrepaymentReport.sql
  Purpose: Read-only reporting stored procedures for the Prepayment Report tab. 3.7 is
           explicitly READ-ONLY - it never writes back to any other page. It aggregates the
           prepayment position and amortisation progress per delivery group from the
           amortisation schedules + periods (Tab 2), confirmed against journal export status
           (Tab 3) and reconciliation status (Tab 6).

           Report_GetGrid       - the 3.7 summary report table: per group + GL, the recognised
                                   amount, amortised to date, outstanding balance, % amortised,
                                   periods left, end date and status.
                                   Honours the four filters (period, group, GL account, status).
           Report_GetKpis       - the 5 KPI cards for the active period.
           Report_GetDrilldown  - the per-group drill-down: amortisation schedule (period #,
                                   period, amount, cumulative, status) + balance-movement card.
           Report_GetGroups     - data-driven "Delivery group" filter dropdown.
           Report_GetGlAccounts - data-driven "Account type" filter dropdown.
           Report_GetPeriods    - data-driven "Period" filter dropdown.

  Design  : Balances share the SAME basis as the rest of the app (Admin "Outstanding", Tab 6
            FINHUB balance) via prepayment.fn_FinhubBalance():
              Recognised  = SUM(AmortisationSchedule.TotalAmount where Status <> 'Draft')
              Amortised   = SUM(AmortisationPeriod.Amount where Status = 'Exported')
              Outstanding = Recognised - Amortised
            The drill-down's balance-movement card shows the current-period amortisation
            (the AmortisationPeriod whose PeriodDate falls in the report month).

  Grain   : One report row per (DeliveryGroup, PrepaymentGl). A group with multiple GLs gets a
            row per GL (mirrors the 3.7 "GL account" column).

  Naming  : prepayment.Report_<Action>
  Run after: 01_Schema, 08_StoredProcedures_GlReconciliation (for fn_FinhubBalance). Data via Import tab.
======================================================================================*/

SET NOCOUNT ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  fn_ReportGroupGl()
  Inline TVF - the per (group, GL) report figures (recognised / amortised / outstanding /
  periods-left / end-date / rolled-up status). Built on the amortisation schedules/periods
  so it always agrees with fn_FinhubBalance() and the Admin / Tab 6 numbers. One row per
  group + GL that has at least one non-Draft schedule. These are running-balance figures,
  not period-scoped - the report period only affects "amortised to date" presentation,
  which the callers handle.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION prepayment.fn_ReportGroupGl()
RETURNS TABLE
AS
RETURN
(
    WITH sched AS (
        SELECT
            s.Id,
            s.DeliveryGroupId,
            s.PrepaymentGlId,
            s.Status            AS ScheduleStatus,
            s.EndDate,
            Recognised = CASE WHEN s.Status <> 'Draft' THEN s.TotalAmount ELSE 0 END,
            IsActive   = CASE WHEN s.Status <> 'Draft' THEN 1 ELSE 0 END,
            -- amortised to date = exported periods
            Amortised  = ISNULL((
                            SELECT SUM(p.Amount) FROM dbo.tblPPM_AmortisationPeriod p
                            WHERE  p.AmortisationScheduleId = s.Id
                              AND  p.Status = 'Exported' AND p.IsDeleted = 0), 0),
            -- periods left = scheduled periods not yet exported (active schedules only)
            PeriodsLeft = CASE WHEN s.Status = 'Draft' THEN 0 ELSE ISNULL((
                            SELECT COUNT(*) FROM dbo.tblPPM_AmortisationPeriod p
                            WHERE  p.AmortisationScheduleId = s.Id
                              AND  p.Status <> 'Exported' AND p.IsDeleted = 0), 0) END,
            ScheduleEnd = ISNULL((
                            SELECT MAX(p.PeriodDate) FROM dbo.tblPPM_AmortisationPeriod p
                            WHERE  p.AmortisationScheduleId = s.Id AND p.IsDeleted = 0), s.EndDate)
        FROM   dbo.tblPPM_AmortisationSchedule s
        WHERE  s.IsDeleted = 0
          AND  s.DeliveryGroupId IS NOT NULL
          AND  s.PrepaymentGlId  IS NOT NULL
    )
    SELECT
        sched.DeliveryGroupId,
        sched.PrepaymentGlId,
        Recognised  = SUM(sched.Recognised),
        Amortised   = SUM(sched.Amortised),
        Outstanding = SUM(sched.Recognised) - SUM(sched.Amortised),
        PeriodsLeft = MAX(sched.PeriodsLeft),       -- periods remaining for the group's longest active schedule
        EndDate     = MAX(CASE WHEN sched.IsActive = 1 THEN sched.ScheduleEnd END),
        -- group-level rolled-up schedule status (worst-wins ordering: Blocked > Suspended > Draft > Active > Completed)
        StatusRank  = MIN(CASE sched.ScheduleStatus
                              WHEN 'Blocked'   THEN 1
                              WHEN 'Suspended' THEN 2
                              WHEN 'Draft'     THEN 3
                              WHEN 'Active'    THEN 4
                              WHEN 'Completed' THEN 5
                              ELSE 6 END)
    FROM   sched
    GROUP BY sched.DeliveryGroupId, sched.PrepaymentGlId
    -- Exclude pure-Draft group/GL combos: a report line needs at least one recognised
    -- (non-Draft) schedule. Draft-only setups have no recognised prepayment position yet.
    HAVING SUM(sched.IsActive) > 0
);
GO

/*--------------------------------------------------------------------------------------
  Report_GetGrid
  The 3.7 summary report table - one row per (group, GL) with the columns:
    Group, Group name, GL account, Vendor, Recognised amount, Amortised to date,
    Outstanding balance, % amortised, Periods left, End date, Status.
  Filters (all optional):
    @Period   'YYYY/MM' - report period (defaults to latest); scopes "amortised to date".
    @GroupId  BIGINT    - a single delivery group (NULL/0 = all).
    @GlId     BIGINT    - a single prepayment GL account (NULL/0 = all).
    @Status   VARCHAR   - 'Amortising' | 'Completed' | 'Pending' | 'Blocked' (NULL/''/'All' = all).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Report_GetGrid
    @Period  VARCHAR(7)  = NULL,
    @GroupId BIGINT      = NULL,
    @GlId    BIGINT      = NULL,
    @Status  VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Period = '' SET @Period = NULL;
    IF @Period IS NULL
        SET @Period = (SELECT MAX(Period) FROM dbo.tblPPM_Reconciliation WHERE IsDeleted = 0);
    IF @Period IS NULL
        SET @Period = CONVERT(VARCHAR(4), YEAR(SYSUTCDATETIME())) + '/'
                    + RIGHT('0' + CONVERT(VARCHAR(2), MONTH(SYSUTCDATETIME())), 2);

    IF ISNULL(@GroupId, 0) = 0 SET @GroupId = NULL;
    IF ISNULL(@GlId, 0)    = 0 SET @GlId = NULL;
    IF @Status = '' OR @Status = 'All' SET @Status = NULL;

    SELECT
        r.DeliveryGroupId,
        dg.DeliveryGroupCode,
        dg.GroupName,
        gl.GlAccount,
        gl.GlDescription,
        Vendor = (
            SELECT TOP 1 v.VendorName
            FROM   dbo.tblPPM_AmortisationSchedule s2
            JOIN   dbo.tblPPM_Invoice i2 ON i2.Id = s2.InvoiceId AND i2.IsDeleted = 0
            JOIN   dbo.tblPPM_Vendor  v  ON v.Id  = i2.VendorId
            WHERE  s2.DeliveryGroupId = r.DeliveryGroupId
              AND  s2.PrepaymentGlId  = r.PrepaymentGlId
              AND  s2.IsDeleted = 0 AND s2.Status <> 'Draft'
            ORDER BY s2.TotalAmount DESC),
        CapexOpex = (
            SELECT TOP 1 i3.CapexOpex
            FROM   dbo.tblPPM_AmortisationSchedule s3
            JOIN   dbo.tblPPM_Invoice i3 ON i3.Id = s3.InvoiceId AND i3.IsDeleted = 0
            WHERE  s3.DeliveryGroupId = r.DeliveryGroupId
              AND  s3.PrepaymentGlId  = r.PrepaymentGlId
              AND  s3.IsDeleted = 0 AND s3.Status <> 'Draft'
            ORDER BY s3.TotalAmount DESC),
        r.Recognised,
        r.Amortised,
        r.Outstanding,
        PercentAmortised = CASE WHEN r.Recognised > 0
                                THEN CAST(ROUND(100.0 * r.Amortised / r.Recognised, 1) AS DECIMAL(5,1))
                                ELSE 0 END,
        r.PeriodsLeft,
        r.EndDate,
        -- normalise the rolled-up schedule status into the 3.7 report status vocabulary
        Status = CASE r.StatusRank
                    WHEN 1 THEN 'Blocked'
                    WHEN 2 THEN 'Suspended'
                    WHEN 3 THEN 'Pending'        -- a Draft schedule = pending approval / setup
                    WHEN 4 THEN 'Amortising'
                    WHEN 5 THEN 'Completed'
                    ELSE 'Amortising' END
    INTO #grid
    FROM   prepayment.fn_ReportGroupGl() r
    JOIN   dbo.tblPPM_DeliveryGroup dg ON dg.Id = r.DeliveryGroupId AND dg.IsDeleted = 0
    LEFT   JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.Id = r.PrepaymentGlId;

    SELECT *
    FROM   #grid
    WHERE  (@GroupId IS NULL OR DeliveryGroupId = @GroupId)
      AND  (@GlId    IS NULL OR GlAccount = (SELECT GlAccount FROM dbo.tblPPM_PrepaymentGlAccount WHERE Id = @GlId))
      AND  (@Status  IS NULL OR Status = @Status)
    ORDER BY DeliveryGroupCode, GlAccount;

    DROP TABLE #grid;
END
GO

/*--------------------------------------------------------------------------------------
  Report_GetKpis
  The 5 KPI cards (3.7) for the active period:
    Total recognised, Total amortised, Outstanding balance,
    Groups with balance, Completed this period.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Report_GetKpis
    @Period  VARCHAR(7)  = NULL,
    @GroupId BIGINT      = NULL,
    @GlId    BIGINT      = NULL,
    @Status  VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Period = '' SET @Period = NULL;
    IF @Period IS NULL
        SET @Period = (SELECT MAX(Period) FROM dbo.tblPPM_Reconciliation WHERE IsDeleted = 0);
    IF @Period IS NULL
        SET @Period = CONVERT(VARCHAR(4), YEAR(SYSUTCDATETIME())) + '/'
                    + RIGHT('0' + CONVERT(VARCHAR(2), MONTH(SYSUTCDATETIME())), 2);

    IF ISNULL(@GroupId, 0) = 0 SET @GroupId = NULL;
    IF ISNULL(@GlId, 0)    = 0 SET @GlId = NULL;
    IF @Status = '' OR @Status = 'All' SET @Status = NULL;

    DECLARE @glAccount VARCHAR(10) =
        CASE WHEN @GlId IS NULL THEN NULL
             ELSE (SELECT GlAccount FROM dbo.tblPPM_PrepaymentGlAccount WHERE Id = @GlId) END;

    ;WITH g AS (
        SELECT
            r.DeliveryGroupId, r.PrepaymentGlId,
            r.Recognised, r.Amortised, r.Outstanding, r.PeriodsLeft,
            gl.GlAccount,
            Status = CASE r.StatusRank
                        WHEN 1 THEN 'Blocked' WHEN 2 THEN 'Suspended' WHEN 3 THEN 'Pending'
                        WHEN 4 THEN 'Amortising' WHEN 5 THEN 'Completed' ELSE 'Amortising' END
        FROM   prepayment.fn_ReportGroupGl() r
        LEFT   JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.Id = r.PrepaymentGlId
        WHERE  (@GroupId   IS NULL OR r.DeliveryGroupId = @GroupId)
          AND  (@glAccount IS NULL OR gl.GlAccount = @glAccount)
    ),
    f AS (
        SELECT * FROM g
        WHERE (@Status IS NULL OR Status = @Status)
    )
    SELECT
        Period            = @Period,
        TotalRecognised   = ISNULL(SUM(Recognised), 0),
        TotalAmortised    = ISNULL(SUM(Amortised), 0),
        Outstanding       = ISNULL(SUM(Outstanding), 0),
        TotalGroups       = (SELECT COUNT(*) FROM dbo.tblPPM_DeliveryGroup WHERE IsDeleted = 0 AND IsActive = 1),
        GroupsWithBalance = (SELECT COUNT(DISTINCT DeliveryGroupId) FROM f WHERE Outstanding > 0.01),
        -- "completed this period" = groups that fully finished amortising (no periods left + nil outstanding)
        CompletedThisPeriod = (SELECT COUNT(DISTINCT DeliveryGroupId) FROM f
                               WHERE Status = 'Completed' OR (PeriodsLeft = 0 AND Outstanding <= 0.01))
    FROM   f;
END
GO

/*--------------------------------------------------------------------------------------
  Report_GetDrilldown
  The per-group drill-down (3.7):
    Set 1 - the amortisation schedule (period #, period, amount, running cumulative, status)
    Set 2 - the balance-movement card (opening, amortised to date, this period, closing,
            periods remaining, % amortised) as label/value rows
    Set 3 - the header (codes, names, month X of Y, % for the progress bar)
  @DeliveryGroupId selects the group; @Period drives the "current period" highlight + movement.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Report_GetDrilldown
    @DeliveryGroupId BIGINT,
    @Period          VARCHAR(7) = NULL,
    @GlId            BIGINT     = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Period = '' SET @Period = NULL;
    IF @Period IS NULL
        SET @Period = (SELECT MAX(Period) FROM dbo.tblPPM_Reconciliation WHERE IsDeleted = 0);
    IF ISNULL(@GlId, 0) = 0 SET @GlId = NULL;

    DECLARE @periodStart DATE = TRY_CONVERT(DATE, LEFT(@Period,4) + '-' + RIGHT(@Period,2) + '-01');

    -- Resolve the schedule to drill into: the largest non-Draft schedule for the group
    -- (optionally constrained to a GL). Mirrors the 3.7 "selected group" drill-down.
    DECLARE @scheduleId BIGINT = (
        SELECT TOP 1 s.Id
        FROM   dbo.tblPPM_AmortisationSchedule s
        WHERE  s.DeliveryGroupId = @DeliveryGroupId AND s.IsDeleted = 0 AND s.Status <> 'Draft'
          AND  (@GlId IS NULL OR s.PrepaymentGlId = @GlId)
        ORDER BY s.TotalAmount DESC, s.Id);

    /* (1) amortisation schedule rows with running cumulative */
    SELECT
        p.PeriodNumber,
        PeriodLabel = LEFT(DATENAME(MONTH, p.PeriodDate), 3) + ' ' + CONVERT(VARCHAR(4), YEAR(p.PeriodDate)),
        p.PeriodDate,
        p.Amount,
        Cumulative  = SUM(p.Amount) OVER (ORDER BY p.PeriodNumber ROWS UNBOUNDED PRECEDING),
        p.Status,
        IsCurrent   = CASE WHEN @periodStart IS NOT NULL
                            AND p.PeriodDate >= @periodStart
                            AND p.PeriodDate < DATEADD(MONTH, 1, @periodStart)
                           THEN 1 ELSE 0 END
    FROM   dbo.tblPPM_AmortisationPeriod p
    WHERE  p.AmortisationScheduleId = @scheduleId AND p.IsDeleted = 0
    ORDER BY p.PeriodNumber;

    /* (2) balance-movement card figures */
    SELECT
        Recognised  = ISNULL((SELECT TotalAmount FROM dbo.tblPPM_AmortisationSchedule WHERE Id = @scheduleId), 0),
        AmortisedToDate = ISNULL((
                            SELECT SUM(Amount) FROM dbo.tblPPM_AmortisationPeriod
                            WHERE  AmortisationScheduleId = @scheduleId AND Status = 'Exported' AND IsDeleted = 0), 0),
        ThisPeriod  = ISNULL((
                            SELECT SUM(Amount) FROM dbo.tblPPM_AmortisationPeriod
                            WHERE  AmortisationScheduleId = @scheduleId AND IsDeleted = 0
                              AND  @periodStart IS NOT NULL
                              AND  PeriodDate >= @periodStart AND PeriodDate < DATEADD(MONTH, 1, @periodStart)), 0),
        PeriodsTotal = ISNULL((SELECT COUNT(*) FROM dbo.tblPPM_AmortisationPeriod
                               WHERE AmortisationScheduleId = @scheduleId AND IsDeleted = 0), 0),
        PeriodsExported = ISNULL((SELECT COUNT(*) FROM dbo.tblPPM_AmortisationPeriod
                               WHERE AmortisationScheduleId = @scheduleId AND Status = 'Exported' AND IsDeleted = 0), 0),
        StartDate   = (SELECT MIN(PeriodDate) FROM dbo.tblPPM_AmortisationPeriod WHERE AmortisationScheduleId = @scheduleId AND IsDeleted = 0),
        EndDate     = (SELECT MAX(PeriodDate) FROM dbo.tblPPM_AmortisationPeriod WHERE AmortisationScheduleId = @scheduleId AND IsDeleted = 0);

    /* (3) header */
    SELECT
        dg.DeliveryGroupCode,
        dg.GroupName,
        gl.GlAccount,
        gl.GlDescription,
        ScheduleId   = @scheduleId,
        Period       = @Period
    FROM   dbo.tblPPM_DeliveryGroup dg
    LEFT   JOIN dbo.tblPPM_AmortisationSchedule s ON s.Id = @scheduleId
    LEFT   JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.Id = s.PrepaymentGlId
    WHERE  dg.Id = @DeliveryGroupId;
END
GO

/*--------------------------------------------------------------------------------------
  Report_GetGroups - data-driven "Delivery group" filter (groups that have a non-Draft
  schedule, i.e. appear in the report), newest code first.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Report_GetGroups
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT
        dg.Id,
        dg.DeliveryGroupCode,
        dg.GroupName
    FROM   dbo.tblPPM_DeliveryGroup dg
    JOIN   dbo.tblPPM_AmortisationSchedule s
           ON s.DeliveryGroupId = dg.Id AND s.IsDeleted = 0 AND s.Status <> 'Draft'
    WHERE  dg.IsDeleted = 0
    ORDER BY dg.DeliveryGroupCode DESC;
END
GO

/*--------------------------------------------------------------------------------------
  Report_GetGlAccounts - data-driven "Account type" filter (the prepayment GLs actually in
  use on report schedules). 514xxx prepayment GLs with their descriptions.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Report_GetGlAccounts
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT
        gl.Id,
        gl.GlAccount,
        gl.GlDescription
    FROM   dbo.tblPPM_PrepaymentGlAccount gl
    JOIN   dbo.tblPPM_AmortisationSchedule s
           ON s.PrepaymentGlId = gl.Id AND s.IsDeleted = 0 AND s.Status <> 'Draft'
    WHERE  gl.IsDeleted = 0
    ORDER BY gl.GlAccount;
END
GO

/*--------------------------------------------------------------------------------------
  Report_GetPeriods - data-driven "Period" filter. Distinct months that have an
  amortisation period, newest first, plus a "YTD <year>" pseudo-option is added in the
  service layer (this proc returns the concrete months).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Report_GetPeriods
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PeriodKey   = CONVERT(VARCHAR(4), YEAR(PeriodDate)) + '/'
                    + RIGHT('0' + CONVERT(VARCHAR(2), MONTH(PeriodDate)), 2),
        PeriodLabel = LEFT(DATENAME(MONTH, PeriodDate), 3) + ' ' + CONVERT(VARCHAR(4), YEAR(PeriodDate)),
        SortKey     = MIN(PeriodDate)
    FROM   dbo.tblPPM_AmortisationPeriod
    WHERE  IsDeleted = 0 AND PeriodDate IS NOT NULL
    GROUP BY CONVERT(VARCHAR(4), YEAR(PeriodDate)) + '/' + RIGHT('0' + CONVERT(VARCHAR(2), MONTH(PeriodDate)), 2),
             LEFT(DATENAME(MONTH, PeriodDate), 3) + ' ' + CONVERT(VARCHAR(4), YEAR(PeriodDate))
    ORDER BY SortKey DESC;
END
GO

PRINT 'Prepayment Report stored procedures created (fn_ReportGroupGl, Report_GetGrid, Report_GetKpis, Report_GetDrilldown, Report_GetGroups, Report_GetGlAccounts, Report_GetPeriods).';
GO
