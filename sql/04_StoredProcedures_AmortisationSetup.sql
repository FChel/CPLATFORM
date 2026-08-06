/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Prepayment & Amortisation Setup (Page 2)

  File   : 04_StoredProcedures_AmortisationSetup.sql
  Naming : prepayment.AmortisationSetup_<Action>
  Purpose: Reads + writes backing the AmortisationSetup user control / repository (Dapper).
           Implements 3.2 of prepayment-requirements-v2:
             - new invoices on prepayment-flagged PO lines (Source B)
             - existing prepayment balance invoices (Source C)
             - the prepayment GL account list (514xxx) for the setup panel
             - save setup -> generate amortisation periods -> generate journals (Page 3)
  Run after: 01_Schema, 02_StoredProcedures_PoIdentification
======================================================================================*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GetKpis - the five KPI cards (3.2).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GetKpis
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        -- New invoices on prepayment-flagged lines that still need setup. Matches the
        -- GetNewInvoices grid filter: the invoice's PO line must be flagged 'Prepayment'.
        NewInvoicesToReview = (
            SELECT COUNT(*)
            FROM dbo.tblPPM_Invoice inv
            JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = inv.PoDeliveryLineId
                                             AND l.IsDeleted = 0
                                             AND l.PrepaymentFlag = 'Prepayment'
            WHERE inv.IsDeleted = 0 AND inv.IsExistingBalance = 0
              AND inv.SetupStatus IN ('Pending','AmortisationNeeded','DraftInProgress','PendingClassification')),
        -- Existing prepayment balance invoices (pre-loaded register)
        ExistingBalanceInvoices = (
            SELECT COUNT(*) FROM dbo.tblPPM_Invoice
            WHERE IsDeleted = 0 AND IsExistingBalance = 1),
        -- Setups still pending input
        AmortisationSetupsPending = (
            SELECT COUNT(*) FROM dbo.tblPPM_Invoice
            WHERE IsDeleted = 0
              AND SetupStatus IN ('AmortisationNeeded','DraftInProgress','PendingClassification')),
        -- Active amortisation schedules
        SchedulesActive = (
            SELECT COUNT(*) FROM dbo.tblPPM_AmortisationSchedule
            WHERE IsDeleted = 0 AND Status = 'Active'),
        -- Total prepayment balance: recognised minus amortised across all schedules
        TotalPrepaymentBalance = (
            SELECT ISNULL(SUM(s.TotalAmount), 0)
                 - ISNULL((SELECT SUM(p.Amount) FROM dbo.tblPPM_AmortisationPeriod p
                           WHERE p.IsDeleted = 0 AND p.Status IN ('Exported','Posted')), 0)
            FROM dbo.tblPPM_AmortisationSchedule s
            WHERE s.IsDeleted = 0);
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GetNewInvoices - "New Invoices - vendor line item level" grid.
  Invoices on prepayment-flagged PO lines that are not yet fully set up.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GetNewInvoices
