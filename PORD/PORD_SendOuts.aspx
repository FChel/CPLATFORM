<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_SendOuts.aspx.cs" Inherits="CPlatform.PORD.PORD_SendOuts" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Send-outs</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
    <style>
        details.links { position: relative; display: inline-block; }
        details.links summary { list-style: none; cursor: pointer; }
        details.links summary::-webkit-details-marker { display: none; }
        details.links .menu { position: absolute; right: 0; top: 100%; margin-top: 4px; z-index: 30; background: var(--white);
            border: 1px solid var(--line); border-radius: var(--r); box-shadow: var(--shadow); min-width: 300px; padding: 6px; }
        details.links .menu a { display: flex; justify-content: space-between; gap: 12px; padding: 7px 10px; border-radius: var(--r-sm); color: var(--ink-2); font-size: 13px; }
        details.links .menu a:hover { background: var(--orange-soft); text-decoration: none; }
        details.links .menu .h { font-size: 10px; text-transform: uppercase; letter-spacing: .05em; color: var(--ink-3); font-weight: 700; padding: 6px 10px 2px; }
        .send-bar { display: flex; flex-wrap: wrap; gap: 16px; align-items: flex-end; margin-bottom: 16px; }
        .send-bar .form-row { margin: 0; min-width: 220px; }
    </style>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("sendouts") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>Send for review</h1>
                <p class="lead">Packages are created when a file is loaded, one per Delivery Manager program. Each send emails AS Fin the full package and each PO contact a link to their own POs.</p>
            </div>
        </div>

        <%= RenderDemoNotice() %>
        <asp:PlaceHolder ID="phMsg" runat="server" />

        <div class="card">
            <div class="send-bar">
                <div class="form-row">
                    <label for="txtDueDate">Respond-by date (new sends)</label>
                    <input type="date" id="txtDueDate" name="dueDate" class="input" value="<%= DueDateValue %>" />
                </div>
                <asp:Button ID="btnSend" runat="server" CssClass="btn btn-primary" Text="Send / remind selected" OnClick="btnSend_Click" />
                <span class="muted" style="font-size:12px;max-width:420px;"><asp:Literal ID="litSendNote" runat="server" /></span>
            </div>

            <div class="tbl-wrap" style="max-height:none;overflow:visible;">
                <table class="tbl">
                    <thead><tr>
                        <th style="width:34px;"><input type="checkbox" onclick="var c=this.checked;document.querySelectorAll('.pkgPick').forEach(function(x){if(!x.disabled)x.checked=c;})" /></th>
                        <th>Program</th><th>AS Fin mailbox</th><th class="num">POCs</th><th class="num">POs</th>
                        <th>Responses</th><th>Due</th><th>Status</th><th>Last email</th><th></th>
                    </tr></thead>
                    <tbody>
                    <asp:Repeater ID="rptPkgs" runat="server">
                        <ItemTemplate>
                        <tr>
                            <td><input type="checkbox" class="pkgPick" name="pick" value="<%# P(Container.DataItem).PackageID %>" <%# CanSend(P(Container.DataItem)) ? "" : "disabled" %>
                                       title="<%# PORDHelper.Attr(PickTitle(P(Container.DataItem))) %>" /></td>
                            <td><strong><%# PORDHelper.Enc(P(Container.DataItem).DmProgram) %></strong><span class="sub">#<%# P(Container.DataItem).PackageID %></span></td>
                            <td><%# AsFinCell(P(Container.DataItem).DmProgram) %></td>
                            <td class="num"><%# PocCount(P(Container.DataItem).PackageID) %></td>
                            <td class="num"><%# PoCount(P(Container.DataItem).PackageID) %></td>
                            <td><%# Progress(P(Container.DataItem).PackageID) %></td>
                            <td class="nowrap"><%# PORDHelper.Date(P(Container.DataItem).DueDate) %></td>
                            <td class="nowrap"><%# PORDHelper.StatusPill(P(Container.DataItem).Status, P(Container.DataItem).DueDate) %></td>
                            <td class="nowrap"><%# PORDHelper.DateTimeShort(P(Container.DataItem).LastEmailDate) %></td>
                            <td class="actions"><%# Actions(P(Container.DataItem)) %></td>
                        </tr>
                        </ItemTemplate>
                    </asp:Repeater>
                    </tbody>
                </table>
            </div>
        </div>
    </main>
    <%= RenderFooter() %>
</div>

<div class="pord-modal" id="pvModal" onclick="if(event.target===this)pvClose();">
    <div class="pord-modal-card" role="dialog" aria-label="Email preview">
        <div class="pord-modal-bar">
            <span class="ttl" id="pvTitle">Email preview</span>
            <span class="seg" id="pvSeg"></span>
            <button type="button" class="btn btn-sm btn-ghost" onclick="pvClose()">Close &times;</button>
        </div>
        <iframe id="pvFrame" title="Email preview" src="about:blank"></iframe>
    </div>
</div>
<script>
    function pvOpen(pkg, program, pocs) {
        var seg = document.getElementById('pvSeg'); seg.innerHTML = '';
        var opts = [{ l: 'AS Fin', u: 'PORD_EmailPreview.aspx?pkg=' + pkg + '&aud=asfin' }];
        (pocs || []).forEach(function (p) { opts.push({ l: p.n, u: 'PORD_EmailPreview.aspx?pkg=' + pkg + '&aud=poc&poc=' + encodeURIComponent(p.e) }); });
        opts.slice(0, 5).forEach(function (o, i) {
            var b = document.createElement('button'); b.type = 'button'; b.textContent = o.l; if (!i) b.className = 'on';
            b.onclick = function () { seg.querySelectorAll('button').forEach(function (x) { x.className = ''; }); b.className = 'on'; document.getElementById('pvFrame').src = o.u; };
            seg.appendChild(b);
        });
        document.getElementById('pvTitle').textContent = program + ' — email preview';
        document.getElementById('pvFrame').src = opts[0].u;
        document.getElementById('pvModal').classList.add('show');
    }
    function pvClose() { document.getElementById('pvModal').classList.remove('show'); document.getElementById('pvFrame').src = 'about:blank'; }
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') pvClose(); });
    document.addEventListener('click', function (e) {
        document.querySelectorAll('details.links[open]').forEach(function (d) { if (!d.contains(e.target)) d.removeAttribute('open'); });
    });
</script>
</form>
</body>
</html>
