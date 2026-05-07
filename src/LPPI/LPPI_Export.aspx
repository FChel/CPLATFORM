<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Export.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Export" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review — Export to ERP</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* Picker layout — finalised packages awaiting export. Wider than
           the standard tbl, since we need to fit checkbox / id / program /
           dates / counts / dollars / actions. */
        .picker-table .col-pick   { width: 36px; }
        .picker-table .col-pkgid  { width: 70px;  white-space: nowrap; }
        .picker-table .col-prog   { min-width: 160px; }
        .picker-table .col-date   { width: 110px; white-space: nowrap; }
        .picker-table .col-by     { width: 180px; }
        .picker-table .col-num    { width: 68px;  text-align: right; }
        .picker-table .col-money  { width: 110px; text-align: right; font-variant-numeric: tabular-nums; }
        .picker-table .col-action { width: 110px; white-space: nowrap; }

        /* Sticky totals strip at the bottom of the picker. Mirrors the
           reviewer-page bulk-bar pattern visually so admins recognise the
           "you have N selected" feedback. Updates live via JS when
           checkboxes toggle. */
        .totals-strip {
            position: sticky;
            bottom: 16px;
            background: var(--ink);
            color: #fff;
            border-radius: var(--r-lg);
            padding: 14px 22px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 18px;
            box-shadow: var(--shadow-lg);
            margin-top: 16px;
            z-index: 20;
            flex-wrap: wrap;
        }
        .totals-strip .summary {
            display: flex;
            gap: 24px;
            flex-wrap: wrap;
            font-size: 13px;
        }
        .totals-strip .summary .item {
            display: flex;
            flex-direction: column;
            gap: 2px;
        }
        .totals-strip .summary .lbl {
            font-size: 10px;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            color: rgba(255,255,255,0.65);
            font-weight: 600;
        }
        .totals-strip .summary .val {
            font-size: 16px;
            font-weight: 600;
            color: #fff;
            font-variant-numeric: tabular-nums;
        }
        .totals-strip .summary .val.money {
            color: var(--orange);
        }
        .totals-strip .actions {
            display: flex;
            gap: 8px;
            align-items: center;
        }
        .totals-strip .actions .btn-primary {
            background: var(--orange);
            border-color: var(--orange);
        }
        .totals-strip .actions .btn-primary:hover:not(:disabled) {
            background: var(--orange-hover);
            border-color: var(--orange-hover);
        }
        .totals-strip .actions .btn-primary:disabled {
            opacity: 0.45;
            cursor: not-allowed;
        }

        /* Empty state in the picker */
        .picker-empty {
            text-align: center;
            padding: 48px 24px;
            background: var(--bg);
            border-radius: var(--r);
            color: var(--ink-3);
            font-size: 14px;
        }
        .picker-empty strong { color: var(--ink-2); }
    </style>
    <script>
        // Live update of the totals strip as checkboxes toggle. Uses the
        // data-pkg-* attributes baked onto each row so we never go back
        // to the server for the calculation.
        function updateTotals() {
            var checks = document.querySelectorAll('.pkgPick:checked');
            var pkgs   = checks.length;
            var docs   = 0;
            var total  = 0;
            for (var i = 0; i < checks.length; i++) {
                var row = checks[i].closest('tr');
                if (!row) continue;
                docs  += parseInt(row.getAttribute('data-pkg-docs')  || '0', 10) || 0;
                total += parseFloat(row.getAttribute('data-pkg-total') || '0') || 0;
            }
            var elPkgs = document.getElementById('selPkgs');
            var elDocs = document.getElementById('selDocs');
            var elTot  = document.getElementById('selTotal');
            var elBtn  = document.getElementById('<%= btnExport.ClientID %>');
            if (elPkgs) elPkgs.textContent = pkgs;
            if (elDocs) elDocs.textContent = docs;
            if (elTot)  elTot.textContent  = '$' + total.toLocaleString('en-AU',
                { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            if (elBtn)  elBtn.disabled = pkgs === 0;
        }

        // Select-all toggles every visible row checkbox.
        function toggleAll(master) {
            var boxes = document.querySelectorAll('.pkgPick');
            for (var i = 0; i < boxes.length; i++) boxes[i].checked = master.checked;
            updateTotals();
        }

        // Confirm before generating — re-states the package count.
        function confirmExport(btnId) {
            var checks = document.querySelectorAll('.pkgPick:checked');
            if (checks.length === 0) return false;
            return confirm('Generate the ERP payment file for ' + checks.length +
                ' package' + (checks.length === 1 ? '' : 's') + '?\n\n' +
                'These packages will be marked as Exported and locked.');
        }

        document.addEventListener('DOMContentLoaded', function () {
            // Bind change handler to every row checkbox.
            var boxes = document.querySelectorAll('.pkgPick');
            for (var i = 0; i < boxes.length; i++) boxes[i].addEventListener('change', updateTotals);
            updateTotals();
        });
    </script>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("export") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Export to ERP</h1>
                <p class="lead">
                    Pick one or more <strong>Finalised</strong> packages, then generate the Payment Request bulk-upload spreadsheet.
                    Selected packages are stamped as <strong>Exported</strong> and locked once the file is generated.
                </p>
            </div>
        </div>

        <asp:PlaceHolder ID="phMessage" runat="server" />

        <div class="card">
            <h2>Finalised packages awaiting export</h2>

            <asp:PlaceHolder ID="phEmpty" runat="server" Visible="false">
                <div class="picker-empty">
                    <strong>No Finalised packages.</strong><br/>
                    Packages must be finalised on the reviewer page before they can be exported.
                </div>
            </asp:PlaceHolder>

            <asp:PlaceHolder ID="phPicker" runat="server">
                <div class="tbl-wrap">
                    <asp:Repeater ID="rptFinalised" runat="server">
                        <HeaderTemplate>
                            <table class="tbl picker-table">
                                <thead>
                                    <tr>
                                        <th class="col-pick"><input type="checkbox" onclick="toggleAll(this)" title="Select all" /></th>
                                        <th class="col-pkgid">Package</th>
                                        <th class="col-prog">Capability Manager</th>
                                        <th class="col-date">Finalised</th>
                                        <th class="col-by">Finalised by</th>
                                        <th class="col-num">Docs</th>
                                        <th class="col-num">Payable</th>
                                        <th class="col-money">Payable $</th>
                                        <th class="col-action"></th>
                                    </tr>
                                </thead>
                                <tbody>
                        </HeaderTemplate>
                        <ItemTemplate>
                            <tr data-pkg-docs='<%# Eval("PayableDocCount") %>'
                                data-pkg-total='<%# Eval("PayableInterest") %>'>
                                <td class="col-pick">
                                    <%-- Plain HTML checkbox with runat=server. Using <asp:CheckBox>
                                         here would wrap the input in a <span class="pkgPick"> and
                                         the JS lookup via .pkgPick:checked would silently miss.
                                         Server-side selection still works via FindControl, just
                                         cast to HtmlInputCheckBox instead of CheckBox. --%>
                                    <input type="checkbox" runat="server" id="chkPick" class="pkgPick" />
                                    <asp:HiddenField runat="server" ID="hfPackageId" Value='<%# Eval("PackageID") %>' />
                                </td>
                                <td class="col-pkgid">#<%# Eval("PackageID") %></td>
                                <td class="col-prog"><strong><%# LPPIHelper.Enc(Eval("Program")) %></strong></td>
                                <td class="col-date"><%# LPPIHelper.FormatDate(Eval("FinalisedDate")) %></td>
                                <td class="col-by"><%# LPPIHelper.Enc(Eval("FinalisedBy")) %></td>
                                <td class="col-num"><%# Eval("DocCount") %></td>
                                <td class="col-num"><%# Eval("PayableDocCount") %></td>
                                <td class="col-money">$<%# LPPIHelper.FormatMoney(Eval("PayableInterest")) %></td>
                                <td class="col-action">
                                    <a class="btn btn-sm btn-secondary"
                                       href='<%# "LPPI_Review.aspx?t=" + System.Uri.EscapeDataString(Convert.ToString(Eval("Token"))) %>'
                                       target="_blank" rel="noopener">Open review &rarr;</a>
                                </td>
                            </tr>
                        </ItemTemplate>
                        <FooterTemplate>
                                </tbody>
                            </table>
                        </FooterTemplate>
                    </asp:Repeater>
                </div>

                <%-- Sticky totals strip --%>
                <div class="totals-strip" aria-live="polite">
                    <div class="summary">
                        <div class="item">
                            <span class="lbl">Selected</span>
                            <span class="val"><span id="selPkgs">0</span> package(s)</span>
                        </div>
                        <div class="item">
                            <span class="lbl">Payable docs</span>
                            <span class="val"><span id="selDocs">0</span></span>
                        </div>
                        <div class="item">
                            <span class="lbl">Total payable</span>
                            <span class="val money" id="selTotal">$0.00</span>
                        </div>
                    </div>
                    <div class="actions">
                        <asp:Button ID="btnExport" runat="server" CssClass="btn btn-primary"
                                    Text="Generate ERP file"
                                    OnClick="btnExport_Click"
                                    OnClientClick="return confirmExport(this.id);" />
                    </div>
                </div>
            </asp:PlaceHolder>
        </div>

        <div class="card">
            <h2>Recent export batches</h2>
            <p class="muted" style="font-size:13px;">
                Re-download a previously generated file. The file bytes are stored against the export batch row in the database.
            </p>
            <div class="tbl-wrap">
                <asp:Repeater ID="rptBatches" runat="server">
                    <HeaderTemplate>
                        <table class="tbl">
                            <thead>
                                <tr>
                                    <th>Batch</th>
                                    <th>File</th>
                                    <th>Generated</th>
                                    <th>By</th>
                                    <th class="num">Packages</th>
                                    <th class="num">Docs</th>
                                    <th class="num">Lines</th>
                                    <th class="num">Total $</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                    </HeaderTemplate>
                    <ItemTemplate>
                        <tr>
                            <td>#<%# Eval("ExportBatchID") %></td>
                            <td><%# LPPIHelper.Enc(Eval("FileName")) %></td>
                            <td><%# LPPIHelper.FormatDate(Eval("GeneratedDate"), "dd/MM/yyyy HH:mm") %></td>
                            <td><%# LPPIHelper.Enc(Eval("GeneratedByName")) %></td>
                            <td class="num"><%# Eval("PackageCount") %></td>
                            <td class="num"><%# Eval("DocumentCount") %></td>
                            <td class="num"><%# Eval("LineCount") %></td>
                            <td class="num">$<%# LPPIHelper.FormatMoney(Eval("TotalAmount")) %></td>
                            <td class="actions">
                                <a class="btn btn-sm btn-secondary"
                                   href='<%# "LPPI_Export_Download.ashx?b=" + Eval("ExportBatchID") %>'>
                                    Download
                                </a>
                            </td>
                        </tr>
                    </ItemTemplate>
                    <FooterTemplate>
                            </tbody>
                        </table>
                    </FooterTemplate>
                </asp:Repeater>
                <asp:PlaceHolder ID="phNoBatches" runat="server" Visible="false">
                    <p class="muted" style="text-align:center;padding:24px 0;">No export batches yet.</p>
                </asp:PlaceHolder>
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
