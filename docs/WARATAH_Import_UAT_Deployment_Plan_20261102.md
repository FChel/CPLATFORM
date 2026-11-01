# WARATAH NORM import — controlled deployment and UAT plan

Prepared 2 November 2026 (Australia/Sydney). **Deployment readiness: BLOCKED pending the gates below.** This is a review plan, not execution authorisation. WARATAH UAT is the target; no separate local SQL/IIS environment is proposed.

## Evidence and gates

- Working checkout: `steve/norm-onboarding`, HEAD `8c33b163a4e2e6ff84e6f89d4259dd96086bd53a`.
- Read-only `git ls-remote` and GitHub compare identify remote `uat` as `b23d722d71f090d6ef8ee3a417eae1d1c6684ba0`: four commits ahead, 42 changed paths, covering LPPI FAQ synchronisation, POD client review and the added POD validation step. No path overlaps the candidate list below. No direct file conflict is evident; a real integration and its tests remain outstanding. No fetch or refs were changed during preparation.
- Latest observed deployment of that remote baseline succeeded: https://github.com/FChel/CPLATFORM/actions/runs/39546026721 . This is **not** a deployment of the uncommitted NORM work. Current automatic-deployment toggle/environment approval settings must be checked in GitHub before merging.
- All four increments are present: FR-016 service/storage/audit gate and exact engine check; 1A signatures/detail/control parsing; B1 separate company persistence/copying/lineage; B2 session preview/confirmation and separate calculation outcome.
- Combined source review found no unrelated application modification, new table/framework, stream/currency configuration, annual rollover or replacement feature. Existing calculation changes are confined to the approved FR-016 exact-zero check. The direct backend import API and legacy APIs remain for compatibility/tests; browser acceptance uses the guarded confirmation path.
- Re-run: 1A **44**, B1 **14 service scenarios + calculation regression + six helper assertions**, B2 **27**, FR-016 **34 service + 9 engine**; production and full ASP.NET compilation pass. Existing CS0162 warning remains at `eJET/eJET.aspx.cs:125`. `git diff --check` passes.
- Deployment-request substitute tests pass. Deployment/rollback/preservation/checksum/SQL-gate safety tests also pass against an explicitly labelled current-HEAD baseline package fixture with fake IIS/S3. Candidate package/rollback tests and the new upstream `tools/Test-Pord.ps1` must pass on the integrated committed candidate; the baseline fixture is not evidence for that candidate.
- No WARATAH connection or SQL, including read-only SQL, was executed. Current schema, installed SQL digest, backup recoverability, server configuration and FY2026 records are **unknown**, not assumed absent or compatible.
- The existing FY2026 script explicitly says **Never run on WARATAH**. It is a reference/template only. A reviewed WARATAH-UAT operator variant is required; do not override that instruction by simply filling its isolated-database parameter.

Approval gates: **G1** feature-branch commit/integration/PR preparation; **G2** administrator read-only WARATAH inventory; **G3** backup, migration and SQL-baseline recording; **G4** validated PR merge/deployment window; **G5** named FY2026 initial configuration creation/approval and browser UAT. Stop at a failed gate. Coordinate the window with other developers because WARATAH is shared.

## Exact candidate allowlist

Use this list, not `git add .`, `git add docs` or `git add tools/tests`:

```powershell
$candidateFiles = @(
 'App_Code/ErpTrialBalanceImporter.cs',
 'App_Code/NORMHelper.cs',
 'App_Code/NORMImportModels.cs',
 'App_Code/NORMImportService.cs',
 'App_Code/NORMMappingManagement.cs',
 'App_Code/NORMReviewPack.cs',
 'App_Code/RomanTrialBalanceImporter.cs',
 'App_Code/StatementEngine.cs',
 'NORM/NORM_Import.aspx',
 'NORM/NORM_Import.aspx.cs',
 'NORM/NORM_Statements.aspx.cs',
 'js/norm.js',
 'sql/NORM_CompanyLedger_Increment1B.sql',
 'sql/NORM_FY2026_Isolated_Test_Configuration.sql',
 'tools/tests/FR016/Run.ps1',
 'tools/tests/FR016/Tests.cs',
 'tools/tests/FR016/TestSupport.cs',
 'tools/tests/Increment1A/Run.ps1',
 'tools/tests/Increment1A/Tests.cs',
 'tools/tests/Increment1B/Run.ps1',
 'tools/tests/Increment1B/Tests.cs',
 'tools/tests/Increment1B/EngineTests.cs',
 'tools/tests/Increment1B/DisplayTests.cs',
 'tools/tests/Increment1B2/Run.ps1',
 'tools/tests/Increment1B2/Tests.cs',
 'tools/tests/Increment1B2/PageHarness.cs',
 'docs/FR016_Code_Walkthrough.md',
 'docs/FR016_Implementation_and_Verification_20261019.md',
 'docs/Increment_1A_ERP_Parsing_Verification.md',
 'docs/Increment_1B_Stage_B1_Handover.md',
 'docs/Increment_1B_Stage_B2_Handover.md',
 'docs/WARATAH_Import_UAT_Deployment_Plan_20261102.md'
)
```

