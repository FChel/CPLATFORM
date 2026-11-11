# NORM Package 1A Step 3 — WARATAH cutover plan

11 November 2026 · Living record · Readiness inspection only.

## Finding

**Ready for a separately authorised migration-first cutover.** The live Step 2 baseline satisfies the prepared Step 3 migration. No technical schema, retained-data or cross-module dependency blocker was found. Reuse the established PR #6 / Steps 1–2 procedure unchanged, with a short, owner-confirmed NORM write restriction through immediate compatibility verification. The deployer performs its ordinary brief application-pool interruption; do not manually stop IIS or add maintenance files/features.

This review executed read-only SSM/SQL and local source inspection only. It did not migrate, back up, restore, deploy, change years/names/assignments, commit, push, merge or create a PR. The 236 passing isolated checks and successful full ASP.NET compilation remain the implementation evidence; no application or migration code changed in this review.

## Verified live baseline

Successful read-only SSM `a732c3be-5221-489b-ad6c-0535fe8eeac6` (11 November, 03:35:45 UTC / 14:35:45 AEDT) confirmed:

| Item | Verified result |
|---|---|
| Target / identity | WARATAH / CPlatform, ONLINE; integrated SQL login `WORKGROUP\WARATAH$`, using the established SYSTEM SSM connection. No authentication or permission reinvestigation. |
| Deployed revision / rollback base | `d92492ebdb2f34eb4b16618226cc9a4f42d88884` (Step 2). |
| Manifest and registered SQL digest | Both `bcffcd360838e6c8c2f738a4a53884e3c4e67d7aec9a43a6aa98bbed1a40df48`; this matches the base SQL sources with established Windows CRLF packaging. |
| Deployment state | No pending.json or app_offline.htm. |
| Migration prerequisites | YearSetupDocument and UserEntity present; CoverageStart/End are DATE (3 bytes), IsFullYear BIT (1 byte); run ReportingEntityName absent. |
| Current designation | DEPT YearSetupId 3, FY2026 current. FY2025 (ID 2) and FY2024 (ID 1) historical. |
| Approved configuration | Release 3, FY2026 DEPT, `uat-fy2026-import-20261102`, active/Approved. Population ID 3: company 1000, ledger 0L, Departmental, required. |
| Accepted baseline | 8 imports, 9 files, 7,849 trial-balance rows, 8 runs, 74 audit events, 7 setup documents, 492 setup figures. |
| Current original source | Import 7; DEPT / FY2026 / 1000 / 0L / Departmental; 984 rows. |
| Backfill mapping | 1 FY2025 run → `Department of Defence - Departmental`; 7 FY2026 runs → `Defence Departmental - WARATAH UAT`. No unmatched run/import/entity-name record. |

Supplementary SSM `8c7bbf0a-4648-4948-bea6-b5341b2d63e7` (03:37:49–52 UTC) verified all seven retained setup documents' hashes and sizes, no duplicate active document versions per setup/type, and no drift in the eleven existing application files being changed. The installed deployer SHA-256 is `05d557af78f33cda61f4c0961a9b35218f064084d94934a404cfc79b141d2eb2`, matching the established HEAD Git blob / LF-normalised working source. Deployment scripts have no candidate changes.

The first inspection, SSM `acda1ac8-0400-426f-a2ac-d33ffdf5c52b`, stopped on an invalid inspection query that placed scope columns on Import rather than TrialBalanceRow. The corrected read-only query above succeeded. This was an inspection-query error, not a database baseline defect; no write occurred.

## Exact migration and application impact

`sql/NORM_Stage1_Package1A_Step3.sql` adds only `dbo.tblNORM_CalculationRun.ReportingEntityName NVARCHAR(200) NULL` and backfills the eight existing runs from their recorded import entity/year registration, falling back to entity code if a name is absent. All eight live records have a matching name. The column preserves headings on subsequent name edits. Existing data alteration is limited to populating this new column; no source conversion, reset, reseed or change to existing amounts/fingerprints/keys/dates is required.

The script has explicit database confirmation, prerequisite/rerun guards, XACT_ABORT and one transaction around addition/backfill. It does not change a current year, entity name, release, population, assignment, retained original, FK, index or check/default constraint. ALTER TABLE takes a schema-modification lock and backfill takes short update locks on this NORM table.

