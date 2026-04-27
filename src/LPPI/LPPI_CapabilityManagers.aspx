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

        /* Display-name sub-section inside the Manage panel */
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
        .display-name-row {
            display: flex;
            align-items: center;
            gap: 10px;
        }
        /* Ensure the display-name input inherits the app font stack, not the
           browser default. The .input class in lppi.css covers this, but an
           explicit font-family here guards against any specificity collision. */
        .display-name-row .input {
            flex: 1;
            font-family: var(--font);
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
                <p class="lead">Capability Manager groups are created automatically when an ERP export file is loaded. Edit the display name or manage recipients for each group using the Manage button.</p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- ================================================================
             Manage panel — surfaces above the list when operator clicks Manage.
             Contains display-name editing AND recipient management in one card.
             ================================================================ --%>
        <asp:Panel ID="pnlEmails" runat="server" Visible="false" CssClass="card">
            <div class="page-head" style="margin-bottom: 1rem;">
                <div>
                    <h2 style="margin: 0;">
                        Recipients for
                        <asp:Literal ID="litCmProgram" runat="server" /> —
                        <asp:Literal ID="litCmDisplayName" runat="server" />
                    </h2>
                </div>
                <div>
                    <asp:Button ID="btnCloseEmails" runat="server" CssClass="btn btn-ghost"
                        Text="Done" OnClick="btnCloseEmails_Click" CausesValidation="false" />
                </div>
            </div>

            <%-- Display name section --%>
            <div class="panel-section">
                <div class="panel-section-title">Display name</div>
                <div class="display-name-row">
                    <asp:TextBox ID="txtDisplayName" runat="server" CssClass="input" MaxLength="200"
                        placeholder="Friendly name shown in send-outs (optional)" />
                    <asp:Button ID="btnSaveDisplayName" runat="server" CssClass="btn btn-secondary"
                        Text="Save name" OnClick="btnSaveDisplayName_Click" CausesValidation="false" />
                    <asp:HiddenField ID="hfCmId" runat="server" />
                </div>
            </div>

            <%-- Add recipients section --%>
            <div class="panel-section">
                <div class="panel-section-title">Add recipient(s)</div>
                <p class="muted" style="margin-bottom: 10px;">
                    Enter one or more addresses separated by commas or semicolons.
                    Tick <strong>CC</strong> to add all of them as CC recipients instead of TO.
                </p>
                <div class="form-grid">
                    <div class="form-row form-row-wide">
                        <label for="txtEmail">Email address(es)</label>
                        <asp:TextBox ID="txtEmail" runat="server" CssClass="input" MaxLength="2000"
                            placeholder="name@defence.gov.au, other@defence.gov.au" />
                    </div>
                    <div class="form-row form-row-check">
                        <label><asp:CheckBox ID="chkCc" runat="server" /> CC (otherwise TO)</label>
                    </div>
                    <div class="form-row form-row-actions">
                        <asp:Button ID="btnAddEmail" runat="server" CssClass="btn btn-primary"
                            Text="Add recipient(s)" OnClick="btnAddEmail_Click" />
                    </div>
                </div>
            </div>

            <%-- Recipients table --%>
            <div class="panel-section">
                <div class="panel-section-title">Current recipients</div>
                <div class="tbl-wrap">
                    <asp:Repeater ID="rptEmails" runat="server" OnItemCommand="rptEmails_ItemCommand">
                        <HeaderTemplate>
                            <table class="tbl">
                                <thead>
                                    <tr>
                                        <th>Email</th>
                                        <th>Role</th>
                                        <th class="num"></th>
                                    </tr>
                                </thead>
                                <tbody>
                        </HeaderTemplate>
                        <ItemTemplate>
                            <tr>
                                <td><%# LPPIHelper.Enc(Eval("Email")) %></td>
                                <td><%# (bool)Eval("IsCC") ? "CC" : "TO" %></td>
                                <td class="num">
                                    <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost btn-danger"
                                        Text="Delete"
                                        CommandName="Delete" CommandArgument='<%# Eval("CmEmailID") %>'
                                        OnClientClick="return confirm('Delete this email address permanently?');" />
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
                                    <th>Recipients (TO)</th>
                                    <th>Recipients (CC)</th>
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
                            <td><%# LPPIHelper.Enc(Eval("ToList")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("CcList")) %></td>
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