Exclude `docs/analysis/`, `docs/requirements/`, attachments/source workbooks, temporary binaries/packages and unrelated files. The 32-path list contains only the reviewed implementation, tests, two existing SQL scripts and six reports. It does not change `web.config`, UDLs or deployment configuration. Retain all upstream LPPI/POD/workflow changes during integration.

## Git and candidate verification — after G1

Run in `C:\Development\NORM`, using the allowlist above:

```powershell
git switch -c codex/norm-trial-balance-uat
git add -- $candidateFiles
git diff --cached --check
git diff --cached --name-only
git diff --cached
git commit -m "NORM: validate and confirm trial balance imports with separate company and ledger"
git fetch origin uat
git merge --no-edit origin/uat
```

This preserves the already developed work and integrates the latest shared baseline on the feature branch. If upstream advances or an unexpected conflict appears, stop, review and rerun the checks; do not choose an entire old application tree over `uat`.

```powershell
& './tools/tests/Increment1A/Run.ps1'
& './tools/tests/Increment1B/Run.ps1'
& './tools/tests/Increment1B2/Run.ps1'
& './tools/tests/FR016/Run.ps1'
& './tools/Test-Pord.ps1'
$candidateCommit = (git rev-parse HEAD).Trim()
$candidateOutput = Join-Path $env:TEMP ('norm-waratah-' + $candidateCommit)
& './tools/deploy/New-WaratahPackage.ps1' -Commit $candidateCommit -OutputDirectory $candidateOutput -Compile
& './tools/deploy/Test-DeploymentSafety.ps1' -PackageInfo (Join-Path $candidateOutput 'package.json')
& './tools/deploy/Test-DeploymentRequest.ps1'
Get-Content -LiteralPath (Join-Path $candidateOutput 'package.json')
git push -u origin codex/norm-trial-balance-uat
```

Use a fresh output directory on reruns. Stop on any command/test failure. Open a GitHub PR **into `uat`**, attach the evidence and this plan, and wait for **WARATAH validation**. Do not merge at this point. `gh` is not installed in the inspected environment; use the existing GitHub UI rather than installing tooling. Feature-branch pushes do not deploy.

## WARATAH inventory — after G2, before database changes

An authorised administrator identifies the existing application connection locally on WARATAH using `NORM.ConnectionString` or the existing UDL. Do not print/store credentials in the review log. Record the actual SQL server/instance and database, operator, time, installed application commit, and the current `C:\ProgramData\CPLATFORM-Deploy\database-baseline.json` and database-history reference. Repository SQL is not proof of deployed schema.

Run the following read-only SQL in that **explicitly confirmed database**, under a read-only inspection grant. Do not run this during preparation:

