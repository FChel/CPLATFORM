/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - GL Balance Reconciliation (Tab 6 / 3.6, Source D)

  File   : 08_StoredProcedures_GlReconciliation.sql
  Purpose: Read + write stored procedures for the GL Balance Reconciliation tab.
           Recon_SaveExtract       - ingest an uploaded SAP GL balance CSV: writes one
                                      GlExtractFile + N GlBalanceRecord rows, then upserts
                                      the Reconciliation rows for that period.
           Recon_GetGrid           - the reconciliation grid: per group + GL, the SAP
                                      closing balance (from the latest extract) vs the live
                                      FINHUB prepayment balance, with variance + status.
           Recon_GetKpis           - the 5 KPI cards.
           Recon_GetVarianceDetail - the two investigation panels for one reconciliation row.
           Recon_GetPeriods        - periods that have an extract (data-driven dropdown).
           Recon_Resolve           - mark a variance explained / raise an adjustment.

  Design  : The SAP side is the uploaded extract (GlBalanceRecord.ClosingBalance). The FINHUB
            (prepayment) side is DERIVED LIVE from Tab 2/3 - the outstanding prepayment asset
            per group+GL = SUM(AmortisationSchedule.TotalAmount, non-Draft)
                          - SUM(AmortisationPeriod.Amount where Exported). So the FINHUB
            balance always agrees with the rest of the app (same basis as the Admin
            "Outstanding balance" KPI), and only the SAP figures are persisted.

  Grain   : Reconciliation is per (DeliveryGroup, PrepaymentGl, Period).
  Status  : Reconciled | Variance | JournalPending | NotMatched
            - Variance       : |SAP - FINHUB| > 0.01 and no later exported journal pending
            - JournalPending : balances differ only because an Approved (not yet Exported)
                               journal will move the SAP side once posted (shown with a "*")
            - NotMatched     : a group/GL exists on one side only
            - Reconciled     : within 1 cent

  Naming  : prepayment.Recon_<Action>
  Run after: 01_Schema_PrepaymentManagement.sql (data is loaded via the Import tab)
======================================================================================*/

SET NOCOUNT ON;
-- Required so the write procs (Recon_Resolve, Recon_SaveExtract) are CREATED with
-- QUOTED_IDENTIFIER ON. dbo.tblPPM_Reconciliation has a PERSISTED computed column
-- (Variance), and SQL Server rejects any INSERT/UPDATE/DELETE on such a table unless
-- the executing module was created with QUOTED_IDENTIFIER ON (else Msg 1934).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  fn_FinhubBalance(@Period)
  Inline TVF - the live FINHUB (prepayment) outstanding balance per group + GL, i.e. the
  recognised prepayment asset less what has been amortised (exported) to date. Not period
  scoped for the asset itself (a prepayment asset is a running balance), but exposed as a
  function so the grid and detail panels share one definition.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION prepayment.fn_FinhubBalance()
RETURNS TABLE
AS
RETURN
(
    SELECT
        s.DeliveryGroupId,
        s.PrepaymentGlId,
        Recognised = SUM(CASE WHEN s.Status <> 'Draft' THEN s.TotalAmount ELSE 0 END),
        Amortised  = ISNULL((
                        SELECT SUM(p.Amount)
                        FROM   dbo.tblPPM_AmortisationPeriod p
                        WHERE  p.AmortisationScheduleId = s.Id
                          AND  p.Status = 'Exported' AND p.IsDeleted = 0
                     ), 0),
        Outstanding = SUM(CASE WHEN s.Status <> 'Draft' THEN s.TotalAmount ELSE 0 END)
                    - ISNULL((
                        SELECT SUM(p.Amount)
                        FROM   dbo.tblPPM_AmortisationPeriod p
                        WHERE  p.AmortisationScheduleId = s.Id
                          AND  p.Status = 'Exported' AND p.IsDeleted = 0
                     ), 0)
    FROM   dbo.tblPPM_AmortisationSchedule s
    WHERE  s.IsDeleted = 0
      AND  s.DeliveryGroupId IS NOT NULL
      AND  s.PrepaymentGlId  IS NOT NULL
    GROUP BY s.DeliveryGroupId, s.PrepaymentGlId, s.Id
);
GO