AS
BEGIN
    SET NOCOUNT ON;
    -- 4 (Page 1 -> Page 2): only invoices whose PO delivery line is flagged 'Prepayment' in
    -- Tab 1 appear here. Un-flagging the line in Tab 1 removes the invoice from this grid.
    -- The line link is therefore an INNER join with a flag filter (no LEFT join).
    SELECT
        InvoiceId    = inv.Id,
        InvoiceNo    = inv.InvoiceNo,
        PoNumber     = po.PoNumber,
        LineNumber   = l.LineNumber,
        Vendor       = v.VendorName,
        GlAccount    = inv.GlAccount,            -- prepayment GL (514xxx)
        CashGlAccount = inv.CashGlAccount,       -- offset / expense GL
        CapexOpex    = inv.CapexOpex,
        InvoiceDate  = inv.InvoiceDate,
        Amount       = inv.Amount,               -- AUD
        AmountDoc    = inv.AmountDoc,            -- document/foreign amount
        FxRate       = inv.FxRate,
        ForeignCurrency = inv.ForeignCurrency,
        Description  = inv.Description,
        Flag         = inv.Flag,
        SetupStatus  = inv.SetupStatus
    FROM dbo.tblPPM_Invoice inv
    JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = inv.PoDeliveryLineId
                                     AND l.IsDeleted = 0
                                     AND l.PrepaymentFlag = 'Prepayment'
    LEFT JOIN dbo.tblPPM_Vendor v ON v.Id = inv.VendorId
    WHERE inv.IsDeleted = 0 AND inv.IsExistingBalance = 0
    ORDER BY inv.InvoiceDate DESC, inv.InvoiceNo;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GetExistingBalanceInvoices - "Existing Prepayment Balance Invoices".
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GetExistingBalanceInvoices
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        InvoiceId        = inv.Id,
        InvoiceNo        = inv.InvoiceNo,
        PoNumber         = po.PoNumber,
        LineNumber       = l.LineNumber,
        Vendor           = v.VendorName,
        GlAccount        = inv.GlAccount,
        CapexOpex        = inv.CapexOpex,
        InvoiceDate      = inv.InvoiceDate,
        Amount           = inv.Amount,
        RecognisedAmount = ISNULL(s.TotalAmount, inv.Amount),
        AmortisedToDate  = ISNULL((SELECT SUM(p.Amount) FROM dbo.tblPPM_AmortisationPeriod p
                                   WHERE p.AmortisationScheduleId = s.Id AND p.IsDeleted = 0
                                     AND p.Status IN ('Exported','Posted')), 0),
        Periods          = s.Periods,
        ScheduleStatus   = s.Status
    FROM dbo.tblPPM_Invoice inv
    JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    LEFT JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = inv.PoDeliveryLineId
    LEFT JOIN dbo.tblPPM_Vendor v ON v.Id = inv.VendorId
    LEFT JOIN dbo.tblPPM_AmortisationSchedule s ON s.InvoiceId = inv.Id AND s.IsDeleted = 0
    WHERE inv.IsDeleted = 0 AND inv.IsExistingBalance = 1
    ORDER BY inv.InvoiceDate DESC, inv.InvoiceNo;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_ResolveInvoiceByPo - maps a PO number to the invoice the setup panel
  should open. Used by the "Open" action on the Tab 1 "Existing Prepayment POs" grid so
  the user lands on that PO's invoice rather than the page default. Prefers the prepayment
  invoice that still needs setup; falls back to the most recent invoice on the PO.
  Returns NULL InvoiceId when the PO has no invoice yet.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_ResolveInvoiceByPo
    @PoNumber VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) InvoiceId = inv.Id
    FROM dbo.tblPPM_Invoice inv
    JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    WHERE po.PoNumber = @PoNumber AND inv.IsDeleted = 0 AND po.IsDeleted = 0
    ORDER BY
        -- prefer an invoice still awaiting setup, then the newest
        CASE WHEN inv.SetupStatus <> 'Complete' THEN 0 ELSE 1 END,
        inv.InvoiceDate DESC, inv.Id DESC;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GetInvoiceDetail - header for the selected invoice's setup panel,
  including the original PO-line GL (to auto-suggest the expense GL).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GetInvoiceDetail
    @InvoiceId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        InvoiceId       = inv.Id,
        InvoiceNo       = inv.InvoiceNo,
        PoNumber        = po.PoNumber,
        LineNumber      = l.LineNumber,
        Vendor          = v.VendorName,
        Amount          = inv.Amount,
        AmountDoc       = inv.AmountDoc,
        FxRate          = inv.FxRate,
        ForeignCurrency = inv.ForeignCurrency,
        Description     = inv.Description,
        OriginalGl      = ISNULL(inv.CashGlAccount, ISNULL(l.GlAccount, inv.GlAccount)), -- offset/expense GL to suggest
        CashGlAccount   = inv.CashGlAccount,
        CapexOpex       = inv.CapexOpex,
        ProfitCentre    = inv.ProfitCentre,
        ProfitCentreDesc = inv.ProfitCentreDesc,
        WbsCostCentre   = ISNULL(inv.WbsElement, l.WbsCostCentre),
        CompanyCode     = cc.CompanyCode,
        DeliveryGroupId = po.DeliveryGroupId,
        DeliveryGroup   = dg.DeliveryGroupCode,
        SetupStatus     = inv.SetupStatus,
        -- existing schedule (if any) so the panel can pre-fill on re-open
        ScheduleId          = s.Id,
        AssetClassification = s.AssetClassification,
        ExpenditureType     = s.ExpenditureType,
        AmortisationType    = s.AmortisationType,
        StartDate           = s.StartDate,
        EndDate             = s.EndDate,
        Periods             = s.Periods,
        Frequency           = s.Frequency,
        PrepaymentGlId      = s.PrepaymentGlId,
        ExpenseGlAccount    = s.ExpenseGlAccount
    FROM dbo.tblPPM_Invoice inv
    JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    LEFT JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = inv.PoDeliveryLineId
    LEFT JOIN dbo.tblPPM_Vendor v ON v.Id = inv.VendorId
    LEFT JOIN dbo.tblPPM_CompanyCode cc ON cc.Id = po.CompanyCodeId
    LEFT JOIN dbo.tblPPM_DeliveryGroup dg ON dg.Id = po.DeliveryGroupId
    LEFT JOIN dbo.tblPPM_AmortisationSchedule s ON s.InvoiceId = inv.Id AND s.IsDeleted = 0
    WHERE inv.Id = @InvoiceId AND inv.IsDeleted = 0;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GetPrepaymentGlAccounts - the prepayment GL list (514xxx) for the setup dropdown.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GetPrepaymentGlAccounts
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PrepaymentGlId      = Id,
        GlAccount           = GlAccount,
        GlDescription       = GlDescription,
        AssetClassification = AssetClassification,
        ExpenditureType     = ExpenditureType
    FROM dbo.tblPPM_PrepaymentGlAccount
    WHERE IsDeleted = 0 AND IsActive = 1
    ORDER BY GlAccount;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GetScheduleForInvoice - generated amortisation periods for preview.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GetScheduleForInvoice
    @InvoiceId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PeriodId     = p.Id,
        PeriodNumber = p.PeriodNumber,
        PeriodDate   = p.PeriodDate,
        Amount       = p.Amount,
        Status       = p.Status
    FROM dbo.tblPPM_AmortisationPeriod p
    JOIN dbo.tblPPM_AmortisationSchedule s ON s.Id = p.AmortisationScheduleId
    WHERE s.InvoiceId = @InvoiceId AND s.IsDeleted = 0 AND p.IsDeleted = 0
    ORDER BY p.PeriodNumber;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_SaveDraft - persist classification + account assignment without
  generating journals (the "Save draft" button).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_SaveDraft
    @InvoiceId           BIGINT,
    @AssetClassification VARCHAR(15),
    @ExpenditureType     VARCHAR(15),
    @AmortisationType    VARCHAR(15),
    @StartDate           DATE = NULL,
    @EndDate             DATE = NULL,
    @Periods             INT = NULL,
    @Frequency           VARCHAR(15) = NULL,
    @PrepaymentGlId      BIGINT = NULL,
    @ExpenseGlAccount    VARCHAR(10) = NULL,
    @CostCentreWbs       VARCHAR(30) = NULL,
    @CompanyCode         VARCHAR(4) = NULL,
    @UserId              INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @amount NUMERIC(18,2) = (SELECT Amount FROM dbo.tblPPM_Invoice WHERE Id = @InvoiceId);
    DECLARE @dgId BIGINT = (SELECT po.DeliveryGroupId FROM dbo.tblPPM_Invoice i
                            JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId WHERE i.Id = @InvoiceId);
    DECLARE @ccId BIGINT = (SELECT Id FROM dbo.tblPPM_CompanyCode WHERE CompanyCode = @CompanyCode);
    DECLARE @perAmt NUMERIC(18,2) = CASE WHEN ISNULL(@Periods,0) > 0 THEN @amount / @Periods ELSE @amount END;

    IF EXISTS (SELECT 1 FROM dbo.tblPPM_AmortisationSchedule WHERE InvoiceId = @InvoiceId AND IsDeleted = 0)
        UPDATE dbo.tblPPM_AmortisationSchedule
        SET AssetClassification = @AssetClassification, ExpenditureType = @ExpenditureType,
            AmortisationType = @AmortisationType, StartDate = @StartDate, EndDate = @EndDate,
            Periods = @Periods, Frequency = @Frequency, PeriodAmount = @perAmt, TotalAmount = @amount,
            PrepaymentGlId = @PrepaymentGlId, ExpenseGlAccount = @ExpenseGlAccount,
            CostCentreWbs = @CostCentreWbs, CompanyCodeId = @ccId, DeliveryGroupId = @dgId,
            Status = 'Draft', ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
        WHERE InvoiceId = @InvoiceId AND IsDeleted = 0;
    ELSE
        INSERT dbo.tblPPM_AmortisationSchedule
            (InvoiceId, DeliveryGroupId, AssetClassification, ExpenditureType, AmortisationType,
             StartDate, EndDate, Periods, Frequency, PeriodAmount, TotalAmount, PrepaymentGlId,
             ExpenseGlAccount, CostCentreWbs, CompanyCodeId, Status, CreatedBy)
        VALUES (@InvoiceId, @dgId, @AssetClassification, @ExpenditureType, @AmortisationType,
             @StartDate, @EndDate, @Periods, @Frequency, @perAmt, @amount, @PrepaymentGlId,
             @ExpenseGlAccount, @CostCentreWbs, @ccId, 'Draft', @UserId);

    UPDATE dbo.tblPPM_Invoice
    SET SetupStatus = 'DraftInProgress', ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
    WHERE Id = @InvoiceId;

    SELECT ScheduleId = Id FROM dbo.tblPPM_AmortisationSchedule WHERE InvoiceId = @InvoiceId AND IsDeleted = 0;
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_GenerateScheduleAndJournals - the "Generate schedule & preview
  journals" button. Saves the schedule, builds equal-period amortisation rows, and
  generates the Recognition journal (capitalise) + per-period Amortisation journals,
  all created in 'Draft' status ready for submission on Page 3.

  Schedule total must equal the invoice amount (duplicate/over-allocation blocked).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_GenerateScheduleAndJournals
    @InvoiceId           BIGINT,
    @AssetClassification VARCHAR(15),
    @ExpenditureType     VARCHAR(15),
    @AmortisationType    VARCHAR(15),
    @StartDate           DATE,
    @EndDate             DATE = NULL,
    @Periods             INT,
    @Frequency           VARCHAR(15) = 'Monthly',
    @PrepaymentGlId      BIGINT,
    @ExpenseGlAccount    VARCHAR(10),
    @CostCentreWbs       VARCHAR(30) = NULL,
    @CompanyCode         VARCHAR(4) = NULL,
    @UserId              INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @amount NUMERIC(18,2) = (SELECT Amount FROM dbo.tblPPM_Invoice WHERE Id = @InvoiceId AND IsDeleted = 0);
    IF @amount IS NULL
    BEGIN
        RAISERROR('Invoice %I64d not found.', 16, 1, @InvoiceId); RETURN;
    END
    IF ISNULL(@Periods,0) <= 0
    BEGIN
        RAISERROR('Periods must be greater than zero.', 16, 1); RETURN;
    END

    DECLARE @dgId BIGINT = (SELECT po.DeliveryGroupId FROM dbo.tblPPM_Invoice i
                            JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId WHERE i.Id = @InvoiceId);
    DECLARE @ccId BIGINT = (SELECT Id FROM dbo.tblPPM_CompanyCode WHERE CompanyCode = @CompanyCode);
    DECLARE @prepayGl VARCHAR(10) = (SELECT GlAccount FROM dbo.tblPPM_PrepaymentGlAccount WHERE Id = @PrepaymentGlId);
    DECLARE @vendorId BIGINT = (SELECT VendorId FROM dbo.tblPPM_Invoice WHERE Id = @InvoiceId);
    DECLARE @invNo VARCHAR(30) = (SELECT InvoiceNo FROM dbo.tblPPM_Invoice WHERE Id = @InvoiceId);
    DECLARE @perAmt NUMERIC(18,2) = ROUND(@amount / @Periods, 2);
    DECLARE @period VARCHAR(7) = FORMAT(@StartDate, 'yyyy/MM');

    BEGIN TRY
        BEGIN TRAN;

        -- 1. Upsert the amortisation schedule header (Active once generated).
        DECLARE @schedId BIGINT;
        SELECT @schedId = Id FROM dbo.tblPPM_AmortisationSchedule WHERE InvoiceId = @InvoiceId AND IsDeleted = 0;
        IF @schedId IS NOT NULL
            UPDATE dbo.tblPPM_AmortisationSchedule
            SET AssetClassification = @AssetClassification, ExpenditureType = @ExpenditureType,
                AmortisationType = @AmortisationType, StartDate = @StartDate, EndDate = @EndDate,
                Periods = @Periods, Frequency = @Frequency, Basis = 'Equal monthly (straight-line)',
                PeriodAmount = @perAmt, TotalAmount = @amount, PrepaymentGlId = @PrepaymentGlId,
                ExpenseGlAccount = @ExpenseGlAccount, CostCentreWbs = @CostCentreWbs, CompanyCodeId = @ccId,
                DeliveryGroupId = @dgId, Status = 'Active', ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
            WHERE Id = @schedId;
        ELSE
        BEGIN
            INSERT dbo.tblPPM_AmortisationSchedule
                (InvoiceId, DeliveryGroupId, AssetClassification, ExpenditureType, AmortisationType,
                 StartDate, EndDate, Periods, Frequency, Basis, PeriodAmount, TotalAmount, PrepaymentGlId,
                 ExpenseGlAccount, CostCentreWbs, CompanyCodeId, Status, CreatedBy)
            VALUES (@InvoiceId, @dgId, @AssetClassification, @ExpenditureType, @AmortisationType,
                 @StartDate, @EndDate, @Periods, @Frequency, 'Equal monthly (straight-line)', @perAmt, @amount,
                 @PrepaymentGlId, @ExpenseGlAccount, @CostCentreWbs, @ccId, 'Active', @UserId);
            SET @schedId = SCOPE_IDENTITY();
        END

        -- 2. Clear existing journals before touching periods.
        --    Amortisation journals hold a FK on AmortisationPeriodId, so they must be deleted
        --    before the period rows - otherwise the period DELETE raises FK_Jnl_Period.
        --    Recognition journals: only Draft/Rejected are wiped so that a journal already
        --    submitted or approved survives the regeneration - this lets the Duplicate
        --    Recognition Blocked exception fire in the Admin Control Tower when a user
        --    regenerates a schedule after an earlier recognition journal has been submitted.
        DELETE ja FROM dbo.tblPPM_JournalAudit ja
            JOIN dbo.tblPPM_Journal j ON j.Id = ja.JournalId
            WHERE j.InvoiceId = @InvoiceId AND j.JournalType = 'Amortisation';
        DELETE je FROM dbo.tblPPM_JournalEntry je
            JOIN dbo.tblPPM_Journal j ON j.Id = je.JournalId
            WHERE j.InvoiceId = @InvoiceId AND j.JournalType = 'Amortisation';
        DELETE FROM dbo.tblPPM_Journal WHERE InvoiceId = @InvoiceId AND JournalType = 'Amortisation';

        DELETE ja FROM dbo.tblPPM_JournalAudit ja
            JOIN dbo.tblPPM_Journal j ON j.Id = ja.JournalId
            WHERE j.InvoiceId = @InvoiceId AND j.JournalType = 'Recognition'
              AND j.Status IN ('Draft','Rejected');
        DELETE je FROM dbo.tblPPM_JournalEntry je
            JOIN dbo.tblPPM_Journal j ON j.Id = je.JournalId
            WHERE j.InvoiceId = @InvoiceId AND j.JournalType = 'Recognition'
              AND j.Status IN ('Draft','Rejected');
        DELETE FROM dbo.tblPPM_Journal
            WHERE InvoiceId = @InvoiceId AND JournalType = 'Recognition'
              AND Status IN ('Draft','Rejected');

        -- 3. Rebuild the amortisation periods (equal split; last period absorbs rounding).
        DELETE FROM dbo.tblPPM_AmortisationPeriod WHERE AmortisationScheduleId = @schedId;
        ;WITH nums AS (
            SELECT TOP (@Periods) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n
            FROM sys.all_objects
        )
        INSERT dbo.tblPPM_AmortisationPeriod
            (AmortisationScheduleId, PeriodNumber, PeriodDate, FiscalYear, FiscalPeriod, Amount, CumulativeAmount, Status, CreatedBy)
        SELECT
            @schedId, n,
            DATEADD(MONTH, n-1, @StartDate),
            YEAR(DATEADD(MONTH, n-1, @StartDate)),
            MONTH(DATEADD(MONTH, n-1, @StartDate)),
            CASE WHEN n = @Periods THEN @amount - (@perAmt * (@Periods - 1)) ELSE @perAmt END,
            CASE WHEN n = @Periods THEN @amount ELSE @perAmt * n END,
            'Planned', @UserId
        FROM nums;

        -- 4. Generate the Recognition (capitalisation) journal - Dr prepayment asset / Cr expense.
        --    If a previous recognition journal survived the delete (i.e. it was already
        --    submitted/approved - the Blocked scenario), we must use a unique ref suffix so we
        --    do not violate UQ_Journal_Ref.
        DECLARE @survivingRecCount INT = (
            SELECT COUNT(*) FROM dbo.tblPPM_Journal
            WHERE InvoiceId = @InvoiceId AND JournalType = 'Recognition' AND IsDeleted = 0
        );
        DECLARE @recBase VARCHAR(25) = 'JRNL-REC-' + FORMAT(@StartDate,'MMyy') + '-' + RIGHT('000'+CAST(@schedId AS VARCHAR(10)),3);
        DECLARE @recRef VARCHAR(30) = CASE
            WHEN @survivingRecCount > 0
            THEN @recBase + '-' + RIGHT('0' + CAST(@survivingRecCount + 1 AS VARCHAR(5)), 2)
            ELSE @recBase
        END;

        INSERT dbo.tblPPM_Journal
            (JournalRef, JournalType, InvoiceId, AmortisationScheduleId, DeliveryGroupId,
             DrAccount, CrAccount, CostObject, Amount, PostingPeriod, Status, PreparerUserId, CreatedBy)
        VALUES (@recRef, 'Recognition', @InvoiceId, @schedId, @dgId,
             @prepayGl, @ExpenseGlAccount, @CostCentreWbs, @amount, @period, 'Draft', @UserId, @UserId);
        DECLARE @recJnl BIGINT = SCOPE_IDENTITY();

        INSERT dbo.tblPPM_JournalEntry (JournalId, DebitCredit, Account, Description, CostObject, Amount, CreatedBy)
        VALUES (@recJnl, 'Dr', @prepayGl, 'Prepayment asset', @CostCentreWbs, @amount, @UserId),
               (@recJnl, 'Cr', @ExpenseGlAccount, 'Expense (reversal)', @CostCentreWbs, @amount, @UserId);

        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
        VALUES (@recJnl, 'Created', @UserId, 'Generated from amortisation setup', @UserId);

        -- 5. Generate one Amortisation (expense) journal per period - Dr expense / Cr asset.

        DECLARE @running NUMERIC(18,2) = @amount;
        DECLARE @pn INT, @pAmt NUMERIC(18,2), @pDate DATE, @pId BIGINT;
        DECLARE pc CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, PeriodNumber, Amount, PeriodDate FROM dbo.tblPPM_AmortisationPeriod
            WHERE AmortisationScheduleId = @schedId ORDER BY PeriodNumber;
        OPEN pc; FETCH NEXT FROM pc INTO @pId, @pn, @pAmt, @pDate;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @running = @running - @pAmt;
            DECLARE @amrRef VARCHAR(30) = 'JRNL-AMR-' + FORMAT(@pDate,'MMyy') + '-' + RIGHT('000'+CAST(@schedId AS VARCHAR(10)),3) + '-' + RIGHT('00'+CAST(@pn AS VARCHAR(3)),2);
            INSERT dbo.tblPPM_Journal
                (JournalRef, JournalType, InvoiceId, AmortisationScheduleId, AmortisationPeriodId, DeliveryGroupId,
                 DrAccount, CrAccount, CostObject, Amount, PostingPeriod, RemainingBalance, Status, PreparerUserId, CreatedBy)
            VALUES (@amrRef, 'Amortisation', @InvoiceId, @schedId, @pId, @dgId,
                 @ExpenseGlAccount, @prepayGl, @CostCentreWbs, @pAmt, FORMAT(@pDate,'yyyy/MM'), @running, 'Draft', @UserId, @UserId);
            DECLARE @amrJnl BIGINT = SCOPE_IDENTITY();
            INSERT dbo.tblPPM_JournalEntry (JournalId, DebitCredit, Account, Description, CostObject, Amount, CreatedBy)
            VALUES (@amrJnl, 'Dr', @ExpenseGlAccount, 'Expense', @CostCentreWbs, @pAmt, @UserId),
                   (@amrJnl, 'Cr', @prepayGl, 'Prepayment asset', @CostCentreWbs, @pAmt, @UserId);
            FETCH NEXT FROM pc INTO @pId, @pn, @pAmt, @pDate;
        END
        CLOSE pc; DEALLOCATE pc;

        -- 5. Mark the invoice setup complete.
        UPDATE dbo.tblPPM_Invoice
        SET SetupStatus = 'Complete', ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
        WHERE Id = @InvoiceId;

        COMMIT;

        SELECT ScheduleId = @schedId, RecognitionJournalId = @recJnl, PeriodsCreated = @Periods;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