Live CalculationRun has its existing identity PK, unique RunGuid key and enabled filtered `IX_tblNORM_Run_Import` (ImportId, CalculationRunId; includes StatusCode/CompletedUtc; active rows). Its trusted/enabled status check remains Running/Complete/Failed. The existing unique filtered YearSetup current index is enabled, keyed by EntityCode with IsCurrent=1 and IsDeactivated=0. No relevant table/database DDL trigger was returned. All ten FKs touching CalculationRun are enabled/trusted NORM relationships: Import/Release outbound; Lineage, LineResult, ValidationResult, BudgetFigure, ManualInput, CashFlowJournal, RunNarrative and WorkflowItem inbound. None changes. Visible SQL dependencies comprise the table's own constraints; no matching synonym or explicit cross-database CalculationRun dependency was found in the accessible online databases. Repository application references outside NORM were absent. Catalog review cannot establish the absence of external/ad hoc dynamic SQL, but no such dependency is evidenced.

Application changes are confined to explicit authorised entity/year setup selection; approval and stale-state checks; audited name editing; retained-document listing/download; recording a run-name snapshot; and selecting recorded run year/name in Statements, Reporting, Excel, Word and related existing comparative readers. Calculations, source identity, coverage rules and other CPLATFORM modules are unchanged. The new handler uses the existing SQL document storage and entity checks; no new storage, role, stream assignment or current-year mechanism is added.

The saved YearSetup designation remains sole current-year authority. Exact-year approval is required for activation/upload. Reporting names remain year-scoped in ReportingEntity; changes affect new/recalculated runs. Existing runs retain their snapshot and recorded import year. Backfill cannot reconstruct a name overwritten before Step 3; the live backfill values above match the available approved year registrations. Historical FY2024 retained documents can be retrieved without activating FY2024 or inventing configuration for it.

## SQL-to-application interval

**Step 2 is compatible with the additive schema.** Its run INSERT supplies an explicit five-column list and omits the new nullable field; its readers use named columns. The backfill changes no existing field, so existing Step 2 reads/calculations retain their old behaviour. Schema locks may cause brief waiting/timeouts during migration; they do not justify a full manual CPLATFORM shutdown.

Keep NORM import, mapping/recalculation, Year Setup/configuration and other NORM writes suspended during the interval. Otherwise Step 2 could insert a run with a NULL snapshot after backfill, or change the year using its older upload sequence. This is an avoidable lineage/control gap rather than demonstrated cross-module corruption. The write restriction must be confirmed by the owner/operators, then actual in-flight NORM operations must drain; an empty SQL transaction view alone is insufficient confirmation.

If deployment automatically restores Step 2 files, it can safely restart against the additive column. Keep NORM writes suspended until the owner approves a verified rollback or successful compatible deployment. No deployer modification is necessary.

## Candidate pinning

- Branch: `codex/norm-package1a-step3`; base/HEAD and application rollback revision: `d92492ebdb2f34eb4b16618226cc9a4f42d88884`.
- Reviewed working migration SHA-256: `d3ba99aaff9b3218aa4b85f5f272f3e977ff8039631878137eeb96c8a5a89c78`.
- Same migration with LF line endings: `3cd503176c7059fe3beae14dc29b5d1f2f054370eead21e31a8915cacf80b1ba`.
- Reviewed candidate fingerprint: `93a98bac4a5922c7f99eb9f562075649faadbd31373cf6ed69d20d717ac623bc`. SHA-256 of the twenty sorted relative-path + space + lowercase file-SHA-256 lines below, each terminated by LF; each file is normalised CRLF→LF before hashing. This identifies content without treating Git newline normalisation as a source change.
- **No immutable Step 3 application revision exists yet:** committing is prohibited in this task. After separate authorisation, commit only the reviewed files and approved living documents, pin that SHA, run `New-WaratahPackage.ps1 -Commit <SHA> -OutputDirectory <new local directory> -Compile`, and record its actual package checksum and sqlSha256. Recheck merged `uat` content and deploy that exact integrated SHA. Never register the migration-only checksum as the full package SQL digest, or package base HEAD as if it contained Step 3.

Reviewed application/SQL/test files:

