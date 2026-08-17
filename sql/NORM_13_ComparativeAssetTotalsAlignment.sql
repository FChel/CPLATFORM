SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51016, 'Run NORM_08_StartOfFinancialYearSetup.sql before NORM_13_ComparativeAssetTotalsAlignment.sql.', 1;

DECLARE @Official2023_24PdfHash CHAR(64) = '1d02b0ee80d807735035ceed18eb7871d0d50193845db6e7317335f3ca9cd62b';
DECLARE @DocumentId BIGINT;

SELECT TOP (1) @DocumentId=d.YearSetupDocumentId
FROM dbo.tblNORM_YearSetupDocument d
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
  AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.SourceFileHash=@Official2023_24PdfHash AND d.IsDeactivated=0
ORDER BY d.UploadedUtc DESC;

IF @DocumentId IS NULL
    THROW 51017, 'The retained official FY2023-24 financial statements document was not found.', 1;

UPDATE dbo.tblNORM_YearSetupFigure
SET Amount=68417.000,
    SourceLocator=N'Audited FY2024 Statement of Financial Position - face statement',
    MatchConfidence=100.00,
    ReviewStatus='Confirmed'
WHERE YearSetupDocumentId=@DocumentId AND FigureType='PriorActual' AND StatementCode='SOFP'
  AND LineCode=N'Assets held for sale' AND IsDeactivated=0;

IF @@ROWCOUNT<>1
    THROW 51018, 'The retained FY2024 assets held for sale figure was not found.', 1;

UPDATE dbo.tblNORM_YearSetupFigure
SET Amount=145895278.000,
    SourceLocator=N'Audited FY2024 Statement of Financial Position - calculated control total',
    MatchConfidence=100.00,
    ReviewStatus='Confirmed'
WHERE YearSetupDocumentId=@DocumentId AND FigureType='PriorActual' AND StatementCode='SOFP'
  AND LineCode=N'Total assets' AND IsDeactivated=0;

IF @@ROWCOUNT=0
BEGIN
    INSERT dbo.tblNORM_YearSetupFigure
        (YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
    VALUES
        (@DocumentId,'PriorActual','SOFP',N'Total assets',N'Total assets',145895278.000,
         N'Audited FY2024 Statement of Financial Position - calculated control total',100.00,'Confirmed');
END;

PRINT 'NORM FY2024 assets held for sale corrected to 68,417 and total assets aligned to 145,895,278.';
GO
