/* NORM 07 - Defence FY2025 publication alignment.
   Idempotent. Adds published SoCE current, comparative and original-budget baselines
   without changing immutable trial-balance results. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL
    THROW 51007, 'Run NORM_06_PreparationControlCentre.sql before NORM_07_DefencePublicationAlignment.sql.', 1;

DECLARE @release INT = (
    SELECT TOP 1 ConfigurationReleaseId
    FROM dbo.tblNORM_ConfigurationRelease
    WHERE FinancialYear=2025 AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0
    ORDER BY ConfigurationReleaseId DESC
);

IF @release IS NULL
    THROW 51008, 'The approved FY2025 DEPT NORM configuration release was not found.', 1;

DECLARE @source NVARCHAR(500) = N'Defence Annual Report 2024-25, departmental Statement of Changes in Equity';
DECLARE @url NVARCHAR(1000) = N'https://www.defence.gov.au/sites/default/files/2025-10/Defence-Annual-Report-2024-25.pdf';

DECLARE @figures TABLE
(
    LineCode VARCHAR(200) NOT NULL,
    AuditedActual DECIMAL(19,3) NOT NULL,
    PriorActual DECIMAL(19,3) NOT NULL,
    OriginalBudget DECIMAL(19,3) NOT NULL
);

INSERT @figures (LineCode,AuditedActual,PriorActual,OriginalBudget) VALUES
('SOCE_CONTRIBUTED_OPEN',93451248,79150682,93452272),
('SOCE_CONTRIBUTED_OWNER',14547808,14300566,13796448),
('SOCE_CONTRIBUTED_CLOSE',107999056,93451248,107248719),
('SOCE_RETAINED_OPEN',1753672,9886750,630697),
('SOCE_RETAINED_RESULT',-8730925,-8133078,-8566510),
('SOCE_RETAINED_CLOSE',-6977253,1753672,-7935813),
('SOCE_RESERVE_OPEN',37405577,34038023,34038023),
('SOCE_RESERVE_OCI',4208127,3367554,0),
('SOCE_RESERVE_CLOSE',41613704,37405577,34038023),
('SOCE_TOTAL_OPEN',132610497,123075455,128120992),
('SOCE_TOTAL_RESULT',-8730925,-8133078,-8566510),
('SOCE_TOTAL_OCI',4208127,3367554,0),
('SOCE_TOTAL_COMPREHENSIVE',-4522798,-4765524,-8566510),
('SOCE_TOTAL_OWNER',14547808,14300566,13796448),
('SOCE_TOTAL_CLOSE',142635507,132610497,133350929);

BEGIN TRANSACTION;

INSERT dbo.tblNORM_SourceFigure
    (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
SELECT @release,2025,'DEPT','SOCE',f.LineCode,t.FigureType,t.Amount,@source,@url
FROM @figures f
CROSS APPLY (VALUES
    ('AuditedActual',f.AuditedActual),
    ('PriorActual',f.PriorActual),
    ('OriginalBudget',f.OriginalBudget)
) t(FigureType,Amount)
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblNORM_SourceFigure existing
    WHERE existing.ConfigurationReleaseId=@release
      AND existing.StatementCode='SOCE'
      AND existing.LineCode=f.LineCode
      AND existing.FigureType=t.FigureType
);

IF OBJECT_ID('dbo.tblNORM_BudgetFigure','U') IS NOT NULL
BEGIN
    INSERT dbo.tblNORM_BudgetFigure
        (CalculationRunId,StatementCode,LineCode,OriginalBudget,SourceSystem,SourceReference,StatusCode,UpdatedBy)
    SELECT r.CalculationRunId,'SOCE',f.LineCode,f.OriginalBudget,
        N'Published Defence financial statements',@source,'Loaded',N'NORM publication alignment'
    FROM dbo.tblNORM_CalculationRun r
    CROSS JOIN @figures f
    WHERE r.ConfigurationReleaseId=@release AND r.StatusCode='Complete' AND r.IsDeactivated=0
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.tblNORM_BudgetFigure b
          WHERE b.CalculationRunId=r.CalculationRunId
            AND b.StatementCode='SOCE'
            AND b.LineCode=f.LineCode
      );
END;

COMMIT TRANSACTION;

PRINT 'NORM Defence FY2025 publication baselines applied.';
GO
