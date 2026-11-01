# FR-016 — implementation and isolated verification

Date: 19 October 2026 (Australia/Sydney). Stage B/C complete; awaiting review.

## Scope and baseline

Stage A was approved for implementation and isolated testing only. No commit, push,
pull request, merge, deployment, SQL execution, live database access or WARATAH access
was performed. No schema, shared model/interface, parser, protected configuration or
unrelated application source was changed.

Checkout: `steve/norm-onboarding`, HEAD `8c33b163a4e2e6ff84e6f89d4259dd96086bd53a`.
Locally recorded `uat` and `origin/uat` match that commit. Remote freshness was not
verified: no fetch or Git integration was authorised. Existing untracked
`docs/analysis/` and `docs/requirements/` predate this work and were preserved.

Business references: BRD v0.6 FR-016, directly necessary FR-010 controls and FR-080
zero unexplained difference; 16 October assessment under
`docs/analysis/BRD_Assessment_20261016_0946/`.

## Original defect and correction

Previously, `CommitImport` computed debit, credit and net, then accepted the import
without requiring net zero. The subsequent `DEBITS_EQUAL_CREDITS` check permitted
differences up to and including AUD 1.00. A calculation failure did not undo the
previously committed source.

The acceptance path now requires exact decimal net zero and lossless storage of all
selected reporting row amounts and import totals. It does not round data to make a
check pass. Non-zero totals, including sub-cent differences, stop before the import
transaction. Balanced amounts containing non-zero fractional cents also stop, since
the existing database cannot retain them without changing their value. Trailing
decimal zeroes are accepted (for example 1.2300 equals 1.23).

ERP signed ending balances and its signed movement reconciliation are unchanged.
ROMAN final recognised amounts and trailing-minus credits are unchanged. The balance
population is the currently selected statement input after existing ledger filtering;
it is not an independently defined company/stream/partition population.

## Exact files and methods changed

| File | Methods / effect |
|---|---|
| `App_Code/NORMImportService.cs` | `CommitImport`: retains duplicate check ordering; computes exact totals; rejects before reporting writes; commits correlated rejection audit events separately; writes successful check evidence inside the existing acceptance transaction; sanitises failures from the new audit handling. |
| `App_Code/NORMImportService.cs` | New private `FitsStoredAmount`: checks inclusive +/-99,999,999,999,999,999.99 range and equality to the two-place decimal representation. Applies to accumulated balance, each non-null opening/debit/credit amount and stored import totals. |
| `App_Code/NORMImportService.cs` | New private `ExactAmount` and `StorageError`: retain exact invariant decimal values and identify the first offending source row/field for audit. |
| `App_Code/NORMImportService.cs` | New private `WriteBalanceAudit`: records separate balance and storage results, units, expected value, difference, tolerance and totals. |
| `App_Code/StatementEngine.cs` | `WriteValidations`: changes only `DEBITS_EQUAL_CREDITS` to compare unscaled `totalNet == 0m`, zero tolerance and exact AUD detail. Other checks and their tolerances are unchanged. |
| `tools/tests/FR016/TestSupport.cs` | New isolated database/transaction substitutes and calculation invocation substitute for service tests. No real connection implementation. |
| `tools/tests/FR016/Tests.cs` | New synthetic ERP/ROMAN fixtures, acceptance and validation tests. |
| `tools/tests/FR016/Run.ps1` | New repeatable compiler/test runner and isolated full-site compile. |
| `docs/FR016_Implementation_and_Verification_20261019.md` | This evidence and UAT handover. |

## Audit handling

Rejected balance/storage attempts have a fresh GUID `ImportAttempt` identity and
transactionally retained events in existing `tblNORM_AuditEvent`:

- `IMPORT_REJECTED`: release, year, entity, source type, combined source hash, selected row count.
- `IMPORT_BALANCE_VALIDATION`: `DEBITS_EQUAL_CREDITS` result, exact AUD actual/difference,
  expected zero, tolerance zero, debit and credit totals.
- `IMPORT_STORAGE_VALIDATION`: lossless-storage Pass/Fail and the first row/field/amount
  that fails, or a totals-range explanation.
