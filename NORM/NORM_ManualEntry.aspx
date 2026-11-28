<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_ManualEntry.aspx.cs" Inherits="CPlatform.NORM.NORM_ManualEntry" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Manual Entry</title>
    <link rel="stylesheet" href="../css/norm.css" />
    <style>
        .norm-manual-panel { max-width: 1050px; margin-inline: auto; }
        .norm-manual-table { table-layout: fixed; }
        .norm-manual-table thead th:not(:first-child), .norm-manual-table td { width: 30%; text-align: left; }
        .norm-manual-table th, .norm-manual-table td { font-size: 13px; padding: 8px; vertical-align: top; }
        .norm-manual-table td small { display: block; margin-top: 4px; color: #505b65; font-weight: 400; overflow-wrap: anywhere; }
        .norm-manual-table input + input { margin-top: 4px; }
        .norm-manual-panel input.norm-button-dark { width: auto; background: #17202a; }
        .norm-manual-panel input.norm-button-dark:hover { background: #384653; }
        .norm-manual-panel details { margin-top: 12px; }
    </style>
</head>
<body class="norm-page norm-reporting-page">
<form id="form1" runat="server">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Government reporting platform</small></span></a>
        <nav class="norm-top-actions" aria-label="NORM actions">
            <a href="NORM_Workspace.aspx">Preparer workspace</a>
            <a href="NORM_Reporting.aspx">Reporting framework</a>
            <a href="<%= Enc(StatementUrl) %>">Draft financial statements</a>
            <span class="norm-chip norm-env"><%= Enc(CurrentEnvironment) %></span>
        </nav>
    </header>
    <main class="norm-reporting-shell">
        <section class="norm-reporting-panel norm-manual-panel">
            <header><div><h1>Manual Entry</h1><p><%= Enc(ContextLabel) %></p></div></header>
            <%= SelectorHtml %>
            <% if (!String.IsNullOrEmpty(Notice)) { %><p role="status"><%= Enc(Notice) %></p><% } %>
            <%= ScheduleHtml %>
            <asp:PlaceHolder ID="EditControls" Visible="false" runat="server">
                <input type="hidden" name="manualToken" value="<%= Enc(FormToken) %>" />
                <input type="hidden" name="manualContext" value="<%= Enc(ScheduleToken) %>" />
                <p><label for="manualReason">Change reason</label><br />
                    <small id="manualReasonHelp">Required when you change or clear a saved entry or add or change a comparative amount or reference, including the first comparative entry.</small><br />
                    <input id="manualReason" name="manualReason" aria-describedby="manualReasonHelp" maxlength="500" size="55" value="<%= Enc(Request.Form["manualReason"]) %>" /></p>
                <asp:Button ID="SaveSchedule" runat="server" Text="Save schedule and record evidence" CssClass="norm-button norm-button-dark" OnClick="SaveSchedule_Click" />
            </asp:PlaceHolder>
            <%= EvidenceHtml %>
        </section>
    </main>
</form>
</body>
</html>
