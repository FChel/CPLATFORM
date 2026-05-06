<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Help.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Help" %>
<%@ Import Namespace="CPlatform.LPPI" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review &mdash; Help</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        /* ---------- Help page layout ----------
           Two-column on desktop: a compact contents nav on the left, the
           help body on the right. Single column on narrow viewports. */
        .help-grid {
            display: grid;
            grid-template-columns: 220px 1fr;
            gap: 24px;
            margin-top: 16px;
        }
        @media (max-width: 900px) {
            .help-grid { grid-template-columns: 1fr; }
            .help-toc { position: static; max-height: none; }
        }

        .help-toc {
            position: sticky;
            top: 88px;
            align-self: start;
            background: var(--white);
            border: 1px solid var(--line);
            border-radius: var(--r-lg);
            padding: 14px 16px;
            box-shadow: var(--shadow-sm);
            font-size: 13px;
            max-height: calc(100vh - 100px);
            overflow-y: auto;
        }
        .help-toc .toc-title {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            color: var(--ink-3);
            font-weight: 700;
            margin-bottom: 8px;
        }
        .help-toc ol {
            list-style: none;
            margin: 0;
            padding: 0;
            counter-reset: toc;
        }
        .help-toc ol > li {
            counter-increment: toc;
            margin-bottom: 4px;
        }
        .help-toc ol > li > a {
            display: block;
            padding: 5px 8px;
            border-radius: var(--r-sm);
            color: var(--ink-2);
            text-decoration: none;
        }
        .help-toc ol > li > a::before {
            content: counter(toc) ". ";
            color: var(--orange-deep);
            font-weight: 700;
            margin-right: 4px;
        }
        .help-toc ol > li > a:hover {
            background: var(--orange-soft);
            color: var(--orange-deep);
            text-decoration: none;
        }

        .help-body {
            background: var(--white);
            border: 1px solid var(--line);
            border-radius: var(--r-lg);
            padding: 28px 32px;
            box-shadow: var(--shadow-sm);
            max-width: 900px;
        }
        .help-body section {
            margin-bottom: 28px;
            scroll-margin-top: 88px;
        }
        .help-body section:last-child { margin-bottom: 0; }

        .help-body h2 {
            font-size: 18px;
            color: var(--ink);
            margin: 0 0 12px;
            padding-bottom: 6px;
            border-bottom: 2px solid var(--orange-tint);
        }
        .help-body h3 {
            font-size: 14px;
            color: var(--ink);
            margin: 18px 0 6px;
        }
        .help-body p { margin: 0 0 12px; color: var(--ink-2); line-height: 1.65; }
        .help-body ul, .help-body ol { margin: 0 0 12px; padding-left: 22px; color: var(--ink-2); line-height: 1.65; }
        .help-body li { margin-bottom: 5px; }
        .help-body code {
            background: var(--bg);
            border: 1px solid var(--line);
            border-radius: var(--r-sm);
            padding: 1px 6px;
            font-family: var(--font-mono);
            font-size: 12px;
            color: var(--orange-deep);
        }

        /* Lifecycle pills inline — match the dashboard's status-pill colours
           so admins recognise them immediately. */
        .lifecycle-pills {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            align-items: center;
            margin: 4px 0 12px;
            font-size: 12px;
        }
        .lifecycle-pills .pill {
            padding: 2px 10px;
        }
        .lifecycle-pills .arrow {
            color: var(--ink-3);
            font-weight: 700;
        }

        /* Two-column term/definition grid for the page reference. */
        .help-body dl.help-pages {
            display: grid;
            grid-template-columns: 200px 1fr;
            gap: 8px 18px;
            margin: 0;
        }
        .help-body dl.help-pages dt {
            font-weight: 600;
            color: var(--ink);
            margin: 0;
        }
        .help-body dl.help-pages dd {
            margin: 0;
            color: var(--ink-2);
        }
        @media (max-width: 600px) {
            .help-body dl.help-pages { grid-template-columns: 1fr; gap: 4px 0; }
            .help-body dl.help-pages dd { margin-bottom: 8px; }
        }

        .callout {
            background: var(--orange-soft);
            border-left: 4px solid var(--orange);
            padding: 12px 16px;
            border-radius: 0 var(--r) var(--r) 0;
            margin: 12px 0;
            font-size: 13px;
            color: var(--ink-2);
        }
        .callout strong { color: var(--orange-deep); }

        .callout.warn {
            background: var(--warn-bg);
            border-left-color: var(--warn);
        }
        .callout.warn strong { color: var(--warn); }
    </style>
