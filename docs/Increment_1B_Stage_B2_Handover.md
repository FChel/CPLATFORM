# Increment 1B Stage B2 — single-file browser inspection and confirmation

30 October 2026. Implemented and verified with the original workbook, synthetic fixtures, production page handlers and database substitutes. No real SQL or hosted-browser acceptance is claimed.

## Files and methods changed in B2

| File | Change |
|---|---|
| `NORM/NORM_Import.aspx` | Remove format selector. Add Inspect, preview/validation summary, explicit Confirm, Cancel and separate source/calculation results. Enable normal read/write session and ViewState MAC/event validation. Disable ViewState on preview/result/error panels. No multi-file control. |
| `NORM/NORM_Import.aspx.cs` | `OnInit` binds ViewState to session/current user. `Page_Load` reconstructs preview from session on postback. `ImportButton_Click` inspects only; `ConfirmButton_Click` validates token and selected release, rejects a newly selected file, consumes pending state and calls confirmation; `CancelButton_Click` removes it. Small private display/cleanup/error methods HTML-encode source-derived values. Existing release binding retained. |
| `App_Code/NORMImportModels.cs` | Add serializable `NORMImportPreview` for one server-session pending upload; add `NORMImportOutcome.CalculationError`. No database model change. |
| `App_Code/NORMImportService.cs` | Add `Inspect`, `Confirm`, `PreviewConfiguration`, `PreviewQuery`. `CommitImport` accepts an optional confirmation fingerprint and checks it inside the existing transaction. Browser confirmation also checks retained legacy source populations before insertion. Construct outcome before calculation so a post-acceptance calculation exception is returned as calculation failure, not source rejection. Existing direct backend methods remain available. |
| `App_Code/RomanTrialBalanceImporter.cs` | Add `Recognises`: explicit reporting-period header plus pipe-framed detail signature. `Parse` and sign conventions unchanged. |
| `App_Code/ErpTrialBalanceImporter.cs` | Update the legacy-route safeguard message to direct users towards inspection/confirmation. Detection, financial parsing and reconciliation logic unchanged. |
| `tools/tests/FR016/TestSupport.cs` | Extend existing substitutes with calculation failure and preparer-identity/access switches; defaults preserve prior suite behaviour. |
| `tools/tests/Increment1B2/Run.ps1`, `Tests.cs`, `PageHarness.cs` | New targeted runner/tests and minimal hosting/control substitutes for production page handlers. |

Other pre-existing working-tree changes belong to earlier increments and are preserved. No configuration, SQL, financial calculations, migration scripts or deployment settings were changed in B2.

## Preview and confirmation

1. The preparer selects an approved release and one file, then chooses **Inspect file**. There is no ERP/ROMAN selection or extension-based parser choice.
2. XLSX container bytes enter existing ERP signature detection. Text must match the explicit ROMAN signature. A workbook parse failure never falls back to ROMAN. Existing unsupported/ambiguous workbook checks remain.
3. The service reads configuration/permissions/mappings, parses, validates and returns a preview. It writes no accepted reporting data and invokes no calculation. Defence detail/company/grand-total controls use the existing parser. Preview checks exact-zero ending balances and existing lossless decimal eligibility.
4. One pending preview, including a clone of the source bytes, resides in normal ASP.NET session state for the current user. It expires after 15 minutes; normal session expiry/recycling can invalidate it earlier. A new inspection replaces the previous pending upload; cancellation removes it.
5. The page displays entity, approved release/version, source layout, FY, company, actual/unknown ledger, periods, row count, debit/credit ending-balance totals, net, validation results and unmapped account count. Partial periods remain labelled as periods. Unmapped accounts generate a warning and do not disable valid acceptance.
6. Confirmation uses the session object, owner and opaque token. The only pending-state value in protected ViewState is the token; preview panels do not store their financial summary in ViewState. Submitted financial/identity fields are never read. A changed selected release or newly posted file requires inspection again.
7. The pending session entry is removed before acceptance. `Confirm` also consumes the preview object, verifies expiry/owner/file hash, reparses the same bytes, and compares source identity/layout and current configuration. ASP.NET's normal exclusive session lock serialises same-session requests; replay cannot reuse the pending entry.
8. Configuration fingerprinting covers the approved release record, active permitted sources, active mappings and statement lines. It detects changed mappings even if the release hash was not refreshed. The fingerprint is read again under `HOLDLOCK` inside the acceptance transaction before writes.
9. Existing company/year data blocks acceptance until replacement exists. The locked check also covers retained legacy ERP company codes and conservatively prevents overlap with matching legacy ROMAN source codes; it does not invent a ROMAN company/ledger identity.
10. Successful acceptance retains the existing file bytes, hashes, rows and audit/control events. Calculation follows the accepted-source transaction. Calculation failure returns the accepted import reference and a separate failure message, with no invitation to re-upload. Existing engine failure recording remains unchanged.

