<%@ Control Language="C#" AutoEventWireup="true" CodeFile="ImportData.ascx.cs" Inherits="Prepayment.Web.Controls.PPMImportData" %>
<div class="page">
  <div class="page-title">Import Data</div>
  <div class="page-sub">Upload the Prepayment Dashboard workbook (.xlsx) &middot; Loads PO commitment, invoice and GL listing sheets into the database</div>

  <div class="search-card">
    <h3>📥 Import Excel Workbook</h3>
    <p style="font-size:13px;color:var(--muted);margin:8px 0 14px">
      Select <strong>Prepayment Dashboard_2026.xlsx</strong> (or a workbook with the same sheets:
      <em>PO Commitment (Aligned)</em>, <em>Invoice (Aligned)</em>, <em>GL_Listing</em>). The data will be
      parsed and loaded into FINHUB across all tabs.
    </p>
    <div style="display:grid;grid-template-columns:1fr auto;gap:12px;align-items:flex-end">
      <div class="fld">
        <label>Excel workbook (.xlsx)</label>
        <input type="file" id="import-file" accept=".xlsx" style="padding:7px 10px;font-size:13px" />
      </div>
      <div style="display:flex;gap:8px">
        <button type="button" class="btn primary" onclick="importRun()">Import &amp; load</button>
      </div>
    </div>

    <div style="margin-top:14px;padding:11px 14px;background:var(--warn-bg);border-radius:8px;font-size:12px;color:var(--warn);font-weight:600">
      ⚠ Full replace: importing clears the existing prepayment data and reloads it from this file.
      This affects every tab. Make sure you are loading the correct workbook.
    </div>

    <div id="import-busy" style="display:none;margin-top:14px;font-size:13px;color:var(--blue);font-weight:600">
      ⏳ Parsing workbook and loading… this can take a moment for the GL listing sheet.
    </div>
    <div id="import-banner" style="display:none;margin-top:14px;padding:11px 14px;border-radius:8px;font-size:13px;font-weight:600"></div>
  </div>

  <!-- Result summary (populated by JS after a successful import) -->
  <div id="import-result" class="table-wrap" style="display:none">
    <div class="table-head-row"><h3>Last import — loaded record counts</h3><span class="badge s" id="import-file-label"></span></div>
    <table class="grid-actions">
      <thead><tr><th>Entity</th><th class="num">Records loaded</th></tr></thead>
      <tbody id="import-counts"></tbody>
    </table>
  </div>
</div>
