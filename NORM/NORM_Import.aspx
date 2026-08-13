<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_Import.aspx.cs" Inherits="CPlatform.NORM.NORM_Import" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Import trial balance</title>
    <link rel="stylesheet" href="../css/norm.css" />
    <link rel="stylesheet" href="../css/norm-import.css" />
</head>
<body class="norm-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Preparer workspace</small></span></a>
        <nav class="norm-top-actions"><a href="../Default.aspx">FinHub</a><a href="NORM_Workspace.aspx">Workspace</a><a href="NORM_Statements.aspx">Statements</a><a href="NORM_Help.aspx">Help</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span></nav>
    </header>
    <main class="norm-form-shell">
        <section class="norm-form-intro">
            <span class="norm-kicker">Immutable source capture</span>
            <h1>Import a trial balance</h1>
            <p>NORM validates the source evidence before committing it. Every original file, its SHA-256 fingerprint, reporting periods and accepted statement rows are retained with the calculation evidence.</p>
            <ol class="norm-process-list"><li>Validate files, periods and reporting entity</li><li>Commit the complete source set in one transaction</li><li>Calculate statements and assurance checks</li></ol>
        </section>
        <section class="norm-form-card">
            <asp:Panel ID="ErrorPanel" runat="server" Visible="false" CssClass="norm-alert norm-alert-error">
                <strong>Import not completed</strong><asp:Literal ID="ErrorMessage" runat="server" />
            </asp:Panel>
            <label for="ReleaseList">Approved configuration</label>
            <asp:DropDownList ID="ReleaseList" runat="server" CssClass="norm-input" />
            <div id="SingleFileImport">
                <label for="SourceTypeList">Source format</label>
                <asp:DropDownList ID="SourceTypeList" runat="server" CssClass="norm-input">
                    <asp:ListItem Value="ERP">ERP trial balance (.xlsx)</asp:ListItem>
                    <asp:ListItem Value="ROMAN">Historical ROMAN trial balance (.txt)</asp:ListItem>
                </asp:DropDownList>
                <label for="TrialBalanceFile">Authoritative trial balance file</label>
                <div class="norm-file-box"><asp:FileUpload ID="TrialBalanceFile" runat="server" CssClass="norm-file-input" /><small>One complete, frozen year-end trial balance. Maximum 100 MB. The original is retained with its fingerprint and is not served by IIS.</small></div>
                <p class="norm-transition-method"><strong>One source of truth:</strong> each calculation run uses one complete trial balance. Load a new version to create a separately traceable run and movement comparison.</p>
            </div>
            <div class="norm-form-actions"><a href="NORM_Workspace.aspx">Cancel</a><asp:Button ID="ImportButton" runat="server" Text="Import and calculate" CssClass="norm-button" OnClick="ImportButton_Click" /></div>
        </section>
    </main>
</form>
</body>
</html>
