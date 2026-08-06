/*======================================================================================
  Prepayment Management Dashboard
  Stored Procedures - Group Workflow Control (Tab 5 / 3.5)

  File   : 07_StoredProcedures_GroupWorkflow.sql
  Purpose: Read + write stored procedures for the Group Workflow Control tab.
           Group_GetWorkflow      - one row per delivery group: code, name, preparer,
                                     approver, #POs, #Invoices, #Journals, current Stage,
                                     Status (all per 3.5). Filterable by status and a
                                     free-text search over group name / preparer.
           Group_GetKpis          - 5 KPI aggregates (group counts by status).
           Group_GetFilterOptions - the distinct statuses present in the live data, so the
                                     "Filter by status" dropdown is data-driven.
           Group_GetUsers         - active AppUsers for the reassign picker.
           Group_Reassign         - writes a new preparer / approver onto a group.

  Spec    : 3.5 - "Summary table of all delivery groups: Group code, Group name,
            Preparer, Approver, number of POs, number of Invoices, number of Journals,
            current Stage, Status, Action." "Filter by status; search by group name or
            preparer." Action column: View detail, Reassign (+ Send reminder / Escalate,
            which belong to the deferred Tab 8 email system and are intentionally NOT
            implemented here).

  Design  : Every read value is DERIVED LIVE from the Tab 1/2/3 transactional tables.
            The GroupWorkflowState table is NOT read - the matrix is recomputed on each
            load so it can never drift. A group's row aggregates all of its purchase
            orders (PurchaseOrder.DeliveryGroupId) and journals (Journal.DeliveryGroupId).

  Stage code scheme (internal, used to derive Stage/Status only):
            0 = not yet reached, 1 = complete, 2 = in progress/pending, 3 = rejected.

  Naming  : prepayment.Group_<Action>
  Run after: 01_Schema_PrepaymentManagement.sql (data is loaded via the Import tab)
======================================================================================*/

SET NOCOUNT ON;
GO
USE [CPlatform];
GO

/* fn_GroupWorkflowStages changed signature across iterations (it used to take @Period), and a
   plain CREATE FUNCTION can't alter a TVF signature - so drop any prior version first to keep
   this script re-runnable. */
IF OBJECT_ID('prepayment.fn_GroupWorkflowStages', 'IF') IS NOT NULL
    DROP FUNCTION prepayment.fn_GroupWorkflowStages;
GO

