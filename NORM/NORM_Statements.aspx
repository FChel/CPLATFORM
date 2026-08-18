<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_Statements.aspx.cs" Inherits="CPlatform.NORM.NORM_Statements" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Financial statements</title>
    <link rel="stylesheet" href="../css/norm.css?v=20260818-3" />
</head>
<body class="norm-page norm-statements-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Statements.aspx" aria-label="NORM statements home">
            <span class="norm-brand-mark">N</span>
            <span><strong>NORM</strong><small>Notes, Output, Reporting and Mapping</small></span>
        </a>
        <nav class="norm-top-actions" aria-label="NORM actions">
            <span class="norm-chip" id="metaRelease">Configuration</span>
            <span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span>
            <a href="../Default.aspx">FinHub</a>
            <a href="NORM_Help.aspx">Help</a>
            <% if (CanPrepare) { %>
                <a href="NORM_Workspace.aspx">Preparer workspace</a>
                <a href="NORM_Reporting.aspx?run=<%= selectedRunId %>">Reporting framework</a>
                <a class="norm-button norm-button-small" href="NORM_Import.aspx">Import trial balance</a>
            <% } %>
            <button type="button" id="printStatements" class="norm-button norm-button-small norm-button-quiet">Print</button>
            <a id="excelExportLink" class="norm-button norm-button-small norm-button-quiet" href="#" hidden>Excel statements</a>
            <a id="wordExportLink" class="norm-button norm-button-small norm-button-quiet" href="#" hidden>Editable Word</a>
            <a id="reviewPackLink" class="norm-button norm-button-small norm-review-pack-button" href="#" hidden>Download review pack</a>
        </nav>
    </header>

    <main id="normApp" class="norm-statement-shell" aria-live="polite">
        <aside class="norm-rail" aria-label="Statement set">
            <div class="norm-rail-heading">Statement set</div>
            <div id="statementNav" class="norm-statement-nav"></div>
            <div class="norm-rail-heading norm-rail-heading-spaced">Reporting profile</div>
            <div id="profileSummary" class="norm-profile-summary"></div>
            <div class="norm-rail-heading norm-rail-heading-spaced">Evidence</div>
            <button type="button" id="unmappedButton" class="norm-unmapped-button"></button>
            <div class="norm-run-stamp">
                <span>Calculation run</span>
                <strong id="metaRun">-</strong>
                <small id="metaFingerprint">-</small>
            </div>
        </aside>

        <section class="norm-document-wrap">
            <div class="norm-viewbar" role="group" aria-label="Statement view">
                <div><strong>Statement view</strong><span>Preparation evidence or clean publication presentation</span></div>
                <div><button type="button" id="preparationView" class="active">Preparation</button><button type="button" id="publicationView">Publication</button></div>
            </div>
            <div class="norm-status-legend"><span><i class="tied"></i>Tied to published baseline</span><span><i class="close"></i>Within tolerance</span><span><i class="variance"></i>Review variance</span><span><i class="mapped"></i>Mapped TB result</span></div>
            <div id="testBanner" class="norm-test-banner" hidden>
                <strong>Deliberate test break</strong>
                <span>This is a child copy created to prove that NORM catches an out-of-balance trial balance.</span>
            </div>
            <div id="runReadiness" class="norm-readiness" hidden></div>
            <article id="statementDocument" class="norm-document"></article>
        </section>

        <aside class="norm-assurance" aria-label="Assurance checks">
            <div class="norm-assurance-head">
                <span>Assurance</span>
                <strong id="validationScore">-</strong>
            </div>
            <div id="validationList" class="norm-validation-list"></div>
            <div id="comparisonSummary" class="norm-comparison-summary"></div>
            <div id="disclosureProgress" class="norm-disclosure-progress"></div>
            <div class="norm-coverage">
                <div><span>Mapped by value</span><strong id="coverageValue">-</strong></div>
                <div class="norm-meter"><i id="coverageBar"></i></div>
            </div>
            <% if (CanPrepare) { %>
            <div class="norm-proof-card">
                <span class="norm-kicker">Proof control</span>
                <strong>Run a genuine break test</strong>
                <p>Creates an immutable child import with a $48,250 imbalance and recalculates every check.</p>
                <asp:Button ID="CreateBreakButton" runat="server" Text="Create test break" CssClass="norm-button norm-button-dark" OnClick="CreateBreak_Click" />
            </div>
            <% } %>
        </aside>
    </main>

    <div id="traceScrim" class="norm-scrim" hidden></div>
    <aside id="traceDrawer" class="norm-trace" aria-hidden="true" aria-label="Figure derivation"></aside>
    <section id="printReview" class="norm-print-review" role="dialog" aria-modal="true" aria-labelledby="printReviewTitle" hidden>
        <div>
            <span class="norm-kicker">Controlled publication check</span>
            <h2 id="printReviewTitle">Review before printing</h2>
            <p id="printReviewSummary"></p>
            <div id="printReviewIssues" class="norm-print-review-issues"></div>
            <div class="norm-print-review-actions">
                <button type="button" id="cancelPrint" class="norm-button norm-button-quiet">Return to assurance</button>
                <button type="button" id="confirmPrint" class="norm-button">Print controlled draft</button>
            </div>
        </div>
    </section>
    <div id="printBook" class="norm-print-book" aria-hidden="true"></div>

    <script>window.NORM_DATA = <%= NormDataJson %>;</script>
    <script src="../js/norm.js?v=20260818-3"></script>
</form>
</body>
</html>
