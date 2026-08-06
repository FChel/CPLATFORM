<%@ Page Language="C#" AutoEventWireup="true" CodeFile="CFO-Toolkit.aspx.cs" Inherits="CFOToolkitPage" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="theme-color" content="#071514" />
    <title>CFO Toolkit | CPlatform</title>
    <link rel="stylesheet" href="css/cfo-toolkit.css?v=20260806-3" />
</head>
<body>
    <form id="form1" runat="server">
        <div class="ambient ambient-one"></div>
        <div class="ambient ambient-two"></div>
        <div class="page-grid"></div>

        <div class="site-shell">
            <header class="site-header">
                <a class="wordmark" href="CFO-Toolkit.aspx" aria-label="CFO Toolkit home">
                    <span class="wordmark-mark" aria-hidden="true">
                        <span>CFO</span>
                    </span>
                    <span class="wordmark-copy">
                        <strong>CFO Toolkit</strong>
                        <small>Finance modules, one place</small>
                    </span>
                </a>

                <div class="header-meta">
                    <span class="platform-label">CPlatform</span>
                    <span class="environment <%= EnvironmentClass %>">
                        <i aria-hidden="true"></i>
                        <%= HttpUtility.HtmlEncode(EnvironmentLabel) %>
                    </span>
                </div>
            </header>

            <main>
                <section class="hero">
                    <div class="hero-copy">
                        <span class="hero-kicker"><i></i>Australian Government finance</span>
                        <h1>Prepare. Transact.<br /><em>Assure. Report.</em></h1>
                        <p>Transact, investigate, assure and report—built to help you find answers, complete tasks and deliver reliable financial information.</p>

                        <div class="hero-actions">
                            <a class="button button-primary" href="#toolkit">
                                Explore the toolkit
                                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m7 10 5 5 5-5"/></svg>
                            </a>
                            <% if (!String.IsNullOrWhiteSpace(PrimaryLaunchUrl())) { %>
                            <a class="button button-quiet button-launch" href="<%= HttpUtility.HtmlAttributeEncode(PrimaryLaunchUrl()) %>" target="_blank" rel="noopener noreferrer">
                                <span><%= HttpUtility.HtmlEncode(PrimaryLaunchLabel()) %></span>
                                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6"/></svg>
                            </a>
                            <% } %>
                        </div>

                        <div class="hero-modes" aria-label="CFO Toolkit capabilities">
                            <span>Transact</span><i></i><span>Investigate</span><i></i><span>Assure</span><i></i><span>Report</span>
                        </div>
                    </div>

                    <div class="hero-panel" aria-label="Finance workflow overview">
                        <div class="panel-orbit orbit-one"></div>
                        <div class="panel-orbit orbit-two"></div>
                        <div class="panel-topline">
                            <span>Connected workflow</span>
                            <span class="live-state"><i></i>Ready</span>
                        </div>
                        <div class="panel-centre">
                            <span class="panel-monogram">CFO</span>
                            <small>Toolkit</small>
                        </div>
                        <div class="workflow-step step-one">
                            <span class="step-number">01</span>
                            <span><b>Transact</b><small>Journals and forms</small></span>
                        </div>
                        <div class="workflow-step step-two">
                            <span class="step-number">02</span>
                            <span><b>Investigate</b><small>POs, SAP and accounts</small></span>
                        </div>
                        <div class="workflow-step step-three">
                            <span class="step-number">03</span>
                            <span><b>Assure</b><small>Reviews and reconciliations</small></span>
                        </div>
                        <div class="workflow-step step-four">
                            <span class="step-number">04</span>
                            <span><b>Report</b><small>Statements and schedules</small></span>
                        </div>
                    </div>
                </section>

                <section class="toolkit-section" id="toolkit">
                    <div class="section-heading">
                        <div>
                            <span class="section-kicker">Finance modules</span>
                            <h2>Select the module you need.</h2>
                        </div>
                        <p>Purpose-built modules for common government finance activities.</p>
                    </div>

                    <div class="tool-grid">
                        <% if (IsTileVisible("NORMWorkspace")) { %>
                        <a class="tool-card tool-featured theme-ember" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("NORMWorkspace")) %>" target="_blank" rel="noopener noreferrer">
                            <span class="card-glow"></span>
                            <div class="tool-topline">
                                <span class="tool-icon">
                                    <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 8h4M7 12h10M7 16h7M17 8v5M14.5 10.5h5"/></svg>
                                </span>
                                <span class="tool-label">Financial statements</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Control centre</span>
                                <h3>NORM Workspace</h3>
                                <p>Load trial balances, coordinate preparation and move every statement from source to sign-off.</p>
                            </div>
                            <div class="feature-flow" aria-hidden="true">
                                <span>Load</span><i></i><span>Prepare</span><i></i><span>Review</span><i></i><span>Approve</span>
                            </div>
                            <span class="tool-action">Open workspace <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (HasStatementsTile()) { %>
                        <a class="tool-card theme-sky" href="<%= HttpUtility.HtmlAttributeEncode(FirstTileUrl("NORMStatements", "NORM")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 2h8l4 4v16H6zM14 2v4h4M9 13h6M9 17h6M9 9h2"/></svg></span>
                                <span class="tool-label">Published view</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Traceable reporting</span>
                                <h3>NORM Statements</h3>
                                <p>Review financial statements, assurance results and figure-level lineage.</p>
                            </div>
                            <span class="tool-action">View statements <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("LPPI")) { %>
                        <a class="tool-card tool-wide-when-last theme-mint" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("LPPI")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6"/><circle cx="16" cy="16" r="3"/><path d="M16 14.5V16l1 1"/></svg></span>
                                <span class="tool-label">Compliance review</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Payment assurance</span>
                                <h3>LPPI Review</h3>
                                <p>Review and classify Late Payment Penalty Interest cases efficiently.</p>
                            </div>
                            <span class="tool-action">Open dashboard <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("PrepaymentWorkspace")) { %>
                        <a class="tool-card tool-prominent theme-blue" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("PrepaymentWorkspace")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon">
                                    <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 8h10M7 12h4M7 16h3M15 12v6M12 15h6"/></svg>
                                </span>
                                <span class="tool-label">Prepayment lifecycle</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Recognition and control</span>
                                <h3>Prepayment Management</h3>
                                <p>Identify prepayment POs and invoices, establish amortisation schedules, generate journals and reconcile G/L balances.</p>
                            </div>
                            <div class="feature-flow" aria-hidden="true">
                                <span>Identify</span><i></i><span>Recognise</span><i></i><span>Amortise</span><i></i><span>Reconcile</span>
                            </div>
                            <span class="tool-action">Open module <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("eJET")) { %>
                        <a class="tool-card theme-violet" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("eJET")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h10M17 16l3 3-3 3"/></svg></span>
                                <span class="tool-label">Journal preparation</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Validate and submit</span>
                                <h3>eJET</h3>
                                <p>Prepare validated journal entry transactions with less rework.</p>
                            </div>
                            <span class="tool-action">Prepare a journal <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("COA")) { %>
                        <a class="tool-card theme-gold" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("COA")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3M8 11h6M11 8v6"/></svg></span>
                                <span class="tool-label">Reference module</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Find the right code</span>
                                <h3>Chart of Accounts</h3>
                                <p>Search cost centres, G/L accounts and valid accounting combinations.</p>
                            </div>
                            <span class="tool-action">Search the chart <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("eJETMulti")) { %>
                        <a class="tool-card theme-coral" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("eJETMulti")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg></span>
                                <span class="tool-label">Bulk journals</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">One template, many entries</span>
                                <h3>eJET Multi</h3>
                                <p>Generate multiple journals from a single structured template.</p>
                            </div>
                            <span class="tool-action">Build journals <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("DFGForms")) { %>
                        <a class="tool-card theme-blue" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("DFGForms")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6M9 14h6M9 17h4"/></svg></span>
                                <span class="tool-label">Guided forms</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">Route work cleanly</span>
                                <h3>Finance Forms</h3>
                                <p>Complete finance forms with built-in workflow routing.</p>
                            </div>
                            <span class="tool-action">Browse forms <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>

                        <% if (IsTileVisible("CAPS")) { %>
                        <a class="tool-card theme-slate" href="<%= HttpUtility.HtmlAttributeEncode(TileUrl("CAPS")) %>" target="_blank" rel="noopener noreferrer">
                            <div class="tool-topline">
                                <span class="tool-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m12 2 10 5-10 5L2 7l10-5zM2 12l10 5 10-5M2 17l10 5 10-5"/></svg></span>
                                <span class="tool-label">Platform administration</span>
                            </div>
                            <div class="tool-copy">
                                <span class="eyebrow">System access</span>
                                <h3>CAPS Portal</h3>
                                <p>Open the CAPS administration and platform portal.</p>
                            </div>
                            <span class="tool-action">Open CAPS <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg></span>
                        </a>
                        <% } %>
                    </div>
                </section>

                <section class="principles">
                    <div class="principles-lead">
                        <span class="section-kicker">Designed for finance teams</span>
                        <h2>Clear access.<br />Reliable outcomes.</h2>
                    </div>
                    <div class="principle-list">
                        <div><span>01</span><p><b>One access point</b> for the finance modules teams use most.</p></div>
                        <div><span>02</span><p><b>Clear functions</b> and next steps for each task.</p></div>
                        <div><span>03</span><p><b>Traceable assurance</b> from source data to final output.</p></div>
                    </div>
                </section>
            </main>

            <footer>
                <div><strong>CFO Toolkit</strong><span>Powered by CPlatform</span></div>
                <span class="footer-status"><i></i><%= HttpUtility.HtmlEncode(EnvironmentLabel) %> environment</span>
            </footer>
        </div>
    </form>
</body>
</html>
