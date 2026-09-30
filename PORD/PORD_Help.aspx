<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="PORD_Help.aspx.cs" Inherits="CPlatform.PORD.PORD_Help" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>PO Review — Help</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <link rel="stylesheet" href="../css/pord.css" />
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("help") %>
    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">PO Review</div>
                <h1>Help</h1>
                <p class="lead">How the PO Review module works, for administrators. Reviewers get their own Instructions tab on the review page.</p>
            </div>
        </div>
        <%= RenderDemoNotice() %>

        <div class="pord-instr pord-help-main">
            <nav class="pord-toc"><div class="t">Contents</div>
                <ol>
                    <li><a href="#h-what">What PO Review does</a></li>
                    <li><a href="#h-cycle">The monthly cycle</a></li>
                    <li><a href="#h-rules">Business rules</a></li>
                    <li><a href="#h-resp">Responses &amp; reasons</a></li>
                    <li><a href="#h-life">Package lifecycle</a></li>
                    <li><a href="#h-pages">Pages</a></li>
                    <li><a href="#h-settings">Settings</a></li>
                    <li><a href="#h-decisions">Assumptions to confirm</a></li>
                    <li><a href="#h-next">Future checks</a></li>
                </ol>
            </nav>
            <div class="pord-doc">
                <section id="h-what">
                    <h2>What PO Review does</h2>
                    <p>PO Review is the first tranche of the <strong>Financial Operations Compliance Program</strong>. It uses the same approach as LPPI Review:
                    BODS extracts the exceptions each month, the business responds on a single page, and each round is checked against the next month's data.
                    Tranche 1 covers <strong>Non-Standard Payment Terms (NSPT)</strong> on open purchase orders.</p>
                    <p>Other PO checks can be added as new check types without a new module, because each flagged PO records which check raised it.</p>
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
                    <h2>Business rules</h2>
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
                    <p>SAP payment terms are <em>keys</em>. Net days come from the <code>PORD.TermKeyDays</code> setting (demo: <code>Z005 → 5</code>, <code>Z020 → 20</code>, …).
                    The real key list, and the treatment of keys with cash-discount tiers, must be confirmed with the ERP team.</p>
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

                <section id="h-settings">
                    <h2>Settings</h2>
                    <p>All settings are optional <code>web.config</code> appSettings with safe defaults. The server owns <code>web.config</code>, so nothing needs to change there for the demo.</p>
                    <table class="ref">
                        <tr><th>Key</th><th>Default</th><th>Purpose</th></tr>
                        <tr><td><code>PORD.DemoMode</code></td><td>true</td><td>In-memory demo data. Set to false once the PORD schema is installed.</td></tr>
                        <tr><td><code>PORD.DefaultDueDays</code></td><td>14</td><td>Default respond-by window on Send-outs.</td></tr>
                        <tr><td><code>PORD.ReminderWindowDays</code></td><td>3</td><td>Days before the due date that "Due soon" shows.</td></tr>
                        <tr><td><code>PORD.EscalateAtReview</code></td><td>2</td><td>Review Nbr at which a PO counts as a repeat exception.</td></tr>
                        <tr><td><code>PORD.ExclusionMonths</code></td><td>12</td><td>How long an accepted valid reason excludes a PO.</td></tr>
                        <tr><td><code>PORD.TermKeyDays</code></td><td>Z000:0;Z005:5;…</td><td>Payment-term key to net-days map.</td></tr>
                        <tr><td><code>PORD.BaseUrl</code></td><td>LPPI.BaseUrl</td><td>Host used in email links.</td></tr>
                    </table>
                    <p>Admin access currently uses the LPPI admin list. SAP deep links use <code>LPPI.SapBaseUrl</code>.</p>
                </section>

                <section id="h-decisions">
                    <h2>Assumptions to confirm</h2>
                    <p>The demo is built on these working assumptions from the spec review. Each one needs a business decision before build.</p>
                    <table class="ref">
                        <tr><th>#</th><th>Question</th><th>Working assumption</th></tr>
                        <tr><td>1</td><td>Foreign-currency POs: exclude, or apply the 14-day rule?</td><td>Include, and apply the 14-day rule (KL comment). Needs a Currency column.</td></tr>
                        <tr><td>2</td><td>Grouping and routing columns</td><td><strong>BODS owns this.</strong> The NSPT extract is a new file. We will review the first file against the expected columns and tell BODS what is missing.</td></tr>
                        <tr><td>3</td><td>Which BP payment term?</td><td><strong>Confirmed:</strong> BP master terms are the Defence standard, 20 days or 5 days for PEPPOL suppliers.</td></tr>
                        <tr><td>4</td><td>Who is the PO contact?</td><td>PO Creator, resolved to an email by BODS.</td></tr>
                        <tr><td>5</td><td>Definition of "open PO"</td><td>Still to deliver &gt; 0, or delivered but not fully invoiced.</td></tr>
                        <tr><td>6</td><td>No response at finalise</td><td>Recorded as No response; carried forward; escalated at Review Nbr ≥ <%= PORDHelper.EscalateAtReview %>.</td></tr>
                        <tr><td>7</td><td>Exclusion lifetime</td><td><%= PORDHelper.ExclusionMonths %> months, tied to PO + term key, revocable.</td></tr>
                        <tr><td>8</td><td>Closing the loop</td><td>Outcomes CSV to FSO Compliance, BP list to DFIM, verification by the next load.</td></tr>
                        <tr><td>9</td><td>Contracts over $1m (outside RMG-417 scope)</td><td>Not treated as a valid reason until policy confirms.</td></tr>
                        <tr><td>10</td><td>Run date</td><td>A fixed business day that does not clash with the LPPI load.</td></tr>
                    </table>
                </section>

                <section id="h-next">
                    <h2>Future checks</h2>
                    <p>From the Decision Brief's compliance-monitoring list. Each is a new check type on the same pipeline:</p>
                    <ul>
                        <li>Currency vs bank mismatch on vendor, outline agreement or PO</li>
                        <li>Direct payment threshold</li>
                        <li>Purpose of payment codes</li>
                        <li>Non-procurement payment type</li>
                    </ul>
                </section>
            </div>
        </div>
    </main>
    <%= RenderFooter() %>
</div>
</form>
</body>
</html>
