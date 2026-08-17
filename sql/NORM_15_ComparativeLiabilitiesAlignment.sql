SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51022, 'Run NORM_08_StartOfFinancialYearSetup.sql before NORM_15_ComparativeLiabilitiesAlignment.sql.', 1;

DECLARE @Official2023_24PdfHash CHAR(64) = '1d02b0ee80d807735035ceed18eb7871d0d50193845db6e7317335f3ca9cd62b';
DECLARE @DocumentId BIGINT;

SELECT TOP (1) @DocumentId=d.YearSetupDocumentId
FROM dbo.tblNORM_YearSetupDocument d
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
  AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.SourceFileHash=@Official2023_24PdfHash AND d.IsDeactivated=0
ORDER BY d.UploadedUtc DESC;

IF @DocumentId IS NULL
    THROW 51023, 'The retained official FY2023-24 financial statements document was not found.', 1;

DECLARE @Corrections TABLE
(
    LineCode NVARCHAR(240) NOT NULL PRIMARY KEY,
    LineLabel NVARCHAR(300) NOT NULL,
    Amount DECIMAL(19,3) NOT NULL,
    SourceLocator NVARCHAR(300) NOT NULL
);

INSERT @Corrections(LineCode,LineLabel,Amount,SourceLocator)
VALUES
    (N'Employee payables',N'Employee payables',353406.000,N'Audited FY2024 Statement of Financial Position - face statement'),
    (N'Leases',N'Leases',3139113.000,N'Audited FY2024 Statement of Financial Position - face statement'),
    (N'Employee provisions',N'Employee provisions',3285642.000,N'Audited FY2024 Statement of Financial Position - face statement'),
    (N'Asset restoration provisions',N'Asset restoration provisions',1056201.000,N'Audited FY2024 Statement of Financial Position - face statement'),
    (N'Other provisions',N'Other provisions',266891.000,N'Audited FY2024 Statement of Financial Position - face statement'),
    (N'Total liabilities',N'Total liabilities',13848233.000,N'Audited FY2024 Statement of Financial Position - face statement'),
    (N'Net assets',N'Net assets',132047045.000,N'Audited FY2024 Statement of Financial Position - calculated control total'),
    (N'Statement of Changes in Equity',N'Total equity',132047045.000,N'Audited FY2024 Statement of Financial Position - calculated control total');

UPDATE f
SET Amount=correction.Amount,
    LineLabel=correction.LineLabel,
    SourceLocator=correction.SourceLocator,
    MatchConfidence=100.00,
    ReviewStatus='Confirmed',
    IsDeactivated=0
FROM dbo.tblNORM_YearSetupFigure f
INNER JOIN @Corrections correction ON correction.LineCode=f.LineCode
WHERE f.YearSetupDocumentId=@DocumentId AND f.FigureType='PriorActual'
  AND f.StatementCode='SOFP';

INSERT dbo.tblNORM_YearSetupFigure
    (YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
SELECT @DocumentId,'PriorActual','SOFP',correction.LineCode,correction.LineLabel,correction.Amount,
       correction.SourceLocator,100.00,'Confirmed'
FROM @Corrections correction
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.tblNORM_YearSetupFigure f
    WHERE f.YearSetupDocumentId=@DocumentId AND f.FigureType='PriorActual'
      AND f.StatementCode='SOFP' AND f.LineCode=correction.LineCode AND f.IsDeactivated=0
);

IF
(
    SELECT COUNT(*)
    FROM dbo.tblNORM_YearSetupFigure f
    INNER JOIN @Corrections correction ON correction.LineCode=f.LineCode AND correction.Amount=f.Amount
    WHERE f.YearSetupDocumentId=@DocumentId AND f.FigureType='PriorActual'
      AND f.StatementCode='SOFP' AND f.IsDeactivated=0
)<>8
    THROW 51024, 'The retained FY2024 liability and reconciliation figures could not all be aligned.', 1;

PRINT 'NORM FY2024 liabilities, total liabilities, net assets and total equity aligned to the published statements.';
GO
