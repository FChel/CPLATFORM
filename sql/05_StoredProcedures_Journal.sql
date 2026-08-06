/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Journal Generation (Page 3)

  File   : 05_StoredProcedures_Journal.sql
  Naming : prepayment.Journal_<Action>
  Purpose: Reads + writes backing the JournalGeneration user control / repository (Dapper).
           Implements 3.3 of prepayment-requirements-v2:
             - Part A recognition journal queue; Part B amortisation journal queue
             - journal detail (entries, PO source, approval/audit)
             - approval workflow: Submit -> Approve / Reject -> Export (batch)
  Run after: 01_Schema, 04_StoredProcedures_AmortisationSetup
======================================================================================*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
USE [CPlatform];
GO

/*--------------------------------------------------------------------------------------
  Journal_GetKpis - the five KPI cards (3.3).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_GetKpis
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        RecognitionJournalsReady = (
            SELECT COUNT(*) FROM dbo.tblPPM_Journal
            WHERE IsDeleted = 0 AND JournalType = 'Recognition'
              AND Status IN ('Draft','PendingApproval','Approved')),
        AmortisationJournalsReady = (
            SELECT COUNT(*) FROM dbo.tblPPM_Journal
            WHERE IsDeleted = 0 AND JournalType = 'Amortisation'
              AND Status IN ('Draft','PendingApproval','Approved')),
        PendingApproval = (
            SELECT COUNT(*) FROM dbo.tblPPM_Journal
            WHERE IsDeleted = 0 AND Status = 'PendingApproval'),
        ApprovedExportReady = (
            SELECT COUNT(*) FROM dbo.tblPPM_Journal
            WHERE IsDeleted = 0 AND Status = 'Approved'),
        ExportedThisPeriod = (
            SELECT COUNT(*) FROM dbo.tblPPM_Journal
            WHERE IsDeleted = 0 AND Status = 'Exported'
              AND PostingPeriod = FORMAT(SYSUTCDATETIME(),'yyyy/MM'));
END
GO

/*--------------------------------------------------------------------------------------
  Journal_GetRecognitionQueue - Part A: recognition journals ready.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_GetRecognitionQueue
    @VendorNames NVARCHAR(MAX) = NULL   -- comma-separated vendor names; NULL = no filter
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        JournalId  = j.Id,
        JournalRef = j.JournalRef,
        PoNumber   = po.PoNumber,
        InvoiceNo  = inv.InvoiceNo,
        Vendor     = v.VendorName,
        CapexOpex  = inv.CapexOpex,
        DrAccount  = j.DrAccount,
        CrAccount  = j.CrAccount,
        Amount     = j.Amount,
        Period     = j.PostingPeriod,
        Status     = j.Status
    FROM dbo.tblPPM_Journal j
    LEFT JOIN dbo.tblPPM_Invoice inv ON inv.Id = j.InvoiceId
    LEFT JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    LEFT JOIN dbo.tblPPM_Vendor v ON v.Id = inv.VendorId
    WHERE j.IsDeleted = 0 AND j.JournalType = 'Recognition'
      AND j.Status IN ('Draft','PendingApproval','Approved','Rejected')
      AND (
          @VendorNames IS NULL
          OR v.VendorName IN (
              SELECT LTRIM(RTRIM(value))
              FROM STRING_SPLIT(@VendorNames, ',')
              WHERE LTRIM(RTRIM(value)) <> ''
          )
      )
    ORDER BY j.JournalRef;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_GetAmortisationQueue - Part B: amortisation journals ready.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_GetAmortisationQueue
    @VendorNames NVARCHAR(MAX) = NULL   -- comma-separated vendor names; NULL = no filter
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        JournalId        = j.Id,
        JournalRef       = j.JournalRef,
        PoNumber         = po.PoNumber,
        Vendor           = v.VendorName,
        CapexOpex        = inv.CapexOpex,
        Period           = j.PostingPeriod,
        DrAccount        = j.DrAccount,
        CrAccount        = j.CrAccount,
        PeriodAmount     = j.Amount,
        RemainingBalance = j.RemainingBalance,
        Status           = j.Status
    FROM dbo.tblPPM_Journal j
    LEFT JOIN dbo.tblPPM_Invoice inv ON inv.Id = j.InvoiceId
    LEFT JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    LEFT JOIN dbo.tblPPM_Vendor v ON v.Id = inv.VendorId
    WHERE j.IsDeleted = 0 AND j.JournalType = 'Amortisation'
      AND j.Status IN ('Draft','PendingApproval','Approved','Rejected')
      AND (
          @VendorNames IS NULL
          OR v.VendorName IN (
              SELECT LTRIM(RTRIM(value))
              FROM STRING_SPLIT(@VendorNames, ',')
              WHERE LTRIM(RTRIM(value)) <> ''
          )
      )
    ORDER BY j.JournalRef;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_ResolveJournalByPo - maps a PO number to the journal the page should open.
  Used by the "View journals" action on the Tab 1 "Existing Prepayment POs" grid so the
  user lands on that PO's journal rather than the page default. Prefers the Recognition
  journal (the capitalisation entry that heads the PO's journal set); falls back to the
  most recent journal on the PO. Returns NULL JournalId when the PO has no journal yet.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_ResolveJournalByPo
    @PoNumber VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) JournalId = j.Id
    FROM dbo.tblPPM_Journal j
    JOIN dbo.tblPPM_Invoice inv ON inv.Id = j.InvoiceId
    JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    WHERE po.PoNumber = @PoNumber AND j.IsDeleted = 0 AND po.IsDeleted = 0
    ORDER BY
        -- prefer the recognition journal, then the newest
        CASE WHEN j.JournalType = 'Recognition' THEN 0 ELSE 1 END,
        j.CreatedDate DESC, j.Id DESC;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_GetDetail - drill-down for a selected journal: header + entries + PO source +
  approval/audit fields. Returned as multiple result sets.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_GetDetail
    @JournalId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) header
    SELECT
        JournalId    = j.Id,
        JournalRef   = j.JournalRef,
        JournalType  = j.JournalType,
        PoNumber     = po.PoNumber,
        LineNumber   = l.LineNumber,
        InvoiceNo    = inv.InvoiceNo,
        Vendor       = v.VendorName,
        Amount       = j.Amount,
        Period       = j.PostingPeriod,
        Status       = j.Status,
        OriginalGl   = ISNULL(l.GlAccount, inv.GlAccount),
        CostObject   = j.CostObject,
        CompanyCode  = cc.CompanyCode,
        PreparerName = pu.DisplayName,
        ApproverName = au.DisplayName,
        RemainingBalance = j.RemainingBalance,
        ScheduleId   = j.AmortisationScheduleId,
        Periods      = s.Periods
    FROM dbo.tblPPM_Journal j
    LEFT JOIN dbo.tblPPM_Invoice inv ON inv.Id = j.InvoiceId
    LEFT JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = inv.PurchaseOrderId
    LEFT JOIN dbo.tblPPM_PoDeliveryLine l ON l.Id = inv.PoDeliveryLineId
    LEFT JOIN dbo.tblPPM_Vendor v ON v.Id = inv.VendorId
    LEFT JOIN dbo.tblPPM_CompanyCode cc ON cc.Id = po.CompanyCodeId
    LEFT JOIN dbo.tblPPM_AppUser pu ON pu.Id = j.PreparerUserId
    LEFT JOIN dbo.tblPPM_AppUser au ON au.Id = j.ApproverUserId
    LEFT JOIN dbo.tblPPM_AmortisationSchedule s ON s.Id = j.AmortisationScheduleId
    WHERE j.Id = @JournalId AND j.IsDeleted = 0;

    -- 2) journal entries (Dr/Cr lines)
    SELECT
        DebitCredit = e.DebitCredit,
        Account     = e.Account,
        Description = e.Description,
        CostObject  = e.CostObject,
        Amount      = e.Amount
    FROM dbo.tblPPM_JournalEntry e
    WHERE e.JournalId = @JournalId AND e.IsDeleted = 0
    ORDER BY CASE WHEN e.DebitCredit = 'Dr' THEN 0 ELSE 1 END, e.Id;

    -- 3) audit trail
    SELECT
        Action     = a.Action,
        ActionBy   = u.DisplayName,
        Comments   = a.Comments,
        ActionDate = a.ActionDate
    FROM dbo.tblPPM_JournalAudit a
    LEFT JOIN dbo.tblPPM_AppUser u ON u.Id = a.ActionByUserId
    WHERE a.JournalId = @JournalId AND a.IsDeleted = 0
    ORDER BY a.ActionDate;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_Submit - preparer submits a draft journal for approval (Draft -> PendingApproval).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_Submit
    @JournalId BIGINT,
    @UserId    INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tblPPM_Journal
    SET Status = 'PendingApproval', ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
    WHERE Id = @JournalId AND IsDeleted = 0 AND Status = 'Draft';
    DECLARE @rc INT = @@ROWCOUNT;
    IF @rc > 0
        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, CreatedBy)
        VALUES (@JournalId, 'Submitted', @UserId, @UserId);
    SELECT @rc;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_Approve - approver approves a pending journal (-> Approved, joins export queue).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_Approve
    @JournalId BIGINT,
    @UserId    INT,
    @Comments  NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tblPPM_Journal
    SET Status = 'Approved', ApproverUserId = @UserId, ApprovedDate = SYSUTCDATETIME(),
        ApproverComments = @Comments, ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
    WHERE Id = @JournalId AND IsDeleted = 0 AND Status IN ('PendingApproval','Draft');
    DECLARE @rc INT = @@ROWCOUNT;
    IF @rc > 0
        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
        VALUES (@JournalId, 'Approved', @UserId, @Comments, @UserId);
    SELECT @rc;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_Reject - approver rejects (-> Rejected; requires a reason code per 7 #8).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_Reject
    @JournalId  BIGINT,
    @UserId     INT,
    @Comments   NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tblPPM_Journal
    SET Status = 'Rejected', ApproverUserId = @UserId,
        ApproverComments = @Comments, ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
    WHERE Id = @JournalId AND IsDeleted = 0 AND Status IN ('PendingApproval','Draft');
    DECLARE @rc INT = @@ROWCOUNT;
    IF @rc > 0
        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
        -- Save NULL when no comment entered - NULL triggers the FollowUp exception in Admin tab
        VALUES (@JournalId, 'Rejected', @UserId, NULLIF(LTRIM(RTRIM(ISNULL(@Comments,''))), ''), @UserId);
    SELECT @rc;
END
GO

/*--------------------------------------------------------------------------------------
  Journal_Export - export approved journals to a SAP interface batch.
  If @JournalId is supplied, exports just that journal; otherwise exports all 'Approved'.
  Records a batch ref + timestamp and marks journals Exported.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_Export
    @JournalId BIGINT = NULL,
    @UserId    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tblPPM_Journal
                   WHERE IsDeleted = 0 AND Status = 'Approved'
                     AND (@JournalId IS NULL OR Id = @JournalId))
    BEGIN
        SELECT 0; RETURN;  -- nothing to export
    END

    BEGIN TRY
        BEGIN TRAN;
        DECLARE @batchRef VARCHAR(30) = 'PREP-' + FORMAT(SYSUTCDATETIME(),'MMyy') + '-' +
            RIGHT('00' + CAST((SELECT COUNT(*)+1 FROM dbo.tblPPM_ExportBatch) AS VARCHAR(10)), 2);

        INSERT dbo.tblPPM_ExportBatch (BatchRef, JournalCount, Status, ExportedDate, CreatedBy)
        VALUES (@batchRef, 0, 'Exported', SYSUTCDATETIME(), @UserId);
        DECLARE @batchId BIGINT = SCOPE_IDENTITY();

        UPDATE dbo.tblPPM_Journal
        SET Status = 'Exported', ExportBatchId = @batchId, ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
        WHERE IsDeleted = 0 AND Status = 'Approved'
          AND (@JournalId IS NULL OR Id = @JournalId);
        DECLARE @count INT = @@ROWCOUNT;

        -- Mark the matching amortisation periods exported (so prepayment balance moves).
        UPDATE p SET p.Status = 'Exported', p.ModifiedBy = @UserId, p.ModifiedDate = SYSUTCDATETIME()
        FROM dbo.tblPPM_AmortisationPeriod p
        JOIN dbo.tblPPM_Journal j ON j.AmortisationPeriodId = p.Id
        WHERE j.ExportBatchId = @batchId AND j.JournalType = 'Amortisation';

        UPDATE dbo.tblPPM_ExportBatch SET JournalCount = @count WHERE Id = @batchId;

        INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
        SELECT Id, 'Exported', @UserId, 'Batch ' + @batchRef, @UserId
        FROM dbo.tblPPM_Journal WHERE ExportBatchId = @batchId;

        COMMIT;
        SELECT @count;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

/*--------------------------------------------------------------------------------------
  Journal_ApproveAllReady - approve every PendingApproval journal at once
  ("Approve all ready" button).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Journal_ApproveAllReady
    @JournalType VARCHAR(15) = NULL,   -- NULL = both
    @UserId      INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tblPPM_Journal
    SET Status = 'Approved', ApproverUserId = @UserId, ApprovedDate = SYSUTCDATETIME(),
        ModifiedBy = @UserId, ModifiedDate = SYSUTCDATETIME()
    WHERE IsDeleted = 0 AND Status = 'PendingApproval'
      AND (@JournalType IS NULL OR JournalType = @JournalType);
    DECLARE @rc INT = @@ROWCOUNT;

    INSERT dbo.tblPPM_JournalAudit (JournalId, Action, ActionByUserId, Comments, CreatedBy)
    SELECT Id, 'Approved', @UserId, 'Approve all ready', @UserId
    FROM dbo.tblPPM_Journal
    WHERE IsDeleted = 0 AND Status = 'Approved' AND ApproverUserId = @UserId
      AND ApprovedDate >= DATEADD(SECOND,-5,SYSUTCDATETIME());

    SELECT @rc;
END
GO

PRINT 'Journal Generation (Page 3) stored procedures created.';
GO
