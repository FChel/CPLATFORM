# POD — engineering notes

POD (PO Review Dashboard) is the purchase order dashboard for the Financial Operations Compliance Program. Internal `PORD` names, settings and URLs are retained for compatibility. Its first live check is **Non-Standard Payment Terms (NSPT)**. It follows the LPPI Review loop (load → package → review → finalise → close out) and reuses LPPI's design system, identity, admin list and SAP deep links. See [the client review and deployment guide](POD_CLIENT_REVIEW.md).

The in-app **Help** page (`PORD/PORD_Help.aspx`) is the functional reference. Technical settings, delivery limitations and open business decisions belong in these engineering notes.

## Multi-check design

- **Check registry** (`App_Code/PORDChecks.cs`). Each check defines its key, name, tab label, status (Live / Example / Planned), issue categories, the issue cell and "why flagged" text on the review page, fix wording, and its BODS extract. Reason codes carry a `CheckType`.
- **The exception is the unit.** Each exception is a PO × check pair, with typed common PO fields; check-specific values go in `Attr`. A PO flagged by two checks appears once per check and is answered separately. Exclusions are keyed by PO + check + the accepted value.
- **One package per Delivery Manager program per cycle, covering all checks.** That means one AS Fin email, one POC email per contact, and one review page with a tab per check. This avoids sending a separate email for every check.
- **Dashboard tabs.** *All checks* shows active check cards and an expandable upcoming-check list; `?check=KEY` scopes exception figures to one check. Package status is shared across checks; the package table always shows a count per check.
- **Shared across checks:** response model (fix / valid reason / reassign), lifecycle, finalise, repeat tracking (Review Nbr), outcomes CSV (with a Check column) and next-load verification.
- **Demo:** *Currency vs bank mismatch* is seeded as an **example** second check, so the tabs can be seen working. Direct payment threshold, Purpose of payment codes and Non-procurement payment type are listed as planned (Decision Brief).

Adding a check means a registry entry, its reason codes, its classification rule and an extract type on Load file. No new pages, handlers or tables are needed.

## Client review build

- `PORD.DemoMode` defaults to **true**. Data comes from `PordDemoStore`, an in-memory store seeded relative to today and shared by everyone viewing the app domain. It resets on app-pool recycle or via **Help → Restore sample data**, with an explicit checkbox. Use fictional data only; feedback should be recorded outside the sample store. Setting the flag to false now fails closed until a real store exists; it cannot silently present sample data as production storage.
- **No SQL is involved.** Adding a file under `sql/` changes the WARATAH SQL digest, so the schema will be proposed separately through the SQL approval process.
- In demo mode no email is sent. Send-outs records the send, and **Preview emails** renders exactly what AS Fin and each PO contact would receive.
- The production code paths are the rule engine (`PORDRules`), the CSV parser (`PORDCsvParser`), the email builders (`PORDEmail`) and the save/finalise validation. Only persistence (`IPordStore`) is stubbed.
- Load file checks the 13 specification columns and flags the four routing/currency additions. Unknown SAP keys are rejected, never inferred from digits in a key. Package loading stays visibly disabled in this environment; there is no simulated successful import.
- Dashboard and summary values are grouped by currency. Cross-check totals count each company + PO once. Exception counts remain PO × check.
- Save requests keep the submitted snapshot separate from edits made while the request is in flight. Versions advance at least one millisecond. Summary counts update from saved responses; validation errors reveal the affected check even when it was hidden by filters.
- Exclusions are revoked by PO + check + accepted value. New exclusions retain their source package; reopening removes only exclusions created by that package, regardless of the day it is reopened. Expired/revoked exclusions do not prevent a new grant. VR99 never creates an exclusion.
- ASP.NET source files use UTF-8 with BOM so punctuation is preserved even on hosts whose configured file encoding differs. CSS/JS references carry a release version to invalidate earlier browser caches.

## Layout

