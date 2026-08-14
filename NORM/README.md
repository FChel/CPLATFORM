# NORM proof engine

This is the clean FY2025 Proof Engine build for NORM. It implements immutable
trial-balance imports, approved configuration releases, all four primary
statements, conditional PRIMA notes, preparation and publication views,
persisted figure lineage, validations, Excel and Word outputs, and comparison
against the audited FY2025 statement figures.

The import is intentionally strict. It reads financial year and period coverage
from the file contents, not filenames. ERP workbooks must meet the column
contract and every accepted ERP row must prove that opening balance plus debit
and credit movements equals the ending balance before anything is committed.

Each run uses one complete, authoritative and frozen trial balance. The source
may be a supported ERP workbook or ROMAN text report. NORM validates the
financial year, period coverage and balance contract, retains the exact source
bytes and SHA-256 fingerprint, and commits the import atomically. The former
FY2025 split-file transition exception is no longer part of the operating
model.

## SQL installation order

1. If an older NORM prototype exists and its data is not required, preview and
   deliberately configure `../sql/NORM_00_Optional_Reset.sql`.
2. Run `../sql/NORM_01_ProofEngine_Schema.sql`.
3. Run `../sql/NORM_FY2025_mapping.sql` to load the supplied 901-account
   FY2025 Departmental mapping and audited comparison figures.
4. Run `../sql/NORM_02_FY2025_Promote.sql`.
5. For production database-gated access, configure and run
   `../sql/NORM_03_AdminUser_Template.sql`.
6. Run `../sql/NORM_04_GovernmentReportingPlatform.sql` to install the entity
   reporting profile, conditional PRIMA disclosure catalogue, accounting-policy
   narratives and collaborative review workflow.
7. Run `../sql/NORM_05_StatementDemoEnhancements.sql` for statement, note,
   journal, validation and manual-disclosure demonstration data.
8. Run `../sql/NORM_06_PreparationControlCentre.sql` for the expanded control
   centre, materiality, workflow, import-impact features and published FY2025
   comparative and original-budget statement baselines.
9. Run `../sql/NORM_07_DefencePublicationAlignment.sql` to install the published
   statement-of-changes-in-equity baseline and budget seed rows.

The promotion script refuses to complete when fewer than 850 FY2025 mappings or
the audited statement baseline are present.

## IIS installation

- Deploy the repository application root as the existing .NET Framework 4.8
  WebForms application, preserving the environment-specific `web.config` and
  UDL connection file unless an approved configuration change is in scope.
- Retain `bin/EPPlus-LGPL.dll` version 4.5.3.3.
- Ensure the application-pool identity can read the configured UDL file.
- UAT with Windows Authentication can use
  `NORM.PreparerAccessMode=AllAuthenticated`.
- Production should use `NORM.PreparerAccessMode=Database` after an access row
  has been installed.
- Keep Windows Authentication enabled and Anonymous Authentication disabled
  for the NORM application path.

The public read-only entry is `NORM/NORM_Statements.aspx`. It presents all four
departmental primary statements, PRIMA-aligned generated notes and figure-level
lineage. Preparer functions are under `NORM/NORM_Workspace.aspx`,
`NORM/NORM_Import.aspx` and `NORM/NORM_Reporting.aspx`.

Every completed run can generate an Excel financial-statements workbook with a
linked contents sheet, a separate tab for each primary statement and a separate
tab for every selected note. The Excel review pack separately contains frozen
run metadata, assurance results, statement comparison, disclosure register,
Audit Committee view, workflow, full source-to-figure lineage and the unmapped
row worklist. Both outputs read persisted run evidence and do not recalculate
figures. The adjacent Word export produces an editable statement and note set.

The statement header also lists every retained source file. Preparer-authorised
users can download the exact original bytes; the download is tied to the
completed run and recorded in `tblNORM_AuditEvent`. ERP-backed lineage rows can
open SAP Fiori **Display Line Items in General Ledger** (F2217), filtered to
company code, G/L account and fiscal year. Configure `NORM.SapBaseUrl`, or let
NORM reuse `LPPI.SapBaseUrl`, and optionally override
`NORM.SapGlLineItemsIntent`. A trial-balance balance is aggregated, so the SAP
link is a live investigation route. The retained original, SHA-256 fingerprint
and persisted lineage remain the frozen run evidence.

The in-app `NORM_Help.aspx` page is the functional source of truth for
preparers and reviewers.

## FY2025 verification run

1. Open the preparer workspace and select the approved FY2025 departmental
   configuration release.
2. Supply one complete authoritative FY2025 trial balance in a supported ERP
   workbook or ROMAN text format.
3. NORM validates the file contract and balance, retains the exact original and
   its SHA-256 fingerprint, commits the rows atomically and creates an immutable
   calculation run.
4. Review the assurance panel and the audited-versus-computed comparison shown
   when a figure is opened.
5. Download the review pack and confirm its run fingerprint and **Source files**
   sheet match the statement screen and supplied original. The retained source
   download is the authoritative TB to provide to a reviewer who needs to
   reproduce or refine mappings.
6. Use **Create test break** to create a separate child import with a deliberate
   $48,250 imbalance. The valid parent import remains unchanged.
7. Return to the valid run from the workspace after confirming the blocking
   debit/credit validation fails on the test run.

The independent expected baseline and variance list are recorded in
`FY2025_VERIFICATION.md`. The repeatable checker is
`../tools/NORM_FY2025_ReplayCheck.py`.

Mapping variances are evidence for DFG accounting review. Published amounts are
never substituted for calculated amounts.
