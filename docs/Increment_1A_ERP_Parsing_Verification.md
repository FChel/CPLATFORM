# Increment 1A — Defence ERP parsing and isolated verification

Date: 30 October 2026. Implemented and verified locally; no database import or browser acceptance is claimed.

## Result from the original source

Source: `C:\Development\Transfer\Defence TB - ERP Format FY26.xlsx` (86,156 bytes).
SHA-256: `1C4BAD018973963AA67FF37689EB1ABACA7009661B92681415CEB12CDC06C195`.

| Parsed value | Result |
|---|---:|
| Company | 1000 |
| Actual ledger | 0L |
| Financial year | 2026 |
| Posting dates | 1 July 2025–30 June 2026 |
| Reporting periods | 01–12 |
| Account detail rows | 984 (worksheet rows 28–1011) |
| Sum of opening balances | 0.00 |
| Sum of debit movements | 4,466,384,209,424.24 |
| Sum of signed credit movements | -4,466,384,209,424.24 |
| Sum of ending balances | 0.00 |
| Positive ending balances | 268,992,152,391.65 |
| Absolute negative ending balances | 268,992,152,391.65 |

These are source company-currency amounts, not thousands or statement presentation values. Currency authorisation remains an Increment 1B prerequisite; this workbook's column headings alone do not establish an ISO currency.

Company subtotal row 1012 and grand-total row 1013 are excluded from account records. Each independently matches the sum of detail in all four monetary columns. The tests also independently traverse the original 984 detail rows and both control rows, compare every parsed account, description and amount, and check the totals above. Neither control is used to construct or adjust balances.

## Production changes

| File | Change |
|---|---|
| `App_Code/NORMImportModels.cs` | Add the approved `NORMTrialBalanceRow.SourceCompanyCode` field. No other model changes. |
| `App_Code/ErpTrialBalanceImporter.cs` | Add `ParseForInspection`, explicit signature checks, `ParseDefence`, four-column control reconciliation and small private helpers. Add an early Defence-layout safeguard to existing `Parse`. Existing legacy parsing arithmetic, filtering, hashes and return semantics remain unchanged. |

`ParseForInspection(byte[], IList<string> allowedCompanyCodes, int expectedFinancialYear)` accepts content, not a filename or selected parser. It examines workbook worksheets for one supported data header. Defence recognition requires the exact query `2CCFITRIALBALQ0001 / Trial Balance`, provider `2CIFIGLBALCUBE / G/L Account Balance - Cube`, observed `Funds of Flow Ledger Fiscal Year` label and all eight expected headings. Parsing requires consistent year, company, Ledger/Source Ledger and posting-date metadata. Conflicting metadata, unsupported layouts and multiple matching data sheets fail. A malformed Defence-marked workbook cannot fall back to a legacy sheet.

Legacy inspection recognises the previously supported header signature with fiscal-year and date metadata, then calls existing `Parse`. It returns an explicit warning about the historical company-in-SourceLedger contract. No original legacy workbook is available in the inspected sample collection; compatibility evidence is synthetic legacy fixtures and the existing FR-016 suite, not a claim of real legacy-file acceptance.

Defence parsing retains company `1000` and actual ledger `0L` separately, the complete `DCOA/...` identifier, description text, source row and signed monetary values. It requires one configured company per file in this increment, complete detail and both totals, unique account identifiers, exact row arithmetic, exact control reconciliation and an exactly zero ending-balance sum. Unexpected rows or another company fail rather than being silently filtered. Partial-year metadata remains partial (the March variation returns period 09).

Precision: reuse the existing `TryDecimal` conversion from EPPlus cell values. No new rounding, tolerance, balancing row or adjustment is introduced. Numeric XLSX cells pass through EPPlus's `double` representation and existing .NET `Convert.ToDecimal`; this is not a claim of byte-exact preservation of XML floating-point lexical residues. Every converted amount in the original sample is cent-representable. Synthetic string-valued fractional cents are retained exactly in parsed rows and hashes; parse success does not establish lossless database storage eligibility.

## Compatibility safeguard and consumer review

Reviewed existing `SourceLedger` consumers before changing behaviour:

