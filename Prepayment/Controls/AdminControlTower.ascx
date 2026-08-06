<%@ Control Language="C#" AutoEventWireup="true" CodeFile="AdminControlTower.ascx.cs" Inherits="Prepayment.Web.Controls.PPMAdminControlTower" %>
<div class="page">
  <div class="page-title">Admin Control Tower</div>
  <div class="page-sub">End-to-end lifecycle visibility &middot; Exceptions &middot; Approval watch &middot; Export batches</div>

  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <!-- Process Tracker — full width (10-column table needs the room) -->
  <div class="table-wrap tracker-wrap">
    <div class="table-head-row"><h3>Process tracker — all active prepayments</h3><button type="button" class="btn sm" onclick="adminExportTracker()">Export</button></div>
    <div class="table-scroll">
      <table class="tracker-table">
        <thead>
          <tr>
            <th>PO Number</th><th>Vendor</th><th class="center">CAPEX/OPEX</th><th class="num">Amount</th>
            <th class="center">PO flag</th><th class="center">Invoice</th><th>Setup</th>
            <th>Recognised</th><th>Amortising</th><th class="center">Export</th><th>Status</th>
          </tr>
        </thead>
        <tbody>
          <asp:Repeater ID="rptProcessTracker" runat="server">
            <ItemTemplate>
              <tr>
                <td class="po-num"><%# Eval("PoNumber") %></td>
                <td class="vendor"><%# Eval("Vendor") %></td>
                <td class="center"><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td>
                <td class="num"><%# Eval("Amount") %></td>
                <td class="center" style='<%# Eval("PoFlagStyle") %>'><%# Eval("PoFlag") %></td>
                <td class="center" style='<%# Eval("InvoiceStyle") %>'><%# Eval("Invoice") %></td>
                <td style='<%# Eval("SetupStyle") %>'><%# Eval("Setup") %></td>
                <td style='<%# Eval("RecognisedStyle") %>'><%# Eval("Recognised") %></td>
                <td style='<%# Eval("AmortisingStyle") %>'><%# Eval("Amortising") %></td>
                <td class="center" style='<%# Eval("ExportStyle") %>'><%# Eval("Export") %></td>
                <td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td>
              </tr>
            </ItemTemplate>
          </asp:Repeater>
          <asp:PlaceHolder ID="phNoTrackerRows" runat="server" Visible="false">
            <tr><td colspan="11" style="text-align:center;color:var(--faint);font-size:12px;padding:16px">No active prepayments found.</td></tr>
          </asp:PlaceHolder>
        </tbody>
      </table>
    </div>
  </div>

  <!-- Exceptions + Period Summary — side by side below the tracker -->
  <div class="two-col admin-detail">
    <div class="card">
      <div class="card-head">
        <h3>Exceptions &amp; blocked items</h3>
        <span class="badge e"><asp:Literal ID="litExceptionCount" runat="server" /> open</span>
      </div>
      <div class="card-body" style="padding:0">
        <asp:Repeater ID="rptExceptions" runat="server">
          <ItemTemplate>
            <div style='padding:12px 16px;border-bottom:1px solid #f0f2f4;display:flex;justify-content:space-between;align-items:center;gap:10px'>
              <div>
                <strong style="font-size:13px"><%# Eval("Title") %></strong>
                <div style="font-size:12px;color:var(--muted)"><%# Eval("Detail") %></div>
              </div>
              <span class='badge <%# Eval("Tag.Cls") %>'><%# Eval("Tag.Text") %></span>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="phNoExceptions" runat="server" Visible="false">
          <div style="padding:16px;text-align:center;color:var(--faint);font-size:13px">No open exceptions.</div>
        </asp:PlaceHolder>
      </div>
    </div>

    <div class="card">
      <div class="card-head"><h3>Period summary &mdash; <asp:Literal ID="litPeriodLabel" runat="server" /></h3></div>
      <div class="card-body">
        <div style="display:grid;gap:0">
          <asp:Repeater ID="rptPeriodSummary" runat="server">
            <ItemTemplate>
              <div style='display:flex;justify-content:space-between;font-size:13px;padding:9px 0;<%# (Container.ItemIndex < 4) ? "border-bottom:1px solid #f0f2f4" : "" %>'>
                <span style="color:var(--muted)"><%# Eval("Label") %></span>
                <strong class='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></strong>
              </div>
            </ItemTemplate>
          </asp:Repeater>
        </div>
      </div>
    </div>
  </div>

  <!-- §3.4 Admin actions: force-advance a stuck item, reassign approver, clear exception, re-export failed batch -->
  <div class="section-head admin-actions-head"><h3>Admin Action</h3><span class="badge a">Intervene &amp; override</span></div>
  <div class="two-col admin-actions">
    <div class="card">
      <div class="card-head"><h3>Force-advance &amp; reassign approver</h3></div>
      <div class="card-body">
        <div class="fld" style="margin-bottom:10px"><label>Stuck item (PO)</label>
          <select id="adm-stuck">
            <option value="">— select a stuck item —</option>
            <asp:Repeater ID="rptStuckItems" runat="server">
              <ItemTemplate><option value='<%# Eval("PoNumber") %>'><%# Eval("Label") %></option></ItemTemplate>
            </asp:Repeater>
          </select>
          <asp:PlaceHolder ID="phNoStuck" runat="server" Visible="false">
            <div style="font-size:12px;color:var(--faint);margin-top:4px">No stuck items — every journal is approved or exported.</div>
          </asp:PlaceHolder>
        </div>
        <div style="display:flex;gap:8px;margin-bottom:14px">
          <button type="button" class="btn sm primary" onclick="adminForceAdvance()">Force-advance one stage</button>
        </div>
        <div class="fld" style="margin-bottom:10px"><label>Reassign approver to</label>
          <select id="adm-approver">
            <option value="">— select approver —</option>
            <asp:Repeater ID="rptApprovers" runat="server">
              <ItemTemplate><option value='<%# Eval("Id") %>'><%# Eval("DisplayName") %></option></ItemTemplate>
            </asp:Repeater>
          </select>
        </div>
        <div style="display:flex;gap:8px">
          <button type="button" class="btn sm" onclick="adminReassignApprover()">Reassign approver (uses PO above)</button>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-head"><h3>Clear exception &amp; re-export batch</h3></div>
      <div class="card-body">
        <div class="fld" style="margin-bottom:8px"><label>Open exception</label>
          <select id="adm-exception">
            <option value="">— select an exception —</option>
            <asp:Repeater ID="rptOpenExceptions" runat="server">
              <ItemTemplate><option value='<%# Eval("Id") %>'><%# Eval("Title") %> — <%# Eval("Detail") %></option></ItemTemplate>
            </asp:Repeater>
          </select>
          <asp:PlaceHolder ID="phNoClearable" runat="server" Visible="false">
            <div style="font-size:12px;color:var(--faint);margin-top:4px">No clearable (stored) exceptions open.</div>
          </asp:PlaceHolder>
        </div>
        <div style="margin-bottom:10px">
          <label style="font-size:12px;font-weight:700;color:var(--muted);display:block;margin-bottom:5px">Resolution note</label>
          <textarea id="adm-exc-note" style="width:100%;height:48px;padding:9px 11px;border:1px solid var(--border);border-radius:8px;font-size:13px;font-family:inherit;background:var(--surface2);resize:vertical" placeholder="Document the resolution…"></textarea>
        </div>
        <div style="display:flex;gap:8px;margin-bottom:14px">
          <button type="button" class="btn sm success" onclick="adminClearException()">Clear exception</button>
        </div>
        <div class="fld" style="margin-bottom:10px"><label>Failed export batch</label>
          <select id="adm-batch">
            <option value="">— select a failed batch —</option>
            <asp:Repeater ID="rptFailedBatches" runat="server">
              <ItemTemplate><option value='<%# Eval("Id") %>'><%# Eval("Label") %></option></ItemTemplate>
            </asp:Repeater>
          </select>
          <asp:PlaceHolder ID="phNoBatches" runat="server" Visible="false">
            <div style="font-size:12px;color:var(--faint);margin-top:4px">No failed export batches.</div>
          </asp:PlaceHolder>
        </div>
        <div style="display:flex;gap:8px">
          <button type="button" class="btn sm" onclick="adminReExportBatch()">Re-export failed batch</button>
        </div>
      </div>
    </div>
  </div>
</div>