/*--------------------------------------------------------------------------------------
  fn_GroupWorkflowStages()
  Inline TVF - one row per active delivery group with the six internal stage codes plus
  the live counts of POs / Invoices / Journals. The stage codes drive the derived
  current-Stage and Status only; the grid shows the counts (per 3.5), not glyphs.
--------------------------------------------------------------------------------------*/
CREATE FUNCTION prepayment.fn_GroupWorkflowStages()
RETURNS TABLE
AS
RETURN
(
    SELECT
        dg.Id                                                   AS DeliveryGroupId,
        dg.DeliveryGroupCode,
        dg.GroupName,
        ISNULL(pu.DisplayName, N'-')                            AS PreparerName,
        ISNULL(au.DisplayName, au.RoleName)                     AS ApproverName,

        /* 3.5 count columns */
        PoCount      = (SELECT COUNT(*) FROM dbo.tblPPM_PurchaseOrder po
                        WHERE po.DeliveryGroupId = dg.Id AND po.IsDeleted = 0),
        InvoiceCount = (SELECT COUNT(*) FROM dbo.tblPPM_Invoice i
                        JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId AND po.IsDeleted = 0
                        WHERE po.DeliveryGroupId = dg.Id AND i.IsDeleted = 0),
        JournalCount = (SELECT COUNT(*) FROM dbo.tblPPM_Journal j
                        WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0),

        /* PO FLAG: 1 = Prepayment line exists, 3 = only NotPrepayment, 0 = none */
        CASE
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_PurchaseOrder po
                         JOIN dbo.tblPPM_PoDeliveryLine l ON l.PurchaseOrderId = po.Id AND l.IsDeleted = 0
                         WHERE po.DeliveryGroupId = dg.Id AND po.IsDeleted = 0
                           AND l.PrepaymentFlag = 'Prepayment') THEN 1
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_PurchaseOrder po
                         JOIN dbo.tblPPM_PoDeliveryLine l ON l.PurchaseOrderId = po.Id AND l.IsDeleted = 0
                         WHERE po.DeliveryGroupId = dg.Id AND po.IsDeleted = 0
                           AND l.PrepaymentFlag = 'NotPrepayment') THEN 3
            ELSE 0
        END AS PoFlagStage,

        /* INVOICE: 1 = at least one invoice on the group's POs, 0 = none */
        CASE WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Invoice i
                          JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId AND po.IsDeleted = 0
                          WHERE po.DeliveryGroupId = dg.Id AND i.IsDeleted = 0)
             THEN 1 ELSE 0 END AS InvoiceStage,

        /* SETUP: 1 = Complete, 2 = AmortisationNeeded/DraftInProgress, 0 = none */
        CASE
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Invoice i
                         JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId AND po.IsDeleted = 0
                         WHERE po.DeliveryGroupId = dg.Id AND i.IsDeleted = 0
                           AND i.SetupStatus = 'Complete') THEN 1
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Invoice i
                         JOIN dbo.tblPPM_PurchaseOrder po ON po.Id = i.PurchaseOrderId AND po.IsDeleted = 0
                         WHERE po.DeliveryGroupId = dg.Id AND i.IsDeleted = 0
                           AND i.SetupStatus IN ('AmortisationNeeded','DraftInProgress')) THEN 2
            ELSE 0
        END AS SetupStage,

        /* RECOGNISED: success -> pending -> rejected -> none */
        CASE
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.JournalType = 'Recognition' AND j.Status IN ('Approved','Exported')) THEN 1
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.JournalType = 'Recognition' AND j.Status = 'PendingApproval') THEN 2
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.JournalType = 'Recognition' AND j.Status = 'Rejected') THEN 3
            ELSE 0
        END AS RecognitionStage,

        /* AMORTISING: exported -> pending -> rejected -> none */
        CASE
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.JournalType = 'Amortisation' AND j.Status = 'Exported') THEN 1
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.JournalType = 'Amortisation' AND j.Status = 'PendingApproval') THEN 2
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.JournalType = 'Amortisation' AND j.Status = 'Rejected') THEN 3
            ELSE 0
        END AS AmortisationStage,

        /* EXPORTED: 1 = all journals exported (none left), 2 = an Approved one is ready, 0 = none */
        CASE
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.Status = 'Exported')
             AND NOT EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.Status NOT IN ('Exported')) THEN 1
            WHEN EXISTS (SELECT 1 FROM dbo.tblPPM_Journal j WHERE j.DeliveryGroupId = dg.Id AND j.IsDeleted = 0
                           AND j.Status = 'Approved') THEN 2
            ELSE 0
        END AS ExportStage

    FROM      dbo.tblPPM_DeliveryGroup dg
    LEFT JOIN dbo.tblPPM_AppUser pu ON pu.Id = dg.PreparerUserId AND pu.IsDeleted = 0
    LEFT JOIN dbo.tblPPM_AppUser au ON au.Id = dg.ApproverUserId AND au.IsDeleted = 0
    WHERE     dg.IsDeleted = 0
      AND     dg.IsActive  = 1
);
GO

