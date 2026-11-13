# NORM — Package 1C, Step 2: Population Partitioning and Independent Validation

Living verification record · 13 November 2026

## Outcome and scope

**Step 2 is implemented and locally verified, ready for Project Owner review.** File inspection can partition the existing legacy ERP layout's explicit company rows into separate population previews and validate each independently. Offsetting population imbalances cannot pass as a combined zero. The preview reconciles source records and distinguishes invalid rows from valid rows withheld from acceptance.

Multi-population file acceptance remains blocked at page and service boundaries, including an attempt to confirm just one extracted population. Mixed-release acceptance remains blocked. The original single-population acceptance path remains available.

This extends the locally verified [Step 1](NORM_Stage1_Package1C_Step1_Verification.md) and implements the bounded Step 2 in the [gap assessment](NORM_Stage1_Package1C_Gap_Assessment.md). BRD v0.7 FR-010/012/013/016–018, approved D1–D6, Package 1A identity/coverage/access controls and the [approved Package 1B profiles/fixture manifest](../Package%201B/NORM_Stage1_Package1B_Source_Profiles.md) remain authoritative. No decisions were reopened.

Refreshed `origin/uat`: `59c487168f875b4de0fdd289c33324ec194f5d74`, unchanged from Step 1. Continued in the existing checkout on top of the uncommitted Step 1 work; preserved its verification record and other pre-existing changes. No branch, commit, PR, deployment, SQL/schema/configuration-record change or shared-environment access occurred. No Step 3 or Step 4 implementation was undertaken.

## Files and methods changed in Step 2

| File | Changes and reuse |
|---|---|
| `App_Code/NORMImportModels.cs` | Small serializable `NORMImportFileContext` holds a shared original reference, filename/hash, population count and source-record counts. Each existing `NORMImportPreview` retains its own rows, row count, invalid-row set, totals, errors, release/fingerprint, coverage and reused `NORMSourceScope`. Existing row model made serializable for session compatibility. No database/file-storage architecture added. |
| `App_Code/NORMImportService.cs` | Extracted Step 1 binding into `ResolveAgencyRelease` for reuse. New `InspectFileForAgency` returns population previews; `InspectPopulations` groups existing parsed row evidence; `BuildPopulationPreview` reuses the former single-population validation body for each group. `ValidatePreparationBatch` checks full five-part duplicate identities and complete/exclusive row assignment. `BatchConfirmationBlockReason` adds a multi-population safety block. Original single-result APIs continue rejecting multi-population files. Native text rejection auditing is retained on the new file-inspection route. |
| `App_Code/ErpTrialBalanceImporter.cs` | `ParseLegacy` shares the existing parser with an inspection-only strict mode. Inspection rejects unsupported nonblank detail/subtotal/control records instead of skipping them, unsupported extra detail fields, financial detail before the header, and identifiable financial rows on an additional legacy worksheet. `CountReportRows`/`BlankReportRow` expose selected-report counts; record reconciliation also prevents an orphan native record being counted as successfully extracted detail or metadata. Existing historical direct `Parse` retains its permissive filtering contract; governed inspection uses strict mode. |
| `App_Code/NORMTextTrialBalanceImporter.cs` | Existing `Csv` and `Fixed` parsers add record-count metadata after their unchanged native validation. No new format, report-block recognition, comparative population or monetary interpretation. |
| `NORM/NORM_Import.aspx.cs` | Upload handler adds all population previews returned for each file. `ShowPreview` shows one file accounting summary plus separate population results, original hash, accepted/invalid/withheld counts and existing replacement information. Existing confirmation handler uses the extended service block before acceptance. |
| `NORM/NORM_Import.aspx` | Clarifies inspection-only status for mixed contexts/multi-population files and identifies replacement by the complete five-part scope. Existing controls/layout reused. |
| `tools/tests/Package1CStep1/Tests.cs` | Minimal partial-class/test-runner hook allows the dedicated Step 2 suite to reuse the existing fixture builders, release/configuration substitute and assertions. All 35 Step 1 checks still run unchanged. |
| `tools/tests/Package1CStep2/Run.ps1`, `Tests.cs` | Dedicated focused suite compiles production models/parsers/service/page, reusing the existing multipart/session/control harness and parameter-aware substituted storage. Controlled fixtures are constructed/modified only in memory. |
| This document | Results, accounting definitions, safety boundaries and review disposition. |

