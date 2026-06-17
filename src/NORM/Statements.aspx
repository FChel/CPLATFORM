<%@ Page Language="C#" AutoEventWireup="true" CodeFile="Statements.aspx.cs" Inherits="Statements" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>NORM — Statements</title>
<%-- Page CSS is scoped under .norm so it cannot leak into the shared RenderHeader() chrome. --%>
<link rel="stylesheet" type="text/css" href="../css/norm.css" />
</head>
<body>
<form id="form1" runat="server">
<%-- RenderHeader() emits the standard CPlatform chrome here in the live app. --%>

<div class="norm" id="normApp">
  <div class="top">
    <div class="brand"><b>NORM</b><span>Notes, Output, Reporting &amp; Mapping</span></div>
    <div class="ctx">
      <span class="pill" id="ctxEntity">Departmental</span>
      <span class="pill num" id="ctxFy">FY 2023&#8211;24</span>
      <span class="pill ver num" id="ctxVer">map FY2024 v0.1</span>
      <span class="pill env" id="ctxEnv">UAT</span>
    </div>
  </div>

  <div class="shell">
    <aside class="rail">
      <h4>Statement set</h4>
      <ul class="nav" id="nav"></ul>
      <h4>Reporting period</h4>
      <ul class="nav">
        <li aria-current="true"><span class="ix"></span>30 June 2024</li>
        <li class="disabled"><span class="ix"></span>30 June 2025 (cross-system)</li>
      </ul>
    </aside>

    <main class="canvas">
      <div class="doc" id="doc"></div>
      <div class="foot">The above statement should be read in conjunction with the accompanying notes. Every figure traces to its source trial-balance accounts.</div>
    </main>

    <aside class="side">
      <h4>Validations</h4>
      <ul class="vlist" id="vlist"></ul>
      <div class="meter">
        <div class="lab"><span>Trial balance mapped</span><b id="covPct"></b></div>
        <div class="bar"><i id="covBar"></i></div>
      </div>
      <button type="button" class="pool" id="poolBtn"></button>
      <div class="demo">
        <div class="t">Demonstration</div>
        <button type="button" class="btn" id="breakBtn">Inject a test break</button>
      </div>
    </aside>
  </div>

  <div class="scrim" id="scrim"></div>
  <aside class="drawer" id="drawer" aria-hidden="true" aria-label="Figure derivation"></aside>
</div>

<script type="text/javascript">
  // Server emits the statement payload built from the tblNORM_ tables.
  window.NORM_DATA = <%= NormDataJson %>;
</script>
<script type="text/javascript" src="../js/norm.js"></script>
</form>
</body>
</html>
