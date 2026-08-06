/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Tab 1 (PO Identification)

  File   : 02_StoredProcedures_Tab1.sql
  Purpose: Read + write procedures backing the Tab 1 user control / repository (Dapper).
           Column names match the C# entity properties exactly (DB-first).
  Run after: 01_Schema_PrepaymentManagement.sql
======================================================================================*/

SET NOCOUNT ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  Tab1_GetKpis - the five KPI cards at the top of Tab 1.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_GetKpis
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @today DATE =
        (SELECT MAX(SourceLoadDate) FROM dbo.tblPPM_PurchaseOrder WHERE IsDeleted = 0);

    SELECT
        -- New POs loaded on the latest daily load
        NewPosToday = (
            SELECT COUNT(*) FROM dbo.tblPPM_PurchaseOrder
            WHERE IsDeleted = 0 AND SourceLoadDate = @today),
        VendorCountToday = (
            SELECT COUNT(DISTINCT VendorId) FROM dbo.tblPPM_PurchaseOrder
            WHERE IsDeleted = 0 AND SourceLoadDate = @today),
        -- Lines flagged as prepayment (across all loaded POs)
        FlaggedAsPrepayment = (
            SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine
            WHERE IsDeleted = 0 AND PrepaymentFlag = 'Prepayment'),
        FlaggedVendorCount = (
            SELECT COUNT(DISTINCT po.VendorId)
            FROM dbo.tblPPM_PoDeliveryLine l
            JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = l.PurchaseOrderId
            WHERE l.IsDeleted = 0 AND l.PrepaymentFlag = 'Prepayment'),
        -- Lines still awaiting a decision
        AwaitingReview = (
            SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine
            WHERE IsDeleted = 0 AND PrepaymentFlag = 'Pending'),
        NotPrepayment = (
            SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine
            WHERE IsDeleted = 0 AND PrepaymentFlag = 'NotPrepayment'),
        -- Total commitment value of currently loaded POs
        TotalCommitmentValue = (
            SELECT ISNULL(SUM(TotalValue), 0) FROM dbo.tblPPM_PurchaseOrder
            WHERE IsDeleted = 0);
END
GO

