<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_Workspace.aspx.cs" Inherits="CPlatform.NORM.NORM_Workspace" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Preparer workspace</title>
    <link rel="stylesheet" href="../css/norm.css" />
</head>
<body class="norm-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Preparer workspace</small></span></a>
        <nav class="norm-top-actions"><a href="../Default.aspx">FinHub</a><a href="NORM_Statements.aspx">Financial statements</a><a href="NORM_Reporting.aspx">Reporting framework</a><a href="NORM_Help.aspx">Help</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span><span class="norm-user"><%= Server.HtmlEncode(CurrentUser) %></span></nav>
    </header>
    <main class="norm-workspace">
        <section class="norm-workspace-hero">
            <div><span class="norm-kicker">Government financial reporting</span><h1>From source file to an accountable, publication-ready reporting set.</h1><p>Each run locks its source, configuration, statements, notes, validations and figure-level derivation together. Entity rules also drive annual report and Audit Committee modules.</p></div>
            <div class="norm-workspace-actions"><a class="norm-button norm-button-dark" href="NORM_Import.aspx">Import trial balance</a><a class="norm-button" href="NORM_Reporting.aspx">Configure reporting set</a></div>
        </section>
        <section class="norm-summary-grid"><%= SummaryHtml %></section>
        <section class="norm-control-strip"><%= ControlStatusHtml %></section>
        <section class="norm-latest-import">
            <header><div><span class="norm-kicker">Latest trial-balance activity</span><h2>What changed since the previous import</h2></div><a class="norm-button norm-button-dark" href="NORM_Import.aspx">Import a new trial balance</a></header>
            <%= LatestActivityHtml %>
        </section>
        <section class="norm-module-centre">
            <header><div><span class="norm-kicker">NORM Control Centre</span><h2>Prepare, assure and publish the complete reporting set</h2></div><p>Core financial-statement modules are active. Phase 2 deliverables are separated so the roadmap is visible without crowding the statutory accounts workflow.</p></header>
            <div class="norm-module-grid">
                <a class="norm-module-card norm-module-card-setup" href="NORM_YearSetup.aspx"><span>Annual setup</span><strong>Start of Financial Year Setup</strong><p>Set the current reporting year and load the Prior Year Financial Statements and Portfolio Budget Statements.</p><em>Start annual setup &rarr;</em></a>
                <a class="norm-module-card norm-module-card-primary" href="<%= String.IsNullOrWhiteSpace(LatestStatementsUrl) ? "NORM_Statements.aspx" : LatestStatementsUrl %>"><span>Core module</span><strong>Financial statements</strong><p>PRIMA face statements, notes, comparisons, drill-through and publication controls.</p><em>Open statements &rarr;</em></a>
                <a class="norm-module-card" href="<%= String.IsNullOrWhiteSpace(LatestMappingUrl) ? "NORM_Import.aspx" : LatestMappingUrl %>"><span>Controlled export</span><strong>Account mapping</strong><p>Export every trial-balance account, balance, face-statement mapping and note mapping to Excel.</p><em>Download Excel mapping &rarr;</em></a>
                <a class="norm-module-card" href="<%= String.IsNullOrWhiteSpace(LatestStatementsUrl) ? "NORM_Statements.aspx#asset-movement" : LatestStatementsUrl + "#asset-movement" %>"><span>Core module</span><strong>Asset movement table</strong><p>Closing balances and depreciation by PRIMA asset class, with source lineage and controlled register inputs.</p><em>Review movement table &rarr;</em></a>
                <a class="norm-module-card" href="NORM_Reporting.aspx#manual-inputs"><span>Controlled input</span><strong>Cash-flow journals</strong><p>Non-cash adjustments and category reclassifications included in cash-flow drill-through.</p><em>Open journal register &rarr;</em></a>
                <a class="norm-module-card" href="NORM_Reporting.aspx#manual-inputs"><span>Controlled input</span><strong>Manual disclosures</strong><p>Lease maturities, contingencies, asset-register reconciliations and evidence references.</p><em>Open input register &rarr;</em></a>
            </div>
            <div class="norm-phase-two-head"><span>Phase 2</span><p>Separate reporting streams, using the same controlled source and workflow foundation.</p></div>
            <div class="norm-module-grid norm-phase-two-grid">
                <a class="norm-module-card norm-module-card-future" href="NORM_Reporting.aspx#workflow"><span>Phase 2</span><strong>Annual performance statements</strong><p>Performance measures, evidence, assurance and accountable-authority sign-off.</p><em>View workflow &rarr;</em></a>
                <a class="norm-module-card norm-module-card-future" href="NORM_Reporting.aspx#workflow"><span>Phase 2</span><strong>Audit Committee pack</strong><p>Draft statements, material movements, judgements, risks and certification status.</p><em>View workflow &rarr;</em></a>
                <a class="norm-module-card norm-module-card-future" href="NORM_Reporting.aspx#workflow"><span>Phase 2</span><strong>Annual report financial information</strong><p>Outcome tables and finance-linked annual report disclosures from the approved reporting set.</p><em>View workflow &rarr;</em></a>
            </div>
        </section>
        <section class="norm-workspace-grid">
            <div class="norm-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Reporting runs</span><h2>Import and replay history</h2></div></div><%= RunsHtml %></div>
            <aside class="norm-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Configuration</span><h2>Approved releases</h2></div></div><%= ReleasesHtml %></aside>
        </section>
        <section class="norm-workspace-grid norm-workspace-followup">
            <div class="norm-panel"><%= NextStepsHtml %></div>
            <aside class="norm-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Audit trail</span><h2>Who changed what and when</h2></div></div><%= AuditTrailHtml %></aside>
        </section>
        <section class="norm-panel norm-process-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Proof-engine workflow</span><h2>What NORM controls</h2></div></div>
            <div class="norm-process-cards"><article><b>1</b><strong>Immutable import</strong><p>Source file, rows and fingerprints retained.</p></article><article><b>2</b><strong>Entity rules</strong><p>Profile selections drive the required PRIMA content.</p></article><article><b>3</b><strong>Versioned mapping</strong><p>Only approved configuration releases calculate.</p></article><article><b>4</b><strong>Statements and notes</strong><p>Every amount retains its contributing rows and policy context.</p></article><article><b>5</b><strong>Team workflow</strong><p>Preparation and review cover statements, Audit Committee and annual report modules.</p></article><article><b>6</b><strong>Assurance and replay</strong><p>Face and note controls, disclosure completeness and audited comparisons run together.</p></article></div>
        </section>
    </main>
</form>
</body>
</html>
