# PO Review (PORD) — engineering notes

PO Review is tranche 1 of the Financial Operations Compliance Program: **Non-Standard Payment Terms (NSPT)** on open purchase orders. It follows the LPPI Review loop (load → package → review → finalise → close out) and reuses LPPI's design system, identity, admin list and SAP deep links.

The in-app **Help** page (`PORD/PORD_Help.aspx`) is the functional reference. It includes the business rules, the response model and the assumptions that still need confirmation.

## Status: demonstration scaffold

- `PORD.DemoMode` defaults to **true**. Data comes from `PordDemoStore`, an in-memory store seeded relative to today and shared by everyone viewing the app domain. It resets on app-pool recycle or via **Reset demo data** on the Dashboard.
- **No SQL is involved.** Adding a file under `sql/` changes the WARATAH SQL digest, so the schema will be proposed separately through the SQL approval process.
- In demo mode no email is sent. Send-outs records the send, and **Preview emails** renders exactly what AS Fin and each PO contact would receive.
- The production code paths are the rule engine (`PORDRules`), the CSV parser (`PORDCsvParser`), the email builders (`PORDEmail`) and the save/finalise validation. Only persistence (`IPordStore`) is stubbed.

## Layout

```
App_Code/
  PORDModels.cs      Domain types (PO, package, reason, exclusion …). CheckType allows future tranches.
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
3. Access uses the LPPI admin list. Browse to `PORD/PORD_Admin.aspx`. No landing-page tile has been added yet, because a tile needs `CPlatform.Tile.PORD.*` in the server-owned `web.config`.

## Next steps (after the spec is confirmed)

1. Agree the extract layout, including the proposed Currency, DM Program, Delivery Manager and POC Email columns.
2. Write the `tblPORD_*` schema (packages, POCs, POs, reviews + history, exclusions, email log, load batches) and put it through the SQL approval process.
3. Implement `PordSqlStore : IPordStore` using the LPPIHelper OLE DB conventions, then set `PORD.DemoMode=false`.
4. Wire real sending through the LPPI SMTP settings, gated by a `PORD.ProductionMode` flag (mirroring LPPI).
