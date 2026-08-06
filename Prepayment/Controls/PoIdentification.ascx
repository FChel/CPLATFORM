<%@ Control Language="C#" AutoEventWireup="true" CodeFile="PoIdentification.ascx.cs" Inherits="Prepayment.Web.Controls.PPMPoIdentification" %>
<div class="page">
  <div class="page-title">Purchase Order Identification</div>
  <div class="page-sub">Search ERP commitment data (PO / vendor / WBS / delivery group) &middot; Flag delivery schedule lines as prepayment at line-item level &middot; Review existing prepayment POs</div>

  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <div class="search-card">
    <h3>🔍 Search Commitment Data (SQL Server — refreshed daily)</h3>
    <div class="search-grid">
      <div class="fld"><label>PO Number</label><input type="text" id="f-po" placeholder="e.g. 3000077540" value="<%# Server.HtmlEncode(SearchPo) %>" /></div>
      <div class="fld"><label>Vendor Name</label><input type="text" id="f-vendor" placeholder="e.g. JFD Australia" value="<%# Server.HtmlEncode(SearchVendor) %>" /></div>
      <div class="fld"><label>WBS Element</label><input type="text" id="f-project" placeholder="e.g. S-0017839-01-01-04-05" value="<%# Server.HtmlEncode(SearchProject) %>" /></div>
      <div class="fld"><label>Delivery Group</label>
        <select id="f-group" style="padding:8px 10px;border:1px solid var(--border);border-radius:6px;background:var(--surface2);min-width:180px">
          <%# GroupOptionsHtml %>
        </select>
      </div>
      <div style="display:flex;gap:8px;align-items:flex-end">
        <button type="button" class="btn primary" onclick="tab1Search()">Search</button>
        <button type="button" class="btn" onclick="tab1Clear()">Clear</button>
      </div>
    </div>
  </div>

  <div class="section-head">
    <h3>Search Results — New Commitment Lines</h3>
    <div style="display:flex;gap:8px;align-items:center">
      <span style="font-size:13px;color:var(--muted)"><%# SearchResultCount %> results</span>
      <span class="badge b">Daily SQL load: <%# Server.HtmlEncode(LastLoadLabel) %></span>
    </div>
  </div>
  <div class="table-wrap">
    <table class="po-results">
      <thead>
        <tr><th>PO Number</th><th>Vendor</th><th>WBS Element</th><th>Delivery Group</th><th>Capability Mgr</th><th class="center">Type</th><th class="num">Total Commitment</th><th class="num">Open Commitment</th><th>PO Date</th><th class="center">Lines</th><th class="center">Prepayment lines</th><th class="act">Action</th></tr>
      </thead>
      <tbody>
        <asp:Repeater ID="rptSearchResults" runat="server">
          <ItemTemplate>
            <tr style='<%# Eval("RowStyle") %>'>
              <td class="po-num"><%# Eval("PoNumber") %></td>
              <td class="vendor"><%# Eval("Vendor") %></td>
              <td class="wbs" title='<%# Eval("Wbs") %>'><%# Eval("Wbs") %></td>
              <td><span class="grp-pill" title='<%# Server.HtmlEncode((string)Eval("DeliveryGroupName")) %>'><%# Eval("DeliveryGroup") %></span></td>
              <td class="mgr"><%# Eval("CapabilityManager") %></td>
              <td class="center"><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td>
              <td class="num"><%# Eval("PoValue") %></td>
              <td class="num"><%# Eval("CurrentCommitment") %></td>
              <td class="nowrap"><%# Eval("PoDate") %></td>
              <td class="center"><%# Eval("Lines") %></td>
              <td class="center"><span class='badge <%# Eval("PrepaymentLines.Cls") %>'><%# Eval("PrepaymentLines.Text") %></span></td>
              <td class="act"><button type="button" class='<%# (bool)Eval("ActionPrimary") ? "btn sm primary" : "btn sm" %>' onclick="tab1OpenSchedule('<%# Eval("PoNumber") %>')"><%# Eval("ActionText") %></button></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
  </div>

  <asp:PlaceHolder ID="phSchedule" runat="server" Visible="false">
  <div class="delivery-card">
    <div class="delivery-head">
      <div>
        <h4>Delivery Schedule — PO <%# ScheduleHeader.PoNumber %> / <%# ScheduleHeader.Vendor %></h4>
        <div class="delivery-sub"><%# ScheduleHeader.DeliveryGroup %> &middot; WBS <%# ScheduleHeader.Project %> &middot; <%# ScheduleHeader.CapexOpex %> &middot; Cap. Mgr <%# ScheduleHeader.CapabilityManager %> &middot; <%# ScheduleHeader.Currency %> &middot; Total <%# ScheduleTotalLabel %> &middot; <%# ScheduleHeader.LineCount %> schedule lines</div>
      </div>
      <span class="badge w"><%# ScheduleHeader.LinesNeedingClassification %> lines need classification</span>
    </div>
    <table>
      <thead>
        <tr><th>Line</th><th>Acct</th><th>Item / Description</th><th>GL Account</th><th>WBS Element</th><th>CAPEX/OPEX</th><th>Scheduled date</th><th>Qty</th><th>Line value</th><th style="min-width:150px">Prepayment?</th><th>Notes</th></tr>
      </thead>
      <tbody>
        <asp:Repeater ID="rptDeliveryLines" runat="server">
          <ItemTemplate>
            <tr style='<%# Eval("RowStyle") %>' data-line-id='<%# Eval("DeliveryLineId") %>'>
              <td><%# Eval("Line") %></td><td><%# Eval("AcctAssign") %></td><td><%# Eval("Description") %><br /><span style="color:var(--muted);font-size:12px"><%# Eval("WbsDescription") %></span></td>
              <td><%# Eval("GlAccount") %><br /><span style="color:var(--muted);font-size:12px"><%# Eval("GlDescription") %></span></td><td style="font-size:12px"><%# Eval("Wbs") %></td><td><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td><td><%# Eval("ScheduledDate") %></td><td><%# Eval("Qty") %></td><td><%# Eval("LineValue") %></td>
              <td>
                <%-- §3.1: 3-state dropdown selector — Prepayment / Not Prepayment / Pending --%>
                <select class="line-flag" onchange="tab1ToggleLine(this)" style="font-size:12px;padding:5px 8px;border:1px solid var(--border);border-radius:6px;background:var(--surface2);min-width:140px">
                  <option value="Prepayment"<%# Eval("Flag").ToString() == "Prepayment" ? " selected" : "" %>>Prepayment</option>
                  <option value="NotPrepayment"<%# Eval("Flag").ToString() == "NotPrepayment" ? " selected" : "" %>>Not Prepayment</option>
                  <option value="Pending"<%# Eval("Flag").ToString() == "Pending" ? " selected" : "" %>>Pending</option>
                </select>
              </td>
              <td>
                <asp:PlaceHolder runat="server" Visible='<%# (bool)Eval("Decided") %>'><span style="font-size:12px;color:var(--muted)"><%# Eval("Note") %></span></asp:PlaceHolder>
                <asp:PlaceHolder runat="server" Visible='<%# !(bool)Eval("Decided") %>'><input type="text" class="line-note" placeholder="Add note…" value='<%# Eval("Note") %>' style="font-size:12px;padding:5px 8px;border:1px solid var(--border);border-radius:6px;background:var(--surface2);width:100%" /></asp:PlaceHolder>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
    <div class="save-bar">
      <span><%# ScheduleSummary %></span>
      <div style="display:flex;gap:8px">
        <button type="button" class="btn" onclick="tab1SaveDraft()">Save draft</button>
        <button type="button" class="btn success" onclick="tab1Confirm('<%# ScheduleHeader.PoId %>')">Confirm &amp; move to prepayment tab</button>
      </div>
    </div>
  </div>
  </asp:PlaceHolder>

  <div class="section-head">
    <h3>Existing Prepayment POs (previously flagged)</h3>
    <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
      <span class="badge s"><%# ExistingActiveCount %> active</span>
      <div class="vf-wrap" id="vf-wrap">
        <button type="button" class="vf-btn <%# !string.IsNullOrEmpty(ExistingVendorFilter) ? "active" : "" %>" id="vf-btn" onclick="vfToggle(event)">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M3 6h18M7 12h10M11 18h2"/></svg>
          Filter by vendor
          <span class="vf-count" id="vf-count" style="<%# string.IsNullOrEmpty(ExistingVendorFilter) ? "display:none" : "" %>"><%# string.IsNullOrEmpty(ExistingVendorFilter) ? "" : ExistingVendorFilter.Split(',').Length.ToString() %></span>
        </button>
        <div class="vf-drop" id="vf-drop">
          <div class="vf-search-wrap">
            <input type="text" id="vf-search" placeholder="Search vendors…" oninput="vfSearch(this.value)" autocomplete="off" />
          </div>
          <div class="vf-list" id="vf-list"></div>
          <div class="vf-footer">
            <button type="button" class="vf-clear" onclick="vfClear()">Clear all</button>
            <button type="button" class="vf-apply" onclick="vfApply()">Apply filter</button>
          </div>
        </div>
      </div>
    </div>
  </div>
  <script>var VF_ACTIVE = '<%# Server.HtmlEncode(ExistingVendorFilter) %>';</script>
  <div class="table-wrap">
    <table id="existing-pos-table">
      <thead><tr><th>PO Number</th><th>Vendor</th><th>Delivery Group</th><th>Recognised amount</th><th>Amortisation status</th><th>Outstanding balance</th><th>Action</th></tr></thead>
      <tbody>
        <asp:Repeater ID="rptExistingPos" runat="server">
          <ItemTemplate>
            <tr><td class="po-num"><%# Eval("PoNumber") %></td><td class="vendor"><%# Eval("Vendor") %></td><td><%# Eval("DeliveryGroup") %></td><td><%# Eval("RecognisedAmount") %></td><td><span class='badge <%# Eval("AmortisationStatus.Cls") %>'><%# Eval("AmortisationStatus.Text") %></span></td><td><%# Eval("OutstandingBalance") %></td><td><button type="button" class="btn sm" onclick='tab1OpenExisting("<%# Eval("PoNumber") %>", "<%# Eval("ActionTarget") %>")'><%# Eval("ActionText") %></button></td></tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
  </div>
</div>
