<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Load.aspx.cs" Inherits="CPlatform.PORD.PORD_Load" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Load file</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server" enctype="multipart/form-data">
<div class="lppi-shell">
    <%= RenderHeader("load") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>Load monthly extract</h1>
                <p class="lead">Upload the BODS <code>PO_NSPT_REVIEW_*.csv</code> extract. It is checked and classified before anything is saved.</p>
            </div>
            <div class="btn-row">
                <a class="btn btn-secondary" href="PORD_SampleFile.ashx">Download sample file</a>
            </div>
        </div>

        <%= RenderDemoNotice() %>
        <asp:PlaceHolder ID="phMsg" runat="server" />

        <div class="pord-cols">
            <div class="card">
                <h2>1. Choose the file</h2>
                <div class="pord-drop">
                    <strong>CSV or tab-delimited, UTF-8, header row first</strong>
                    <div class="muted" style="font-size:12px;margin-top:4px;">Columns are matched by name, in any order.</div>
                    <asp:FileUpload ID="fuFile" runat="server" accept=".csv,.txt,.tsv" />
                </div>
                <div class="btn-row" style="margin-top:14px;">
                    <asp:Button ID="btnPreview" runat="server" CssClass="btn btn-primary" Text="Upload &amp; check" OnClick="btnPreview_Click" />
                    <asp:Button ID="btnSample" runat="server" CssClass="btn btn-secondary" Text="Check the sample file" OnClick="btnSample_Click" />
                </div>
            </div>
            <div class="card">
                <h2>Expected columns</h2>
                <p class="card-lead">The business requirement's report output, plus the columns the spec review proposes adding. Without the proposed columns, grouping, POC emails and the foreign-currency rule cannot work.</p>
                <ul class="pord-check"><%= ColumnChecklistHtml %></ul>
            </div>
        </div>

        <asp:Panel ID="pnlResult" runat="server" Visible="false" CssClass="card" style="margin-top:16px;">
            <div class="card-head">
                <h2>2. Check — <asp:Literal ID="litFile" runat="server" /></h2>
                <asp:Literal ID="litHeaderPill" runat="server" />
            </div>
            <div class="stat-grid" style="grid-template-columns:repeat(auto-fit,minmax(150px,1fr));">
                <div class="stat"><div class="lbl">Rows in file</div><div class="val"><asp:Literal ID="litRows" runat="server" /></div></div>
                <div class="stat err"><div class="lbl">Flagged</div><div class="val"><asp:Literal ID="litFlagged" runat="server" /></div></div>
                <div class="stat ok"><div class="lbl">Compliant</div><div class="val"><asp:Literal ID="litCompliant" runat="server" /></div><div class="sub">not loaded</div></div>
                <div class="stat"><div class="lbl">Excluded</div><div class="val"><asp:Literal ID="litExcluded" runat="server" /></div><div class="sub">active exclusions</div></div>
                <div class="stat warn"><div class="lbl">Rejected rows</div><div class="val"><asp:Literal ID="litFailed" runat="server" /></div></div>
            </div>
            <div class="pord-section-label">By issue</div>
            <div class="btn-row" style="margin-bottom:16px;"><asp:Literal ID="litCats" runat="server" /></div>

            <div class="pord-section-label">First flagged rows</div>
            <div class="tbl-wrap" style="max-height:420px;"><asp:Literal ID="litPreview" runat="server" /></div>

            <div class="pord-section-label" style="margin-top:20px;">3. Commit</div>
            <p class="muted" style="font-size:13px;">Committing creates or refreshes one package per program, increments Review Nbr for POs seen before,
                skips POs with an active exclusion, and marks last month's "will amend" POs that no longer appear as resolved.</p>
            <div class="btn-row">
                <asp:Button ID="btnCommit" runat="server" CssClass="btn btn-primary" Text="Commit load" OnClick="btnCommit_Click" />
                <a class="btn btn-secondary" href="PORD_Load.aspx">Cancel</a>
            </div>
        </asp:Panel>

        <div class="card" style="margin-top:16px;">
            <h2>Load history</h2>
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead><tr><th>#</th><th>File</th><th>Loaded</th><th>By</th><th class="num">Rows</th><th class="num">Flagged</th><th class="num">Excluded</th><th class="num">Repeat</th><th class="num">Resolved</th></tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptBatches" runat="server"><ItemTemplate>
                        <tr>
                            <td>#<%# B(Container.DataItem).BatchID %></td>
                            <td><%# PORDHelper.Enc(B(Container.DataItem).FileName) %></td>
                            <td class="nowrap"><%# PORDHelper.DateTimeShort(B(Container.DataItem).LoadedDate) %></td>
                            <td><%# PORDHelper.Enc(B(Container.DataItem).LoadedBy) %></td>
                            <td class="num"><%# B(Container.DataItem).RowsInFile %></td>
                            <td class="num"><%# B(Container.DataItem).Flagged %></td>
                            <td class="num"><%# B(Container.DataItem).Excluded %></td>
                            <td class="num"><%# B(Container.DataItem).Repeat %></td>
                            <td class="num" style="color:var(--ok);font-weight:600;"><%# B(Container.DataItem).Resolved %></td>
                        </tr>
                    </ItemTemplate></asp:Repeater>
                    </tbody>
                </table>
            </div>
        </div>
    </main>
    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
