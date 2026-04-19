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
        /* Subtle highlight for the CM row whose recipients are being edited.
           Scoped to this page — lives here so we do not churn the shared
           lppi.css for a single-page affordance. */
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
                <p class="subtitle">Review LPPI lines and record pay / no-pay decisions</p>
                <p class="lead">Maintain Capability Manager groups and the email recipients used for review send-outs.</p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <%-- Email management panel — surfaces at the top of the page when
             the operator clicks "Manage emails" on a CM row, so it lands
             in view rather than below the list. --%>
        <asp:Panel ID="pnlEmails" runat="server" Visible="false" CssClass="card">
            <div class="page-head" style="margin-bottom:0.5rem;">
                <div>
                    <h2 style="margin:0;">Recipients for <asp:Literal ID="litCmName" runat="server" /></h2>
                </div>
                <div>
                    <asp:Button ID="btnCloseEmails" runat="server" CssClass="btn btn-ghost"
                        Text="Done" OnClick="btnCloseEmails_Click" CausesValidation="false" />
                </div>
            </div>
            <p class="muted">
                Enter one or more addresses separated by commas or semicolons. Each address will be added individually.
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
                    <asp:Button ID="btnAddEmail" runat="server" CssClass="btn btn-primary" Text="Add recipient(s)" OnClick="btnAddEmail_Click" />
                    <asp:HiddenField ID="hfCmId" runat="server" />
                </div>
            </div>

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
        </asp:Panel>

        <div class="card">
            <h2>Add or update a group</h2>
            <p class="muted">Program is the canonical group key and must match the <code>CAPABILITY_MANAGER_PROGRAM</code> value seen in BODS extracts.</p>
            <div class="form-grid">
                <div class="form-row">
                    <label for="txtProgram">Program</label>
                    <asp:TextBox ID="txtProgram" runat="server" CssClass="input" MaxLength="200" placeholder="e.g. ARMY" />
                </div>
                <div class="form-row">
                    <label for="txtDisplayName">Display name (optional)</label>
                    <asp:TextBox ID="txtDisplayName" runat="server" CssClass="input" MaxLength="200" />
                </div>
                <div class="form-row form-row-check">
                    <label><asp:CheckBox ID="chkActive" runat="server" Checked="true" /> Active</label>
                </div>
                <div class="form-row form-row-actions">
                    <asp:Button ID="btnSaveCm" runat="server" CssClass="btn btn-primary" Text="Save group" OnClick="btnSaveCm_Click" />
                </div>
            </div>
        </div>

        <div class="card">
            <h2>Existing groups</h2>
            <div class="tbl-wrap">
                <%-- OnItemCommand wiring is required for "Manage emails" to
                     reach the code-behind — without it clicks post back and
                     silently bubble to nowhere. --%>
                <asp:Repeater ID="rptCms" runat="server"
                              OnItemCommand="rptCms_ItemCommand"
                              OnItemDataBound="rptCms_ItemDataBound">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Program</th>
                                    <th>Display name</th>
                                    <th>Status</th>
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
                            <td>
                                <%# (bool)Eval("IsActive")
                                    ? "<span class=\"pill pill-open\">Active</span>"
                                    : "<span class=\"pill pill-closed\">Inactive</span>" %>
                            </td>
                            <td><%# LPPIHelper.Enc(Eval("ToList")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("CcList")) %></td>
                            <td class="num"><%# Eval("OpenDocs") %></td>
                            <td class="num">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text="Manage emails"
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
</div>
</form>
</body>
</html>
