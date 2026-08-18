<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_Help.aspx.cs" Inherits="CPlatform.NORM.NORM_Help" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Help and control guide</title>
    <link rel="stylesheet" href="../css/norm.css" />
</head>
<body class="norm-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Statements.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Help and control guide</small></span></a>
        <nav class="norm-top-actions" aria-label="NORM actions">
            <a href="../Default.aspx">FinHub</a>
            <a href="NORM_Statements.aspx">Statements</a>
            <% if (CanPrepare) { %><a href="NORM_Workspace.aspx">Workspace</a><a href="NORM_Reporting.aspx">Reporting framework</a><% } %>
            <span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span>
        </nav>
    </header>
    <main class="norm-help-shell">
        <section class="norm-help-hero">
            <article class="norm-help-intro">
                <span class="norm-kicker">How NORM works</span>
                <h1>Fast preparation. Visible evidence. Accounting stays in control.</h1>
                <p>NORM replaces the repetitive spreadsheet assembly process with a controlled reporting cycle. It retains the original source, applies an approved mapping release and entity reporting profile, generates the PRIMA statement and note set, runs assurance checks and lets reviewers open every current-year figure back to its contributing G/L rows.</p>
                <div class="norm-help-path"><span>Profile</span><i>→</i><span>Import</span><i>→</i><span>Generate</span><i>→</i><span>Validate</span><i>→</i><span>Review</span><i>→</i><span>Publish</span></div>
            </article>
            <aside class="norm-help-proof">
                <span class="norm-kicker">The evidence contract</span>
                <strong>Nothing important is hidden in a workbook formula.</strong>
                <p>Each completed run binds source-file hashes, approved configuration, calculated figures, assurance results and source-row lineage with one fingerprint. A review pack replays that frozen evidence without recalculating it.</p>
            </aside>
        </section>

        <section class="norm-help-grid">
            <article class="norm-help-card">
                <span class="norm-kicker">Preparer workflow</span>
                <h2>Run the reporting cycle</h2>
                <ol>
                    <li>Choose the approved configuration for the reporting year.</li>
                    <li>Confirm entity type, reporting basis and activities in the reporting framework. These selections drive conditional PRIMA disclosures.</li>
                    <li>Upload the trial balance. NORM validates its structure and internal arithmetic before committing anything.</li>
                    <li>Review all four primary statements, generated note tables, policy wording and the disclosure-completeness check.</li>
                    <li>Open statement figures to inspect classifications and contributing source rows.</li>
                    <li>Assign preparers and reviewers for statements, Audit Committee and annual report modules.</li>
                    <li>Download the editable Word set, the linked financial-statements Excel workpaper and the Excel review pack for the accounting review record.</li>
                </ol>
                <div class="norm-help-callout"><strong>Accounting judgement is not automated.</strong><p>NORM does the assembly, arithmetic and evidence work. DFG accounting staff approve mappings, investigate variances and sign off the result.</p></div>
            </article>

            <article class="norm-help-card">
                <span class="norm-kicker">FY2025 exception</span>
                <h2>ROMAN periods 01–10 + ERP periods 11–12</h2>
                <p>FY2025 is the one controlled two-file transition year. Both originals are required, independently fingerprinted and checked for the expected period ranges with no overlap or gap.</p>
                <p>ERP ending balances drive the FY2025 statements because the ERP opening position already carries the migrated ROMAN year-to-date balance. Adding ROMAN again would double count periods 01–10. Before and after FY2025, the normal process is one trial-balance file.</p>
                <h3>What reviewers can retrieve</h3>
                <p>Preparer-authorised users can download each exact retained original from the statement header. Every retrieval is logged against the source-file record.</p>
            </article>

            <article class="norm-help-card">
                <span class="norm-kicker">PRIMA reporting set</span>
                <h2>Statements, notes and policy wording</h2>
                <p>The statement reader includes the Statement of Comprehensive Income, Statement of Financial Position, Statement of Changes in Equity, Cash Flow Statement and the conditional notes set. Administered schedules and specialist disclosures are activated by the entity profile.</p>
                <h3>Editable working content</h3>
                <p>Accounting-policy templates are saved against the calculation run so the reporting team can replace them with entity wording without changing approved mappings. The Word export carries figures, notes, policies and the disclosure register into an editable preparation copy.</p>
                <div class="norm-help-callout"><strong>PRIMA is a starting point, not a substitute for judgement.</strong><p>Remove immaterial or inapplicable disclosures only after the profile assessment is documented; add entity-specific information required by legislation, transactions and materiality.</p></div>
            </article>

            <article class="norm-help-card">
                <span class="norm-kicker">Figure drill-through</span>
                <h2>From statement to source</h2>
                <p>Select any current-year statement amount to open its frozen derivation. The drawer shows the mapping snapshot, note and cash-flow classification, original source amount and the contribution presented in $'000.</p>
                <h3>SAP G/L account jump</h3>
                <p>Where the row is ERP-backed and SAP is configured, the G/L account opens SAP Fiori “Display Line Items in General Ledger” for the company code, account and fiscal year. This is the closest useful live jump for a trial-balance aggregate.</p>
                <div class="norm-help-callout"><strong>Live investigation versus frozen proof</strong><p>SAP line items may change with permissions or later postings. The retained upload, SHA-256 fingerprint and persisted NORM lineage are the evidence for the calculation run.</p></div>
            </article>

            <article class="norm-help-card">
                <span class="norm-kicker">Mapping control</span>
                <h2>Change an account mapping safely</h2>
                <ol>
                    <li>Open <strong>Account mapping</strong> from the Control Centre and create a draft from the current approved release.</li>
                    <li>Download the editable workbook and use its controlled account-type and face-line lists.</li>
                    <li>Enter a reason for every changed account, then upload the complete workbook.</li>
                    <li>Review blocking validation, unmapped-account warnings and the financial impact against the retained TB.</li>
                    <li>A NORM administrator approves and locks the release.</li>
                    <li>Recalculate the retained TB to create a new run; the earlier run and its mapping snapshot do not change.</li>
                </ol>
                <div class="norm-help-callout"><strong>The mapping export from a completed run remains read-only evidence.</strong><p>Use the mapping-management workbook for changes. Completed-run exports always show exactly what that historical calculation used.</p></div>
            </article>

            <article class="norm-help-card">
                <span class="norm-kicker">Assurance</span>
                <h2>What the control panel means</h2>
                <ul>
                    <li><strong>Blocking:</strong> the run is not ready for accounting sign-off.</li>
                    <li><strong>Warning:</strong> the result can be reviewed, but an item needs explicit attention.</li>
                    <li><strong>Mapping coverage:</strong> shows how much source value is classified by the approved release.</li>
                    <li><strong>Audited comparison:</strong> highlights FY2025 differences; audited values never replace calculated results.</li>
                    <li><strong>Break test:</strong> creates a separate child import with a deliberate imbalance so the control response can be demonstrated safely.</li>
                </ul>
            </article>
        </section>

        <section class="norm-help-footer">
            <p>Configuration owners: keep SAP host settings, preparer access and the approved mapping release current before each reporting cycle.</p>
            <a href="NORM_Statements.aspx">Return to statements →</a>
        </section>
    </main>
</form>
</body>
</html>