No preview table, parser framework, configuration mechanism, replacement operation or additional financial calculation was introduced. In-memory preview state is short-lived, not a durable source acceptance or general rejected-attempt archive.

## Original Defence preview

Source: `C:\Development\Transfer\Defence TB - ERP Format FY26.xlsx`.

| Field | Result |
|---|---:|
| Entity in test configuration | DEPT |
| Company / actual ledger | 1000 / 0L |
| Financial year / periods | 2026 / 01–12 |
| Account rows | 984 |
| Debit ending balances | 268,992,152,391.65 AUD |
| Absolute credit ending balances | 268,992,152,391.65 AUD |
| Net | 0.00 AUD |

These debit/credit figures are the FR-016 signed ending-balance population, not gross movements. The existing parser independently reconciles gross movements to both report totals. The fixture with one synthetic mapping reports 983 unmapped accounts and remains eligible for acceptance. Production mapping coverage was not assessed.

## Final verification

| Check | Result |
|---|---|
| `& './tools/tests/Increment1B2/Run.ps1'` | **27 scenarios passed** |
| `& './tools/tests/Increment1A/Run.ps1'` | **44 passed** |
| `& './tools/tests/Increment1B/Run.ps1'` | **14 service scenarios**, real calculation-method regression and six display/helper assertions passed |
| `& './tools/tests/FR016/Run.ps1'` | **34 service + 9 engine tests passed** |
| Production import/engine compilation | Passed |
| Full isolated ASP.NET application compilation | Passed; existing CS0162 warning at `eJET/eJET.aspx.cs:125` |
| `git diff --check` | Passed |

B2 coverage includes original 984-row preview; zero acceptance before confirmation; cancel; production confirm handler; encoded preview values; changed release, stale token, forged summary fields and revoked preparer access; imbalance, unsupported contents, owner/expiry checks; cloned/tampered bytes and identity; fractional cents; changed approval, mapping, permission and in-transaction configuration; replacement rejection; replay; accepted-source/calculation-failure distinction; and legacy ERP/ROMAN detection and acceptance.

The page harness uses real ASP.NET session objects and the production handlers, but supplies controls and identity/database boundaries. It does not execute a hosted HTTP lifecycle or real browser multipart upload, ViewState MAC round trip, IIS authentication or SQL locking. Full ASP.NET compilation validates production markup/code binding separately.

FY2025 remains compatible with the existing service contract: the controlled `ImportFy2025Transition` method is unchanged and its synthetic regression passes. Single-file FY2025 ERP/ROMAN inspection explicitly reports the existing pair requirement and cannot bypass it. B2 does not add a two-file browser workflow.

## Remaining running-environment prerequisites

- An authorised isolated SQL database and WebForms host; the previously inspected machine lacks SQL Server/LocalDB and IIS/IIS Express.
- Review/apply the existing baseline and company/ledger migration through the approved procedure. No SQL was executed here.
- A legitimately approved FY2026 DEPT test release with company 1000/ledger 0L permission. The prepared provisioning script still creates a Draft; initial approval must be resolved without fabricating a mapping change or requiring complete mapping.
- A local-only connection, working SQL OLE DB provider, Windows-authenticated preparer identity and enabled normal ASP.NET session facilities. No persistent/shared preview service is required.
- Real SQL verification of storage/retrieval, audit/file retention, rollback and concurrent duplicate/configuration changes.
- A hosted browser test of upload → inspect → cancel/confirm, expiration/session loss, stale tabs, changed file/release, double click/refresh, unmapped review and separate calculation failure reporting.

No install, database provisioning, live connection, SQL execution, commit, push, merge or deployment occurred. Work stops at implementation and isolated verification.

Unchanged calculation file SHA-256: `DCE8BDF982F9B88DADDF82F684158582F789F5406CC4DA508E9DB73707DE3962`.
Unchanged company migration SHA-256: `DBF4DDCD6EF48DE8B886B8D5DDC66025E6E53978246898EFC024FCCE76C200E6`.
Unchanged FY2026 Draft script SHA-256: `3D203B8B27954899C5413451375A9658B45AF9364E873649D1B1A16881B495AF`.
