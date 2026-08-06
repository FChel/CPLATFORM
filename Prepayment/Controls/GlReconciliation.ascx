<%@ Control Language="C#" AutoEventWireup="true" CodeFile="GlReconciliation.ascx.cs" Inherits="Prepayment.Web.Controls.PPMGlReconciliation" %>
<div class="page">
  <div class="page-title">GL Balance Reconciliation</div>
  <div class="page-sub">Load SAP GL balance extract &middot; Reconcile against FINHUB prepayment records by group &middot; Identify variances</div>

  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <!-- Upload card -->
  <div class="search-card">
    <h3>📂 Load GL Balance File</h3>
    <div style="display:grid;grid-template-columns:1fr 1fr auto;gap:12px;align-items:flex-end">
      <div class="fld">
        <label>GL balance file (CSV)</label>
        <input type="file" id="recon-file" accept=".csv" style="padding:7px 10px;font-size:13px" />
      </div>
      <div class="fld">
        <label>Reporting period</label>
        <input type="text" id="recon-period" value="<%= Server.HtmlEncode(Period) %>" placeholder="YYYY/MM e.g. 2026/06" />
      </div>
      <div style="display:flex;gap:8px">
        <button type="button" class="btn primary" onclick="reconUpload()">Upload &amp; reconcile</button>
        <button type="button" class="btn" onclick="reconTemplate()">Download template</button>
      </div>
    </div>
    <asp:PlaceHolder ID="phBanner" runat="server" Visible="false">
      <div style="margin-top:12px;padding:10px 14px;background:var(--suc-bg);border-radius:8px;font-size:12px;color:var(--success);font-weight:600">
        <asp:Literal ID="litBanner" runat="server" />
      </div>
    </asp:PlaceHolder>
    <asp:PlaceHolder ID="phNoExtract" runat="server" Visible="false">
      <div style="margin-top:12px;padding:10px 14px;background:var(--surface2);border-radius:8px;font-size:12px;color:var(--muted)">
        No GL extract loaded yet. Upload a CSV (Group code, GL account, closing balance …) to reconcile against FINHUB.
      </div>
    </asp:PlaceHolder>
  </div>

  <!-- Reconciliation table -->
  <div class="section-head">
    <h3>Reconciliation by group — <asp:Literal ID="litPeriodLabel" runat="server" /></h3>
    <div style="display:flex;gap:8px;align-items:flex-end">
      <div class="fld" style="min-width:150px;margin:0"><label style="font-size:11px">Period</label>
        <select id="recon-period-filter" onchange="reconApply()">
          <asp:Repeater ID="rptPeriodOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("PeriodKey") %>'<%# IsPeriod(Eval("PeriodKey")) %>><%# Eval("PeriodLabel") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <button type="button" class="btn sm" id="recon-variances-btn" onclick="reconToggleVariances()"><%= VariancesOnly ? "Show all" : "Show variances only" %></button>
      <button type="button" class="btn sm primary" onclick="reconExport()">Export reconciliation</button>
    </div>
  </div>
  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Group</th>
          <th>Group name</th>
          <th>GL account</th>
          <th style="text-align:right">SAP GL balance</th>
          <th style="text-align:right">FINHUB balance</th>
          <th style="text-align:right">Variance</th>
          <th>Status</th>
          <th>Action</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="rptReconciliation" runat="server">
          <ItemTemplate>
            <tr style='<%# Eval("RowStyle") %>'>
              <td class="po-num"><%# Eval("Group") %></td><td class="vendor"><%# Eval("GroupName") %></td><td title='<%# Eval("GlDescription") %>'><%# Eval("GlAccount") %></td>
              <td style="text-align:right;font-variant-numeric:tabular-nums"><%# Eval("SapBalance") %></td>
              <td style="text-align:right;font-variant-numeric:tabular-nums"><%# Eval("FinhubBalance") %></td>
              <td style='<%# Eval("VarianceStyle") %>'><%# Eval("Variance") %></td>
              <td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td>
              <td><button type="button" class="btn sm" style='<%# Eval("ActionStyle") %>' onclick='reconInvestigate(<%# Eval("ReconciliationId") %>)'><%# Eval("ActionText") %></button></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="phNoRows" runat="server" Visible="false">
          <tr><td colspan="8" style="text-align:center;color:var(--faint);font-size:12px;padding:16px">No reconciliation rows for this period.</td></tr>
        </asp:PlaceHolder>
        <tr style="background:#f7f8fa;font-weight:700">
          <td colspan="3">Total — <%= RowCount %> rows</td>
          <td style="text-align:right;font-variant-numeric:tabular-nums"><%= TotalSap %></td>
          <td style="text-align:right;font-variant-numeric:tabular-nums"><%= TotalFinhub %></td>
          <td style='text-align:right;font-variant-numeric:tabular-nums;<%= TotalVarStyle %>'><%= TotalVar %></td>
          <td><span class='badge <%= TotalBadgeCls %>'><%= Server.HtmlEncode(TotalBadgeText) %></span></td><td></td>
        </tr>
      </tbody>
    </table>
  </div>
  <div style="font-size:12px;color:var(--faint);margin-top:-14px;margin-bottom:18px">* Variance offset by an approved journal not yet exported — balance will move once posted to SAP</div>

  <!-- Variance investigation -->
  <asp:PlaceHolder ID="phDetail" runat="server" Visible="false">
  <div id="recon-detail" class="section-head"><h3>Variance investigation — <asp:Literal ID="litDetailTitle" runat="server" /></h3></div>
  <div class="two-col">
    <div class="card">
      <div class="card-head"><h3>GL extract detail</h3><span class="badge b">From SAP file</span></div>
      <div class="card-body">
        <div class="po-source">
          <div class="title">SAP GL account <asp:Literal ID="litGlTitle" runat="server" /></div>
          <div class="po-source-grid">
            <asp:Repeater ID="rptGlExtractDetail" runat="server">
              <ItemTemplate>
                <div class="po-field"><div class="lbl"><%# Eval("Label") %></div><div class="val"><%# Eval("Value") %></div></div>
              </ItemTemplate>
            </asp:Repeater>
          </div>
        </div>
      </div>
    </div>
    <div class="card">
      <div class="card-head"><h3>FINHUB record detail</h3><span class='badge <%= FinhubBadgeCls %>'><%= Server.HtmlEncode(FinhubBadgeText) %></span></div>
      <div class="card-body">
        <div class="po-source" style='<%= DetailIsVariance ? "background:#fdecea;border-color:#f0b0b0" : "" %>'>
          <div class="title" style='<%= DetailIsVariance ? "color:var(--error)" : "" %>'>FINHUB prepayment balance — <asp:Literal ID="litFinhubTitle" runat="server" /></div>
          <div class="po-source-grid">
            <asp:Repeater ID="rptFinhubDetail" runat="server">
              <ItemTemplate>
                <div class="po-field"><div class="lbl"><%# Eval("Label") %></div><div class="val" style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div></div>
              </ItemTemplate>
            </asp:Repeater>
          </div>
        </div>
        <div style="margin-top:10px"><label style="font-size:12px;font-weight:700;color:var(--muted);display:block;margin-bottom:5px">Investigation notes</label>
          <textarea id="recon-note" style="width:100%;height:60px;padding:9px 11px;border:1px solid var(--border);border-radius:8px;font-size:13px;font-family:inherit;background:var(--surface2);resize:vertical" placeholder="Document reason for variance…"><asp:Literal ID="litNote" runat="server" /></textarea>
        </div>
        <div class="fld" style="margin-top:10px"><label style="font-size:12px;font-weight:700;color:var(--muted)">Assign to</label>
          <select id="recon-assignee">
            <option value="">— unassigned —</option>
            <asp:Repeater ID="rptAssignees" runat="server">
              <ItemTemplate><option value='<%# Eval("Id") %>'<%# IsAssignee(Eval("Id")) %>><%# Eval("DisplayName") %></option></ItemTemplate>
            </asp:Repeater>
          </select>
        </div>
        <div style="display:flex;gap:8px;margin-top:8px">
          <button type="button" class="btn sm success" onclick='reconResolve("MarkExplained")'>Mark resolved</button>
          <button type="button" class="btn sm" onclick='reconResolve("RaiseAdjustment")'>Raise adjustment</button>
        </div>
      </div>
    </div>
  </div>
  </asp:PlaceHolder>
</div>
