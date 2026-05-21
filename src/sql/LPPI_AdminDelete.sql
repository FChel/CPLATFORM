/* =============================================================================
   LPPI Review — admin user delete script (DEV / UAT testing only)
   Hard-deletes a single user from tblLPPI_AdminUsers by UserId.
   Set @UserId before running.
   ============================================================================= */

USE [CPlatform];
GO

DECLARE @UserId NVARCHAR(100) = N'DOMAIN\name0.surname0';

DELETE FROM dbo.tblLPPI_AdminUsers
WHERE LOWER(UserId) = LOWER(@UserId);

PRINT 'Deleted ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' row(s) for: ' + @UserId;
GO
