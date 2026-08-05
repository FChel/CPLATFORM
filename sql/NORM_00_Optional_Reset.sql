/* ============================================================================
   OPTIONAL destructive reset for pre-proof-engine NORM prototypes

   PREVIEW IS THE DEFAULT. Do not run the destructive path until the expected
   server name and confirmation phrase are deliberately populated.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedServer SYSNAME = N'REPLACE_WITH_APPROVED_SERVER_NAME';
DECLARE @PreviewOnly BIT = 1;
DECLARE @Confirmation NVARCHAR(100) = N'';

IF CONVERT(SYSNAME, @@SERVERNAME) <> @ExpectedServer
    THROW 51100, 'Server guard failed. Update @ExpectedServer only after confirming the target.', 1;

SELECT s.name AS SchemaName, t.name AS TableName, SUM(p.rows) AS ApproximateRows
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name LIKE 'tblNORM[_]%'
GROUP BY s.name,t.name
ORDER BY t.name;

IF @PreviewOnly = 1
BEGIN
    PRINT 'Preview only. No objects were changed.';
    RETURN;
END;

IF @Confirmation <> N'DROP NORM PROTOTYPE DATA'
    THROW 51101, 'Confirmation phrase is incorrect. No objects were changed.', 1;

BEGIN TRANSACTION;

DECLARE @DropForeignKeys NVARCHAR(MAX) = N'';
SELECT @DropForeignKeys = @DropForeignKeys +
    N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' +
    QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(13)
FROM sys.foreign_keys
WHERE OBJECT_NAME(parent_object_id) LIKE 'tblNORM[_]%' OR OBJECT_NAME(referenced_object_id) LIKE 'tblNORM[_]%';
IF LEN(@DropForeignKeys) > 0 EXEC sys.sp_executesql @DropForeignKeys;

DECLARE @DropTables NVARCHAR(MAX) = N'';
SELECT @DropTables = @DropTables + N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + CHAR(13)
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name LIKE 'tblNORM[_]%'
ORDER BY CASE t.name
    WHEN 'tblNORM_AuditEvent' THEN 1
    WHEN 'tblNORM_ValidationResult' THEN 2
    WHEN 'tblNORM_Lineage' THEN 3
    WHEN 'tblNORM_LineResult' THEN 4
    WHEN 'tblNORM_CalculationRun' THEN 5
    WHEN 'tblNORM_ImportFile' THEN 6
    WHEN 'tblNORM_TrialBalanceRow' THEN 7
    WHEN 'tblNORM_Import' THEN 8
    ELSE 20 END;
IF LEN(@DropTables) > 0 EXEC sys.sp_executesql @DropTables;

COMMIT TRANSACTION;
PRINT 'Legacy NORM prototype objects were removed. Run NORM_01 next.';
