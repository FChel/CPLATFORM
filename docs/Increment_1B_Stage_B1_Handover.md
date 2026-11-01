# Increment 1B — Stage B1 implementation and isolated verification

30 October 2026. Backend persistence changes implemented; no SQL executed and no real database acceptance claimed. Browser upload workflow remains unchanged and the Increment 1A Defence safeguard remains active on that path.

## Exact changes

| File | Change |
|---|---|
| `sql/NORM_CompanyLedger_Increment1B.sql` (new, unexecuted) | Nullable `VARCHAR(20) SourceCompanyCode` on trial-balance rows and permitted configuration; replace permitted-source uniqueness with `(FinancialYear,EntityCode,SourceCompanyCode,SourceLedger)`. Transactional, rerunnable, validates existing column/constraint definitions. No historical updates. No stream/currency fields. |
| `sql/NORM_FY2026_Isolated_Test_Configuration.sql` (new, unexecuted) | Guarded, repeatable Draft provisioning for FY2026/DEPT/company 1000/ledger 0L. Requires explicit isolated database name, approved FY2025 baseline ID and configuration-review acknowledgement. Copies only active reviewed account-map and statement-line configuration with target year/release. Does not copy any approval, published amount, source figure, journal, manual disclosure or result. |
| `App_Code/NORMImportService.cs` | New backend-only `ImportDefence`: detect contents through existing parser, check approved release/year and exact permitted company/ledger pair, normalise DCOA account keys through existing `ExtractGl`, then reuse `CommitImport`. No mapping-completion gate. Row inserts store company separately; source bytes and row hashes remain unchanged. `LoadLedgers` selects only null-company legacy configuration. `CreateTestBreak` copies company with the original rows. |
| Same service, `CommitImport` | Before new-contract insertion, lock/check existing entity/company/year sources and reject replacement; no append/replacement machinery introduced. Existing exact-file duplicate check remains. Legacy company-only ERP records also block an implicit new-contract replacement. |
| `App_Code/ErpTrialBalanceImporter.cs` | Validate explicit supported currency/unit metadata: AUD and unscaled dollars only. No new configuration mechanism. Existing signatures, amounts, exact controls and legacy parser safeguard remain. |
| `App_Code/NORMMappingManagement.cs`, `RecalculateLatest` | Include company on both sides of the row-copy statement; retain original amounts and hashes. |
| `App_Code/NORMHelper.cs` | Small `SourceCompany` and `SourceLedgerLabel` read helpers distinguish explicit identities from known legacy ERP company codes and unknown ROMAN identities. SAP helper still takes a company, not a ledger. |
| `NORM/NORM_Statements.aspx.cs`, `LoadLineage`; `js/norm.js` | Retrieve/display/search company and ledger separately; display historical source code with actual ledger unknown. SAP links receive company. No import page/workflow changes. |
| `App_Code/NORMReviewPack.cs`, `AddLineage` | Retrieve company and source type, append company export column, display actual/unknown ledger correctly and pass company to SAP links. |
| `tools/tests/FR016/TestSupport.cs` | Extend existing substitutes with permitted pairs, replacement conflict and optional query responses; preserve original defaults. No real connection implementation. |
| `tools/tests/Increment1B/Run.ps1`, `Tests.cs`, `EngineTests.cs`, `DisplayTests.cs` (new) | Targeted service, real calculation-method and real display-helper tests. Temporary compiled artifacts only. |

No further model change was necessary. `StatementEngine.cs` is unchanged from the starting tree, SHA-256 `DCE8BDF982F9B88DADDF82F684158582F789F5406CC4DA508E9DB73707DE3962`. The existing FR-016 balance/storage validation and rejection-audit logic in `CommitImport` were preserved. The service adds company persistence and the replacement guard around those checks.

## Original Defence file

`C:\Development\Transfer\Defence TB - ERP Format FY26.xlsx`

SHA-256 `1C4BAD018973963AA67FF37689EB1ABACA7009661B92681415CEB12CDC06C195`.

| Value | Verified result |
|---|---:|
| Company / actual ledger | 1000 / 0L |
| Financial year / periods | 2026 / 01–12 |
| Account rows passed to persistence | 984 |
| Opening total | 0.00 |
| Signed debit movement total | 4,466,384,209,424.24 |
| Signed credit movement total | -4,466,384,209,424.24 |
| Ending net | 0.00 |
| Positive ending balances / absolute negative ending balances | 268,992,152,391.65 each |

These are parser/service results against substitutes, not stored SQL results. All 984 account inserts retain company and actual ledger, amounts and original parser row hashes. Persisted mapping keys use the existing DCOA suffix convention, while original identifiers remain in retained source bytes and the parser hash. Hashes are source-row hashes, not hashes reconstructed from the normalised database account key. Source-file hash, bytes and periods are verified unchanged.

