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
    <style>
        /* The lifecycle pill colour rules that previously lived inline here
           have been consolidated into lppi.css (look for "Lifecycle pills").
           Page-specific layout for the LPPI exposure block remains here. */

        /* =====================================================================
           LPPI exposure block — dollar totals
           Layout: hero card on the left (40%), three breakdown cards on the
           right (60% split evenly) on desktop. Single column on mobile.
           ===================================================================== */
        .exposure-section {
            margin-bottom: 24px;
        }
        .exposure-section .section-label {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            color: var(--ink-3);
            font-weight: 700;
            margin-bottom: 10px;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .exposure-section .section-label::after {
            content: "";
            flex: 1;
            height: 1px;
            background: var(--line);
        }

        .exposure-grid {
            display: grid;
            grid-template-columns: minmax(280px, 1.4fr) repeat(3, 1fr);
            gap: 12px;
        }
        @media (max-width: 1100px) {
            .exposure-grid {
                grid-template-columns: 1fr 1fr;
            }
            .exposure-hero {
                grid-column: 1 / -1;
            }
        }
        @media (max-width: 640px) {
            .exposure-grid {
                grid-template-columns: 1fr;
            }
            .exposure-hero {
                grid-column: auto;
            }
        }

        /* Hero card — total LPPI exposure */
        .exposure-hero {
            background: linear-gradient(135deg, var(--orange-soft) 0%, var(--white) 60%);
            border: 1px solid var(--orange-tint);
            border-radius: var(--r-lg);
            padding: 22px 24px;
            box-shadow: var(--shadow-sm);
            position: relative;
            overflow: hidden;
        }
        .exposure-hero::before {
            content: "";
            position: absolute;
            top: 0; left: 0;
            width: 4px; height: 100%;
            background: var(--orange);
        }
        .exposure-hero .lbl {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            color: var(--orange-deep);
            font-weight: 700;
        }
        .exposure-hero .val {
            font-size: 36px;
            font-weight: 700;
            color: var(--orange);
            margin-top: 6px;
            line-height: 1.05;
            font-variant-numeric: tabular-nums;
            letter-spacing: -0.01em;
        }
        .exposure-hero .currency {
            font-size: 22px;
            font-weight: 600;
            color: var(--orange-deep);
            margin-right: 2px;
            vertical-align: 4px;
        }
        .exposure-hero .sub {
            font-size: 12px;
            color: var(--ink-3);
            margin-top: 4px;
        }

        /* Breakdown cards — Payable / Not payable / Awaiting review */
        .exposure-card {
            background: var(--white);
            border: 1px solid var(--line);
            border-radius: var(--r-lg);
            padding: 16px 18px;
            box-shadow: var(--shadow-sm);
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            min-height: 122px;
        }
        .exposure-card .lbl {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: var(--ink-3);
            font-weight: 600;
        }
        .exposure-card .val {
            font-size: 22px;
            font-weight: 700;
            color: var(--ink);
            margin-top: 4px;
            line-height: 1.1;
            font-variant-numeric: tabular-nums;
        }
        .exposure-card .val .currency {
            font-size: 13px;
            font-weight: 600;
            color: var(--ink-3);
            margin-right: 1px;
            vertical-align: 3px;
        }

        /* Progress bar showing share of total — colour-coded per outcome */
        .exposure-bar {
            margin-top: 10px;
        }
        .exposure-bar .track {
            height: 5px;
            background: var(--line-2);
            border-radius: 999px;
            overflow: hidden;
        }
        .exposure-bar .fill {
            height: 100%;
            border-radius: 999px;
            transition: width 0.4s ease;
        }
        .exposure-bar .pct {
            font-size: 11px;
            font-weight: 600;
            color: var(--ink-3);
            margin-top: 4px;
            font-variant-numeric: tabular-nums;
        }

        .exposure-card.payable    .val { color: var(--ok); }
        .exposure-card.payable    .fill { background: var(--ok); }
        .exposure-card.notpayable .val { color: var(--err); }
        .exposure-card.notpayable .fill { background: var(--err); }
        .exposure-card.awaiting   .val { color: var(--warn); }
        .exposure-card.awaiting   .fill { background: var(--warn); }
    </style>
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
                <p class="lead">Late Payment Penalty Interest review admin overview.</p>
            </div>
            <div class="btn-row">
                <a class="btn btn-secondary" href="LPPI_Batches.aspx">View batches</a>
                <a class="btn btn-primary"   href="LPPI_Load.aspx">Load new file</a>
            </div>
        </div>

        <asp:PlaceHolder ID="phWarnings" runat="server" />

        <%-- ================================================================
             LPPI exposure — dollar totals headline
             ================================================================ --%>
        <section class="exposure-section">
            <div class="section-label">LPPI exposure</div>
            <div class="exposure-grid">
                <div class="exposure-hero">
                    <div class="lbl">Total exposure</div>
                    <div class="val">
                        <span class="currency">$</span><asp:Literal ID="litExpTotal" runat="server" Text="0.00"/>
                    </div>
                    <div class="sub">across <asp:Literal ID="litExpDocs" runat="server" Text="0"/> documents</div>
                </div>

                <div class="exposure-card payable">
                    <div>
                        <div class="lbl">Payable (confirmed)</div>
                        <div class="val">
                            <span class="currency">$</span><asp:Literal ID="litExpPayable" runat="server" Text="0.00"/>
                        </div>
                    </div>
                    <div class="exposure-bar">
                        <div class="track"><div class="fill" style="width: <%= ExpPayablePct %>%"></div></div>
                        <div class="pct"><%= ExpPayablePct %>% of total</div>
                    </div>
                </div>

                <div class="exposure-card notpayable">
                    <div>
                        <div class="lbl">Not payable</div>
                        <div class="val">
                            <span class="currency">$</span><asp:Literal ID="litExpNotPayable" runat="server" Text="0.00"/>
                        </div>
                    </div>
                    <div class="exposure-bar">
                        <div class="track"><div class="fill" style="width: <%= ExpNotPayablePct %>%"></div></div>
                        <div class="pct"><%= ExpNotPayablePct %>% of total</div>
                    </div>
                </div>

                <div class="exposure-card awaiting">
                    <div>
                        <div class="lbl">Awaiting review</div>
                        <div class="val">
                            <span class="currency">$</span><asp:Literal ID="litExpAwaiting" runat="server" Text="0.00"/>
                        </div>
                    </div>
                    <div class="exposure-bar">
                        <div class="track"><div class="fill" style="width: <%= ExpAwaitingPct %>%"></div></div>
                        <div class="pct"><%= ExpAwaitingPct %>% of total</div>
                    </div>
                </div>
            </div>
        </section>

        <%-- Existing stat-grid below — counts, not dollars --%>
        <div class="stat-grid">
            <div class="stat">
                <div class="lbl">Total documents</div>
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
            <p style="color:var(--ink-3);font-size:13px;">
                Packages in flight (NotSent / Sent / In review / Finalised). Use Send-outs to issue them, or send a reminder when one is approaching its due date.
                Finalised packages are queued for export — visit the Export page to ship them to ERP.
                Admins can open the review link for any package for QA — the reviewer page will be read-only when the package is not currently active.
            </p>
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
                                        <%# RenderPackageActions(Eval("PackageID"), Eval("Token"), Eval("Status"), (bool)Eval("CanRemind")) %>
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
                            <FooterTemplate></FooterTemplate>
                        </asp:Repeater>
                    </tbody>
                </table>
            </div>
            <p style="margin-top:12px;">
                <a href="LPPI_Batches.aspx" class="btn btn-ghost btn-sm">View all batches &rarr;</a>
            </p>
        </div>

    </main>

    <footer class="lppi-footer">
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>

    <%-- Hidden postback mechanism for the remind button rendered inside RenderPackageActions.
         The JS sets hfRemindPackageId then clicks btnRemindTrigger. --%>
    <asp:HiddenField ID="hfRemindPackageId" runat="server" />
    <asp:Button ID="btnRemindTrigger" runat="server" Style="display:none;"
        OnClick="btnRemindTrigger_Click" CausesValidation="false" />
</div>
</form>
</body>
</html>
