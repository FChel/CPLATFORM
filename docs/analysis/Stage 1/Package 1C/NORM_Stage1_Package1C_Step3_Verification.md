# NORM — Package 1C, Step 3: Atomic Multi-Population Batch Acceptance and Replacement

Living implementation and verification record · 13 November 2026

## Outcome

**Step 3 is implemented and locally verified, ready for Project Owner review.** One agency can confirm a complete validated batch containing several files, populations, authorised years/releases, companies, optional ledgers and streams, including new sources and exact-scope replacements. Every accepted population receives its own immutable import with its own release/year/coverage/rows and unchanged original evidence. One acceptance transaction commits the batch; calculation remains a separate per-import operation after commit.

This implements Step 3 of the [approved gap assessment](NORM_Stage1_Package1C_Gap_Assessment.md), on top of the locally verified [Step 1](NORM_Stage1_Package1C_Step1_Verification.md) and [Step 2](NORM_Stage1_Package1C_Step2_Verification.md). Their temporary mixed-release/multi-population confirmation restrictions are superseded by the protections below. Approved BRD v0.7 D1–D6, Package 1A identity/coverage/access controls and Package 1B native profiles remain unchanged. No accounting aggregation, mapping, calculation-engine or Step 4 history/navigation work was implemented.

Refreshed `origin/uat` remains `59c487168f875b4de0fdd289c33324ec194f5d74`. Continued in the existing `codex/norm-source-download-fix` checkout over uncommitted Steps 1/2; their records and other pre-existing changes were preserved. No branch, commit, PR, deployment, shared-environment access, SQL/schema change or approved configuration-data change occurred. All changes remain local.

## Changed files and reused functionality

| File | Step 3 changes |
|---|---|
| `App_Code/NORMImportModels.cs` | Added internal population-set, retained-preview and staged-batch fingerprints to existing file/preview contexts. These stay in server session. Existing five-part `NORMSourceScope`, rows, coverage and original references are reused. |
| `App_Code/NORMImportService.cs` | `InspectPopulations` seals full-file population evidence; `ValidateBatch` records the staged selection. Added `PopulationEvidence`, `FilePopulationFingerprint`, `BatchEvidence`, `VerifyBatchMembership`, `VerifyRetainedPreview`, `ReinspectBatchFiles`, `ExpectedPredecessor` and `ParsedPopulation` as local helpers. `ValidateBatchCore` reuses preparation checks. `ConfirmBatchCore` extends the existing transaction with complete-file reinspection, every-release recheck, deterministic scope locks and per-population persistence. `BatchConfirmationBlockReason` now reports actual preparation failures instead of the temporary feature prohibition. |
| `NORM/NORM_Import.aspx.cs` | Valid complete mixed batches can confirm. Preview shows explicit New/Replacement/Unresolved status and all existing per-population context/counts. Separate replacement checkbox remains mandatory. Encoded result messages identify each accepted source's year/company/ledger/stream/release/original. Batch validation is computed once when rendering the preview. |
| `NORM/NORM_Import.aspx` | Replaced inspection-only copy with whole-batch acceptance wording. Existing controls and layout remain. |
| `tools/tests/FR016/TestSupport.cs` | Extended the existing isolated storage boundary with transaction-aware query callbacks, commit/rollback counters, late-write fault injection, commit publication, calculation callbacks and upload-limit override. Existing runners still use their previous defaults. No real connection implementation or database was added. |
| `tools/tests/Package1CStep1/Tests.cs`, `tools/tests/Package1CStep2/Tests.cs` | Reused their fixtures/configuration/page harness. Updated only the now-obsolete feature-block assertions to successful complete-batch acceptance or rejected incomplete/invalid sets; added the Step 3 runner hook. Prior verification documents retain their historical results. |
| `tools/tests/Package1CStep3/Run.ps1`, `Store.cs`, `Tests.cs` | New focused runner and bounded accepted-source state substitute. Production models, parsers, service, persistence command construction, page and original-download handler are exercised. Seeded originals/rows/calculation links and unrelated sources are fingerprinted before failures and compared afterwards. |
| This document | Actual implementation, commands/results, safeguards and limits. |

PR #7–#9's existing `OleDbConnection`/`ReadCommitted` transaction, single commit/rollback, immutable import/file/row storage and replacement audit are reused. The complete `PersistImport`, `FindCurrentSource` and `Calculate` bodies were compared with fetched `origin/uat` and are unchanged. `StatementEngine.cs`, `NORMSourceFileHandler.cs`, SQL helpers, SQL sources and other CPLATFORM modules were not changed in Step 3. No new SQL persistence statements or parameter-ordering convention were introduced.

## Confirmation, reinspection and transaction sequence

