# Package 1C — bounded UAT correction

Living execution record, 13 November 2026. Authorised scope: implement, test and deploy the three corrections in the Project Owner's bounded correction request. This supersedes the earlier assessment-only pause for these corrections; CM-01 comparative implementation, compact preview redesign and Package1D remain excluded.

## Exact runtime change

Baseline: refreshed `origin/uat` = `f4c6eff1e6a8d82a19cad07073561dade12d357b`. Branch `codex/norm-package1c-uat-correction`. Unrelated prior working changes are preserved.

| File | Correction |
|---|---|
| `App_Code/NORMImportService.cs` | Remove the blanket year2025 error from `BuildPopulationPreview`. All normal approved agency/release/population, coverage, exact-zero and storage checks remain. No UAT-agency exception. Historical `Import`/`ImportFy2025Transition` paths are unchanged; governed inspection still excludes ROMAN/new transition pairing and append. |
| `css/norm-import.css` | Import-review confirmation's native disabled/disabled-hover styles use grey background/text, no shadow and `not-allowed` cursor. Existing validation/explanation remains; no preview redesign. |
| `NORM/NORM_Import.aspx` | Synchronise the history URL's release parameter from the actual dropdown value on initialisation, selection change and navigation click. Preserve existing stream/path and server-side validation; no form postback or automatic newest-release selection. |

No other runtime module, source-history authorisation handler, calculation/mapping code, database schema/configuration, user account/permission, authentication setting or deployment infrastructure changes are required.

## Focused verification and regressions

`tools/tests/Package1CUatCorrection/{Run.ps1,Tests.cs,Navigation.cjs}` reuses existing Package1C harnesses. `Package1CStep1/Tests.cs` adds only a conditional runner hook. `Increment1B2/Tests.cs` replaces the obsolete broad-year expected rejection with ordinary full-year FY2025 eligibility while retaining governed ROMAN rejection. The actual prepared controlled workbooks and manifest are retained unchanged as reproducibility/UAT evidence.

| Executed command | Actual result |
|---|---|
| `& ./tools/tests/Package1CUatCorrection/Run.ps1` | 8 focused correction cases passed; existing Steps1–4 141 passed; combined149, zero failures. |
| `node tools/tests/Package1CUatCorrection/Navigation.cjs` | 5 passed: initial selection/stream, change without postback, latest value at click, no invented newest release, disabled-hover/cursor/shadow rules. Executes actual page script with a DOM substitute; not an authenticated browser UAT claim. |
| `& ./tools/tests/Increment1B2/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 63 passed. |
| `& ./tools/tests/Package1BSourceDownload/Run.ps1` | 37 passed. |
| `& ./tools/tests/Package1B/Run.ps1` | 38 passed. |
| `& ./tools/tests/Increment1A/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 44 passed. |
| `& ./tools/tests/Increment1B/Run.ps1 -SamplePath './docs/Trial Balance Examples/Defence TB - ERP Format FY26.xlsx'` | 21 passed (14 service, one real-helper calculation, six helper/display assertions). |
| `& ./tools/tests/FR016/Run.ps1` | 43 passed; real-helper/OleDb production compilation and full ASP.NET site compilation passed, existing eJET line125 CS0162 warning only. |

Total **400 passing checks**, including 13 new correction checks. No full-suite counts are double-counted. Focused artifact `norm-package1c-uat-correction-6720cef63ba4459e955275a60654767a`; full build `norm-fr016-0b32091e845e455aa4ff5396c0cdf53f` under runner temporary directories.

The actual file02 automatically resolves primary8→release7 with additional selections empty, FY2025 full coverage, company1000/NULL/Departmental, two rows +50/-50, profile LEGACY-ERP/1 and original SHA-256. Actual file07+file02 has exactly one invalid population (two company2000 FY2026 rows, +0.01) and six valid rows withheld. The actual four-file/five-population batch has1,976 rows, is confirmable and commits atomically under database substitutes; retained originals remain exact and historical seed inventories unchanged despite post-commit calculation failure. Partial FY2025, unsupported ROMAN, transition-period partial ERP, missing/ambiguous historical approval and unauthorised history context still reject. Forced page/service confirmation of invalid data opens no storage transaction.

## Deployment and preservation

Read-only preflight SSM `b2613803-342f-498c-8dd3-80396f320f2b` succeeded. Receipt `WARATAH_Package1C_Correction_Preflight_20261113.json`: expected baseline commit,258/258 managed hashes,10 imports/11 originals, audit high-water111, health passed and all source/configuration/audit fingerprints captured. No test imports, configuration or accounting mappings were changed for this correction.

Pinned candidate package compilation, deployment safety/request validation, PR/merge/workflow and postdeployment preservation results will be appended after execution. Publishing uses the existing PR→uat→WARATAH validation→Deploy WARATAH process. No bypass of the registered SQL baseline or alternate deployment/authentication mechanism is permitted.

## Owner browser resume point

After deployment/preservation succeeds: **repeat Step1 briefly** to verify Import release8→Source History carries the selected context; then **resume from Step2**, selecting primary8 with additional configurations empty, inspecting actual07+02. Expected: only the FY2026 company2000/Administered +0.01 population invalid; FY2025 file02 binds automatically to7 and validates. Confirmation should be visibly disabled. Proceed to Step3 and the existing positive Step4/5 sequence only after Step2 matches and its SQL checkpoint is reviewed.

No owner credentials or authenticated administrator browser session will be used in Codex. Browser UAT remains owner-executed; no live acceptance/download outcome is inferred from local substitutes or deployment health. Final Package1C acceptance remains on hold for remaining browser/SQL checkpoints, live no-run evidence/disposition and the existing genuine native multi-population evidence limitation. Stop once this correction is deployed and preserved; no Stage2/CM-01 or Package1D implementation.