/*--------------------------------------------------------------------------------------
  Recon_SaveExtract
  Ingests one uploaded GL balance extract. @Balances is a JSON array of
    { "GroupCode":"DIG", "GlAccount":"514008", "OpeningBalance":80000,
      "PeriodDebit":120000, "PeriodCredit":0, "ClosingBalance":200000 }
  Writes a GlExtractFile header + a GlBalanceRecord per line, then (re)builds the
  Reconciliation rows for @Period from the SAP balances vs the live FINHUB balances.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_SaveExtract
    @SourceFileName NVARCHAR(260),
    @Period         VARCHAR(7),          -- 'YYYY/MM'
    @Balances       NVARCHAR(MAX),       -- JSON array (see above)
    @UserId         INT,
    @NewFileId      BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @year  INT = TRY_CONVERT(INT, LEFT(@Period, 4));
    DECLARE @month INT = TRY_CONVERT(INT, RIGHT(@Period, 2));
    DECLARE @defaultCompany BIGINT = (SELECT TOP 1 Id FROM dbo.tblPPM_CompanyCode WHERE IsDeleted = 0 ORDER BY Id);

    -- Parse the JSON once into a temp table, resolving group + GL + company-code ids.
    -- 3.6 CSV columns: Group, GL Account, Company Code, Opening, Period Dr, Period Cr, Closing, Extract Date.
    SELECT
        b.GroupCode, b.GlAccount,
        dg.Id  AS DeliveryGroupId,
        gl.Id  AS PrepaymentGlId,
        CompanyCodeId = ISNULL(cc.Id, @defaultCompany),
        b.OpeningBalance, b.PeriodDebit, b.PeriodCredit, b.ClosingBalance,
        ExtractDate = ISNULL(TRY_CONVERT(DATE, b.ExtractDate), CAST(SYSUTCDATETIME() AS DATE))
    INTO   #bal
    FROM   OPENJSON(@Balances)
           WITH (
               GroupCode      VARCHAR(20)   '$.GroupCode',
               GlAccount      VARCHAR(10)   '$.GlAccount',
               CompanyCode    VARCHAR(4)    '$.CompanyCode',
               OpeningBalance NUMERIC(18,2) '$.OpeningBalance',
               PeriodDebit    NUMERIC(18,2) '$.PeriodDebit',
               PeriodCredit   NUMERIC(18,2) '$.PeriodCredit',
               ClosingBalance NUMERIC(18,2) '$.ClosingBalance',
               ExtractDate    VARCHAR(20)   '$.ExtractDate'
           ) b
    LEFT JOIN dbo.tblPPM_DeliveryGroup       dg ON dg.DeliveryGroupCode = b.GroupCode   AND dg.IsDeleted = 0
    LEFT JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.GlAccount         = b.GlAccount   AND gl.IsDeleted = 0
    LEFT JOIN dbo.tblPPM_CompanyCode         cc ON cc.CompanyCode       = b.CompanyCode AND cc.IsDeleted = 0;

    IF NOT EXISTS (SELECT 1 FROM #bal)
    BEGIN
        RAISERROR('The uploaded file contained no balance rows.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

        INSERT dbo.tblPPM_GlExtractFile (SourceFileName, ReportingPeriod, ExtractDate, AccountCount, GroupCount, CreatedBy)
        SELECT @SourceFileName, @Period, CAST(SYSUTCDATETIME() AS DATE),
               COUNT(DISTINCT GlAccount), COUNT(DISTINCT GroupCode), @UserId
        FROM   #bal;
        SET @NewFileId = SCOPE_IDENTITY();

        INSERT dbo.tblPPM_GlBalanceRecord
            (GlExtractFileId, DeliveryGroupId, PrepaymentGlId, CompanyCodeId, FiscalYear, FiscalPeriod,
             OpeningBalance, PeriodDebit, PeriodCredit, ClosingBalance, ExtractDate, CreatedBy)
        SELECT @NewFileId, b.DeliveryGroupId, b.PrepaymentGlId, b.CompanyCodeId, @year, @month,
               b.OpeningBalance, b.PeriodDebit, b.PeriodCredit, b.ClosingBalance,
               b.ExtractDate, @UserId
        FROM   #bal b
        WHERE  b.PrepaymentGlId IS NOT NULL;   -- only rows that map to a real prepayment GL

        /* Rebuild the Reconciliation rows for this period from the just-loaded SAP balances
           vs the live FINHUB balances. UPSERT on the unique (group, GL, period) key. */
        ;WITH sap AS (
            SELECT b.DeliveryGroupId, b.PrepaymentGlId, SapBalance = SUM(b.ClosingBalance)
            FROM   dbo.tblPPM_GlBalanceRecord b
            WHERE  b.GlExtractFileId = @NewFileId AND b.IsDeleted = 0
            GROUP BY b.DeliveryGroupId, b.PrepaymentGlId
        ),
        fin AS (
            SELECT DeliveryGroupId, PrepaymentGlId, Finhub = SUM(Outstanding)
            FROM   prepayment.fn_FinhubBalance()
            GROUP BY DeliveryGroupId, PrepaymentGlId
        ),
        merged AS (
            SELECT
                DeliveryGroupId = ISNULL(sap.DeliveryGroupId, fin.DeliveryGroupId),
                PrepaymentGlId  = ISNULL(sap.PrepaymentGlId,  fin.PrepaymentGlId),
                SapBalance      = ISNULL(sap.SapBalance, 0),
                PrepaymentBal   = ISNULL(fin.Finhub, 0),
                HasSap          = CASE WHEN sap.DeliveryGroupId IS NOT NULL THEN 1 ELSE 0 END,
                HasFin          = CASE WHEN fin.DeliveryGroupId IS NOT NULL THEN 1 ELSE 0 END
            FROM   sap
            FULL OUTER JOIN fin
                   ON fin.DeliveryGroupId = sap.DeliveryGroupId
                  AND fin.PrepaymentGlId  = sap.PrepaymentGlId
            WHERE  ISNULL(sap.DeliveryGroupId, fin.DeliveryGroupId) IS NOT NULL
        )
        MERGE dbo.tblPPM_Reconciliation AS tgt
        USING (
            SELECT m.*,
                   -- a group/GL with an Approved (not yet Exported) journal is "journal pending"
                   PendingExport = CASE WHEN EXISTS (
                       SELECT 1 FROM dbo.tblPPM_Journal j
                       WHERE  j.DeliveryGroupId = m.DeliveryGroupId
                         AND  j.IsDeleted = 0 AND j.Status = 'Approved'
                   ) THEN 1 ELSE 0 END
            FROM merged m
        ) AS src
            ON  tgt.DeliveryGroupId = src.DeliveryGroupId
            AND tgt.PrepaymentGlId  = src.PrepaymentGlId
            AND tgt.Period          = @Period
            AND tgt.IsDeleted       = 0
        WHEN MATCHED THEN UPDATE SET
            tgt.SapBalance        = src.SapBalance,
            tgt.PrepaymentBalance = src.PrepaymentBal,
            tgt.GlExtractFileId   = @NewFileId,
            tgt.Status            = CASE
                                       WHEN src.HasSap = 0 OR src.HasFin = 0 THEN 'NotMatched'
                                       WHEN ABS(src.SapBalance - src.PrepaymentBal) <= 0.01 THEN 'Reconciled'
                                       WHEN src.PendingExport = 1 THEN 'JournalPending'
                                       ELSE 'Variance'
                                    END,
            tgt.ModifiedBy        = @UserId,
            tgt.ModifiedDate      = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT
            (DeliveryGroupId, PrepaymentGlId, Period, GlExtractFileId, SapBalance, PrepaymentBalance, Status, CreatedBy)
            VALUES (src.DeliveryGroupId, src.PrepaymentGlId, @Period, @NewFileId, src.SapBalance, src.PrepaymentBal,
                    CASE
                       WHEN src.HasSap = 0 OR src.HasFin = 0 THEN 'NotMatched'
                       WHEN ABS(src.SapBalance - src.PrepaymentBal) <= 0.01 THEN 'Reconciled'
                       WHEN src.PendingExport = 1 THEN 'JournalPending'
                       ELSE 'Variance'
                    END,
                    @UserId);

    COMMIT;

    DROP TABLE #bal;
