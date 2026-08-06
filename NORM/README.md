# NORM proof engine

This is the clean FY2025 Proof Engine build for NORM. It implements immutable
trial-balance imports, approved configuration releases, SOCI and SoFP generation,
persisted figure lineage, validations, a genuine test-break run and comparison
against the audited FY2025 statement figures.

The import is intentionally strict. It reads financial year and period coverage
from the file contents, not filenames. ERP workbooks must meet the column
contract and every accepted ERP row must prove that opening balance plus debit
and credit movements equals the ending balance before anything is committed.

FY2025 is a controlled transition exception: one import retains the ROMAN
periods 01-10 text report and the ERP periods 11-12 workbook. NORM rejects a
missing file, wrong year, wrong range, overlap or gap. ERP ending balances are
the statement input because the ERP starting balances already contain the
migrated ROMAN year-to-date position; summing both files would double count
periods 01-10. Other financial years remain normal single-file imports.

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

The promotion script refuses to complete when fewer than 850 FY2025 mappings or
the audited statement baseline are present.

## IIS installation

- Deploy `src` as the existing .NET Framework 4.8 WebForms application.
- Retain `bin/EPPlus-LGPL.dll` version 4.5.3.3.
- Ensure the application-pool identity can read the configured UDL file.
- UAT with Windows Authentication can use
  `NORM.PreparerAccessMode=AllAuthenticated`.
- An anonymous, non-production demonstration site such as WARATAH can use
  `NORM.PreparerAccessMode=Demo`. This mode is ignored when
  `CPlatform.Environment=PROD`; `NORM.DemoUserId` supplies the shared audit
  identity when required.
- Production should use `NORM.PreparerAccessMode=Database` after an access row
  has been installed.
- Except for an explicitly configured non-production demo, keep Windows
  Authentication enabled and Anonymous Authentication disabled.

The public read-only entry is `NORM/NORM_Statements.aspx`. It presents all four
departmental primary statements, PRIMA-aligned generated notes and figure-level
lineage. Preparer functions are under `NORM/NORM_Workspace.aspx`,
`NORM/NORM_Import.aspx` and `NORM/NORM_Reporting.aspx`.

Every completed run can also generate an Excel review pack from the statement
header. The pack contains the frozen run metadata, assurance results, statement
comparison, PRIMA disclosure register, Audit Committee view, team workflow,
full source-to-figure lineage and unmapped-row worklist. It reads persisted run
evidence and does not recalculate figures. The adjacent Word export produces an
editable statement and note set for final entity formatting.

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

1. Open the preparer workspace and select `FY2025 DEPT v1.0`.
2. Supply both `Balances_2025_period_1-to-10_ROMAN.txt` and
   `Balances_2025_period_10plus_ERP.xlsx` in the labelled FY2025 fields.
3. NORM validates period coverage 01-10 plus 11-12, retains both originals and
   their separate SHA-256 fingerprints, commits the ERP ending-balance rows
   atomically and creates an immutable calculation run.
4. Review the assurance panel and the audited-versus-computed comparison shown
   when a figure is opened.
5. Download the review pack and confirm its run fingerprint and **Source files**
   sheet match the statement screen and both supplied originals.
6. Use **Create test break** to create a separate child import with a deliberate
   $48,250 imbalance. The valid parent import remains unchanged.
7. Return to the valid run from the workspace after confirming the blocking
   debit/credit validation fails on the test run.

The independent expected baseline and variance list are recorded in
`FY2025_VERIFICATION.md`. The repeatable checker is
`../tools/NORM_FY2025_ReplayCheck.py`.

Mapping variances are evidence for DFG accounting review. Published amounts are
never substituted for calculated amounts.
