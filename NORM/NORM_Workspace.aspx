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
        <section class="norm-workspace-grid">
            <div class="norm-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Reporting runs</span><h2>Import and replay history</h2></div></div><%= RunsHtml %></div>
            <aside class="norm-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Configuration</span><h2>Approved releases</h2></div></div><%= ReleasesHtml %></aside>
        </section>
        <section class="norm-panel norm-process-panel"><div class="norm-panel-head"><div><span class="norm-kicker">Proof-engine workflow</span><h2>What NORM controls</h2></div></div>
            <div class="norm-process-cards"><article><b>1</b><strong>Immutable import</strong><p>Source file, rows and fingerprints retained.</p></article><article><b>2</b><strong>Entity rules</strong><p>Profile selections drive the required PRIMA content.</p></article><article><b>3</b><strong>Versioned mapping</strong><p>Only approved configuration releases calculate.</p></article><article><b>4</b><strong>Statements and notes</strong><p>Every amount retains its contributing rows and policy context.</p></article><article><b>5</b><strong>Team workflow</strong><p>Preparation and review cover statements, Audit Committee and annual report modules.</p></article><article><b>6</b><strong>Assurance and replay</strong><p>Face and note controls, disclosure completeness and audited comparisons run together.</p></article></div>
        </section>
    </main>
</form>
</body>
</html>
