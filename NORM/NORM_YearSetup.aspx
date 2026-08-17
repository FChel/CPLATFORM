<%@ Page Language="C#" AutoEventWireup="true" CodeFile="NORM_YearSetup.aspx.cs" Inherits="CPlatform.NORM.NORM_YearSetup" %>
<!doctype html>
<html lang="en-AU">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NORM - Start of Financial Year Setup</title>
    <link rel="stylesheet" href="../css/norm.css?v=20260817-3" />
</head>
<body class="norm-page">
<form id="form1" runat="server" enctype="multipart/form-data">
    <header class="norm-topbar">
        <a class="norm-brand" href="NORM_Workspace.aspx"><span class="norm-brand-mark">N</span><span><strong>NORM</strong><small>Start of Financial Year Setup</small></span></a>
        <nav class="norm-top-actions"><a href="../Default.aspx">FinHub</a><a href="NORM_Workspace.aspx">Control Centre</a><a href="NORM_Statements.aspx">Financial statements</a><a href="NORM_Help.aspx">Help</a><span class="norm-chip norm-env"><%= Server.HtmlEncode(CurrentEnvironment) %></span><span class="norm-user"><%= Server.HtmlEncode(CurrentUser) %></span></nav>
    </header>

    <main class="norm-year-setup-shell">
        <section class="norm-year-setup-hero">
            <div><span class="norm-kicker">NORM Control Centre</span><h1>Start of Financial Year Setup</h1><p>Set the reporting year once, then load the authoritative comparative and Original Budget documents. NORM scans the complete document for financial-statement headings and maps high-confidence figures into the controlled reporting set.</p></div>
            <aside><span>Reporting period</span><strong>FY<%= Server.HtmlEncode(CurrentYearDisplay) %></strong><small>Comparative year: <%= Server.HtmlEncode(PriorYearDisplay) %></small></aside>
        </section>

        <asp:Panel ID="MessagePanel" runat="server" CssClass="norm-alert norm-alert-success" Visible="false"><asp:Literal ID="MessageText" runat="server" /></asp:Panel>
        <asp:Panel ID="ErrorPanel" runat="server" CssClass="norm-alert norm-alert-error" Visible="false"><strong>Setup could not be saved</strong><asp:Literal ID="ErrorText" runat="server" /></asp:Panel>
        <asp:Panel ID="InstallPanel" runat="server" CssClass="norm-reporting-install" Visible="false"><strong>Start-of-year database objects are not installed.</strong><p>Run <code>sql/NORM_08_StartOfFinancialYearSetup.sql</code>, then reload this page.</p></asp:Panel>

        <section class="norm-year-setup-panel">
            <header><div><span class="norm-step">01</span><div><span class="norm-kicker">Reporting period</span><h2>Set the current financial year</h2></div></div><p>This year drives statement headings. The comparative year is calculated automatically as current year minus one.</p></header>
            <div class="norm-year-field-row">
                <label for="CurrentFinancialYear"><span>Current financial year</span><asp:TextBox ID="CurrentFinancialYear" runat="server" CssClass="norm-input" MaxLength="4" inputmode="numeric" pattern="[0-9]{4}" required="required" title="Enter exactly four digits, for example 2025." placeholder="2025" /></label>
                <div class="norm-year-preview"><span>Previous financial year</span><strong id="priorYearPreview"><%= Server.HtmlEncode(PriorYearDisplay) %></strong><small>Calculated automatically</small></div>
                <asp:Button ID="SaveYearButton" runat="server" Text="Save financial year" CssClass="norm-button" OnClick="SaveYearButton_Click" />
            </div>
        </section>

        <section class="norm-year-setup-panel">
            <header><div><span class="norm-step">02</span><div><span class="norm-kicker">Controlled source documents</span><h2>Load comparatives and Original Budget</h2></div></div><p>NORM searches the full PDF, Word document or Excel workbook. A page or sheet is requested only if the document cannot be confidently located or read.</p></header>
            <div class="norm-year-upload-grid">
                <article class="norm-year-upload-card">
                    <div class="norm-year-upload-icon">PY</div><span class="norm-kicker">Comparative figures</span><h3>Prior Year Financial Statements</h3><p>Loads the audited current-year column from the prior-year statements into this year's comparative column.</p>
                    <div class="norm-file-box"><asp:FileUpload ID="PriorYearFile" runat="server" CssClass="norm-file-input" accept=".pdf,.doc,.docx,.xls,.xlsx" /><small>PDF, Word or Excel · maximum 100 MB</small></div>
                    <asp:Button ID="UploadPriorButton" runat="server" Text="Upload prior-year statements" CssClass="norm-button norm-button-dark" OnClick="UploadPriorButton_Click" CausesValidation="false" />
                    <%= PriorDocumentHtml %>
                </article>
                <article class="norm-year-upload-card">
                    <div class="norm-year-upload-icon">OB</div><span class="norm-kicker">Original Budget</span><h3>Portfolio Budget Statements</h3><p>Loads the approved budget column into the Original Budget column used throughout the financial statements.</p>
                    <div class="norm-file-box"><asp:FileUpload ID="BudgetFile" runat="server" CssClass="norm-file-input" accept=".pdf,.doc,.docx,.xls,.xlsx" /><small>PDF, Word or Excel · maximum 100 MB</small></div>
                    <asp:Button ID="UploadBudgetButton" runat="server" Text="Upload Portfolio Budget Statements" CssClass="norm-button norm-button-dark" OnClick="UploadBudgetButton_Click" CausesValidation="false" />
                    <%= BudgetDocumentHtml %>
                </article>
            </div>
        </section>

        <section class="norm-year-setup-panel">
            <header><div><span class="norm-step">03</span><div><span class="norm-kicker">Extraction assurance</span><h2>Detected statement figures</h2></div></div><p>Only high-confidence label and column matches are applied automatically. The source document, locator and confidence are retained for review.</p></header>
            <%= FigurePreviewHtml %>
        </section>
    </main>
    <div id="normUploadProgress" class="norm-upload-progress" role="dialog" aria-modal="true" aria-labelledby="normUploadProgressTitle" aria-describedby="normUploadProgressDetail" hidden>
        <div class="norm-upload-progress-card">
            <div class="norm-upload-progress-mark" aria-hidden="true"><span>N</span><i></i></div>
            <span class="norm-kicker">Document intake · overall progress (estimated)</span>
            <div class="norm-upload-progress-heading"><div><h2 id="normUploadProgressTitle">Loading source document</h2><p id="normUploadProgressPhase">Preparing secure upload</p></div><strong id="normUploadProgressPercent">0%</strong></div>
            <div id="normUploadProgressTrack" class="norm-upload-progress-track" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><i id="normUploadProgressBar"></i></div>
            <p id="normUploadProgressDetail" class="norm-upload-progress-detail">NORM is preparing the controlled source document.</p>
            <div class="norm-upload-progress-stages" aria-hidden="true"><span class="active">Upload</span><i></i><span>Scan document</span><i></i><span>Map figures</span></div>
            <small>Keep this window open. Large or image-heavy PDFs can take a few minutes to scan.</small>
            <button id="normUploadProgressClose" type="button" class="norm-button norm-button-dark" hidden>Close</button>
        </div>
    </div>
    <script>
    (function(){
        var yearInput=document.getElementById('<%= CurrentFinancialYear.ClientID %>'),preview=document.getElementById('priorYearPreview');
        if(yearInput&&preview){yearInput.addEventListener('input',function(){preview.textContent=/^[0-9]{4}$/.test(yearInput.value)?String(Number(yearInput.value)-1):'—';});}

        var form=document.getElementById('form1'),priorButton=document.getElementById('<%= UploadPriorButton.ClientID %>'),budgetButton=document.getElementById('<%= UploadBudgetButton.ClientID %>'),
            priorFile=document.getElementById('<%= PriorYearFile.ClientID %>'),budgetFile=document.getElementById('<%= BudgetFile.ClientID %>'),dialog=document.getElementById('normUploadProgress'),
            title=document.getElementById('normUploadProgressTitle'),phase=document.getElementById('normUploadProgressPhase'),percent=document.getElementById('normUploadProgressPercent'),
            detail=document.getElementById('normUploadProgressDetail'),track=document.getElementById('normUploadProgressTrack'),bar=document.getElementById('normUploadProgressBar'),
            stages=dialog?dialog.querySelectorAll('.norm-upload-progress-stages span'):[],closeButton=document.getElementById('normUploadProgressClose'),activeButton=null,processingTimer=null,currentProgress=0;
        if(!form||!priorButton||!budgetButton||!dialog||!window.FormData||!window.XMLHttpRequest){return;}

        priorButton.addEventListener('click',function(){activeButton=priorButton;});
        budgetButton.addEventListener('click',function(){activeButton=budgetButton;});
        closeButton.addEventListener('click',function(){dialog.hidden=true;document.body.classList.remove('norm-upload-busy');priorButton.disabled=false;budgetButton.disabled=false;});

        function setProgress(value,heading,status,message,stage){
            currentProgress=Math.max(currentProgress,Math.min(100,Math.round(value)));
            title.textContent=heading;phase.textContent=status;detail.textContent=message;percent.textContent=currentProgress+'%';bar.style.width=currentProgress+'%';track.setAttribute('aria-valuenow',String(currentProgress));
            for(var i=0;i<stages.length;i++){stages[i].className=i<stage?'complete':(i===stage?'active':'');}
        }
        function showFailure(message){
            if(processingTimer){window.clearInterval(processingTimer);}dialog.classList.add('failed');title.textContent='Upload could not be completed';phase.textContent='The request stopped before confirmation';detail.textContent=message;closeButton.hidden=false;priorButton.disabled=false;budgetButton.disabled=false;
        }
        function renderResponse(html){document.open();document.write(html);document.close();}

        form.addEventListener('submit',function(event){
            var submitter=event.submitter||activeButton;activeButton=null;
            if(submitter!==priorButton&&submitter!==budgetButton){return;}
            event.preventDefault();
            var fileInput=submitter===priorButton?priorFile:budgetFile,label=submitter===priorButton?'Prior Year Financial Statements':'Portfolio Budget Statements';
            if(!fileInput.files||!fileInput.files.length){fileInput.setCustomValidity('Choose the '+label+' document.');fileInput.reportValidity();window.setTimeout(function(){fileInput.setCustomValidity('');},100);return;}

            var data=new FormData(form);if(submitter.name){data.append(submitter.name,submitter.value||'Upload');}
            dialog.hidden=false;dialog.classList.remove('failed');closeButton.hidden=true;document.body.classList.add('norm-upload-busy');priorButton.disabled=true;budgetButton.disabled=true;
            currentProgress=0;setProgress(3,'Loading '+label,'Preparing secure upload','Checking the file and preparing the controlled upload.',0);

            var xhr=new XMLHttpRequest();xhr.open((form.method||'POST').toUpperCase(),form.action||window.location.href,true);xhr.withCredentials=true;
            xhr.upload.onprogress=function(progressEvent){if(!progressEvent.lengthComputable){return;}var filePercent=Math.round((progressEvent.loaded/progressEvent.total)*100);setProgress(5+(filePercent*.65),'Loading '+label,'Uploading document · '+filePercent+'% transferred','Sending the source document securely to NORM.',0);};
            xhr.upload.onload=function(){setProgress(72,'Reading '+label,'Upload complete · scanning the document','Searching the complete document for statement headings, tables and financial figures.',1);processingTimer=window.setInterval(function(){if(currentProgress<96){setProgress(currentProgress+(currentProgress<88?2:1),'Reading '+label,currentProgress<86?'Scanning statement pages':'Matching figures to the reporting set',currentProgress<86?'NORM is locating statement tables throughout the document.':'NORM is validating labels, columns and high-confidence matches.',currentProgress<86?1:2);}},1400);};
            xhr.onerror=function(){showFailure('The browser lost contact with NORM. Your selected file has not been confirmed as loaded.');};
            xhr.onabort=function(){showFailure('The upload was interrupted before NORM confirmed completion.');};
            xhr.onload=function(){if(processingTimer){window.clearInterval(processingTimer);}if(xhr.status>=200&&xhr.status<400){setProgress(100,label+' loaded','Processing complete','NORM has finished the request and is refreshing the extraction results.',2);window.setTimeout(function(){renderResponse(xhr.responseText);},500);}else{title.textContent='NORM returned an error';phase.textContent='Opening the error details';detail.textContent='The server response will open so the issue can be reviewed.';window.setTimeout(function(){renderResponse(xhr.responseText);},900);}};
            xhr.send(data);
        });
    })();
    </script>
</form>
</body>
</html>
