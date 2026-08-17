<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_YearSetup.aspx.cs" Inherits="CPlatform.NORM.NORM_YearSetup" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Start of Financial Year Setup</title>
    <link rel="stylesheet" href="../css/norm.css?v=20260817-3" />
</head>
<body class="norm-page">
<form id="form1" runat="server" enctype="multipart/form-data">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Start of Financial Year Setup</small></span></a>
        <nav class="norm-top-actions"><a href="../Default.aspx">FinHub</a><a href="NORM_Workspace.aspx">Control Centre</a><a href="NORM_Statements.aspx">Financial statements</a><a href="NORM_Help.aspx">Help</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span><span class="norm-user"><%= Server.HtmlEncode(CurrentUser) %></span></nav>
    </header>

    <main class="norm-year-setup-shell">
        <section class="norm-year-setup-hero">
            <div><span class="norm-kicker">NORM Control Centre</span><h1>Start of Financial Year Setup</h1><p>Set the reporting year once, then load the authoritative comparative and Original Budget documents. NORM scans the complete document for financial-statement headings and maps high-confidence figures into the controlled reporting set.</p></div>
            <aside><span>Reporting period</span><strong>FY<%= Server.HtmlEncode(CurrentYearDisplay) %></strong><small>Comparative year: <%= Server.HtmlEncode(PriorYearDisplay) %></small></aside>
        </section>

        <asp:Panel ID="MessagePanel" runat="server" CssClass="norm-alert norm-alert-success" Visible="false"><asp:Literal ID="MessageText" runat="server" /></asp:Panel>
        <asp:Panel ID="ErrorPanel" runat="server" CssClass="norm-alert norm-alert-error" Visible="false"><strong>Setup could not be saved</strong><asp:Literal ID="ErrorText" runat="server" /></asp:Panel>
        <asp:Panel ID="InstallPanel" runat="server" CssClass="norm-reporting-install" Visible="false"><strong>Start-of-year database objects are not installed.</strong><p>Run <code>sql/NORM_08_StartOfFinancialYearSetup.sql</code>, then reload this page.</p></asp:Panel>

        <section class="norm-year-setup-panel">
            <header><div><span class="norm-step">01</span><div><span class="norm-kicker">Reporting period</span><h2>Set the current financial year</h2></div></div><p>This year drives statement headings. The comparative year is calculated automatically as current year minus one.</p></header>
            <div class="norm-year-field-row">
                <label for="CurrentFinancialYear"><span>Current financial year</span><asp:TextBox ID="CurrentFinancialYear" runat="server" CssClass="norm-input" MaxLength="4" inputmode="numeric" pattern="[0-9]{4}" required="required" title="Enter exactly four digits, for example 2025." placeholder="2025" /></label>
                <div class="norm-year-preview"><span>Previous financial year</span><strong id="priorYearPreview"><%= Server.HtmlEncode(PriorYearDisplay) %></strong><small>Calculated automatically</small></div>
                <asp:Button ID="SaveYearButton" runat="server" Text="Save financial year" CssClass="norm-button" OnClick="SaveYearButton_Click" />
            </div>
        </section>

        <section class="norm-year-setup-panel">
            <header><div><span class="norm-step">02</span><div><span class="norm-kicker">Controlled source documents</span><h2>Load comparatives and Original Budget</h2></div></div><p>NORM searches the full PDF, Word document or Excel workbook. A page or sheet is requested only if the document cannot be confidently located or read.</p></header>
            <div class="norm-year-upload-grid">
                <article class="norm-year-upload-card">
                    <div class="norm-year-upload-icon">PY</div><span class="norm-kicker">Comparative figures</span><h3>Prior Year Financial Statements</h3><p>Loads the audited current-year column from the prior-year statements into this year's comparative column.</p>
                    <div class="norm-file-box"><asp:FileUpload ID="PriorYearFile" runat="server" CssClass="norm-file-input" accept=".pdf,.doc,.docx,.xls,.xlsx" /><small>PDF, Word or Excel · maximum 100 MB</small></div>
                    <asp:Button ID="UploadPriorButton" runat="server" Text="Upload prior-year statements" CssClass="norm-button norm-button-dark" OnClick="UploadPriorButton_Click" CausesValidation="false" />
                    <%= PriorDocumentHtml %>
                </article>
                <article class="norm-year-upload-card">
                    <div class="norm-year-upload-icon">OB</div><span class="norm-kicker">Original Budget</span><h3>Portfolio Budget Statements</h3><p>Loads the approved budget column into the Original Budget column used throughout the financial statements.</p>
                    <div class="norm-file-box"><asp:FileUpload ID="BudgetFile" runat="server" CssClass="norm-file-input" accept=".pdf,.doc,.docx,.xls,.xlsx" /><small>PDF, Word or Excel · maximum 100 MB</small></div>
                    <asp:Button ID="UploadBudgetButton" runat="server" Text="Upload Portfolio Budget Statements" CssClass="norm-button norm-button-dark" OnClick="UploadBudgetButton_Click" CausesValidation="false" />
                    <%= BudgetDocumentHtml %>
                </article>
            </div>
        </section>

        <section class="norm-year-setup-panel">
            <header><div><span class="norm-step">03</span><div><span class="norm-kicker">Extraction assurance</span><h2>Detected statement figures</h2></div></div><p>Only high-confidence label and column matches are applied automatically. The source document, locator and confidence are retained for review.</p></header>
            <%= FigurePreviewHtml %>
        </section>
    </main>
    <script>
    (function(){var input=document.getElementById('<%= CurrentFinancialYear.ClientID %>'),preview=document.getElementById('priorYearPreview');if(!input||!preview)return;input.addEventListener('input',function(){preview.textContent=/^[0-9]{4}$/.test(input.value)?String(Number(input.value)-1):'—';});})();
    </script>
</form>
</body>
</html>