/*--------------------------------------------------------------------------------------
  fn_GroupWorkflowDerive - classifies the six stage codes into a CurrentStageKey +
  StatusKey, so the grid can show a derived Stage/Status badge and filter by status.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION prepayment.fn_GroupWorkflowDerive
(
    @PoFlag INT, @Invoice INT, @Setup INT, @Recognition INT, @Amortisation INT, @Export INT
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        StatusKey =
            CASE
                WHEN @Recognition = 3 OR @Amortisation = 3 OR @PoFlag = 3 THEN 'Blocked'
                WHEN @Export = 1                                          THEN 'FullyExported'
                WHEN @Setup = 2 OR @Recognition = 2 OR @Amortisation = 2  THEN 'NeedsAttention'
                ELSE 'OnTrack'
            END,
        CurrentStageKey =
            CASE
                WHEN @Export = 1        THEN 'Exported'
                WHEN @Export = 2        THEN 'ExportReady'
                WHEN @Amortisation = 3  THEN 'Rejected'
                WHEN @Amortisation = 2  THEN 'PendingApproval'
                WHEN @Amortisation = 1  THEN 'Amortising'
                WHEN @Recognition = 3   THEN 'Rejected'
                WHEN @Recognition = 2   THEN 'PendingApproval'
                WHEN @Recognition = 1   THEN 'Recognised'
                WHEN @Setup = 1         THEN 'SetupComplete'
                WHEN @Setup = 2         THEN 'AmortSetup'
                WHEN @Invoice = 1       THEN 'InvoiceReview'
                WHEN @PoFlag = 1        THEN 'PoFlagging'
                WHEN @PoFlag = 3        THEN 'NotPrepayment'
                ELSE 'NotStarted'
            END
);
GO

/*--------------------------------------------------------------------------------------
  fn_GroupStatusLabel - status key -> human label (single source of truth for the dropdown).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION prepayment.fn_GroupStatusLabel(@Key VARCHAR(20))
RETURNS NVARCHAR(40)
AS
BEGIN
    RETURN CASE @Key
        WHEN 'OnTrack'        THEN N'On track'
        WHEN 'NeedsAttention' THEN N'Needs attention'
        WHEN 'Blocked'        THEN N'Blocked'
        WHEN 'FullyExported'  THEN N'Fully exported'
        ELSE @Key
    END;
END
GO

/*--------------------------------------------------------------------------------------
  Group_GetWorkflow
  One row per active group (3.5 columns), optionally filtered by status, group name and
  preparer (each an exact match driven by the data-driven dropdowns).

  @StatusFilter - NULL/'' = all, else OnTrack|NeedsAttention|Blocked|FullyExported
  @GroupName    - NULL/'' = all, else exact GroupName
  @Preparer     - NULL/'' = all, else exact PreparerName
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_GetWorkflow
    @StatusFilter VARCHAR(20)   = NULL,
    @GroupName    NVARCHAR(150)  = NULL,
    @Preparer     NVARCHAR(150)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @StatusFilter = '' SET @StatusFilter = NULL;
    IF @GroupName = ''    SET @GroupName = NULL;
    IF @Preparer = ''     SET @Preparer = NULL;

    SELECT
        s.DeliveryGroupCode,
        s.GroupName,
        s.PreparerName,
        s.ApproverName,
        s.PoCount,
        s.InvoiceCount,
        s.JournalCount,
        d.CurrentStageKey,
        d.StatusKey
    FROM   prepayment.fn_GroupWorkflowStages() s
    CROSS APPLY prepayment.fn_GroupWorkflowDerive
        (s.PoFlagStage, s.InvoiceStage, s.SetupStage, s.RecognitionStage, s.AmortisationStage, s.ExportStage) d
    WHERE (@StatusFilter IS NULL OR d.StatusKey    = @StatusFilter)
      AND (@GroupName    IS NULL OR s.GroupName    = @GroupName)
      AND (@Preparer     IS NULL OR s.PreparerName = @Preparer)
    ORDER BY s.DeliveryGroupCode;
END
GO

/*--------------------------------------------------------------------------------------
  Group_GetKpis - five status buckets across all active groups. Buckets are mutually
  exclusive and sum to TotalGroups.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_GetKpis
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH g AS (
        SELECT d.StatusKey
        FROM   prepayment.fn_GroupWorkflowStages() s
        CROSS APPLY prepayment.fn_GroupWorkflowDerive
            (s.PoFlagStage, s.InvoiceStage, s.SetupStage, s.RecognitionStage, s.AmortisationStage, s.ExportStage) d
    )
    SELECT
        COUNT(*)                                                       AS TotalGroups,
        SUM(CASE WHEN StatusKey = 'OnTrack'        THEN 1 ELSE 0 END)  AS OnTrack,
        SUM(CASE WHEN StatusKey = 'NeedsAttention' THEN 1 ELSE 0 END)  AS NeedsAttention,
        SUM(CASE WHEN StatusKey = 'Blocked'        THEN 1 ELSE 0 END)  AS Blocked,
        SUM(CASE WHEN StatusKey = 'FullyExported'  THEN 1 ELSE 0 END)  AS FullyExported
    FROM g;
END
GO

/*--------------------------------------------------------------------------------------
  Group_GetFilterOptions
  Three result sets so every filter dropdown is data-driven (no hard-coded option lists):
    (1) Statuses  - the distinct StatusKeys actually present, with labels + sort order
    (2) GroupNames - the distinct delivery-group names (alphabetical)
    (3) Preparers - the distinct preparer display names (alphabetical)
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_GetFilterOptions
AS
BEGIN
    SET NOCOUNT ON;

    /* (1) Statuses present in the data */
    SELECT DISTINCT
        StatusKey   = d.StatusKey,
        StatusLabel = prepayment.fn_GroupStatusLabel(d.StatusKey),
        SortOrder   = CASE d.StatusKey
                         WHEN 'OnTrack' THEN 1 WHEN 'NeedsAttention' THEN 2
                         WHEN 'Blocked' THEN 3 WHEN 'FullyExported' THEN 4 ELSE 5 END
    FROM   prepayment.fn_GroupWorkflowStages() s
    CROSS APPLY prepayment.fn_GroupWorkflowDerive
        (s.PoFlagStage, s.InvoiceStage, s.SetupStage, s.RecognitionStage, s.AmortisationStage, s.ExportStage) d
    ORDER BY SortOrder;

    /* (2) Group names (active groups, alphabetical) */
    SELECT GroupName
    FROM   dbo.tblPPM_DeliveryGroup
    WHERE  IsDeleted = 0 AND IsActive = 1
    ORDER BY GroupName;

    /* (3) Preparers assigned to active groups (alphabetical) */
    SELECT DISTINCT PreparerName = ISNULL(u.DisplayName, N'-')
    FROM   dbo.tblPPM_DeliveryGroup dg
    LEFT   JOIN dbo.tblPPM_AppUser u ON u.Id = dg.PreparerUserId AND u.IsDeleted = 0
    WHERE  dg.IsDeleted = 0 AND dg.IsActive = 1
    ORDER BY PreparerName;
