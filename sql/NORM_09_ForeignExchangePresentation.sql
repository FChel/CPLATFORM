/* NORM 09 - separate foreign exchange losses and gains in the SoCI.
   Existing approved releases remain immutable. This patch repairs the controlled
   prior-year overlay for the exact official Defence Annual Report 2024-25 PDF;
   clean configurations are corrected in the seed scripts. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51009, 'Run NORM_08_StartOfFinancialYearSetup.sql before NORM_09_ForeignExchangePresentation.sql.', 1;

DECLARE @OfficialPdfHash CHAR(64) = '88bf16696234bb7c16e1258d77628b46752d825b03df2ae70d650e03a5f2dd0f';

INSERT dbo.tblNORM_YearSetupFigure
    (YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
SELECT d.YearSetupDocumentId,'PriorActual','SOCI','Foreign exchange gains',
       N'Net foreign exchange gains',30556.000,N'PDF page 242',100.00,'Confirmed'
FROM dbo.tblNORM_YearSetupDocument d
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE d.DocumentTypeCode='PriorYearFinancialStatements'
  AND d.SourceFileHash=@OfficialPdfHash
  AND d.IsDeactivated=0
  AND y.IsCurrent=1
  AND y.IsDeactivated=0
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.tblNORM_YearSetupFigure f
      WHERE f.YearSetupDocumentId=d.YearSetupDocumentId
        AND f.StatementCode='SOCI'
        AND f.LineCode='Foreign exchange gains'
  );

PRINT 'NORM foreign exchange presentation overlay applied.';
GO
