/* NORM 16 - published Statement of Financial Position equity presentation.
   Idempotent. Populates the three published equity classes and total across
   audited current, retained prior-year and original-budget columns. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL OR OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NULL
    THROW 51025, 'Run NORM_07_DefencePublicationAlignment.sql and NORM_08_StartOfFinancialYearSetup.sql before NORM_16_StatementOfFinancialPositionEquity.sql.', 1;

DECLARE @ReleaseId INT =
(
    SELECT TOP (1) ConfigurationReleaseId
    FROM dbo.tblNORM_ConfigurationRelease
    WHERE FinancialYear=2025 AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0
    ORDER BY ConfigurationReleaseId DESC
);
IF @ReleaseId IS NULL
    THROW 51026, 'The approved FY2025 DEPT NORM configuration release was not found.', 1;

DECLARE @Official2023_24PdfHash CHAR(64) = '1d02b0ee80d807735035ceed18eb7871d0d50193845db6e7317335f3ca9cd62b';
DECLARE @PriorDocumentId BIGINT;
SELECT TOP (1) @PriorDocumentId=d.YearSetupDocumentId
FROM dbo.tblNORM_YearSetupDocument d
INNER JOIN dbo.tblNORM_YearSetup y ON y.YearSetupId=d.YearSetupId
WHERE y.EntityCode='DEPT' AND y.CurrentFinancialYear=2025 AND y.IsCurrent=1 AND y.IsDeactivated=0
  AND d.DocumentTypeCode='PriorYearFinancialStatements' AND d.SourceFileHash=@Official2023_24PdfHash AND d.IsDeactivated=0
ORDER BY d.UploadedUtc DESC;
IF @PriorDocumentId IS NULL
    THROW 51027, 'The retained official FY2023-24 financial statements document was not found.', 1;

DECLARE @SourceReference NVARCHAR(500) = N'Defence Annual Reports 2024-25 and 2023-24, departmental Statement of Financial Position';
DECLARE @SourceUrl NVARCHAR(1000) = N'https://www.defence.gov.au/about/reviews-inquiries/annual-reports';
DECLARE @Figures TABLE
(
    LineCode VARCHAR(200) NOT NULL PRIMARY KEY,
    LineLabel NVARCHAR(300) NOT NULL,
    AuditedActual DECIMAL(19,3) NOT NULL,
    PriorActual DECIMAL(19,3) NOT NULL,
    OriginalBudget DECIMAL(19,3) NOT NULL
);
INSERT @Figures(LineCode,LineLabel,AuditedActual,PriorActual,OriginalBudget)
VALUES
    ('EQUITY_CONTRIBUTED',N'Contributed equity',107999056.000,93451248.000,107248719.000),
    ('EQUITY_RETAINED',N'(Accumulated Deficit) / Retained surpluses',-6977253.000,1190220.000,-7935813.000),
    ('EQUITY_RESERVES',N'Reserves',41613704.000,37405577.000,34038023.000),
    ('EQUITY_TOTAL',N'Total equity',142635507.000,132047045.000,133350929.000);

BEGIN TRANSACTION;

UPDATE existing
SET Amount=source.Amount,SourceReference=@SourceReference,SourceUrl=@SourceUrl,IsDeactivated=0
FROM dbo.tblNORM_SourceFigure existing
INNER JOIN
(
    SELECT f.LineCode,t.FigureType,t.Amount
    FROM @Figures f
    CROSS APPLY (VALUES
        ('AuditedActual',f.AuditedActual),
        ('PriorActual',f.PriorActual),
        ('OriginalBudget',f.OriginalBudget)
    ) t(FigureType,Amount)
) source ON source.LineCode=existing.LineCode AND source.FigureType=existing.FigureType
WHERE existing.ConfigurationReleaseId=@ReleaseId AND existing.StatementCode='SOFP';

INSERT dbo.tblNORM_SourceFigure
    (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
SELECT @ReleaseId,2025,'DEPT','SOFP',source.LineCode,source.FigureType,source.Amount,@SourceReference,@SourceUrl
FROM
(
    SELECT f.LineCode,t.FigureType,t.Amount
    FROM @Figures f
    CROSS APPLY (VALUES
        ('AuditedActual',f.AuditedActual),
        ('PriorActual',f.PriorActual),
        ('OriginalBudget',f.OriginalBudget)
    ) t(FigureType,Amount)
) source
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_SourceFigure existing
    WHERE existing.ConfigurationReleaseId=@ReleaseId AND existing.StatementCode='SOFP'
      AND existing.LineCode=source.LineCode AND existing.FigureType=source.FigureType
);

UPDATE existing
SET Amount=f.PriorActual,LineLabel=f.LineLabel,
    SourceLocator=N'Audited FY2024 Statement of Financial Position - equity section',
    MatchConfidence=100.00,ReviewStatus='Confirmed',IsDeactivated=0
FROM dbo.tblNORM_YearSetupFigure existing
INNER JOIN @Figures f ON f.LineCode=existing.LineCode
WHERE existing.YearSetupDocumentId=@PriorDocumentId AND existing.StatementCode='SOFP'
  AND existing.FigureType='PriorActual';

INSERT dbo.tblNORM_YearSetupFigure
    (YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus)
SELECT @PriorDocumentId,'PriorActual','SOFP',f.LineCode,f.LineLabel,f.PriorActual,
       N'Audited FY2024 Statement of Financial Position - equity section',100.00,'Confirmed'
FROM @Figures f
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_YearSetupFigure existing
    WHERE existing.YearSetupDocumentId=@PriorDocumentId AND existing.StatementCode='SOFP'
      AND existing.LineCode=f.LineCode
);

IF OBJECT_ID('dbo.tblNORM_BudgetFigure','U') IS NOT NULL
BEGIN
    UPDATE b
    SET OriginalBudget=f.OriginalBudget,SourceSystem=N'Published Defence financial statements',
        SourceReference=@SourceReference,StatusCode='Loaded',UpdatedBy=N'NORM publication alignment',
        UpdatedUtc=SYSUTCDATETIME(),IsDeactivated=0
    FROM dbo.tblNORM_BudgetFigure b
    INNER JOIN dbo.tblNORM_CalculationRun r ON r.CalculationRunId=b.CalculationRunId
    INNER JOIN @Figures f ON f.LineCode=b.LineCode
    WHERE r.ConfigurationReleaseId=@ReleaseId AND r.StatusCode='Complete' AND r.IsDeactivated=0
      AND b.StatementCode='SOFP';

    INSERT dbo.tblNORM_BudgetFigure
        (CalculationRunId,StatementCode,LineCode,OriginalBudget,SourceSystem,SourceReference,StatusCode,UpdatedBy)
    SELECT r.CalculationRunId,'SOFP',f.LineCode,f.OriginalBudget,
           N'Published Defence financial statements',@SourceReference,'Loaded',N'NORM publication alignment'
    FROM dbo.tblNORM_CalculationRun r
    CROSS JOIN @Figures f
    WHERE r.ConfigurationReleaseId=@ReleaseId AND r.StatusCode='Complete' AND r.IsDeactivated=0
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.tblNORM_BudgetFigure b
          WHERE b.CalculationRunId=r.CalculationRunId AND b.StatementCode='SOFP' AND b.LineCode=f.LineCode
      );
END;

COMMIT TRANSACTION;

PRINT 'NORM Statement of Financial Position equity figures aligned to the published statements.';
GO
