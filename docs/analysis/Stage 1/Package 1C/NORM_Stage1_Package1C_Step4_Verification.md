# NORM Stage 1 — Package 1C, Step 4 verification

Date: 13 November 2026. Status: locally complete; ready for Project Owner review.

## Scope and baseline

Implemented source history, immediate-predecessor comparison, run-independent retained-original retrieval, separate population completeness and focused navigation. Existing local Steps 1–3 and unrelated working-tree changes are preserved. No Package 1D work, PR, deployment, shared-environment operation, schema migration, approved configuration change or genuine fixture change was performed.

`origin/uat` was refreshed and remains `59c487168f875b4de0fdd289c33324ec194f5d74`. The current checkout is `codex/norm-source-download-fix`, HEAD `2672324d23f61470f366a0dd8bf097147201faa1`; its application baseline matches that integration baseline before the existing local Package 1C work. No new commit was created.

## Implementation and reused functionality

| File | Methods / change |
|---|---|
| `App_Code/NORMSourceHistory.cs` | New read-only evidence models and `ApprovedReleases`, `ScopeForImport`, `CurrentPopulations`, `Versions`, `Compare`, `Accounts`, `ScopePredicate` and `Coverage`. Uses existing SQL helpers, access checks and retained import/row/file/audit evidence. |
| `NORM/NORM_SourceHistory.aspx` and `.aspx.cs` | Focused WebForms page. `LoadHistory`, population/history/comparison rendering and contextual URLs expose accepted populations, versions, originals, comparison and separately missing requirements. Output is HTML encoded. |
| `App_Code/NORMSourceFileHandler.cs` | `ProcessRequest` accepts exactly one import or legacy run selector plus file ID. Adds retained-byte SHA-256 verification while retaining existing authorisation, MIME/filename handling, no-cache response and audit-before-stream behaviour. |
| `NORM/NORM_Workspace.aspx` and `.aspx.cs` | Source-history entry points replace the former same-release, completed-run, GL-only mapped comparison in `BuildLatestActivity`. Existing financial statement and drill-through pages remain intact. |
| `NORM/NORM_Import.aspx` and `.aspx.cs` | Accepted results link to their source history. Initial `Page_Load` restores an explicitly requested release only when present in the authorised approved dropdown. History links retain release and stream context. |
| `tools/tests/Package1CStep4/{Run.ps1,HistorySupport.cs,PageHarness.cs,Tests.cs}` | New focused tests extending the existing Steps 1–3 harness; actual service/page/handler code with isolated database, identity and hosting substitutes. |
| `tools/tests/Package1CStep1/Tests.cs` | Conditional Step 4 test entry point; existing assertions retained. |
| `tools/tests/Package1BSourceDownload/{Run.ps1,TestSupport.cs,Tests.cs}` | Extend the existing handler suite for import/file retrieval, integrity and failure cases; compile the existing crypto helper. |

The Step 3 acceptance service, transaction, parser and partitioning implementation was not edited for Step 4. Existing `NORMSourceScope`, `NORMImportService.MissingRequiredPopulations`, `NORMHelper` authorisation and named-to-positional OLE DB command rewriting, `NORMCrypto`, retained originals and audit tables are reused. No new infrastructure or framework is introduced; full-site compilation uses ASP.NET WebForms / .NET Framework 4.8.

## Exact-scope history and comparison

The history key is Entity + FinancialYear + SourceCompanyCode + nullable SourceLedger + ReportingStream. NULL and `0L` remain distinct. All source queries constrain the complete identity. Version discovery uses classified retained rows and accepted, active, non-test original imports (`ParentImportId IS NULL`; Imported/Calculated). It does not depend on a calculation run or restrict history to one configuration release.

Versions are ordered by accepted ImportId descending, consistent with the existing source-selection convention. The next eligible same-scope version is the immediate predecessor. A → B → C therefore yields C → B and B → A even across release changes and unrelated intervening imports. Historical release inactivity does not remove an accepted original from history. Unclassified legacy identity is not guessed. An import with multiple populations requires an explicit population selection.

The page identifies current/superseded status, import, configuration release/version, source type/profile, retained coverage/period metadata, accepting operator/time, predecessor, original filename/hash and existing replacement audit evidence. Missing historical profile/coverage evidence remains explicitly unknown.

Comparison reads active retained account rows from both exact populations, with an outer row predicate as well as source eligibility checks. It does not aggregate across populations or apply accounting mappings. Ambiguous duplicate accounts in historical retained data produce a clear error rather than silently aggregating.

- Structural changes: added account, removed account and changed description, including added/removed accounts with zero balance.
- Financial changes: nonzero signed current minus previous balances, with previous/current/difference and separate positive, negative and net movement totals. An absent account contributes zero to arithmetic but is displayed as not present.
- Metadata changes: release, source type/profile and retained coverage/period changes are separate evidence.
- No predecessor: explicitly reported; no fabricated zero-source comparison.

Account evidence includes retained source row number and TB row ID. Existing statement drill-through is preserved; no new financial aggregation or drill-through framework was added.

## Retrieval, access and navigation

`NORM_SourceFile.ashx?import=<accepted-import>&file=<retained-file>` retrieves an exact accepted import/file original with no calculation dependency. Existing `run=<run>&file=<file>` URLs continue to work, including failed calculations. History provides both current and historical import/file links.

