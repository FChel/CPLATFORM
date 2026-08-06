<%@ Control Language="C#" AutoEventWireup="true" CodeFile="AmortisationSetup.ascx.cs" Inherits="Prepayment.Web.Controls.PPMAmortisationSetup" %>
<div class="page">
  <div class="page-title">Prepayment Identification &amp; Amortisation Setup</div>
  <div class="page-sub">Vendor-line invoice notifications (incl. foreign-currency) &middot; Prepayment 514xxx GL &amp; offset GL &middot; Current/non-current &amp; CAPEX/OPEX classification &middot; One-off or scheduled amortisation</div>

  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <div class="alert-bar">
    <div class="alert-icon">🔔</div>
    <div>
      <h4><%# NewInvoiceCount %> new invoices detected on prepayment-flagged PO lines</h4>
      <p>These invoices were posted since the last review. Each must be assessed at vendor line item level to confirm inclusion in the prepayment balance and to configure amortisation.</p>
    </div>
    <div style="flex-shrink:0"><button type="button" class="btn sm primary" onclick="switchInner('t2',0)">Review all</button></div>
  </div>

  <div class="stab-row">
    <div class="stab active" onclick="switchInner('t2',0)">New Invoices on Prepayment Lines <span class="badge w" style="margin-left:6px"><%# NewInvoiceCount %></span></div>
    <div class="stab" onclick="switchInner('t2',1)">Existing Prepayment Balance Invoices <span class="badge b" style="margin-left:6px"><%# ExistingInvoiceCount %></span></div>
  </div>

  <div class="inner-pane active" id="t2-0">
    <div class="table-head-row"><h3>New Invoices — vendor line item level</h3><span class="info-chip">📅 May – Jun 2026</span></div>
    <table class="grid-actions">
      <thead><tr><th>Invoice No.</th><th>PO / Line</th><th>Vendor</th><th>Prepay GL</th><th>Offset GL</th><th>CAPEX/OPEX</th><th>Invoice date</th><th>Amount (AUD)</th><th>Foreign</th><th>Flag</th><th class="setup-col">Setup status</th><th>Action</th></tr></thead>
      <tbody>
        <asp:Repeater ID="rptNewInvoices" runat="server">
          <ItemTemplate>
            <tr style='<%# Eval("RowStyle") %>'>
              <td class="po-num"><%# Eval("InvoiceNo") %></td><td><%# Eval("PoLine") %></td><td class="vendor"><%# Eval("Vendor") %></td><td><%# Eval("GlAccount") %></td><td><%# Eval("CashGlAccount") %></td><td><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td><td><%# Eval("InvoiceDate") %></td><td><%# Eval("Amount") %></td><td style="font-size:12px;color:var(--muted)"><%# Eval("ForeignAmount") %></td><td><span class='badge <%# Eval("Flag.Cls") %>'><%# Eval("Flag.Text") %></span></td><td class="setup-col"><span class='badge <%# Eval("SetupStatus.Cls") %>'><%# Eval("SetupStatus.Text") %></span></td><td><button type="button" class='<%# (bool)Eval("ActionPrimary") ? "btn sm primary" : "btn sm" %>' onclick="amortSelectInvoice('<%# Eval("InvoiceId") %>')"><%# Eval("ActionText") %></button></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
  </div>

  <div class="inner-pane" id="t2-1">
    <div class="table-head-row"><h3>Existing Prepayment Balance Invoices</h3><span class="badge b"><%# ExistingInvoiceCount %> invoices</span></div>
    <table class="grid-actions">
      <thead><tr><th>Invoice No.</th><th>PO / Line</th><th>Vendor</th><th>GL Account</th><th>CAPEX/OPEX</th><th>Invoice date</th><th>Amount</th><th>Recognised amount</th><th>Amortisation status</th><th>Action</th></tr></thead>
      <tbody>
        <asp:Repeater ID="rptExistingBalanceInvoices" runat="server">
          <ItemTemplate>
            <tr><td class="po-num"><%# Eval("InvoiceNo") %></td><td><%# Eval("PoLine") %></td><td class="vendor"><%# Eval("Vendor") %></td><td><%# Eval("GlAccount") %></td><td><span class='badge <%# (string)Eval("CapexOpex") == "CAPEX" ? "b" : "s" %>'><%# Eval("CapexOpex") %></span></td><td><%# Eval("InvoiceDate") %></td><td><%# Eval("Amount") %></td><td><%# Eval("RecognisedAmount") %></td><td><span class='badge <%# Eval("AmortisationStatus.Cls") %>'><%# Eval("AmortisationStatus.Text") %></span></td><td><button type="button" class="btn sm" onclick="amortOpenExisting('<%# Eval("InvoiceId") %>', '<%# Eval("PoNumber") %>', '<%# Eval("ActionTarget") %>')"><%# Eval("ActionText") %></button></td></tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
  </div>

  <asp:PlaceHolder ID="phSetup" runat="server" Visible="false">
  <div style="margin-top:18px">
    <div style="font-size:13px;font-weight:700;color:var(--faint);text-transform:uppercase;letter-spacing:.04em;margin-bottom:14px">Amortisation Setup — <%# SelectedInvoice.InvoiceNo %> · <%# SelectedInvoice.CapexOpex %><%# SelectedInvoice.ForeignCurrency != null ? " · " + SelectedInvoice.ForeignCurrency + " " + (SelectedInvoice.AmountDoc.HasValue ? SelectedInvoice.AmountDoc.Value.ToString("N0") : "") : "" %> (selected)</div>
    <div class="two-col">
      <div class="card">
        <div class="card-head"><h3>Setup inputs</h3><span class="badge w">Input required</span></div>
        <div class="card-body">
          <input type="hidden" id="setup-invoice-id" value="<%# SelectedInvoice.InvoiceId %>" />
          <div class="form-section">
            <h4>Classification</h4>
            <div class="form-grid">
              <div class="fld"><label>Current / Non-current</label>
                <div class="radio-row" style="margin-top:6px">
                  <label class="radio-opt"><input type="radio" name="cc" id="cc-current" <%# IsCurrent ? "checked" : "" %> /> Current (≤ 12 months)</label>
                  <label class="radio-opt"><input type="radio" name="cc" <%# IsCurrent ? "" : "checked" %> /> Non-current</label>
                </div>
              </div>
              <div class="fld"><label>Capital / Non-capital</label>
                <div class="radio-row" style="margin-top:6px">
                  <label class="radio-opt"><input type="radio" name="cap" id="cap-capital" <%# IsCapital ? "checked" : "" %> /> Capital asset</label>
                  <label class="radio-opt"><input type="radio" name="cap" <%# IsCapital ? "" : "checked" %> /> Non-capital</label>
                </div>
              </div>
            </div>
          </div>
          <div class="form-section">
            <h4>Amortisation type</h4>
            <div class="radio-row" style="margin-bottom:14px">
              <label class="radio-opt"><input type="radio" name="atype" id="atype-oneoff" <%# IsOneOff ? "checked" : "" %> /> One-off recognition</label>
              <label class="radio-opt"><input type="radio" name="atype" <%# IsOneOff ? "" : "checked" %> /> Amortisation schedule</label>
            </div>
            <div class="form-grid">
              <div class="fld"><label>Start date</label><input type="date" id="setup-start" value="<%# StartDateValue %>" /></div>
              <div class="fld"><label>End date</label><input type="date" id="setup-end" value="<%# EndDateValue %>" /></div>
              <div class="fld"><label>Basis</label><select><option selected>Equal monthly (straight-line)</option><option>Days-based</option><option>Manual</option></select></div>
              <div class="fld"><label>Periods</label><input type="number" id="setup-periods" value="<%# PeriodsValue %>" /></div>
            </div>
          </div>
          <div class="form-section">
            <h4>Account assignment</h4>
            <div class="form-grid">
              <div class="fld"><label>Prepayment asset account</label>
                <select id="setup-prepay-gl">
                  <asp:Repeater ID="rptGlOptions" runat="server">
                    <ItemTemplate>
                      <option value='<%# Eval("PrepaymentGlId") %>' <%# IsSelectedGl(Eval("PrepaymentGlId")) ? "selected" : "" %>><%# Eval("GlAccount") %> — <%# Eval("GlDescription") %></option>
                    </ItemTemplate>
                  </asp:Repeater>
                </select>
              </div>
              <div class="fld"><label>Offset / expense account</label><input type="text" id="setup-expense-gl" value="<%# ExpenseGlValue %>" /></div>
              <div class="fld"><label>Cost centre / WBS</label><input type="text" id="setup-wbs" value="<%# SelectedInvoice.WbsCostCentre %>" /></div>
              <div class="fld"><label>Profit centre</label><input type="text" id="setup-profit" value="<%# SelectedInvoice.ProfitCentre %>" readonly /></div>
              <div class="fld"><label>Company code</label><input type="text" id="setup-company" value="<%# SelectedInvoice.CompanyCode %>" /></div>
            </div>
          </div>
          <div style="display:flex;gap:8px;margin-top:4px;flex-wrap:wrap">
            <button type="button" class="btn" onclick="amortSaveDraft()">Save draft</button>
            <button type="button" class="btn success" onclick="amortGenerate()">Generate schedule &amp; preview journals</button>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="card-head"><h3>Generated amortisation schedule</h3><span class="badge s">System calculated &middot; Editable</span></div>
        <div class="card-body" style="padding:14px">
          <div style="margin-bottom:12px;font-size:13px;color:var(--muted)"><%# ScheduleSummaryLine %></div>
          <table class="sched-table">
            <thead><tr><th>#</th><th>Period</th><th>Status</th><th style="text-align:right">Amount ($)</th></tr></thead>
            <tbody>
              <asp:Repeater ID="rptSchedule" runat="server">
                <ItemTemplate>
                  <tr data-pid='<%# Eval("PeriodId") %>'><td><%# Eval("Num") %></td><td><%# Eval("Period") %></td><td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td><td><input type="text" class="pa-input" value='<%# Eval("Amount") %>' style="width:90px;text-align:right" /></td></tr>
                </ItemTemplate>
              </asp:Repeater>
              <tr class="total-row"><td colspan="3" style="text-align:right;font-size:13px">Total</td><td style="text-align:right;font-variant-numeric:tabular-nums"><%# ScheduleTotal %></td></tr>
            </tbody>
          </table>
          <div style="margin-top:10px;display:flex;gap:8px;align-items:center;flex-wrap:wrap">
            <button type="button" class="btn" onclick="amortSavePeriods(<%# InvoiceIdValue %>)">Save period edits</button>
            <span style="font-size:12px;color:var(--muted)">Edit amounts above, then save — a mismatch vs. invoice total raises an Admin exception.</span>
          </div>
          <div style="margin-top:10px;padding:10px 12px;background:var(--warn-bg);border-radius:8px;font-size:12px;color:var(--warn);font-weight:600">⚠ Schedule total must equal invoice amount. Duplicate recognition and over-allocation are blocked.</div>
        </div>
      </div>
    </div>
  </div>
  </asp:PlaceHolder>
</div>