```sql
SELECT CONVERT(sysname,SERVERPROPERTY('ServerName')) AS ActualServer,
       DB_NAME() AS ActualDatabase,
       SERVERPROPERTY('ProductVersion') AS SqlVersion;

SELECT OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName,
       OBJECT_NAME(c.object_id) AS TableName,
       c.name, TYPE_NAME(c.user_type_id) AS DataType,
       c.max_length,c.is_nullable
FROM sys.columns c
WHERE c.object_id IN (OBJECT_ID('dbo.tblNORM_TrialBalanceRow'),
                     OBJECT_ID('dbo.tblNORM_EntityLedger'),
                     OBJECT_ID('dbo.tblNORM_ConfigurationRelease'))
ORDER BY TableName,c.column_id;

SELECT k.name,i.name AS IndexName,ic.key_ordinal,c.name AS ColumnName
FROM sys.key_constraints k
JOIN sys.indexes i ON i.object_id=k.parent_object_id
                 AND i.index_id=k.unique_index_id
JOIN sys.index_columns ic ON ic.object_id=i.object_id
                         AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE k.parent_object_id=OBJECT_ID('dbo.tblNORM_EntityLedger')
ORDER BY k.name,ic.key_ordinal;

SELECT name,OBJECT_SCHEMA_NAME(parent_object_id) AS ParentSchema,
       OBJECT_NAME(parent_object_id) AS ParentTable,
       OBJECT_NAME(referenced_object_id) AS ReferencedTable
FROM sys.foreign_keys
WHERE parent_object_id IN (OBJECT_ID('dbo.tblNORM_TrialBalanceRow'),OBJECT_ID('dbo.tblNORM_EntityLedger'))
   OR referenced_object_id IN (OBJECT_ID('dbo.tblNORM_TrialBalanceRow'),OBJECT_ID('dbo.tblNORM_EntityLedger'));

SELECT * FROM dbo.tblNORM_EntityLedger
WHERE EntityCode='DEPT' AND FinancialYear IN (2025,2026);

SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode,
       ReleaseLabel,StatusCode,IsDeactivated,ContentHash,
       ParentConfigurationReleaseId,ReviewedBy,ReviewedUtc,
       ApprovedBy,ApprovedUtc
FROM dbo.tblNORM_ConfigurationRelease
WHERE EntityCode='DEPT' AND FinancialYear IN (2025,2026);

SELECT ImportId,ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,
       SourceFileName,SourceFileHash,[RowCount],StatusCode,
       IsTestBreak,IsDeactivated,ImportedUtc
FROM dbo.tblNORM_Import
WHERE EntityCode='DEPT' AND FinancialYear=2026;

SELECT tb.ImportId,tb.SourceLedger,COUNT_BIG(*) AS SourceRows
FROM dbo.tblNORM_TrialBalanceRow tb
JOIN dbo.tblNORM_Import i ON i.ImportId=tb.ImportId
WHERE i.EntityCode='DEPT' AND i.FinancialYear=2026
GROUP BY tb.ImportId,tb.SourceLedger;

IF COL_LENGTH('dbo.tblNORM_TrialBalanceRow','SourceCompanyCode') IS NOT NULL
    EXEC sys.sp_executesql N'
      SELECT tb.ImportId,tb.SourceCompanyCode,tb.SourceLedger,COUNT_BIG(*) AS SourceRows
      FROM dbo.tblNORM_TrialBalanceRow tb
      JOIN dbo.tblNORM_Import i ON i.ImportId=tb.ImportId
      WHERE i.EntityCode=''DEPT'' AND i.FinancialYear=2026
      GROUP BY tb.ImportId,tb.SourceCompanyCode,tb.SourceLedger;';
```

Also verify the existing core import/file/row/audit/calculation/lineage/validation tables, mapping/statement tables and browser reporting dependencies against the reviewed release. Check source-file period/content columns, mapping-management metadata and administrator access. Capture FY2025 import/configuration controls before the change. Do not recreate missing tables by running every SQL script.

Compatibility is conditional: both target tables must exist; existing company columns, if present, must be nullable VARCHAR(20); the legacy uniqueness key must match the migration's expected three columns, or the new key must match its four columns. Unexpected constraints, indexes, dependencies or schema drift require DBA review before execution.

**Existing FY2026 sources are a stop condition for the proposed first acceptance.** Do not delete/deactivate them, change company/year or create another release to evade replacement protection. Reuse an existing approved configuration only after validating its precise scope and evidence. An existing Draft/version must not be overwritten silently. Plan any historical-data treatment separately.

## Backup, migration and SQL digest — after G3

