# NORM — Package 1D: Bounded Security Correction Implementation

Living record · 17 November 2026 · George (Codex).

**LOCAL CANDIDATE READY — OWNER REVIEW REQUIRED.** E01/FR-093 and C04/D5 are implemented locally. All nine focused suites pass, with525 reported checks/assertions counted once per suite. Full ASP.NET working-tree candidate compilation passes. No AWS,WARATAH,live SQL,browser-owner session,PR,merge or deployment was used. No handoff package is necessary for this code review;none was created in Deploy or Exports. Local test/compilation products remain temporary;immutable evidence below is documentation,not a deployable package.

## Candidate and scope

Local branch **`codex/norm-package1d-security`**,based on the existing `origin/uat` integration commit **`3184226c2d3a0334cb8ff2e18750ce8e0e38d488`**. Its runtime tree matched the inspected prior implementation. No remote fetch was required or performed. The candidate is the local selective commit containing this implementation record;the final handoff reports its Git commit ID. The exact40 runtime/test input files and their canonical UTF-8/LF SHA-256 values are in [the source manifest](Local_Verification_20261117_114113/Candidate_Source_Manifest_20261117.json). Existing unrelated modified/untracked documentation,fixtures and SQL operator files were preserved,not bundled into the candidate.

**18 runtime files changed:**

| Exact repository path | Why |
|---|---|
| `App_Code/NORMErrorHandling.cs` (new) | Closed reviewed business-message catalogue,narrow numeric/identity templates,fixed aggregate/PDF guidance,generic unexpected-error responses,restricted private diagnostic persistence/correlation and safe handler boundary. |
| `App_Code/NORMBasePage.cs` | Replace raw framework exception display;preserve failure status;after committed headers suppress remaining output rather than append technical HTML. |
| `NORM/NORM_Import.aspx.cs` | Both broad catches retain the Exception for safe classification;pending-state clearing and accepted-source/failed-calculation distinction unchanged. |
| `NORM/NORM_YearSetup.aspx.cs` | Replace broad exception-type trust with reviewed safe-message selection;retain the controlled Year Setup denial. |
| `App_Code/NORMStartOfYearSetup.cs` | PDF wrapper retains its inner cause but no longer concatenates it into user text. |
| `NORM/NORM_Mapping.aspx.cs` | Safe presentation in the three existing broad catches;no mapping/calculation logic change. |
| `NORM/NORM_SourceHistory.aspx.cs` | Reviewed controlled messages;technical InvalidOperation failures become safe failures while protected sections still clear. |
| `NORM/NORM_Workspace.aspx.cs` | Sanitise displayed historical FailureDetail without modifying stored run diagnostics. |
| `App_Code/NORMMappingWorkbookHandler.cs` | Replace explicit raw base-exception HTTP output. |
| `App_Code/NORMSourceFileHandler.cs` | Thin unexpected-error boundary;all normal download/status/ownership/hash/audit-before-bytes logic retained. |
| `App_Code/NORMFinancialStatementsExcel.cs` | Thin unexpected-error boundary around existing export. |
| `App_Code/NORMWordExport.cs` | Same;no renderer/business change. |
| `App_Code/NORMMappingExport.cs` | Same;no workbook/data change. |
| `App_Code/NORMReviewPack.cs` | Same;no pack-generation change. |
| `App_Code/NORMYearSetupDocumentHandler.cs` | Existing fixed generic catch also attempts bounded private diagnostics;existing message/status retained. |
| `App_Code/NORMHelper.cs` | Catch-compatible typed entity-denial signal;read-only active registered-Preparer and specific revoked-grant classification. Existing access SQL and locks retained. |
| `App_Code/NORMImportModels.cs` | Two opaque internal server-only issuance/batch slots,never accepted from form/ViewState input. |
| `App_Code/NORMImportService.cs` | Private immutable issuance/batch evidence,session/actual actor/full membership/hash/expiry/one-use validation,and the private independent revoked-assignment audit path. |

**22 test/harness/runner files changed**,individually listed in the source manifest:existing runners include the new helper;FR016 substitutes now host an authenticated isolated session and typed denial/role classification;Import harnesses supply that actual isolated identity;existing Step3 tests add22 revocation-security and10 safe-error checks;the real ASP.NET ViewState harness now uses the production base page and tests foreign entity-option rejection. No new test framework or unrelated product feature was added.

