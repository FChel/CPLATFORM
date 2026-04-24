<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="LPPI_Info.aspx.cs" Inherits="CPlatform.LPPI.LPPI_Info" %>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>LPPI Review</title>
    <link rel="stylesheet" href="../css/lppi.css" />
    <style>
        .info-shell {
            min-height: 100vh;
            background: var(--bg);
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            padding: 40px 20px;
        }

        .info-card {
            background: var(--white);
            border: 1px solid var(--line);
            border-radius: var(--r-lg);
            box-shadow: var(--shadow-sm);
            max-width: 600px;
            width: 100%;
            padding: 40px;
        }

        .info-brand {
            display: flex;
            align-items: center;
            gap: 12px;
            margin-bottom: 32px;
            text-decoration: none;
            color: var(--ink);
        }

        .info-brand .mark {
            width: 44px;
            height: 44px;
            background: var(--orange);
            border-radius: var(--r);
            display: flex;
            align-items: center;
            justify-content: center;
            flex-shrink: 0;
        }

        .info-brand .mark svg {
            width: 24px;
            height: 24px;
            stroke: #fff;
            fill: none;
            stroke-width: 2;
        }

        .info-brand-title {
            font-size: 1.5rem;
            font-weight: 700;
            line-height: 1.1;
        }

        .info-brand-sub {
            font-size: 0.8rem;
            color: var(--ink-3);
            margin-top: 2px;
        }

        .info-card h1 {
            font-size: 1.25rem;
            margin: 0 0 12px;
        }

        .info-card p {
            color: var(--ink-2);
            font-size: 14px;
            line-height: 1.65;
            margin: 0 0 16px;
        }

        .info-card p:last-child {
            margin-bottom: 0;
        }

        .info-divider {
            border: none;
            border-top: 1px solid var(--line);
            margin: 24px 0;
        }

        .info-identity {
            background: var(--bg);
            border: 1px solid var(--line);
            border-radius: var(--r);
            padding: 12px 16px;
            font-size: 13px;
            color: var(--ink-2);
            margin-bottom: 20px;
        }

        .info-identity strong {
            display: block;
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: var(--ink-3);
            margin-bottom: 4px;
        }

        .info-identity code {
            font-family: Consolas, monospace;
            font-size: 13px;
            color: var(--ink);
        }

        .info-footer {
            margin-top: 32px;
            font-size: 12px;
            color: var(--ink-3);
            text-align: center;
        }
    </style>
</head>
<body>
<form id="form1" runat="server">
<div class="info-shell">

    <div class="info-card">

        <%-- Brand --%>
        <div class="info-brand">
            <div class="mark">
                <svg viewBox="0 0 24 24">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <path d="M14 2v6h6"/>
                    <circle cx="12" cy="15" r="3"/>
                    <path d="M12 13v2l1 1"/>
                </svg>
            </div>
            <div>
                <div class="info-brand-title">LPPI Review</div>
                <div class="info-brand-sub">Defence Finance Group</div>
            </div>
        </div>

        <%-- About --%>
        <h1>About LPPI Review</h1>
        <p>
            LPPI Review is a Defence Finance Group module for reviewing late payment
            penalty interest (LPPI) cases and recording pay&nbsp;/&nbsp;no-pay decisions.
        </p>
        <p>
            If you are a Capability Manager, you will receive an email with a link
            to your review when a batch is ready. No account or login is
            required the link in that email gives you direct access to your documents.
        </p>

        <hr class="info-divider" />

        <%-- Admin access callout --%>
        <h1>Need admin access?</h1>

        <div class="info-identity">
            <strong>Your Windows identity</strong>
            <code><asp:Literal ID="litIdentity" runat="server" /></code>
        </div>

        <p>
            To request access to the LPPI admin pages, contact the LPPI administrator
            and include your Windows identity shown above.
        </p>

        <asp:PlaceHolder ID="phContact" runat="server" />

    </div>

    <div class="info-footer">
        Defence Finance Group &middot; LPPI Review &middot; <asp:Literal ID="litEnv" runat="server" />
    </div>

</div>
</form>
</body>
</html>
