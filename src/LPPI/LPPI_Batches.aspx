<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Batches.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Batches" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Batches</title>
    <link rel="stylesheet" href="../css/lppi.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("batches") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Load batches</h1>
                <p class="lead">Every file load is recorded here. Select a batch to drill down to the documents it contained.</p>
            </div>
            <div class="btn-row">
                <a class="btn btn-primary" href="LPPI_Load.aspx">Load new file</a>
            </div>
        </div>

        <div class="card">
            <h2>Recent batches</h2>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptBatches" runat="server" OnItemCommand="rptBatches_ItemCommand">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Batch</th>
                                    <th>File</th>
                                    <th>Loaded</th>
                                    <th>By</th>
                                    <th class="num">In file</th>
                                    <th class="num">Inserted</th>
                                    <th class="num">Skipped</th>
                                    <th class="num">Failed</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td>#<%# Eval("BatchID") %></td>
                            <td><%# LPPIHelper.Enc(Eval("FileName")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("LoadedDate"), "dd/MM/yyyy HH:mm") %></td>
                            <td><%# LPPIHelper.Enc(Eval("LoadedByName")) %></td>
                            <td class="num"><%# Eval("RowsInFile") %></td>
                            <td class="num"><%# Eval("RowsInserted") %></td>
                            <td class="num"><%# Eval("RowsSkipped") %></td>
                            <td class="num"><%# Eval("RowsFailed") %></td>
                            <td class="num">
                                <asp:LinkButton runat="server" CssClass="btn btn-sm btn-ghost" Text="View documents"
                                    CommandName="View" CommandArgument='<%# Eval("BatchID") %>' />
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

        <asp:Panel ID="pnlDocs" runat="server" Visible="false" CssClass="card">
            <h2>Lines in batch #<asp:Literal ID="litBatchId" runat="server" /></h2>
            <div class="stat-grid">
                <div class="stat"><span class="stat-label">Total lines</span><span class="stat-value"><asp:Literal ID="litTotal" runat="server" /></span></div>
                <div class="stat stat-ok"><span class="stat-label">Reviewed</span><span class="stat-value"><asp:Literal ID="litReviewed" runat="server" /></span></div>
                <div class="stat stat-warn"><span class="stat-label">Outstanding</span><span class="stat-value"><asp:Literal ID="litOutstanding" runat="server" /></span></div>
                <div class="stat"><span class="stat-label">Exported</span><span class="stat-value"><asp:Literal ID="litExported" runat="server" /></span></div>
            </div>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptDocs" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Doc No</th>
                                    <th class="num">Item seq</th>
                                    <th>Vendor</th>
                                    <th>PO</th>
                                    <th>CM Program</th>
                                    <th>Invoice date</th>
                                    <th>Payment date</th>
                                    <th class="num">Days late</th>
                                    <th class="num">Interest</th>
                                    <th>Status</th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td><%# LPPIHelper.SapFiNumberHtml(Eval("DocNoAccounting"), Eval("CompanyCode"), Eval("ClearingMonth")) %></td>
                            <td class="num"><%# string.Format("{0:000}", Eval("ItemSequence")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                            <td><%# LPPIHelper.SapPoNumberHtml(Eval("PoNumber")) %></td>
                            <td><%# LPPIHelper.Enc(Eval("CapabilityManagerProgram")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("InvoiceDate")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("PaymentRunDate")) %></td>
                            <td class="num"><%# Eval("DaysVariance") %></td>
                            <td class="num"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></td>
                            <td>
                                <%# Eval("ReasonCode") == DBNull.Value
                                    ? "<span class=\"pill pill-pending\">Pending</span>"
                                    : "<span class=\"pill pill-reviewed\">" + LPPIHelper.Enc(Eval("ReasonCode")) + "</span>" %>
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

    <footer class="lppi-footer">
        <span>LPPI Review · <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
