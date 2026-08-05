<%@ Page Language="C#" AutoEventWireup="true" ValidateRequest="false" CodeFile="NORM_Reporting.aspx.cs" Inherits="CPlatform.NORM.NORM_Reporting" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Reporting framework</title>
    <link rel="stylesheet" href="../css/norm.css" />
</head>
<body class="norm-page norm-reporting-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Government reporting platform</small></span></a>
        <nav class="norm-top-actions" aria-label="NORM actions">
            <span class="norm-chip"><%= Server.HtmlEncode(RunLabel) %></span>
            <span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span>
            <a href="../Default.aspx">FinHub</a>
            <a href="NORM_Workspace.aspx">Preparer workspace</a>
            <a href="NORM_Statements.aspx?run=<%= SelectedRunId %>">Financial statements</a>
            <a href="NORM_Help.aspx">Help</a>
        </nav>
    </header>

    <main class="norm-reporting-shell">
        <section class="norm-reporting-hero">
            <div>
                <span class="norm-kicker">Commonwealth reporting framework</span>
                <h1>Configure once. Generate every required statement, note and review module.</h1>
                <p>The entity profile drives the PRIMA disclosure register. Figures remain linked to the frozen trial-balance lineage; policy wording and workflow evidence remain editable for the reporting team.</p>
            </div>
            <aside>
                <span>Required disclosures</span><strong><%= RequiredCount %></strong>
                <small><%= Server.HtmlEncode(ReadinessLabel) %></small>
            </aside>
        </section>

        <% if (!PlatformInstalled) { %>
        <section class="norm-reporting-install">
            <strong>Reporting platform database objects are not installed.</strong>
            <p>Run <code>src/sql/NORM_04_GovernmentReportingPlatform.sql</code>, then reload this page.</p>
        </section>
        <% } else { %>

        <section class="norm-reporting-panel" id="entity-profile">
            <header><div><span class="norm-step">01</span><h2>Entity reporting profile</h2></div><p>This is the rules input. It decides which PRIMA schedules and notes appear.</p></header>
            <div class="norm-profile-grid">
                <label><span>Entity type</span><select name="entityType"><%= EntityTypeOptions %></select></label>
                <label><span>Reporting basis</span><select name="reportingBasis"><%= ReportingBasisOptions %></select></label>
                <label><span>Disclosure set</span><select name="disclosureTier"><%= DisclosureTierOptions %></select></label>
            </div>
            <label class="norm-field-wide"><span>Materiality basis</span><textarea name="materialityBasis" rows="3"><%= Server.HtmlEncode(MaterialityBasis) %></textarea><small>Record both quantitative thresholds and qualitative considerations. This becomes part of the preparation evidence.</small></label>
            <div class="norm-capability-head"><h3>Entity activities and balances</h3><span>Select everything that applies</span></div>
            <div class="norm-capability-grid"><%= CapabilityHtml %></div>
        </section>

        <section class="norm-reporting-panel" id="disclosure-register">
            <header><div><span class="norm-step">02</span><h2>PRIMA disclosure register</h2></div><p>Required, conditional and not-applicable items are visible in one controlled register.</p></header>
            <div class="norm-disclosure-summary">
                <article><strong><%= RequiredCount %></strong><span>Required</span></article>
                <article><strong><%= GeneratedCount %></strong><span>Generated or drafted</span></article>
                <article><strong><%= NeedsInputCount %></strong><span>Needs input</span></article>
                <article><strong><%= NotApplicableCount %></strong><span>Not applicable</span></article>
            </div>
            <div class="norm-disclosure-register"><%= DisclosureHtml %></div>
        </section>

        <section class="norm-reporting-panel" id="policies">
            <header><div><span class="norm-step">03</span><h2>Accounting policies and note commentary</h2></div><p>Templates are starting points. Entity wording is saved against the immutable calculation run and carried into the Word export.</p></header>
            <div class="norm-policy-list"><%= NarrativeHtml %></div>
        </section>

        <section class="norm-reporting-panel" id="workflow">
            <header><div><span class="norm-step">04</span><h2>Team workflow</h2></div><p>Assign preparation and review across financial statements, Audit Committee and annual report modules.</p></header>
            <div class="norm-workflow-table"><%= WorkflowHtml %></div>
        </section>

        <div class="norm-reporting-savebar">
            <div><strong>Run-specific working content</strong><span>Profile selections apply to FY configuration; narratives and workflow are retained against this calculation run.</span></div>
            <asp:Button ID="SaveButton" runat="server" Text="Save reporting workspace" CssClass="norm-button norm-button-dark" OnClick="Save_Click" />
        </div>
        <% } %>
    </main>
</form>
</body>
</html>
