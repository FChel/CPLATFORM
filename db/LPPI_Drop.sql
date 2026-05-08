/* =============================================================================
   LPPI Review — DROP script
   Database: CPlatform
   DEV / UAT reset use only — DO NOT RUN IN PRODUCTION.
   Drops every tblLPPI_* object. Order respects FK dependencies: child tables
   are dropped before their parents. Idempotent via IF OBJECT_ID checks.

   Drop order:
      1. tblLPPI_AdminUsers                (no FK dependencies)
      2. tblLPPI_EmailLog                  (FK -> ReviewPackages)
      3. tblLPPI_ReviewHistory             (FK -> Documents, ReviewPackages, ReasonCodes)
      4. tblLPPI_ReviewPackageDocuments    (FK -> ReviewPackages, Documents)
      5. tblLPPI_PackagePocs               (FK -> ReviewPackages)
      6. tblLPPI_ReviewPackages            (FK -> CapabilityManagers, ExportBatches)
      7. tblLPPI_Reviews                   (FK -> Documents, ReasonCodes)
      8. tblLPPI_CapabilityManagerEmails   (legacy table — dropped if still present)
      9. tblLPPI_CapabilityManagers
     10. tblLPPI_ReasonCodes
     11. tblLPPI_Documents                 (FK -> LoadBatches, ExportBatches)
     12. tblLPPI_ExportBatches             (no incoming FKs left at this point)
     13. tblLPPI_LoadBatches
   ============================================================================= */

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [CPlatform];
GO

PRINT 'LPPI_Drop.sql starting — this will remove every tblLPPI_* object.';
GO

IF OBJECT_ID(N'dbo.tblLPPI_AdminUsers', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_AdminUsers;
    PRINT '  dropped tblLPPI_AdminUsers';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_EmailLog', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_EmailLog;
    PRINT '  dropped tblLPPI_EmailLog';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_ReviewHistory', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_ReviewHistory;
    PRINT '  dropped tblLPPI_ReviewHistory';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_ReviewPackageDocuments', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_ReviewPackageDocuments;
    PRINT '  dropped tblLPPI_ReviewPackageDocuments';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_PackagePocs', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_PackagePocs;
    PRINT '  dropped tblLPPI_PackagePocs';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_ReviewPackages', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_ReviewPackages;
    PRINT '  dropped tblLPPI_ReviewPackages';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_Reviews', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_Reviews;
    PRINT '  dropped tblLPPI_Reviews';
END
GO

/* Legacy table — collapsed into tblLPPI_CapabilityManagers.Email +
   EmailDisplayName. Drop is idempotent against a fresh DB; only fires
   on databases that still have the old table. */
IF OBJECT_ID(N'dbo.tblLPPI_CapabilityManagerEmails', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_CapabilityManagerEmails;
    PRINT '  dropped tblLPPI_CapabilityManagerEmails (legacy)';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_CapabilityManagers', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_CapabilityManagers;
    PRINT '  dropped tblLPPI_CapabilityManagers';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_ReasonCodes', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_ReasonCodes;
    PRINT '  dropped tblLPPI_ReasonCodes';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_Documents', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_Documents;
    PRINT '  dropped tblLPPI_Documents';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_ExportBatches', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_ExportBatches;
    PRINT '  dropped tblLPPI_ExportBatches';
END
GO

IF OBJECT_ID(N'dbo.tblLPPI_LoadBatches', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_LoadBatches;
    PRINT '  dropped tblLPPI_LoadBatches';
END
GO

PRINT 'LPPI_Drop.sql complete.';
GO
