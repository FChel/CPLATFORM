# Package 1B — accepted-source download correction

Living record · 13 November 2026 · Implemented and verified locally; not deployed

**No technical obstacle prevents the existing handler from separating source acceptance from calculation success.** The existing run/file URL already joins retained file → import → calculation run and checks the stored entity. Calculation status is not needed to establish that an original file belongs to an accepted source.

## Minimum correction

Only production file `App_Code/NORMSourceFileHandler.cs` changes. Both metadata and byte-fetch queries now require `i.StatusCode IN ('Imported','Calculated')` instead of `r.StatusCode='Complete'`. A failed, running or successful calculation can therefore reference an accepted source; an Importing/Failed import cannot expose an original even if a run happens to be Complete. The accepted-source predicates are repeated at byte fetch, not just metadata lookup.

The existing authenticated identity/Preparer gate, stored-entity access check, parameterised run/file IDs, exact file/import/run joins, stored-entity predicate on byte retrieval, run/import deactivation checks, safe filename, cache/response handling and successful-download audit are preserved. No query accepts a caller-supplied entity in place of the stored entity. Audit write still precedes sending any original bytes and fails closed. Generic not-found messages now refer to an accepted source, not a completed calculation.

The URL contract remains `NORM_SourceFile.ashx?run=<run>&file=<file>`; no new endpoint, import-ID parameter, mapping catalogue, schema, source acceptance or financial approval mechanism is introduced. For the known accepted source the existing relationship is Import9 / Run9 / File10. After a separately authorised deployment, that same authenticated URL can serve the retained bytes despite Run9 remaining Failed. The failed run is not relabelled Complete and no recalculation or re-upload is needed.

Calculation, mapping, source acceptance, replacement, configuration releases and financial-statement approval code are unchanged. There is no requirement to invent accounting mappings merely to retrieve accepted financial evidence. The earlier proposed catalogue workaround is superseded by this owner-authorised correction.

## Verification

Fresh `origin/uat` was checked at `724d67ccf691ba955f066c1e42cf2e014c2c73f9`; the local application tree matched before editing. Tests use real production handler and ASP.NET HTTP response objects with isolated storage/identity fixtures; they do not contact WARATAH or claim live SQL execution.

| Check | Actual result |
|---|---|
| `tools/tests/Package1BSourceDownload/Run.ps1 -Baseline` | 1 Passed: compiles the actual deployed handler from origin/uat and reproduces 404 for accepted Imported source / Failed run. |
| `tools/tests/Package1BSourceDownload/Run.ps1` | 20 Passed / 0 Failed: exact genuine CSV bytes/hash through failed/running/complete run cases; successful-run regression; unauthenticated/unauthorised/entity denial; invalid/foreign/mismatched IDs; inactive/unaccepted/failed imports; missing bytes; acceptance/run/entity recheck at byte fetch; audit-failure no-byte release. |
| `tools/tests/Package1B/Run.ps1` | 38 Passed / 0 Failed: genuine native text import/retention/download regressions. |
| `tools/tests/Increment1B2/Run.ps1 -SamplePath <genuine Defence XLSX>` | 63 Passed / 0 Failed: existing actual service/page/access/replacement/coverage regressions. |
| `tools/tests/FR016/Run.ps1` | 43 Passed / 0 Failed, production import/engine compilation and full ASP.NET compilation Passed. Existing unrelated eJET.aspx.cs:125 CS0162 warning only. |

**164 passing correction/regression checks, plus one separate baseline reproduction; zero failures.** No broader unrelated suite was repeated. New focused fixtures preserve the real genuine ActewAGL CSV (27,669 bytes; SHA-256 `9d3a113b3c0ef344c7d3c419f5c8d6e241c9543b2731f4f9d53098cfae5ad631`) and use in-memory accepted/rejected/run/identity relationships. Both query predicates/joins and HTTP/audit behaviour are tested at the substituted storage boundary; actual database enforcement remains future controlled verification.

## Scope, fingerprints and stopping point

- Corrected handler raw SHA-256: `b4772194c58504b120baf98f78be735e491664b8bf07dcf0b4eca49d14f03a86`.
- Focused test paths: `tools/tests/Package1BSourceDownload/Run.ps1`, `TestSupport.cs`, `Tests.cs`. Existing application/test files other than the handler were not changed by this correction.
- Full-site build artifact: `C:\Users\Steve\AppData\Local\Packages\openai.codex_2p2nqsd0c76g0\AC\#!sandbox.{5f2187b2-895e-40b1-a425-f608ebb87c6d}\Temp\norm-fr016-ddc3c630b59347afad03d5b7a0a9ce2d`.
- Final focused-test artifact: `C:\Users\Steve\AppData\Local\Packages\openai.codex_2p2nqsd0c76g0\AC\#!sandbox.{ba1cf4bf-6243-4f20-9529-3738b1949839}\Temp\norm-source-download-bf9a8597f1054067805dfef072c1f622`.

No WARATAH/API/SQL/download request was performed by this local correction. Import9, failed Run9, original File10, Releases5/6, DEPT and all live audit rows remain unchanged. A future actual authorised download should append the existing SOURCE_FILE_DOWNLOADED event; it must never rewrite the earlier evidence. This task made no commit, push, PR, merge or deployment.

**Stop: correction locally verified and ready for separate deployment authorisation.** Live download of the existing original and remaining Icon UAT are not claimed Passed; Package 1B closure remains pending that later verification. Do not modify releases/mappings, re-upload Import9 or begin Package1C.