1. Agree a shared change window; prevent concurrent schema/configuration writes and coordinate UAT imports. Record the currently successful application run and preserve server-owned config/UDL/evidence.
2. DBA takes a uniquely named full SQL backup using the confirmed target database, e.g. `BACKUP DATABASE [actual UAT database] TO DISK=N'<approved unique backup path>' WITH COPY_ONLY,CHECKSUM;`. Verify with `RESTORE VERIFYONLY ... WITH CHECKSUM`, confirm off-instance availability and an established restore procedure. VERIFYONLY is not a substitute for demonstrated recovery arrangements. Deployment file backups do not back up SQL.
3. Transfer the reviewed migration from the pinned feature commit; verify its checksum. Execute **only** `sql/NORM_CompanyLedger_Increment1B.sql` against the confirmed database. Do not execute the FY2026 template, reset, promotion or other SQL scripts at this step.
4. Verify the two nullable columns, unchanged historical values and new uniqueness key. Perform permission/uniqueness probes in transactions that are rolled back. Stop on errors; the migration's transaction rolls back its own work on failure.
5. Obtain the exact candidate SQL source digest from `package.json` generated by `New-WaratahPackage.ps1` for the pinned integrated feature commit. Do not use a working-tree file hash, guess the digest or reuse the September initial digest. The digest covers **all tracked SQL paths and contents**, including templates not executed. The DBA approval must distinguish the company migration applied from the FY2026 isolated template reviewed but not run.
6. Record the approved digest through the existing administrator installer. In a reviewed administrator folder containing the pinned installer/runtime scripts, copy the current installed target config to a separate input file, then run:

```powershell
# Administrator on WARATAH, after backup/migration verification.
Copy-Item -LiteralPath 'C:\ProgramData\CPLATFORM-Deploy\config.json' -Destination '<reviewed admin folder>\target.config.json'
& '<reviewed admin folder>\Install-WaratahServer.ps1' `
  -ConfigSource '<reviewed admin folder>\target.config.json' `
  -SqlSha256 '<exact candidate package.json sqlSha256>' `
  -DatabaseApproval '<change reference; target DB; backup reference; migration applied and verified; isolated configuration template reviewed/not executed>'
```

The input config must not be the same file as the installer's destination. Review installed-script hashes before refreshing them. This step appends `database-history.jsonl` and updates `database-baseline.json`; never edit the digest merely to bypass deployment. If `uat` acquires other SQL changes before merge, stop and reconcile the actual candidate digest and their approvals.

## Application integration and deployment — after G4

Confirm `WARATAH_AUTO_DEPLOY=true`, the `waratah` environment accepts `uat`, required checks/approvals pass, and the database gate above is complete. Merge the reviewed feature PR into `uat` through GitHub. Preserve all latest upstream changes; do not push the old checkout directly to `uat`.

Record the merge commit. In GitHub Actions open **Deploy WARATAH**, verify that exact merge SHA, and wait for completed **success**. Check packaging/compilation, OIDC, S3 upload, SSM result and HTTP health checks. Record the actual run URL and SSM command ID. A successful source commit, PR validation, skipped run or old successful deployment is not a deployment of this work. If automatic deployment is disabled or skipped, stop and obtain authorisation for the existing manual workflow on that exact `uat` commit.

Verify authenticated NORM/Mapping/Reporting access and preserve the existing `web.config`, UDL connections, uploads and evidence. Health HTTP 200 alone does not establish import or SQL correctness.

## FY2026 UAT provisioning and initial approval — after G5

Do not run `NORM_FY2026_Isolated_Test_Configuration.sql` unchanged on WARATAH. Prepare a reviewed operator copy from the pinned candidate, retaining its transaction and data-copy statements, with only these target/scope edits:

- Replace the isolated-only instruction with explicit **WARATAH UAT only** authorisation and the change reference.
- Replace `@ConfirmedIsolatedDatabase` with `@ConfirmedUatDatabase`; require both an explicit `@ExpectedServer` and actual database match using `SERVERPROPERTY('ServerName')`/`DB_NAME()` before DML. Default these parameters and baseline ID to NULL so accidental execution stops.
- Supply the inspected approved FY2025 DEPT parent ID and reviewed-configuration acknowledgement. Use a reviewed unused version, proposed `uat-fy2026-import-20261102`.
- Replace isolated-only labels/notes with **WARATAH UAT — FY2026 Departmental / AUD; no publication authority**, consistently in insert and rerun checks. Keep the existing FY2025 parent and all its data/approvals unchanged.
- Preserve the exact copy scope: active reviewed account-map and statement-line configuration only. Do not copy PublishedFigure, SourceFigure, journals, manual disclosures, approvals or calculation results. No new stream/currency fields.
- Check for FY2026 sources/conflicting versions before execution; recheck permitted pair and copied content after any rerun. Creation is Draft only. Existing matching Draft reruns must not overwrite edits.

The operator copy is reviewed change evidence outside the application package; it is not permission to alter the server database during this preparation stage.

For first approval, use a specifically approved **initial test-baseline administrative operation**, not `NORMMappingManagement.Approve` with a fabricated change. That method requires a recorded mapping change; no application amendment is proposed. Named accounting reviewer/administrator approve the exact initial content and its warnings, and the DBA records their fresh approval in existing release/audit tables, following the initial-baseline provisioning precedent rather than pretending a workbook edit occurred.

Exact administrative transaction requirements:

1. Confirm expected WARATAH server/database, change reference, target release ID, FY2026 DEPT Draft status, intended FY2025 parent, and active 1000/0L permission. Verify the named approver is authorised; record actual DB operator separately. Fail on a mismatched or already approved target.
2. Obtain business sign-off on the expected hash and named scope before opening the approval transaction; do not hold SQL locks while waiting for human review. Begin a serializable transaction. Lock the target release and its active mapping/template/permission rows for the duration. Validate the existing mapping integrity checks (nonempty configuration, no duplicate active GLs, no invalid non-null account types, and no supplied statement destinations missing from templates). **Missing source-account mappings are not a failure.** Review applicable FY2026 statement configuration explicitly.
3. Compute the same mapping hash as `NORMMappingManagement.MappingHash`: query active rows ordered by `GlCode`, select `GlCode` and `ISNULL(AccountType,'')`, `ISNULL(StatementLine,'')`, `ISNULL(NoteSubLine,'')`, `ISNULL(CashFlowClass,'')`; concatenate each row with `|` separators and a single LF after every row; SHA-256 the UTF-8 bytes and use lowercase hex. Match the independently reviewed expected hash before updating. Do not substitute SQL's UTF-16 HASHBYTES input.
4. On the same connection/transaction, bind the reviewed values to the following update and audit statements. Require exactly one updated release. No MappingChange row is inserted, no FY2025 row is updated, and no financial data is copied.

```sql
-- Parameters supplied only after explicit initial-baseline approval.
UPDATE dbo.tblNORM_ConfigurationRelease
SET StatusCode='Approved',ContentHash=@ReviewedMappingHash,
    ReviewedBy=@ActualReviewer,ReviewedUtc=SYSUTCDATETIME(),
    ApprovedBy=@ActualApprover,ApprovedUtc=SYSUTCDATETIME()