END
GO

/*--------------------------------------------------------------------------------------
  Recon_GetGrid
  The reconciliation grid for @Period (defaults to the latest period that has an extract).
  @VariancesOnly = 1 returns only rows whose Status is Variance / NotMatched.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_GetGrid
    @Period        VARCHAR(7) = NULL,
    @VariancesOnly BIT        = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF @Period = '' SET @Period = NULL;
    IF @Period IS NULL
        SET @Period = (SELECT MAX(Period) FROM dbo.tblPPM_Reconciliation WHERE IsDeleted = 0);

    SELECT
        r.Id                       AS ReconciliationId,
        dg.DeliveryGroupCode,
        dg.GroupName,
        gl.GlAccount,
        gl.GlDescription,
        r.SapBalance,
        r.PrepaymentBalance,
        r.Variance,
        r.Status,
        r.Period
    FROM   dbo.tblPPM_Reconciliation r
    JOIN   dbo.tblPPM_DeliveryGroup       dg ON dg.Id = r.DeliveryGroupId
    LEFT   JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.Id = r.PrepaymentGlId
    WHERE  r.IsDeleted = 0
      AND  r.Period = @Period
      AND  (@VariancesOnly = 0 OR r.Status IN ('Variance','NotMatched'))
    ORDER BY dg.DeliveryGroupCode, gl.GlAccount;
END
GO

/*--------------------------------------------------------------------------------------
  Recon_GetKpis
  Five KPI cards for @Period (defaults to latest extract period).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_GetKpis
    @Period VARCHAR(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Period = '' SET @Period = NULL;
    IF @Period IS NULL
        SET @Period = (SELECT MAX(Period) FROM dbo.tblPPM_Reconciliation WHERE IsDeleted = 0);

    DECLARE @fileId BIGINT = (
        SELECT TOP 1 Id FROM dbo.tblPPM_GlExtractFile
        WHERE ReportingPeriod = @Period AND IsDeleted = 0 ORDER BY CreatedDate DESC, Id DESC);

    SELECT
        LastFileName  = (SELECT SourceFileName FROM dbo.tblPPM_GlExtractFile WHERE Id = @fileId),
        LastLoadedBy  = (SELECT ISNULL(u.DisplayName, u.WindowsAccount)
                         FROM dbo.tblPPM_GlExtractFile f
                         LEFT JOIN dbo.tblPPM_AppUser u ON u.Id = f.CreatedBy
                         WHERE f.Id = @fileId),
        LastLoadedDate = (SELECT CreatedDate FROM dbo.tblPPM_GlExtractFile WHERE Id = @fileId),
        GroupCount     = (SELECT GroupCount FROM dbo.tblPPM_GlExtractFile WHERE Id = @fileId),
        AccountCount   = (SELECT AccountCount FROM dbo.tblPPM_GlExtractFile WHERE Id = @fileId),

        TotalGroups        = (SELECT COUNT(*) FROM dbo.tblPPM_DeliveryGroup WHERE IsDeleted = 0 AND IsActive = 1),
        GroupsReconciled   = (SELECT COUNT(DISTINCT DeliveryGroupId) FROM dbo.tblPPM_Reconciliation
                              WHERE Period = @Period AND IsDeleted = 0 AND Status = 'Reconciled'),
        VariancesFound     = (SELECT COUNT(*) FROM dbo.tblPPM_Reconciliation
                              WHERE Period = @Period AND IsDeleted = 0 AND Status IN ('Variance','NotMatched')),
        TotalSapBalance    = (SELECT ISNULL(SUM(SapBalance), 0) FROM dbo.tblPPM_Reconciliation
                              WHERE Period = @Period AND IsDeleted = 0),
        TotalFinhubBalance = (SELECT ISNULL(SUM(PrepaymentBalance), 0) FROM dbo.tblPPM_Reconciliation
                              WHERE Period = @Period AND IsDeleted = 0),
        Period             = @Period;
END
GO

/*--------------------------------------------------------------------------------------
  Recon_GetVarianceDetail
  The investigation panels for one reconciliation row (3.6):
    Set 1 - GL extract detail (opening / Dr / Cr / closing + company + extract date)
    Set 2 - FINHUB record detail (per-invoice recognised amounts + amortised total +
            net balance + SAP balance + variance)
    Set 3 - the header row (codes, names, note, status, assignee) for the panel titles
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_GetVarianceDetail
    @ReconciliationId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @dg BIGINT, @gl BIGINT, @period VARCHAR(7), @fileId BIGINT;
    SELECT @dg = DeliveryGroupId, @gl = PrepaymentGlId, @period = Period, @fileId = GlExtractFileId
    FROM   dbo.tblPPM_Reconciliation WHERE Id = @ReconciliationId AND IsDeleted = 0;

    /* (1) GL extract detail */
    SELECT
        OpeningBalance = ISNULL(SUM(OpeningBalance), 0),
        PeriodDebit    = ISNULL(SUM(PeriodDebit), 0),
        PeriodCredit   = ISNULL(SUM(PeriodCredit), 0),
        ClosingBalance = ISNULL(SUM(ClosingBalance), 0),
        CompanyCode    = (SELECT TOP 1 c.CompanyCode FROM dbo.tblPPM_GlBalanceRecord br
                          LEFT JOIN dbo.tblPPM_CompanyCode c ON c.Id = br.CompanyCodeId
                          WHERE br.GlExtractFileId = @fileId AND br.DeliveryGroupId = @dg AND br.PrepaymentGlId = @gl),
        ExtractDate    = (SELECT TOP 1 ExtractDate FROM dbo.tblPPM_GlBalanceRecord
                          WHERE GlExtractFileId = @fileId AND DeliveryGroupId = @dg AND PrepaymentGlId = @gl)
    FROM   dbo.tblPPM_GlBalanceRecord
    WHERE  GlExtractFileId = @fileId AND DeliveryGroupId = @dg AND PrepaymentGlId = @gl AND IsDeleted = 0;

    /* (2a) FINHUB per-invoice recognised amounts (3.6: "per-invoice recognised amounts") */
    SELECT
        InvoiceNo  = i.InvoiceNo,
        Recognised = s.TotalAmount
    FROM   dbo.tblPPM_AmortisationSchedule s
    JOIN   dbo.tblPPM_Invoice i ON i.Id = s.InvoiceId AND i.IsDeleted = 0
    WHERE  s.IsDeleted = 0 AND s.Status <> 'Draft'
      AND  s.DeliveryGroupId = @dg AND s.PrepaymentGlId = @gl
    ORDER BY i.InvoiceNo;

    /* (2b) FINHUB record detail totals (live) */
    SELECT
        Recognised  = ISNULL(SUM(Recognised), 0),
        Amortised   = ISNULL(SUM(Amortised), 0),
        Outstanding = ISNULL(SUM(Outstanding), 0),
        SapBalance  = (SELECT SapBalance FROM dbo.tblPPM_Reconciliation WHERE Id = @ReconciliationId),
        Variance    = (SELECT Variance   FROM dbo.tblPPM_Reconciliation WHERE Id = @ReconciliationId)
    FROM   prepayment.fn_FinhubBalance()
    WHERE  DeliveryGroupId = @dg AND PrepaymentGlId = @gl;

    /* (3) header */
    SELECT
        r.Id, r.Period, r.Status, r.Variance, r.InvestigationNote,
        r.ResolutionAction, r.AssignedToUserId,
        AssignedTo = u.DisplayName,
        dg.DeliveryGroupCode, dg.GroupName,
        gl.GlAccount, gl.GlDescription
    FROM   dbo.tblPPM_Reconciliation r
    JOIN   dbo.tblPPM_DeliveryGroup       dg ON dg.Id = r.DeliveryGroupId
    LEFT   JOIN dbo.tblPPM_PrepaymentGlAccount gl ON gl.Id = r.PrepaymentGlId
    LEFT   JOIN dbo.tblPPM_AppUser u ON u.Id = r.AssignedToUserId
    WHERE  r.Id = @ReconciliationId;