## E01 / FR-093 behaviour

Expected reviewed literals and tightly constrained numeric templates stay actionable and HTML-encoded by existing pages. Technical exceptions never qualify merely because they are InvalidOperation/InvalidData/Argument exceptions. Unknown/provider/file/framework/reflection and wrapped technical failures receive fixed guidance;safe PDF and mapping aggregate guidance never echoes the technical suffix. Stored raw calculation failures remain untouched in SQL and display through the safe formatter. Source acceptance followed by calculation failure retains its existing distinct warning.

Private unexpected-error diagnostics reuse AuditEvent with fixed **`NORM_REQUEST_FAILED` / `NORMRequest`**,server-generated reference,actual authenticated active application user,DB UTC,operation,exception type/HResult and bounded method-only stack metadata. No exception text,SQL/connection string,source bytes/rows,request body,cookie/token or arbitrary entity payload is logged. The writer is private;there is no public audit endpoint or caller-selected audit shape. Per-request deduplication also prevents recursive retry after persistence failure. SQL unavailable/unregistered/unauthenticated cases stay generic and failed,without claiming diagnostic persistence. The optional Trace fallback is bounded and guarded;no durable listener is assumed.

The diagnostic code/type is outside existing Workspace and SourceHistory audit selectors. Tests guard that exclusion. Correlation is displayed only as an opaque reference after successful persistence. Downloads retain all existing explicit400/401/403/404/409 responses and successful bytes;unexpected audit/storage failures return a safe non-success response without source bytes. If headers/bytes have already been committed,HTTP cannot retroactively change the status/transfer;the boundary suppresses further output and never appends technical details. No claim of atomic network transfer is made.

## C04 / D5 security boundary

After authorised inspection,the service attaches private serializable immutable evidence bound to the original preview object,confirmation fingerprint,real owner,session and original Preparer status. Complete batch validation binds the exact member references and sorted batch fingerprint. Direct legitimate service calls are sealed while current authority is valid;the browser already performs that stage at inspection. The complete member set must match,although order may differ for stable-lock-order compatibility. Old previews without issuance evidence require reinspection.

At confirmation,authenticated actor/session,issued evidence,complete membership,original fingerprints/bytes/hash,expiry,unconsumed state and replacement acknowledgement are checked before a frozen rejection snapshot is eligible. Only the helper's typed denial for that exact entity,with the same active registered Preparer and active entity but missing assignment,can enter the private revoked-grant writer. An arbitrary UnauthorizedAccessException,disabled user,changed role/inactive entity,unrelated scope,forged/copied marker,missing member,modified bytes/release,foreign actor/session,expiry or replay cannot use it.

**All original acceptance checks remain:** role/entity gate,reinspection/configuration validation,locked transaction authority check,predecessor locks and source rollback. A failure may occur before the acceptance transaction;the live owner test must not claim the inner gate was reached solely from denial. The normal1C rejection writer retains its entity-access check. The special audit-only writer accepts only the service-private frozen snapshot,rechecks actual session/actor and missing-grant classification transactionally,and has no source/grant write or protected-data retrieval capability.

A genuine revoked confirmation writes **one `IMPORT_REJECTED_SOURCE` event per original**,grouping both fixture populations with shared attempt GUID,batch fingerprint,actual attempted actor,DB UTC,fixed **`ENTITY_ASSIGNMENT_REVOKED`**,Outcome=Rejected,hash/profile/coverage and bounded encoded scopes. It commits independently after source rollback/disposal. It does not restore access or use SYSTEM as the attempted browser actor. Audit insert/commit failure rolls back audit rows and returns the fixed evidence-persistence failure message;no source can commit. No automatic retry/backfill is introduced. Normal authorised stale-conflict auditing remains intact. Consumed service replay now exits routinely with no new acceptance or rejection event,matching the approved one-use safeguards.

## Local verification

[Immutable final-run logs and manifest](Local_Verification_20261117_114113/Candidate_Source_Manifest_20261117.json) record the tested source. Use the `*_Final_20261117.txt` files for the final combined suite,real-host error response and full compiler outcomes;earlier same-directory successful runs are retained without overwriting. Counts below exclude repeated runs and avoid recounting the combined Step1–4 constituents.

