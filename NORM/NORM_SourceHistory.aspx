<%@ Page Language="C#" AutoEventWireup="true" EnableSessionState="false" CodeFile="NORM_SourceHistory.aspx.cs" Inherits="CPlatform.NORM.NORM_SourceHistoryPage" %>
<!doctype html>
<html lang="en-AU">
<head runat="server"><meta charset="utf-8" /><meta name="viewport" content="width=device-width,initial-scale=1" /><title>NORM - Accepted source history</title><link rel="stylesheet" href="../css/norm.css" /></head>
<body class="norm-page">
    <header class="norm-topbar"><a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Preparer workspace</small></span></a><nav class="norm-top-actions"><a href="<%= Server.HtmlEncode(ReturnUrl) %>">Return to import preparation</a><a href="NORM_Workspace.aspx">Workspace</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span></nav></header>
    <main class="norm-workspace">
        <section class="norm-panel"><h1>Accepted source history</h1><p>Select a population to review accepted versions, compare its immediate predecessor and download originals.</p><%= ErrorHtml %>
            <form method="get" action="NORM_SourceHistory.aspx"><%= FiltersHtml %><button type="submit" class="norm-button">Show sources</button></form><%= ContextHtml %>
        </section>
        <section class="norm-panel"><h2>Current accepted populations</h2><%= PopulationHtml %></section>
        <section class="norm-panel"><h2>Accepted-version history</h2><%= HistoryHtml %></section>
        <section class="norm-panel"><h2>Immediate predecessor comparison</h2><%= ComparisonHtml %></section>
        <section class="norm-panel"><h2>Missing required populations</h2><%= MissingHtml %></section>
    </main>
</body></html>
