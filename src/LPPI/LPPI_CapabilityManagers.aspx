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
                <asp:Repeater ID="rptCms" runat="server" OnItemCommand="rptCms_ItemCommand">
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
                        <tr>
                            <td><strong><%# LPPIHelper.Enc(Eval("Program")) %></strong></td>
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
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost" Text="Manage emails"
                                    CommandName="Manage" CommandArgument='<%# Eval("CmID") %>' />
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text='<%# (bool)Eval("IsActive") ? "Disable" : "Enable" %>'
                                    CommandName="Toggle" CommandArgument='<%# Eval("CmID") %>' />
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

        <%-- Email management panel — shown when the operator clicks "Manage emails" --%>
        <asp:Panel ID="pnlEmails" runat="server" Visible="false" CssClass="card">
            <h2>Recipients for <asp:Literal ID="litCmName" runat="server" /></h2>
            <p class="muted">
                Enter one or more addresses separated by commas. Each address will be added individually.
                Tick <strong>CC</strong> to add all of them as CC recipients instead of TO.
            </p>
            <div class="form-grid">
                <div class="form-row form-row-wide">
                    <label for="txtEmail">Email address(es)</label>
                    <%-- TO-DO #2: single comma-separated field replaces the old single-address input --%>
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
                                    <th>Status</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td><%# LPPIHelper.Enc(Eval("Email")) %></td>
                            <td><%# (bool)Eval("IsCC") ? "CC" : "TO" %></td>
                            <td>
                                <%# (bool)Eval("IsActive")
                                    ? "<span class=\"pill pill-open\">Active</span>"
                                    : "<span class=\"pill pill-closed\">Disabled</span>" %>
                            </td>
                            <td class="num">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost"
                                    Text='<%# (bool)Eval("IsActive") ? "Disable" : "Enable" %>'
                                    CommandName="Toggle" CommandArgument='<%# Eval("CmEmailID") %>' />
                                <%-- TO-DO #3: delete button --%>
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

    </main>
</div>
</form>
</body>
</html>