- `NORMImportService.Import` and `ImportFy2025Transition` call `ErpTrialBalanceImporter.Parse`; `CommitImport` writes `row.SourceLedger` directly to the existing database column. `CreateTestBreak` copies that column.
- `NORMMappingManagement.RecalculateLatest` copies stored trial-balance rows.
- `StatementEngine.Run` selects stored `SourceLedger` with account values.
- `NORM/NORM_Statements.aspx.cs` includes it in source drill-through; `NORMReviewPack` includes it in exported source lineage.
- `RomanTrialBalanceImporter` and existing isolated FR-016 substitutes retain their previous contracts.

The persistence-facing `Parse` now throws an explicit **inspection only / Increment 1B** error when Defence metadata is present, including beyond worksheet one. It never returns the corrected Defence row semantics to those callers. The new inspection entry point is not wired to any browser, database or calculation routine. Existing service code and SQL remain unchanged.

The safeguard is tested both directly and through production `NORMImportService.Import` compiled with the existing FR-016 database/calculation substitutes: no connection-open call, committed write or calculation occurs. This is an isolated service-boundary test, not a real database test.

## Tests and compilation

New files only:

- `tools/tests/Increment1A/Tests.cs`: original sample and synthetic variations.
- `tools/tests/Increment1A/Run.ps1`: small compiler/runner reusing deployed EPPlus and existing FR-016 substitutes. Artifacts go to a unique temporary directory; no real connection implementation is loaded.
- This handover document.

Commands and final results:

| Command/check | Result |
|---|---|
| `& './tools/tests/Increment1A/Run.ps1'` | **44 tests passed** |
| `& './tools/tests/FR016/Run.ps1'` — service suite | **34 tests passed** |
| Same — engine suite | **9 tests passed** |
| Production import/engine compilation against real helper/OLE DB APIs | **Passed** |
| Full ASP.NET compilation of isolated working-tree copy | **Passed**; existing CS0162 warning at `eJET/eJET.aspx.cs:125` |
| `git diff --check` | **Passed** |

Coverage includes original-file recognition and all detail amounts; legacy compatibility; company/ledger separation; exact zero; control mismatch in each monetary column for each total; wrong/missing/conflicting year, dates, ledger, account, description and amounts; unauthorised company; duplicate accounts; missing totals; ambiguous worksheets; worksheet order; malformed Defence plus legacy fallback; unsupported content; partial-year periods; sub-cent preservation; and the persistence safeguard.

The first sandbox full-site compilation failed to load the copied EPPlus assembly with access denied. The authorised rerun outside the sandbox passed; the final rerun after the final parser edits also passed.

FR-016 implementation files are byte-for-byte unchanged from the start of this increment:

- `App_Code/NORMImportService.cs`: SHA-256 `747CD95EF16D39129F117BF2758C8FF6623E4FE880C76DE074698464E67D6258`.
- `App_Code/StatementEngine.cs`: SHA-256 `DCE8BDF982F9B88DADDF82F684158582F789F5406CC4DA508E9DB73707DE3962`.

## Working tree and Increment 1B

Branch remains `steve/norm-onboarding`. Increment 1A adds 199 lines to the ERP parser and one model field, plus the three new files above. The already modified service/engine files and pre-existing untracked FR-016 documents, analysis, requirements and tests are preserved. No staging, commit, fetch, push, merge, deployment, schema/configuration change, live connection or browser workflow change occurred.

Minimum Increment 1B requirements:

1. An authorised isolated environment and separately approved FY2026 agency/configuration release, with company, ledger, stream, currency and expected reporting coverage.
2. An approved persistence contract capable of retaining company and actual ledger independently, the complete source identifier and accurate source period/provenance. Review downstream account-key expectations before removing the safeguard.
3. Inspection/validation preview and explicit confirmation, with evidence retention and server revalidation; only then enable database acceptance. Keep FR-016 exact-zero and lossless decimal gates.
4. Test actual persistence and source retrieval separately from parsing, then calculation separately from successful acceptance. Do not treat this increment as a database import or financial-statement publication.

For subsequent batch work, follow the latest agreed rules: company/year populations are not assembled from separate uploaded files; all files conflicting on Agency + Company + Year are rejected while unrelated valid populations remain eligible. Prior accepted data requires explicit confirmed replacement rather than addition. None of that is implemented here.
