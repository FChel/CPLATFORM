<%@ Page Language="C#" AutoEventWireup="true" EnableSessionState="true" EnableViewStateMac="true" EnableEventValidation="true" CodeFile="NORM_Import.aspx.cs" Inherits="CPlatform.NORM.NORM_Import" %>
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
            <ol class="norm-process-list"><li>Upload and inspect the source</li><li>Review validation and confirm acceptance</li><li>Review calculation results</li></ol>
        </section>
        <section class="norm-form-card">
            <asp:Panel ID="ErrorPanel" runat="server" EnableViewState="false" Visible="false" CssClass="norm-alert norm-alert-error">
                <strong>Source not accepted</strong><asp:Literal ID="ErrorMessage" runat="server" />
            </asp:Panel>
            <asp:Panel ID="ResultPanel" runat="server" EnableViewState="false" Visible="false" CssClass="norm-alert"><asp:Literal ID="ResultMessage" runat="server" /></asp:Panel>
            <label for="ReleaseList">Approved configuration</label>
            <asp:DropDownList ID="ReleaseList" runat="server" CssClass="norm-input" />
            <div id="SingleFileImport">
                <label for="TrialBalanceFile">Authoritative trial balance file</label>
                <div class="norm-file-box"><asp:FileUpload ID="TrialBalanceFile" runat="server" CssClass="norm-file-input" /><small>One trial balance file. NORM identifies its layout and reporting periods from the contents. Maximum 100 MB.</small></div>
                <p class="norm-transition-method">Existing company/year sources cannot be replaced through this page.</p>
            </div>
            <div class="norm-form-actions"><asp:Button ID="CancelButton" runat="server" Text="Cancel preview" OnClick="CancelButton_Click" /><asp:Button ID="ImportButton" runat="server" Text="Inspect file" CssClass="norm-button" OnClick="ImportButton_Click" /></div>
            <asp:Panel ID="PreviewPanel" runat="server" EnableViewState="false" Visible="false">
                <h2>Review before acceptance</h2>
                <asp:Literal ID="PreviewMessage" runat="server" />
                <asp:Button ID="ConfirmButton" runat="server" Text="Confirm acceptance" CssClass="norm-button" OnClick="ConfirmButton_Click" />
            </asp:Panel>
        </section>
    </main>
</form>
</body>
</html>
