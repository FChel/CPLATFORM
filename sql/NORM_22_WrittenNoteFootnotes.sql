SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
  FY2025 demo written disclosures for two departmental notes only.

  These are the footnotes and accounting-policy wording published beneath
  Notes 1.1B and 3.1A in the Defence Annual Report 2024-25. They are retained
  as editable narrative templates. No current-year figures are seeded: face
  statements and note figures continue to be driven by the current trial
  balance, controlled schedules and the prior-year Start of Year upload.
*/

/* Safely neutralise the superseded numeric demo reconstruction, if an earlier
   draft of migration 22 was ever run in an environment. */
IF OBJECT_ID('dbo.tblNORM_DemoCurrentNoteFigure','U') IS NOT NULL
BEGIN
    UPDATE dbo.tblNORM_DemoCurrentNoteFigure
    SET IsDeactivated=1
    WHERE EntityCode='DEPT' AND FinancialYear=2025
      AND DisclosureCode IN ('N1_1B','N3_1A');
END;

IF OBJECT_ID('dbo.tblNORM_NarrativeTemplate','U') IS NULL
BEGIN
    THROW 50001, 'NORM narrative templates are not installed. Run NORM_04_GovernmentReportingPlatform.sql first.', 1;
END;

DECLARE @Narratives TABLE
(
    DisclosureCode VARCHAR(40),
    NarrativeType VARCHAR(30),
    TemplateText NVARCHAR(MAX)
);

INSERT @Narratives VALUES
(
    'N1_1B',
    'AccountingPolicy',
    N'Footnote 1 - The 2023-24 Inventory consumption comparative has been restated for the change in the point of consumption recognition of GSI inventory. Refer to the Comparative restatement disclosure in the Overview section for further details.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
    N'Footnote 2 - The above lease disclosures should be read in conjunction with the accompanying notes 1.1D, 1.2C, 3.2A and 3.4A. Defence has short-term lease commitments of $4.3m as at 30 June 2025 (2023-24: $4.8m).' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
    N'Accounting Policy - Defence has elected not to recognise Right of Use assets and lease liabilities for short-term leases of assets that have a lease term of 12 months or less and leases of low-value assets (less than $10,000). Defence recognises the lease payments associated with these leases as an expense on a straight-line basis over the lease term.'
),
(
    'N3_1A',
    'AccountingPolicy',
    N'The closing balance of cash held in OPA - special accounts excludes amounts held in trust on behalf of other entities of $1.7m in 2024-25 (2023-24: $1.7m) per footnote 3 in Note 5.2 Special Accounts. In addition, there are amounts held in trust within entity-specific bank accounts of $397.3m in 2024-25 (2023-24: $606.4m). Refer to Note 8.1 Assets Held in Trust.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
    N'Accounting Policy - Cash and cash equivalents includes: (a) cash on hand; (b) demand deposits in bank accounts with an original maturity of three months or less that are readily convertible to known amounts of cash and subject to insignificant risk of changes in value; and (c) cash in special accounts. Cash is measured at its nominal amount. Cash and cash equivalents denominated in a foreign currency are converted using the applicable exchange rate at the reporting date.'
);

UPDATE target
SET target.TemplateText=source.TemplateText,
    target.IsDeactivated=0
FROM dbo.tblNORM_NarrativeTemplate target
INNER JOIN dbo.tblNORM_ConfigurationRelease release
    ON release.ConfigurationReleaseId=target.ConfigurationReleaseId
INNER JOIN @Narratives source
    ON source.DisclosureCode=target.DisclosureCode
   AND source.NarrativeType=target.NarrativeType
WHERE release.EntityCode='DEPT'
  AND release.FinancialYear=2025
  AND release.IsDeactivated=0;

INSERT dbo.tblNORM_NarrativeTemplate
    (ConfigurationReleaseId,DisclosureCode,NarrativeType,TemplateText)
SELECT release.ConfigurationReleaseId,source.DisclosureCode,
       source.NarrativeType,source.TemplateText
FROM dbo.tblNORM_ConfigurationRelease release
CROSS JOIN @Narratives source
WHERE release.EntityCode='DEPT'
  AND release.FinancialYear=2025
  AND release.IsDeactivated=0
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.tblNORM_NarrativeTemplate target
      WHERE target.ConfigurationReleaseId=release.ConfigurationReleaseId
        AND target.DisclosureCode=source.DisclosureCode
        AND target.NarrativeType=source.NarrativeType
  );

PRINT 'FY2025 supplier and cash written note disclosures installed; no current-year figures seeded.';