Both routes require an authenticated preparer and authorisation for the stored import entity. The file must belong to the supplied import or run. The byte query repeats ownership, accepted status, active-import and entity constraints; the run route retains its active-run condition. Actual retained bytes must match stored SHA-256, otherwise retrieval fails with 409. A successful `SOURCE_FILE_DOWNLOADED` audit must complete before any bytes are streamed; audit failure fails closed. Invalid/contradictory selectors, missing evidence and access failures produce no source download.

The source-history page checks preparer/entity access and clears rendered metadata on validation/access failure. Its completeness context is an explicitly selected accessible active approved release. Entity/year must match the selected source; it never substitutes the newest release or system year. A retired-release source remains viewable without implicitly selecting another completeness release.

Missing required populations reuse `MissingRequiredPopulations` and appear in their own section for the selected agency/year/release/stream. They neither invalidate accepted sources nor alter acceptance. Return links and authorised import-release restoration retain the preparation context. Workspace and accepted import results provide the entry points.

## Executed local verification

Commands were run from `C:\Development\NORM`. All final executions below passed. Test runners compile production code into isolated temporary directories and return failure on failed assertions.

| Command | Actual result |
|---|---|
| `& ./tools/tests/Package1CStep4/Run.ps1` | 32 Step 4 checks passed; combined Steps 1–4: 141 passed, 0 failed. |
| `& ./tools/tests/Package1BSourceDownload/Run.ps1` | 37 passed, including the original 20 compatibility checks and 17 additional checks. |
| `& ./tools/tests/Increment1B2/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 63 passed. |
| `& ./tools/tests/Package1B/Run.ps1` | 38 passed. |
| `& ./tools/tests/Increment1A/Run.ps1` | 44 passed. |
| `& ./tools/tests/Increment1B/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 21 passed: 14 service, one real calculation and six real-helper display/retrieval assertions. |
| `& ./tools/tests/FR016/Run.ps1` | 43 passed (34 import/transaction + nine exact-decimal engine checks); real-helper/OleDb production compilation and full ASP.NET site compilation passed. |
| `git diff --check -- App_Code NORM tools/tests` | Passed; line-ending conversion notices only. |

Total: **387 passing checks**, including **49 new Step 4 checks** (32 history/page + 17 download). The combined suite includes the earlier 109 Steps 1–3 checks without double-counting separate executions.

Final Step 4 artifact directory: `norm-package1c-step4-bb85ebac65bb4d59971e22ed93387a21`. Final full-build artifact directory: `norm-fr016-690d816af79c49d5bfa6247f99b181f3` (both under the runner's isolated temporary directory). Full compilation reports the existing `eJET/eJET.aspx.cs:125` CS0162 unreachable-code warning. An earlier full-build invocation returned a failure; the diagnostic rerun, with no intervening production-code change, completed successfully as recorded above.

Focused coverage includes A/B/C predecessors across releases; unrelated identities and NULL/0L isolation; test/derived/inactive/unaccepted exclusions; no-run and failed-run histories; retired releases; structural zero and description-only changes; positive/negative signed movements; metadata changes; unknown legacy evidence; ambiguous historical accounts; actual page encoding, access rejection and contextual return; missing requirements independent of acceptance; genuine single-population acceptance followed by calculation failure before run creation and exact original retrieval; and independent histories/originals for accepted mixed-population batches.

Download tests exercise current/historical/no-run evidence, complete/failed legacy runs, invalid owner/file combinations, contradictory selectors, authentication and entity denial, changed accepted status/ownership at byte fetch, corrupted retained bytes and audit failure before streaming.

### Genuine source integrity

Read-only SHA-256 checks of all seven Package 1C genuine sources match the existing source manifest:

| Source | SHA-256 |
|---|---|
| Defence ERP FY26 | `1c4bad018973963aa67ff37689eb1abaca7009661b92681415ceb12cdc06c195` |
| AAG 2020 | `9d3a113b3c0ef344c7d3c419f5c8d6e241c9543b2731f4f9d53098cfae5ad631` |
| AAG 2019 | `e0723bd91976302d38f0d0c79c66acf5f640acead0d2f4eb4b7adfd64384e3b8` |
| AAG 2018 | `1a3920bc0290f323088b42f7913cacddca815d7f7a1b7b1fbbb5e3697126170b` |
| Icon company 86 | `868589229f3a0a58851cccce97eedfcff4210590c6278404d1f5bf74812ca4b7` |
| Icon company 87 | `0b92408349be4c93b451fac5f88e6f31b09c3983e7b13e361571c0d5b0091394` |
| Icon company 80 | `019f3f14975ff8a1f672b59fd8bed9fc95ab0cb4b36c70031db36ef0dae61f7f` |

The genuine AAG 2020 acceptance/no-run download case verifies returned bytes against its retained original hash. Controlled fixtures are labelled and used for replacement chains and mixed-population mechanics. No genuine native multi-population source acceptance is claimed.

## Limits and review readiness

No unmet local Step 4 criterion or genuine implementation blocker remains. The prior Step 3 dependency on a run-independent original route is resolved. Source acceptance and history do not imply accounting readiness or financial-statement approval.

Database/identity substitutions verify local application behaviour and query intent, not live SQL execution, Windows-authenticated browser operation or comprehensive concurrent-user/security assurance. Those environment checks and Package 1D remain outside this step. No shared database was accessed or modified for this work, and no schema change is required.

Recommend Project Owner review of Step 4. Stop here before PR, WARATAH deployment or Package 1D.
