SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51019, 'Run NORM_08_StartOfFinancialYearSetup.sql before NORM_14_ComparativePayablesAlignment.sql.', 1;

DECLARE @Official2023_24PdfHash CHAR(64) = '1d02b0ee80d807735035ceed18eb7871d0d50193845db6e7317335f3ca9cd62b';
DECLARE @DocumentId BIGINT;

SELECT TOP (1) @DocumentId=d.YearSetupDocumentId
FROM dbo.tblNORM_YearSetupDocument d
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
  AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.SourceFileHash=@Official2023_24PdfHash AND d.IsDeactivated=0
ORDER BY d.UploadedUtc DESC;

IF @DocumentId IS NULL
    THROW 51020, 'The retained official FY2023-24 financial statements document was not found.', 1;

UPDATE f
SET Amount=correction.Amount,
    SourceLocator=N'Audited FY2024 Statement of Financial Position - face statement',
    MatchConfidence=100.00,
    ReviewStatus='Confirmed'
FROM dbo.tblNORM_YearSetupFigure f
INNER JOIN
(
    VALUES
        (N'Suppliers payables',CAST(5351477.000 AS DECIMAL(19,3))),
        (N'Other payables',CAST(395503.000 AS DECIMAL(19,3)))
) correction(LineCode,Amount) ON correction.LineCode=f.LineCode
WHERE f.YearSetupDocumentId=@DocumentId AND f.FigureType='PriorActual'
  AND f.StatementCode='SOFP' AND f.IsDeactivated=0;

IF @@ROWCOUNT<>2
    THROW 51021, 'Both retained FY2024 payable figures were not found.', 1;

PRINT 'NORM FY2024 suppliers payables corrected to 5,351,477 and other payables corrected to 395,503.';
GO
