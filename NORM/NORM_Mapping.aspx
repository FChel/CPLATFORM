<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_Mapping.aspx.cs" Inherits="CPlatform.NORM.NORM_Mapping" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" /><meta name="viewport" content="width=device-width,initial-scale=1" />
    <title>NORM - Edit mapping workbook</title>
    <link rel="stylesheet" href="../css/norm.css" /><link rel="stylesheet" href="../css/norm-mapping.css" /><link rel="stylesheet" href="../css/norm-mapping-simplified.css" />
</head>
<body class="norm-page">
<form id="form1" runat="server" enctype="multipart/form-data">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Mapping control</small></span></a>
        <nav class="norm-top-actions"><a href="NORM_Workspace.aspx">Control Centre</a><a href="NORM_Import.aspx">Import TB</a><a href="NORM_Statements.aspx">Statements</a><a href="NORM_Help.aspx">Help</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span><span class="norm-user"><%= Server.HtmlEncode(CurrentUser) %></span></nav>
    </header>
    <main class="norm-mapping-shell">
        <section class="norm-mapping-hero">
            <div><span class="norm-kicker">Account mapping</span><h1>Edit mapping workbook</h1><p>Download the mapping for the latest retained trial balance, complete any changes in Excel and upload it for controlled validation.</p></div>
            <aside class="norm-unmapped-callout"><strong><%= UnmappedAccountCount %></strong><span><%= UnmappedAccountLabel %></span><small>Based on unique accounts in the latest retained trial balance.</small></aside>
        </section>
        <asp:Panel ID="MessagePanel" runat="server" Visible="false" CssClass="norm-alert"><strong><asp:Literal ID="MessageTitle" runat="server" /></strong><asp:Literal ID="MessageText" runat="server" /></asp:Panel>

        <asp:Panel ID="WorkflowPanel" runat="server" Visible="false">
            <section class="norm-mapping-card norm-mapping-workbook">
                <header><div><span class="norm-kicker">Working workbook</span><h2>Edit mapping workbook</h2><p>All accounts from the latest trial balance are included. NORM maintains the controlled working version automatically.</p></div><asp:HyperLink ID="DownloadWorkbookLink" runat="server" CssClass="norm-button norm-button-dark">Download editable workbook</asp:HyperLink></header>
                <div class="norm-release-summary norm-release-summary-compact"><%= ReleaseSummaryHtml %></div>
                <div class="norm-mapping-guidance"><strong>Edit only the blue workbook columns</strong><span>Account type</span><span>Face statement line code</span><span>Note sub-line</span><span>Cash-flow class</span><span>Change reason</span></div>
                <asp:Panel ID="UploadPanel" runat="server">
                    <div class="norm-upload-row"><div><label for="MappingFile">Upload completed mapping workbook</label><asp:FileUpload ID="MappingFile" runat="server" CssClass="norm-file-input" accept=".xlsx" /><small>The full workbook is validated before any account is changed. Invalid or incomplete workbooks are rejected as a whole.</small></div><asp:Button ID="ApplyWorkbookButton" runat="server" Text="Upload and validate workbook" CssClass="norm-button" OnClick="ApplyWorkbookButton_Click" /></div>
                </asp:Panel>
            </section>

            <section class="norm-mapping-card">
                <header><div><span class="norm-kicker">Validation</span><h2>Validation and financial impact</h2></div><span class="norm-chip"><%= ValidationStatus %></span></header>
                <%= ValidationHtml %>
                <div class="norm-impact-table"><%= ImpactHtml %></div>
            </section>

            <section class="norm-mapping-grid">
                <article class="norm-mapping-card">
                    <span class="norm-kicker">Controlled approval</span><h2>Review and approve</h2><p>Approval locks the mapping content and records the reviewer, approver, timestamp and content fingerprint.</p>
                    <asp:Panel ID="ApprovalPanel" runat="server"><asp:CheckBox ID="WarningsAcknowledged" runat="server" Text=" I have reviewed and acknowledge the listed warnings" /><div class="norm-form-actions"><asp:Button ID="ApproveButton" runat="server" Text="Approve immutable release" CssClass="norm-button" OnClick="ApproveButton_Click" /></div></asp:Panel>
                    <asp:Literal ID="ApprovalNote" runat="server" />
                </article>
                <article class="norm-mapping-card">
                    <span class="norm-kicker">Apply approved mapping</span><h2>Recalculate the retained TB</h2><p>NORM copies the latest retained trial balance without changing it and creates a new calculation run under the approved mapping version.</p>
                    <asp:Panel ID="RecalculatePanel" runat="server"><asp:Button ID="RecalculateButton" runat="server" Text="Recalculate latest trial balance" CssClass="norm-button norm-button-dark" OnClick="RecalculateButton_Click" /></asp:Panel>
                    <asp:Literal ID="RecalculateNote" runat="server" />
                </article>
            </section>

            <section class="norm-mapping-card"><header><div><span class="norm-kicker">Audit evidence</span><h2>Who changed what, when and why</h2></div></header><%= AuditHtml %></section>
        </asp:Panel>
    </main>
</form>
</body></html>
