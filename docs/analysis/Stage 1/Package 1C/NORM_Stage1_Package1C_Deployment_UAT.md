# Package 1C — controlled deployment and integration UAT

Living record, 13 November 2026. In progress; acceptance is not yet recommended.

## Candidate and authorisation

Project Owner authorised integration and WARATAH deployment of locally verified Steps 1–4, followed by focused integration UAT. Package 1D and Stage 2 remain excluded. No permission to change approved configuration or database schema is inferred.

Baseline: refreshed `origin/uat` at `59c487168f875b4de0fdd289c33324ec194f5d74`. Candidate branch: `codex/norm-package1c-uat`. Existing unrelated working-tree changes are excluded and preserved. Exact candidate commit, PR, validation and deployment receipts will be recorded below when executed.

The runtime candidate comprises six `App_Code` files (existing parsers/models/service/download handler plus new source-history service) and six NORM page files (import/workspace pairs and new source-history pair). No other CPLATFORM runtime module, SQL, deployment infrastructure, connection file or web.config is included. The existing retention and nullable-identity/coverage schema suffices; no migration is required.

Tests include the four Package 1C harnesses, necessary existing harness extensions, and seven unchanged genuine source fixtures required to reproduce the tests. Exact-path `.gitattributes` rules disable text normalisation for these retained originals so Git archive/checkout preserves their SHA-256. Assessment and Steps 1–4 verification records are retained as historical step evidence; the temporary Step 1/2 confirmation restrictions were superseded by Step 3.

## Local verification

Pre-candidate regressions: combined Steps 1–4 141; source downloads 37; Increment1B2 63; Package1B 38; Increment1A 44; Increment1B 21; FR016 43. Total 387 passed. Full ASP.NET compilation passed (existing eJET CS0162 warning). Commands are those in the Step 4 verification record, with the genuine Defence `-SamplePath` explicitly supplied to Increment1A/1B/1B2. Pinned-commit package validation is recorded below after completion. These checks use labelled controlled mechanics fixtures and genuine native originals; they do not establish live SQL/browser results.

## Live evidence and execution

Read-only WARATAH preflight through established SSM connection: `9cb1914b-6f2a-4565-b6ac-425d3132d084`, Success at 02:39 UTC / 13:39 AEDT. Immutable receipt: `WARATAH_Package1C_Preflight_20261113.json`. Current deployment is the expected baseline; 255/255 managed hashes match, all 11 retained original hashes match actual bytes, no pending/offline marker, standard health passes, protected config/UDL hashes recorded. Source inventory, full original/import/row/run/configuration/audit fingerprints are captured before deployment. Audit high-water 103. PORD/eJET 200 and existing Prepayment SYSTEM-context 500 are baseline observations, not authenticated functional tests.

Actual active approved populations: DEPT release 3/FY2026/company1000/0L/Departmental required; UAT_AAG release 5/FY2020/company150/NULL/Departmental required; UAT_ICON release 6/FY2016/company86/NULL/Departmental required. DEPT release 1/FY2025 is approved but has no classified population permission. Releases 2 and 4 are Draft. There is no Administered population, second company/ledger, same-agency mixed-year permission set or second approved same-year release for cross-release replacement. The exact owner-reviewed matrix was requested; no setup records were changed. Existing source IDs 1–10 and files 1–11 remain preserved.

The application will be published only through a PR into `uat`, passing WARATAH validation before merge and verifying the Deploy WARATAH workflow. Existing automatic rollback and server configuration/data protection remain applicable.

## UAT gates and limitations

Positive mixed-year/release/company/ledger/stream acceptance needs an explicitly reviewed same-agency UAT matrix. The existing multi-scope procedure names configuration prerequisites; its old single-release restriction describes earlier behaviour and is superseded by Package 1C. DEPT, UAT_AAG and UAT_ICON must not be combined as one agency. No approved permissions, releases, mappings or year settings will be altered to manufacture a positive result.

Genuine native multi-population-file accounting evidence remains unavailable. Labelled controlled supported-layout fixtures can establish mechanics only. Authenticated browser outcomes, atomic live acceptance/replacement, cross-release history, no-run retrieval and completeness remain unverified until actually executed and evidenced below.

## Outcome

Pending deployment and integration evidence. Final acceptance belongs to the Project Owner. Stop after the acceptance review; do not begin Package 1D or Stage 2.
