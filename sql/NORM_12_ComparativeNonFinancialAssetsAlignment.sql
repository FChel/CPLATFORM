SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51014, 'Run NORM_08_StartOfFinancialYearSetup.sql before NORM_12_ComparativeNonFinancialAssetsAlignment.sql.', 1;

DECLARE @Official2023_24PdfHash CHAR(64) = '1d02b0ee80d807735035ceed18eb7871d0d50193845db6e7317335f3ca9cd62b';

UPDATE f
SET Amount=CASE f.LineCode
        WHEN N'Inventories' THEN 9597327.000
        WHEN N'Prepayments' THEN 3500032.000
    END,
    SourceLocator=N'Audited FY2024 Statement of Financial Position - face statement',
    MatchConfidence=100.00,
    ReviewStatus='Confirmed'
FROM dbo.tblNORM_YearSetupFigure f
INNER JOIN dbo.tblNORM_YearSetupDocument d ON d.YearSetupDocumentId=f.YearSetupDocumentId
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
  AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.SourceFileHash=@Official2023_24PdfHash AND d.IsDeactivated=0
  AND f.FigureType='PriorActual' AND f.StatementCode='SOFP'
  AND f.LineCode IN (N'Inventories',N'Prepayments') AND f.IsDeactivated=0;

IF @@ROWCOUNT<>2
    THROW 51015, 'The retained official FY2023-24 inventories and prepayments figures were not both found.', 1;

PRINT 'NORM FY2024 inventories corrected to 9,597,327 and prepayments corrected to 3,500,032.';
GO
