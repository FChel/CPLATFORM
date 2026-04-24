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
    <script>
        function copyReviewLink(token, baseUrl) {
            var url = baseUrl.replace(/\/?$/, '/') + 'LPPI/LPPI_Review.aspx?t=' + encodeURIComponent(token);
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(url).then(function () {
                    showCopied(event.currentTarget);
                }).catch(function () { fallbackCopy(url, event.currentTarget); });
            } else {
                fallbackCopy(url, event.currentTarget);
            }
        }
        function fallbackCopy(text, btn) {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed'; ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.focus(); ta.select();
            try { document.execCommand('copy'); showCopied(btn); } catch (e) { alert('Copy failed — URL:\n' + text); }
            document.body.removeChild(ta);
        }
        function showCopied(btn) {
            if (!btn) return;
            var orig = btn.textContent;
            btn.textContent = 'Copied!';
            btn.disabled = true;
            setTimeout(function () { btn.textContent = orig; btn.disabled = false; }, 1800);
        }
        function openReviewLink(token, baseUrl) {
            var url = baseUrl.replace(/\/?$/, '/') + 'LPPI/LPPI_Review.aspx?t=' + encodeURIComponent(token);
            window.open(url, '_blank');
        }
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
                <p class="lead">Pick the Capability Manager groups with outstanding documents and issue a review package. Each group gets its own unguessable link.</p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />
        <asp:PlaceHolder ID="phUnconfigured" runat="server" />

        <div class="card">
            <h2>Outstanding work by group</h2>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtDueDate">Due date</label>
                    <asp:TextBox ID="txtDueDate" runat="server" CssClass="input" TextMode="Date" />
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnSend" runat="server" CssClass="btn btn-primary" Text="Send selected groups" OnClick="btnSend_Click" />
                </div>
            </div>

            <div class="tbl-wrap">
                <asp:Repeater ID="rptGroups" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th><input type="checkbox" id="chkAll" onclick="document.querySelectorAll('.cmPick').forEach(function(c){c.checked=this.checked}.bind(this))" /></th>
                                    <th>Program</th>
                                    <th>Recipients</th>
                                    <th class="num">Unreviewed docs</th>
                                    <th>Open package?</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td>
                                <asp:CheckBox runat="server" ID="chkPick" CssClass="cmPick"
                                              Enabled='<%# (int)Eval("UnreviewedDocs") > 0 && (int)Eval("ToCount") > 0 %>' />
                                <asp:HiddenField runat="server" ID="hfCmId" Value='<%# Eval("CmID") %>' />
                            </td>
                            <td>
                                <strong><%# LPPIHelper.Enc(Eval("Program")) %></strong>
                                <%# (int)Eval("ToCount") == 0
                                    ? " <span class=\"pill pill-overdue\">no recipients</span>"
                                    : "" %>
                            </td>
                            <td><%# LPPIHelper.Enc(Eval("ToList")) %></td>
                            <td class="num"><%# Eval("UnreviewedDocs") %></td>
                            <td>
                                <%# Eval("OpenPackageID") == DBNull.Value
                                    ? "<span class=\"muted\">No</span>"
                                    : "<span class=\"pill pill-open\">#" + Eval("OpenPackageID") + "</span>" %>
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

        <div class="card">
            <h2>Recent send-outs</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptRecent" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Package</th>
                                    <th>Program</th>
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
                            <td>
                                <%# string.Equals((string)Eval("Status"), "Open")
                                    ? "<span class=\"pill pill-open\">Open</span>"
                                    : "<span class=\"pill pill-closed\">" + LPPIHelper.Enc(Eval("Status")) + "</span>" %>
                            </td>
                            <td><%# LPPIHelper.FormatDate(Eval("LastEmailDate"), "dd/MM/yyyy HH:mm") %></td>
                            <td class="actions" style="white-space:nowrap;">
                                <%# string.Equals((string)Eval("Status"), "Open") ? RenderLinkButtons(Eval("Token")) : "" %>
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
</form>
</body>
</html>
