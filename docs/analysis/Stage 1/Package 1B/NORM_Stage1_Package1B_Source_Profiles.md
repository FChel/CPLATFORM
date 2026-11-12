# NORM Package 1B — approved source profiles

Maintained specification · 11 November 2026 · Profile version 1

## Authority and BRD v0.7 clarification

The owner approved these native profiles and local implementation in “NORM Stage 1, Package 1B — Approved Source Profiles and Implementation”. No further decision on their monetary, coverage or text-format interpretation is pending.

For proposed BRD v0.7 FR-010 and the D2 format wording, read **“XLSX, CSV and documented text formats, including fixed-width reports”** in place of “XLSX, CSV and documented delimited-text layouts/format”. This approved Package 1B clarification includes the Icon Water positional report; it does not assert that fixed-width is delimiter-separated or commission other agency formats. This maintained addendum clarifies the proposed baseline without overwriting the historical DOCX, roadmap or decision record. The complete BRD v0.7 remains formally proposed/pending whole-document agreement. D1–D6 otherwise remain authoritative.

## Common contract

- Agency comes from active approved NORM configuration. The source must match its configured year. Use existing authorised company/optional ledger/stream population configuration; require one unambiguous match. Configuration must not contradict an explicit source company. No manual ERP/parser selector.
- Currency defaults to AUD and monetary units to dollars, multiplier 1, unless the source explicitly identifies otherwise. These initial profiles support AUD dollars only: explicit incompatible currency, scale or unrecognised metadata is rejected, not silently normalised. Genuine text reports do not require newly invented currency/unit fields.
- Preserve original bytes/hash, source description, account identifiers and ending signs. Store canonical credit movements as signed negative amounts; keep the original positive Icon credit magnitude in retained bytes. Do not aggregate or import comparative columns as additional populations.
- Strict UTF-8 (optional BOM)/ASCII for text. No binary/NUL, malformed quotes, unknown nonblank records or silent row filters. CSV permits quoted delimiters, doubled quotes and embedded line breaks. Fixed-width uses actual column positions, not whitespace splitting. Blank lines/form feeds used by these reports are non-data.
- Source and extracted row counts must agree after documented metadata/control exclusions. Reject missing required detail, duplicate accounts at the account grain, malformed amounts, decimal overflow/rounding, storage precision loss and nonzero net. Keep the existing exact-zero and storage checks, coverage, preview, confirmation, scoped replacement and history mechanisms.
- Exact dates must remain within the FY. A non-current year requires full-year coverage; only the explicitly configured current year permits partial coverage. The isolated Icon positive tests designate FY2016 current; WARATAH FY2026 is unchanged.
- Persist profile ID/version and source hash in `IMPORT_SOURCE_PROFILE` audit in the acceptance transaction. Bind version into the preview identity fingerprint and repeat recognition on confirmation. Existing schema permits only ERP/ROMAN source categories, so native TB files use existing `ERP` storage category; `Layout`/`ProfileVersion` in preview and the profile audit identify the precise native format. This is a storage compatibility code, not a claim that CSV/fixed-width is XLSX. No SQL change is required.
- Inspection/failed-acceptance rejection evidence for new text paths uses `IMPORT_REJECTED_SOURCE`, unique attempt ID, authenticated actor, database UTC default, available filename/hash and source company, active agency/configuration FY, reason and outcome. Failed confirmation additionally records available profile, resolved company/ledger/stream and coverage. No accepted source/calculation on rejection; if audit storage fails, fail safely with a specific audit-unavailable message. Routine expiry/cancellation/unchecked acknowledgement adds no event. Defence legacy rejection coverage remains outside this bounded text change.

## DEFENCE-ERP/1 — XLSX

Existing `ErpTrialBalanceImporter.ParseForInspection`/`ParseDefence` remains unchanged. ZIP/XLSX content is recognised by actual worksheet signatures: Query Name `2CCFITRIALBALQ0001 / Trial Balance`, InfoProvider Name `2CIFIGLBALCUBE / G/L Account Balance - Cube`, fiscal year, company and posting dates, eight-column report header. Company/optional Ledger or Source Ledger is source evidence; unique configured stream may fill absent stream.

Columns A–H: company code/name, DCOA account/description, opening, debit movement, signed credit movement, ending. Each detail must reconcile opening + debit + signed credit = ending exactly; unique accounts and complete details are required. All four extracted sums must match company subtotal and grand total; ending must be exactly zero. Original DCOA identifiers remain in retained source; existing extraction normalises the GL key for accepted rows as before. Currency/units rules remain the existing validated Defence contract. Dates 1 July 2025–30 June 2026 establish FY2026 full year. Null ledger remains permitted where configured.

The existing legacy ERP fallback is unchanged and labelled `LEGACY-ERP/1` for provenance; it is not an additional new acceptance-profile family or an expansion of its controls.

## ACTEWAGL-FINSTATS/1 — CSV

Recognition is content-based: quoted `Company : ` first preamble label; exactly five two-field metadata records, blank spacer, then the complete four-column header. Company metadata matches `<digits> - ActewAGL Distribution Partnership FinStats`; source company is the numeric prefix (150 in genuine fixtures).

Preamble: Company, CY financial year, CY month, PY financial year, PY month. Financial-year strings are `YYYY/YY`, adjacent CY/PY with internally consistent suffixes. Both months must be Jun. Header names are `Account`, `Account Desc`, `Jun <CY year>` and `Jun <PY year>`, each exactly once; the named map permits their order to change when all details follow the same order. No new generic profile configuration mechanism is introduced.

