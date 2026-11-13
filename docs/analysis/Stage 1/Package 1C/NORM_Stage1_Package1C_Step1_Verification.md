# NORM — Package 1C, Step 1: Population Context and Release Binding

Living implementation and verification record · 13 November 2026

## Outcome

**Step 1 is implemented and locally verified, ready for Project Owner review.** A single-agency upload can now prepare files from different authorised financial years, bind each to its applicable active approved configuration release, and show each file's own identity, release and coverage. Mixed-year/mixed-release batch confirmation is disabled in the preview and rejected at both page and service boundaries before the acceptance transaction opens.

Authorised scope: [Package 1C assessment, Step 1](NORM_Stage1_Package1C_Gap_Assessment.md). No population splitting, new profile, mapping, financial calculation, schema change, configuration-record change, PR or deployment was undertaken. Existing working-tree changes and original fixtures were preserved.

Fetched `origin/uat` at `59c487168f875b4de0fdd289c33324ec194f5d74`; the starting application/test/SQL trees matched that baseline. Reused the current checkout, `codex/norm-source-download-fix` at `2672324d23f61470f366a0dd8bf097147201faa1`; no branch or commit was created. All changes remain local.

## Implementation and reuse

| Changed file | Change / existing functionality reused |
|---|---|
| `App_Code/NORMImportModels.cs` | `NORMImportPreview.Population` reuses `NORMSourceScope` for the five-part identity. The same one-population-per-file preview carries approved release/version/fingerprint, exact dates/full-or-partial status and original filename/hash/token/retained bytes. Added a read-only approved-configuration fingerprint accessor; no new general import model hierarchy. |
| `App_Code/ErpTrialBalanceImporter.cs` | Added preparation-only `DiscoverForInspection`, sharing existing content recognition, native detail/control validation and coverage parsing. Normal `ParseForInspection` still requires its expected year and authorised companies. Discovery reads the source year first; legacy discovery checks consistent fiscal-year metadata. No layout or monetary interpretation change. |
| `App_Code/NORMTextTrialBalanceImporter.cs` | Added preparation-only discovery sharing the CSV/fixed-width parser bodies. Discovery omits only the selected-release year comparison; normal bound inspection repeats it. Native CSV CY-only rules, Icon MAR-16 interpretation, signed movements, controls and exact-zero checks remain. |
| `App_Code/NORMImportService.cs` | Added `InspectForAgency`: load the explicitly selected primary approved configuration to establish agency, discover source facts, bind the detected year, then repeat existing approved-release/population/coverage/precision inspection. Preparation batch validation now uses all five identity dimensions independently of release and preserves size/configuration-snapshot checks. Added a mixed-context confirmation block outside the existing transaction. |
| `NORM/NORM_Import.aspx` | Reused approved-release controls; added an optional multiple-selection list for other years' explicit approved releases. Explained automatic unique-year resolution, ambiguity and the mixed-batch acceptance restriction. |
| `NORM/NORM_Import.aspx.cs` | Uses each file's bound context for existing preview rendering. Primary release starts with an explicit selection prompt, rather than defaulting to the newest listed release. Snapshots primary/additional selections in signed ViewState and rejects changed selections on confirmation. Mixed contexts disable confirmation/replacement controls and show a specific review-only message. |
| `tools/tests/Package1CStep1/Run.ps1`, `Tests.cs`, `PageHarness.cs` | New focused suite compiles production parsers/models/service/page with substituted storage and identity. Exercises actual multipart uploads through the production inspection handler, real session/control objects, rendered preview, and direct invocation of the confirmation handler despite its disabled control. Fixtures are genuine originals or labelled, in-memory controlled mechanics copies. |
| `tools/tests/FR016/TestSupport.cs` | Added an optional query callback with parameter names/values/order so the new suite verifies agency/year filtering and positional OleDb arguments. Existing suites continue using their existing substitutes. No real connection implementation. |
| `tools/tests/Increment1B2/PageHarness.cs` | Added the new list control and selection snapshot to the existing page harness. Production confirmation/session handlers remain the code under test. |
| This document | Implementation, test results, limits and review disposition. |

Release binding uses configuration facts, never a source-supplied agency, calendar date or newest-release assumption. The primary selection explicitly binds its year. For another year, one explicitly selected approved release binds that year; conflicting explicit choices for the same year reject clearly. Without a choice, exactly one active approved release for the agency/year is required. Zero candidates or multiple candidates reject with a corrective message; the service never sorts and takes the latest. A unique release must still authorise the actual company/ledger/stream. Existing entity access checks apply even if the caller can access several agencies.

The selection UI binds one explicit release per year for this upload; it does not introduce per-file overrides for competing same-year configurations. Unresolved bindings fail safely. This satisfies the Step 1 mixed-year preparation scope without enabling a broader release-selection workflow.

`Population` contains identity, not release identity. Duplicate detection includes agency/year/company/nullable ledger/stream and excludes release and period; NULL and actual `0L` remain distinct. Same identity under different releases is a duplicate. Different years with the same company/ledger/stream are separate populations. Existing configured stream resolution, missing-required independence, unmapped warnings, precision and Year Setup coverage rules are reused.

Discovery does not authorise acceptance. Its output is followed by normal bound inspection, and each file still must resolve to one population. Both-stream and multi-company mechanics remain available across separate files; the existing multi-population-file rejection is unchanged. Original bytes are cloned/hashed through the existing preview path, not converted into invented split files.

## Acceptance safety

`BatchConfirmationBlockReason` is used by preview rendering, the page confirmation handler and the public service confirmation boundary. The message is:

> Mixed-year or mixed-release batches can be inspected, but batch acceptance is not available for them yet. Inspect a separate single-release batch to accept sources.

The service block precedes preview consumption and opening any transaction. Tests assert no acceptance rows, calculation, transaction or audit writes for this unsupported confirmation. A disabled button alone is not the safeguard.

The existing `ValidateBatchCore`, complete `ConfirmBatchCore` and `PersistImport` method bodies were compared with fetched `origin/uat` and are unchanged. Existing locking, predecessor rechecks, replacement acknowledgement, one commit/rollback, original retention and post-commit calculation behaviour are preserved. A separately inspected, single-release batch remains eligible, including when its bound year differs from the primary agency configuration's year; the saved selection snapshot prevents incorrectly comparing the primary selection with the first file's bound release.

## Commands and actual results

Commands ran from `C:\Development\NORM` using the existing .NET Framework compiler and isolated temporary copies. No shared database, WARATAH, browser UAT or deployment access was used. Counts below are unique final suite results, not totals accumulated from reruns.

| Command | Passed | Failed |
|---|---:|---:|
| `& ./tools/tests/Package1CStep1/Run.ps1` | 35 | 0 |
| `& ./tools/tests/Increment1B2/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 63 | 0 |
| `& ./tools/tests/Package1B/Run.ps1` | 38 | 0 |
| `& ./tools/tests/Increment1A/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 44 | 0 |
| `& ./tools/tests/Increment1B/Run.ps1` | 21 | 0 |
| `& ./tools/tests/FR016/Run.ps1` | 43 | 0 |
| `& ./tools/tests/Package1BSourceDownload/Run.ps1` | 20 | 0 |
| **Total** | **264** | **0** |

FR016 also passed production import/engine compilation against real OleDb/helper APIs and full ASP.NET site compilation, including the actual page markup/new control. The existing unrelated `eJET/eJET.aspx.cs:125` CS0162 warning remains. `git diff --check -- App_Code NORM tools/tests` passed. No SQL file changed.

Final focused artifact directory: `norm-package1c-step1-bfb109c35828463793849c77490df451`; final production/full-site build: `norm-fr016-ea0522d3b1a547d8aedca4a99a6f1038`, each under its runner-reported isolated sandbox temporary directory. Runners print full absolute artifact paths and per-case results. No test files or modified fixture copies were written into genuine source examples.

### Focused acceptance coverage

| Required scenario | Actual result |
|---|---|
| Existing single-year import | Passed: preparation and unchanged confirmation; existing replacement/session/rollback regressions pass. |
| Two authorised years / one agency | Passed with genuine FY2019/FY2020 AAG files under isolated same-agency configurations; distinct release/fingerprint and dates. Actual multipart page test uploads FY2019 first while primary is FY2020 and verifies both contexts. |
| Companies/ledgers/streams | Passed with controlled Defence copies: multiple companies, ledgers and both streams across separate files. Each five-part identity remains separate. |
| NULL versus 0L | Passed as separate configured identities and batch keys; original NULL lookup/persistence regression passes. |
| Wrong-year/unavailable/inactive/unapproved release | Passed: explicit wrong-year inspection rejects; wrong-year or other-agency candidates cannot satisfy the detected year; unavailable/draft/inactive explicit selections and candidate sets reject. |
| Ambiguous approved releases | Passed: no automatic newest choice, explicit older release succeeds, conflicting selections reject; actual page additional-selection path resolves ambiguity. Primary control initially has no selected release. |
| Cross agency | Passed: explicit selection rejects even with access to both agencies; automatic candidates are agency-filtered; separately staged cross-agency previews fail batch validation. |
| Duplicate five-part identity | Passed even under two distinct releases; different years and NULL/0L do not collide. Changed snapshots of the same release also invalidate staging. |
| Mixed-release confirmation | Passed: disabled preview plus page/service rejection before any acceptance transaction. Changed additional selections invalidate confirmation. |
| Existing native facts | Passed: Defence 984 rows, signed controls, FY2026 full dates; genuine CSV counts/totals/CY-only treatment; Icon counts, signed credit movements, printed controls and exact partial dates. Existing three-profile regression suite passes. |
| Coverage / validation / splitting boundary | Passed: non-current Icon partial source still rejects; controlled legacy discovery preserves identity; nonzero legacy preview remains unconfirmable; multi-population file still rejects. Existing exact-zero, precision, stale-year and atomic rollback tests pass. |

Genuine files remain unchanged: independently read SHA-256 values match the approved Package 1B manifest for Defence, AAG FY2020/FY2019 and Icon company 86. These establish source facts; isolated configurations and controlled mutations establish binding/partition-identity mechanics only. No accounting mapping or agency financial-statement correctness is inferred.

## Limits, blockers and disposition

No unmet criterion or blocker remains for the authorised **local Step 1** implementation. SQL Server locking, Windows-authenticated browser operation and comprehensive concurrency/security assurance are not claimed by these substituted-storage tests. Completed Package 1A/1B live UAT was not repeated. No genuine approved configuration records were changed for testing.

Mixed-release atomic acceptance and multi-population file splitting remain intentionally unavailable and require further authorisation. Genuine multi-population-file evidence and the owner-reviewed same-agency release/population matrix remain the later Package 1C acceptance inputs recorded in the assessment; they do not block review of Step 1. The normal approved profiles still produce one population per file.

**Stop for Project Owner review.** Step 2 has not begun. No SQL change, shared-environment modification, PR or publishing action is authorised or performed by this step.