/*--------------------------------------------------------------------------------------
  Tab1_SearchPurchaseOrders - the "Search Results - New Commitment Lines" grid.
  All params optional (NULL = no filter). LIKE on text fields.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_SearchPurchaseOrders
    @PoNumber          VARCHAR(20)  = NULL,
    @VendorName        NVARCHAR(200) = NULL,
    @ProjectCode       VARCHAR(30)  = NULL,
    @DeliveryGroupCode VARCHAR(20)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- "New Commitment Lines" = POs from the most recent daily load. When the user supplies
    -- any explicit filter we search across all loads instead (so older POs can be found).
    DECLARE @hasFilter BIT =
        CASE WHEN @PoNumber IS NOT NULL OR @VendorName IS NOT NULL
               OR @ProjectCode IS NOT NULL OR @DeliveryGroupCode IS NOT NULL
             THEN 1 ELSE 0 END;
    DECLARE @latestLoad DATE =
        (SELECT MAX(SourceLoadDate) FROM dbo.tblPPM_PurchaseOrder WHERE IsDeleted = 0);

    SELECT
        PoId          = po.Id,
        PoNumber      = po.PoNumber,
        Vendor        = v.VendorName,
        Project       = po.ProjectCode,          -- now carries the WBS element
        Wbs           = po.ProjectCode,
        DeliveryGroup = dg.DeliveryGroupCode,
        DeliveryGroupName = dg.GroupName,
        CapexOpex     = po.CapexOpex,
        CapabilityManager = capm.ManagerDesc,
        DeliveryManager   = delm.ManagerDesc,
        ManagerProgram    = capm.Program,
        PoValue       = po.TotalValue,
        CurrentCommitment = po.CurrentCommitment,
        TotalCommitment   = po.TotalCommitment,
        Currency      = po.CurrencyCode,
        PoDate        = po.PoDate,
        LinesCount    = po.LinesCount,
        UnreviewedLines = (
            SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine l
            WHERE l.PurchaseOrderId = po.Id AND l.IsDeleted = 0 AND l.PrepaymentFlag = 'Pending'),
        FlaggedLines = (
            SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine l
            WHERE l.PurchaseOrderId = po.Id AND l.IsDeleted = 0 AND l.PrepaymentFlag = 'Prepayment')
    FROM dbo.tblPPM_PurchaseOrder po
    LEFT JOIN dbo.tblPPM_Vendor v        ON v.Id  = po.VendorId
    LEFT JOIN dbo.tblPPM_DeliveryGroup dg ON dg.Id = po.DeliveryGroupId
    LEFT JOIN dbo.tblPPM_Manager capm    ON capm.Id = po.CapabilityMgrId
    LEFT JOIN dbo.tblPPM_Manager delm    ON delm.Id = po.DeliveryMgrId
    WHERE po.IsDeleted = 0
      AND (@hasFilter = 1 OR po.SourceLoadDate = @latestLoad)  -- unfiltered => newest load only
      AND (@PoNumber          IS NULL OR po.PoNumber   LIKE '%' + @PoNumber + '%')
      AND (@VendorName        IS NULL OR v.VendorName  LIKE '%' + @VendorName + '%')
      AND (@ProjectCode       IS NULL OR po.ProjectCode LIKE '%' + @ProjectCode + '%')  -- WBS element
      AND (@DeliveryGroupCode IS NULL
           OR dg.DeliveryGroupCode LIKE '%' + @DeliveryGroupCode + '%'
           OR dg.GroupName        LIKE '%' + @DeliveryGroupCode + '%')
    ORDER BY po.PoDate DESC, po.PoNumber;
END
GO

/*--------------------------------------------------------------------------------------
  Tab1_GetDeliverySchedule - header (1 row) + lines (N rows) for one PO.
  Returned as two result sets (Dapper QueryMultiple).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_GetDeliverySchedule
    @PoId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: header
    SELECT
        PoId          = po.Id,
        PoNumber      = po.PoNumber,
        Vendor        = v.VendorName,
        DeliveryGroup = dg.DeliveryGroupCode,
        Project       = po.ProjectCode,          -- WBS element
        CapexOpex     = po.CapexOpex,
        CapabilityManager = capm.ManagerDesc,
        DeliveryManager   = delm.ManagerDesc,
        Currency      = po.CurrencyCode,
        TotalValue    = po.TotalValue,
        LineCount     = (SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine l
                         WHERE l.PurchaseOrderId = po.Id AND l.IsDeleted = 0),
        LinesNeedingClassification = (SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine l
                         WHERE l.PurchaseOrderId = po.Id AND l.IsDeleted = 0 AND l.PrepaymentFlag = 'Pending')
    FROM dbo.tblPPM_PurchaseOrder po
    LEFT JOIN dbo.tblPPM_Vendor v        ON v.Id  = po.VendorId
    LEFT JOIN dbo.tblPPM_DeliveryGroup dg ON dg.Id = po.DeliveryGroupId
    LEFT JOIN dbo.tblPPM_Manager capm    ON capm.Id = po.CapabilityMgrId
    LEFT JOIN dbo.tblPPM_Manager delm    ON delm.Id = po.DeliveryMgrId
    WHERE po.Id = @PoId AND po.IsDeleted = 0;

    -- Result set 2: lines
    SELECT
        Id               = l.Id,
        PurchaseOrderId  = l.PurchaseOrderId,
        LineNumber       = l.LineNumber,
        AcctAssignNumber = l.AcctAssignNumber,
        Description      = l.Description,
        ServiceNote      = l.ServiceNote,
        GlAccount        = l.GlAccount,
        GlDescription    = l.GlDescription,
        WbsCostCentre    = l.WbsCostCentre,
        WbsDescription   = l.WbsDescription,
        CapexOpex        = l.CapexOpex,
        ScheduledDate    = l.ScheduledDate,
        Quantity         = l.Quantity,
        OpenQuantity     = l.OpenQuantity,
        UnitPrice        = l.UnitPrice,
        LineValue        = l.LineValue,
        PrepaymentFlag   = l.PrepaymentFlag,
        FlagNote         = l.FlagNote
    FROM dbo.tblPPM_PoDeliveryLine l
    WHERE l.PurchaseOrderId = @PoId AND l.IsDeleted = 0
    ORDER BY l.LineNumber, l.AcctAssignNumber;
END
GO

/*--------------------------------------------------------------------------------------
  Tab1_GetExistingPrepaymentPos - "Existing Prepayment POs (previously flagged)" grid.
  Aggregates recognised / outstanding from the amortisation schedule + periods.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_GetExistingPrepaymentPos
    @VendorNames NVARCHAR(MAX) = NULL   -- comma-separated vendor names; NULL = no filter
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        PoId          = po.Id,
        PoNumber      = po.PoNumber,
        Vendor        = v.VendorName,
        DeliveryGroup = dg.DeliveryGroupCode,
        RecognisedAmount   = ISNULL(SUM(s.TotalAmount), 0),
        OutstandingBalance = ISNULL(SUM(s.TotalAmount), 0)
                           - ISNULL(SUM(amort.AmortisedToDate), 0),
        AmortisationStatus = MAX(
            CASE
                WHEN s.Status = 'Completed' THEN 'Ready for export'
                WHEN s.Status = 'Active'    THEN 'Amortising'
                WHEN s.Status = 'Draft'     THEN 'Pending approval'
                ELSE s.Status
            END)
    FROM dbo.tblPPM_PurchaseOrder po
    LEFT JOIN dbo.tblPPM_Vendor v         ON v.Id  = po.VendorId
    LEFT JOIN dbo.tblPPM_DeliveryGroup dg ON dg.Id = po.DeliveryGroupId
    JOIN dbo.tblPPM_Invoice inv           ON inv.PurchaseOrderId = po.Id AND inv.IsDeleted = 0
    JOIN dbo.tblPPM_AmortisationSchedule s ON s.InvoiceId = inv.Id AND s.IsDeleted = 0
    OUTER APPLY (
        SELECT AmortisedToDate = SUM(p.Amount)
        FROM dbo.tblPPM_AmortisationPeriod p
        WHERE p.AmortisationScheduleId = s.Id AND p.IsDeleted = 0
          AND p.Status IN ('Exported','Posted')
    ) amort
    WHERE po.IsDeleted = 0
      AND (
          @VendorNames IS NULL
          OR v.VendorName IN (
              SELECT LTRIM(RTRIM(value))
              FROM STRING_SPLIT(@VendorNames, ',')
              WHERE LTRIM(RTRIM(value)) <> ''
          )
      )
    GROUP BY po.Id, po.PoNumber, v.VendorName, dg.DeliveryGroupCode
    ORDER BY po.PoNumber;
END
GO

/*--------------------------------------------------------------------------------------
  Tab1_UpdateLineFlag - write: set the prepayment flag (+ note) on one delivery line.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_UpdateLineFlag
    @DeliveryLineId BIGINT,
    @PrepaymentFlag VARCHAR(15),
    @Note           NVARCHAR(300) = NULL,
    @UserId         INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @PrepaymentFlag NOT IN ('Prepayment','NotPrepayment','Pending')
    BEGIN
        RAISERROR('Invalid PrepaymentFlag value "%s".', 16, 1, @PrepaymentFlag);
        RETURN;
    END

    UPDATE dbo.tblPPM_PoDeliveryLine
    SET PrepaymentFlag = @PrepaymentFlag,
        FlagNote       = @Note,
        FlaggedByUserId = @UserId,
        FlaggedDate    = SYSUTCDATETIME(),
        ModifiedBy     = @UserId,
        ModifiedDate   = SYSUTCDATETIME()
    WHERE Id = @DeliveryLineId AND IsDeleted = 0;

    SELECT @@ROWCOUNT;  -- rows affected
END
GO

/*--------------------------------------------------------------------------------------
  Tab1_ConfirmAndAdvance - confirm a PO's classification and push the flagged lines to Page 2.
  4 (Page 1 -> Page 2 / Page 5).

  Rules:
   * BLOCK if any delivery line is still 'Pending' (Page 1 must classify every line first).
     Nothing is changed; returns Status='Blocked' with the pending count so the UI can warn.
   * Otherwise: for every line flagged 'Prepayment', flag its matched invoice so it surfaces
     in the Tab 2 "New Invoices" grid (SetupStatus -> 'AmortisationNeeded' if not already
     set up), and advance the delivery group's workflow stage (PoFlagged -> InvoiceMatched).

  Returns one row: Status ('Ok' | 'Blocked' | 'NothingFlagged'), Flagged, Pending, InvoicesLinked.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_ConfirmAndAdvance
    @PoId   BIGINT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @flagged INT =
        (SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine
         WHERE PurchaseOrderId = @PoId AND IsDeleted = 0 AND PrepaymentFlag = 'Prepayment');
    DECLARE @pending INT =
        (SELECT COUNT(*) FROM dbo.tblPPM_PoDeliveryLine
         WHERE PurchaseOrderId = @PoId AND IsDeleted = 0 AND PrepaymentFlag = 'Pending');

    -- Gate 1: every line must be classified (no 'Pending' allowed).
    IF @pending > 0
    BEGIN
        SELECT Status = 'Blocked', Flagged = @flagged, Pending = @pending, InvoicesLinked = 0;
        RETURN;
    END

    -- Gate 2: nothing to do if no line was flagged as a prepayment.
    IF @flagged = 0
    BEGIN
        SELECT Status = 'NothingFlagged', Flagged = 0, Pending = 0, InvoicesLinked = 0;
        RETURN;
    END

    BEGIN TRY
        BEGIN TRAN;

        -- Push to Page 2: any invoice matched to a now-confirmed prepayment line becomes an
        -- actionable "new invoice". Only nudge invoices that still need setup.
        UPDATE inv
        SET inv.Flag = 'Prepayment',
            inv.SetupStatus = CASE WHEN inv.SetupStatus IN ('Pending','PendingClassification')
                                   THEN 'AmortisationNeeded' ELSE inv.SetupStatus END,
            inv.ModifiedBy = @UserId, inv.ModifiedDate = SYSUTCDATETIME()
        FROM dbo.tblPPM_Invoice inv
        JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = inv.PoDeliveryLineId
        WHERE l.PurchaseOrderId = @PoId AND l.IsDeleted = 0
          AND l.PrepaymentFlag = 'Prepayment'
          AND inv.IsDeleted = 0 AND inv.IsExistingBalance = 0;
        DECLARE @linked INT = @@ROWCOUNT;

        -- Page 1 -> Page 5: advance the group's workflow stage (upsert).
        DECLARE @groupId BIGINT = (SELECT DeliveryGroupId FROM dbo.tblPPM_PurchaseOrder WHERE Id = @PoId);
        DECLARE @period VARCHAR(7) = FORMAT(SYSUTCDATETIME(), 'yyyy/MM');
        IF @groupId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.tblPPM_GroupWorkflowState
                       WHERE DeliveryGroupId = @groupId AND Period = @period)
                UPDATE dbo.tblPPM_GroupWorkflowState
                SET PoFlagComplete = 1,
                    CurrentStage = CASE WHEN CurrentStage = 'PoFlagged' THEN 'InvoiceMatched' ELSE CurrentStage END,
                    ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
                WHERE DeliveryGroupId = @groupId AND Period = @period;
            ELSE
                INSERT dbo.tblPPM_GroupWorkflowState
                    (DeliveryGroupId, Period, CurrentStage, Status, PoFlagComplete, CreatedBy)
                VALUES (@groupId, @period, 'InvoiceMatched', 'OnTrack', 1, @UserId);
        END

        COMMIT;
        SELECT Status = 'Ok', Flagged = @flagged, Pending = 0, InvoicesLinked = @linked;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

/*--------------------------------------------------------------------------------------
  PoIdentification_GetDeliveryGroups - distinct active groups for the Tab 1 search dropdown.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.PoIdentification_GetDeliveryGroups
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Code = DeliveryGroupCode, Name = GroupName
    FROM dbo.tblPPM_DeliveryGroup
    WHERE IsDeleted = 0 AND IsActive = 1
    ORDER BY DeliveryGroupCode;
END
GO

PRINT 'PO Identification stored procedures created.';
GO
