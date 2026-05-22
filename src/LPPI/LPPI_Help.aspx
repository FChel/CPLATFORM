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
           so admins recognise them immediately. The classes themselves
           (.pill.notsent etc.) live in lppi.css; we just inherit them. */
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
                    <li><a href="#roles">Who does what</a></li>
                    <li><a href="#emails">How send-outs work</a></li>
                    <li><a href="#summary-page">In-flight Summary view</a></li>
                    <li><a href="#pages">Page-by-page guide</a></li>
                    <li><a href="#operations">Common operations</a></li>
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
                        An ERP extract of late payments is loaded into FinHub, which bundles each Capability Manager program's late payments into a review package, emails the AS Fin team and the invoice POCs for that program, captures their decisions (Payable / Not Payable per document with a Reason Code), and ships the payable cases back to ERP as a Payment Request bulk-upload spreadsheet.
                    </p>
                    <div class="callout">
                        <strong>Replaces a shared spreadsheet.</strong> Before this module existed the same workflow ran in a
                        shared Excel file. Moving it to a web app gives every save an audit trail, prevents two people overwriting
                        each other's edits, and gives admins a single dashboard to see what is outstanding across all programs.
                    </div>
                </section>

                <section id="lifecycle">
                    <h2>Package lifecycle</h2>
                    <p>A review package moves through a small number of statuses, in a fixed order:</p>
                    <div class="lifecycle-pills" aria-label="Package lifecycle">
                        <span class="pill notsent">Not sent</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill sent">Sent</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill inreview">In review</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill finalised">Finalised</span>
                        <span class="arrow">&rarr;</span>
                        <span class="pill exported">Exported</span>
                        <span class="arrow" aria-hidden="true">&nbsp;|&nbsp;</span>
                        <span class="pill cancelled">Cancelled</span>
                    </div>
                    <ul>
                        <li><strong>Not sent</strong> &mdash; created at file-load time, no email yet. Editable on the reviewer page (admin QA).</li>
                        <li><strong>Sent</strong> &mdash; the initial emails have been sent. Document set is now frozen.</li>
                        <li><strong>In review</strong> &mdash; at least one document has a reason code. Reminders are still allowed.</li>
                        <li><strong>Finalised</strong> &mdash; the AS Fin team has clicked Finalise. Form fields are locked. Any documents without a reason code have been auto-marked as <code>RC-NR</code> (Payable, no response received). Reversible: AS Fin can click Unfinalise to reopen.</li>
                        <li><strong>Exported</strong> &mdash; admin has included the package in an ERP payment file. Terminal &mdash; no further changes.</li>
                        <li><strong>Cancelled</strong> &mdash; admin-cancelled side branch. Documents become eligible for repackaging on the next file load.</li>
                    </ul>
                    <div class="callout">
                        <strong>Two checkpoints, two responsibilities.</strong>
                        <em>Finalise</em> is self-service for AS Fin &mdash; it closes off the review.
                        <em>Export</em> is admin-only &mdash; it ships the file to ERP and locks the package permanently.
                    </div>
                </section>

                <section id="roles">
                    <h2>Who does what</h2>
                    <p>The module recognises three populations:</p>
                    <ul>
                        <li>
                            <strong>AS Fin / Capability Manager team</strong> &mdash; receives the group summary email at the team mailbox configured for the program (e.g. AS Fin ARMY for the ARMY package). The team is accountable for the review and clicks <em>Finalise</em> when they are happy with all decisions. The team self-organises internally: a small team has one person doing everything; a larger team can have one person review and another finalise. The system does not model the internal split &mdash; it just records who clicked Save / Finalise / Unfinalise via Windows identity.
                        </li>
                        <li>
                            <strong>Invoice POCs</strong> &mdash; the named contact on each invoice in the LPPI file. Each POC receives their own scoped email with a personal review link that shows only the documents assigned to them. POCs cannot finalise the package &mdash; that is AS Fin's responsibility &mdash; but they can save reason code decisions for their documents. There is no recipient configuration step for POCs; their email addresses come straight from the LPPI file.
                        </li>
                        <li>
                            <strong>Administrator (DFG)</strong> &mdash; loads files, configures the AS Fin email for each Capability Manager program, issues review packages, sends reminders, and ships finalised packages to ERP via Export.
                            Admin access is gated by the <code>tblLPPI_AdminUsers</code> table (see Admin users page).
                        </li>
                    </ul>
                </section>

                <section id="emails">
                    <h2>How send-outs work</h2>
                    <p>
                        Each Send / Reminder click on the Send-outs page dispatches two kinds of email for the selected package:
                    </p>
                    <ol>
                        <li>
                            <strong>AS Fin group summary</strong> &mdash; one email to the CM team mailbox configured on the Capability Managers page. Sender is the LPPI mailbox. The link in this email opens the full package and includes the Finalise button.
                        </li>
                        <li>
                            <strong>Per-POC email</strong> &mdash; one email to each distinct invoice POC in the package. Sender is the AS Fin team mailbox (so POC replies land in the AS Fin inbox). Each link is unique to the POC and opens a filtered view of only their documents.
                        </li>
                    </ol>
                    <p>
                        The single Capability Manager email configured on each program is used for <strong>both</strong> the AS Fin <em>To</em> address (on the group summary) and the AS Fin <em>From</em> address (on the per-POC mails). One inbox owns the conversation in both directions.
                    </p>
                    <p>
                        On a reminder, POCs whose documents are all reviewed are skipped automatically &mdash; no chasing complete work. AS Fin always receives the reminder regardless of progress, since they can chase up the gaps directly via the reviewer page. Per-POC issues (invalid email addresses in the LPPI file, individual SMTP failures) are surfaced as a warning on the result line and never block the AS Fin send.
                    </p>
                    <div class="callout">
                        <strong>Empty-POC packages.</strong> If the LPPI file has no usable POC for any document in a package, the AS Fin email still sends; the result line reports "No POCs configured" and AS Fin can review and finalise on their own.
                    </div>
                </section>

                <section id="summary-page">
                    <h2>In-flight Summary view</h2>
                    <p>
                        The <strong>Summary</strong> page is a read-only operational view of the current review cycle &mdash;
                        complementary to the Dashboard's at-a-glance exec view. Use it to see, in flight:
                    </p>
                    <ul>
                        <li>The reason-code split across in-scope documents (with an <strong>Awaiting</strong> pseudo-row for unreviewed docs).</li>
                        <li>A Not Payable sub-cut, useful for spotting clusters &mdash; e.g. lots of RC-RL signalling upstream data issues.</li>
                        <li>Progress by Capability Manager program (packages, documents, reviewed / total bar, distinct POCs and interest exposure, with a totals row).</li>
                        <li>The top 10 outstanding POCs by document count, for chase-up triage.</li>
                    </ul>
                    <p>
                        Two independent pickers sit at the top of the page:
                    </p>
                    <ul>
                        <li><strong>Scope</strong> &mdash; picks the universe of packages. Defaults to <em>Current cycle</em> (every in-flight package); past load batches are listed below it for focusing on a specific monthly file.</li>
                        <li><strong>Capability Manager</strong> &mdash; defaults to <em>(All programs)</em>; pick a specific program to narrow the whole page (counts, reason-code split, non-payment, POCs, export) to just that program. Useful for spotting POC gaps by group, or reason-code clusters within one program.</li>
                    </ul>
                    <p>
                        The <strong>Export full data</strong> button generates a 53-column xlsx covering every line of every
                        in-scope document &mdash; same layout as the reviewer page's export, but cycle-wide rather than per package.
                        The export honours both pickers, so an ARMY-filtered Current-cycle export contains only ARMY's in-flight lines.
                    </p>
                    <div class="callout">
                        <strong>Reporting note.</strong> For trend analysis, CFO-level visibility, and cycle-over-cycle reporting,
                        the Summary page is intentionally lightweight &mdash; deeper analytics live in Power BI.
                    </div>
                </section>

                <section id="pages">
                    <h2>Page-by-page guide</h2>
                    <dl class="help-pages">
                        <dt>Dashboard</dt>
                        <dd>Read-only overview. LPPI exposure (dollar headlines), counts, open packages (NotSent/Sent/InReview/Finalised), recent loads.</dd>

                        <dt>Summary</dt>
                        <dd>Operational in-flight view of the current cycle. Reason-code split (with Awaiting), non-payment cluster view, by-program and by-CM-number cuts, top-10 outstanding POCs, plus a full 53-column xlsx export of every in-scope line.</dd>

                        <dt>Load file</dt>
                        <dd>Upload an LPPI file (<code>LATEPMT_INTEREST_REVIEW_*.xls</code>), preview, then commit. The reconcile step creates new packages and adds documents to existing NotSent packages.</dd>

                        <dt>Send-outs</dt>
                        <dd>Lists in-flight packages. Issue first sends, send reminders, preview the AS Fin and POC emails. Each send dispatches one group summary plus one email per POC. The disabled-checkbox on a row indicates either a CM with no AS Fin email configured or a status that is not actionable. Finalised packages are visible (read-only) so you can see what is queued for export, and offer a <em>Notify AS Fin</em> button to send a courtesy summary email to a typed recipient. In test mode the real Send button is replaced with Mark-as-sent / remind.</dd>

                        <dt>Reviewer page</dt>
                        <dd>Token-authenticated. AS Fin reviews each document from the group summary link, picks a reason code, optionally adds comments and an objective reference, then clicks Finalise to close the package. Unfinalise is available on the same toolbar to reopen. POCs use a separate per-POC link from their own email; they see only their documents and cannot finalise.</dd>

                        <dt>Export</dt>
                        <dd>Pick one or more Finalised packages, generate the ERP Payment Request bulk-upload file. Selected packages flip to Exported and are locked. Past export batches are listed below the picker with Download buttons that re-stream the stored file.</dd>

                        <dt>Batches</dt>
                        <dd>Every file load is recorded. Drill into a batch to see the lines it brought in, including which export batch (if any) shipped each line.</dd>

                        <dt>Capability Managers</dt>
                        <dd>Configure the AS Fin email and display name for each CM program. A package cannot be sent until both fields are populated. There is no recipient list to maintain &mdash; one mailbox per program is the whole model. The page banner counts programs that are missing this configuration.</dd>

                        <dt>Reason Codes</dt>
                        <dd>Maintain the reason code list. Each code carries an Outcome (Payable / Not Payable) and an optional <em>Requires comments</em> flag.</dd>

                        <dt>Admin users</dt>
                        <dd>Manage the LPPI admin allow-list. Users not in this list cannot reach the admin pages. The reviewer page is unaffected (it uses tokens, not Windows identity).</dd>
                    </dl>
                </section>

                <section id="operations">
                    <h2>Common operations</h2>

                    <h3>Loading a new file</h3>
                    <ol>
                        <li>Receive the LPPI file (named <code>LATEPMT_INTEREST_REVIEW_*.xls</code>).</li>
                        <li>Open <strong>Load file</strong>, choose the file, click <em>Upload &amp; preview</em>.</li>
                        <li>Confirm the header validation passes and the row count looks reasonable.</li>
                        <li>Click <em>Commit</em>. A new batch is recorded; documents are reconciled into existing NotSent packages or a fresh package per CM program. Per-POC links are created at the same time.</li>
                        <li>Visit <strong>Send-outs</strong> &mdash; new packages will be in NotSent and ready to issue.</li>
                    </ol>

                    <h3>Configuring a Capability Manager email</h3>
                    <ol>
                        <li>Open <strong>Capability Managers</strong>. The banner at the top counts programs that have no AS Fin email configured yet.</li>
                        <li>Click <em>Manage</em> on a program to open the configuration panel.</li>
                        <li>Enter the AS Fin email address and a display name (both are required &mdash; the display name is what POCs see as the From-name on their email). Click <em>Save</em>.</li>
                        <li>The page validates that the email is an <code>@defence.gov.au</code> or <code>@annpsr.gov.au</code> address (subdomains allowed).</li>
                    </ol>

                    <h3>Issuing review packages</h3>
                    <ol>
                        <li>Open <strong>Send-outs</strong>. The page lists every package that is NotSent / Sent / InReview / Finalised.</li>
                        <li>Use <em>Preview AS Fin</em> or <em>Preview POC</em> on any actionable row to see the rendered email before committing to a send. The POC preview uses placeholder values to illustrate the template; real per-POC sends use real data.</li>
                        <li>The <em>POCs</em> column shows the fan-out scope for each package. The picker checkbox is disabled when the CM has no AS Fin email configured &mdash; click <em>Not configured</em> in the recipient column to jump to the Capability Managers page.</li>
                        <li>Set the due date if you want to override the default, then click <em>Send / remind selected</em> on the rows you want to issue. Each click dispatches the group summary plus one email per POC.</li>
                        <li>The result line reports the number of initial / reminder packages sent and the POC fan-out outcome (sent, skipped, failed). Per-package warnings are listed below.</li>
                        <li>In test mode (ProductionMode = false), use <em>Mark as sent (test)</em> instead &mdash; this drives the lifecycle to Sent without dispatching email but still writes the audit rows for the AS Fin and per-POC sends that would have been made.</li>
                    </ol>

                    <h3>Sending reminders</h3>
                    <p>Reminders are valid only on <strong>Sent</strong> or <strong>In review</strong> packages. NotSent uses Send instead; Finalised / Exported / Cancelled packages cannot be reminded. The dashboard surfaces packages that are near deadline or overdue. POCs whose documents are all reviewed are automatically skipped on a reminder so they are not chased for completed work; the AS Fin reminder always goes regardless of progress.</p>

                    <h3>Notifying AS Fin after finalise</h3>
                    <p>Once a package is Finalised, a <em>Notify AS Fin</em> button appears in the actions cell of that row on the Send-outs page. Use it to explicitly inform the responsible AS Fin officer that the package has been closed off. Each click prompts for a recipient email (must be a Defence or ANPSR address); the CM team mailbox is added on CC automatically and LPPI support is BCCed. The email carries the package summary &mdash; payable and not-payable counts and dollars, broken down by RC-NR (defaulted on finalise) and RC-RL (reload-eligible, returns next cycle) so the recipient can see exactly what has been decided. The link in the email opens the reviewer page read-only.</p>

                    <h3>Finalising and unfinalising (AS Fin self-service)</h3>
                    <p>This happens on the reviewer page using the AS Fin link, not in the admin pages &mdash; AS Fin manages its own workflow. POC links cannot finalise.</p>
                    <ul>
                        <li>The toolbar <em>Finalise</em> button (green) closes the package off. Any documents that were not coded are auto-marked as <code>RC-NR</code> (Payable &mdash; no response received from CM). Form fields lock.</li>
                        <li>The same slot becomes <em>Unfinalise</em> (orange) when the package is Finalised. Clicking it clears the auto-applied <code>RC-NR</code> rows, returns the package to InReview, and reopens the form. History is recorded for both directions.</li>
                        <li>Once a package is Exported it is terminal &mdash; the toolbar shows no action button.</li>
                    </ul>

                    <h3>Exporting payable cases to ERP</h3>
                    <ol>
                        <li>Open <strong>Export</strong>. The page lists Finalised packages awaiting export.</li>
                        <li>Tick one or more packages. The totals strip at the bottom shows the package count, payable doc count, and total payable dollars for what you have selected.</li>
                        <li>Click <em>Generate ERP file</em>. The xlsx is built, stored against an export-batch row in the database, and downloaded to your browser.</li>
                        <li>The picked packages flip to <strong>Exported</strong> and disappear from the picker.</li>
                        <li>The Recent batches table at the bottom of the page shows past export runs with Download buttons &mdash; the file is re-streamed from the database, no need to regenerate.</li>
                    </ol>
                    <div class="callout warn">
                        <strong>Export is irreversible.</strong> Once a package is Exported, it cannot be unfinalised or modified. If you need to amend a payment after export, do it through the normal ERP correction process &mdash; not by trying to undo the export here.
                    </div>
                </section>

                <section id="support">
                    <h2>Support</h2>
                    <p>For functional questions, use the <strong>Feedback &amp; support</strong> button in the page header &mdash; it opens a pre-populated mailto with the support inbox addresses configured in <code>web.config</code>.</p>
                    <p>For module documentation and source, see the repository <code>README.md</code> at the root folder and the SQL scripts in the <code>sql/</code> subfolder.</p>
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
