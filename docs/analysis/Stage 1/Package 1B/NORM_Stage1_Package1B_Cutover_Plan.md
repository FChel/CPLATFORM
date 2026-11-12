# NORM Stage 1 — Package 1B cutover plan

Living plan · 12 November 2026 · Read-only readiness complete; no execution authorised

## Execution authorisation — 13 November 2026

The owner has now authorised isolated UAT configuration, selected Package 1B commit/PR/deployment and the three primary genuine-file scenarios. The readiness findings below remain historical evidence, not a current prohibition on those authorised actions.

Fresh preflight confirms deployed `c3317d8`, all 254 managed hashes, unchanged SQL registration and DEPT baseline. Candidate pin still matches all eleven runtime/test files. Existing checkout moved to `codex/norm-package1b-uat` from current `origin/uat` without discarding unrelated local changes.

Isolated source-mechanics configuration completed transactionally under SYSTEM at 09:27:33 AEDT, with explicit owner/Administrator approval recorded: Release 5 `UAT_AAG` FY2020/company150 and Release 6 `UAT_ICON` FY2016/company86, both null canonical ledger, Departmental, required population, respective current year. Empty mapping-set hash is retained; no accounting mappings or statement definitions were invented/copied. Existing initial test-baseline approval/audit mechanism was used, distinct from ordinary changed-mapping approval. DEPT configuration/mappings/source/original/run/setup-document fingerprint is identical before/after (`5c06f203ba285206fdc7fe6157876e8e49ce86445a0f2f755e789e947b5abcc2`).

Evidence: [configuration operator data script](Prepare_Isolated_UAT_20261113.sql), [transaction result](WARATAH_Isolated_Configuration_20261113.json), SSM `b76a4ef6-1def-42bd-a165-1cbbfea628e7`. This is data setup only: no schema, registered SQL baseline or DEPT write. Continue exact candidate publishing and genuine-file UAT; closure is not yet claimed.

**Not Ready for the complete deployment-and-targeted-UAT execution:** the application candidate and unchanged deployment process are ready, but the required isolated FY2020/FY2016 configuration does not exist on WARATAH. The smallest next action is separate owner authorisation for the existing-mechanism test configuration and normal publishing, followed by exact-commit packaging and targeted UAT. No further implementation increment or source-profile decision is needed.

No application, SQL, configuration, Git commit/PR/push/merge or deployment change was made by this readiness task. No server start/stop, scheduling change or operating-time extension was requested or performed. Local tests write only isolated temporary fixtures. SSM commands executed SELECT, file reads/hashes and HTTP health checks; they did not create accepted imports or write NORM records. All server work is now stopped; remaining preparation is local documentation.

## 1. Verified deployed baseline and exact candidate

Remote `uat` was queried read-only and remains `c3317d83246ceffc09ace1fa9a734c8e8829fd4b`. Read-only SSM verified the same live revision on WARATAH, matching all **254 managed file hashes**, no `pending.json`, no `app_offline.htm`, and no pre-existing new text reader. Local HEAD `789c99748cb59281e5aca9544a4c980e8a60841d` has the same application tree as the deployed merge. Future publishing must still start from then-current `origin/uat` and stop for review if it has moved.

The reviewed application delta is exactly four files, all NORM:

| Path | Change | Pinned raw SHA-256 |
|---|---|---|
| `App_Code/NORMImportModels.cs` | Parsed/preview profile version | `10b3fc8abfe27c32bfe8495fd5f6359c1999f30a9ae02f9c454bbb0d0c507d4f` |
| `App_Code/NORMImportService.cs` | Native text dispatch, unambiguous population resolution, profile provenance, bounded text rejection/failed-acceptance audit | `2c3959a6e1fa5fb59414d82d2907b6de469cbd4689375496e801aed4083a825c` |
| `App_Code/NORMTextTrialBalanceImporter.cs` | New approved ActewAGL CSV and Icon fixed-width extraction | `0cea60d53e1e44d844ef6a06c6612445361109433598e542d7b37199ae80b421` |
| `NORM/NORM_Import.aspx.cs` | Accurate native layout/profile and currency/sign preview | `9946cc5d439811d1f47e29e756b05141caa6ac8efdb3ac065e332f2449816f42` |