/*--------------------------------------------------------------------------------------
  AmortisationSetup_SavePeriodAmounts - persist manually-edited period amounts from the
  Tab 2 schedule table and update the schedule's TotalAmount to the actual entered sum.
  If TotalAmount <> Invoice.Amount the Admin Control Tower fires a "Schedule total
  mismatch" (Error) exception, making that exception dynamic.
  @PeriodsJson - JSON array: [{"Id": 101, "Amount": 1234.56}, ...]
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.AmortisationSetup_SavePeriodAmounts
    @InvoiceId   BIGINT,
    @PeriodsJson NVARCHAR(MAX),
    @UserId      INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Update each period's amount from the submitted JSON.
    UPDATE p
    SET p.Amount = j.Amount,
        p.ModifiedBy = @UserId,
        p.ModifiedDate = SYSUTCDATETIME()
    FROM dbo.tblPPM_AmortisationPeriod p
    JOIN OPENJSON(@PeriodsJson)
         WITH (Id BIGINT '$.Id', Amount NUMERIC(18,2) '$.Amount') j ON j.Id = p.Id
    WHERE p.IsDeleted = 0;

    -- Set TotalAmount = actual sum of all periods - may differ from Invoice.Amount,
    -- which is exactly the condition the Admin Error exception checks.
    UPDATE s
    SET s.TotalAmount = (
        SELECT SUM(p2.Amount)
        FROM dbo.tblPPM_AmortisationPeriod p2
        WHERE p2.AmortisationScheduleId = s.Id AND p2.IsDeleted = 0
    ),
        s.ModifiedBy = @UserId,
        s.ModifiedDate = SYSUTCDATETIME()
    FROM dbo.tblPPM_AmortisationSchedule s
    WHERE s.InvoiceId = @InvoiceId AND s.IsDeleted = 0;

    SELECT @@ROWCOUNT AS PeriodsUpdated;
END
GO

PRINT 'Amortisation Setup (Page 2) stored procedures created.';
GO
