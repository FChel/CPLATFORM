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
                <h1>Export to ERP Payment Request - bulk upload template</h1>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <div class="card">
            <h2>Export parameters</h2>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtFrom">Loaded into CPLATFORM from</label>
                    <asp:TextBox ID="txtFrom" runat="server" CssClass="input" TextMode="Date" />
                </div>
                <div class="form-row">
                    <label for="txtTo">Loaded into CPLATFORM to</label>
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
                    <asp:Button ID="btnPreview" runat="server" CssClass="btn btn-secondary" Text="Preview count"        OnClick="btnPreview_Click" />
                    <asp:Button ID="btnExport"  runat="server" CssClass="btn btn-primary"   Text="Generate payment file" OnClick="btnExport_Click"
                                OnClientClick="return confirm('Generate the payment file now?');" />
                </div>
            </div>
        </div>

    </main>

    <footer class="lppi-footer">
        <span>LPPI Review · <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
