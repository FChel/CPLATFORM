<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Deactivated.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Deactivated" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review &mdash; Deactivated lines</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* ---------- Deactivated page ---------------------------------------
           Pure list page — small explainer card on top, a single grouped
           table below. No write actions; admin watches the list and acts
           upstream (re-extract from BODS, reload). */

        .deact-explainer {
            display: flex;
            gap: 16px;
            align-items: flex-start;
            background: var(--white);
            border: 1px solid var(--line);
            border-left: 4px solid var(--orange);
            border-radius: var(--r);
            padding: 16px 20px;
            margin-bottom: 20px;
            box-shadow: var(--shadow-sm);
        }
        .deact-explainer .icon {
            flex: 0 0 auto;
            width: 32px; height: 32px;
            border-radius: 8px;
            background: var(--orange-soft);
            color: var(--orange-deep);
            display: flex; align-items: center; justify-content: center;
        }
        .deact-explainer .icon svg { width: 18px; height: 18px; }
        .deact-explainer .body { flex: 1; font-size: 13px; color: var(--ink-2); line-height: 1.5; }
        .deact-explainer .body strong { color: var(--ink); }

        /* Stat strip — one number per pill, full width. */
        .deact-stats {
            display: flex;
            gap: 12px;
            margin-bottom: 16px;
            flex-wrap: wrap;
        }
        .deact-stats .stat {
            flex: 1 1 140px;
            background: var(--white);
            border: 1px solid var(--line);
            border-radius: var(--r);
            padding: 12px 16px;
            box-shadow: var(--shadow-sm);
        }
        .deact-stats .stat .label {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            color: var(--ink-3);
            font-weight: 600;
        }
        .deact-stats .stat .value {
            font-size: 22px;
            font-weight: 700;
            color: var(--ink);
            margin-top: 2px;
        }
        .deact-stats .stat.amount .value { color: var(--orange-deep); }

        /* CM group banner row — orange-tinted, full row span. */
        tr.cm-group td {
            background: var(--orange-tint);
            color: var(--orange-deep);
            font-weight: 700;
            font-size: 12px;
            text-transform: uppercase;
            letter-spacing: 0.04em;
            padding: 8px 12px;
        }

        /* Slim columns. */
        .tbl-deact td.col-doc      { white-space: nowrap; font-variant-numeric: tabular-nums; }
        .tbl-deact td.col-line     { white-space: nowrap; text-align: right; font-variant-numeric: tabular-nums; }
        .tbl-deact td.col-amt      { white-space: nowrap; text-align: right; font-variant-numeric: tabular-nums; }
        .tbl-deact td.col-when     { white-space: nowrap; color: var(--ink-3); font-size: 12px; }
        .tbl-deact td.col-comments { font-size: 12px; color: var(--ink-2); max-width: 360px; }
        .tbl-deact td.col-objref   { font-size: 12px; color: var(--ink-3); white-space: nowrap; }

        .empty-state-deact {
            background: var(--white);
            border: 1px solid var(--line);
            border-radius: var(--r-lg);
            padding: 40px 24px;
            text-align: center;
            color: var(--ink-3);
            box-shadow: var(--shadow-sm);
        }
        .empty-state-deact h2 { color: var(--ink); margin: 0 0 6px; font-size: 16px; }
        .empty-state-deact p  { margin: 0; font-size: 13px; }
    </style>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("deactivated") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Deactivated lines</h1>
                <p class="lead">
                    Documents marked <strong>RC-RL</strong> &mdash; <em>Incorrect data, document eligible for reload</em>.
                    Each line below is currently held back from ERP export; the next BODS file that contains a corrected
                    row for the same document will supersede it.
                </p>
            </div>
        </div>

        <%-- Explainer --%>
        <div class="deact-explainer">
            <div class="icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="9"/>
                    <path d="M12 7v5"/>
                    <path d="M12 16h.01"/>
                </svg>
            </div>
            <div class="body">
                A line ends up here when AS Fin or a POC marks it <strong>RC-RL</strong> on the reviewer page and the
                package is then finalised. The original line stays in the database for the audit trail; it is excluded
                from ERP exports until BODS supplies a corrected row at the same <em>DocNoAccounting / ItemSequence</em>,
                at which point the next file load supersedes it and the corrected row enters a fresh review package.
                <br /><br />
                <strong>What to do here:</strong> nothing on this screen &mdash; this is a watch-list. To clear an entry,
                resolve the underlying data issue with BODS / the source system and re-extract; the next load will pick it up.
            </div>
        </div>

        <%-- Stats --%>
        <asp:PlaceHolder ID="phStats" runat="server" Visible="false">
            <div class="deact-stats">
                <div class="stat">
                    <div class="label">Lines awaiting reload</div>
                    <div class="value"><asp:Literal ID="litLineCount"  runat="server" /></div>
                </div>
                <div class="stat">
                    <div class="label">Documents</div>
                    <div class="value"><asp:Literal ID="litDocCount"   runat="server" /></div>
                </div>
                <div class="stat">
                    <div class="label">Capability managers</div>
                    <div class="value"><asp:Literal ID="litCmCount"    runat="server" /></div>
                </div>
                <div class="stat amount">
                    <div class="label">Held interest ($)</div>
                    <div class="value"><asp:Literal ID="litTotalDollars" runat="server" /></div>
                </div>
            </div>
        </asp:PlaceHolder>

        <%-- Table or empty state --%>
        <asp:PlaceHolder ID="phEmpty" runat="server" Visible="false">
            <div class="empty-state-deact">
                <h2>No deactivated lines</h2>
                <p>Nothing has been marked <strong>RC-RL</strong> in any finalised package, or every previously deactivated row has been superseded by a subsequent file load.</p>
            </div>
        </asp:PlaceHolder>

        <asp:PlaceHolder ID="phResults" runat="server" Visible="false">
            <div class="card" style="padding: 0; overflow: hidden;">
                <div class="tbl-wrap">
                    <table class="tbl tbl-deact">
                        <thead>
                            <tr>
                                <th class="col-doc">Document / Line</th>
                                <th>Vendor</th>
                                <th>PO</th>
                                <th class="col-amt num">Interest ($)</th>
                                <th class="col-comments">Reviewer comments</th>
                                <th class="col-objref">Obj Ref</th>
                                <th>Reviewed by</th>
                                <th class="col-when">Package finalised</th>
                            </tr>
                        </thead>
                        <tbody>
                            <asp:Repeater ID="rptLines" runat="server" OnItemDataBound="rptLines_ItemDataBound">
                                <ItemTemplate>
                                    <%-- Group banner — emitted only on the first row of each CM group --%>
                                    <asp:PlaceHolder ID="phGroup" runat="server" Visible="false">
                                        <tr class="cm-group">
                                            <td colspan="8">
                                                <%# LPPIHelper.Enc(Eval("CapabilityManagerProgram")) %>
                                                &mdash;
                                                <asp:Literal ID="litGroupCount" runat="server" />
                                                line<asp:Literal ID="litGroupPlural" runat="server" />
                                                ($<asp:Literal ID="litGroupAmount" runat="server" />)
                                            </td>
                                        </tr>
                                    </asp:PlaceHolder>
                                    <tr>
                                        <td class="col-doc">
                                            <%# LPPIHelper.Enc(Eval("DocNoAccounting")) %>
                                            <span style="color: var(--ink-3); font-weight: 400;">/ <%# string.Format("{0:000}", Eval("ItemSequence")) %></span>
                                        </td>
                                        <td><%# LPPIHelper.Enc(Eval("VendorName")) %></td>
                                        <td><%# LPPIHelper.Enc(Eval("PoNumber")) %></td>
                                        <td class="col-amt num"><%# LPPIHelper.FormatMoney(Eval("InterestPayable")) %></td>
                                        <td class="col-comments"><%# LPPIHelper.Enc(Eval("Comments")) %></td>
                                        <td class="col-objref"><%# LPPIHelper.Enc(Eval("ObjectiveReference")) %></td>
                                        <td><%# LPPIHelper.Enc(Eval("ReviewedByName")) %></td>
                                        <td class="col-when">
                                            <%# LPPIHelper.FormatDate(Eval("FinalisedDate")) %>
                                            <span style="color: var(--ink-3); font-size: 11px;">
                                                (pkg #<%# Eval("PackageID") %>)
                                            </span>
                                        </td>
                                    </tr>
                                </ItemTemplate>
                            </asp:Repeater>
                        </tbody>
                    </table>
                </div>
            </div>
        </asp:PlaceHolder>

    </main>

    <footer class="lppi-footer">
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
