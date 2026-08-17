/* NORM 11 - correct the official FY2024 receivables comparative retained by
   the FY2025 start-of-year setup. The automated PDF match had selected a note
   total instead of the face Statement of Financial Position row. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51012, 'Run NORM_08_StartOfFinancialYearSetup.sql before NORM_11_ComparativeReceivablesAlignment.sql.', 1;

DECLARE @Official2023_24PdfHash CHAR(64) = '1d02b0ee80d807735035ceed18eb7871d0d50193845db6e7317335f3ca9cd62b';

UPDATE f
SET Amount=1957738.000,
    SourceLocator=N'Audited FY2024 Statement of Financial Position - face statement',
    MatchConfidence=100.00,
    ReviewStatus='Confirmed'
FROM dbo.tblNORM_YearSetupFigure f
INNER JOIN dbo.tblNORM_YearSetupDocument d ON d.YearSetupDocumentId=f.YearSetupDocumentId
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
  AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.SourceFileHash=@Official2023_24PdfHash AND d.IsDeactivated=0
  AND f.FigureType='PriorActual' AND f.StatementCode='SOFP'
  AND f.LineCode='Trade and other receivables' AND f.IsDeactivated=0;

IF @@ROWCOUNT=0
    THROW 51013, 'The retained official FY2023-24 financial statements receivables figure was not found.', 1;

PRINT 'NORM FY2024 trade and other receivables comparative corrected to 1,957,738.';
GO