- `IMPORT_REJECTED_SOURCE`: per-source hash, byte count, periods, statement-input flag
  and one bounded Unicode filename field. No separate filename events or offsets.
  Spaces and Unicode remain unchanged; only `%`, `;`, `=`, CR and LF are escaped as
  `%25`, `%3B`, `%3D`, `%0D` and `%0A`. Decode these tokens once, with `%25` last.
  Supported names contain 1-260 UTF-16 code units. At worst their escaped representation
  is 780 characters; the complete source event fits the existing 2,000-character field.
  Empty/oversized names explicitly fail the audit transaction without truncation; the
  existing safe error says rejection evidence could not be saved. No reporting data is created.

The audit table's existing user and UTC timestamp fields identify actor and time.
Free-text entity/name values are escaped to preserve field boundaries. Repeated rejected
uploads create distinct attempts; they do not enter the accepted-import duplicate check.
Existing accepted duplicates retain their previous error and create no new import.

No rejected import header, import file blob, reporting row or calculation is created.
All rejection evidence commits together. A write/open/commit failure remains a rejection
and reports that evidence could not be saved, without exposing the underlying provider
error. Successful balance/storage audit events commit alongside the existing accepted
import, original files, rows and `IMPORT_CREATED` event. A failure in those new audit
writes rolls back the existing transaction and prevents calculation invocation.

Extremely large parsed inputs can exceed the decimal accumulator. This stops acceptance;
the audit records balance `NotRun` with the overflow reason and storage `Fail`, rather
than inventing an exact aggregate or recording Pass. This is audit text, not a new value
in the calculation validation-result table.

## Automated results

Command from repository root:

```powershell
& './tools/tests/FR016/Run.ps1'
git diff --check
```

Final result: **34 service tests + 9 StatementEngine tests = 43 PASS, 0 FAIL**.
Runner exit code 0. `git diff --check` passed.

All service test expectations below concern the isolated substitutes, not actual SQL.
In rejection tests, assertions check no committed import header, file, reporting rows,
or invocation of the calculation substitute. Successful tests exercise the real parser
and service through its public `Import` entry point, with substitute database data.

| Service test | Expected | Actual | Status |
|---|---|---|---|
| ERP +100 / -100 | Accept, retain validation Pass, invoke calculation once | As expected | PASS |
| +10.01,+20.02,-5.01,-25.02 | Accept multiple debit/credit rows | As expected | PASS |
| +1.2300 / -1.23 | Accept lossless trailing zeroes | As expected | PASS |
| 0 / 0 | Accept zero-valued rows | As expected | PASS |
| SQL maximum / negative maximum | Accept lossless boundary | As expected | PASS |
| +0.01 difference | Reject, exact failure evidence, no reporting writes | As expected | PASS |
| -0.01 difference | Reject, exact failure evidence, no reporting writes | As expected | PASS |
| +0.99 difference | Reject, exact failure evidence, no reporting writes | As expected | PASS |
| +1.00 difference | Reject, exact failure evidence, no reporting writes | As expected | PASS |
| -1.00 difference | Reject, exact failure evidence, no reporting writes | As expected | PASS |
| +0.001 difference | Reject without rounding | As expected | PASS |
| -0.001 difference | Reject without rounding | As expected | PASS |
| +1.001 / -1.001 | Balance Pass but storage Fail; reject | As expected | PASS |
| Opening amount 1.001; balanced endings | Storage rejection identifies OpeningBalance | As expected | PASS |
| Debit movement 0.001; balanced endings | Storage rejection identifies DebitMovement | As expected | PASS |
| Credit movement 0.001; balanced endings | Storage rejection identifies CreditMovement | As expected | PASS |
| Difference 0.0000000000000000000000000001 | Reject, retain exact difference | As expected | PASS |
| +/-100000000000000000 | Reject amount outside SQL range | As expected | PASS |
| Two +60000000000000000 and two negative | Reject stored-total overflow despite zero net | As expected | PASS |
| Two Decimal.MaxValue values | Reject; balance NotRun and storage Fail retained | As expected | PASS |
| Missing ERP ending value | Existing invalid-ending error; no reporting writes | As expected | PASS |
| Malformed workbook bytes | Parser error; no reporting writes | As expected | PASS |
| ROMAN 100.00 / 100.00- | Accept original sign convention | As expected | PASS |
| ROMAN 100.01 / 100.00- | Reject +0.01 | As expected | PASS |
| ROMAN matched row missing amount | Existing missing-amount error | As expected | PASS |
| Existing accepted duplicate | Existing duplicate message; no write connection | As expected | PASS |
| Same rejected file twice | Two separate attempt identities | As expected | PASS |
| Bounded Unicode filename audit | Recover spaces, Unicode, delimiters and literal escape tokens; distinguish names; one source event; bound worst-case expansion; reject empty/oversized names without partial evidence | As expected | PASS |
| Rejection audit write failure | Reject, no retained records, safe truthful error | As expected | PASS |
| Rejection audit commit failure | Reject, no retained records, truthful error | As expected | PASS |
| Accepted balance-audit write failure | Roll back all accepted writes, safe error, no calculation | As expected | PASS |
| Accepted storage-audit write failure | Roll back all accepted writes, safe error, no calculation | As expected | PASS |
| Late rejection source-audit failure | Roll back earlier audit writes, truthful error | As expected | PASS |
| Accepted transaction commit failure | No committed writes or calculation invocation | As expected | PASS |

