<%@ Control Language="C#" AutoEventWireup="true" CodeFile="GroupWorkflowControl.ascx.cs" Inherits="Prepayment.Web.Controls.PPMGroupWorkflowControl" %>
<div class="page">
  <div class="page-title">Group Workflow Control</div>
  <div class="page-sub">Admin view across all <asp:Literal ID="litGroupCount" runat="server" /> delivery groups &middot; Monitor workflow stage &middot; Intervene or reassign at any stage</div>

  <div class="kpi-row">
    <asp:Repeater ID="rptKpis" runat="server">
      <ItemTemplate>
        <div class="kpi"><div class="lbl"><%# Eval("Label") %></div><div class='val <%# Eval("ValueClass") %>' style='<%# Eval("ValueStyle") %>'><%# Eval("Value") %></div><div class="sub"><%# Eval("Sub") %></div></div>
      </ItemTemplate>
    </asp:Repeater>
  </div>

  <div class="search-card" style="margin-bottom:18px">
    <div style="display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap">
      <div class="fld" style="flex:1;min-width:180px"><label>Group name</label>
        <select id="gwGroupName">
          <option value="">All</option>
          <asp:Repeater ID="rptGroupNameOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("Key") %>'<%# IsGroupName(Eval("Key")) %>><%# Eval("Label") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <div class="fld" style="flex:1;min-width:180px"><label>Preparer</label>
        <select id="gwPreparer">
          <option value="">All</option>
          <asp:Repeater ID="rptPreparerOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("Key") %>'<%# IsPreparer(Eval("Key")) %>><%# Eval("Label") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <div class="fld" style="flex:1;min-width:180px"><label>Status</label>
        <select id="gwStatus">
          <option value="">All</option>
          <asp:Repeater ID="rptStatusOptions" runat="server">
            <ItemTemplate><option value='<%# Eval("Key") %>'<%# IsStatus(Eval("Key")) %>><%# Eval("Label") %></option></ItemTemplate>
          </asp:Repeater>
        </select>
      </div>
      <button type="button" class="btn primary" onclick="gwApplyFilter()">Apply</button>
      <button type="button" class="btn" onclick="gwClearFilter()">Clear</button>
    </div>
  </div>

  <div class="table-wrap">
    <div class="table-head-row">
      <h3>All <asp:Literal ID="litGroupCountHead" runat="server" /> delivery groups — workflow status</h3>
      <div style="display:flex;gap:8px">
        <button type="button" class="btn sm" onclick="gwExportCsv()">Export CSV</button>
        <button type="button" class="btn sm primary" onclick="gwBulkReassign()">Bulk reassign</button>
      </div>
    </div>
    <table>
      <thead>
        <tr>
          <th style="width:28px;text-align:center"><input type="checkbox" id="gw-select-all" onclick="gwToggleAll(this)" title="Select all" /></th>
          <th>Group</th>
          <th>Group name</th>
          <th>Preparer</th>
          <th>Approver</th>
          <th style="text-align:center">POs</th>
          <th style="text-align:center">Invoices</th>
          <th style="text-align:center">Journals</th>
          <th>Current stage</th>
          <th>Status</th>
          <th>Action</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="rptGroupWorkflow" runat="server">
          <ItemTemplate>
            <tr style='<%# Eval("RowStyle") %>' data-group='<%# Eval("Group") %>'>
              <td style="text-align:center"><input type="checkbox" class="gw-row-chk" /></td>
              <td class="po-num"><%# Eval("Group") %></td><td class="vendor"><%# Eval("GroupName") %></td><td><%# Eval("Preparer") %></td><td><%# Eval("Approver") %></td>
              <td style="text-align:center;font-variant-numeric:tabular-nums"><%# Eval("PoCount") %></td>
              <td style="text-align:center;font-variant-numeric:tabular-nums"><%# Eval("InvoiceCount") %></td>
              <td style="text-align:center;font-variant-numeric:tabular-nums"><%# Eval("JournalCount") %></td>
              <td><span class='badge <%# Eval("Stage.Cls") %>'><%# Eval("Stage.Text") %></span></td>
              <td><span class='badge <%# Eval("Status.Cls") %>'><%# Eval("Status.Text") %></span></td>
              <td style="white-space:nowrap">
                <button type="button" class="btn sm" onclick='gwViewDetail("<%# Eval("Group") %>","<%# Eval("ActionTarget") %>")'>View detail</button>
                <button type="button" class="btn sm" onclick='gwReassign("<%# Eval("Group") %>")' title="Reassign preparer / approver">Reassign</button>
                <button type="button" class="btn sm" onclick='gwSendReminder("<%# Eval("Group") %>")' title="Send a workflow reminder to the group preparer">Send reminder</button>
                <button type="button" class="btn sm" onclick='gwEscalate("<%# Eval("Group") %>")' title="Escalate — raise an Admin exception for this group">Escalate</button>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="phNoRows" runat="server" Visible="false">
          <tr><td colspan="11" style="text-align:center;color:var(--faint);font-size:12px;padding:16px">No delivery groups match the current filter.</td></tr>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="phFooter" runat="server">
          <tr>
            <td colspan="11" style="text-align:center;color:var(--faint);font-size:12px;padding:12px"><asp:Literal ID="litRemaining" runat="server" /> more groups &middot; Showing <asp:Literal ID="litShown" runat="server" /> of <asp:Literal ID="litTotal" runat="server" /></td>
          </tr>
        </asp:PlaceHolder>
      </tbody>
    </table>
  </div>

  <div style="margin-top:4px">
    <div class="section-head"><h3>Workflow stage key</h3></div>
    <div style="display:flex;gap:10px;flex-wrap:wrap">
      <span class="info-chip" style="background:var(--suc-bg);color:var(--success)">On track</span>
      <span class="info-chip" style="background:var(--warn-bg);color:var(--warn)">Needs attention</span>
      <span class="info-chip" style="background:var(--err-bg);color:var(--error)">Blocked</span>
      <span class="info-chip" style="background:var(--accent-bg);color:var(--blue)">Fully exported</span>
    </div>
    <div style="font-size:12px;color:var(--faint);margin-top:8px">
      Stages: PO Flagged → Invoice Matched → Setup Complete → Journal Generated → Pending Approval → Approved → Exported
    </div>
  </div>
</div>
