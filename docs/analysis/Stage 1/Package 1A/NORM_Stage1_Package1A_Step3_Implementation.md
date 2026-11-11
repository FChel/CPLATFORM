# NORM Package 1A Step 3 — Year Setup and source-document controls

11 November 2026 · Living implementation record · Development and isolated testing only.

## Outcome

Implemented on `codex/norm-package1a-step3`, based on `origin/uat` at `d92492ebdb2f34eb4b16618226cc9a4f42d88884`. Ready for a separately authorised controlled WARATAH migration/deployment and verification. Step 3 is not yet accepted on WARATAH.

No AWS/shared database access, migration execution, configuration designation change, deployment, commit, push or pull request occurred. Existing Step 1 and Step 2 UAT records and unrelated working-tree files were preserved. Full annual rollover remains Stage 4; live Preparer security UAT remains mandatory in Package 1D.

## Implemented behaviour

- **FR-001 / FR-008 prerequisites:** `tblNORM_YearSetup` remains the sole current-year authority. Missing or ambiguous designation fails clearly; neither system date nor latest approved release supplies a default. Activation requires an active entity/year registration and an exact-year approved configuration release. The existing unique filtered current-entity index remains the database invariant. Designation and audit commit together; switching years does not update imports, evidence, calculations or configuration releases.
- **Entity access:** Year Setup lists only accessible entities, using the existing Step 1 `CanPrepareEntity` / `RequireEntityAccess` checks. Administrators retain access to all configured entities; Preparers require an assignment. Stored setup/document records determine direct-request authority. Services recheck permissions inside write/download transactions, including revocation. No stream assignments or new roles are introduced. Existing reviewer users must hold an existing authorised application role/assignment; a distinct Reviewer role remains outside this increment.
- **FR-003:** The selected saved entity/year's `tblNORM_ReportingEntity.EntityName` is editable, trimmed, limited to 1–200 characters and rejects empty/control-character values. Updates compare the previously displayed name under a lock, and audit previous/new name, year and operator through `ENTITY_REPORTING_NAME_UPDATED` (existing audit timestamp). Other entity/year names remain untouched.
- **FR-002 / BR-05:** Both Year Setup document types list retained versions with ID, filename, extraction status, size, hash and original-download link. Current, superseded, historical-year and `ReviewRequired` documents remain retrievable. `NORM_YearSetupDocument.ashx?document=<id>` resolves the stored entity, checks permission, verifies SHA-256, audits `YEAR_SETUP_DOCUMENT_DOWNLOADED`, and returns the original bytes as a non-cacheable attachment. Failed authorisation/integrity/audit produces no successful download.
- **Safe operations:** Upload is separate from year activation and uses the explicitly selected saved setup. It never calls `SaveYear`. Empty, malformed or invalid uploads leave the designation and accepted documents unchanged. Before replacing a document, its transaction rechecks permission, setup identity/timestamp, applicable release and the previously displayed active document ID. Supersession, original bytes, extracted figures and upload audit commit atomically. Concurrent/stale requests fail and require reload. Year activation compares the current setup ID and timestamp; name updates compare the prior value. WebForms signed state is bound to the authenticated session. Revoked access also clears cached protected page content before rendering.
- **Historical output context:** Statements, Reporting, Excel and Word use the selected run's recorded import year/entity and reporting-name snapshot. Comparative/budget/note/asset readers receive the recorded year, rather than following the current pointer. Review-pack and Administered context readers follow the same rule. Calculation amounts, mapping rules, note derivation and publication formatting were not redesigned.

A reporting-name edit applies to new/recalculated runs. Existing runs retain their snapshot. The name participates in new-run input fingerprints so recalculation after a name edit does not reuse the previous heading. Reopening an existing run never substitutes the new name or current-year designation.

## Components

| Area | Files |
|---|---|
| Setup controls, transaction validation, audit and retained originals | `App_Code/NORMStartOfYearSetup.cs`, `NORM/NORM_YearSetup.aspx`, `NORM/NORM_YearSetup.aspx.cs` |
| Entity-authorised download | New `App_Code/NORMYearSetupDocumentHandler.cs`, `NORM/NORM_YearSetupDocument.ashx` |
| Run-name snapshot | `App_Code/StatementEngine.cs` |
| Recorded context consumers | `App_Code/NORMFinancialStatementsExcel.cs`, `App_Code/NORMWordExport.cs`, `NORM/NORM_Statements.aspx.cs`, `NORM/NORM_Reporting.aspx.cs`, `App_Code/NORMAdministeredStatements.cs`, `App_Code/NORMReportingFramework.cs`, `App_Code/NORMReviewPack.cs` |
| Prepared migration | `sql/NORM_Stage1_Package1A_Step3.sql` |
| Tests | `tools/tests/Package1AStep3/` (runner, database/hosting substitutes, service/page/handler tests and real-engine context tests) |
| Build verification | `tools/tests/FR016/Run.ps1` now includes allowlisted new untracked application files in its temporary full-site build; no deployment mechanism changed. |

## Prepared SQL and compatibility

The existing year-scoped entity name is mutable and calculation runs previously had no recorded heading name. The smallest history-preservation change is one nullable `ReportingEntityName NVARCHAR(200)` column on the existing `tblNORM_CalculationRun`; no name-history table is added.