WHERE ConfigurationReleaseId=@TargetReleaseId
  AND FinancialYear=2026 AND EntityCode='DEPT'
  AND ParentConfigurationReleaseId=@ReviewedParentReleaseId
  AND StatusCode='Draft' AND IsDeactivated=0;
IF @@ROWCOUNT<>1
    THROW 51000, 'Initial test-baseline approval target changed.', 1;

INSERT dbo.tblNORM_AuditEvent
    (EventCode,EntityType,EntityId,DetailText,PerformedBy)
VALUES
    ('INITIAL_TEST_BASELINE_APPROVED','ConfigurationRelease',
     CONVERT(VARCHAR(20),@TargetReleaseId),
     @ApprovalEvidenceDetail,SUSER_SNAME());
```

`@ApprovalEvidenceDetail` must contain the change reference, target/parent IDs, reviewed hash, named reviewer/approver, UAT-only scope and explicit statement that this is initial baseline approval with no fabricated mapping edit. Commit only under the already recorded business approval. Roll back on validation/hash/target mismatch and submit changed content for fresh review. Keep the approved operator-copy checksum, hash export and transaction/audit evidence. This is a procedure for administrator review, not an executable approval already performed.

Then open `https://cplatform.beeqira.com.au/NORM/NORM_Import.aspx` as the authorised preparer. Confirm the named FY2026 DEPT approved release appears in the existing list. If absent, inspect its status/deactivation and permissions; do not relabel FY2025 or relax source authorisation. A year-workspace change through existing setup, if needed, requires separate scope acknowledgement and must not copy prior financial data.

## Browser UAT

Before testing, record matching import counts/IDs and the approved FY2026 release. Preserve a FY2025/unrelated-module smoke-test baseline. Coordinate so no other user accepts the target company/year during the test.