1. **Authorise and bind the retained preview.** The authenticated actor must equal the supplied acceptance actor and every preview owner. Invalid, expired or consumed previews fail. All replacement acknowledgements are checked before consuming any member, so unchecked acknowledgement can be corrected without consuming part of the batch.
2. **Protect the exact staged selection.** Inspection fingerprints cover identity, release/configuration, coverage, counts/exclusions, actual canonical row values, profile, totals and predecessor metadata. Checking actual values prevents a changed amount/description being hidden behind an unchanged `RowHash`. `ValidateBatch` records the complete staged membership, so omitting an entire selected file or substituting another preview fails. The existing single-source service entry remains compatible when a caller supplies newly inspected previews without a prior explicit staging call.
3. **Reinspect each complete original.** Verify bytes/hash, parse the full original with the existing approved profile and configuration, and rebuild every population. Compare complete population count, population-set fingerprint and individual row/context evidence with the retained inspection. Only fresh, private reinspection rows feed persistence. A submitted subset is still reparsed as a whole file and rejected when its sibling populations are missing; changing retained counts or exclusions also fails. Whole-file control/metadata/detail accounting from Step 2 remains mandatory.
4. **Validate the batch.** Reuse full five-part duplicate, single-agency, shared-file row assignment/completeness, size and configuration-snapshot checks. Release and period are excluded from source identity. A repeated identity under another release, or another coverage period, is still a duplicate/overlap. Upload limits count each uploaded file once.
5. **Open the established single transaction.** Recheck authenticated ownership and entity authority. Read/recheck every distinct approved release, its permitted populations, mapping/line snapshot and saved Year Setup designation using existing held-lock configuration queries, in ascending release-ID order. Any changed non-first release or year invalidates the batch.
6. **Lock/recheck each predecessor.** Use deterministic full-identity ordering for the existing `UPDLOCK,HOLDLOCK` current-source lookup. Recheck retained evidence, submitted bytes, expiry, complete staged membership and coverage within the transaction. All predecessor IDs must still equal those inspected. NULL ledger uses `IS NULL`, independently of actual `0L`. Shared original byte arrays are hashed once per retained array rather than once per population under the locks.
7. **Persist all populations, then commit once.** Call the unchanged `PersistImport` with each fresh population's own release, year, agency, rows, controls/totals, exact coverage and original bytes. Write acceptance/validation/profile/coverage and applicable predecessor-replacement audit evidence in the same transaction. Any later file, row, audit or commit failure rolls back the complete acceptance transaction. No previous import, original, calculation or unrelated row is deleted or deactivated.
8. **Calculate after commit.** Keep the existing one-import calculation calls; return results in the original preview order even though lock/persistence order is deterministic. No combined financial-statement run is created. A calculation failure reports accepted source IDs plus the existing calculation-failure message, without rejection, re-upload or rollback of accepted sources.

For the current supported layouts, a file has its evidenced file-level year and selected applicable release. Mixed-year/release acceptance operates across files; legacy company populations within one file share that evidenced context while retaining separate identities, rows, totals and predecessors. No hypothetical within-file mixed-year/report-block format was added.

## Persistence, replacement and evidence

The existing-compatible storage choice is **one original accepted import per population**. Every population from a shared upload retains the exact complete original filename, bytes and SHA-256 in its own import/file evidence. This duplicates retained bytes across the applicable imports and avoids a shared-file schema or fabricated split files. Each import/file row count represents its own population; audit additionally identifies full-file detail/control/metadata/blank counts and the shared original reference.

`IMPORT_CREATED` now includes the population's own entity/year/release, batch reference, original file-reference token, full-file population/record counts, accepted population rows and zero invalid rows. Existing profile, balance, storage and coverage validation audits remain transactional. `IMPORT_SOURCE_REPLACED` retains its existing detail contract, with the correct predecessor and the affected population's own five-part scope. Failed native-text acceptance retains the existing separate rejection-audit path; routine cancellation, expiry and unchecked acknowledgement still require no event. This step does not claim broader rejected-file retention or a redesign of rejection auditing.

The latest accepted original for an exact identity remains current, independently of release. Current-year partial replacement stores new actual dates/status and supersedes coverage; it does not append periods or relabel the predecessor. Valid sources remain acceptable with unmapped accounts or other missing required populations. Final approval completeness gating is outside this work.

## Actual verification

Commands ran from `C:\Development\NORM`. Tests use labelled controlled legacy/Defence copies and isolated configuration records, plus unchanged genuine native originals. Database and identity boundaries are substituted; production parser/service/page/persistence command and handler code is compiled and exercised. The new state substitute publishes accepted changes only on transaction commit and checks older/current/unrelated identities, bytes, rows and calculation links after every rejected attempt. It does not emulate the SQL optimiser or prove live SQL Server lock behaviour.