`sql/NORM_Stage1_Package1A_Step3.sql` is **prepared, not executed**. It requires explicit target confirmation, the Step 2/Year Setup baseline and existing unique filtered current-year index. It adds the column and backfills existing runs from their recorded entity/year registration, including historical registrations, with entity-code fallback where the name is unavailable. Addition/backfill occur in one transaction with `XACT_ABORT`, rollback and rerun guard. It does not alter existing fingerprints, accepted sources, dates, documents, years, users, releases, constraints or indexes.

Working-file SHA-256: `D3BA99AAFF9B3218AA4B85F5F272F3E977FF8039631878137EEB96C8A5A89C78`. Recompute on the eventual pinned deployment candidate using the established SQL baseline process.

Existing runs did not store the name originally displayed. Backfill preserves the best available recorded entity/year name at migration time; it cannot reconstruct a name already overwritten before Step 3. Review existing historical headings against retained evidence before authorising the migration. New snapshots preserve subsequent name changes without rewriting earlier runs.

The application requires this additive column. For a later authorised cutover, use the established migration-first WARATAH procedure, backup/baseline approval and a confirmed NORM write restriction through compatible application verification. No new maintenance mechanism is needed. Do not change DEPT's FY2026 designation during migration/deployment. On transaction failure, verify rollback and stop. If application cutover fails after migration, stop NORM writes and use the established application rollback; the extra column may remain for the previous application. Do not drop snapshot evidence or restore the shared database as a routine rollback. Audited UAT year/name changes require controlled reversal through Year Setup.

## Isolated verification

| Check | Result | Count / scope |
|---|---|---|
| Step 3 service/page/handler controls | Passed | 62 |
| Real engine reporting-name/context checks | Passed | 3 |
| Increment1A | Passed | 44, unchanged Defence workbook |
| Increment1B | Passed | 21: 14 service, 1 calculation and 6 real-helper assertions |
| Increment1B2 | Passed | 63, includes Step 1 permissions/identity/replacement and Step 2 coverage |
| FR016 | Passed | 43: 34 service and 9 engine |
| **Total** | **Passed** | **236; zero failing checks** |
| Production import/engine compilation | Passed | Real helper/OleDb APIs |
| Full ASP.NET compilation | Passed | Final working-tree application, including new handler/page controls; existing unrelated `eJET.aspx.cs:125` CS0162 warning |
| SQL Server migration, constraints, lock concurrency and storage/readback | Not Tested | No shared database writes or SQL execution authorised |
| Windows-authenticated WARATAH browser/download UAT | Not Tested | No Step 3 deployment authorised |
| Live assigned/unassigned/revoked Preparer UAT | Blocked / deferred | Approved live identity remains unavailable; mandatory Package 1D security UAT |

The focused tests execute production service/page/download code with substituted database and identity hosting. They exercise both document types in `ReviewRequired` and successful XLSX extraction paths, byte/hash round trips, superseded/history retrieval, forged IDs, revocation at write/download, stale designation/name/document requests, failure rollback and encoded display. Three additional tests execute the real statement engine with controlled empty-amount fixtures to verify persisted name snapshots, new-name fingerprints and independence from the current pointer. Historical-header source assertions and recorded-year loader checks supplement these tests; they do not claim browser rendering or SQL Server locking verification. Existing extraction line catalogue is substituted in the setup harness; the complete production catalogue compiles in the full-site build.

Commands:

```powershell
& ./tools/tests/Package1AStep3/Run.ps1
& ./tools/tests/Increment1A/Run.ps1 -SamplePath (Resolve-Path 'docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx').Path
& ./tools/tests/Increment1B/Run.ps1 -SamplePath (Resolve-Path 'docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx').Path
& ./tools/tests/Increment1B2/Run.ps1 -SamplePath (Resolve-Path 'docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx').Path
& ./tools/tests/FR016/Run.ps1
```

The sandbox denied ASP.NET's EPPlus assembly load; the final full-site build passed outside the sandbox using an isolated temporary application copy and build-only config. No production configuration/UDL/evidence was copied or changed. Final build artifacts: `C:\Users\Steve\AppData\Local\Temp\norm-fr016-283cd940fff84487bbaab939f5dc8efd`. This is build evidence, not deployment evidence.

## Controlled WARATAH verification still required

Following separate authorisation, review/apply only the Step 3 migration, record the SQL baseline and deploy the pinned compatible application through the established process. Verify:

1. DEPT remains FY2026; Release 3, Import 7/current-source identity, historical imports/runs, hashes and completed Step 1/2 evidence remain intact.
2. An approved exact-year activation succeeds and audits; unapproved activation, stale tabs and real concurrent saves/uploads fail without partial state. Restore FY2026 through audited Year Setup after any authorised year-switch UAT.
3. Administrator entity access and name editing, previous/new audit and new-run headings; earlier run headings/years stay unchanged after year/name changes in Statements, Reporting, Excel and Word. Restore any UAT name through audited setup.
4. Both Year Setup document types upload/extract through the actual browser; current, superseded, historical and `ReviewRequired` originals download with matching local SHA-256, filenames and audit events. Invalid uploads leave the pointer/documents intact; forged/unauthorised IDs fail.
5. Existing Defence ERP import/replacement, period eligibility, source evidence and NORM/module health remain operational. Live Preparer assignment/revocation/direct-request checks retain their Package 1D obligation.

No local implementation blocker remains. Recommendation: approve controlled WARATAH verification when ready; reserve Step 3 acceptance until its required target-stack results are recorded. Stop here; no further increment has begun.
