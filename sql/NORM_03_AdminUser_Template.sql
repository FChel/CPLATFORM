/* Add a NORM preparer/administrator after replacing the placeholders. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @UserId NVARCHAR(160) = N'YOURDOMAIN\YOUR.USER';
DECLARE @DisplayName NVARCHAR(200) = N'Your Name';
DECLARE @RoleCode VARCHAR(20) = 'Administrator';
DECLARE @CreatedBy NVARCHAR(160) = N'YOURDOMAIN\YOUR.USER';

IF @UserId LIKE N'YOURDOMAIN%' OR @CreatedBy LIKE N'YOURDOMAIN%'
    THROW 51200, 'Replace the placeholder user values before running this script.', 1;
IF @RoleCode NOT IN ('Preparer','Administrator')
    THROW 51201, 'RoleCode must be Preparer or Administrator.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_AdminUser WHERE UserId = @UserId)
    INSERT dbo.tblNORM_AdminUser (UserId,DisplayName,RoleCode,CreatedBy)
    VALUES (@UserId,@DisplayName,@RoleCode,@CreatedBy);
ELSE
    UPDATE dbo.tblNORM_AdminUser
    SET DisplayName = @DisplayName,RoleCode = @RoleCode,IsDeactivated = 0
    WHERE UserId = @UserId;

PRINT 'NORM access entry is ready.';