</head>
<body>
<form id="form1" runat="server">
<div class="lppi-shell">
    <%= RenderHeader("help") %>

    <main class="lppi-main">
        <div class="page-head">
            <div>
                <div class="crumb">LPPI Review</div>
                <h1>Help</h1>
                <p class="lead">Module overview, package lifecycle, and a page-by-page guide for administrators.</p>
            </div>
        </div>

        <div class="help-grid">

            <%-- Sticky table of contents --%>
            <nav class="help-toc" aria-label="Help contents">
                <div class="toc-title">On this page</div>
                <ol>
                    <li><a href="#overview">Overview</a></li>
                    <li><a href="#lifecycle">Package lifecycle</a></li>
                    <li><a href="#pages">Page-by-page guide</a></li>
                    <li><a href="#flags">Configuration flags</a></li>
                    <li><a href="#operations">Common operations</a></li>
                    <li><a href="#troubleshooting">Troubleshooting</a></li>
                    <li><a href="#support">Support</a></li>
                </ol>
            </nav>

            <%-- Body --%>
            <article class="help-body">

                <section id="overview">
                    <h2>Overview</h2>
                    <p>
                        LPPI Review is the Defence Finance Group module for processing Late Payment Penalty Interest (LPPI)
                        cases under
                        <a href="https://www.finance.gov.au/publications/resource-management-guides/supplier-pay-time-or-pay-interest-policy-rmg-417"
                           target="_blank" rel="noopener">RMG-417 &mdash; Supplier Pay On-Time or Pay Interest Policy</a>.
                    </p>
                    <p>
                        The end-to-end loop is: <strong>Load file</strong> &rarr; <strong>Send-outs</strong> (issue review packages
                        to Capability Managers) &rarr; <strong>Reviewer page</strong> (CMs classify each document with a Reason
                        Code) &rarr; <strong>Export</strong> (push payable cases back to BODS for SAP processing).
                    </p>
                    <p>
                        Each loaded file is parsed line-by-line, broken into <strong>review packages</strong> by Capability Manager
                        program (e.g. ARMY, NAVY), and emailed to the recipients configured for that group. Reviewers click the
                        link in the email and complete their review on a token-authenticated page &mdash; no application login
                        required for them.
                    </p>
                </section>

                <section id="lifecycle">
                    <h2>Package lifecycle</h2>
                    <p>Every package moves through a fixed sequence of statuses, driven by the application code (not by SQL defaults):</p>
                    <div class="lifecycle-pills">
                        <span class="pill notsent">NotSent</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill sent">Sent</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill inreview">InReview</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill complete">Complete</span>
                    </div>
                    <p>Plus the side branch <span class="pill cancelled">Cancelled</span> for packages that were withdrawn.</p>
                    <ul>
                        <li><strong>NotSent</strong> &mdash; created at file load, contents reconciled into the CM&rsquo;s existing NotSent package or a fresh one. Editable in QA but not yet emailed.</li>
                        <li><strong>Sent</strong> &mdash; the first send (or Mark-as-sent in test mode) flips the status and stamps SentDate. Document set is now frozen for that package.</li>
                        <li><strong>InReview</strong> &mdash; first reviewer save against a Sent package flips the status. Reminders can be sent at any point during Sent or InReview.</li>
                        <li><strong>Complete</strong> &mdash; set automatically when every document in the package has a Reason Code. ClosedDate is stamped. Reviewer page becomes read-only.</li>
                        <li><strong>Cancelled</strong> &mdash; admin action to withdraw a package. Documents are eligible for repackaging on the next file load.</li>
                    </ul>
                    <div class="callout">
                        <strong>Note:</strong> editing a NotSent package on the reviewer page (admin QA) does NOT change its status &mdash;
                        only Send (or Mark-as-sent in test mode) on the Send-outs page does that.
                    </div>
                </section>

                <section id="pages">
                    <h2>Page-by-page guide</h2>
                    <dl class="help-pages">
                        <dt>Dashboard</dt>
                        <dd>Module overview &mdash; total LPPI exposure (with payable / not-payable / awaiting breakdown), document and package counts, and the list of open packages.</dd>

                        <dt>Help</dt>
                        <dd>This page.</dd>

                        <dt>Load file</dt>
                        <dd>Upload a <code>LATEPMT_INTEREST_REVIEW_*.xls</code> extract from BODS (tab-delimited despite the .xls extension). The file is parsed and previewed before commit.</dd>

                        <dt>Batches</dt>
                        <dd>History of every loaded file with line-level drill-through. Read-only.</dd>

                        <dt>Send-outs</dt>
                        <dd>Issue NotSent packages, send reminders for Sent / InReview ones, preview the email body before sending. The "Mark as sent (test)" button is available only when ProductionMode is false.</dd>

                        <dt>Capability Managers</dt>
                        <dd>Manage CM groups (ARMY, NAVY, etc.) &mdash; created automatically at file load &mdash; and their email recipient lists (To and Cc).</dd>

                        <dt>Reason Codes</dt>
                        <dd>Maintain the active Reason Code list (RC01-RC16 by default plus any custom codes). Outcome (Payable / NotPayable) and Requires-Comments flag drive reviewer-page validation.</dd>

                        <dt>Export</dt>
                        <dd>Build the ERP Payment Request bulk-upload spreadsheet for reviewed Payable documents within a date range. Mark-as-exported on commit so cases are not double-billed.</dd>

                        <dt>Admin users</dt>
                        <dd>Manage the LPPI admin access list. Anyone not on this list is redirected to the public access-denied landing page when they attempt to reach an admin page.</dd>
                    </dl>
                </section>

                <section id="flags">
                    <h2>Configuration flags</h2>
                    <p>The most important <code>web.config</code> appSettings:</p>
                    <ul>
                        <li><code>CPlatform.Environment</code> &mdash; environment label (DEV / UAT / PROD), shown on the header chip.</li>
                        <li><code>LPPI.ProductionMode</code> &mdash; <strong>true</strong> enables real email sending and hides the Mark-as-sent button; <strong>false</strong> blocks Send and shows Mark-as-sent. Mutually exclusive by construction.</li>
                        <li><code>LPPI.BaseUrl</code> &mdash; the public hostname used when building reviewer links in outgoing emails.</li>
                        <li><code>LPPI.DefaultDueDays</code> &mdash; default review window applied to new packages.</li>
                        <li><code>LPPI.ReminderWindowDays</code> &mdash; "due soon" threshold used by reminders and dashboard pills.</li>
                        <li><code>LPPI.SmtpHost</code> &nbsp;/&nbsp; <code>LPPI.SmtpPort</code> &nbsp;/&nbsp; <code>LPPI.SmtpEnableSsl</code> &nbsp;/&nbsp; <code>LPPI.SmtpUser</code> &nbsp;/&nbsp; <code>LPPI.SmtpPassword</code> &mdash; SMTP relay configuration (only consulted when ProductionMode is true).</li>
                        <li><code>LPPI.MailFrom</code> &nbsp;/&nbsp; <code>LPPI.MailFromName</code> &mdash; From address on outgoing reviewer emails.</li>
                        <li><code>LPPI.SupportMailboxTo</code> &nbsp;/&nbsp; <code>LPPI.SupportMailboxCc</code> &mdash; the support / feedback mailbox surfaced in the reviewer email and the page-header "Feedback &amp; support" button.</li>
                        <li><code>LPPI.SapBaseUrl</code> &mdash; SAP S/4HANA Fiori host for the document-number and PO deep links.</li>
                    </ul>
                    <div class="callout warn">
                        <strong>PROD checklist:</strong> set <code>CPlatform.Environment</code> to <code>PROD</code>, set <code>LPPI.ProductionMode</code> to <code>true</code>,
                        set the real <code>LPPI.SmtpHost</code> and confirm <code>LPPI.BaseUrl</code> matches the public hostname &mdash;
                        otherwise reviewer email links will be wrong.
                    </div>
                </section>

                <section id="operations">
                    <h2>Common operations</h2>

                    <h3>Loading a new file</h3>
                    <ol>
                        <li>Receive the file from BODS (named <code>LATEPMT_INTEREST_REVIEW_*.xls</code>).</li>
                        <li>Open <strong>Load file</strong>, choose the file, click <em>Upload &amp; preview</em>.</li>
                        <li>Confirm the header validation passes and the row count looks reasonable.</li>
                        <li>Click <em>Commit</em>. A new batch is recorded; documents are reconciled into existing NotSent packages or a fresh package per CM.</li>
                        <li>Visit <strong>Send-outs</strong> &mdash; new packages will be in <em>NotSent</em> and ready to issue.</li>
                    </ol>

                    <h3>Issuing review packages</h3>
                    <ol>
                        <li>Open <strong>Send-outs</strong>. The page shows every NotSent / Sent / InReview package.</li>
                        <li>Use <em>Preview email</em> on any row to see the rendered email before committing to a send.</li>
                        <li>Confirm the recipients are configured on <strong>Capability Managers</strong> &mdash; the page warns when a program has no recipient (it cannot be sent until configured).</li>
                        <li>Set the due date if you want to override the default, then click <em>Send / remind selected</em> on the rows you want to issue.</li>
                        <li>In UAT (ProductionMode = false), use <em>Mark as sent (test)</em> instead &mdash; this drives the lifecycle to Sent without dispatching email.</li>
                    </ol>

                    <h3>Sending reminders</h3>
                    <p>Reminders are valid only on <strong>Sent</strong> or <strong>InReview</strong> packages &mdash; never on NotSent (use Send) or Complete / Cancelled. The dashboard surfaces packages that are near deadline or overdue.</p>

                    <h3>Exporting payable cases to ERP</h3>
                    <ol>
                        <li>Open <strong>Export</strong>.</li>
                        <li>Choose the date range (defaults to last month). Optionally restrict to a single batch.</li>
                        <li>Click <em>Preview count</em> to see how many distinct payable documents and lines will be exported.</li>
                        <li>Click <em>Generate payment file</em>. The spreadsheet is built using the ERP bulk-upload template (27 columns, Sheet1, plain headers).</li>
                        <li>By default <em>Mark documents as exported</em> is on &mdash; this stamps ExportedDate so the same case is not double-billed on a later run.</li>
                    </ol>
                </section>

                <section id="troubleshooting">
                    <h2>Troubleshooting</h2>
                    <ul>
                        <li><strong>"Send is disabled"</strong> &mdash; <code>LPPI.ProductionMode</code> is false. In UAT this is normal and Mark-as-sent is the alternative; in PROD this needs to be flipped to <code>true</code>.</li>
                        <li><strong>"No active recipients configured"</strong> on a send &mdash; the CM group has no rows in <code>tblLPPI_CapabilityManagerEmails</code>. Add at least one To recipient on the Capability Managers page.</li>
                        <li><strong>Code changes not taking effect</strong> &mdash; a stale <code>bin\App_Code.dll</code> can override updated <code>App_Code/*.cs</code> files. Touch <code>web.config</code> (e.g. add a trailing space) to recycle the application pool, or restart the application pool directly.</li>
                        <li><strong>Reviewer page shows a Windows-auth prompt</strong> &mdash; IIS Windows Authentication must be enabled in IIS Manager (not just <code>web.config</code>). The reviewer page uses anonymous + token; admin pages use Windows identity.</li>
                        <li><strong>Email font is Times New Roman in Outlook</strong> &mdash; should be fixed as of April 2026. If it recurs, every text-bearing element in <code>LPPIEmail.cs</code> <code>BuildBody</code> must declare <code>font-family</code> inline; the Word renderer in Outlook does not inherit fonts from a parent.</li>
                        <li><strong>Reviewer link returns "Invalid link"</strong> &mdash; the package may have been Cancelled, the token may have been regenerated, or the email link may be older than the most recent reload-and-reissue. Check the package status on Send-outs.</li>
                        <li><strong>OLE DB parameter binding error</strong> &mdash; SQL must use positional <code>?</code> placeholders, not named <code>@param</code> markers. The <code>LPPIHelper.P()</code> helper does the translation; make sure new code goes through it.</li>
                    </ul>
                </section>

                <section id="support">
                    <h2>Support</h2>
                    <p>For functional questions, use the <strong>Feedback &amp; support</strong> button in the page header &mdash; it opens a pre-populated mailto with the support inbox addresses configured in <code>web.config</code>.</p>
                    <p>For module documentation and source, see the repository <code>README.md</code> at the repo root and the SQL scripts under <code>db/</code>.</p>
                </section>

            </article>
        </div>

    </main>

    <footer class="lppi-footer">
        <span>LPPI Review &middot; <%= CurrentEnv %></span>
    </footer>
</div>
</form>
</body>
</html>
