<%@ Page Language="C#" %>
<%@ Import Namespace="CPlatform.PORD" %>
<!doctype html>
<html lang="en-AU">
<head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>POD — Administrator access</title>
    <link rel="stylesheet" href="../css/lppi.css" /><link rel="stylesheet" href="../css/pord.css?v=20261021d" />
</head>
<body><main class="pord-access card">
    <div class="crumb">POD · PO Review Dashboard</div>
    <h1>Administrator access required</h1>
    <p>Your account <strong><%= PORDHelper.Enc(PORDHelper.CurrentUser) %></strong> does not have access to POD administration. Contact your application administrator to request access.</p>
    <p>If you are reviewing purchase orders, use the personal review link in your notification email.</p>
    <a class="btn btn-primary" href="../Default.aspx">Return to FinHub</a>
</main></body></html>
