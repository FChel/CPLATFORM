<%@ Control Language="C#" AutoEventWireup="true" CodeFile="PrepaymentReport.ascx.cs" Inherits="Prepayment.Web.Controls.PPMPrepaymentReport" %>
<div class="page">
  <div class="page-title">Prepayment Report by Group</div>
  <div class="page-sub">Prepayment position and amortisation progress by delivery group &middot; Export for reporting</div>

  <!-- Filters -->
  <div class="search-card">
    <div style="display:grid;grid-template-columns:1fr 1fr 1fr 1fr auto;gap:12px;align-items:flex-end">
      <div class="fld"><label>Delivery group</label>
        <select id="rep-group" onchange="reportApply()">
          <option value="">All groups</option>
          <asp:Repeater ID="rptGroupOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("Id") %>'<%# IsGroup(Eval("Id")) %>><%# Eval("DeliveryGroupCode") %> — <%# Eval("GroupName") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <div class="fld"><label>Period</label>
        <select id="rep-period" onchange="reportApply()">
          <asp:Repeater ID="rptPeriodOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("PeriodKey") %>'<%# IsPeriod(Eval("PeriodKey")) %>><%# Eval("PeriodLabel") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <div class="fld"><label>Status</label>
        <select id="rep-status" onchange="reportApply()">
          <option value="All"<%= IsStatus("All") %>>All</option>
          <option value="Amortising"<%= IsStatus("Amortising") %>>Amortising</option>
          <option value="Completed"<%= IsStatus("Completed") %>>Completed</option>
          <option value="Pending"<%= IsStatus("Pending") %>>Pending approval</option>
          <option value="Suspended"<%= IsStatus("Suspended") %>>Suspended</option>
          <option value="Blocked"<%= IsStatus("Blocked") %>>Blocked</option>
        </select>
      </div>
      <div class="fld"><label>Account type</label>
        <select id="rep-gl" onchange="reportApply()">
          <option value="">All accounts</option>
          <asp:Repeater ID="rptGlOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("Id") %>'<%# IsGl(Eval("Id")) %>><%# Eval("GlAccount") %> — <%# Eval("GlDescription") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <div style="display:flex;gap:8px">
        <button type="button" class="btn primary" onclick="reportApply()">Run report</button>
        <button type="button" class="btn" onclick="reportExport('csv')">Export CSV</button>
      </div>
    </div>
  </div>

  <!-- KPI summary -->
  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <!-- Report table -->
  <div class="table-wrap">
    <div class="table-head-row">
      <h3>Prepayment balances by group</h3>
      <div style="display:flex;gap:8px;align-items:center">
        <span class="badge b"><asp:Literal ID="litPeriodLabel" runat="server" /></span>
        <button type="button" class="btn sm" onclick="reportExport('excel')">Export Excel</button>
        <button type="button" class="btn sm primary" onclick="reportExport('pdf')">Export PDF</button>
      </div>
    </div>
    <table>
      <thead>
        <tr>
          <th>Group</th>
          <th>Group name</th>
          <th>GL account</th>
          <th>Vendor</th>
          <th style="text-align:center">CAPEX/OPEX</th>
          <th style="text-align:right">Recognised amount</th>
          <th style="text-align:right">Amortised to date</th>
          <th style="text-align:right">Outstanding balance</th>
          <th style="min-width:120px">% amortised</th>
          <th style="text-align:center">Periods left</th>
          <th>End date</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="rptReportRows" runat="server">
          <ItemTemplate>
            <tr style='<%# Eval("RowStyle") %>;cursor:pointer' onclick='reportDrill(<%# Eval("DeliveryGroupId") %>)' title="Click to load this group's drill-down">
              <td class="po-num"><%# Eval("Group") %></td><td class="vendor"><%# Eval("GroupName") %></td>
              <td title='<%# Eval("GlAccount") %>'><%# Eval("GlAccount") %></td><td><%# Eval("Vendor") %></td>
              <td style="text-align:center"><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td>
              <td style="text-align:right;font-variant-numeric:tabular-nums"><%# Eval("Recognised") %></td>
              <td style='text-align:right;font-variant-numeric:tabular-nums;<%# Eval("AmortisedStyle") %>'><%# Eval("Amortised") %></td>
              <td style='text-align:right;font-variant-numeric:tabular-nums;<%# Eval("OutstandingStyle") %>'><%# Eval("Outstanding") %></td>
              <td>
                <div style="display:flex;align-items:center;gap:6px">
                  <div style="flex:1;background:var(--border);border-radius:999px;height:7px;overflow:hidden;min-width:60px">
                    <div style='background:var(--success);height:100%;width:<%# Eval("PercentWidth") %>;border-radius:999px'></div>
                  </div>
                  <span style="font-size:11px;color:var(--muted);min-width:36px;text-align:right"><%# Eval("PercentLabel") %></span>
                </div>
              </td>
              <td style="text-align:center"><%# Eval("PeriodsLeft") %></td><td><%# Eval("EndDate") %></td>
              <td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="phNoRows" runat="server" Visible="false">
          <tr><td colspan="12" style="text-align:center;color:var(--faint);font-size:12px;padding:16px">No prepayment balances match the selected filters.</td></tr>
        </asp:PlaceHolder>
        <tr style="background:#f7f8fa;font-weight:700">
          <td colspan="5">Total — <%= RowCount %> row<%= RowCount == 1 ? "" : "s" %></td>
          <td style="text-align:right;font-variant-numeric:tabular-nums"><%= TotalRecognised %></td>
          <td style="text-align:right;font-variant-numeric:tabular-nums;color:var(--success)"><%= TotalAmortised %></td>
          <td style="text-align:right;font-variant-numeric:tabular-nums;color:var(--blue)"><%= TotalOutstanding %></td>
          <td colspan="4"></td>
        </tr>
      </tbody>
    </table>
  </div>

  <!-- Per-group drill-down -->
  <asp:PlaceHolder ID="phDrilldown" runat="server" Visible="false">
  <div id="report-drill" class="section-head"><h3>Group drill-down — <asp:Literal ID="litDrillTitle" runat="server" /></h3><span class="badge b">Click any row above to load</span></div>
  <div class="two-col">
    <div class="card">
      <div class="card-head"><h3>Amortisation schedule</h3><span class="badge s"><asp:Literal ID="litDrillProgress" runat="server" /></span></div>
      <div class="card-body" style="padding:14px">
        <table class="sched-table">
          <thead><tr><th>#</th><th>Period</th><th style="text-align:right">Amount</th><th style="text-align:right">Cumulative</th><th>Status</th></tr></thead>
          <tbody>
            <asp:Repeater ID="rptDrilldownSchedule" runat="server">
              <ItemTemplate>
                <tr style='<%# Eval("RowStyle") %>'><td><%# Eval("Num") %></td><td><%# Eval("Period") %></td><td style="text-align:right"><%# Eval("Amount") %></td><td style="text-align:right"><%# Eval("Cumulative") %></td><td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td></tr>
              </ItemTemplate>
            </asp:Repeater>
            <tr class="total-row"><td colspan="2" style="text-align:right">Total</td><td style="text-align:right"><asp:Literal ID="litScheduleTotal" runat="server" /></td><td></td><td></td></tr>
          </tbody>
        </table>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h3>Balance movement</h3></div>
      <div class="card-body">
        <div class="mini-sched">
          <asp:Repeater ID="rptBalanceMovement" runat="server">
            <ItemTemplate>
              <div class="row"><span><%# Eval("Label") %></span><strong style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></strong></div>
            </ItemTemplate>
          </asp:Repeater>
        </div>
        <div style="margin-top:14px;background:var(--surface2);border:1px solid var(--border);border-radius:8px;padding:12px">
          <div style="font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.04em;color:var(--faint);margin-bottom:8px">Progress</div>
          <div style="background:var(--border);border-radius:999px;height:10px;overflow:hidden">
            <div style='background:var(--success);height:100%;width:<%= ProgressWidth %>;border-radius:999px'></div>
          </div>
          <div style="display:flex;justify-content:space-between;font-size:12px;color:var(--muted);margin-top:6px">
            <span><asp:Literal ID="litAmortisedLabel" runat="server" /></span><span><asp:Literal ID="litRemainingLabel" runat="server" /></span>
          </div>
        </div>
      </div>
    </div>
  </div>
  </asp:PlaceHolder>
</div>
