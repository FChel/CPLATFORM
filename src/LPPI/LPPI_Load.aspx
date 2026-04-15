<%@ Page Language="C#" AutoEventWireup="true" ResponseEncoding="utf-8"
    CodeFile="LPPI_Load.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Load" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Load file</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body>
<form id="form1" runat="server" enctype="multipart/form-data">
<div class="lppi-shell">
    <%= RenderHeader("load") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Load BODS file</h1>
                <p class="lead">Upload a <code>LATEPMT_INTEREST_REVIEW_*.xls</code> extract from your own computer. The file is parsed and previewed before anything is written to the database.</p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessages" runat="server" />

        <div class="card">
            <h2>Choose a file</h2>
            <p class="muted" style="font-size:13px;">
                BODS extracts are tab-delimited text files despite the <code>.xls</code> extension.
                Files up to 100&nbsp;MB are accepted.
            </p>
            <div class="form-grid">
                <div class="form-row form-row-wide">
                    <label for="fuBods">BODS extract</label>
                    <asp:FileUpload ID="fuBods" runat="server" CssClass="input" />
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnPreview" runat="server" CssClass="btn btn-primary"
                                Text="Upload &amp; preview" OnClick="btnPreview_Click" />
                </div>
            </div>
        </div>

        <asp:Panel ID="pnlPreview" runat="server" Visible="false" CssClass="card">
            <h2>Preview — <asp:Literal ID="litPreviewName" runat="server"/></h2>
            <p class="muted" style="font-size:13px;">
                Showing first 20 rows. Header validation: <asp:Literal ID="litHeaderStatus" runat="server"/>.
            </p>
            <div class="tbl-wrap">
                <asp:Literal ID="litPreviewTable" runat="server"/>
            </div>
            <div class="btn-row" style="margin-top:16px;">
                <asp:Button runat="server" ID="btnCommit" CssClass="btn btn-primary" Text="Commit load" OnClick="btnCommit_Click" />
                <asp:Button runat="server" ID="btnCancel" CssClass="btn btn-secondary" Text="Cancel"     OnClick="btnCancel_Click" CausesValidation="false" />
            </div>
        </asp:Panel>

        <asp:Panel ID="pnlResult" runat="server" Visible="false" CssClass="card">
            <h2>Load result</h2>
            <div class="stat-grid">
                <div class="stat stat-ok"><span class="stat-label">Inserted</span><span class="stat-value"><asp:Literal ID="litResIns" runat="server"/></span></div>
                <div class="stat stat-warn"><span class="stat-label">Skipped (already loaded)</span><span class="stat-value"><asp:Literal ID="litResSkip" runat="server"/></span></div>
                <div class="stat stat-err"><span class="stat-label">Failed</span><span class="stat-value"><asp:Literal ID="litResFail" runat="server"/></span></div>
                <div class="stat"><span class="stat-label">In file</span><span class="stat-value"><asp:Literal ID="litResTotal" runat="server"/></span></div>
            </div>
            <details>
                <summary style="cursor:pointer;font-weight:600;color:var(--ink-2);">Skipped doc numbers</summary>
                <pre style="background:var(--bg);padding:12px;border-radius:8px;font-size:12px;max-height:240px;overflow:auto;"><asp:Literal ID="litResSkipList" runat="server"/></pre>
            </details>
            <details style="margin-top:8px;">
                <summary style="cursor:pointer;font-weight:600;color:var(--ink-2);">Failed rows</summary>
                <pre style="background:var(--bg);padding:12px;border-radius:8px;font-size:12px;max-height:240px;overflow:auto;"><asp:Literal ID="litResFailList" runat="server"/></pre>
            </details>
            <div class="btn-row" style="margin-top:16px;">
                <a class="btn btn-primary"   href="LPPI_Batches.aspx">View batches</a>
                <a class="btn btn-secondary" href="LPPI_Load.aspx">Load another file</a>
            </div>
        </asp:Panel>
    </main>

    <footer class="lppi-footer">Defence Finance Group · LPPI Review · <%= CurrentEnv %></footer>
</div>
</form>
</body>
</html>