| Final command | Passed | Failed |
|---|---:|---:|
| `& ./tools/tests/Package1CStep3/Run.ps1` — 35 Step 1 + 32 Step 2 + 42 new Step 3 checks | 109 | 0 |
| `& ./tools/tests/Increment1B2/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 63 | 0 |
| `& ./tools/tests/Package1B/Run.ps1` | 38 | 0 |
| `& ./tools/tests/Increment1A/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 44 | 0 |
| `& ./tools/tests/Increment1B/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 21 | 0 |
| `& ./tools/tests/FR016/Run.ps1` | 43 | 0 |
| `& ./tools/tests/Package1BSourceDownload/Run.ps1` | 20 | 0 |
| **Unique final checks; reruns not accumulated** | **338** | **0** |

FR016 also passed production import/engine compilation against the real OleDb/helper APIs and full ASP.NET compilation of the final working-tree site, including actual markup/code-behind. The existing unrelated `eJET/eJET.aspx.cs:125` CS0162 warning remains. A models-only compiler warning found during development was corrected with an explicit field initialiser. Windows Application Control blocked initial isolated Increment1A/SourceDownload executables; authorised out-of-sandbox reruns passed. These were temporary test processes, not environment deployments. Workbook regressions explicitly pinned the approved repository fixture.

`git diff --check -- App_Code NORM tools/tests` passed. No SQL, statement-engine or source-download-handler change is present. Independent SHA-256 reads of all seven genuine Defence/AAG/Icon fixtures match the approved Package 1B manifest. Counts, movements, signs, CY-only semantics, controls and coverage remain covered by the genuine-profile suites; controlled multi-population acceptance is mechanics evidence, not genuine agency accounting evidence.

Final focused artifacts: `norm-package1c-step3-1900e397e1bb421b955c3e3c498f6cc3`. Final production/full-site build: `norm-fr016-87b371a6fc484563a464a570ebc1d3eb`. Runners print their complete absolute isolated temporary paths and per-case results.

### Focused outcomes

| Scenario | Actual result |
|---|---|
| Mixed files/populations/years/releases | Three-population controlled batch commits once; every stored import retains its own year/release/identity and results map back to preview order. |
| Companies, streams and optional ledgers | Departmental/Administered, NULL/0L and an additional `2L` population persist separately; unrelated current sources and all predecessor versions remain unchanged. |
| New plus replacement / release change | Both commit together; a new release still replaces the existing exact identity. Replacement audits name the correct predecessors. |
| Shared original and upload limit | Every applicable import retains identical original bytes/hash; existing handler retrieves them. The shared upload is charged once against its upload limit. |
| Coverage / missing required / unmapped | Current partial supersedes full coverage without append. Invalid member coverage blocks acceptance. Missing required populations and unmapped accounts do not prevent otherwise valid source acceptance. |
| Whole-file/batch omissions and substitution | Missing sibling, valid sibling of an invalid population, forged counts/exclusions, substituted inspection, omitted staged file and duplicate scope across releases all reject without accepted-state change. A subset attempt actually invokes full original reinspection. |
| Changed rows/bytes/identity/release | Reject before persistence, including changes to actual row data while retaining the old `RowHash`, and byte/row mutations injected after reinspection but inside the transaction. |
| Stale year/release/predecessor | Changed non-first release/population configuration, missing/changed saved year and changed non-first predecessor reject. Two independent previews also demonstrate a real sequential stale-source conflict after one commits. |
| Later persistence/audit/commit failures | Second-file, later-row, later replacement-audit, later storage-validation-audit and commit fault cases preserve the complete accepted-store fingerprint and produce no calculation. |
| Ownership / authority / replay | Wrong owner/actor, revoked authority before or inside the transaction and expired members reject. Successful multi-population confirmation cannot replay. |
| Page confirmation | Actual multipart upload renders all populations, year/release/status and encoded filenames; unchecked replacement preserves the preview with no source change; checked whole-batch confirmation succeeds. |
| Failed post-commit calculation | All accepted sources remain stored. Per-population failures are reported as calculation failures; the production handler retrieves exact bytes through failed-run links. No mappings are invented. |
| Deterministic locking | Release order is ascending ID; scope order is unchanged when preview submission order is reversed. This verifies command intent through substitutes, not live contention guarantees. |

## Limits, blockers and review recommendation

**No blocker remains for review of the implemented local Step 3.** No live SQL Server, Windows-authenticated browser, deployment or comprehensive concurrent-user assurance was performed or claimed. Package 1D still owns comprehensive live security/concurrency assurance. The existing transaction and original-retention schema require no migration for this implementation; publication remains separately authorised work.

The genuine native examples still contain one population each. No genuine native multi-population-file acceptance is claimed. Controlled fixtures prove partition/transaction/replacement mechanics only. Genuine multi-population source evidence and the owner-reviewed acceptance configuration remain the later acceptance inputs already recorded in the assessment; no approved data was modified to make local tests pass.

Existing source retrieval through failed/running/complete **run/file** links is preserved and tested. Calculation failure before any run record can be created still leaves accepted import/file bytes retained; import-only history/navigation and the no-run retrieval route remain the existing Step 4 dependency. This step neither changes the handler nor claims that excluded route is delivered. Calculation/accounting readiness and final financial-statement approval are not inferred from successful source acceptance.

**Recommend Project Owner review of Step 3. Stop before Step 4.** No PR, WARATAH deployment, shared-environment modification or SQL operation has been performed.
