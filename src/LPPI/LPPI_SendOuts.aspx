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
        /* Email preview modal — page-specific, kept inline. The pill colour
           rules that used to live here have moved to the shared lppi.css
           lifecycle-pills section so all pages stay in sync. */
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

        /* Capability Manager cell — program name primary, optional
           "Not configured" pill below when AS Fin email is missing. */
        .cm-cell { line-height: 1.35; }
        .cm-cell .cm-program { font-size: 14px; color: var(--ink-1); }
        .cm-cell .cm-config  { margin-top: 4px; }

        .pill-not-configured {
            background: #fff4e0;
            color: #8a4500;
            border: 1px solid #f3c89d;
            font-size: 11px;
            padding: 2px 8px;
            border-radius: 999px;
            font-weight: 600;
            white-space: nowrap;
        }

        /* Notify modal — recipient prompt for the AS Fin notification. Same
           visual idiom as the preview modal so the page stays consistent. */
        #notifyOverlay {
            display: none;
            position: fixed;
            inset: 0;
            background: rgba(0,0,0,0.55);
            z-index: 1000;
            align-items: center;
            justify-content: center;
        }
        #notifyOverlay.open { display: flex; }
        #notifyDialog {
            background: #fff;
            border-radius: 8px;
            width: 480px;
            max-width: 96vw;
            max-height: 90vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            box-shadow: 0 8px 32px rgba(0,0,0,0.18);
        }
        #notifyToolbar {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 12px 16px;
            border-bottom: 1px solid #e5e5e5;
        }
        #notifyToolbar span { font-size: 14px; font-weight: 600; color: #1a1a1a; }
        #notifyBody    { padding: 16px; }
        #notifyActions {
            padding: 12px 16px;
            border-top: 1px solid #e5e5e5;
            display: flex;
            gap: 8px;
            justify-content: flex-end;
        }
    </style>
    <script>
        function openReviewLink(token, baseUrl) {
            var url = baseUrl.replace(/\/?$/, '/') + 'LPPI/LPPI_Review.aspx?t=' + encodeURIComponent(token);
            window.open(url, '_blank');
        }

        // --- Preview modal ---
        // audience is "asfin" or "poc". POC previews use placeholder values
        // (see LPPIEmail.BuildEmailHtml) so no per-POC selection is needed.
        function openPreview(packageId, emailType, audience) {
            var overlay = document.getElementById('previewOverlay');
            var frame   = document.getElementById('previewFrame');
            var label   = document.getElementById('previewLabel');
            var aud = (audience === 'poc') ? 'POC' : 'AS Fin';
            label.textContent = 'Email preview — Package #' + packageId + ' · ' + aud + ' · ' + emailType;
            frame.src = 'LPPI_EmailPreview.aspx?id=' + packageId
                      + '&type=' + encodeURIComponent(emailType)
                      + '&audience=' + encodeURIComponent(audience || 'asfin');
            overlay.classList.add('open');
        }
        function closePreview() {
            var overlay = document.getElementById('previewOverlay');
            overlay.classList.remove('open');
            document.getElementById('previewFrame').src = 'about:blank';
        }
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { closePreview(); closeNotify(); }
        });

        // --- Notify AS Fin modal ---
        // openNotify(packageId) is called by the per-row "Notify AS Fin"
        // button rendered in RenderPackageActions. The package ID is
        // stashed in the dialog via a data attribute and surfaced in the
        // context line so the operator can see exactly which package they
        // are notifying about.
        function openNotify(packageId) {
            var overlay = document.getElementById('notifyOverlay');
            var dialog  = document.getElementById('notifyDialog');
            var ctx     = document.getElementById('notifyContext');
            var email   = document.getElementById('notifyEmail');
            var err     = document.getElementById('notifyError');

            dialog.setAttribute('data-package-id', packageId);
            ctx.textContent = 'Send the package summary for #' + packageId + ' to the responsible AS Fin officer.';
            email.value     = '';
            err.style.display = 'none';
            err.textContent   = '';

            overlay.classList.add('open');
            // Defer focus so the modal has actually rendered before we
            // call .focus() — otherwise some browsers ignore it.
            setTimeout(function () { email.focus(); }, 50);
        }

        function closeNotify() {
            document.getElementById('notifyOverlay').classList.remove('open');
        }

        // Client-side validation mirrors LPPIHelper.ValidateDefenceEmail
        // for snappy feedback. The server is still authoritative.
        function isDefenceLikeEmail(s) {
            if (!s) return false;
            var v = s.trim().toLowerCase();
            // Basic shape, then domain whitelist. Subdomains allowed.
            if (!/^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$/.test(v)) return false;
            if (v.endsWith('@defence.gov.au') || v.endsWith('.defence.gov.au')) return true;
            if (v.endsWith('@annpsr.gov.au')  || v.endsWith('.annpsr.gov.au'))  return true;
            return false;
        }

        function submitNotify() {
            var dialog = document.getElementById('notifyDialog');
            var email  = document.getElementById('notifyEmail');
            var err    = document.getElementById('notifyError');
            var sendBtn = document.getElementById('notifySendBtn');

            var packageId = dialog.getAttribute('data-package-id');
            var addr      = (email.value || '').trim();

            if (!packageId) {
                err.textContent = 'No package selected.';
                err.style.display = '';
                return;
            }
            if (!isDefenceLikeEmail(addr)) {
                err.textContent = 'Please enter a valid Defence (@defence.gov.au) or ANNPSR (@annpsr.gov.au) email address.';
                err.style.display = '';
                email.focus();
                return;
            }

            // Stash values into the hidden ASP.NET fields, then trigger the
            // hidden postback button which fires btnNotify_Click on the
            // server. Disable the send button while the postback is in
            // flight so the operator does not double-submit.
            document.getElementById('<%= hfNotifyPackageId.ClientID %>').value = packageId;
            document.getElementById('<%= hfNotifyRecipient.ClientID %>').value = addr;
            sendBtn.disabled = true;
            sendBtn.textContent = 'Sending…';
            document.getElementById('<%= btnNotify.ClientID %>').click();
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
                <p class="lead">
                    Packages are created automatically when a file is loaded. Pick the packages you want to send (or remind) and issue them here.
                    Each send dispatches a group summary to AS Fin and a per-POC email to every distinct invoice contact in the package.
                    Finalised packages remain in the list for visibility but cannot be re-sent.
                </p>
            </div>
        </div>

        <%-- Test mode banner shown when ProductionMode is false --%>
        <asp:PlaceHolder ID="phUatBanner" runat="server" />

        <asp:PlaceHolder ID="phMessage" runat="server" />
        <asp:PlaceHolder ID="phUnconfigured" runat="server" />

        <div class="card">
            <h2>Open packages</h2>
            <p style="color:var(--ink-3);font-size:13px;">
                NotSent packages can be issued for the first time. Sent and InReview packages can be reminded.
                Finalised packages are read-only here — they are visible for monitoring and proceed to Export when ready.
                Use Preview AS Fin or Preview POC at any time to see what the recipients will receive.
            </p>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtDueDate">Due date (applied to NotSent packages on first send)</label>
                    <asp:TextBox ID="txtDueDate" runat="server" CssClass="input" TextMode="Date" />
                </div>
                <div class="form-row form-row-actions">
                    <%-- Real send — visible always, disabled when not in production. --%>
                    <asp:Button ID="btnSend" runat="server"
                                CssClass="btn btn-primary"
                                Text="Send / remind selected"
                                OnClick="btnSend_Click" />

                    <%-- Visible only in non-production. Drives the lifecycle
                         without dispatching email so end-to-end flow can be
                         tested. Branches by current status: NotSent gets a
                         simulated initial send (status transitions to Sent),
                         Sent / InReview gets a simulated reminder (no status
                         change). Mutually exclusive with the real send by
                         construction (gated on the same ProductionMode flag). --%>
                    <asp:Button ID="btnMarkSent" runat="server"
                                CssClass="btn btn-secondary"
                                Text="Mark as sent / remind (test)"
                                Visible="false"
                                OnClick="btnMarkSent_Click" />
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
                                    <th class="num">POCs</th>
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
                                <%-- Plain HTML checkbox with runat=server (not asp:CheckBox)
                                     so the .pkgPick CSS class lands on the actual <input>
                                     element rather than a wrapping <span>. The chkAll
                                     select-all up in the header relies on
                                     document.querySelectorAll('.pkgPick') being able to
                                     toggle .checked on each result. Server-side selection
                                     still works via FindControl, cast to HtmlInputCheckBox.

                                     The disabled attribute is data-bound — runat=server
                                     makes ASP.NET bind to HtmlInputCheckBox.Disabled which
                                     is a bool property, NOT a passthrough HTML attribute.
                                     Actionable criteria (May 2026):
                                       - the CM has its AS Fin email configured
                                         (EmailConfigured = 1 from the SQL projection), AND
                                       - the status is one of NotSent / Sent / InReview.
                                       Finalised rows show but cannot be picked.

                                     Note: POC count is NOT part of the gate — empty-POC
                                     packages still send (AS Fin gets the group summary;
                                     POC fan-out is skipped with a warning). The send
                                     pipeline handles it. --%>
                                <%-- Picker tooltip explains the disabled state on hover.
                                     The two reasons a row is non-pickable are mutually
                                     exclusive in practice but rendered as one combined
                                     message — the SQL projection of EmailConfigured plus
                                     the status guard cover both cases. --%>
                                <input type="checkbox" runat="server" id="chkPick" class="pkgPick"
                                       disabled='<%# !( (bool)Eval("EmailConfigured")
                                                    && ( string.Equals(Convert.ToString(Eval("Status")), "NotSent",  System.StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(Convert.ToString(Eval("Status")), "Sent",     System.StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(Convert.ToString(Eval("Status")), "InReview", System.StringComparison.OrdinalIgnoreCase) ) ) %>'
                                       title='<%# (bool)Eval("EmailConfigured")
                                                    ? "Select to send/remind this package."
                                                    : "AS Fin email not configured for this Capability Manager. Click the program name to fix on the Capability Managers page." %>' />
                                <asp:HiddenField runat="server" ID="hfPackageId" Value='<%# Eval("PackageID") %>' />
                            </td>
                            <td>#<%# Eval("PackageID") %></td>
                            <td><%# RenderCmCell(Container.DataItem) %></td>
                            <td class="num"><%# Eval("PocCount") %></td>
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

<%-- Notify AS Fin recipient prompt modal --%>
<div id="notifyOverlay" onclick="if(event.target===this)closeNotify();">
    <div id="notifyDialog" role="dialog" aria-labelledby="notifyTitle">
        <div id="notifyToolbar">
            <span id="notifyTitle">Notify AS Fin — recipient</span>
            <button type="button" class="btn btn-sm btn-ghost" onclick="closeNotify()">Close &times;</button>
        </div>
        <div id="notifyBody">
            <p id="notifyContext" class="muted" style="font-size:13px;margin-top:0;"></p>
            <label for="notifyEmail" style="display:block;font-size:13px;font-weight:600;margin-bottom:4px;">Recipient email</label>
            <input type="email" id="notifyEmail" class="input"
                   placeholder="firstname.lastname@defence.gov.au"
                   style="width:100%;box-sizing:border-box;" />
            <p class="muted" style="font-size:12px;margin:6px 0 0;">
                Must be a Defence (<code>@defence.gov.au</code>) or ANNPSR (<code>@annpsr.gov.au</code>) address.
                The CM team mailbox is added on CC and LPPI support on BCC automatically.
            </p>
            <div id="notifyError" class="alert alert-err" style="display:none;margin-top:10px;"></div>
        </div>
        <div id="notifyActions">
            <button type="button" class="btn btn-ghost" onclick="closeNotify()">Cancel</button>
            <button type="button" id="notifySendBtn" class="btn btn-primary" onclick="submitNotify();">Send notification</button>
        </div>
    </div>
</div>

<%-- Hidden postback mechanism for the Notify modal. JS writes the package
     ID and recipient into the hidden fields then clicks the hidden button
     to fire btnNotify_Click. Keeping this within the form so ASP.NET wires
     up the postback correctly. --%>
<asp:HiddenField runat="server" ID="hfNotifyPackageId" />
<asp:HiddenField runat="server" ID="hfNotifyRecipient" />
<asp:Button     runat="server" ID="btnNotify"
                style="display:none;"
                OnClick="btnNotify_Click"
                UseSubmitBehavior="false" />

</form>
</body>
</html>