END
GO

/*--------------------------------------------------------------------------------------
  Group_GetUsers - active app users for the reassign picker.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_GetUsers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  Id,
            DisplayName = ISNULL(DisplayName, WindowsAccount),
            RoleName
    FROM    dbo.tblPPM_AppUser
    WHERE   IsDeleted = 0 AND IsActive = 1
    ORDER BY ISNULL(DisplayName, WindowsAccount);
END
GO

/*--------------------------------------------------------------------------------------
  Group_Reassign - reassigns a group's preparer / approver (0/NULL = leave unchanged).
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_Reassign
    @DeliveryGroupCode VARCHAR(20),
    @PreparerUserId    INT = NULL,
    @ApproverUserId    INT = NULL,
    @ModifiedBy        INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tblPPM_DeliveryGroup
    SET    PreparerUserId = CASE WHEN ISNULL(@PreparerUserId,0) > 0 THEN @PreparerUserId ELSE PreparerUserId END,
           ApproverUserId = CASE WHEN ISNULL(@ApproverUserId,0) > 0 THEN @ApproverUserId ELSE ApproverUserId END,
           ModifiedBy     = @ModifiedBy,
           ModifiedDate   = SYSUTCDATETIME()
    WHERE  DeliveryGroupCode = @DeliveryGroupCode
      AND  IsDeleted = 0;

    SELECT @@ROWCOUNT AS Updated;
END
GO

/*--------------------------------------------------------------------------------------
  Group_Escalate - 3.5 "Escalate" row action. Raises an Admin exception (ExceptionItem)
  against the group so it surfaces on the Admin Control Tower (Tab 4 / 3.4). This is the
  3.5 -> Page 4 escalation feed. Returns the new exception id.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_Escalate
    @DeliveryGroupCode VARCHAR(20),
    @Note              NVARCHAR(400) = NULL,
    @UserId            INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @dgId BIGINT = (SELECT Id FROM dbo.tblPPM_DeliveryGroup WHERE DeliveryGroupCode = @DeliveryGroupCode AND IsDeleted = 0);
    IF @dgId IS NULL BEGIN RAISERROR('Unknown delivery group.', 16, 1); RETURN; END

    DECLARE @name NVARCHAR(150) = (SELECT GroupName FROM dbo.tblPPM_DeliveryGroup WHERE Id = @dgId);

    INSERT dbo.tblPPM_ExceptionItem (Title, Detail, ExceptionType, DeliveryGroupId, Status, CreatedBy)
    VALUES (N'Group escalated - ' + @DeliveryGroupCode,
            ISNULL(@Note, N'Workflow escalated for ' + ISNULL(@name, @DeliveryGroupCode) + N' - requires admin attention'),
            'FollowUp', @dgId, 'Open', @UserId);

    SELECT SCOPE_IDENTITY() AS ExceptionId;
END
GO

/*--------------------------------------------------------------------------------------
  Group_SendReminder - 3.5 "Send reminder" row action. The prototype has no live email
  (Tab 8 / 3.8 is out of scope), so the reminder is recorded in the WorkflowReminder
  activity table - NOT as an ExceptionItem - so it stays out of the Admin "clear exception"
  picker (Tab 4 / 3.4). Returns the new reminder id.
--------------------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE prepayment.Group_SendReminder
    @DeliveryGroupCode VARCHAR(20),
    @UserId            INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @dgId BIGINT = (SELECT Id FROM dbo.tblPPM_DeliveryGroup WHERE DeliveryGroupCode = @DeliveryGroupCode AND IsDeleted = 0);
    IF @dgId IS NULL BEGIN RAISERROR('Unknown delivery group.', 16, 1); RETURN; END

    DECLARE @preparerId INT      = (SELECT PreparerUserId FROM dbo.tblPPM_DeliveryGroup WHERE Id = @dgId);
    DECLARE @preparer   NVARCHAR(150) =
        (SELECT ISNULL(u.DisplayName, u.WindowsAccount)
         FROM   dbo.tblPPM_AppUser u
         WHERE  u.Id = @preparerId);

    INSERT dbo.tblPPM_WorkflowReminder (DeliveryGroupId, PreparerUserId, PreparerName, Detail, SentByUserId, CreatedBy)
    VALUES (@dgId, @preparerId, @preparer,
            N'Workflow reminder sent to preparer ' + ISNULL(@preparer, N'(unassigned)'),
            @UserId, @UserId);

    SELECT SCOPE_IDENTITY() AS ReminderId;
END
GO

/*--------------------------------------------------------------------------------------
  One-time cleanup: retire reminder rows that earlier builds wrote into ExceptionItem
  (Title 'Reminder sent - ...'). They are activity records, not clearable exceptions, so
  they must not appear in the Admin "clear exception" picker (Tab 4 / 3.4). Soft-delete
  is used to preserve any history. Re-running is harmless (idempotent).
--------------------------------------------------------------------------------------*/
UPDATE dbo.tblPPM_ExceptionItem
SET    IsDeleted    = 1,
       ModifiedDate = SYSUTCDATETIME()
WHERE  IsDeleted = 0
  AND  Title LIKE N'Reminder sent - %';
GO

PRINT 'Group Workflow objects created (fn_GroupWorkflowStages, fn_GroupWorkflowDerive, fn_GroupStatusLabel, Group_GetWorkflow, Group_GetKpis, Group_GetFilterOptions, Group_GetUsers, Group_Reassign, Group_Escalate, Group_SendReminder). Legacy reminder ExceptionItem rows retired.';
GO