Defence extraction, mapping, calculation, Year Setup, permission architecture, other modules, SQL, workflow and deployment scripts are unchanged. Defence/legacy ERP receive version provenance through the shared service only. No extra agency formats, general parser framework or Package 1C orchestration is included. ActewAGL imports CY only; Icon uses positional columns and is never labelled delimiter-separated.

Seven supporting test files: modified `tools/tests/FR016/Run.ps1`, `TestSupport.cs`, `tools/tests/Increment1A/Run.ps1`, `Increment1B/Run.ps1`, `Increment1B2/Run.ps1`; new `tools/tests/Package1B/Run.ps1`, `Tests.cs`. Runners compile the new production class; test substitutes support real original-file handler round trips. Existing assertions were not weakened.

**Candidate pin:** [immutable manifest](NORM_Stage1_Package1B_Candidate_Pin_20261112_103157.json), fingerprint `c04b27b1743911f6d469bd387003c6d5e24dfae2c58a5fb7737abd878808a121`. It binds the deployed baseline plus the four application/seven test file hashes. Exact bytes are copied under `C:\Development\NORM\.deployment\Package1B_Readiness_20261112_103157\`. This is a local candidate snapshot, **not a deployable Git package or commit**. Do not package HEAD now: the standard packager uses `git archive` and would omit the uncommitted implementation. A separately authorised commit must include the reviewed new class and tests; then compile/package that exact commit. If Git normalises line endings, compare LF-normalised source content and compile the exact resulting package; its manifest hashes become the deployment authority.

Maintained Package 1B documents and read-only evidence may accompany that later reviewed change. Exclude all pre-existing Package 1A/deployment-plan edits, unrelated analysis, SQL operator files and historical source material from an accidental blanket commit. Genuine original fixtures are approved UAT inputs, not website payload. No broad `git add` is appropriate.

## 2. Actual WARATAH read-only findings

Readiness snapshot at 21:34:49 AEDT; final dependency/health check at 21:42:13 AEDT, 12 November 2026. An initially expired AWS session was renewed by the owner. The standard GitHub OIDC deployment connection was never missing; local credential expiry did not affect its setup.

| Check | Actual result |
|---|---|
| Live revision / managed hashes | Passed: `c3317d8`, 254/254, no mismatches. |
| Registered and deployed SQL source digest | Passed: both `7c6468d6f56890846fcb8e1c53abba5a4ee0cd77e0cd3c7c6a566a9ca8b6ad51`. |
| Current deployment transaction | Passed: no pending/offline markers. |
| Standard health | Passed: CFO Toolkit, CSS, NORM Statements HTTP/content checks using the unchanged installed health script, one attempt. The earlier unauthenticated ad hoc Statements probe was inconclusive; it is superseded by this standard default-credentials check. |
| Installed deploy/health/common scripts | Passed: all three hashes equal repository LF-normalised source; raw local CRLF hashes naturally differ. No tooling reinstall is needed. |
| NORM state | 8 imports, 9 source files, 7,849 TB rows, 8 runs, 85 audit rows/high-water 85. Read-only requests create no NORM import/audit records. |
| Existing agency/year | Only DEPT reporting entities FY2025/FY2026; current Year Setup ID 3 remains FY2026. |
| Releases/populations | Releases 1/3 Approved, 2/4 Draft. Release 3 is DEPT FY2026 `uat-fy2026-import-20261102`; its sole permitted population is 1000 / 0L / Departmental. Release 4 has the same scope in Draft. No isolated ActewAGL/Icon entity, FY2020/FY2016 release or applicable population exists. |

Read-only dependencies verified against the actual CPlatform database:

- The actual application connection resolves as `CPlatformApp`; `HAS_PERMS_BY_NAME('dbo.tblNORM_AuditEvent','OBJECT','INSERT') = 1`.
- Audit columns: EventCode varchar(60), EntityType varchar(40), EntityId varchar(60), DetailText nvarchar(2000), PerformedBy nvarchar(160), PerformedUtc datetime2. Privileged read-only metadata confirms `PerformedUtc` defaults to `sysutcdatetime()`. No audit check constraints or triggers impose an event-code catalogue. `IMPORT_SOURCE_PROFILE` and existing `IMPORT_REJECTED_SOURCE` therefore need no seed/table/permission migration. Application metadata visibility is restricted (`VIEW DEFINITION = 0`), which explained initially hidden default definitions; this was resolved through SYSTEM's read-only metadata access, not by granting permissions.
- Actual source-type constraints accept existing `ERP`/`ROMAN` categories for retained files. Candidate text TBs deliberately retain `ERP` as the existing technical category; native layout/profile is shown in preview and profile audit. No new source-type code or constraint change is required. Actual exact-coverage constraint is installed.
- `git diff origin/uat -- sql tools/deploy .github` is empty. No SQL file, baseline registration or installer action is necessary; do not execute historical SQL/reset/setup scripts or update the registered digest to bypass a check.

Evidence: [compact deployed/database snapshot](WARATAH_ReadOnly_20261112_103449.json), [application audit permission result](WARATAH_Dependencies_Final_20261112.json), [privileged audit metadata and standard health](WARATAH_MetadataHealth_20261112.json). Original snapshot SSM `49e8efba-2bac-4178-9cce-8cadd84ab74e` succeeded; its oversized manifest output was read back compactly by `623b1a27-c505-4567-b800-e7d357af2cf8` without rerunning SQL. Final metadata/health SSM `87bd90a8-1f9e-4c13-9bdb-5a259616065c` succeeded. Intermediate operator-script parse/argument failures are retained as evidence; they are not application defects or failed deployments. No writes were executed by them.

## 3. Local regression and unchanged publishing process

The prior **293 passing checks** and successful full ASP.NET compilation remain valid; pinned production text/service hashes match that verified implementation. Readiness reran only relevant checks: **38 Package1B + 63 Increment1B2 + 43 FR016 = 144 Passed / 0 Failed**, plus production import/engine and full ASP.NET compilation Passed. Existing unrelated eJET.aspx.cs:125 CS0162 warning only. No new code/test change was made during readiness.

`Test-DeploymentRequest.ps1` Passed (fake request/account/SSM polling, no AWS deployment). `Test-DeploymentSafety.ps1 -PackageInfo <retained 789c997 package.json>` Passed deployment/rollback, stale-file removal, protected-path preservation, traversal/checksum and SQL gate tests using fake IIS/S3/temp files. The initial invocation omitted its mandatory PackageInfo; it did not run and was corrected. The safety test used the previous successful package to verify **unchanged tooling**, not an exact Package 1B deployable package.

Existing process is reusable unchanged: GitHub `WARATAH validation` → PR into `uat` → merge → `Deploy WARATAH`, temporary OIDC credentials, private package S3 bucket and pinned SSM deployment document. Feature branches do not deploy; environment accepts only `uat`. Installed scripts' LF SHA-256:

- Deploy-Waratah.ps1: `05d557af78f33cda61f4c0961a9b35218f064084d94934a404cfc79b141d2eb2`
- Test-Waratah.ps1: `be89b1cd30a7c0e08533180a631ea01f831773cdd3d645d9468e5860344533b5`
- Waratah.Common.ps1: `541621a2ea09bef9bc0d6c9c98421105a3c8cf82ce628a1be46854f764bcad5b`

Do not use `AWS-RunPowerShellScript` as an alternate deployment route; its use here was solely authorised read-only inspection. Standard application publishing still uses the configured restricted deployment document and process in [WARATAH_DEPLOYMENT.md](../../../WARATAH_DEPLOYMENT.md).

## 4. Smallest isolated configuration for genuine positive UAT

**Absent on the checked server; not created or authorised by this plan.** Reuse the existing reporting-entity, configuration-release, source-population, Year Setup and user-assignment mechanisms. Do not add infrastructure, source-profile tables, a parser selector, new agency-specific code or a new setup mechanism.

Prefer reusing suitable existing **isolated** reporting entities if they exist at execution time. None exists in this snapshot. Otherwise separately authorise two clearly labelled test entity codes within the existing 20-character field (for example `UAT_AAG`, `UAT_ICON`, proposed labels only, not asserted genuine agency identifiers). Their approved configuration provides the active agency context for mechanics UAT and must not be represented as an approved agency financial statement or published output. Never repurpose DEPT/Release 3, change DEPT current year, or fabricate company/ledger evidence.

| Initial genuine positive | Minimal approved existing-mechanism configuration | Coverage and scope |
|---|---|---|
| ActewAGL `AAG_TrialBalanceFinStats150.csv` | Isolated reporting entity; FY2020 approved release with current-year designation (FY2020 is simplest), one authorised company 150 population; configured ledger null/not applicable or a canonical ledger only if owner-approved; one approved stream. | Agency from that release; CY only; full 2019-07-01–2020-06-30; 409 rows. Full-year FY2020 may also be non-current if the isolated agency has another explicit current year. |
| Icon `coy 86.txt` | Different isolated reporting entity; FY2016 approved release; current-year designation **FY2016**; one authorised company 86 population with unambiguous canonical optional ledger and one approved stream. | Partial 2015-07-01–2016-03-31; 18 rows. This cannot accept under a completed FY2016 or DEPT FY2026 designation. |

Use the normal release preparation/review/approval path and approved required-population definitions. Supply ordinary statement-line/profile configuration required by the existing release/automatic post-acceptance calculation path, without inventing ActewAGL/Icon accounting mappings or copying DEPT financial authority. Unmapped accounts remain eligible for source acceptance; accounting sign-off is outside this mechanical UAT. If calculation fails after source acceptance, record that separately and do not misreport a rejected import or upload again blindly.

The owner/operator must authorise the actual test entity codes, canonical nullable ledger and stream values, release ownership/approval and operator access before configuration writes. This is ordinary controlled setup, not a reopened decision about approved source units/signs/dates or a development request. Administrator can exercise the existing path; assigned Preparer needs its existing entity assignment, with integrated security UAT remaining 1D.

Optional extra genuine coverage: add company 87 and 80 populations to the same isolated Icon FY2016 release **before approval**, then exercise 17-row/single-page and 412-row/nine-page reports. One parser/profile family handles all companies. To run earlier ActewAGL originals live, add ordinary FY2018/FY2019 approved isolated releases; their full-year coverage permits non-current-year acceptance. These extra live configurations are not required for the smallest primary-fixture UAT; all six originals already passed local tests. No substitute positive fixtures or modified-year copies are needed.

## 5. Targeted browser and SQL UAT after separate execution approval

Use original files and SHA-256 from the [approved profile manifest](NORM_Stage1_Package1B_Source_Profiles.md). Scope one population/file, one active agency per batch; no 1C orchestration. Record actual IDs/times/outcomes, not assumed passing scenarios. Before each case capture current original import IDs for target scopes and unaffected DEPT state; after each case compare precisely.

| Case | Browser evidence | Read-only SQL/file evidence and expected result |
|---|---|---|
| Defence regression | Inspect genuine FY2026 workbook using existing authorised DEPT Release 3, verify `DEFENCE-ERP/1`, 984 rows, company 1000/0L/Departmental, full-year dates, totals and replacement warning; **cancel** to preserve current DEPT source. | No accepted import/run/audit change from routine cancellation; DEPT FY2026/current source and files remain intact. No positive DEPT replacement is required just to prove 1B recognition. |
| Genuine ActewAGL primary | Under isolated approved FY2020 configuration: automatic `ACTEWAGL-FINSTATS/1`, company 150, resolved optional ledger/stream; 409 rows; debit/absolute credit 11,925,983,562.62, net 0.00, AUD dollars multiplier 1; actual 2019-07-01–2020-06-30. Confirm once. | New accepted source contains exactly 409 CY account rows, correct signs/identity/coverage. No PY population/rows. Retained original byte length/hash equals genuine CSV, including prior column. New source profile/coverage/balance/storage/create audit attaches to the actual accepted import. |
| Genuine Icon primary | Isolated FY2016 current configuration: automatic `ICON-WATER-FIXED/1`, company 86, configured optional ledger/stream; 18 rows; 2015-07-01–2016-03-31 partial. Explain positive source credits → signed canonical credit movements; confirm once. | Ending positive/absolute negative 382,097,954.00 and net 0.00. Sum source beginning/ending 0.00, debit and positive source credit each 161,053,185.12. Each canonical row opening + debit + signed credit = ending. Original descriptions/signs retained in original. All printed controls verified. |
| Genuine extra Icon, if configured | Company 87: 17 rows; company 80: 412 rows/nine pages. Same fixed-width profile, no per-company selector. | Ending positive totals respectively 1,421,192,159.48 / 17,634,789,869.35. Source debit/credit controls respectively 359,051,659.79 / 5,128,014,912.26; beginning/ending net 0.00. Page headings/footer never enter detail. |
| Original retrieval and provenance | Download each newly accepted original through the actual authenticated source-file link; record profile shown at preview. | `tblNORM_ImportFile` file/hash/bytes plus downloaded SHA-256 match genuine original. Query `IMPORT_SOURCE_PROFILE` by Import ID; exact version/hash retained. Technical SourceType ERP is intentional; native profile is audit evidence. Replaced originals and old audit rows remain available. |
| Rejection controls | Controlled **negative** copies only: CSV amount/nonzero/malformed or year-header conflict; Icon inconsistent page/control, malformed detail or explicit non-AUD; unsupported content. Inspect only. | No accepted import/file/row/run/current-source changes. Available filename/hash/agency/company/reason/operator/time/outcome in `IMPORT_REJECTED_SOURCE`. Profile audit insertion is not an event-type deployment dependency. Specific safe messages; rejected bytes need not be retained. |
| Coverage/precision/conflict edges | On separately approved test configuration/copies only, test completed-year rejection of partial Icon, balanced sub-cent precision rejection and ambiguous population binding. Do not modify shared DEPT year or approved releases for a negative test. | Exact-zero/storage/coverage rejection leaves originals/current sources intact. An ambiguity test needs an explicitly authorised isolated configuration variant; if unavailable mark Not Tested, keep passing local evidence, do not silently create it. |
| Same-scope replacement | Re-inspect the **same genuine isolated** CSV or Icon original: replacement scope visible, unchecked acknowledgement retains preview/no write, cancellation safe; then explicitly acknowledge/confirm a controlled isolated replacement if authorised. | Only that agency/year/company/ledger/stream current source changes; predecessor bytes/rows/runs preserved, `IMPORT_SOURCE_REPLACED` links prior ID. DEPT and the other isolated agency unchanged. No manual reset/delete to clean up UAT. |

Use `tblNORM_Import`, `tblNORM_ImportFile`, `tblNORM_TrialBalanceRow`, `tblNORM_CalculationRun`, `tblNORM_AuditEvent` filtered by actual recorded import/release/entity IDs. Verify row-count/sign sums, exact dates/IsFullYear, original FileContent hash and profile event detail. Do not query “latest” across unrelated entities or count derived recalculation copies as current original sources. Record accepted-but-calculation-error distinctly. Invalidate/reinspect stale previews after any approved configuration change. No browser UAT was performed by this readiness task.

## 6. Standard execution, verification and rollback — future authorisation only

1. At the next scheduled available session, reconfirm live revision/SQL digest/current markers, protected config hashes, DEPT FY2026/current sources and accepted original hashes/counts. Verify current application backup/recovery records and planned brief CPLATFORM pool interruption using existing practice. Do not extend server hours or start a stopped instance for this plan.
2. Separately authorise reviewed candidate commit and isolated test setup. Reconcile snapshot hashes; exclude unrelated changes. If `uat` moved, review its diff without discarding feature work. Commit only the selected runtime/tests/docs under a `codex/` branch as normally used; pin the immutable commit.
3. Run unchanged `New-WaratahPackage.ps1 -Commit <candidate> -OutputDirectory <new local path> -Compile`, `Test-DeploymentSafety.ps1 -PackageInfo <actual candidate package.json>` and `Test-DeploymentRequest.ps1`. Confirm expected four runtime content changes, no deletions, expected total **255 managed files**, unchanged other modules, excluded protected data and SQL digest `7c6468d6...8b6ad51`. This exact-commit package validation is not possible before the currently prohibited commit; do not claim it has already happened.
4. Follow the standard PR into `uat`, wait for **WARATAH validation**, merge under separate publishing authority and verify the resulting **Deploy WARATAH** run. Record PR, immutable candidate/merge SHAs, ZIP checksum, SQL digest, SSM ID, actual workflow outcome and backup/run directory. No source commit alone is a completed deployment.
5. Read-only immediate verification: live current.json revision, 255 manifest hashes/only four changed runtime files, unchanged baseline record/configuration/UDLs/uploads/evidence/other modules, no pending/offline markers, standard health. Compare pre-cutover DEPT source/document/audit state before beginning intentional UAT writes. Then perform targeted cases in section 5 only under the separately approved isolated configuration. Retain labelled UAT records; do not promote these mechanics results to genuine reporting/publication authority.
6. Automatic rollback on copy/post-check failure remains the unchanged deployer's backup/restore transaction. For rollback after a successful deployment, rerun all jobs on the prior successful deployment [42997971861](https://github.com/FChel/CPLATFORM/actions/runs/42997971861) for `c3317d8`, after verifying it remains compatible with the unchanged SQL baseline. This removes the newly managed reader and restores old versions through existing manifest rules; check all 254 hashes, standard health and protected-state preservation. Never remove unknown server files manually or clear pending state blindly.
7. Application rollback does not remove accepted UAT imports/audits/configuration. Existing accepted text sources must remain retrievable; freeze further test imports and record their IDs/history. Use a separately approved data disposition only if needed—no database restore, delete/reset, bulk SQL execution or DEPT configuration change is implied. Existing old application may not recognise new text previews, so source-intake UAT stops on rollback. Preserve all originals/evidence.

## 7. Final readiness and outstanding work

**Application deployment preparation: Ready. Complete deployment-and-UAT execution: Not Ready until isolated configuration is separately authorised/prepared.** No implementation defect, missing audit-event migration or deployment-tool change was identified. Completed server checks do not need to be repeated as an investigation; only refresh volatile state immediately before execution.

Smallest outstanding actions for the next authorised session:

1. Approve the two isolated test entity/release/population contexts and existing-mechanism setup (FY2020/company150 and FY2016-current/company86), with canonical optional ledger/stream and operator access; preserve DEPT FY2026. These configurations are confirmed absent, not an assumed infrastructure gap.
2. Separately authorise the selected commit/PR/publishing steps, then validate/package that exact immutable commit and follow the unchanged `uat` deployment process.
3. Execute immediate verification and primary genuine-fixture browser/SQL UAT; record actual outcomes and retain originals. Optional extra-company/negative-configuration cases can remain explicitly Not Tested if not authorised. Package 1D integrated security and Package 1C orchestration stay excluded.

Readiness stopping condition is met. No more live checks are needed tonight, no deployment/configuration work is started, and shutdown remains under the existing schedule.