- `App_Code/NORMAdministeredStatements.cs`
- `App_Code/NORMFinancialStatementsExcel.cs`
- `App_Code/NORMReportingFramework.cs`
- `App_Code/NORMReviewPack.cs`
- `App_Code/NORMStartOfYearSetup.cs`
- `App_Code/NORMWordExport.cs`
- `App_Code/NORMYearSetupDocumentHandler.cs`
- `App_Code/StatementEngine.cs`
- `NORM/NORM_Reporting.aspx.cs`
- `NORM/NORM_Statements.aspx.cs`
- `NORM/NORM_YearSetup.aspx`
- `NORM/NORM_YearSetup.aspx.cs`
- `NORM/NORM_YearSetupDocument.ashx`
- `sql/NORM_Stage1_Package1A_Step3.sql`
- `tools/tests/FR016/Run.ps1`
- `tools/tests/Package1AStep3/EngineContextTests.cs`
- `tools/tests/Package1AStep3/PageHarness.cs`
- `tools/tests/Package1AStep3/Run.ps1`
- `tools/tests/Package1AStep3/TestSupport.cs`
- `tools/tests/Package1AStep3/Tests.cs`

The Step 3 implementation/cutover living documents can accompany a later authorised commit. Exclude the pre-existing unrelated WARATAH import-plan edit, other untracked analysis/data/fixtures and temporary review files. No workflow, deployment script, web.config or UDL belongs in the change.

## Backup and captures — future execution gate

The latest catalogued full backup is set 12: `D:\SQL\Backup\CPlatform_Pre_NORM_1A_Step2_20261111_124855_6e798c53.bak`, COPY_ONLY/CHECKSUM, undamaged, 140,611,584 bytes. The completed Step 2 record documents VERIFYONLY success. It predates accepted Step 2 UAT and is not the Step 3 rollback point. No backup verification was rerun in this read-only review.

After execution authorisation and confirmed NORM write restriction, reconfirm destination/free space using the established procedure; take and verify a fresh, uniquely named backup:

```sql
BACKUP DATABASE [CPlatform]
TO DISK=N'D:\SQL\Backup\CPlatform_Pre_NORM_1A_Step3_YYYYMMDD_HHMMSS_UNIQUE.bak'
WITH COPY_ONLY, CHECKSUM;
RESTORE VERIFYONLY
FROM DISK=N'D:\SQL\Backup\CPlatform_Pre_NORM_1A_Step3_YYYYMMDD_HHMMSS_UNIQUE.bak'
WITH CHECKSUM;
```

Do not overwrite an existing backup. Preserve the actual path, backup-set ID and successful verification result. Capture the prior current.json/baseline, run IDs/GUIDs/import/release/fingerprints/status and their expected names; current designation/name records; approved Release 3/population; source IDs/counts/hashes; setup document IDs/hashes/status and audit high-water mark. A separate full financial-data export is unnecessary: existing evidence is retained and the only migration backfill is a new heading field.

## Ordered execution after separate authorisation

1. Pin/rebuild the exact candidate and recheck scope/checksums/baseline. Obtain owner/operator confirmation that all NORM writes are suspended; drain in-flight imports, recalculations and setup/configuration actions. Leave other CPLATFORM operating arrangements unchanged. Capture the baseline and complete the fresh verified backup above. Stop on any unexpected state or failed prerequisite.
2. Under the established administrative SSM/SQL identity, confirm WARATAH / CPlatform. Use required filtered-index session options: ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL and QUOTED_IDENTIFIER ON; NUMERIC_ROUNDABORT OFF. Execute only the reviewed Step 3 script, changing only `@ConfirmedDatabase` to `N'CPlatform'` in the reviewed operator copy. Do not reseed or designate a year.
3. Verify the new nullable NVARCHAR(200) column and all eight expected snapshots; existing columns/indexes/FKs/checks, sources, hashes, current Import 7, seven setup originals, Release 3 and current FY2026 remain intact. Verify application SELECT/INSERT access to the existing run table/new field under the established application identity. Stop on mismatch. Register the pinned package's actual SQL digest through the existing installer with a Step 3 approval reference only after schema verification.
4. With publishing authorisation, use the existing validated PR into `uat`, WARATAH validation and Deploy WARATAH workflow. Verify the resulting merge SHA, actual deployment run success, current manifest/baseline agreement and no pending deployment. The deployer performs its usual brief pool interruption and protected-file checks.
5. Verify immediate NORM and established shared-module health. Known SYSTEM smoke-test limitations (including the previously diagnosed Prepayment user lookup) must be compared with their recorded baseline, not misclassified as a new Step 3 failure. Any new failure is a stop point. Release the cutover write restriction only after compatible app/database verification; proceed with controlled UAT under owner authority before unrestricted NORM activity.

