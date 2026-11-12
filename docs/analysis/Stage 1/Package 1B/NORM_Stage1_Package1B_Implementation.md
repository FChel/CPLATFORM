# NORM Stage 1 — Package 1B implementation

Living report · 11 November 2026 · Local implementation complete

**Ready for controlled WARATAH verification.** Genuine Defence XLSX, ActewAGL FinStats CSV and Icon Water fixed-width reports pass local acceptance/retention tests. Final verification: **293 passed / 0 failed**, production import/engine compilation and full ASP.NET compilation passed. No deployment, SQL change, accepted shared import, Git commit/push/PR/merge or Package 1C work occurred.

## Approved contract and baseline

The owner approved native ActewAGL CY-only signed balances, June annual coverage, Icon MAR-16 YTD coverage and beginning + debit − credit reconciliation; AUD dollars multiplier 1 defaults; active agency and unambiguous configured population resolution. Fixed-width explicitly satisfies this initial text requirement and is not represented as delimiter-separated.

[Versioned profile specifications](NORM_Stage1_Package1B_Source_Profiles.md) record exact signatures/positions/mappings, approved interpretation, genuine fixture filenames/SHA-256 and independently verified counts/totals. They also provide the proposed BRD v0.7 FR-010/D2 text clarification addendum. Historical DOCX/decision records are preserved; formal approval status of the complete BRD remains pending. The [living assessment](NORM_Stage1_Package1B_Gap_Assessment.md) is updated; its original implementation gaps are explicitly identified as historical baseline findings.

Fetched `origin/uat` at `c3317d83246ceffc09ace1fa9a734c8e8829fd4b`. The existing checkout HEAD `789c99748cb59281e5aca9544a4c980e8a60841d` has the same tree (`git diff origin/uat HEAD` empty), so the implementation uses the latest shared application baseline without disturbing existing user work. No new branch, worktree or commit was required.

## Changes

- `App_Code/NORMTextTrialBalanceImporter.cs`: one small native-text class with separate CSV and positional extraction methods. Strict UTF-8/ASCII and CSV quoting, named header map, preamble/year agreement, CY-only projection; fixed-width fields, repeated metadata/pages, movement arithmetic and all four printed final controls. Unknown/malformed nonblank records reject. Lossless decimal parsing precedes existing storage precision/exact-zero checks. No broad parser framework or per-company parsers.
- `App_Code/NORMImportService.cs`: content dispatch into existing canonical parsed rows, unique text population resolution, native layout/profile preview and version-bound confirmation, acceptance-transaction profile audit. New text inspection, duplicate-scope preview-batch and failed-confirmation rejection evidence use existing audit storage, with safe audit-failure reporting. Existing cancellation/expiry/unchecked acknowledgement exclusions are preserved. Defence extraction code is unchanged.
- `App_Code/NORMImportModels.cs`: source-profile version on parsed import and server preview. `NORM/NORM_Import.aspx.cs`: native profile/version plus AUD-dollar ending-sign information in encoded preview. Existing source/coverage/identity/counts/totals/warnings and confirmation workflow remain in use.
- `tools/tests/Package1B/Run.ps1`, `Tests.cs`: genuine positive import/confirm/download checks and controlled in-memory edge/negative cases. Existing FR016 test substitutes gain optional scalar/write hooks to exercise the real original-download handler. Existing Increment1A/1B/1B2 and FR016 runners include the new production class in explicit compilation lists. No existing test assertion was weakened to pass this change.

The database constrains source-category values to ERP/ROMAN. Native CSV/fixed-width TBs retain the existing **ERP storage category**, while the exact format/version is retained in `IMPORT_SOURCE_PROFILE` audit and shown accurately in preview. This reuses storage without a migration; do not infer XLSX from the category alone. Profile IDs are `DEFENCE-ERP/1`, `ACTEWAGL-FINSTATS/1`, `ICON-WATER-FIXED/1`; unchanged legacy ERP recognition is labelled `LEGACY-ERP/1`. No new schema, source-profile management interface, company/ledger architecture or Year Setup mechanism was introduced.

The Icon report's full named ledger exceeds the 20-character canonical identity field. The approved profile validates that report name, preserves it in original bytes, and resolves the canonical optional ledger/stream through the one permitted company population. It does not truncate the label or invent a numeric ROMAN ledger. Multiple possible bindings reject. Source Icon credit magnitudes are positive; accepted canonical credit movements are their negatives so the existing movement contract remains consistent. Original signs and amounts remain retrievable in original bytes.

## Actual final verification

