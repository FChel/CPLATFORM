# Package 1C — rejection audit correction

13 November 2026. Owner authorised implementation, focused tests and deployment through the established WARATAH workflow. Final Package1C acceptance remains pending owner browser UAT and applicable SQL audit checkpoints.

## Change and boundary

Only runtime file changed: `App_Code/NORMImportService.cs`.

- `ConfirmBatch` no longer filters qualifying failed confirmations to CSV/Icon profiles. Legacy ERP, governed Defence ERP, ActewAGL and Icon Water use the same rejection path. It creates a shared attempt GUID and verifies the actual authenticated identity. Ownership/authority failures retain their exclusion.
- `RecordBatchAcceptanceRejection` writes existing `IMPORT_REJECTED_SOURCE` events with `EntityType=ImportAttempt`. **One event per original file**, grouping distinct population scopes, prevents repeated failure events merely because the original contains several populations. Different originals share the attempt GUID and batch fingerprint. This is smaller than the assessment's suggested summary-plus-population events and directly meets the approved no-duplication requirement.
- Detail records acceptance phase, rejected outcome, safe specific reason, available scope, release/profile/coverage, original filename and actual SHA256. Stale conflicts include `SOURCE_PREDECESSOR_CHANGED`, expected/observed predecessor IDs and triggering scope captured at the failed check. Scope verification is labelled `Retained` or `ChangedOrUnavailable`; a changed preview is not represented as verified evidence. NULL ledger is explicit.
- Bounded, escaped metadata reserves reason/conflict/hash before optional scope/filename fields. Long scope lists/filenames carry a truncation marker and scope count within the existing2,000-character limit. No source bytes or session tokens are retained in rejection events. No new audit framework, event code, table or schema.
- The writer opens a distinct audit-only transaction after the source transaction has rolled back/disposed. All original-file events commit together. Failure to save this evidence produces an explicit evidence-save failure; neither source acceptance nor incomplete audit evidence is committed.
- Private `RoutineConfirmationException` distinguishes routine expiry and unchecked acknowledgement, including expiry during confirmation, from a real conflict followed by expiry. Cancellation remains unchanged. A successful-commit guard prevents downstream errors being labelled rejected; normal calculation failure remains an accepted-source outcome.
- Existing source checks, persistence order, predecessor selection, locks, accepted/replacement audits, inspection-audit behavior and older `CommitImport` rejection path remain intact. No accounting mapping, preview redesign, permission, authentication, deployment infrastructure, SQL or other module change.

## Verification before deployment

| Suite | Result |
|---|---|
| `Package1CUatCorrection/Run.ps1` (Steps1–4 and previous correction) |162 passed, including13 focused rejection-audit cases; no failures. |
| `Increment1B2/Run.ps1` with existing Defence sample |63 passed. |
| `Package1BSourceDownload/Run.ps1` |37 passed. |
| `Package1B/Run.ps1` |38 passed. |
| `Increment1A/Run.ps1` with existing Defence sample |44 passed. |
| `Increment1B/Run.ps1` with existing Defence sample |21 passed (14 service, one calculation, six helper/display assertions). |
| `FR016/Run.ps1` |43 passed; real-helper/OleDb production compilation and full ASP.NET compilation passed. Existing eJET line125 CS0162 warning only. |
| Existing navigation script/style checks |5 passed. |

**413 passing checks**, without double-counting combined suites. Browser Steps1–9 were not repeated. Tests use existing identity/database/HTTP substitutes and do not constitute new authenticated live UAT.

Focused cases cover a real Legacy two-population stale sequence with one original-file event, mixed-file shared attempt/context, genuine Defence/CSV/Icon profiles, later row failure and separate durable audit, audit-detail and audit-commit failure with no partial acceptance, entry/during-confirmation expiry, conflict followed by expiry, cancellation/unchecked acknowledgement, long escaped metadata, and accepted replacement with expected calculation failure. Existing authority and source-integrity tests remain passing.

Directly affected tests are `Package1CStep2/Tests.cs`, `Package1CStep3/{Tests.cs,Store.cs}`, `Package1CUatCorrection/Tests.cs`, `Increment1B2/Tests.cs` and the existing `FR016/TestSupport.cs`. The latter now observes transaction lifecycle. Old zero-total-write assertions now distinguish permitted rejection-only audit commits from forbidden accepted-source writes; source preservation assertions remain enforced.

## Preflight and deployment receipts

Read-only preflight succeeded at05:54:27 UTC using SSM command `d5861516-cfd7-47ce-af48-fa35f91a68d4`. [Snapshot](WARATAH_Package1C_RejectionAudit_Preflight_20261113.json) and [SELECT-only query](WARATAH_Package1C_RejectionAudit_Preservation_ReadOnly_20261113.sql) retain ten full NORM source/configuration/audit table fingerprints, all original hashes, other user-table row counts, protected-file hashes, current manifest and standard health checks. All258 managed hashes match deployed `01de80857333fd75f0d094d07a94def61fa052b7`; no pending/offline state. Audit high-water154; the live `PerformedUtc` default is `sysutcdatetime()`. No historical audit was fabricated.

Initial preflight SELECT used an unquoted reserved alias and failed read-only; the next successful query output exceeded SSM's24KB response limit. The compressed-output rerun above is the complete baseline used for comparison; incomplete output is not verification evidence.

Candidate, PR, validation, deployment and postflight results will be recorded after the normal workflow completes. No database migration or baseline change is required. Existing server configuration, UDLs, uploads/evidence and other application data must remain preserved.

## Fresh owner browser verification gate

After deployment is confirmed, repeat **only the controlled Step10 stale-conflict scenario**, using your own two independent authenticated browser sessions:

1. Session A: select release8/UAT_P1C; inspect file01 (`01_CONTROLLED_A_FY2026_multi-company.xlsx`), check its two replacement populations and leave it staged. Expected predecessors at baseline are Import17 for1000/NULL/Departmental and Import18 for2000/NULL/Administered.
2. Session B, separate browser profile/private session: select release8; inspect file09 and explicitly confirm the2000/NULL/Administered replacement. Expected: one newly accepted import; calculation may fail as expected. Record its actual import ID. Do not assume the next ID or re-confirm an uncertain result.
3. Return to Session A, acknowledge replacement and confirm its original preview before expiry. Expected: specific accepted-source-changed rejection; neither A population accepted. Record message and approximate time.
4. Report B's new import ID and A's message/time. Stop. George can independently check new shared-attempt audit evidence, authenticated actor/server UTC, specific reason and expected/observed IDs, scopes/hash, no A partial records, unchanged Import17 and unrelated populations/configuration, and preserved historical originals.

These are a **new authorised attempt**, not a backfill or a repeat of Steps1–9. Codex will not use the owner's browser session/credentials, create accounts or change permissions. Live rejection persistence remains Pending until this interaction and the applicable independent SQL checkpoint complete. Step6, replay/download/no-run/genuine native multi-population evidence limitations remain separately recorded. No Package1D or Stage2 work.
