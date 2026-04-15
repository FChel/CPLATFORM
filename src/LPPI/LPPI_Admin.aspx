<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Admin.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Admin" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Dashboard</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("dashboard") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Dashboard</h1>
                <p class="lead">Late Payment Penalty Interest review — admin overview.</p>
            </div>
            <div class="btn-row">
                <a class="btn btn-secondary" href="LPPI_Batches.aspx">View batches</a>
                <a class="btn btn-primary"   href="LPPI_Load.aspx">Load new file</a>
            </div>
        </div>

        <asp:PlaceHolder ID="phWarnings" runat="server" />

        <div class="stat-grid">
            <div class="stat">
                <div class="lbl">Total invoices</div>
                <div class="val"><asp:Literal ID="litTotal" runat="server" Text="0"/></div>
                <div class="sub">across <asp:Literal ID="litBatches" runat="server" Text="0"/> batches</div>
            </div>
            <div class="stat ok">
                <div class="lbl">Reviewed</div>
                <div class="val"><asp:Literal ID="litReviewed" runat="server" Text="0"/></div>
            </div>
            <div class="stat">
                <div class="lbl">Outstanding</div>
                <div class="val"><asp:Literal ID="litOutstanding" runat="server" Text="0"/></div>
            </div>
            <div class="stat">
                <div class="lbl">Open packages</div>
                <div class="val"><asp:Literal ID="litOpen" runat="server" Text="0"/></div>
            </div>
            <div class="stat warn">
                <div class="lbl">Near deadline</div>
                <div class="val"><asp:Literal ID="litNear" runat="server" Text="0"/></div>
            </div>
            <div class="stat err">
                <div class="lbl">Overdue</div>
                <div class="val"><asp:Literal ID="litOverdue" runat="server" Text="0"/></div>
            </div>
        </div>

        <div class="card">
            <h2>Open review packages</h2>
            <p style="color:var(--ink-3);font-size:13px;">Each package was emailed to a Capability Manager group. Use Send-outs to create new ones, or send a reminder when a package is approaching its due date.</p>
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead>
                        <tr>
                            <th>Package</th><th>Capability Manager</th><th>Created</th><th>Due</th>
                            <th class="num">Docs</th><th class="num">Reviewed</th><th>Status</th><th></th>
                        </tr>
                    </thead>
                    <tbody>
                        <asp:Repeater ID="rptPackages" runat="server">
                            <ItemTemplate>
                                <tr>
                                    <td>#<%# Eval("PackageID") %></td>
                                    <td><strong><%# CPlatform.LPPI.LPPIHelper.Enc(Eval("CmDisplay")) %></strong></td>
                                    <td><%# CPlatform.LPPI.LPPIHelper.FormatDate(Eval("CreatedDate")) %></td>
                                    <td><%# CPlatform.LPPI.LPPIHelper.FormatDate(Eval("DueDate")) %></td>
                                    <td class="num"><%# Eval("DocCount") %></td>
                                    <td class="num"><%# Eval("ReviewedCount") %></td>
                                    <td><%# RenderStatusPill(Container.DataItem) %></td>
                                    <td class="actions">
                                        <asp:LinkButton runat="server" CssClass="btn btn-secondary btn-sm"
                                            CommandName="Remind"
                                            CommandArgument='<%# Eval("PackageID") %>'
                                            OnCommand="OnPackageCommand"
                                            Text="Send reminder"
                                            Visible='<%# (bool)Eval("CanRemind") %>' />
                                    </td>
                                </tr>
                            </ItemTemplate>
                            <FooterTemplate></FooterTemplate>
                        </asp:Repeater>
                        <asp:PlaceHolder ID="phNoPackages" runat="server" Visible="false">
                            <tr><td colspan="8" class="muted" style="text-align:center;padding:24px;">No open packages.</td></tr>
                        </asp:PlaceHolder>
                    </tbody>
                </table>
            </div>
        </div>

        <div class="card">
            <h2>Recent loads</h2>
            <div class="tbl-wrap">
                <table class="tbl">
                    <thead>
                        <tr>
                            <th>Batch</th><th>File</th><th>Loaded</th><th>By</th>
                            <th class="num">In file</th><th class="num">Inserted</th>
                            <th class="num">Skipped</th><th class="num">Failed</th>
                        </tr>
                    </thead>
                    <tbody>
                        <asp:Repeater ID="rptBatches" runat="server">
                            <ItemTemplate>
                                <tr>
                                    <td>#<%# Eval("BatchID") %></td>
                                    <td><%# CPlatform.LPPI.LPPIHelper.Enc(Eval("FileName")) %></td>
                                    <td><%# CPlatform.LPPI.LPPIHelper.FormatDate(Eval("LoadedDate"), "dd/MM/yyyy HH:mm") %></td>
                                    <td><%# CPlatform.LPPI.LPPIHelper.Enc(Eval("LoadedByName")) %></td>
                                    <td class="num"><%# Eval("RowsInFile") %></td>
                                    <td class="num"><%# Eval("RowsInserted") %></td>
                                    <td class="num"><%# Eval("RowsSkipped") %></td>
                                    <td class="num"><%# Eval("RowsFailed") %></td>
                                </tr>
                            </ItemTemplate>
                        </asp:Repeater>
                    </tbody>
                </table>
            </div>
        </div>
    </main>

    <footer class="lppi-footer">
        Defence Finance Group · LPPI Review · <%= CurrentEnv %>
    </footer>
</div>
</form>
</body>
</html>
