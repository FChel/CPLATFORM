/* =============================================================================
   LPPI_DataRefresh.sql

   Wipes every row of LPPI TRANSACTIONAL data, leaving the schema and
   CONFIGURATION rows untouched. Use this when you want to re-load from
   scratch in DEV / UAT without losing your admin user list, reason codes,
   or Capability Manager configuration.

   ----------------------------------------------------------------------
   WHAT GETS DELETED
   ----------------------------------------------------------------------
     - tblLPPI_EmailLog                  (every send / mark-as-sent record)
     - tblLPPI_ReviewHistory             (every reviewer-side change snapshot)
     - tblLPPI_ReviewPackageDocuments    (every package <-> document link)
     - tblLPPI_PackagePocs               (every per-POC token row)
     - tblLPPI_ReviewPackages            (every package, every status)
     - tblLPPI_Reviews                   (every reason code / comment / objref)
     - tblLPPI_Documents                 (every loaded document line)
     - tblLPPI_LoadBatches               (every load batch header)
     - tblLPPI_ExportBatches             (every ERP export file header)

   IDENTITY columns are reseeded so PackageID / DocumentID / BatchID /
   HistoryID / ExportBatchID start from 1 again on the next load.

   ----------------------------------------------------------------------
   WHAT IS PRESERVED
   ----------------------------------------------------------------------
     - tblLPPI_AdminUsers                (admin access list)
     - tblLPPI_ReasonCodes               (RC01-RC16, RC-NR, and any custom codes)
     - tblLPPI_CapabilityManagers        (ARMY, NAVY, etc., with their
                                          configured Email + EmailDisplayName)

   ----------------------------------------------------------------------
   SAFETY
   ----------------------------------------------------------------------
   This script will REFUSE TO RUN unless:
     1. The current database is named CPlatform (DB_NAME() check), AND
     2. You have manually commented out the speed-bump RAISERROR below.

   This is intentional. There is no "undo" once this script commits. Read
   the WHAT GETS DELETED list, confirm you actually want this, then comment
   out the RAISERROR line and run.

   Safe to re-run: deletes are idempotent (DELETE on an empty table is a
   no-op) and the identity reseeds work whether the tables are empty or not.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

USE [CPlatform];
GO

/* ----------------------------------------------------------------------
   Speed bump #1 — wrong database guard.
   ---------------------------------------------------------------------- */
IF DB_NAME() <> N'CPlatform'
BEGIN
    RAISERROR('LPPI_DataRefresh: refusing to run — current database is %s, expected CPlatform.', 16, 1, DB_NAME());
    SET NOEXEC ON;
END
GO

/* ----------------------------------------------------------------------
   Speed bump #2 — explicit confirmation.

   To run this script, comment out the RAISERROR line below. This is a
   one-line edit and avoids the "I just hit F5 by accident" failure mode.
   ---------------------------------------------------------------------- */
RAISERROR('LPPI_DataRefresh: speed-bump active. Comment out this RAISERROR in the script to actually run the refresh.', 16, 1);
SET NOEXEC ON;
GO

/* ============================================================================
   Live refresh begins below this point. Execution only reaches here after
   the speed-bump RAISERROR above is commented out.
   ============================================================================ */

PRINT '------------------------------------------------------------';
PRINT 'LPPI_DataRefresh starting on database: ' + DB_NAME();
PRINT 'Started at: ' + CONVERT(NVARCHAR(40), SYSDATETIME(), 121);
PRINT '------------------------------------------------------------';
GO

BEGIN TRANSACTION;

/* Pre-counts — for the after-the-fact summary. */
DECLARE @cnt_EmailLog          INT = (SELECT COUNT(*) FROM dbo.tblLPPI_EmailLog);
DECLARE @cnt_History           INT = (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewHistory);
DECLARE @cnt_PackageDocs       INT = (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackageDocuments);
DECLARE @cnt_PackagePocs       INT = (SELECT COUNT(*) FROM dbo.tblLPPI_PackagePocs);
DECLARE @cnt_Packages          INT = (SELECT COUNT(*) FROM dbo.tblLPPI_ReviewPackages);
DECLARE @cnt_Reviews           INT = (SELECT COUNT(*) FROM dbo.tblLPPI_Reviews);
DECLARE @cnt_Documents         INT = (SELECT COUNT(*) FROM dbo.tblLPPI_Documents);
DECLARE @cnt_LoadBatches       INT = (SELECT COUNT(*) FROM dbo.tblLPPI_LoadBatches);
DECLARE @cnt_ExportBatches     INT = (SELECT COUNT(*) FROM dbo.tblLPPI_ExportBatches);

/* ----------------------------------------------------------------------
   Delete order respects FK dependencies:
     1. EmailLog              -> ReviewPackages
     2. ReviewHistory         -> Documents, ReviewPackages, ReasonCodes (kept)
     3. ReviewPackageDocuments-> ReviewPackages, Documents
     4. PackagePocs           -> ReviewPackages
     5. ReviewPackages        -> CapabilityManagers (kept), ExportBatches
     6. Reviews               -> Documents, ReasonCodes (kept)
     7. Documents             -> LoadBatches, ExportBatches
     8. ExportBatches         (no incoming FKs left after Documents and
                               ReviewPackages cleared)
     9. LoadBatches           (no incoming FKs left)
   ---------------------------------------------------------------------- */

DELETE FROM dbo.tblLPPI_EmailLog;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_EmailLog', @cnt_EmailLog);

DELETE FROM dbo.tblLPPI_ReviewHistory;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_ReviewHistory', @cnt_History);

DELETE FROM dbo.tblLPPI_ReviewPackageDocuments;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_ReviewPackageDocuments', @cnt_PackageDocs);

DELETE FROM dbo.tblLPPI_PackagePocs;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_PackagePocs', @cnt_PackagePocs);

DELETE FROM dbo.tblLPPI_ReviewPackages;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_ReviewPackages', @cnt_Packages);

DELETE FROM dbo.tblLPPI_Reviews;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_Reviews', @cnt_Reviews);

DELETE FROM dbo.tblLPPI_Documents;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_Documents', @cnt_Documents);

DELETE FROM dbo.tblLPPI_ExportBatches;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_ExportBatches', @cnt_ExportBatches);

DELETE FROM dbo.tblLPPI_LoadBatches;
PRINT FORMATMESSAGE('  Deleted %d row(s) from tblLPPI_LoadBatches', @cnt_LoadBatches);

/* ----------------------------------------------------------------------
   Reseed identity columns so the next load starts fresh from 1.
   DBCC CHECKIDENT with RESEED on a now-empty table sets the next value
   to (seed + increment), i.e. 1 for our (1,1) tables.
   ---------------------------------------------------------------------- */

DBCC CHECKIDENT (N'dbo.tblLPPI_LoadBatches',    RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_Documents',      RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_Reviews',        RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_ReviewHistory',  RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_ReviewPackages', RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_PackagePocs',    RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_EmailLog',       RESEED, 0);
DBCC CHECKIDENT (N'dbo.tblLPPI_ExportBatches',  RESEED, 0);

PRINT '  Identity columns reseeded.';

COMMIT TRANSACTION;

PRINT '------------------------------------------------------------';
PRINT 'LPPI_DataRefresh complete.';
PRINT 'Preserved: tblLPPI_AdminUsers, tblLPPI_ReasonCodes, tblLPPI_CapabilityManagers.';
PRINT 'Finished at: ' + CONVERT(NVARCHAR(40), SYSDATETIME(), 121);
PRINT '------------------------------------------------------------';
GO

SET NOEXEC OFF;
GO