Final execution 11 November 2026, 21:48–21:49 AEDT. No live database/connection or WARATAH request was used. Tests compile real production parsers/service/page/handler with database/identity substitutes; full ASP.NET compilation uses an isolated working-tree copy and build-only configuration.

| Runner | Passed | Actual scope |
|---|---:|---|
| `tools/tests/Package1B/Run.ps1` | 38 | Six genuine text originals inspect/confirm/download, expected counts/totals/dates/scope, profile audit, ambiguity/conflict, malformed records, decimal precision, default/explicit currency, controls/pages, partial-year rejection, rejection-audit failure, profile-write rollback and replacement. |
| `tools/tests/Increment1A/Run.ps1 -SamplePath <genuine Defence workbook>` | 44 | Genuine Defence 984-row recognition/amount/control tests plus malformed/signature/date/precision negatives. |
| `tools/tests/Increment1B/Run.ps1 -SamplePath <genuine Defence workbook>` | 21 | 14 service checks, 1 real engine lineage check and 6 helper/display assertions. |
| `tools/tests/Increment1B2/Run.ps1 -SamplePath <genuine Defence workbook>` | 63 | Actual import page/service handlers, identity/access, replacement/transaction substitutes, original coverage/current-year regressions. |
| `tools/tests/FR016/Run.ps1` | 43 | 34 service and 9 real-engine exact-zero/storage checks. Production import/engine compilation and full ASP.NET compilation also passed. |
| `tools/tests/Package1AStep3/Run.ps1` | 77 | 62 setup/page/handler, 3 engine-context, 12 production-JavaScript checks. |
| `tools/tests/Package1AStep3/RunViewState.ps1` | 7 | Real isolated ASP.NET session/MAC pipeline regressions. |
| **Total** | **293** | **Zero failing checks.** |

Full-site compilation reported only the existing unrelated `eJET/eJET.aspx.cs:125` CS0162 unreachable-code warning. No new compile error/warning was introduced. The supplied genuine files remained byte-identical to the profile manifest. New positive tests use FY2018, FY2019, FY2020 CSV and FY2016 Icon originals; FY2016 is designated current only in isolated substituted configuration. Defence remains FY2026. No dates/source records were modified to make positive fixtures eligible.

CSV genuine account counts: 379/386/409, CY net exactly 0.00 each. Icon: company 86 = 18, company 87 = 17, company 80 = 412 detail rows; every row reconciles and four printed controls match, including nine-page company 80. CSV's excluded prior-year data stays in original bytes; it creates no second population. The actual 0.01 excluded FY2016/17 PY imbalance is preserved rather than rounded away. Native CSV/file-column reorder and quote/BOM cases are controlled edge tests, not replacements for genuine positives.

Original-byte retention and actual `NORMSourceFileHandler` hash round trips were exercised for each of the six genuine text positives with substituted SQL reads/writes. These demonstrate the production handler's byte path, not live SQL readback or Windows-authenticated browser acceptance. Acceptance/profile-write failure rolled back rows; a separate failure audit may remain, as required by D5. Rejected source bytes are not retained. Already-accepted historical sources are not backfilled with a newly inferred profile version.

See [immutable final verification summary](NORM_Stage1_Package1B_Local_Verification_20261111_214911.md) and [PowerShell transcript](Local_Verification_20261111_214835.txt). The transcript records commands/build confirmations/artifact paths but this host does not capture every native executable stdout line; the summary records the verified native test results above.

## Remaining verification and stopping point

Local stopping condition is met. No unresolved accounting interpretation or source ambiguity blocks the approved initial profiles. Scope is deliberately bounded: ActewAGL June annual FinStats only and the supplied Icon MAR-16 YTD layout; unsupported other periods/layouts reject until documented/tested profile revisions exist. No other historical workbook families were added.

Controlled WARATAH verification remains **not performed**: live SQL transaction/storage/readback/locking, Windows-authenticated import preview/rejection/confirmation/download, and operator-readable profile/audit evidence. Stage 1 integrated security/assurance remains Package 1D; mixed populations/mixed-year orchestration remains Package 1C. Offline substitutes do not close those gates. Historical agency fixtures require appropriate authorised isolated verification configuration; do not change shared WARATAH's FY2026 designation or admit historical test populations merely to make the Icon positive eligible.

If publishing is separately authorised, prepare a reviewed candidate excluding pre-existing user changes, verify unchanged SQL digest/no migration, then follow the shared `uat` PR/validation/merge/deployment process and report the actual run outcome. This report does not authorise or start it. **Stop here: ready for controlled verification; no deployment or Package 1C begun.**
