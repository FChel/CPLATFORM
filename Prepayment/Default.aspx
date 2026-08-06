<%@ Page Language="C#" AutoEventWireup="true" CodeFile="Default.aspx.cs" Inherits="Prepayment.Web.PPMDefault" ContentType="text/html" ResponseEncoding="utf-8" %>
<%@ Register TagPrefix="uc" TagName="PoIdentification"   Src="~/Prepayment/Controls/PoIdentification.ascx" %>
<%@ Register TagPrefix="uc" TagName="AmortisationSetup"  Src="~/Prepayment/Controls/AmortisationSetup.ascx" %>
<%@ Register TagPrefix="uc" TagName="JournalGeneration"  Src="~/Prepayment/Controls/JournalGeneration.ascx" %>
<%@ Register TagPrefix="uc" TagName="AdminControlTower"  Src="~/Prepayment/Controls/AdminControlTower.ascx" %>
<%@ Register TagPrefix="uc" TagName="GroupWorkflow"      Src="~/Prepayment/Controls/GroupWorkflowControl.ascx" %>
<%@ Register TagPrefix="uc" TagName="GlReconciliation"   Src="~/Prepayment/Controls/GlReconciliation.ascx" %>
<%@ Register TagPrefix="uc" TagName="PrepaymentReport"   Src="~/Prepayment/Controls/PrepaymentReport.ascx" %>
<%@ Register TagPrefix="uc" TagName="ImportData"        Src="~/Prepayment/Controls/ImportData.ascx" %>

<!DOCTYPE html>
<html lang="en">
<head runat="server">
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Prepayment Management Dashboard</title>
<link href="../css/ppm.css?v=5" rel="stylesheet" />
</head>
<body>
<form id="form1" runat="server">

<div class="stripe"></div>
<div class="header">
  <div class="brand">
    <h1>Prepayment Management</h1>
    <p>PO identification &middot; Invoice recognition &middot; Amortisation setup &middot; Journal generation</p>
    <div style="margin-top:6px"><span class="info-chip">👤 Signed in as <%= Server.HtmlEncode(PPMCurrentUserName) %></span></div>
  </div>
  <div class="top-tabs">
    <div class="ttab active" onclick="switchTab(0)">PO Identification</div>
    <div class="ttab" onclick="switchTab(1)">Prepayment &amp; Amortisation</div>
    <div class="ttab" onclick="switchTab(2)">Journals</div>
    <div class="ttab" onclick="switchTab(3)"<%= PPMIsAdmin ? "" : " style=\"display:none\"" %>>Admin</div>
    <div class="ttab" onclick="switchTab(4)">Group Workflow</div>
    <div class="ttab" onclick="switchTab(5)">GL Reconciliation</div>
    <div class="ttab" onclick="switchTab(6)">Prepayment Report</div>
    <div class="ttab" onclick="switchTab(7)"<%= PPMIsAdmin ? "" : " style=\"display:none\"" %>>Import Data</div>
  </div>
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 1 — PO IDENTIFICATION
═══════════════════════════════════════════════════════ -->
<div class="pane active" id="pane-0" data-render="poidentification">
  <%-- Tab 1 is now a self-contained user control with its own DB-backed data load. --%>
  <uc:PoIdentification ID="ucPoIdentification" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 2 — PREPAYMENT & AMORTISATION
═══════════════════════════════════════════════════════ -->
<div class="pane" id="pane-1" data-render="amortisation">
  <%-- Tab 2 is a self-contained user control with its own DB-backed data load. --%>
  <uc:AmortisationSetup ID="ucAmortisationSetup" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 3 — JOURNALS
═══════════════════════════════════════════════════════ -->
<div class="pane" id="pane-2" data-render="journal">
  <%-- Tab 3 is a self-contained user control with its own DB-backed data load. --%>
  <uc:JournalGeneration ID="ucJournalGeneration" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 4 — ADMIN
═══════════════════════════════════════════════════════ -->
<div class="pane" id="pane-3" data-render="admin">
  <%-- Tab 4 is a self-contained user control with its own DB-backed data load. --%>
  <uc:AdminControlTower ID="ucAdminControlTower" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 5 — GROUP WORKFLOW CONTROL
═══════════════════════════════════════════════════════ -->
<div class="pane" id="pane-4" data-render="groupworkflow">
  <%-- Tab 5 is a self-contained user control with its own DB-backed data load. --%>
  <uc:GroupWorkflow ID="ucGroupWorkflow" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 6 — GL RECONCILIATION
═══════════════════════════════════════════════════════ -->
<div class="pane" id="pane-5" data-render="glreconciliation">
  <%-- Tab 6 is a self-contained user control with its own DB-backed data load. --%>
  <uc:GlReconciliation ID="ucGlReconciliation" runat="server" />
</div>
<!-- ═══════════════════════════════════════════════════════
     TAB 7 — PREPAYMENT REPORT BY GROUP
═══════════════════════════════════════════════════════ -->
<div class="pane" id="pane-6" data-render="report">
  <%-- Tab 7 is a self-contained, read-only DB-backed user control (§3.7). --%>
  <uc:PrepaymentReport ID="ucPrepaymentReport" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     TAB 8 — IMPORT DATA (upload the real Excel workbook; full-replace load)
═══════════════════════════════════════════════════════ -->
<%-- Import tab is static (no DB load to refresh) — no data-render, so switchTab just shows it. --%>
<div class="pane" id="pane-7">
  <uc:ImportData ID="ucImportData" runat="server" />
</div>

<!-- ═══════════════════════════════════════════════════════
     Group Workflow (Tab 5) reassign modal — lives in the page chrome (not inside the
     pane) so it covers the viewport and survives AJAX pane refreshes. Populated and
     shown client-side by dashboard.js.
═══════════════════════════════════════════════════════ -->
<div id="gw-reassign-modal" class="gw-modal-overlay" style="display:none">
  <div class="gw-modal">
    <div class="gw-modal-head"><h3 id="gw-modal-title">Reassign group</h3><button type="button" class="gw-modal-x" onclick="gwCloseReassign()">&times;</button></div>
    <div class="gw-modal-body">
      <div id="gw-modal-groups" style="font-size:12px;color:var(--muted);margin-bottom:12px"></div>
      <div class="fld" style="margin-bottom:10px"><label>New preparer</label>
        <select id="gw-modal-preparer"><option value="">— leave unchanged —</option></select>
      </div>
      <div class="fld"><label>New approver</label>
        <select id="gw-modal-approver"><option value="">— leave unchanged —</option></select>
      </div>
    </div>
    <div class="gw-modal-foot">
      <button type="button" class="btn" onclick="gwCloseReassign()">Cancel</button>
      <button type="button" class="btn primary" onclick="gwConfirmReassign()">Save reassignment</button>
    </div>
  </div>
</div>

<script src="../js/ppm.js?v=19"></script>
</form>
</body>
</html>
