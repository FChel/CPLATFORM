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
            <a href="NORM_Statements.aspx<%= SelectedRunId > 0 ? "?run=" + SelectedRunId : "" %>">Financial statements</a>
            <% if (NarrativeMode) { %><a href="NORM_Mapping.aspx?release=<%= SelectedReleaseId %>">Mapping release</a><% } %>
            <a href="NORM_Help.aspx">Help</a>
        </nav>
    </header>

    <main class="norm-reporting-shell">
        <% if (NarrativeMode) { %>
        <section class="norm-reporting-panel norm-narrative-panel">
            <header><div><h1>Disclosures and accounting policies</h1><p><%= Server.HtmlEncode(RunLabel) %></p></div></header>
            <% if (!PlatformInstalled) { %>
            <p>Narrative blocks are unavailable until the approved migration is installed.</p>
            <% } else { %>
            <div class="norm-narrative-layout">
                <aside class="norm-narrative-index" aria-label="Narrative blocks">
                    <h2>Content blocks</h2>
                    <% if (NarrativeCanEdit) { %>
                    <button type="button" class="norm-button norm-button-small norm-button-quiet" id="newNarrative">New block</button>
                    <% } %>
                    <ul id="narrativeList"></ul>
                </aside>
                <div>
                    <p id="narrativeNotice" class="norm-narrative-notice" role="status"><%= Server.HtmlEncode(NarrativeNotice) %></p>
                    <% if (!NarrativeCanEdit) { %><p>This approved configuration is read-only.</p><% } %>
                    <div class="norm-profile-grid norm-narrative-fields">
                        <label class="norm-narrative-destination"><span>Destination</span><select name="blockDestination" id="blockDestination"></select></label>
                        <label><span>Content type</span><select name="blockType" id="blockType">
                            <option value="Disclosure">Narrative disclosure</option><option value="AccountingPolicy">Accounting policy</option>
                            <option value="EntityOverview">Entity overview</option><option value="SectionIntroduction">Section introduction</option>
                        </select></label>
                        <label><span>Position</span><select name="blockPlacement" id="blockPlacement">
                            <option value="Before">Before content</option><option value="After">After content</option>
                        </select></label>
                        <label><span>Sequence</span><input name="blockSequence" id="blockSequence" type="number" min="1" max="999" value="1" /></label>
                        <label><span>Preparation status</span><select name="blockStatus" id="blockStatus">
                            <option value="Draft">Draft</option><option value="Prepared">Prepared</option>
                        </select></label>
                    </div>
                    <p id="blockReview" class="norm-muted">Awaiting review</p>
                    <% if (NarrativeCanEdit) { %>
                    <div class="norm-narrative-toolbar" role="toolbar" aria-label="Text formatting">
                        <button type="button" data-format="bold"><strong>Bold</strong></button>
                        <button type="button" data-format="italic"><em>Italic</em></button>
                        <button type="button" data-format="underline"><u>Underline</u></button>
                        <button type="button" data-format="formatBlock" data-value="h3">Subheading</button>
                        <button type="button" data-format="formatBlock" data-value="p">Paragraph</button>
                        <button type="button" data-format="insertUnorderedList">Bullet list</button>
                        <button type="button" data-format="insertOrderedList">Numbered list</button>
                        <button type="button" data-format="superscript">Superscript</button>
                        <button type="button" data-format="subscript">Subscript</button>
                    </div>
                    <% } %>
                    <div id="blockEditor" class="norm-narrative-content" role="textbox" aria-multiline="true" aria-label="Narrative content"
                        contenteditable="<%= NarrativeCanEdit ? "true" : "false" %>"></div>
                    <input type="hidden" name="blockId" id="blockId" value="0" />
                    <input type="hidden" name="blockEditToken" id="blockEditToken" />
                    <input type="hidden" name="blockHtml" id="blockHtml" />
                    <input type="hidden" name="narrativeToken" value="<%= Server.HtmlEncode(NarrativeToken) %>" />
                    <% if (NarrativeCanEdit) { %>
                    <div class="norm-narrative-actions">
                        <asp:Button ID="SaveNarrativeBlock" runat="server" Text="Save block" CssClass="norm-button"
                            OnClick="SaveNarrativeBlock_Click" OnClientClick="if (!window.normSubmitNarrative()) return false;" />
                        <button type="button" class="norm-button norm-button-quiet" id="cancelNarrative">Cancel</button>
                    </div>
                    <% } %>
                </div>
            </div>
            <% if (NarrativeCanEdit) { %>
            <section class="norm-narrative-rollover" id="narrativeRollover" hidden>
                <h2>Carry content forward</h2>
                <label for="narrativeSource">Preceding-year release</label>
                <select id="narrativeSource" name="narrativeSource"></select>
                <asp:Button ID="RollNarratives" runat="server" Text="Carry forward blocks" CssClass="norm-button norm-button-quiet"
                    OnClick="RollNarrativeBlocks_Click" OnClientClick="if (!window.normConfirmRollover()) return false;" />
            </section>
            <% } %>
            <script id="narrativeEditorData" type="application/json"><%= NarrativeEditorData %></script>
            <script src="../js/norm-narrative.js" defer></script>
            <% } %>
        </section>
        <% } else { %>
        <section class="norm-reporting-hero">
            <div>
                <span class="norm-kicker">Commonwealth reporting framework</span>
                <h1>Configure once. Generate every required statement, note and review module.</h1>
                <p>The entity profile drives the PRIMA disclosure register. Figures remain linked to the frozen trial-balance lineage; policy wording and workflow evidence remain editable for the reporting team.</p>
                <div class="norm-reporting-hero-actions"><a class="norm-button" href="NORM_Mapping.aspx?intent=import">Import mapping</a><span>Load or update the account mapping used by the reporting set.</span></div>
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
            <div class="norm-materiality-panel">
                <div class="norm-capability-head"><h3>Materiality configuration</h3><span>All amounts in $'000</span></div>
                <div class="norm-materiality-grid">
                    <label><span>Overall financial materiality</span><input type="number" step="0.001" name="overallMateriality" value="<%= Server.HtmlEncode(OverallMateriality) %>" /></label>
                    <label><span>Performance materiality</span><input type="number" step="0.001" name="performanceMateriality" value="<%= Server.HtmlEncode(PerformanceMateriality) %>" /></label>
                    <label><span>Clearly trivial threshold</span><input type="number" step="0.001" name="clearlyTrivialThreshold" value="<%= Server.HtmlEncode(ClearlyTrivialThreshold) %>" /></label>
                    <label><span>Budget variance commentary threshold</span><input type="number" step="0.001" name="budgetVarianceThreshold" value="<%= Server.HtmlEncode(BudgetVarianceThreshold) %>" /></label>
                </div>
                <label class="norm-field-wide"><span>Materiality basis</span><textarea name="materialityBasis" rows="2"><%= Server.HtmlEncode(MaterialityBasis) %></textarea></label>
                <label class="norm-field-wide"><span>Qualitative materiality considerations</span><textarea name="qualitativeConsiderations" rows="3"><%= Server.HtmlEncode(QualitativeConsiderations) %></textarea><small>NORM flags potentially immaterial disclosures for assessment; it never removes them automatically.</small></label>
            </div>
            <div class="norm-capability-head"><h3>Entity activities and balances</h3><span>Select everything that applies</span></div>
            <div class="norm-capability-grid"><%= CapabilityHtml %></div>
        </section>

        <section class="norm-reporting-panel norm-disclosure-panel" id="disclosure-register">
            <details class="norm-disclosure-collapsible">
                <summary>
                    <div><span class="norm-step">02</span><h2>PRIMA disclosure register</h2></div>
                    <span class="norm-disclosure-summary-copy">Required, conditional and not-applicable items are visible in one controlled register.</span>
                    <span class="norm-disclosure-toggle" aria-hidden="true"></span>
                </summary>
                <div class="norm-disclosure-content">
                    <div class="norm-disclosure-summary">
                        <article><strong><%= RequiredCount %></strong><span>Required</span></article>
                        <article><strong><%= CompleteCount %></strong><span>Passed all checks</span></article>
                        <article><strong><%= NeedsInputCount %></strong><span>Needs input</span></article>
                        <article><strong><%= NotApplicableCount %></strong><span>Not applicable</span></article>
                    </div>
                    <div class="norm-disclosure-register"><%= DisclosureHtml %></div>
                    <%= DisclosureChecksHtml %>
                </div>
            </details>
        </section>

        <section class="norm-reporting-panel" id="policies">
            <header><div><span class="norm-step">03</span><h2>Accounting policies and note commentary</h2></div><p>Templates are starting points. Entity wording is saved against the immutable calculation run and carried into the Word export.</p></header>
            <div class="norm-policy-list"><%= NarrativeHtml %></div>
        </section>

        <section class="norm-reporting-panel" id="manual-inputs">
            <header><div><span class="norm-step">04</span><h2>Budget, comparatives and controlled inputs</h2></div><p>Bring approved budget evidence, comparative figures, workbook-derived disclosures and cash-flow journals together without changing the frozen trial balance.</p></header>
            <div class="norm-control-module-head"><div><span class="norm-kicker">Original Budget</span><h3>Approved budget figures and source evidence</h3></div><p>Load from the entity's approved budget extract or enter controlled figures here. Every value carries its source reference and preparation status.</p></div>
            <%= BudgetFigureHtml %>
            <aside class="norm-ai-roadmap"><span>Phase 2 · human-reviewed AI assistance</span><strong>Budget variance commentary</strong><p>Use actual-to-budget movements and drill-through evidence to prepare a first draft, with the financial statements team retaining authorship, verification and approval.</p></aside>
            <div class="norm-control-module-head norm-control-module-head-spaced"><div><span class="norm-kicker">Manual disclosure register</span><h3>Schedules that need accounting input</h3></div><p>Includes lease maturity analysis, contingencies, asset-register reconciliation and budget variance commentary.</p></div>
            <%= ManualInputHtml %>
            <div class="norm-control-module-head norm-control-module-head-spaced"><div><span class="norm-kicker">Cash-flow journals</span><h3>Non-cash adjustments and classification journals</h3></div><p>Approved journals are included in cash-flow category balances and figure drill-through.</p></div>
            <%= CashFlowJournalHtml %>
        </section>

        <section class="norm-reporting-panel" id="workflow">
            <header><div><span class="norm-step">05</span><h2>Team workflow</h2></div><p>Assign preparation and review across financial statements, annual performance statements, Audit Committee and annual report modules.</p></header>
            <div class="norm-workflow-table"><%= WorkflowHtml %></div>
        </section>

        <div class="norm-reporting-savebar">
            <div><strong>Run-specific working content</strong><span>Profile selections apply to FY configuration; narratives and workflow are retained against this calculation run.</span><label class="norm-change-reason"><span>Reason for change</span><input name="changeReason" placeholder="What changed and why?" /></label></div>
            <asp:Button ID="SaveButton" runat="server" Text="Save reporting workspace" CssClass="norm-button norm-button-dark" OnClick="Save_Click" />
        </div>
        <% } %>
        <% } %>
    </main>
</form>
</body>
</html>