The real `StatementEngine.WriteValidations` method was invoked through reflection with
empty unrelated result collections and substitute database writes. Zero returned Pass;
+0.01, -0.01, +0.99, +1.00, -1.00, +0.001, -0.001 and
+0.0000000000000000000000000001 each returned Fail: **9/9 PASS**. Every case also
verified zero TB tolerance, exact AUD detail, and unchanged SoFP tolerance.

Temporary copies replace only `using System.Data.OleDb;` with the substitute namespace.
No production business logic is rewritten for tests. Service tests substitute the engine
entry point to verify invocation; the separate engine suite compiles and executes the
real validation method. This does not simulate a successful end-to-end SQL calculation.

## Compilation and regression checks

| Check | Result |
|---|---|
| Test executables, installed .NET Framework compiler, warnings as errors | PASS |
| Production import service, parsers, models, crypto, helper and engine compiled against real OleDb and EPPlus | PASS, warnings as errors |
| Full ASP.NET WebForms site precompile, including unrelated modules | PASS |
| Existing `eJET/eJET.aspx.cs:125` unreachable-code warning CS0162 | Present in full-site compile; file unchanged |
| Git whitespace/error check | PASS |
| Real SQL storage, OleDb parameter binding and transaction rollback | NOT RUN — needs authorised isolated SQL database and matching existing schema |
| Real browser upload, redirect, validation tile and audit inspection | NOT RUN — needs authorised isolated IIS/WebForms application and test database |
| SQL-backed calculation, exports and unrelated-module functional regression | NOT RUN — needs isolated application/database fixtures |
| Live database, WARATAH and deployment | NOT RUN — explicitly unauthorised |

Full-site compilation copies working-tree application files selected by the repository's
existing application allowlist into a temporary directory and supplies a build-only
WebForms 4.8 config. It copies no live config, UDL, uploads, SQL or evidence. The sandbox
denied EPPlus loading during full-site compile; the approved outside-sandbox retry passed.
Earlier runner argument/path errors were corrected before the final successful run.

Final temporary build artifacts:
`C:\Users\Steve\AppData\Local\Temp\norm-fr016-f0172f4c013948409f1fee2c934aa3f0`.
Artifacts are local and temporary; the test source provides repeatable evidence.

KISS correction verification: reran the unchanged runner after replacing filename
encoding/chunking with the bounded field above. All 43 tests, production compilation,
full WebForms compilation and Git diff checks passed. Only the filename test was updated;
no tests were added or removed. The existing eJET warning remains. StatementEngine,
parsers, precision checks, transaction handling, other helpers and test infrastructure
were unchanged by this correction.

## Manual UAT in an authorised isolated environment

Prerequisites: separate IIS/ASP.NET .NET Framework 4.8 application, existing supported
EPPlus assembly, isolated matching-schema SQL database and supported OleDb provider,
an approved FY2026 test release with configured ledger 1000, authorised test user and
the normal configuration/mapping/template fixture needed to calculate that release.
Provision these through the approved baseline process; no migration is part of this work.
Do not reuse WARATAH connections or production source data. Use a disposable database
snapshot to inspect record counts before/after each test.

Create a synthetic XLSX first worksheet with:

| Row | Column A | Column B | Column C | Column E | Column F | Column G | Column H |
|---|---|---|---|---|---|---|---|
| 1 | Fiscal Year | 2026 | | | | | |
| 2 | Posting Date From | 01.07.2025 | | | | | |
| 3 | Posting Date To | 30.06.2026 | | | | | |
| 4 | Company Code | | G/L Account | | | | Ending Balance in Company Code Currency |
| 5 | 1000 | | GL/100 | 100.00 | 0.00 | 0.00 | 100.00 |
| 6 | 1000 | | GL/200 | -100.00 | 0.00 | 0.00 | -100.00 |

