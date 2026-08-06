/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Admin Control Tower ACTIONS (Tab 4 / 3.4)

  File   : 10_StoredProcedures_AdminActions.sql
  Purpose: The 3.4 "Admin actions" write paths + the read SPs that drive their pickers.
           3.4 requires: force-advance a stuck item, reassign approver, clear exception,
           re-export failed batch.

           Admin_GetActionTargets  - data-driven pickers: stuck items (POs whose furthest
                                      journal is Draft/Pending/Rejected), groups/journals
                                      pending approval (reassign), failed export batches,
                                      and the real (stored) ExceptionItem rows for clearing.
           Admin_ForceAdvance       - advance the stuck item one stage: Draft -> PendingApproval,
                                      PendingApproval -> Approved (admin override), Rejected ->
                                      PendingApproval (re-open). Logs a JournalAudit row.
           Admin_ReassignApprover   - set the approver on a PO's non-final journals.
           Admin_ResolveException   - clear/resolve a stored ExceptionItem (with a note).
           Admin_ReExportBatch      - reset a Failed export batch to Ready for re-export.

  Naming  : prepayment.Admin_<Action>
  Run after: 01_Schema, 05_StoredProcedures_Journal, 06_StoredProcedures_Admin. Data via Import tab.
======================================================================================*/

SET NOCOUNT ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  Admin_GetActionTargets
  Returns four result sets that populate the Admin-actions pickers (all data-driven):
    (1) Stuck items   - POs with a journal not yet Exported (Draft / PendingApproval /
                        Rejected), i.e. candidates for force-advance.
    (2) Approver pool - active Approver / Admin users for the reassign picker.
    (3) Failed batches - export batches in 'Failed' status for re-export.
    (4) Open exceptions - the real stored ExceptionItem rows (stable ids) for clearing.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_GetActionTargets
