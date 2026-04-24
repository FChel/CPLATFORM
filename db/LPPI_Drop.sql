/* =============================================================================
   LPPI Review — DROP script
   Database: CPlatform
   DEV / UAT reset use only — DO NOT RUN IN PRODUCTION.
   Drops every tblLPPI_* object. Order respects FK dependencies: child tables
   are dropped before their parents. Idempotent via IF OBJECT_ID checks.

   Drop order:
     1.  tblLPPI_AdminUsers                (no FK dependencies)
     2.  tblLPPI_EmailLog                  (FK -> ReviewPackages)
     3.  tblLPPI_ReviewPackageDocuments    (FK -> ReviewPackages, Documents)
     4.  tblLPPI_ReviewPackages            (FK -> CapabilityManagers)
     5.  tblLPPI_Reviews                   (FK -> Documents, ReasonCodes)
     6.  tblLPPI_CapabilityManagerEmails   (FK -> CapabilityManagers)
     7.  tblLPPI_CapabilityManagers
     8.  tblLPPI_ReasonCodes
     9.  tblLPPI_Documents                 (FK -> LoadBatches)
     10. tblLPPI_LoadBatches
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

IF OBJECT_ID(N'dbo.tblLPPI_ReviewPackageDocuments', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_ReviewPackageDocuments;
    PRINT '  dropped tblLPPI_ReviewPackageDocuments';
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

IF OBJECT_ID(N'dbo.tblLPPI_CapabilityManagerEmails', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_CapabilityManagerEmails;
    PRINT '  dropped tblLPPI_CapabilityManagerEmails';
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

IF OBJECT_ID(N'dbo.tblLPPI_LoadBatches', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.tblLPPI_LoadBatches;
    PRINT '  dropped tblLPPI_LoadBatches';
END
GO

PRINT 'LPPI_Drop.sql complete.';
GO