```
App_Code/
  PORDModels.cs      Domain types (exception = PO × check, package, reason, exclusion …).
  PORDChecks.cs      Check registry: NSPT (live), Currency vs bank (example), planned checks.
  PORDRules.cs       NSPT classification + term-key → net-days mapping. Pure, no I/O.
  PORDStore.cs       IPordStore contract + PordDemoStore (in-memory, seeded).
  PORDHelper.cs      Settings, formatting, JSON, pills; delegates identity/SAP links/admin to LPPIHelper.
  PORDBasePage.cs    Header/nav, admin gate, branded error page.
  PORDCsvParser.cs   BODS CSV parser (header matching by name, RFC 4180, comma or tab).
  PORDEmail.cs       Outlook-safe AS Fin / POC emails (inline font-family on every element).
PORD/
  PORD_Admin.aspx          Dashboard
  PORD_Help.aspx           Admin help (rules, lifecycle, settings, assumptions)
  PORD_Load.aspx           Upload → header check → classification preview (commit disabled in demo)
  PORD_SendOuts.aspx       Issue/remind, email preview, open any reviewer view
  PORD_AsFin.aspx          AS Fin mailbox per Delivery Manager program
  PORD_ReasonCodes.aspx    Valid reasons (VR01–VR03 from the spec, VR99 proposed)
  PORD_Exclusions.aspx     Accepted valid reasons skipped at load; revocable
  PORD_Outcomes.aspx       Close the loop: outcomes CSV, DFIM BP list, verified-at-next-load
  PORD_Review.aspx         Token-authenticated reviewer page (AS Fin = full package, POC = own POs)
  PORD_Review_Save.ashx    Batch save, per-row results, optimistic locking
  PORD_Review_Finalise.ashx  Finalise / reopen (AS Fin token only)
  PORD_EmailPreview.aspx, PORD_SampleFile.ashx, PORD_Outcomes_Export.ashx
css/pord.css               Module styles, loaded after lppi.css
js/pord.js                 Reviewer page behaviour (vanilla JS)
```

## Demo review links

| Program | AS Fin link | POC links |
| --- | --- | --- |
| ARMY | `PORD/PORD_Review.aspx?t=demo-army-asfin` | `…?t=demo-army-poc1` … `poc5` |
| NAVY, AIR FORCE, CASG, CIOG, JCG, DSTG | `demo-navy-asfin`, `demo-airforce-asfin`, … | `demo-<program>-pocN` |

Send-outs → **Open review** lists every link for each package.

## Deploying to WARATAH

1. `tools/deploy/Waratah.Common.ps1` now allows the `PORD/` folder. **The server validates payload paths with its administrator-installed copy of that script**, so an administrator must re-run `Install-WaratahServer.ps1` from a commit that includes this change before the first PORD deployment. Until then, the deployment is rejected with "Unlisted/Invalid manifest entry: PORD/…".
2. There are no `web.config` changes. Every `PORD.*` key has a default.
3. Access uses the LPPI admin list. Browse to `PORD/PORD_Admin.aspx`. A POD landing-page tile is included and appears when the server administrator configures `CPlatform.Tile.PORD.Url` and the permitted environments. The server-owned `web.config` is never replaced by the package.

## Validation

Run `tools/Test-Pord.ps1` with the installed .NET Framework compiler. It compiles the actual model, rules, parser, store, helper and email code using C# 5, then checks validation, contact scoping, stale/rapid saves, finalise/reopen, exclusions, mailbox validation and currency totals. The identity replacement is inside the isolated test executable only. WARATAH validation runs this before compiling and packaging the full application.

## Business decisions still required

- Confirm BODS's definition of open PO, the monthly run day and PO Creator → POC email resolution.
- Review the first NSPT file against the 13 specification columns plus Currency, DM Program, Delivery Manager and POC Email. Confirm the actual SAP term-key map with ERP, including any discount tiers.
- Foreign-currency POs are included under the agreed 14-day rule; BODS must apply GL exclusions 211455 and 212208 because the expected extract has no GL column.
- Contracts over $1 million do not receive an automatic valid reason until policy owners agree the treatment.

## Next steps (after the spec is confirmed)

1. Agree the extract layout, including the proposed Currency, DM Program, Delivery Manager and POC Email columns.
2. Write the `tblPORD_*` schema (packages, POCs, POs, reviews + history, exclusions, email log, load batches) and put it through the SQL approval process.
3. Implement `PordSqlStore : IPordStore` using the LPPIHelper OLE DB conventions, then set `PORD.DemoMode=false`.
4. Wire real sending through the LPPI SMTP settings, gated by a `PORD.ProductionMode` flag (mirroring LPPI).