Column D may contain a synthetic account description. Use test GLs supported by the
isolated configuration when testing successful statement calculations. For each changed
ending amount, change its opening amount to match, so the aggregate test is not stopped
by the existing ERP row-reconciliation rule. Store sub-cent fixture cells as text to
retain the intended decimal input exactly.

1. Upload the balanced fixture with the approved FY2026 release and ERP format. Expect
   accepted import, retained original file, two reporting rows, matching successful audit
   events and a statement run. Verify TB validation Pass and zero tolerance.
2. Repeat with second row -99.99, -100.01, -99.01, -99.00, -99.999 and -100.001.
   Expected net differences: +0.01, -0.01, +0.99, +1.00, +0.001 and -0.001. Each must
   reject with exact difference. Verify unchanged import/row/calculation counts and
   correlated rejection evidence. No rejected original blob should be present.
3. Try balanced +1.001/-1.001: expect storage rejection, balance Pass, storage Fail.
   Try +1.2300/-1.23: expect acceptance and unchanged numeric values in storage.
4. Try +10.01,+20.02,-5.01,-25.02: expect acceptance. Remove an included row's ending
   cell: expect existing format rejection and no new reporting source/calculation.
5. Reupload an accepted original: expect existing duplicate error. Reupload a rejected
   original twice: expect separate attempt IDs and no accepted source.
6. In a disposable test copy only, have the database administrator deny the test
   identity audit INSERT permission. An unbalanced upload must still reject and say
   evidence could not be saved. A balanced upload must not become accepted if its audit
   writes fail. Inspect the database directly; restore permissions afterwards.
7. To verify subsequent validation, use the approved test-break route or an already
   authorised isolated fixture with a non-zero total; do not alter approved real sources.
   Recalculate through the controlled route. Expect DEBITS_EQUAL_CREDITS Fail even at
   +0.01 or +1.00. The existing UI test-break route uses +48250, so sub-dollar cases
   need an authorised fixture/service harness. Existing stored validation rows do not
   change automatically; an existing completed calculation can be reused by fingerprint.
8. Check HTML-safe error rendering, successful redirects, exact AUD detail in the TB
   check, and unchanged unrelated validation tolerances.

## Limitations and dependencies

- Rejection audit retains metadata and validation evidence, not rejected source bytes or
  a complete rejected-row dataset. Precision evidence identifies the first invalid field,
  not every invalid field. No new rejection browser/register is provided.
- Parse errors and pre-existing duplicate/configuration failures retain their existing
  handling; comprehensive FR-010 rejection retention is not claimed. Existing parsers may
  skip non-matching/unconfigured rows; this work does not tighten those format rules.
- A precision failure can coexist with balance Pass; acceptance requires both checks.
- Persisted calculation numeric values remain in thousands with three decimal places.
  A sub-dollar difference can display numerically as zero there, but its result is Fail
  and its exact AUD difference is retained in DetailText. No schema precision changed.
- Historical validation rows and historical accepted sources are not rewritten. Existing
  completed calculation reuse remains unchanged. New/recomputed validation uses exact zero.
- The existing source-commit-then-calculate architecture is preserved. An unrelated later
  calculation failure can still leave a valid accepted source and a failed calculation;
  that differs from a rejected trial-balance attempt, which creates neither.
- Existing deliberate synthetic test-break functionality remains separate from normal
  import acceptance and still produces calculation validation failures.
- WP-00B identity/version/partition contracts, FY2025 source authority and whether its
  ROMAN evidence needs an independent check remain wider work. No scope policy was added.
- Local in-memory rollback assertions do not prove SQL Server transactional behaviour or
  actual persistence. The isolated SQL/browser UAT above remains required.

## Git handover

Tracked source diff: `NORMImportService.cs` +114/-4 lines;
`StatementEngine.cs` +4/-2 lines (118 insertions, 6 deletions total).
The filename-only production correction relative to the reviewed implementation is
10 lines added and 9 deleted; only the corresponding filename test and this report
were also updated.
New task files: this report and three files under `tools/tests/FR016/`.
Existing untracked assessment/requirements folders remain untouched. Nothing was staged
or committed. No PR exists. Review the working-tree diff and isolated UAT evidence before
authorising any later Git integration or deployment activity.
