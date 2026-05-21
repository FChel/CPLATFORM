# CPLATFORM — FinHub

CAPS PLATFORM (externally branded **FinHub**) hosts a set of finance modules including CAPS and other modules that bridge SAP S/4HANA and BODS-driven workflows.

This repository contains the platform shell plus the **LPPI Review** module.

The rest of this README focuses on LPPI Review.

---

## What LPPI Review does

LPPI Review processes Late Payment Penalty Interest (LPPI) cases under [RMG-417](https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417) — Supplier Pay On-Time or Pay Interest Policy.

The end-to-end loop is:

1. **Load** a BODS extract of late-payment cases.
2. **Issue** review packages, grouped by Capability Manager program (e.g. ARMY, NAVY), to the recipients configured for each group.
3. Reviewers **decide** whether each case is payable, on a token-authenticated page (no application login required).
4. **Export** payable cases back to ERP for processing against the responsible cost centres.

For functional documentation — page-by-page guides, package lifecycle, configuration flags — see the in-app **Help** page. The Help page is the source of truth for module behaviour. This README is for engineers picking up the codebase.

---

## Stack

- ASP.NET WebForms .NET Framework 4.8, C#. App_Code is C# 5 compatible.
- SQL Server (CPLATFORM database). Connection is via OLE DB using a UDL file (HTTP access blocked at IIS).
- Vanilla JavaScript (no frameworks), plain CSS.
- EPPlus 4.5.3.3 LGPL for Excel.

---

## Repository layout

```
<<<CPLATFORM root/>>>
  Default.aspx              CPLATFORM landing page (FinHub tiles & hero CTAs)
  web.config                Single config file; values differ UAT vs PROD
  README.md                 This file
  App_Code/                 Shared C# (compiled on-the-fly)
    LPPIBasePage.cs         Base for admin pages (header + admin gate)
    LPPIHelper.cs           OLE DB access, parameter rewrite, helpers
    LPPIEmail.cs            AS Fin / POC email builders (Outlook-safe)
    LPPIExport.cs           ERP Payment Request bulk-upload builder
    LPPIFileParser.cs       BODS extract parser + commit reconcile
  LPPI/                     Module pages (.aspx + .aspx.cs)
    LPPI_Admin.aspx           Dashboard
    LPPI_Summary.aspx         Cycle-summary in-flight view (admin)
    LPPI_Help.aspx            In-app admin help
    LPPI_Load.aspx            Monthly file load
    LPPI_Load.aspx            Monthly file load
    LPPI_Batches.aspx         Load batch history
    LPPI_SendOuts.aspx        Issue / remind / preview
    LPPI_CapabilityManagers   AS Fin email config
    LPPI_ReasonCodes.aspx     Reason code admin
    LPPI_Deactivated.aspx     RC-RL watch-list
    LPPI_Export.aspx          ERP export picker
    LPPI_AdminUsers.aspx      Admin allow-list
    LPPI_Review.aspx          Token-gated reviewer page (AS Fin + POC)
    LPPI_Review_*.ashx        Reviewer endpoints (Save, Finalise, etc.)
    LPPI_Summary_Export.ashx  Admin-auth full data export for the Summary page
  css/lppi.css              All LPPI styles + design tokens
  js/lppi.js                Reviewer-page interactions (vanilla JS)
  Database/
    CPlatform.udl             OLE DB connection file (blocked at IIS)

  sql/
    LPPI_Schema.sql           Canonical fresh-install schema + seeds
    LPPI_AdminAdd.sql         Admin Admin
    LPPI_DeleteAdmin.sql      Delete Admin
```

---

## Conventions

- **Database tables** are prefixed by module (e.g. `tblLPPI_*` for LPPI Review). All data access goes through the module's helper class — direct ADO.NET calls are not the pattern.
- **OLE DB needs positional `?` placeholders**, not named `@param` markers. The helper does the translation; new code should go through it.
- **Admin pages inherit a base page** that renders the shared header and gates by an admin user list. The reviewer page is the exception — it authenticates via an unguessable token and opts out of the admin gate.
- **Read the `.aspx` `Eval()` bindings before rewriting code-behind SQL** — the markup is the source of truth for column aliases.
- Design tokens live in CSS custom properties at the top of the stylesheet (DFG palette: orange, black, white).
- **en-AU spelling everywhere** (`organisation`, `centre`, `colour`). Globalisation is set in `web.config`.

---

## Configuration

The most operationally important `web.config` settings, by category:

- **Environment label** — DEV / UAT / PROD chip rendered on every page.
- **Production mode flag** — single switch that gates real email sending. Mutually exclusive with the UAT-only "Mark as sent (test)" button.
- **Base URL** — the public hostname used to build reviewer links in outgoing emails. Critical to get right at PROD cutover.
- **Due-date defaults** — review window length and "due soon" reminder threshold.
- **SMTP** — host, port, SSL, credentials. Only consulted in production mode.
- **From / support mailboxes** — addresses for outgoing reviewer emails and the page-header support button.
- **SAP Fiori host** — used to build deep links to the document and PO factsheet apps.

Full key list is in the in-app Help page. `web.config` itself has comments next to each setting.

**PROD checklist:** environment label = PROD, production mode = true, real SMTP host configured, base URL matches the public hostname. Otherwise reviewer email links will be wrong.

### UAT vs PROD differences

The same `web.config` will not work in both environments. Per-environment differences:

- **`CPlatform.Environment`** — `UAT` vs `PROD`. Drives the chip colour and label on every page.
- **`LPPI.ProductionMode`** — `false` in UAT (shows the "Mark as sent (test)" button, real send disabled), `true` in PROD (real send enabled, mark-as-sent hidden and server-side refused).
- **`LPPI.BaseUrl`** — the UAT host vs the PROD host. Reviewer email links are built from this; getting it wrong sends recipients to the wrong server.
- **SMTP host / mailboxes** — UAT and PROD typically point at different mail relays and may use different `LPPI.MailFrom` / `LPPI.SupportMailboxTo` values. Confirm at cutover.
- **UDL** — different DEV / UAT / PROD `.udl` files under `Database/`. The active one is selected by `web.config`.
- **Anonymous Authentication** — UAT needs `<anonymousAuthentication enabled="false" />` declared in `web.config`. PROD has anonymous auth locked at the `applicationHost` level, so the same element in `web.config` causes a 500.19 — it must be removed in the PROD `web.config`. This is the one piece of `web.config` markup that genuinely differs between the two environments rather than just appSettings values.

Real values for both environments are tracked in `web_config_private-values.md`.

---

## Database

SQL Server, database name `CPlatform`, table prefix `tblLPPI_*` for everything in this module. Schema covers loaded files, the document detail (one row per line, since the BODS extract may have multiple lines per accounting document), the lookup tables (Capability Managers, reason codes), the package + review structures, the per-POC token set, and three audit logs (review history, email log, admin user list).


A few data model decisions worth knowing about up front:
- **One row per line.** A single accounting document may have multiple lines in the source BODS file. The reviewer codes the document once; the review row is stored against the smallest DocumentID for that document, and joins at read time use a correlated sub-query so every line of the same document inherits the single review.
- **Capability Manager vs. CM Program.** A CM Program (e.g. ARMY) groups many individual Capability Managers. Packages are scoped per Program, but each document carries its specific CM — that CM is the LPPI Charge Cost Centre that will be charged with interest if the outcome is Payable.
- **Per-POC tokens (`tblLPPI_PackagePocs`).** Each invoice POC in a package gets their own unguessable token row, populated at file-load time alongside the package. POC tokens scope the reviewer page to that POC's documents (filtered by `PocEmail`), as opposed to the package-level AS Fin token which sees everything. The set of POCs for a package is frozen once the package leaves NotSent — later loads do not add POCs to an in-flight package.
- **Tax code.** Always exported as `P5` regardless of the source value, interest payments are not tax-output relevant.

---

## SAP / BODS integration

- **Inbound:** BODS produces `LATEPMT_INTEREST_REVIEW_*.xls` extracts (tab-delimited UTF-8 text despite the `.xls` extension).
- **Outbound:** the Export page builds an ERP Payment Request bulk-upload spreadsheet for reviewed Payable cases, matching the supplied template.
- **Deep links:** SAP S/4HANA Fiori `displayFactSheet` (F1852) for the document number and PO columns. Helper functions build the URLs from a configurable base host.

---

## Outlook email rendering — non-obvious workaround

Outlook on Windows uses the Word HTML rendering engine which does **not** inherit `font-family` from a parent element. Anything without an inline font declaration falls back to Times New Roman. The fix in the email body builder is two-fold: a `<head><style>` block as a defence-in-depth fallback (Outlook web, dark mode, mobile clients), and inline `font-family` on every text-bearing element in the body (Outlook desktop, the strict case).

If a future change introduces a new text-bearing element in the email builder, declare `font-family` on its inline style or it will render as Times New Roman in Outlook desktop. This is the single most likely regression after a UI change to the email template.

---

## When something goes wrong

The Help page Troubleshooting section covers the common gotchas (production mode flag, missing recipient configuration, stale App_Code DLL, Windows auth, OLE DB parameter binding). Read that first — it's kept current as issues come up. Anything not covered there is probably new.
