<%@ Page Language="C#" AutoEventWireup="true" CodeFile="Default.aspx.cs" Inherits="CPlatformPage" %>
<!DOCTYPE html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>FinHub &mdash; <%= EnvironmentLabel %></title>
    <link rel="icon" type="image/jpeg" href="Images/Defence.jpg" />

    <!--
        Fonts are fully self-contained. No external network required.

        Primary stack uses Segoe UI, which is present on every Windows workstation
        (Vista and later) and every Windows Server host, so it will render
        identically for any Defence user on a managed SOE. If the user somehow
        does not have it, the stack falls back cleanly through other common
        system fonts to Arial.

        If you later want the more distinctive Inter / Space Grotesk look,
        download the .woff2 files into a Fonts/ folder and uncomment the
        @font-face block at the top of the <style> section, then change the
        --ui-font and --display-font variables. No other changes needed.
    -->

    <style>
        /*
        --- OPTIONAL SELF-HOSTED FONT UPGRADE ---
        Uncomment this block and place the .woff2 files under /Fonts/ if you
        want the Inter + Space Grotesk look. Then change --ui-font to 'Inter'
        and --display-font to 'Space Grotesk' in :root below.

        @font-face {
            font-family: 'Inter';
            src: url('Fonts/Inter-Regular.woff2') format('woff2');
            font-weight: 400; font-style: normal; font-display: swap;
        }
        @font-face {
            font-family: 'Inter';
            src: url('Fonts/Inter-Medium.woff2') format('woff2');
            font-weight: 500; font-style: normal; font-display: swap;
        }
        @font-face {
            font-family: 'Inter';
            src: url('Fonts/Inter-SemiBold.woff2') format('woff2');
            font-weight: 600; font-style: normal; font-display: swap;
        }
        @font-face {
            font-family: 'Inter';
            src: url('Fonts/Inter-Bold.woff2') format('woff2');
            font-weight: 700; font-style: normal; font-display: swap;
        }
        @font-face {
            font-family: 'Space Grotesk';
            src: url('Fonts/SpaceGrotesk-Medium.woff2') format('woff2');
            font-weight: 500; font-style: normal; font-display: swap;
        }
        @font-face {
            font-family: 'Space Grotesk';
            src: url('Fonts/SpaceGrotesk-Bold.woff2') format('woff2');
            font-weight: 700; font-style: normal; font-display: swap;
        }
        */

        :root {
            --def-orange:      #d75b07;
            --def-orange-dark: #b34c05;
            --def-orange-soft: #fff1e6;
            --def-orange-glow: rgba(215, 91, 7, 0.45);

            --paper:    #fbf7f3;   /* warm cream, not white, not grey */
            --paper-2:  #f3ece4;
            --card:     rgba(255, 255, 255, 0.72);
            --card-edge: rgba(215, 91, 7, 0.18);

            --ink:      #1b1410;
            --ink-soft: #4a3f38;
            --muted:    #8a7c70;
            --line:     #ecdfd1;

            /* System font stacks — guaranteed available on Defence SOE, no downloads required */
            --ui-font:      "Segoe UI", "Segoe UI Web", Tahoma, "Helvetica Neue", Arial, sans-serif;
            --display-font: "Segoe UI", "Segoe UI Web", Tahoma, "Helvetica Neue", Arial, sans-serif;
        }

        * { box-sizing: border-box; }

        html, body {
            margin: 0;
            padding: 0;
            min-height: 100%;
            font-family: var(--ui-font);
            color: var(--ink);
            background-color: var(--paper);
            overflow-x: hidden;
            -webkit-font-smoothing: antialiased;
        }

        /* ---------- Animated aurora on a LIGHT base ---------- */
        .aurora {
            position: fixed;
            inset: 0;
            z-index: -3;
            background:
                radial-gradient(55% 45% at 12% 18%, rgba(255,140,40,0.55) 0%, rgba(255,140,40,0) 60%),
                radial-gradient(45% 40% at 88% 10%, rgba(255,180,90,0.50) 0%, rgba(255,180,90,0) 60%),
                radial-gradient(60% 50% at 75% 95%, rgba(215,91,7,0.40)  0%, rgba(215,91,7,0)  60%),
                radial-gradient(50% 50% at 30% 95%, rgba(255,200,140,0.55) 0%, rgba(255,200,140,0) 60%),
                linear-gradient(170deg, var(--paper) 0%, var(--paper-2) 100%);
            animation: drift 22s ease-in-out infinite alternate;
            filter: saturate(1.05);
        }
        @keyframes drift {
            0%   { transform: scale(1)    translate(0,    0); }
            100% { transform: scale(1.06) translate(-1.5%, 1%); }
        }

        /* Subtle grain so the gradient does not look like a CSS demo */
        .grain {
            position: fixed;
            inset: 0;
            z-index: -2;
            pointer-events: none;
            opacity: 0.35;
            mix-blend-mode: multiply;
            background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 0.08 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>");
        }

        /* Faint grid that fades at the edges */
        .grid-overlay {
            position: fixed;
            inset: 0;
            z-index: -1;
            background-image:
                linear-gradient(rgba(120,70,20,0.07) 1px, transparent 1px),
                linear-gradient(90deg, rgba(120,70,20,0.07) 1px, transparent 1px);
            background-size: 56px 56px;
            mask-image: radial-gradient(ellipse at center, #000 35%, transparent 80%);
            -webkit-mask-image: radial-gradient(ellipse at center, #000 35%, transparent 80%);
            pointer-events: none;
        }

        /* ---------- Layout ---------- */
        .shell {
            max-width: 1240px;
            margin: 0 auto;
            padding: 32px 28px 80px 28px;
        }

        /* ---------- Top bar (frosted) ---------- */
        .topbar {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 16px;
            padding: 14px 20px;
            background: var(--card);
            border: 1px solid var(--card-edge);
            border-radius: 16px;
            backdrop-filter: blur(14px) saturate(1.1);
            -webkit-backdrop-filter: blur(14px) saturate(1.1);
            box-shadow: 0 10px 30px rgba(120, 50, 0, 0.08);
        }
        .brand {
            display: flex;
            align-items: center;
            gap: 14px;
        }
        .brand img {
            height: 46px;
            width: auto;
            border-radius: 6px;
            display: block;
        }
        .brand-text {
            display: flex;
            flex-direction: column;
            line-height: 1.1;
        }
        .brand-text .eyebrow {
            font-size: 11px;
            letter-spacing: 0.22em;
            color: var(--muted);
            text-transform: uppercase;
            font-weight: 600;
        }
        .brand-text .name {
            font-family: var(--display-font);
            font-size: 20px;
            font-weight: 700;
            color: var(--ink);
            letter-spacing: 0.02em;
        }

        .env-chip {
            display: inline-flex;
            align-items: center;
            gap: 10px;
            padding: 9px 16px;
            border-radius: 999px;
            font-weight: 700;
            font-size: 11px;
            letter-spacing: 0.18em;
            border: 1px solid var(--card-edge);
            background: rgba(255,255,255,0.6);
            backdrop-filter: blur(8px);
            -webkit-backdrop-filter: blur(8px);
        }
        .env-dot {
            width: 9px;
            height: 9px;
            border-radius: 50%;
            position: relative;
        }
        .env-dot::after {
            content: "";
            position: absolute;
            inset: -4px;
            border-radius: 50%;
            border: 2px solid currentColor;
            opacity: 0.5;
            animation: ring 2.4s ease-out infinite;
        }
        @keyframes ring {
            0%   { transform: scale(0.6); opacity: 0.6; }
            100% { transform: scale(1.8); opacity: 0;   }
        }

        .env-uat     .env-dot { background: #e89b1a; color: #e89b1a; }
        .env-uat              { color: #8a5a00; }
        .env-prod    .env-dot { background: #2ca050; color: #2ca050; }
        .env-prod             { color: #1a6b30; }
        .env-dev     .env-dot { background: #2778c4; color: #2778c4; }
        .env-dev              { color: #155086; }
        .env-unknown .env-dot { background: #c0392b; color: #c0392b; }
        .env-unknown          { color: #8a1f14; }

        /* ---------- Hero ---------- */
        .hero {
            position: relative;
            margin-top: 30px;
            padding: 70px 56px 64px 56px;
            border-radius: 26px;
            background:
                radial-gradient(120% 140% at 0% 0%, rgba(255,180,90,0.35) 0%, rgba(255,180,90,0) 55%),
                radial-gradient(80% 80% at 100% 100%, rgba(215,91,7,0.18) 0%, rgba(215,91,7,0) 60%),
                rgba(255,255,255,0.55);
            border: 1px solid var(--card-edge);
            backdrop-filter: blur(18px) saturate(1.1);
            -webkit-backdrop-filter: blur(18px) saturate(1.1);
            box-shadow:
                0 30px 80px rgba(120, 50, 0, 0.12),
                0 1px 0 rgba(255,255,255,0.8) inset;
            overflow: hidden;
        }
        /* gradient hairline border */
        .hero::before {
            content: "";
            position: absolute;
            inset: -1px;
            border-radius: 26px;
            padding: 1px;
            background: linear-gradient(135deg, rgba(215,91,7,0.65), rgba(255,255,255,0) 45%, rgba(215,91,7,0.55));
            -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
            -webkit-mask-composite: xor;
                    mask-composite: exclude;
            pointer-events: none;
        }
        /* glow blob behind the headline */
        .hero::after {
            content: "";
            position: absolute;
            top: -120px;
            right: -120px;
            width: 420px;
            height: 420px;
            border-radius: 50%;
            background: radial-gradient(circle, var(--def-orange-glow) 0%, rgba(215,91,7,0) 70%);
            pointer-events: none;
            animation: float 9s ease-in-out infinite alternate;
        }
        @keyframes float {
            0%   { transform: translate(0, 0)      scale(1); }
            100% { transform: translate(-30px, 20px) scale(1.08); }
        }

        .hero .kicker {
            display: inline-block;
            font-size: 11px;
            font-weight: 700;
            letter-spacing: 0.28em;
            text-transform: uppercase;
            color: var(--def-orange);
            background: var(--def-orange-soft);
            border: 1px solid #f5d3b0;
            padding: 6px 12px;
            border-radius: 999px;
            margin-bottom: 22px;
        }
        .hero h1 {
            font-family: var(--display-font);
            margin: 0;
            font-size: clamp(46px, 8vw, 92px);
            font-weight: 700;
            letter-spacing: -0.025em;
            line-height: 0.92;
            background: linear-gradient(100deg, #2a1a10 0%, #d75b07 50%, #ff8a3d 100%);
            -webkit-background-clip: text;
                    background-clip: text;
            -webkit-text-fill-color: transparent;
        }
        .hero h1 .accent {
            display: inline-block;
            background: linear-gradient(100deg, #d75b07 0%, #ff8a3d 50%, #ffb27a 100%);
            -webkit-background-clip: text;
                    background-clip: text;
            -webkit-text-fill-color: transparent;
        }
        .hero .tagline {
            margin: 22px 0 0 0;
            font-size: 17px;
            color: var(--ink-soft);
            max-width: 740px;
            line-height: 1.6;
        }
        .hero-cta-row {
            margin-top: 32px;
            display: flex;
            flex-wrap: wrap;
            gap: 12px;
        }
        .btn {
            display: inline-flex;
            align-items: center;
            gap: 8px;
            padding: 14px 22px;
            border-radius: 12px;
            font-family: var(--ui-font);
            font-weight: 700;
            font-size: 14px;
            text-decoration: none;
            border: 1px solid transparent;
            cursor: pointer;
            transition: transform 0.18s ease, box-shadow 0.18s ease, background 0.18s ease;
        }
        .btn-primary {
            background: linear-gradient(135deg, #ff8a3d 0%, var(--def-orange) 60%, var(--def-orange-dark) 100%);
            color: #fff;
            box-shadow: 0 14px 32px rgba(215,91,7,0.35), 0 1px 0 rgba(255,255,255,0.4) inset;
        }
        .btn-primary:hover {
            transform: translateY(-2px);
            box-shadow: 0 18px 40px rgba(215,91,7,0.45), 0 1px 0 rgba(255,255,255,0.4) inset;
        }
        .btn-ghost {
            background: rgba(255,255,255,0.7);
            color: var(--def-orange-dark);
            border-color: var(--def-orange);
            backdrop-filter: blur(8px);
            -webkit-backdrop-filter: blur(8px);
        }
        .btn-ghost:hover {
            background: #fff;
            transform: translateY(-2px);
        }

        /* ---------- Section heading ---------- */
        .section-head {
            margin: 56px 4px 18px 4px;
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            gap: 12px;
        }
        .section-head h2 {
            font-family: var(--display-font);
            margin: 0;
            font-size: 22px;
            font-weight: 700;
            color: var(--ink);
            letter-spacing: -0.01em;
        }
        .section-head h2::before {
            content: "";
            display: inline-block;
            width: 22px;
            height: 3px;
            background: var(--def-orange);
            border-radius: 2px;
            vertical-align: middle;
            margin-right: 12px;
            margin-bottom: 4px;
        }
        .section-head .hint {
            color: var(--muted);
            font-size: 11px;
            letter-spacing: 0.14em;
            text-transform: uppercase;
            font-weight: 600;
        }

        /* ---------- App tile grid ---------- */
        .tiles {
            display: grid;
            grid-template-columns: repeat(4, 1fr);
            gap: 20px;
        }
        .tile {
            position: relative;
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            min-height: 220px;
            padding: 24px;
            border-radius: 20px;
            background:
                linear-gradient(180deg, rgba(255,255,255,0.85) 0%, rgba(255,255,255,0.55) 100%);
            border: 1px solid var(--card-edge);
            color: var(--ink);
            text-decoration: none;
            overflow: hidden;
            backdrop-filter: blur(12px) saturate(1.05);
            -webkit-backdrop-filter: blur(12px) saturate(1.05);
            box-shadow: 0 12px 30px rgba(120, 50, 0, 0.08);
            transition: transform 0.25s ease, box-shadow 0.25s ease, border-color 0.25s ease;
        }
        .tile::after {
            content: "";
            position: absolute;
            inset: 0;
            background: radial-gradient(80% 70% at 100% 0%, rgba(215,91,7,0.18) 0%, rgba(215,91,7,0) 60%);
            opacity: 0;
            transition: opacity 0.25s ease;
            pointer-events: none;
        }
        .tile:hover {
            transform: translateY(-6px);
            border-color: rgba(215,91,7,0.6);
            box-shadow: 0 24px 50px rgba(215, 91, 7, 0.22);
        }
        .tile:hover::after { opacity: 1; }
        .tile:hover .icon  { transform: rotate(-4deg) scale(1.05); }
        .tile:hover .arrow { transform: translateX(4px); }

        .tile .icon {
            width: 50px;
            height: 50px;
            border-radius: 14px;
            background: linear-gradient(135deg, #fff1e6, #ffd9b8);
            border: 1px solid #f0c8a0;
            display: flex;
            align-items: center;
            justify-content: center;
            margin-bottom: 18px;
            box-shadow: 0 6px 16px rgba(215, 91, 7, 0.18);
            transition: transform 0.25s ease;
        }
        .tile .icon svg { width: 24px; height: 24px; stroke: var(--def-orange-dark); fill: none; stroke-width: 2; }
        .tile h3 {
            font-family: var(--display-font);
            margin: 0 0 6px 0;
            font-size: 18px;
            font-weight: 700;
            color: var(--ink);
            letter-spacing: -0.005em;
        }
        .tile p {
            margin: 0;
            color: var(--ink-soft);
            font-size: 13px;
            line-height: 1.55;
        }
        .tile .arrow {
            margin-top: 18px;
            display: inline-flex;
            align-items: center;
            gap: 6px;
            font-size: 11px;
            color: var(--def-orange-dark);
            font-weight: 700;
            letter-spacing: 0.14em;
            text-transform: uppercase;
            transition: transform 0.25s ease;
        }

        /* ---------- Info panel ---------- */
        .info {
            margin-top: 56px;
            padding: 32px 36px;
            border-radius: 20px;
            background: rgba(255,255,255,0.65);
            border: 1px solid var(--card-edge);
            backdrop-filter: blur(12px);
            -webkit-backdrop-filter: blur(12px);
            box-shadow: 0 12px 30px rgba(120, 50, 0, 0.06);
        }
        .info h2 {
            font-family: var(--display-font);
            margin: 0 0 12px 0;
            font-size: 20px;
            font-weight: 700;
            color: var(--ink);
        }
        .info h2::before {
            content: "";
            display: inline-block;
            width: 22px;
            height: 3px;
            background: var(--def-orange);
            border-radius: 2px;
            vertical-align: middle;
            margin-right: 12px;
            margin-bottom: 4px;
        }
        .info p {
            margin: 0 0 10px 0;
            color: var(--ink-soft);
            font-size: 14px;
            line-height: 1.7;
        }
        .info p:last-child { margin-bottom: 0; }
        .info strong { color: var(--def-orange-dark); font-weight: 700; }

        /* ---------- Footer meta ---------- */
        .meta {
            margin-top: 28px;
            display: flex;
            flex-wrap: wrap;
            gap: 22px;
            padding: 14px 20px;
            border-radius: 14px;
            background: rgba(255,255,255,0.55);
            border: 1px solid var(--card-edge);
            color: var(--muted);
            font-size: 12px;
            letter-spacing: 0.04em;
            backdrop-filter: blur(10px);
            -webkit-backdrop-filter: blur(10px);
        }
        .meta span b { color: var(--ink); font-weight: 700; }

        /* ---------- Entry animations ---------- */
        @keyframes rise {
            0%   { opacity: 0; transform: translateY(14px); }
            100% { opacity: 1; transform: translateY(0);    }
        }
        .topbar    { animation: rise 0.5s ease both 0.00s; }
        .hero      { animation: rise 0.6s ease both 0.08s; }
        .section-head { animation: rise 0.5s ease both 0.18s; }
        .tile:nth-child(1) { animation: rise 0.5s ease both 0.22s; }
        .tile:nth-child(2) { animation: rise 0.5s ease both 0.28s; }
        .tile:nth-child(3) { animation: rise 0.5s ease both 0.34s; }
        .tile:nth-child(4) { animation: rise 0.5s ease both 0.40s; }
        .info      { animation: rise 0.5s ease both 0.46s; }
        .meta      { animation: rise 0.5s ease both 0.52s; }

        /* ---------- Responsive ---------- */
        @media (max-width: 1100px) { .tiles { grid-template-columns: repeat(2, 1fr); } }
        @media (max-width: 600px)  {
            .tiles { grid-template-columns: 1fr; }
            .hero  { padding: 44px 26px; }
            .topbar { flex-direction: column; align-items: flex-start; }
        }
    </style>
</head>
<body>
    <div class="aurora"></div>
    <div class="grain"></div>
    <div class="grid-overlay"></div>

    <form id="form1" runat="server">
    <div class="shell">

        <!-- Top bar -->
        <div class="topbar">
            <div class="brand">
                <img src="Images/Defence.jpg" alt="Department of Defence" />
                <div class="brand-text">
                    <span class="eyebrow">Defence Finance Group (DFG)</span>
                    <span class="name">FinHub</span>
                </div>
            </div>
            <div class="env-chip <%= EnvironmentClass %>">
                <span class="env-dot"></span>
                <span><%= EnvironmentLabel %></span>
            </div>
        </div>

        <!-- Hero -->
        <section class="hero">
            <span class="kicker">DFG &middot; FSO &middot; Finance Utilities</span>
            <h1>FinHub<span class="accent"></span></h1>
            <p class="tagline">
                FinHub is a collection of finance utilities &mdash; including chart of accounts search,
                eJET journals, eJET Multi and DFG forms.
            </p>
            <div class="hero-cta-row">
                <a class="btn btn-primary" href="LPPI/LPPI_Admin.aspx" target="_blank" rel="noopener noreferrer">
                    LPPI Review
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M5 12h14M13 6l6 6-6 6"/></svg>
                </a>
                <a class="btn btn-ghost" href="http://creditcarduat.dpesit.protectedsit.mil.au/COASearch.asp" target="_blank" rel="noopener noreferrer">COA Search</a>
            </div>
        </section>

        <!-- Apps -->
        <div class="section-head">
            <h2>Available Utilities</h2>
            <span class="hint">Click to get started</span>
        </div>
        <div class="tiles">

            <a class="tile" href="LPPI/LPPI_Admin.aspx" target="_blank" rel="noopener noreferrer">
                <div>
                    <div class="icon">
                        <svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><circle cx="16" cy="16" r="3"/><path d="M16 14.5V16l1 1"/></svg>
                    </div>
                    <h3>LPPI Review</h3>
                    <p>Review and classify Late Payment Penalty Interest cases from BODS extracts.</p>
                </div>
                <span class="arrow">Open &rarr;</span>
            </a>


            <a class="tile" href="http://creditcarduat.dpesit.protectedsit.mil.au/COASearch.asp" target="_blank" rel="noopener noreferrer">
                <div>
                    <div class="icon">
                        <svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></svg>
                    </div>
                    <h3>Chart of Accounts Search</h3>
                    <p>Look up cost centres, GL accounts and combinations across the chart.</p>
                </div>
                <span class="arrow">Open &rarr;</span>
            </a>

            <a class="tile" href="http://creditcarduat.dpesit.protectedsit.mil.au/eJet.aspx" target="_blank" rel="noopener noreferrer">
                <div>
                    <div class="icon">
                        <svg viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h10"/></svg>
                    </div>
                    <h3>eJET</h3>
                    <p>Prepare, validate and submit journal entry transactions.</p>
                </div>
                <span class="arrow">Open &rarr;</span>
            </a>

            <a class="tile" href="http://creditcarduat.dpesit.protectedsit.mil.au/eJet_Multi.aspx" target="_blank" rel="noopener noreferrer">
                <div>
                    <div class="icon">
                        <svg viewBox="0 0 24 24"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>
                    </div>
                    <h3>eJET Multi</h3>
                    <p>Generate multiple journals from a single template (WBS &rarr; cost centre).</p>
                </div>
                <span class="arrow">Open &rarr;</span>
            </a>

            <a class="tile" href="http://creditcarduat.dpesit.protectedsit.mil.au/Admin/CAPSAdmin/Attachments/Applications/mahdi/CPlatform/Default.aspx" target="_blank" rel="noopener noreferrer">
                <div>
                    <div class="icon">
                        <svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><path d="M9 14h6M9 17h4"/></svg>
                    </div>
                    <h3>DFG Forms</h3>
                    <p>Finance forms with built-in workflow routing.</p>
                </div>
                <span class="arrow">Open &rarr;</span>
            </a>

        </div>

        <!-- About -->
        <section class="info">
            <h2>About this site</h2>
            <p>
                FinHub is a collection of <strong>finance utilities</strong> hosted on the CAPS platform (<strong>CPLATFORM</strong>), it operate independently of CAPS while sharing the same trusted infrastructure.
            </p>
            <p>
                The page you are currently viewing is the <strong><%= EnvironmentLabel %></strong> environment.
            </p>
        </section>

        <!-- Diagnostic strip -->
        <div class="meta">
            <span>Environment: <b><%= EnvironmentLabel %></b></span>
            <span>Site: <b><%= Server.HtmlEncode(string.IsNullOrEmpty(SiteName) ? "(unknown)" : SiteName) %></b></span>
            <span>Host: <b><%= Server.HtmlEncode(string.IsNullOrEmpty(HostName) ? "(unknown)" : HostName) %></b></span>
            <span>Server time: <b><%= DateTime.Now.ToString("dd MMM yyyy HH:mm") %></b></span>
        </div>

    </div>
    </form>
</body>
</html>