END
GO

/*--------------------------------------------------------------------------------------
  Recon_GetUsers - active app users for the "assign to" picker.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_GetUsers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  Id,
            DisplayName = ISNULL(DisplayName, WindowsAccount)
    FROM    dbo.tblPPM_AppUser
    WHERE   IsDeleted = 0 AND IsActive = 1
    ORDER BY ISNULL(DisplayName, WindowsAccount);
END
GO

/*--------------------------------------------------------------------------------------
  Recon_GetPeriods - the distinct periods that have a reconciliation, newest first.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_GetPeriods
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PeriodKey   = Period,
        PeriodLabel = LEFT(DATENAME(MONTH, CONVERT(DATE, LEFT(Period,4) + '-' + RIGHT(Period,2) + '-01')), 3)
                      + ' ' + LEFT(Period,4)
    FROM   dbo.tblPPM_Reconciliation
    WHERE  IsDeleted = 0
    GROUP BY Period
    ORDER BY Period DESC;
END
GO

/*--------------------------------------------------------------------------------------
  Recon_Resolve - record an investigation outcome on a reconciliation row.
    @Action = 'MarkExplained' -> Status stays but the note + resolution are recorded
            = 'RaiseAdjustment' -> flag an adjustment was raised
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Recon_Resolve
    @ReconciliationId BIGINT,
    @Action           VARCHAR(30),
    @Note             NVARCHAR(1000) = NULL,
    @AssignedToUserId INT = NULL,        -- 0/NULL = leave the current assignee unchanged
    @UserId           INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tblPPM_Reconciliation
    SET    InvestigationNote = @Note,
           ResolutionAction  = @Action,
           Status            = CASE WHEN @Action = 'MarkExplained' THEN 'Reconciled' ELSE Status END,
           ResolvedDate      = SYSUTCDATETIME(),
           AssignedToUserId  = CASE WHEN ISNULL(@AssignedToUserId,0) > 0 THEN @AssignedToUserId ELSE AssignedToUserId END,
           ModifiedBy        = @UserId,
           ModifiedDate      = SYSUTCDATETIME()
    WHERE  Id = @ReconciliationId AND IsDeleted = 0;

    SELECT @@ROWCOUNT AS Updated;
END
GO

PRINT 'GL Reconciliation stored procedures created (fn_FinhubBalance, Recon_SaveExtract, Recon_GetGrid, Recon_GetKpis, Recon_GetVarianceDetail, Recon_GetPeriods, Recon_Resolve).';
GO