## Targeted rollback and stopping points

- **Migration failure:** XACT_ABORT/TRY-CATCH rolls back column addition and backfill together. Verify absence of the new column and unchanged baseline; stop without retry or shared restore.
- **Committed migration, deployment failure:** preserve the additive column/snapshots and keep NORM writes suspended. Use the established deployer's previous-file restoration to Step 2 (`d92492e…`) and verify its health. Before any subsequent rollback package deployment, reconcile its source SQL fingerprint through the existing baseline approval procedure against the retained additive schema. Never bypass the digest gate.
- **Step 3 UAT failure:** stop controlled writes, retain evidence/audits and identify the failing action. Reverse any temporary year/name edit through audited Year Setup only when authorised, restoring FY2026 and the captured FY2026 name. Document uploads supersede rather than delete originals; do not delete rows to conceal test evidence. No speculative document reset or broad SQL restore is prescribed.
- If separately authorised schema removal becomes necessary, first preserve `CalculationRunId, ReportingEntityName` and verify all Step 3 consumers have been replaced by compatible Step 2 files, with no intervening new dependency. The sole targeted DDL reversal is `ALTER TABLE dbo.tblNORM_CalculationRun DROP COLUMN ReportingEntityName` in a guarded transaction against the confirmed target. Prefer retaining the harmless column; dropping it destroys newly recorded heading evidence. Full CPlatform restore is last resort and needs separate approval because other modules share the database.

## Minimum live acceptance evidence

| Scenario | Required SQL/browser result |
|---|---|
| Migration and historical baseline | One new column, eight expected name snapshots; preserved existing IDs/GUIDs/fingerprints/hashes/configuration. Current source stays Import 7 and FY2026 stays current. |
| Year Setup authority | Administrator sees DEPT and saved FY2026. Invalid/unapproved activation fails without change. Use existing approved FY2025 and FY2026 releases for an authorised switch/restore; confirm audits/operator and no historical source change. Actual concurrent/stale save/upload must fail safely or block then revalidate; no partial document or designation state. |
| Reporting name and earlier runs | Audited temporary FY2026 name edit succeeds; empty/invalid and stale edits fail. An approved new recalculation uses the new name. Earlier FY2025/FY2026 runs retain their recorded year/name in Statements, Reporting, Excel and Word despite a designation/name change. Restore the original name/year through Year Setup. Record any expected derived import/run separately; it must not replace current original Import 7. |
| Both source-document types and safe uploads | Under the explicit saved FY2026 setup and Release 3 applicability, use approved UAT documents to verify upload/extraction, same-type supersession, exact byte/hash download and audit. Failed/empty/malformed/stale uploads do not change the designation or accepted document. No additional source population or release is needed. |
| Existing historical and ReviewRequired evidence | Download document 7 (FY2025 prior statements) and 8 (FY2025 budget), superseded 3 and 6, and ReviewRequired 1 (FY2024). Links are `/NORM/NORM_YearSetupDocument.ashx?document=<id>`. Check local SHA-256/size against SQL, filename/status and successful-download audit; preserve originals. |
| Access and regression | Administrator remains authorised. Malformed/missing/forged IDs fail safely. Existing Step 1 import/evidence and Step 2 coverage/replacement paths still work; unrelated-module health matches baseline. Assigned/unassigned/revoked Preparer and cross-entity browser tests require an approved identity and remain the existing mandatory Package 1D obligation; do not mark them Passed without execution. |

Record Passed / Failed / Blocked / Not Tested distinctly. SQL locking/storage and authenticated browser cases are currently Not Tested for Step 3. The absent approved live Preparer identity remains a documented deferred security UAT dependency, not a new schema/deployment blocker.

No further assessment or development increment is required. Remaining actions are execution-authorisation gates: commit/package pinning, fresh backup verification, owner-confirmed short NORM write restriction/drain, reviewed migration/baseline registration, existing deployment and focused UAT. No action in this sequence was executed by this readiness task.