AS
BEGIN
    SET NOCOUNT ON;

    /* (1) Stuck items - one row per PO that has a non-exported journal. */
    SELECT DISTINCT
        po.PoNumber,
        Label = po.PoNumber + ' - ' + ISNULL(v.VendorName, '')
              + ' (' + MAX(j.Status) + ')'
    FROM   dbo.tblPPM_Journal j
    JOIN   dbo.tblPPM_PurchaseOrder po ON po.Id = (SELECT i.PurchaseOrderId FROM dbo.tblPPM_Invoice i WHERE i.Id = j.InvoiceId)
    LEFT   JOIN dbo.tblPPM_Vendor v ON v.Id = po.VendorId
    WHERE  j.IsDeleted = 0 AND j.Status IN ('Draft','PendingApproval','Rejected')
    GROUP BY po.PoNumber, v.VendorName
    ORDER BY po.PoNumber;

    /* (2) Approver pool. */
    SELECT  Id,
            DisplayName = ISNULL(DisplayName, WindowsAccount)
    FROM    dbo.tblPPM_AppUser
    WHERE   IsDeleted = 0 AND IsActive = 1
      AND   RoleName IN ('FinanceApprover','Admin')
    ORDER BY ISNULL(DisplayName, WindowsAccount);

    /* (3) Failed export batches. */
    SELECT  Id,
            BatchRef,
            Label = BatchRef + ' - ' + CAST(JournalCount AS VARCHAR(10)) + ' journal(s)'
    FROM    dbo.tblPPM_ExportBatch
    WHERE   IsDeleted = 0 AND Status = 'Failed'
    ORDER BY CreatedDate DESC;

    /* (4) Open stored exceptions (stable ids - these can be cleared). */
    SELECT  e.Id,
            e.Title,
            e.Detail,
            e.ExceptionType,
            e.Status
    FROM    dbo.tblPPM_ExceptionItem e
    WHERE   e.IsDeleted = 0 AND e.Status <> 'Resolved'
    ORDER BY e.CreatedDate DESC, e.Id DESC;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_ForceAdvance - advance the furthest journal(s) on a stuck PO one lifecycle stage.
    Draft           -> PendingApproval (submit on the preparer's behalf)
    Rejected        -> PendingApproval (re-open)
    PendingApproval -> Approved        (admin override approval)
  Returns the number of journals advanced.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_ForceAdvance
    @PoNumber VARCHAR(20),
    @UserId   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @poId BIGINT = (SELECT Id FROM dbo.tblPPM_PurchaseOrder WHERE PoNumber = @PoNumber AND IsDeleted = 0);
    IF @poId IS NULL BEGIN RAISERROR('Unknown PO number.', 16, 1); RETURN; END

    -- Journals on this PO (via their invoice) that are not yet Approved/Exported.
    DECLARE @jids TABLE (Id BIGINT, OldStatus VARCHAR(20), NewStatus VARCHAR(20));

    BEGIN TRAN;
        INSERT @jids (Id, OldStatus, NewStatus)
        SELECT j.Id, j.Status,
               CASE j.Status
                   WHEN 'Draft'           THEN 'PendingApproval'
                   WHEN 'Rejected'        THEN 'PendingApproval'
                   WHEN 'PendingApproval' THEN 'Approved'
               END
        FROM   dbo.tblPPM_Journal j
        JOIN   dbo.tblPPM_Invoice i ON i.Id = j.InvoiceId AND i.IsDeleted = 0
        WHERE  i.PurchaseOrderId = @poId
          AND  j.IsDeleted = 0
          AND  j.Status IN ('Draft','Rejected','PendingApproval');

        UPDATE j
        SET    j.Status         = t.NewStatus,
               j.ApprovedDate   = CASE WHEN t.NewStatus = 'Approved' THEN SYSUTCDATETIME() ELSE j.ApprovedDate END,
               j.RejectReasonCode = CASE WHEN t.OldStatus = 'Rejected' THEN NULL ELSE j.RejectReasonCode END,
               j.ModifiedBy     = @UserId,
               j.ModifiedDate   = SYSUTCDATETIME()
        FROM   dbo.tblPPM_Journal j
        JOIN   @jids t ON t.Id = j.Id;

        DECLARE @n INT = @@ROWCOUNT;

        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
        SELECT Id,
               CASE WHEN NewStatus = 'Approved' THEN 'Approved'
                    WHEN OldStatus = 'Rejected' THEN 'Reopened'
                    ELSE 'Submitted' END,
               @UserId,
               'Admin force-advance: ' + OldStatus + ' -> ' + NewStatus,
               @UserId
        FROM   @jids;
    COMMIT;

    SELECT @n AS Advanced;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_ReassignApprover - set a new approver on a PO's non-final journals (3.4).
  Returns the number of journals reassigned.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_ReassignApprover
    @PoNumber        VARCHAR(20),
    @ApproverUserId  INT,
    @UserId          INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @poId BIGINT = (SELECT Id FROM dbo.tblPPM_PurchaseOrder WHERE PoNumber = @PoNumber AND IsDeleted = 0);
    IF @poId IS NULL BEGIN RAISERROR('Unknown PO number.', 16, 1); RETURN; END
    IF NOT EXISTS (SELECT 1 FROM dbo.tblPPM_AppUser WHERE Id = @ApproverUserId AND IsDeleted = 0)
        BEGIN RAISERROR('Unknown approver.', 16, 1); RETURN; END

    BEGIN TRAN;
        UPDATE j
        SET    j.ApproverUserId = @ApproverUserId,
               j.ModifiedBy     = @UserId,
               j.ModifiedDate   = SYSUTCDATETIME()
        FROM   dbo.tblPPM_Journal j
        JOIN   dbo.tblPPM_Invoice i ON i.Id = j.InvoiceId AND i.IsDeleted = 0
        WHERE  i.PurchaseOrderId = @poId
          AND  j.IsDeleted = 0
          AND  j.Status IN ('Draft','PendingApproval','Rejected');
        DECLARE @n INT = @@ROWCOUNT;

        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
        SELECT j.Id, 'Reassigned', @UserId,
               'Admin reassigned approver to ' + ISNULL((SELECT ISNULL(DisplayName, WindowsAccount) FROM dbo.tblPPM_AppUser WHERE Id = @ApproverUserId), ''),
               @UserId
        FROM   dbo.tblPPM_Journal j
        JOIN   dbo.tblPPM_Invoice i ON i.Id = j.InvoiceId AND i.IsDeleted = 0
        WHERE  i.PurchaseOrderId = @poId AND j.IsDeleted = 0
          AND  j.Status IN ('Draft','PendingApproval','Rejected');
    COMMIT;

    SELECT @n AS Reassigned;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_ResolveException - clear a stored ExceptionItem with an investigation note (3.4).
  Returns 1 if a row was resolved, else 0.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_ResolveException
    @ExceptionId BIGINT,
    @Note        NVARCHAR(1000) = NULL,
    @UserId      INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tblPPM_ExceptionItem
    SET    Status         = 'Resolved',
           ResolutionNote = @Note,
           ResolvedDate   = SYSUTCDATETIME(),
           ModifiedBy     = @UserId,
           ModifiedDate   = SYSUTCDATETIME()
    WHERE  Id = @ExceptionId AND IsDeleted = 0 AND Status <> 'Resolved';

    SELECT @@ROWCOUNT AS Resolved;
END
GO

/*--------------------------------------------------------------------------------------
  Admin_ReExportBatch - reset a Failed export batch to Ready so it can be re-exported (3.4).
  Returns 1 if the batch was reset, else 0.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Admin_ReExportBatch
    @BatchId BIGINT,
    @UserId  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tblPPM_ExportBatch WHERE Id = @BatchId AND IsDeleted = 0 AND Status = 'Failed')
    BEGIN
        SELECT 0 AS ReExported; RETURN;
    END

    BEGIN TRAN;
        UPDATE dbo.tblPPM_ExportBatch
        SET    Status       = 'Ready',
               ModifiedBy   = @UserId,
               ModifiedDate = SYSUTCDATETIME()
        WHERE  Id = @BatchId;

        -- Re-arm the journals attached to the failed batch for export.
        UPDATE dbo.tblPPM_Journal
        SET    Status       = 'Approved',
               ExportBatchId = NULL,
               ModifiedBy   = @UserId,
               ModifiedDate = SYSUTCDATETIME()
        WHERE  ExportBatchId = @BatchId AND IsDeleted = 0 AND Status = 'Exported';
    COMMIT;

    SELECT 1 AS ReExported;
END
GO

PRINT 'Admin action stored procedures created (Admin_GetActionTargets, Admin_ForceAdvance, Admin_ReassignApprover, Admin_ResolveException, Admin_ReExportBatch).';
GO
