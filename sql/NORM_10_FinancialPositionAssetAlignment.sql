/* NORM 10 - controlled FY2025 Statement of Financial Position asset classes.
   Adds the audited, comparative and original-budget face-statement values used
   to present and reconcile the mapped trial-balance asset lineage. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CPlatform];
GO

IF OBJECT_ID('dbo.tblNORM_SourceFigure','U') IS NULL
    THROW 51010, 'Run NORM_06_PreparationControlCentre.sql before NORM_10_FinancialPositionAssetAlignment.sql.', 1;

DECLARE @release INT = (
    SELECT TOP 1 ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease
    WHERE FinancialYear=2025 AND EntityCode='DEPT' AND StatusCode='Approved' AND IsDeactivated=0
    ORDER BY ConfigurationReleaseId DESC
);
IF @release IS NULL THROW 51011, 'The approved FY2025 DEPT NORM configuration release was not found.', 1;

DECLARE @source NVARCHAR(500) = N'Defence Annual Report 2024-25, departmental Statement of Financial Position';
DECLARE @url NVARCHAR(1000) = N'https://www.defence.gov.au/sites/default/files/2025-10/Defence-Annual-Report-2024-25.pdf';
DECLARE @figures TABLE(LineCode VARCHAR(200), AuditedActual DECIMAL(19,3), PriorActual DECIMAL(19,3), OriginalBudget DECIMAL(19,3));
INSERT @figures VALUES
('PPE_LAND',7551266,7033480,6150905),
('PPE_BUILDINGS',21320643,19665541,20579589),
('PPE_SPECIALIST_MILITARY_EQUIPMENT',93662080,88628062,91851358),
('PPE_INFRASTRUCTURE',10591882,9446765,8163428),
('PPE_PLANT_AND_EQUIPMENT',1801482,1641318,1628742),
('PPE_HERITAGE_AND_CULTURAL_ASSETS',403210,401671,404289),
('PPE_INTANGIBLES',3710086,3622578,2654077);

INSERT dbo.tblNORM_SourceFigure
    (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl)
SELECT @release,2025,'DEPT','SOFP',f.LineCode,v.FigureType,v.Amount,@source,@url
FROM @figures f CROSS APPLY (VALUES ('AuditedActual',f.AuditedActual),('PriorActual',f.PriorActual),('OriginalBudget',f.OriginalBudget)) v(FigureType,Amount)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblNORM_SourceFigure x WHERE x.ConfigurationReleaseId=@release
    AND x.StatementCode='SOFP' AND x.LineCode=f.LineCode AND x.FigureType=v.FigureType AND x.IsDeactivated=0);

PRINT 'NORM FY2025 financial-position asset-class baselines applied.';
GO
