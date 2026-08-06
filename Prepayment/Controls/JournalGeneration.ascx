<%@ Control Language="C#" AutoEventWireup="true" CodeFile="JournalGeneration.ascx.cs" Inherits="Prepayment.Web.Controls.PPMJournalGeneration" %>
<div class="page">
  <div class="page-title">Journal Generation</div>
  <div class="page-sub">Part A: Recognise prepayments — capitalise items expensed against PO &middot; Part B: Amortise recognised prepayments — periodic expense journals</div>

  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <div class="stab-row">
    <div class="stab active" onclick="switchInner('t3',0)">Part A — Recognise Prepayments (Capitalise)</div>
    <div class="stab" onclick="switchInner('t3',1)">Part B — Amortise Prepayments (Expense)</div>
  </div>

  <script>
    var JVF_ACTIVE_REC   = '<%= Server.HtmlEncode(RecVendorFilter) %>';
    var JVF_ACTIVE_AMORT = '<%= Server.HtmlEncode(AmortVendorFilter) %>';
  </script>

  <!-- Part A -->
  <div class="inner-pane active" id="t3-0">
    <div class="pane-bar">
      <div><div class="pane-bar-title">Recognition Journal — Prepayment Capitalisation</div>
      <div class="pane-bar-sub">Items expensed at invoice posting are reclassified to the prepayment asset account, linked to the PO account assignment.</div></div>
      <div style="display:flex;gap:8px;align-items:center">
        <div class="vf-wrap" id="jvf-wrap-rec">
          <button type="button" class="vf-btn <%= !string.IsNullOrEmpty(RecVendorFilter) ? "active" : "" %>" id="jvf-btn-rec" onclick="jvfToggle('rec',event)">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M3 6h18M7 12h10M11 18h2"/></svg>
            Filter by vendor
            <span class="vf-count" id="jvf-count-rec" style="<%= string.IsNullOrEmpty(RecVendorFilter) ? "display:none" : "" %>"><%= string.IsNullOrEmpty(RecVendorFilter) ? "" : RecVendorFilter.Split(',').Length.ToString() %></span>
          </button>
          <div class="vf-drop" id="jvf-drop-rec">
            <div class="vf-search-wrap"><input type="text" id="jvf-search-rec" placeholder="Search vendors…" oninput="jvfSearch('rec',this.value)" autocomplete="off" /></div>
            <div class="vf-list" id="jvf-list-rec"></div>
            <div class="vf-footer">
              <button type="button" class="vf-clear" onclick="jvfClear('rec')">Clear all</button>
              <button type="button" class="vf-apply" onclick="jvfApply('rec')">Apply filter</button>
            </div>
          </div>
        </div>
        <button type="button" class="btn sm primary" onclick="journalApproveAll('Recognition')">Approve all ready</button>
      </div>
    </div>
    <div style="padding:16px 18px 0">
      <div style="font-size:12px;font-weight:700;color:var(--faint);text-transform:uppercase;letter-spacing:.04em;margin-bottom:10px">Recognition journal queue</div>
    </div>
    <div style="padding:0 18px 16px">
      <div class="table-wrap" style="margin-bottom:16px">
        <table>
          <thead><tr><th>Journal ref</th><th>PO / Invoice</th><th>Vendor</th><th>CAPEX/OPEX</th><th>Dr (asset)</th><th>Cr (expense)</th><th>Amount</th><th>Period</th><th>Status</th><th>Action</th></tr></thead>
          <tbody id="jvf-rec-tbody">
            <asp:Repeater ID="rptRecognitionJournals" runat="server">
              <ItemTemplate>
                <tr>
                  <td class="po-num"><a href='Default.aspx?journal=<%# Eval("JournalId") %>#pane-2' style="color:var(--blue);text-decoration:none"><%# Eval("JournalRef") %></a></td><td><%# Eval("PoInvoice") %></td><td class="vendor"><%# Eval("Vendor") %></td><td><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td><td><%# Eval("DrAsset") %></td><td><%# Eval("CrExpense") %></td><td><strong><%# Eval("Amount") %></strong></td><td><%# Eval("Period") %></td>
                  <td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td>
                  <td><button type="button" class='<%# (bool)Eval("ActionSuccess") ? "btn sm success" : ((bool)Eval("ActionPrimary") ? "btn sm primary" : "btn sm") %>' onclick="<%# (bool)Eval("ActionSuccess") ? "journalExport('" : "journalSelect('" %><%# Eval("JournalId") %>')"><%# Eval("ActionText") %></button></td>
                </tr>
              </ItemTemplate>
            </asp:Repeater>
          </tbody>
        </table>
      </div>
    </div>

    <asp:PlaceHolder ID="phRecognitionDetail" runat="server" Visible="false">
    <div style="padding:0 18px 20px">
      <div style="font-size:12px;font-weight:700;color:var(--faint);text-transform:uppercase;letter-spacing:.04em;margin-bottom:14px">Journal detail — <%# SelectedRef %> (selected)</div>
      <div class="two-col">
        <div class="card">
          <div class="card-head"><h3>Source: PO Account Assignment</h3><span class="badge b">Auto-linked from PO line</span></div>
          <div class="card-body">
            <div class="po-source">
              <div class="title">PO account assignment — <%# SelectedHeader.PoNumber %> / Line <%# SelectedHeader.LineNumber %></div>
              <div class="po-source-grid">
                <asp:Repeater ID="rptPoSource" runat="server">
                  <ItemTemplate>
                    <div class="po-field"><div class="lbl"><%# Eval("Label") %></div><div class="val"><%# Eval("Value") %></div></div>
                  </ItemTemplate>
                </asp:Repeater>
              </div>
            </div>
            <table class="j-table">
              <thead><tr><th>D/C</th><th>Account</th><th>Description</th><th>Cost object</th><th class="amount">Amount (AUD)</th></tr></thead>
              <tbody>
                <asp:Repeater ID="rptDetailEntries" runat="server">
                  <ItemTemplate>
                    <tr><td><span class='<%# ((string)Eval("Dc")) == "Dr" ? "dr" : "cr" %>'><%# Eval("Dc") %></span></td><td class="account"><%# Eval("Account") %></td><td><%# Eval("Description") %></td><td><%# Eval("CostObject") %></td><td class="amount"><%# Eval("Amount") %></td></tr>
                  </ItemTemplate>
                </asp:Repeater>
                <tr class="subtotal"><td colspan="4">Journal total (balanced)</td><td class="amount"><%# DetailTotal %></td></tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="card">
          <div class="card-head"><h3>Approval &amp; audit</h3><span class="badge <%# StatusBadgeCls %>"><%# StatusBadgeText %></span></div>
          <div class="card-body">
            <div style="display:grid;gap:0">
              <asp:Repeater ID="rptApproval" runat="server">
                <ItemTemplate>
                  <div style='display:flex;justify-content:space-between;font-size:13px;padding:9px 0;<%# (Container.ItemIndex < 4) ? "border-bottom:1px solid #f0f2f4" : "" %>'><span style="color:var(--muted)"><%# Eval("Label") %></span><strong><%# Eval("Value") %></strong></div>
                </ItemTemplate>
              </asp:Repeater>
            </div>
            <div style="margin:14px 0 10px"><label style="font-size:12px;font-weight:700;color:var(--muted);display:block;margin-bottom:5px">Approver comments</label>
              <textarea id="approver-comments" style="width:100%;height:72px;padding:9px 11px;border:1px solid var(--border);border-radius:8px;font-size:13px;font-family:inherit;background:var(--surface2);resize:vertical" placeholder="Enter comments or approval note…"></textarea>
            </div>
            <div style="display:flex;gap:8px">
              <asp:PlaceHolder runat="server" Visible='<%# CanSubmit %>'><button type="button" class="btn primary" style="flex:1" onclick="journalSubmit('<%# SelectedHeader.JournalId %>')">Submit for approval</button></asp:PlaceHolder>
              <asp:PlaceHolder runat="server" Visible='<%# CanApprove %>'>
                <button type="button" class="btn success" style="flex:1" onclick="journalApprove('<%# SelectedHeader.JournalId %>')">✓ Approve</button>
                <button type="button" class="btn" style="background:var(--err-bg);color:var(--error);border-color:#f0b0b0;flex:1" onclick="journalReject('<%# SelectedHeader.JournalId %>')">✗ Reject</button>
              </asp:PlaceHolder>
              <asp:PlaceHolder runat="server" Visible='<%# CanExport %>'><button type="button" class="btn success" style="flex:1" onclick="journalExport('<%# SelectedHeader.JournalId %>')">Export to ERP batch</button></asp:PlaceHolder>
            </div>
          </div>
        </div>
      </div>
    </div>
    </asp:PlaceHolder>
  </div>

  <!-- Part B -->
  <div class="inner-pane" id="t3-1">
    <div class="pane-bar">
      <div><div class="pane-bar-title">Amortisation Journals — System Recommended</div>
      <div class="pane-bar-sub">Approved prepayment balances are periodically expensed. The system recommends one journal line per active prepayment per period.</div></div>
      <div style="display:flex;gap:8px;align-items:center">
        <div class="vf-wrap" id="jvf-wrap-amort">
          <button type="button" class="vf-btn <%= !string.IsNullOrEmpty(AmortVendorFilter) ? "active" : "" %>" id="jvf-btn-amort" onclick="jvfToggle('amort',event)">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M3 6h18M7 12h10M11 18h2"/></svg>
            Filter by vendor
            <span class="vf-count" id="jvf-count-amort" style="<%= string.IsNullOrEmpty(AmortVendorFilter) ? "display:none" : "" %>"><%= string.IsNullOrEmpty(AmortVendorFilter) ? "" : AmortVendorFilter.Split(',').Length.ToString() %></span>
          </button>
          <div class="vf-drop" id="jvf-drop-amort">
            <div class="vf-search-wrap"><input type="text" id="jvf-search-amort" placeholder="Search vendors…" oninput="jvfSearch('amort',this.value)" autocomplete="off" /></div>
            <div class="vf-list" id="jvf-list-amort"></div>
            <div class="vf-footer">
              <button type="button" class="vf-clear" onclick="jvfClear('amort')">Clear all</button>
              <button type="button" class="vf-apply" onclick="jvfApply('amort')">Apply filter</button>
            </div>
          </div>
        </div>
        <button type="button" class="btn sm primary" onclick="journalApproveAll('Amortisation')">Approve all ready</button>
      </div>
    </div>
    <div style="padding:16px 18px">
      <div class="table-wrap" style="margin-bottom:18px">
        <table>
          <thead><tr><th>Journal ref</th><th>PO / Prepayment</th><th>Vendor</th><th>CAPEX/OPEX</th><th>Period</th><th>Dr (expense)</th><th>Cr (asset)</th><th>Period amount</th><th>Remaining balance</th><th>Status</th><th>Action</th></tr></thead>
          <tbody id="jvf-amort-tbody">
            <asp:Repeater ID="rptAmortisationJournals" runat="server">
              <ItemTemplate>
                <tr>
                  <td class="po-num"><%# Eval("JournalRef") %></td><td><%# Eval("PoPrepayment") %></td><td class="vendor"><%# Eval("Vendor") %></td><td><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td><td><%# Eval("Period") %></td><td><%# Eval("DrExpense") %></td><td><%# Eval("CrAsset") %></td><td><strong><%# Eval("PeriodAmount") %></strong></td><td><%# Eval("RemainingBalance") %></td>
                  <td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td>
                  <td><button type="button" class='<%# (bool)Eval("ActionSuccess") ? "btn sm success" : ((bool)Eval("ActionPrimary") ? "btn sm primary" : "btn sm") %>' onclick="<%# (bool)Eval("ActionSuccess") ? "journalExport('" : "journalSelect('" %><%# Eval("JournalId") %>')"><%# Eval("ActionText") %></button></td>
                </tr>
              </ItemTemplate>
            </asp:Repeater>
          </tbody>
        </table>
      </div>
      <asp:PlaceHolder ID="phEmptyAmort" runat="server" Visible="false">
        <div style="padding:24px;text-align:center;color:var(--muted);font-size:13px">No amortisation journals in the queue. Generate a schedule on the Prepayment &amp; Amortisation tab to create them.</div>
      </asp:PlaceHolder>
    </div>
  </div>
</div>