1. Verify the original `Defence TB - ERP Format FY26.xlsx` SHA-256 is `1C4BAD018973963AA67FF37689EB1ABACA7009661B92681415CEB12CDC06C195`. Keep the original unchanged.
2. Select the approved FY2026 DEPT configuration and upload the file. There must be no parser selector. Choose **Inspect file**.
3. Expect Defence ERP; entity DEPT; FY2026; company 1000; ledger 0L; periods 01–12; 984 rows; net AUD 0.00. Debit and absolute credit ending balances are 268,992,152,391.65 each. All detail/control/precision checks pass. Record the actual unmapped count; no requirement that it be zero.
4. Administrator verifies no new accepted import, TB rows or calculation exists before confirmation. Choose **Cancel preview**; verify the same. Old/stale confirmation must not accept anything.
5. Inspect again. Before accepting the original, test an unbalanced **copy**, never edit the original: on row 28 add AUD 0.01 to debit movement F and ending H; on both total rows 1012/1013 add AUD 0.01 to F and H. This preserves row arithmetic/control agreement but produces a nonzero TB. Inspection must reject exact-zero failure and offer no usable acceptance. Verify no accepted source/calculation appeared.
6. Reinspect the original and choose **Confirm acceptance** once. Capture the accepted import ID and the separate calculation result. If calculation fails, the source is still accepted: do not re-upload. Investigate the failed calculation independently.
7. Administrator verifies exactly 984 rows for that import; each has company 1000 and actual ledger 0L; no subtotal/grand-total account rows; periods and file bytes retained; net exactly zero; all original accounts retained including unmapped ones. Stored GL mapping keys use the established DCOA suffix, while original identifiers remain in the retained workbook/hash evidence.
8. Check `IMPORT_CREATED`, balance/storage validation audit events, source hash and retained file bytes. Download the retained source through the normal completed-run evidence link when calculation completes; compare SHA-256. If calculation failed, verify retained bytes administratively and log the separate UI/download limitation rather than claiming a completed-run download passed.
9. Double click/refresh/resubmit the old confirmation and attempt a fresh inspection/confirmation of the same company/year: no second accepted source. Do not delete/deactivate the first import to repeat testing. Configuration/file changes after preview and expired/stale tabs must require another inspection.
10. Review unmapped rows through existing statement lineage/review-pack/mapping exports. Their existence must not invalidate acceptance. Check SAP link company is 1000, not 0L. Do not navigate to or modify SAP as part of this test.
11. Read/retrieve the pre-existing FY2025 runs, retained sources, mappings and approvals and compare pre-test controls. Confirm the existing FY2025 transition API is unchanged; this single-file page does not implement its two-file process. Smoke-test FinHub, LPPI FAQ/reviewer access and POD review screens from the new `uat` baseline without sending mail or changing unrelated business data.

Pass source acceptance and financial statement calculation separately. Do not equate successful calculation with approved statements or publication.

## Recovery and stop conditions

- Before commit: leave the reviewed working tree intact. Before merge: a failed integration/check is contained on the feature branch; no WARATAH deployment occurs.
- Migration failure: transaction rollback, verify the actual schema and stop. Do not refresh the SQL digest or merge until resolved.
- Deployment failure: existing deployment tooling restores managed application files and records the failed run. Inspect SSM/run state; do not clear `pending.json`, discard backups or rerun blindly. Recheck SQL separately because application rollback does not undo migration.
- Before new-source acceptance, the additive schema can ordinarily remain while recovering old application files, subject to verified compatibility and the approved SQL digest.
- After company/actual-ledger rows exist, **do not blindly reinstall the old pre-company reader**: its SAP links treat SourceLedger as company. Keep the compatible reader, stop further imports/coordinate maintenance, and diagnose. Full database restore requires explicit DBA/shared-environment approval and reconciliation of other developers' writes since backup. Never drop company data, recreate the old uniqueness constraint over valid shared ledgers, or delete FY2026 evidence to force rollback/retest.
- Re-running an older workflow can be blocked by the newer SQL source digest. Resolve actual compatibility and record the approved recovery baseline through the existing process; do not bypass checksum controls.
- Configuration approval failure: roll back that transaction and leave the Draft and FY2025 records unchanged. Existing-source conflicts are not resolved by deletion or automatic replacement.

No commit, push, fetch, merge, SQL execution, configuration approval, WARATAH connection or deployment occurred during this preparation. Only read-only Git/GitHub inspection, local substitute tests/compilation and this report were performed.
