<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_CapabilityManagers.aspx.cs" Inherits="CPlatform.LPPI.LPPI_CapabilityManagers" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Capability Managers</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* Row highlight for the CM currently being edited */
        .tbl tr.is-editing > td {
            background: #eef4ff;
            box-shadow: inset 3px 0 0 #3b82f6;
        }
        .edit-flag {
            margin-left: 0.5em;
            font-size: 0.85em;
            color: #1d4ed8;
            font-weight: 500;
        }

        /* Manage panel sub-section divider */
        .panel-section {
            padding: 14px 0 18px;
            border-bottom: 1px solid var(--line);
            margin-bottom: 18px;
        }
        .panel-section:last-child { border-bottom: none; margin-bottom: 0; }
        .panel-section-title {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            font-weight: 700;
            color: var(--ink-3);
            margin-bottom: 10px;
        }

        /* Pair: input + Save button on one row */
        .form-row-inline {
            display: flex;
            align-items: center;
            gap: 10px;
        }
        .form-row-inline .input {
            flex: 1;
            font-family: var(--font);
        }

        /* Two-column grid for the email + display name pair */
        .config-grid {
            display: grid;
            grid-template-columns: 1.4fr 1fr;
            gap: 14px;
            margin-bottom: 12px;
        }
        @media (max-width: 720px) {
            .config-grid { grid-template-columns: 1fr; }
        }
        .config-grid label {
            display: block;
            font-size: 12px;
            font-weight: 600;
            color: var(--ink-3);
            margin-bottom: 4px;
        }
        .config-grid .input {
            width: 100%;
            font-family: var(--font);
        }

        .help-line {
            font-size: 12px;
            color: var(--ink-3);
            margin: 6px 0 14px;
            line-height: 1.55;
        }

        /* Pill that reflects per-row email-configured state. Reuses the
           existing colour primitives in lppi.css. */
        .pill-config-ok {
            background: var(--ok-bg);
            color: var(--ok);
            border: 1px solid var(--ok);
        }
        .pill-config-missing {
            background: var(--warn-bg);
            color: var(--warn);
            border: 1px solid var(--warn);
        }
    </style>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("cm") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Capability Managers</h1>
                <p class="lead">Capability Manager groups are created automatically when an ERP file is loaded. Configure the AS Fin email and display name for each group below; both are required before review packages can be sent.</p>
            </div>
        </div>

        <%-- Banner: count of CMs missing email configuration. Server-rendered
             via litMissingBanner from code-behind. Hidden when count is 0. --%>
        <asp:PlaceHolder ID="phMissingBanner" runat="server" />

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- ================================================================
             Manage panel — surfaces above the list when operator clicks
             Manage. Single combined form: display name, AS Fin email, AS
             Fin display name.
             ================================================================ --%>
        <asp:Panel ID="pnlManage" runat="server" Visible="false" CssClass="card">
            <div class="page-head" style="margin-bottom: 1rem;">
                <div>
                    <h2 style="margin: 0;">
                        Manage
                        <asp:Literal ID="litCmProgram" runat="server" />
                        <asp:Literal ID="litCmDisplayName" runat="server" />
                    </h2>
                </div>
                <div>
                    <asp:Button ID="btnCloseManage" runat="server" CssClass="btn btn-ghost"
                        Text="Done" OnClick="btnCloseManage_Click" CausesValidation="false" />
                </div>
            </div>

            <%-- Display name section --%>
            <div class="panel-section">
                <div class="panel-section-title">Display name</div>
                <p class="help-line">Friendly name shown in send-outs and the Manage header. Optional — when blank, the program code is used.</p>
                <div class="form-row-inline">
                    <asp:TextBox ID="txtDisplayName" runat="server" CssClass="input" MaxLength="200"
                        placeholder="Friendly name (optional)" />
                    <asp:Button ID="btnSaveDisplayName" runat="server" CssClass="btn btn-secondary"
                        Text="Save name" OnClick="btnSaveDisplayName_Click" CausesValidation="false" />
                    <asp:HiddenField ID="hfCmId" runat="server" />
                </div>
            </div>

            <%-- AS Fin email section --%>
            <div class="panel-section">
                <div class="panel-section-title">AS Fin email</div>
                <p class="help-line">
                    The AS Fin team mailbox for this group. Receives the group-summary review email.
                    Also used as the <em>From</em> address on per-POC review emails so POC replies
                    land in this mailbox. Both fields are required &mdash; or leave both blank to clear.
                    Only <code>@defence.gov.au</code> addresses are accepted.
                </p>
                <div class="config-grid">
                    <div>
                        <label for="txtEmail">Email address</label>
                        <asp:TextBox ID="txtEmail" runat="server" CssClass="input" MaxLength="200"
                            placeholder="as.fin.&lt;cm&gt;@defence.gov.au" />
                    </div>
                    <div>
                        <label for="txtEmailDisplayName">Display name</label>
                        <asp:TextBox ID="txtEmailDisplayName" runat="server" CssClass="input" MaxLength="200"
                            placeholder="AS Fin &lt;CM Program&gt;" />
                    </div>
                </div>
                <div>
                    <asp:Button ID="btnSaveEmail" runat="server" CssClass="btn btn-primary"
                        Text="Save email" OnClick="btnSaveEmail_Click" CausesValidation="false" />
                    <asp:Button ID="btnClearEmail" runat="server" CssClass="btn btn-ghost"
                        Text="Clear email" OnClick="btnClearEmail_Click" CausesValidation="false"
                        OnClientClick="return confirm('Clear the configured AS Fin email and display name? Sends to this group will be blocked until reconfigured.');" />
                </div>
            </div>
        </asp:Panel>

        <%-- ================================================================
             CM group list
             ================================================================ --%>
        <div class="card">
            <h2>Groups</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptCms" runat="server"
                              OnItemCommand="rptCms_ItemCommand"
                              OnItemDataBound="rptCms_ItemDataBound">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Program</th>
                                    <th>Display name</th>
                                    <th>AS Fin email</th>
                                    <th>AS Fin display name</th>
                                    <th>Status</th>
                                    <th class="num">Open docs</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr runat="server" id="trRow">
                            <td>
                                <strong><%# LPPIHelper.Enc(Eval("Program")) %></strong>
                                <asp:Literal runat="server" ID="litEditFlag" />
                            </td>
                            <td><%# LPPIHelper.Enc(Eval("DisplayName")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("Email")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("EmailDisplayName")) %></td>
                            <td>
                                <%# Convert.ToInt32(Eval("EmailConfigured")) == 1
                                    ? "<span class=\"pill pill-config-ok\">Configured</span>"
                                    : "<span class=\"pill pill-config-missing\">Not configured</span>" %>
                            </td>
                            <td class="num"><%# Eval("OpenDocs") %></td>
                            <td class="num">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text="Manage"
                                    CommandName="Manage" CommandArgument='<%# Eval("CmID") %>' />
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
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