## Verification performed

| Command/check | Result |
|---|---|
| `& './tools/tests/Increment1B/Run.ps1'` | 14 service scenarios passed; real `Calculate` regression passed; six real-helper assertions passed. |
| `& './tools/tests/Increment1A/Run.ps1'` | 44 tests passed. |
| `& './tools/tests/FR016/Run.ps1'` | 34 service and 9 engine tests passed; production compilation passed; full isolated ASP.NET compilation passed. |
| `git diff --check` | Passed. |

Full-site compilation reports the existing CS0162 warning at `eJET/eJET.aspx.cs:125`. It ran outside the sandbox to allow ASP.NET to load copied assemblies; it did not execute SQL or connect to a database.

Targeted coverage includes separate company/ledger inserts; incorrect/missing permitted pairs; another company using 0L; implicit replacement rejection; explicit USD/thousands rejection and AUD acceptance; exact-zero rejection; balanced fractional-cent rejection through unchanged FR-016 storage validation; audit rollback; unchanged browser safeguard; and synthetic FY2025 ROMAN/ERP transition with null company and historical source codes.

The real calculation method was invoked with the original 984 parsed accounts and one synthetic mapping, using database substitutes. It produced 984 lineage edges, including 983 `UNMAPPED` edges, and the existing `UNMAPPED_DISPOSITION` warning/count of 983. Incomplete mappings did not stop acceptance or the calculation method. This is not an assessment of production FY2026 mapping coverage or complete financial statements.

Display tests use actual `NORMHelper` code on in-memory retrieved-row shapes: new company 1000/ledger 0L remain separate; legacy ERP company 1000 has actual ledger unknown; ROMAN company/ledger are not invented; SAP uses `CompanyCode=1000`, not `0L`.

Actual SQL uniqueness, persistence/retrieval, transactional concurrency and copy behaviour remain unverified because migration execution and database access were excluded. Copy SQL was reviewed and the full application compiled; no real mapping/review export workbook was exercised against SQL. Do not describe substitute tests as real SQL acceptance or proof that live FY2025 data was inspected.

## FY2026 procedure and Stage B2 readiness

1. Separately authorise an isolated database and the approved SQL baseline procedure. Verify schema drift, back up, apply only the reviewed company migration and check historical values. No automatic execution of other SQL files.
2. Review the FY2025 accounting configuration for applicability, then set the explicit parameters in the prepared isolated test script. It creates a Draft under a distinct test version, never relabels FY2025 or copies financial amounts. An existing matching Draft is returned without overwriting edits; a conflicting/approved version fails. Verify its permitted pair and contents after every run.
3. Resolve and record legitimate approval of this initial test release. Existing `NORMMappingManagement.Approve` requires at least one recorded mapping change; a copied Draft alone does not satisfy that condition. No fake mapping change, copied approval or new approval mechanism was introduced. Do not require every source account to be mapped to resolve this administrative approval boundary. If actual reviewed changes exist, use the existing workbook workflow; otherwise initial-baseline approval needs an explicitly authorised procedure.
4. Set the FY2026 workspace using existing year setup only when authorised. Software version, configuration release, reporting year, import ID and calculation run remain distinct. No publication version is created here.
5. In B2, verify real SQL inserts/retrieval, nullable legacy compatibility, multiple companies sharing one ledger, both copy paths, exports, rollback and concurrent duplicate protection. New null-company FY2025 records retain their prior semantics. Test original file bytes and all 984 source accounts after database retrieval.
6. Provide explicit inspection/confirmation before enabling any browser Defence acceptance. Stage B1 intentionally leaves `NORMImportService.Import` / legacy `Parse` blocking the Defence workbook. `ImportDefence` is not called by any browser handler. Calculation still occurs after the accepted-source transaction as in existing code; calculation failure must be reported separately from acceptance.

The prepared test configuration is not agency-approved production configuration. Stream is Departmental for the named isolated DEPT source; NORM amounts are AUD under the approved source contract. No stream/currency database configuration was added. Other companies/years still require their applicable permission and approved release; no multi-file or replacement feature is supplied.

## Deployment and working-tree boundary

Schema must precede the new application queries. Deployment remains subject to the reviewed SQL fingerprint/baseline procedure. Old application rollback cannot safely read new actual-ledger rows as company codes; keep a compatible reader and disable new acceptance. Do not drop captured company data or force the former uniqueness constraint back over valid shared-ledger configuration.

Existing FR-016 and Increment 1A working changes/documents are preserved. Current branch remains `steve/norm-onboarding`; no Git index, commit, push, merge or deployment operation occurred. No migration/configuration script was executed; no database was provisioned or accessed. The new SQL and test files are untracked until a later authorised Git operation.
