<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Help.aspx.cs" Inherits="CPlatform.PORD.PORD_Help" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>POD — Help</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css?v=20261021d" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("help") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">POD</div>
                <h1>Help</h1>
                <p class="lead">How to run the monthly PO review. Reviewers get their own Instructions tab on the review page.</p>
            </div>
        </div>

        <div class="pord-instr pord-help-main">
            <nav class="pord-toc"><div class="t">Contents</div>
                <ol>
                    <li><a href="#h-what">What POD does</a></li>
                    <li><a href="#h-checks">Checks</a></li>
                    <li><a href="#h-cycle">The monthly cycle</a></li>
                    <li><a href="#h-rules">Payment terms rules</a></li>
                    <li><a href="#h-resp">Responses &amp; reasons</a></li>
                    <li><a href="#h-life">Package lifecycle</a></li>
                    <li><a href="#h-pages">Pages</a></li>
                    <li><a href="#h-prepare">Before each cycle</a></li>
                </ol>
            </nav>
            <div class="pord-doc">
                <section id="h-what">
                    <h2>What POD does</h2>
                    <p>POD is the purchase order dashboard for the <strong>Financial Operations Compliance Program</strong>. It works like LPPI Review:
                    BODS extracts exceptions each month, the business responds on a single page, and each round is verified against the next month's data.</p>
                    <p>Each compliance rule is a <strong>check</strong>. <strong>Non-standard payment terms</strong> is the first live check, and more will follow.
                    All checks share one monthly cycle and <strong>one package per Delivery Manager program</strong>. AS Fin and PO contacts get one email and
                    one review page, with a tab for each check that has exceptions for them.</p>
                </section>

                <section id="h-checks">
                    <h2>Checks</h2>
                    <table class="ref">
                        <tr><th>Check</th><th>Status</th><th>What it flags</th><th>Extract</th></tr>
                        <%= ChecksTableHtml %>
                    </table>
                    <p>The <em>Currency vs bank</em> check is an <strong>example</strong> with sample data. It shows how a second check appears on the dashboard tabs, in the packages and on the review page.
                    Planned checks come from the Decision Brief's compliance-monitoring list and are not built yet.</p>
                </section>

                <section id="h-cycle">
                    <h2>The monthly cycle</h2>
                    <ol>
                        <li><strong>Load.</strong> An admin uploads the BODS CSV on <em>Load file</em>. Rows are classified; compliant rows and POs with an active exclusion are dropped. POs seen before get their Review Nbr increased by one.</li>
                        <li><strong>Send.</strong> <em>Send-outs</em> issues one package per Delivery Manager program. AS Fin gets the full package; each PO contact gets a link to their own POs.</li>
                        <li><strong>Respond.</strong> PO contacts and AS Fin record a response for each PO on the review page.</li>
                        <li><strong>Finalise.</strong> AS Fin finalises. POs with no response are recorded as <em>No response</em>. Accepted valid reasons create exclusions.</li>
                        <li><strong>Close &amp; verify.</strong> <em>Outcomes</em> issues the report to the FSO Compliance Team and the supplier-master list to DFIM.
                            Next month's load verifies the commitments: amended POs drop out and are marked <em>Resolved</em>, and unfixed POs return with a higher Review Nbr.</li>
                    </ol>
                </section>

                <section id="h-rules">
                    <h2>Payment terms rules</h2>
                    <p>Defence standard payment terms are <strong>20 days</strong>, or <strong>5 days for suppliers invoicing through PEPPOL</strong> e-invoicing.
                    Business Partner master records carry these standard terms, so an exception is normally a PO whose terms were changed from the BP master.</p>
                    <p>A PO is flagged when <strong>any</strong> of these is true:</p>
                    <table class="ref">
                        <tr><th>Rule</th><th>Test</th><th>Category</th></tr>
                        <tr><td>Non-standard</td><td>AUD, PO net days not 5 or 20, and PO equals the BP master. This should not occur, and means the BP master itself is wrong (goes to DFIM).</td><td><%= PORDHelper.CategoryPill(PordCategory.NonStandard) %></td></tr>
                        <tr><td>Override</td><td>PO payment-term key ≠ BP master payment-term key, e.g. 5 days on a supplier that is not PEPPOL-enabled</td><td><%= PORDHelper.CategoryPill(PordCategory.Override) %></td></tr>
                        <tr><td>Both</td><td>PO net days not 5 or 20, and so different from the BP master</td><td><%= PORDHelper.CategoryPill(PordCategory.NonStandardAndOverride) %></td></tr>
                        <tr><td>Foreign currency</td><td>Non-AUD and PO net days &lt; 14</td><td><%= PORDHelper.CategoryPill(PordCategory.ForeignCurrency) %></td></tr>
                    </table>
                    <p>Excluded at extract: GL accounts <code>211455</code> (capital construction support charges) and <code>212208</code> (estate projects).</p>
                    <p>SAP payment terms are <em>keys</em>, mapped to net days for the check. Unmapped keys must be confirmed with ERP before the file can be loaded.</p>
                </section>

                <section id="h-resp">
                    <h2>Responses &amp; reasons</h2>
                    <table class="ref">
                        <tr><th>Response</th><th>Needs</th><th>Effect</th></tr>
                        <tr><td>Will amend PO terms</td><td>Amend-by date (today or later)</td><td>Checked at next load: <em>Resolved</em> if the PO drops out, otherwise Review Nbr +1.</td></tr>
                        <tr><td>Valid reason</td><td>Reason code + Objective ref (+ comments for VR01, VR02, VR99)</td><td>VR01–VR03 exclude the PO for <%= PORDHelper.ExclusionMonths %> months (tied to its current term key). VR99 does not.</td></tr>
                        <tr><td>Not mine – reassign</td><td>Comments naming the owner</td><td>Admin corrects the POC mapping; the PO returns to the right person next cycle.</td></tr>
                        <tr><td>No response</td><td>—</td><td>Applied at finalise. The PO returns next cycle and is escalated from Review Nbr <%= PORDHelper.EscalateAtReview %>.</td></tr>
                    </table>
                </section>

                <section id="h-life">
                    <h2>Package lifecycle</h2>
                    <p><span class="pill notsent">Not sent</span> → <span class="pill sent">Sent</span> → <span class="pill inreview">In review</span> → <span class="pill finalised">Finalised</span> → <span class="pill exported">Closed</span></p>
                    <p>Same as LPPI: the first save moves a sent package to In review. AS Fin can reopen a finalised package, which clears the No response markers. Closed is terminal.</p>
                </section>

                <section id="h-pages">
                    <h2>Pages</h2>
                    <dl>
                        <dt>Dashboard</dt><dd>Cycle progress, exceptions by category, response mix, packages, repeat exceptions and recent loads.</dd>
                        <dt>Load file</dt><dd>Upload, header check against the expected columns, classification preview, then commit.</dd>
                        <dt>Send-outs</dt><dd>Issue and remind packages, preview the AS Fin and PO contact emails, and open any reviewer view for QA.</dd>
                        <dt>AS Fin groups</dt><dd>The mailbox and display name for each Delivery Manager program.</dd>
                        <dt>Reason codes</dt><dd>Valid reasons and what each one requires.</dd>
                        <dt>Exclusions</dt><dd>POs skipped at load because a valid reason was accepted. Can be revoked.</dd>
                        <dt>Outcomes</dt><dd>Outcomes report, DFIM supplier-master list and verification from the latest load.</dd>
                    </dl>
                </section>

                <section id="h-prepare">
                    <h2>Before each cycle</h2>
                    <ol>
                        <li>Confirm the review period and open-PO scope with BODS. The payment terms extract is separate from LPPI.</li>
                        <li>Check the file headers, currency and payment-term mappings. Refer missing columns to BODS and unmapped terms to ERP before loading.</li>
                        <li>Check each program's AS Fin mailbox and PO contacts, then agree a respond-by date.</li>
                        <li>Preview the AS Fin and PO contact emails before issuing packages.</li>
                        <li>After finalisation, provide the outcomes CSV to the FSO Compliance Team and the supplier-master list to DFIM.</li>
                    </ol>
                    <p>Refer policy questions, including contracts over $1 million, to the FSO Compliance Team. Use the agreed monthly run date so this review does not clash with LPPI.</p>
                </section>
                <asp:Panel ID="pnlSampleData" runat="server">
                    <section class="pord-sample-settings">
                        <h2>Sample data</h2>
                        <p>Restore the starting review cycle when preparing another review session. This clears sample responses and mailbox changes for everyone using this environment.</p>
                        <asp:Button ID="btnRestore" runat="server" CssClass="btn btn-secondary" Text="Restore sample data" OnClick="btnRestore_Click" />
                        <asp:CheckBox ID="chkRestore" runat="server" Text=" I understand this clears the shared sample responses" />
                        <asp:Literal ID="litRestore" runat="server" />
                    </section>
                </asp:Panel>
            </div>
        </div>
    </main>
    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
