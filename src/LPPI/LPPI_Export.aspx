<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Export.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Export" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Export</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("export") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Export to BODS</h1>
                <p class="lead">Produce a tab-delimited file of reviewed documents in the BODS upload format. A copy is saved to the configured export folder and streamed to your browser.</p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <div class="card">
            <h2>Export parameters</h2>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtFrom">First seen from</label>
                    <asp:TextBox ID="txtFrom" runat="server" CssClass="input" TextMode="Date" />
                </div>
                <div class="form-row">
                    <label for="txtTo">First seen to</label>
                    <asp:TextBox ID="txtTo" runat="server" CssClass="input" TextMode="Date" />
                </div>
                <div class="form-row">
                    <label for="ddlBatch">Batch (optional)</label>
                    <asp:DropDownList ID="ddlBatch" runat="server" CssClass="input" />
                </div>
                <div class="form-row form-row-check">
                    <label><asp:CheckBox ID="chkIncludeExported" runat="server" /> Include documents already exported</label>
                </div>
                <div class="form-row form-row-check">
                    <label><asp:CheckBox ID="chkMark" runat="server" Checked="true" /> Mark documents as exported</label>
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnPreview" runat="server" CssClass="btn btn-secondary" Text="Preview count" OnClick="btnPreview_Click" />
                    <asp:Button ID="btnExport"  runat="server" CssClass="btn btn-primary"   Text="Export now"    OnClick="btnExport_Click"
                                OnClientClick="return confirm('Generate the export file now?');" />
                </div>
            </div>
        </div>

        <div class="card">
            <h2>About the export</h2>
            <ul class="bare">
                <li>File name: <code>LATEPMT_INTEREST_INCLUDE_&lt;MMM&gt;_&lt;YYYY&gt;.txt</code></li>
                <li>Format: tab-delimited, UTF-8, dates as <code>dd/MM/yyyy</code>.</li>
                <li>Only documents with a reason code are included.</li>
                <li>Saved copy: <code><%= LPPIHelper.Enc(LPPIHelper.ExportPath) %></code></li>
            </ul>
        </div>
    </main>

    <footer class="lppi-footer">
        <span>LPPI Review · <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
