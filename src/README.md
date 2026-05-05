# CPLATFORM — FinHub

CPLATFORM (externally branded **FinHub**) is the Defence Finance Group internal intranet platform. It hosts a small set of finance modules that bridge SAP S/4HANA and BODS-driven workflows.

This repository contains the platform shell plus the **LPPI Review** module — currently the primary active module. The rest of this README focuses on LPPI Review.

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

- ASP.NET WebForms .NET Framework 4.8, C#. App_Code is C# 5 compatible — no pattern matching like `is decimal d`; use `TryParse` + explicit casts.
- SQL Server (CPLATFORM database). Connection is via OLE DB using a UDL file (HTTP access blocked at IIS). Long-term plan: migrate to encrypted `connectionStrings`.
- Vanilla JavaScript (no frameworks), plain CSS.
- EPPlus 4.5.3.3 LGPL for Excel. Do not introduce ClosedXML or anything that wants newer `System.Runtime` — the older EPPlus has been stable on the CPLATFORM server.
- SMTP (production) or `mailto:` to the Outlook client (UAT), gated by a single config flag.

---

## Conventions

- **Database tables** are prefixed by module (e.g. `tblLPPI_*` for LPPI Review). All data access goes through the module's helper class — direct ADO.NET calls are not the pattern.
- **OLE DB needs positional `?` placeholders**, not named `@param` markers. The helper does the translation; new code should go through it.
- **Admin pages inherit a base page** that renders the shared header and gates by an admin user list. The reviewer page is the exception — it authenticates via an unguessable token and opts out of the admin gate.
- **Read the `.aspx` `Eval()` bindings before rewriting code-behind SQL** — the markup is the source of truth for column aliases.
- Design tokens live in CSS custom properties at the top of the stylesheet (DFG palette: orange, black, white).
- **en-AU spelling everywhere** (`organisation`, `centre`, `colour`). Globalisation is set in `web.config`.

---

## Getting set up (DEV)

1. Create an IIS application pointing at the repo root. Integrated app pool, .NET CLR v4.0.
2. Enable Windows Authentication in IIS Manager (Anonymous + Windows both on). The reviewer page works anonymous + token; admin pages need Windows for identity resolution.
3. Run the schema scripts under `db/` against your DEV database.
4. Create a UDL file pointing at your DEV SQL Server (match the format of the UAT UDL that's already in the repo).
5. Copy `web.config` and replace placeholder values per `web_config_private-values.md`.
6. Recycle the app pool after any edit to App_Code (touching `web.config` is the easy way) — a stale `bin\App_Code.dll` can mask code changes.

`web.config` ships with placeholders for everything environment-specific. Real values are documented in `web_config_private-values.md`. **Any new `web.config` setting requires both** — the placeholder in the public file and the real value in the private one. Do not ship one without the other.

---

## Deploy order

**SQL before code, every time.** Schema scripts are idempotent and guarded — safe to re-run; each object is checked before create.

Code deploys as a single unit — replace files under `src/` and IIS picks them up. If C# changes appear not to take effect, touch `web.config` to force a recompile.

`web.config` edits trigger an app pool restart automatically.

The destructive scripts (full drop, data refresh) refuse to run in PROD by design — both have a guard that must be commented out before they'll execute.

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

---

## Database

SQL Server, database name `CPlatform`, table prefix `tblLPPI_*` for everything in this module. Schema covers loaded files, the document detail (one row per line, since the BODS extract may have multiple lines per accounting document), the lookup tables (Capability Managers, recipient lists, reason codes), the package + review structures, and three audit logs (review history, email log, admin user list).

Schema scripts live under `db/`:
- A create script (idempotent, safe to re-run).
- A data-refresh script (DEV / UAT only — keeps schema and config rows, wipes transactional data).
- A full-drop script (DEV / UAT only — drops every `tblLPPI_*` object).
- An admin user seed script.

Both destructive scripts have a `RAISERROR` guard at the top that must be commented out before they'll run. Do not bypass that guard in PROD.

A few data model decisions worth knowing about up front:
- **One row per line.** A single accounting document may have multiple lines in the source BODS file. The reviewer codes the document once; the review row is stored against the smallest DocumentID for that document, and joins at read time use a correlated sub-query so every line of the same document inherits the single review.
- **Capability Manager vs. CM Program.** A CM Program (e.g. ARMY) groups many individual Capability Managers. Packages are scoped per Program, but each document carries its specific CM — that CM is the LPPI Charge Cost Centre that will be charged with interest if the outcome is Payable.
- **Tax code.** Always exported as `P5` regardless of the source value — Finance has confirmed interest payments are not tax-input or tax-output relevant.

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

## Reporting

For stakeholder / executive reporting, **Power BI** is the preferred tool — connect it to SQL views over the LPPI tables. The application itself is the operational tool, not the reporting tool; do not extend the dashboard with bespoke reporting widgets when Power BI can do it natively.

---

## When something goes wrong

The Help page Troubleshooting section covers the common gotchas (production mode flag, missing recipient configuration, stale App_Code DLL, Windows auth, OLE DB parameter binding). Read that first — it's kept current as issues come up. Anything not covered there is probably new.
