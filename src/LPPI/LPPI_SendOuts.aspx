<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_SendOuts.aspx.cs" Inherits="CPlatform.LPPI.LPPI_SendOuts" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Send-outs</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* Email preview modal */
        #previewOverlay {
            display: none;
            position: fixed;
            inset: 0;
            background: rgba(0,0,0,0.55);
            z-index: 1000;
            align-items: center;
            justify-content: center;
        }
        #previewOverlay.open { display: flex; }
        #previewDialog {
            background: #fff;
            border-radius: 8px;
            width: 680px;
            max-width: 96vw;
            max-height: 90vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            box-shadow: 0 8px 32px rgba(0,0,0,0.18);
        }
        #previewToolbar {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 12px 16px;
            border-bottom: 1px solid #e5e5e5;
            flex-shrink: 0;
        }
        #previewToolbar span { font-size: 14px; font-weight: 600; color: #1a1a1a; }
        #previewToolbar button { font-size: 13px; }
        #previewFrame {
            flex: 1;
            border: none;
            width: 100%;
            min-height: 520px;
        }

        /* Status pill colours for the new package status set */
        .pill.notsent  { background: var(--line-2);    color: var(--ink-3); }
        .pill.sent     { background: var(--orange-soft); color: var(--orange-deep); }
        .pill.inreview { background: var(--orange-soft); color: var(--orange-deep); }
        .pill.complete { background: var(--ok-bg);     color: var(--ok); }
        .pill.cancelled{ background: var(--err-bg);    color: var(--err); }
        .pill.overdue  { background: var(--err-bg);    color: var(--err); }
        .pill.duesoon  { background: var(--warn-bg);   color: var(--warn); }
    </style>
    <script>
        function openReviewLink(token, baseUrl) {
            var url = baseUrl.replace(/\/?$/, '/') + 'LPPI/LPPI_Review.aspx?t=' + encodeURIComponent(token);
            window.open(url, '_blank');
        }

        // --- Preview modal ---
        function openPreview(packageId, emailType) {
            var overlay = document.getElementById('previewOverlay');
            var frame   = document.getElementById('previewFrame');
            var label   = document.getElementById('previewLabel');
            label.textContent = 'Email preview — Package #' + packageId + ' (' + emailType + ')';
            frame.src = 'LPPI_EmailPreview.aspx?id=' + packageId + '&type=' + encodeURIComponent(emailType);
            overlay.classList.add('open');
        }
        function closePreview() {
            var overlay = document.getElementById('previewOverlay');
            overlay.classList.remove('open');
            document.getElementById('previewFrame').src = 'about:blank';
        }
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') closePreview();
        });
    </script>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("sendouts") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Send for review</h1>
                <p class="lead">Packages are created automatically when a file is loaded. Pick the packages you want to send (or remind) and issue them here.</p>
            </div>
        </div>

        <%-- UAT banner shown when ProductionMode is false --%>
        <asp:PlaceHolder ID="phUatBanner" runat="server" />

        <asp:PlaceHolder ID="phMessage" runat="server" />
        <asp:PlaceHolder ID="phUnconfigured" runat="server" />

        <div class="card">
            <h2>Open packages</h2>
            <p style="color:var(--ink-3);font-size:13px;">
                NotSent packages can be issued for the first time. Sent and InReview packages can be reminded.
                Use Preview email at any time to see what the recipients will receive.
            </p>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtDueDate">Due date (applied to NotSent packages on first send)</label>
                    <asp:TextBox ID="txtDueDate" runat="server" CssClass="input" TextMode="Date" />
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnSend" runat="server" CssClass="btn btn-primary" Text="Send / remind selected" OnClick="btnSend_Click" />
                </div>
            </div>

            <div class="tbl-wrap">
                <asp:Repeater ID="rptPackages" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th><input type="checkbox" id="chkAll" onclick="document.querySelectorAll('.pkgPick').forEach(function(c){c.checked=this.checked}.bind(this))" /></th>
                                    <th>Package</th>
                                    <th>Capability Manager</th>
                                    <th>Recipients</th>
                                    <th class="num">Docs</th>
                                    <th class="num">Reviewed</th>
                                    <th>Status</th>
                                    <th>Last email</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td>
                                <asp:CheckBox runat="server" ID="chkPick" CssClass="pkgPick"
                                              Enabled='<%# (int)Eval("ToCount") > 0 %>' />
                                <asp:HiddenField runat="server" ID="hfPackageId" Value='<%# Eval("PackageID") %>' />
                            </td>
                            <td>#<%# Eval("PackageID") %></td>
                            <td>
                                <strong><%# LPPIHelper.Enc(Eval("Program")) %></strong>
                                <%# (int)Eval("ToCount") == 0
                                    ? " <span class=\"pill overdue\">no recipients</span>"
                                    : "" %>
                            </td>
                            <td><%# LPPIHelper.Enc(Eval("ToList")) %></td>
                            <td class="num"><%# Eval("DocCount") %></td>
                            <td class="num"><%# Eval("ReviewedCount") %></td>
                            <td><%# RenderStatusPill(Container.DataItem) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("LastEmailDate"), "dd/MM/yyyy HH:mm") %></td>
                            <td class="actions" style="white-space:nowrap;">
                                <%# RenderPackageActions(Eval("PackageID"), Eval("Token"), Eval("Status")) %>
                            </td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="phNoPackages" runat="server" Visible="false">
                    <p class="muted" style="padding:24px;text-align:center;">No open packages. Load a file to create new packages.</p>
                </asp:PlaceHolder>
            </div>
        </div>

        <div class="card">
            <h2>Recent send-outs</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptRecent" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Package</th>
                                    <th>Capability Manager</th>
                                    <th>Created</th>
                                    <th>Due</th>
                                    <th class="num">Docs</th>
                                    <th class="num">Reviewed</th>
                                    <th>Status</th>
                                    <th>Last email</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td>#<%# Eval("PackageID") %></td>
                            <td><%# LPPIHelper.Enc(Eval("Program")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("CreatedDate"), "dd/MM/yyyy HH:mm") %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("DueDate")) %></td>
                            <td class="num"><%# Eval("TotalDocs") %></td>
                            <td class="num"><%# Eval("ReviewedDocs") %></td>
                            <td><%# RenderStatusPillFromStatus(Eval("Status")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("LastEmailDate"), "dd/MM/yyyy HH:mm") %></td>
                            <td class="actions" style="white-space:nowrap;">
                                <%# RenderRecentActions(Eval("PackageID"), Eval("Token"), Eval("Status")) %>
                            </td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
            </div>
        </div>
    </main>

    <footer class="lppi-footer">
        <span>LPPI Review · <%= CurrentEnv %></span>
    </footer>
</div>

<%-- Email preview modal --%>
<div id="previewOverlay" onclick="if(event.target===this)closePreview();">
    <div id="previewDialog">
        <div id="previewToolbar">
            <span id="previewLabel">Email preview</span>
            <button type="button" class="btn btn-sm btn-ghost" onclick="closePreview()">Close &times;</button>
        </div>
        <iframe id="previewFrame" src="about:blank" title="Email preview"></iframe>
    </div>
</div>

</form>
</body>
</html>