## Partitioning and independent validation

The existing legacy eight-column structure already provides company per detail row. Its financial year, dates and optional explicit reporting-stream metadata govern the file. The existing inspection adapter translates the historical company-in-`SourceLedger` parser contract into company plus NULL optional ledger. Configuration must authorise the resulting population and supply an unambiguous missing stream. Distinct companies may therefore resolve to distinct Departmental/Administered streams using approved configuration. No stream, ledger or population is inferred from GL accounts or statement destinations.

Rows group by their resolved company/optional-ledger/stream under the file's bound agency/year/release. Each group gets its own existing preview and validation, not a file-wide balance verdict. Each retains original source-row numbers, the unchanged complete original bytes/hash and a shared file-reference token. Original bytes are cloned once for that inspection and shared by its previews; no split file is fabricated. Batch upload size counts each uploaded file once.

Existing permitted-population resolution, configured Year Setup eligibility, exact-zero, lossless storage checks, unmapped-account warnings and current/predecessor lookup are reused per population. Account uniqueness is checked at the population's accepted GL grain: the same GL in different populations is allowed; duplicate accounts within one population fail. Release is provenance, not part of the duplicate source key; NULL ledger remains distinct from actual `0L`.

An unresolved/unauthorised company or ambiguous stream remains an invalid population with its source rows and explanation; it is not silently filtered or guessed. Precision and duplicate-account errors identify affected row numbers. Nonzero population balance, coverage failure or arithmetic overflow conservatively invalidates that population's rows; the preview does not pretend to identify the particular accounting entry responsible. Other valid rows are withheld when any batch member is invalid. Unmapped accounts alone remain warnings.

The legacy file contract has one year/coverage context. Step 2 does not invent row-level years, ledger columns, stream columns or alternative coverage. Invalid-year/coverage tests use contradictory file metadata or separate batch members as appropriate. Malformed amounts, unsupported records or structurally invalid dates reject the complete file with a specific error before staging; such rejected files receive no successful reconciliation claim or partially accepted population set.

### Row-accounting definitions

For every successfully inspected file:

`source records = financial detail + metadata/header/separator records + source control records + blank records`

`financial detail = sum of population row counts = distinct assigned source-row numbers`

For every population:

`population detail = invalid rows + validated rows awaiting confirmation or withheld`

**Accepted rows are always zero during inspection.** “Validated” never means accepted. An invalid batch displays its valid rows as withheld. A valid multi-population file also remains withheld by the explicit acceptance safety block.

| Profile | Counting basis and legitimate exclusions |
|---|---|
| Legacy ERP | Selected report worksheet rows, including positional blanks. Nonblank preamble/header rows are metadata after checks against misplaced financial detail. All supported post-header detail must be recognised; no undocumented nonblank subtotal/control/footer is excluded. No independent printed control totals are claimed; source-control count is zero and balances are calculated separately per population. Non-report worksheets are outside this selected-report count; identifiable financial rows on another legacy sheet cause rejection. |
| Defence ERP | Existing selected report worksheet: financial detail, validated company subtotal and grand-total records (two controls), non-detail preamble/header records and blanks. Additional supported report headers remain ambiguous/rejected. Financial detail before the recognised header or an orphan unrecognised post-header record cannot become successful row reconciliation. No hypothetical multi-block recognition was added. |
| ActewAGL CSV | Logical CSV records; a quoted embedded newline belongs to its CSV record. Five preamble records plus the header are metadata; spacer/blank records are separate. Only CY detail is imported; PY stays retained comparative content and is not another population. No source control row exists. |
| Icon Water | Normalised report lines, including terminal blank line when present. Repeated page headings, metadata and positional separator rules are non-detail; the final validated numeric footer is one control record. All pages belong to the same company population. Existing signs, controls and MAR-16 dates are unchanged. |

`ValidatePreparationBatch` rejects repeated row assignment, a missing population, inconsistent per-population counts or an unreconciled file total. It also retains size, single-agency, configuration-snapshot and five-part duplicate checks. Unsupported unknown rows do not qualify as successful metadata/control exclusions merely because the extracted detail balances.

## Confirmation safety

The file context retains the original population count on every child preview. `BatchConfirmationBlockReason` checks that count before mixed-release checks, producing:

> Multi-population file inspection is supported, but acceptance is not yet enabled. No population from this file can be accepted until atomic population acceptance is available.

