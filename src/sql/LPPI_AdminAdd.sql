/* =============================================================================
   LPPI Review — admin user add script
   Database: CPlatform

   *** ADD / REMOVE USERNAMES IN THE BLOCK BELOW BEFORE EXECUTING ***

   Without at least one active row in tblLPPI_AdminUsers, every authenticated
   CPLATFORM user will be redirected to LPPI_Info.aspx and nobody can log in
   to the admin pages.

   Re-runnable: each INSERT is guarded by NOT EXISTS so it is safe to run
   again without creating duplicates. To add more admins later, either add
   them here and re-run, or use the Admin users page in the LPPI module.
   ============================================================================= */

SET NOCOUNT ON;
GO

USE [CPlatform];
GO

DECLARE @Admins TABLE (UserId NVARCHAR(100) NOT NULL);

/* ---------------------------------------------------------------------------
   Add one row per admin. Any number of rows is fine — add or remove as needed.
   --------------------------------------------------------------------------- */
INSERT INTO @Admins (UserId) VALUES
	(N'DOMAIN\name1.surname1'),
	(N'OMAIN\nameN.surnameN');

/* ---------------------------------------------------------------------------
   Loop — do not edit below this line.
   --------------------------------------------------------------------------- */
DECLARE @UserId NVARCHAR(100);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT UserId FROM @Admins;

OPEN cur;
FETCH NEXT FROM cur INTO @UserId;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM dbo.tblLPPI_AdminUsers
        WHERE LOWER(UserId) = LOWER(@UserId))
    BEGIN
        INSERT INTO dbo.tblLPPI_AdminUsers (UserId, IsActive, CreatedBy)
        VALUES (@UserId, 1, N'LPPI_AdminSeed.sql');
        PRINT '  seeded: ' + @UserId;
    END
    ELSE
        PRINT '  skipped (already exists): ' + @UserId;

    FETCH NEXT FROM cur INTO @UserId;
END

CLOSE cur;
DEALLOCATE cur;

PRINT 'LPPI_AdminSeed.sql complete.';
GO