Import only the CY signed closing balance (positive debit, negative credit). Validate the prior token's numeric syntax without imposing exact-zero on excluded PY or persisting it as a population. FY2020 CY means 1 July 2019–30 June 2020; the same approved July–June annual rule gives FY2018/FY2019 coverage for earlier fixtures. Metadata and header years must agree with each other and the selected release year. All nonblank detail records have exactly four fields, a numeric unique account, nonempty supplied description and losslessly parsed decimal tokens.

No currency/scale or independent control row exists in the genuine files; default AUD dollars multiplier 1. Do not manufacture a source control. Independently verified extracted counts and positive/negative totals below are **calculated** controls; net zero is mandatory for CY. The 2017/18 file's excluded 2016/17 PY nets 0.01: preserve that original evidence and do not round it away. Different original-year versions need not have equal gross totals just because nets match.

Ledger/stream are absent; resolve uniquely using company 150 in active configuration, including a configured non-null ledger where uniquely applicable. Missing ledger is not automatically “not applicable”; two possible company ledger/stream bindings reject as ambiguous. No SAP identifier pattern is imposed.

## ICON-WATER-FIXED/1 — positional text

Recognise `Icon Water Limited Ledger` / `Trial Balance - Total Currency` heading plus strict native report structure. This initial version documents the supplied MAR-16 YTD report: `Period: MAR-16`, sequential Page n of N, Currency AUD, Balance Type Year to Date, equal single-company range, ledger `Icon Water Limited Ledger`, consistent company number/name, native field header and dashed positional rule. Other periods/headers require a separately documented profile revision; do not guess dates.

Zero-based field offsets/widths: account 0/15, description 16/31, beginning 48/20, debit 69/20, credit 90/20, ending 111/20; intervening positions are spaces. Account has five digits. Retain the supplied description text, including report truncation, without reconstructing missing characters. Commas are numeric grouping and leading minus carries sign. Do not split on spaces or treat the report as delimited.

Each detail satisfies beginning + positive debits − positive credits = signed ending. Reject negative debit/credit magnitude, duplicate account, malformed/misaligned or unknown record. Canonical row credit movement is negative source credit; canonical opening/ending are unchanged. Detail sums for all four source numeric columns must match the final printed controls exactly. Require controls after all numbered pages; repeat headers/metadata must agree and never enter detail. Footer is excluded from source detail count; data after footer rejects.

Approved YTD interpretation: FY2016, 1 July 2015–31 March 2016, periods 01–09. AUD dollars, multiplier 1. The full printed ledger name exceeds the existing 20-character canonical ledger identity field; validate/preserve its report value and bind the canonical optional ledger and stream through the unique approved company population. No silent truncation or fabricated numeric ROMAN ledger. Multiple configured ledger/stream candidates reject until configuration is unambiguous. This uses existing configuration, not a new alias-management interface.

## Genuine fixture manifest and independent expectations

All paths below are under `docs/Trial Balance Examples/`. Originals were read only. Amounts below are AUD dollars under owner-approved units. Positive and negative ending totals are equal magnitudes with ending net 0.00 for every accepted population.

| File | SHA-256 | FY / detail count | Positive ending (absolute negative equal) |
|---|---|---|---:|
| `Defence TB - ERP Format FY26.xlsx` | `1c4bad018973963aa67ff37689eb1abaca7009661b92681415ceb12cdc06c195` | 2026 / 984 | 268,992,152,391.65 |
| `AAG_TrialBalanceFinStats150.csv` | `9d3a113b3c0ef344c7d3c419f5c8d6e241c9543b2731f4f9d53098cfae5ad631` | 2020 / 409 | 11,925,983,562.62 |
| `AAG_TrialBalanceFinStats150_201819.csv` | `e0723bd91976302d38f0d0c79c66acf5f640acead0d2f4eb4b7adfd64384e3b8` | 2019 / 386 | 11,270,618,549.82 |
| `AAG_TrialBalanceFinStats150_201718.csv` | `1a3920bc0290f323088b42f7913cacddca815d7f7a1b7b1fbbb5e3697126170b` | 2018 / 379 | 10,626,766,953.52 |
| `coy 86.txt` | `868589229f3a0a58851cccce97eedfcff4210590c6278404d1f5bf74812ca4b7` | 2016 / 18 | 382,097,954.00 |
| `coy 87.txt` | `0b92408349be4c93b451fac5f88e6f31b09c3983e7b13e361571c0d5b0091394` | 2016 / 17 | 1,421,192,159.48 |
| `coy 80.txt` | `019f3f14975ff8a1f672b59fd8bed9fc95ab0cb4b36c70031db36ef0dae61f7f` | 2016 / 412 | 17,634,789,869.35 |

Defence independent detail/control expected opening = 0.00, debit movement = 4,466,384,209,424.24, signed credit = -4,466,384,209,424.24, ending = 0.00. Icon source beginning/ending controls = 0.00; debit/positive credit totals: company 86 = 161,053,185.12, company 87 = 359,051,659.79, company 80 = 5,128,014,912.26. Company 80 has nine pages; 86 and 87 one each. The earlier read-only assessment calculated these using PowerShell invariant decimal traversal and compared Icon's actual footer. Defence existing tests independently traverse worksheet cells. Focused tests assert these native counts/totals, dates, scope, hashes, provenance and byte retrieval with production parsers/service and substituted storage/identity. Positive tests use original files; controlled in-memory modifications are edge/negative evidence only.