The preview disables confirmation and replacement controls. The production page confirmation handler and public `ConfirmBatch` service boundary independently enforce the same block. Tests directly invoke the disabled page handler and try `Confirm` on a single extracted population: both reject before opening storage, consuming the preview or writing accepted/audit rows. Ordinary invalid single-population members also reject an otherwise single-release batch before acceptance writes; these tests do not rely on the multi-population guard to conceal invalid-member behaviour.

The complete `ConfirmBatchCore`, `ValidateBatchCore` and `PersistImport` method bodies were compared against fetched `origin/uat` and are unchanged. No guard inside their transaction was necessary. Existing single-source reparse, stale checks, locks, one commit/rollback, replacement/history retention and post-commit calculation behaviour remain intact. New multi-population previews cannot be persisted through the original single-result inspection/reparse route either.

## Commands and actual verification results

All commands ran from `C:\Development\NORM`, using isolated temporary copies and the existing .NET Framework compiler. Database/identity boundaries were substituted; actual production parser/service/page logic and ASP.NET multipart/session/control objects were exercised. No live SQL, Windows-authenticated browser UAT, Package 1D concurrency/security assurance or deployment is claimed.

| Command | Passed | Failed |
|---|---:|---:|
| `& ./tools/tests/Package1CStep2/Run.ps1` — includes 35 Step 1 and 32 Step 2 checks | 67 | 0 |
| `& ./tools/tests/Increment1B2/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 63 | 0 |
| `& ./tools/tests/Package1B/Run.ps1` | 38 | 0 |
| `& ./tools/tests/Increment1A/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 44 | 0 |
| `& ./tools/tests/Increment1B/Run.ps1` | 21 | 0 |
| `& ./tools/tests/FR016/Run.ps1` | 43 | 0 |
| **Unique final suite checks** | **276** | **0** |

FR016 also passed production import/engine compilation with the real OleDb/helper APIs and full ASP.NET site compilation of the current working-tree application. The existing unrelated `eJET/eJET.aspx.cs:125` CS0162 warning remains. `git diff --check -- App_Code NORM tools/tests` passed; no SQL file changed. Completed Package 1A/1B live UAT was not repeated.

Final focused artifacts: `norm-package1c-step2-4523ad860df34409944bb1d4ee0b7434`; final full-build artifacts: `norm-fr016-d10be043e6f148c28648db660cb9a927`, under the runner-reported isolated temporary directories. Runners print full paths and individual case results.

The 32 new checks cover separate multi-company contexts/totals, shared originals, same GL across populations, unique configured streams, within-population duplicates, opposite nonzero nets, precision/coverage failures, unauthorised/ambiguous scopes, ambiguous releases, contradictory years, blank/excluded/unsupported records, misplaced/extra-sheet detail, incomplete/repeated assignment, duplicate five-part identities, NULL/0L, real multipart preview, both confirmation guards, ordinary invalid-member batch rejection, single-source acceptance and mixed-release blocking. All seven genuine native fixtures are also inspected through the new API with their expected row and control counts; existing suites retain the detailed monetary/sign/coverage assertions.

Independent SHA-256 checks of all seven original fixtures match the approved Package 1B manifest. Defence remains 984 rows, with debit movement `4,466,384,209,424.24`, signed credit movement `-4,466,384,209,424.24` and zero ending net. The three AAG CY counts remain 379/386/409; the three Icon counts remain 18/17/412. No genuine original was modified. Controlled legacy/Defence copies and isolated configuration rows demonstrate mechanics only, without accounting mappings or claims of genuine agency financial correctness.

## Limits and review recommendation

**No unmet criterion or blocker remains for this authorised local Step 2.** The genuine Defence, ActewAGL and Icon examples still establish one population per file. Their parsers have not been extended to recognise hypothetical multiple blocks, extract comparative populations or accept a new layout. Genuine native multi-population-file acceptance remains unverified and is not claimed; controlled legacy evidence establishes partition/validation/failure mechanics only.

Structural file rejection deliberately produces a file-level error rather than incomplete population previews. Accepted/rejected row persistence, atomic multi-population/multi-release acceptance, history/comparison changes and final import assurance remain later authorised work. No accounting aggregation, mapping or calculation change was needed.

**Recommend Project Owner review of Step 2. Stop here before Step 3.** No PR or deployment has been created or performed.