| Executed runner | Reported passing checks |
|---|---:|
| `tools/tests/Package1CUatCorrection/Run.ps1` |194 (combined Step1–4/correction,including32 new1D cases) |
| `tools/tests/Increment1B2/Run.ps1` |63 |
| `tools/tests/Package1BSourceDownload/Run.ps1` |37 |
| `tools/tests/Package1B/Run.ps1` |38 |
| `tools/tests/Increment1A/Run.ps1` |44 |
| `tools/tests/Increment1B/Run.ps1` |21 (14 service,1 real calculation,6 helper assertions) |
| `tools/tests/FR016/Run.ps1` |43 (34 service,9 engine),real-helper/OleDb compilation and **full ASP.NET compilation** |
| `tools/tests/Package1AStep3/Run.ps1` |77 (62 service/page/handler,3 engine,12 JavaScript) |
| `tools/tests/Package1AStep3/RunViewState.ps1` |8 real ASP.NET/session/MAC checks,including production-base-page event validation |

**525 reported checks/assertions;all suites pass;no unresolved failed check.** The only full-site compiler warning is the existing unrelated `eJET/eJET.aspx.cs:125` CS0162 unreachable-code warning;that file was not changed. `git diff --check` passed for the correction paths. Shared `web.config`,SQL files,protected deployment tooling and workflows have no diff.

New security cases prove one independently committed two-population revoked event with real attempted actor/hash/both scopes and zero sources;inside-transaction denial rolls back before auditing;expired/consumed/foreign/missing/forged/copied/role-changed/inactive/mismatched contexts cannot generate privileged events;audit insert/commit failure preserves sources and leaves no partial evidence;replay cannot mint another event. Original stale-predecessor/profile/coverage/replacement/history/download tests pass unchanged in substance.

Error cases include injected actual OleDb exception type,file/framework/reflection errors and wrapped parser failures;fixed PDF/mapping guidance;stored private calculation text;bounded diagnostic correlation;unregistered/unauthenticated denial of diagnostic writing;deduplicated diagnostic failure;and ordinary widget exclusion. The real local ASP.NET host submits a foreign entity option and verifies continued framework rejection with generic text and no foreign content. It uses production NORMBasePage with isolated identity/database substitutes,not owner credentials or a claim of Windows/live UAT.

Initial regression runs exposed obsolete harness expectations:new early complete-file rejection occurs before reinspection;consumed replay no longer writes a rejection audit;handled download errors return safe500 rather than escape;and the real-host form builder had incorrectly posted every submit button during a dropdown change. Assertions/form construction were corrected to the intended security/browser contracts,not disabled. Final logs above pass.

## Review gate, limitations and next live activity

Ready for independent owner review of the local candidate. Diagnostic storage is best effort during DB outage and the closed catalogue deliberately falls back safely for unknown future messages. Existing pre-change previews are not compatible with the new server-issued evidence:inspect fresh after any later deployment. No general authentication,hosting,IIS or logging infrastructure was changed.

**No live closure is claimed.** E01/C04 remain pending live acceptance despite passing local tests. Overlapping SQL confirmations and all other outstanding Package1D controls remain separate. Package1C stays **Conditionally Accepted — Verified Scope Complete**,with its two no-run-retrieval/genuine-native-multi-population reservations unchanged. Audit175/176 are not rewritten or relabelled;the new rejection path must be exercised by a new authorised attempt after deployment.

After owner review and separate publishing authority,the next activity requiring WARATAH is **read-only candidate-preservation/schema/default/INSERT-permission preflight immediately before deployment**. Start WARATAH only for that concrete activity once the reviewed local candidate is ready;local source inspection/tests are complete without it. Then use the existing `uat` PR/validation/merge/deployment process only if authorised. Later owner-only safe-error and fresh revoke/confirm/restore UAT need their own checkpoint approvals and independent SQL evidence. Do not repeat accepted1C tests or proceed to Stage2.

**Stop at LOCAL CANDIDATE READY — OWNER REVIEW REQUIRED.** No handoff/deployment package was created;no Exports or protected deployment configuration was modified. Existing unrelated working-tree changes remain deliberately unstaged/untracked. No AWS/WARATAH/live SQL,account/grant changes,PR,publish or browser UAT occurred in this implementation increment.
