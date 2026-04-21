<%@ Page Language="C#" AutoEventWireup="true" Debug="true" %>
<%@ Import Namespace="System" %>
<%@ Import Namespace="System.IO" %>
<%@ Import Namespace="System.Text" %>
<%@ Import Namespace="System.Web" %>
<%@ Import Namespace="System.Collections.Generic" %>
<%@ Import Namespace="System.Data.OleDb" %>
<%@ Import Namespace="System.Xml" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="Newtonsoft.Json" %>
<%@ Import Namespace="OfficeOpenXml" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>eJET Multi - WBS to Cost Centre Transfer</title>
	<%
		string version = DateTime.Now.Ticks.ToString();
	%>
	<meta name="version" content="<%= version %>" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
	<meta http-equiv="Cache-Control" content="no-cache, no-store, must-revalidate" />
	<meta http-equiv="Pragma" content="no-cache" />
	<meta http-equiv="Expires" content="0" />
    <link rel="shortcut icon" href="~/assets/img/favicon.ico" type="image/x-icon" />
    <link rel="icon" type="image/png" sizes="32x32" href="~/assets/img/favicon-32.png" />
    <link rel="icon" type="image/png" sizes="16x16" href="~/assets/img/favicon-16.png" />	
    
	<style type="text/css">
		/* Modern, clean design with Excel-like features */		
		{
			box-sizing: border-box;
		}
		
		body {
			font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, "Helvetica Neue", Arial, sans-serif;
			margin: 0;
			padding: 0;
			background-color: #f8f9fa;
			color: #2c3e50;
			font-size: 14px;
			padding-bottom: 60px;
		}
		
		/* Color palette variables for easy reference:
		   Primary grey: #5a6770
		   - Darker: #4a5660, #3a4650
		   - Lighter: #6a7780, #7a8890
		   
		   Accent orange: #cf4520
		   - Darker: #b83a1a, #a03015
		   - Lighter: #d95530, #e36540
		   - Very light: rgba(207, 69, 32, 0.1)
		*/		
		
		.app-header {
			background: linear-gradient(135deg, #5a6770 0%, #4a5660 100%);
			color: white;
			padding: 16px 24px;
			box-shadow: 0 2px 8px rgba(0,0,0,0.1);
		}
		
		.app-header h1 {
			margin: 0;
			font-size: 24px;
			font-weight: 600;
			letter-spacing: -0.5px;
		}
		
		.app-subtitle {
			margin: 4px 0 0 0;
			font-size: 14px;
			opacity: 0.9;
		}
		
		/* Content wrapper */
		.content-wrapper {
			max-width: 95%;
			margin: 24px auto;
			padding: 0 24px;
		}
		
		/* Modern panel design */
		.modern-panel {
			background: white;
			border-radius: 12px;
			box-shadow: 0 1px 3px rgba(0,0,0,0.1);
			overflow: hidden;
			margin-bottom: 24px;
		}
		
		.panel-header {
			background: linear-gradient(180deg, #fff 0%, #fafbfc 100%);
			padding: 20px 24px;
			border-bottom: 1px solid #e9ecef;
			display: flex;
			justify-content: space-between;
			align-items: center;
		}
		
		.panel-title {
			font-size: 20px;
			font-weight: 600;
			color: #2c3e50;
			margin: 0;
		}
		
		.panel-content {
			padding: 24px;
			overflow: visible;
		}
		
		/* Status badges */
		.status-badge {
			display: inline-flex;
			align-items: center;
			padding: 6px 12px;
			border-radius: 20px;
			font-size: 12px;
			font-weight: 600;
			background: rgba(207, 69, 32, 0.12);
			color: #b83a1a;
			text-transform: uppercase;
			letter-spacing: 0.5px;
		}
		
		.status-badge.success {
			background: rgba(40, 167, 69, 0.12);
			color: #28a745;
		}
		
		/* Info section */
		.info-section {
			background: #f0f8ff;
			border: 1px solid #b0d4ff;
			border-radius: 8px;
			padding: 16px;
			margin-bottom: 24px;
		}
		
		.info-section h3 {
			margin: 0 0 8px 0;
			color: #2c5aa0;
			font-size: 16px;
		}
		
		.info-section p {
			margin: 4px 0;
			color: #495057;
		}
		
		.info-section ul {
			margin: 8px 0;
			padding-left: 24px;
		}
		
		.info-section li {
			margin: 4px 0;
		}
		
		/* Summary statistics */
		.stats-grid {
			display: grid;
			grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
			gap: 16px;
			margin-bottom: 24px;
		}
		
		.stat-card {
			background: #f8f9fa;
			border: 1px solid #e9ecef;
			border-radius: 8px;
			padding: 16px;
			text-align: center;
		}
		
		.stat-label {
			font-size: 12px;
			color: #6c757d;
			text-transform: uppercase;
			letter-spacing: 0.5px;
			margin-bottom: 4px;
		}
		
		.stat-value {
			font-size: 24px;
			font-weight: 700;
			color: #2c3e50;
		}
		
		/* Modern buttons */
		.modern-button, .asp-button {
			height: 40px;
			padding: 0 20px;
			border: none;
			border-radius: 8px;
			font-size: 14px;
			font-weight: 600;
			cursor: pointer;
			transition: all 0.2s ease;
			font-family: inherit;
			display: inline-flex;
			align-items: center;
			gap: 8px;
		}
		
		.button-primary {
			background: linear-gradient(135deg, #cf4520 0%, #b83a1a 100%);
			color: white;
		}
		
		.button-primary:hover {
			background: linear-gradient(135deg, #d95530 0%, #cf4520 100%);
			transform: translateY(-1px);
			box-shadow: 0 4px 12px rgba(207, 69, 32, 0.3);
		}
		
		.button-secondary {
			background: #f8f9fa;
			color: #4a5660;
			border: 2px solid #e9ecef;
		}
		
		.button-secondary:hover {
			background: #e9ecef;
			border-color: #6a7780;
			color: #3a4650;
		}
		
		.button-success {
			background: linear-gradient(135deg, #28a745 0%, #218838 100%);
			color: white;
		}
		
		.button-success:hover {
			background: linear-gradient(135deg, #34ce57 0%, #28a745 100%);
			transform: translateY(-1px);
			box-shadow: 0 4px 12px rgba(40, 167, 69, 0.3);
		}
		
		/* Excel-like table styling */
		.excel-toolbar {
			background: #217346;
			color: white;
			padding: 8px 16px;
			display: flex;
			align-items: center;
			gap: 12px;
			border-radius: 8px 8px 0 0;
		}
		
		.excel-button {
			background: rgba(255,255,255,0.2);
			border: 1px solid rgba(255,255,255,0.3);
			color: white;
			padding: 6px 12px;
			border-radius: 4px;
			cursor: pointer;
			font-size: 13px;
			font-weight: 500;
			display: inline-flex;
			align-items: center;
			gap: 6px;
			transition: all 0.2s ease;
		}
		
		.excel-button:hover {
			background: rgba(255,255,255,0.3);
			transform: translateY(-1px);
		}
		
		.line-items-container {
			position: relative;
			overflow: auto;
			max-height: 600px;
			background: white;
			border: 1px solid #d0d0d0;
			border-radius: 0 0 8px 8px;
		}
		
		.line-items-table {
			border-collapse: collapse;
			width: 100%;
			font-size: 13px;
			table-layout: fixed;
			background: white;
			min-width: 2400px; /* Increased to accommodate all fields */
		}
		
		/* Excel-like headers */
		.line-items-table th {
			background: #e9e9e9;
			border: 1px solid #d0d0d0;
			padding: 8px 6px;
			position: sticky;
			top: 0;
			z-index: 10;
			font-weight: 500;
			font-size: 13px;
			color: #333;
			text-align: left;
			user-select: none;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}
		
		.line-items-table th:hover {
			background: #d9d9d9;
		}
		
		/* Excel-like cells */
		.line-items-table td {
			border: 1px solid #d0d0d0;
			padding: 4px 6px;
			position: relative;
			background: white;
			overflow: hidden;
			text-overflow: ellipsis;
			white-space: nowrap;
		}
		
		.line-items-table tr:hover td {
			background-color: #f7f9fc;
		}
		
		.line-items-table tr.error-row td {
			background-color: #fee;
		}
		
		.line-items-table tr.success-row td {
			background-color: #efe;
		}
		
		/* Align numeric columns */
		.align-right {
			text-align: right;
		}
		
		.align-center {
			text-align: center;
		}
		
		/* Loading overlay */
		.loading-overlay {
			display: none;
			position: fixed;
			top: 0;
			left: 0;
			width: 100%;
			height: 100%;
			background: rgba(255,255,255,0.9);
			backdrop-filter: blur(5px);
			z-index: 9999;
		}
		
		.loading-spinner {
			position: absolute;
			top: 50%;
			left: 50%;
			transform: translate(-50%, -50%);
			text-align: center;
		}
		
		.loading-spinner-icon {
			width: 48px;
			height: 48px;
			border: 3px solid #f3f3f3;
			border-top: 3px solid #cf4520;
			border-radius: 50%;
			animation: spin 0.8s linear infinite;
			margin: 0 auto 16px;
		}
		
		.loading-text {
			font-size: 16px;
			color: #4a5660;
			font-weight: 600;
		}
		
		@keyframes spin {
			0% { transform: rotate(0deg); }
			100% { transform: rotate(360deg); }
		}
		
		/* Floating footer */
		.floating-footer {
			position: fixed;
			bottom: 0;
			left: 0;
			right: 0;
			background: linear-gradient(135deg, #5a6770 0%, #4a5660 100%);
			padding: 16px 0;
			box-shadow: 0 -2px 10px rgba(0,0,0,0.1);
			z-index: 1000;
		}
		
		.floating-footer-content {
			max-width: 95%;
			margin: 0 auto;
			padding: 0 24px;
			display: flex;
			justify-content: space-between;
			align-items: center;
		}
		
		.footer-left, .footer-right {
			display: flex;
			gap: 12px;
			align-items: center;
			flex-wrap: nowrap;
		}
		
		.footer-left label {
			color: white;
			font-weight: 500;
			white-space: nowrap;
			margin: 0;			
		}
		
		input[type="file"] {
			padding: 8px;
			background: white;
			border-radius: 8px;
			font-size: 13px;
		}		
		
		/* Status messages - centered */
		.status-message {
			position: fixed;
			top: 20px;
			left: 50%;
			transform: translateX(-50%);
			padding: 20px 50px 20px 24px;
			border-radius: 8px;
			display: none;
			font-weight: 500;
			box-shadow: 0 4px 12px rgba(0,0,0,0.15);
			z-index: 10000;
			animation: slideInDown 0.3s ease;
			max-width: 500px;
			min-width: 300px;
			white-space: pre-line;
			line-height: 1.5;
		}
		
		@keyframes slideInDown {
			from {
				transform: translate(-50%, -100%);
				opacity: 0;
			}
			to {
				transform: translate(-50%, 0);
				opacity: 1;
			}
		}
		
		@keyframes slideOutUp {
			from {
				transform: translate(-50%, 0);
				opacity: 1;
			}
			to {
				transform: translate(-50%, -100%);
				opacity: 0;
			}
		}
		
		.status-success {
			background-color: #28a745;
			color: white;
			border: none;
		}
		
		.status-error {
			background-color: #dc3545;
			color: white;
			border: none;
		}
		
		.status-warning {
			background-color: #ffc107;
			color: #212529;
			border: none;
		}
		
		.toast-close {
			position: absolute;
			top: 8px;
			right: 12px;
			font-size: 20px;
			cursor: pointer;
			opacity: 0.8;
			transition: opacity 0.2s;
			background: none;
			border: none;
			color: inherit;
			padding: 4px;
			line-height: 1;
		}
		
		.toast-close:hover {
			opacity: 1;
		}
		
		/* Responsive design */
		@media (max-width: 768px) {
			.stats-grid {
				grid-template-columns: 1fr;
			}
			
			.floating-footer-content {
				flex-direction: column;
				gap: 16px;
			}
			
			.footer-left, .footer-right {
				width: 100%;
				justify-content: center;
			}
		}
	</style>	
   
</head>
<body>
    <form id="form1" runat="server">
		<asp:ScriptManager ID="ScriptManager1" runat="server" EnablePageMethods="true" />
	
        <!-- Page Header -->
		<div class="app-header">
			<h1>eJET Multi</h1>
			<p class="app-subtitle">WBS to Cost Centre Multiple Journal Generation</p>
		</div>
        
        <!-- Main Content -->
        <div class="content-wrapper">
            <!-- Status Message -->
            <asp:Panel ID="pnlStatus" runat="server" CssClass="status-message" Visible="false">
                <asp:Label ID="lblStatus" runat="server"></asp:Label>
				<span class="toast-close" onclick="closeToast()">×</span>
            </asp:Panel>
            
            <!-- Instructions Panel -->
            <div class="modern-panel">
                <div class="panel-header">
                    <h2 class="panel-title">Instructions</h2>
                </div>
                <div class="panel-content">
					<div class="info-section">
						<p>Creates multiple journals from a spreadsheet with each row generating a journal that debits WBS and credits Cost Centre.</p>
						<p>Source spreadsheet should contain the following columns (order doesn't matter):</p>
						<ul>
							<li><strong>Required Header Fields:</strong> Company Code, Document Type, Currency</li>
							<li><strong>Required Line Fields:</strong> WBS Element, Cost Centre, G/L Account, Amount, Line Item Text</li>
							<li><strong>Optional Fields:</strong> Document Date, Posting Date, Exchange Rate, Header Text, Tax Code, Order Number, Reference, Assignment, etc.</li>
						</ul>
					</div>
                </div>
            </div>
            
            <!-- Summary Statistics -->
            <div class="modern-panel" id="summaryPanel" style="display:none;">
                <div class="panel-header">
                    <h2 class="panel-title">Upload Summary</h2>
					<span class="status-badge" id="uploadStatus">Processing</span>
                </div>
                <div class="panel-content">
                    <div class="stats-grid">
                        <div class="stat-card">
                            <div class="stat-label">Total Rows</div>
                            <div class="stat-value" id="statTotalRows">0</div>
                        </div>
                        <div class="stat-card">
                            <div class="stat-label">Journals to Create</div>
                            <div class="stat-value" id="statJournalCount">0</div>
                        </div>
                        <div class="stat-card">
                            <div class="stat-label">Total Amount</div>
                            <div class="stat-value" id="statTotalAmount">0.00</div>
                        </div>
                        <div class="stat-card">
                            <div class="stat-label">Valid Rows</div>
                            <div class="stat-value" id="statValidRows">0</div>
                        </div>
                    </div>
                </div>
            </div>
            
            <!-- Data Preview -->
            <div class="modern-panel" id="dataPanel" style="display:none;">
                <div class="panel-header">
                    <h2 class="panel-title">Data Preview</h2>
                </div>
                <div class="panel-content" style="padding: 0;">
					<div class="excel-toolbar">
						<span style="font-weight: 600; margin-right: 16px;">Uploaded Data</span>
						<button type="button" class="excel-button" onclick="clearData()">
							Clear All
						</button>
						<div style="margin-left: auto; color: rgba(255,255,255,0.9); font-size: 13px;">
							Rows: <span id="lblRowCount" style="font-weight: 600;">0</span>
						</div>
					</div>
					<div class="line-items-container">
						<table class="line-items-table" id="dataTable">
							<thead>
								<tr>
									<th style="width: 40px;" class="align-center">#</th>
									<th style="width: 60px;" class="align-center">Status</th>
									<th style="width: 200px;">Validation</th>
									<th style="width: 80px;">Company Code</th>
									<th style="width: 60px;">Doc Type</th>
									<th style="width: 100px;">Document Date</th>
									<th style="width: 100px;">Posting Date</th>
									<th style="width: 60px;">Currency</th>
									<th style="width: 80px;">Exchange Rate</th>
									<th style="width: 150px;">Header Text</th>
									<th style="width: 150px;">Reference</th>
									<th style="width: 150px;">WBS Element</th>
									<th style="width: 100px;">Cost Centre</th>
									<th style="width: 100px;">G/L Account</th>
									<th style="width: 100px;" class="align-right">Amount</th>
									<th style="width: 60px;">Tax Code</th>
									<th style="width: 200px;">Line Item Text</th>
									<th style="width: 100px;">Order Number</th>
									<th style="width: 150px;">Assignment</th>
									<th style="width: 100px;">Profit Center</th>
									<th style="width: 100px;">Trading Partner</th>
									<th style="width: 100px;">Segment</th>
								</tr>
							</thead>
							<tbody id="dataTableBody">
								<!-- Data rows will be populated here -->
							</tbody>
						</table>
					</div>
                </div>
            </div>
        </div>
        
        <!-- Floating Footer -->
        <div class="floating-footer">
            <div class="floating-footer-content">
				<div class="footer-left">
					<label for="<%= fuSpreadsheet.ClientID %>" style="color:white;margin-right:8px;">Upload Spreadsheet:</label>
					<asp:FileUpload ID="fuSpreadsheet" runat="server" CssClass="sap-input" accept=".xlsx,.xls" onchange="autoUploadSpreadsheet()" style="background:white;" />
					<asp:Button ID="btnUploadSpreadsheet" runat="server" Text="Upload" CssClass="sap-button sap-button-secondary" OnClick="UploadSpreadsheet_Click" style="display:none;" />
				</div>
                <div class="footer-right">                
					<asp:Button ID="btnValidate" runat="server" Text="Validate All" CssClass="modern-button button-secondary" OnClientClick="return false;" UseSubmitBehavior="false" Enabled="false" />
					<asp:Button ID="btnDownload" runat="server" Text="Generate Multi-Journal Template" CssClass="modern-button button-primary" OnClick="GenerateMultiJournal_Click" OnClientClick="showLoadingForDownload();" UseSubmitBehavior="true" Enabled="false" />
                </div>
            </div>
        </div>
        
        <!-- Loading Overlay -->
        <div class="loading-overlay" id="loadingOverlay">
            <div class="loading-spinner">
				<div class="loading-spinner-icon"></div>
				<div class="loading-text">Processing your spreadsheet...</div>
			</div>
        </div>
		
		<!-- Error Details Modal -->
		<div id="errorModal" style="display:none; position:fixed; top:0; left:0; width:100%; height:100%; background:rgba(0,0,0,0.5); z-index:10001;">
			<div style="position:absolute; top:50%; left:50%; transform:translate(-50%,-50%); background:white; border-radius:8px; padding:24px; max-width:500px; box-shadow:0 4px 20px rgba(0,0,0,0.2);">
				<h3 style="margin:0 0 16px 0; color:#dc3545;">Validation Errors</h3>
				<div id="errorModalContent" style="margin-bottom:20px; max-height:300px; overflow-y:auto;"></div>
				<button type="button" class="modern-button button-secondary" onclick="closeErrorModal()">Close</button>
			</div>
		</div>
        
        <!-- Hidden fields for data storage -->
        <asp:HiddenField ID="hdnUploadedData" runat="server" />
		<asp:HiddenField ID="hdnValidationResults" runat="server" />
        				
    </form>

	<script type="text/javascript">
		// Configuration
		const CONFIG = {
			REQUIRED_FIELDS: ['Company Code', 'Document Type', 'Currency', 'WBS Element', 'Cost Centre', 'Amount', 'Line Item Text'],
			DATE_FORMAT: 'yyyy-MM-dd',
			MESSAGES: {
				UPLOAD_SUCCESS: 'Successfully uploaded {0} rows',
				VALIDATION_SUCCESS: 'All rows validated successfully',
				VALIDATION_ERROR: 'Found {0} validation errors',
				DOWNLOAD_READY: 'Multi-journal template ready for download'
			}
		};

		// Initialize page
		window.onload = function() {
			// Check if we have data to display
			const uploadedData = document.getElementById('<%= hdnUploadedData.ClientID %>').value;
			if (uploadedData) {
				displayUploadedData();
			}
		};

		// Auto-upload spreadsheet
		function autoUploadSpreadsheet() {
			var fileInput = document.getElementById('<%= fuSpreadsheet.ClientID %>');
			if (fileInput.files && fileInput.files.length > 0) {
				showLoading();
				document.getElementById('<%= btnUploadSpreadsheet.ClientID %>').click();
			}
		}

		// Display uploaded data
		function displayUploadedData() {
			try {
				const dataJson = document.getElementById('<%= hdnUploadedData.ClientID %>').value;
				if (!dataJson) return;
				
				const data = JSON.parse(dataJson);
				if (!data || data.length === 0) return;
				
				// Show panels
				document.getElementById('summaryPanel').style.display = 'block';
				document.getElementById('dataPanel').style.display = 'block';
				
				// Update statistics
				updateStatistics(data);
				
				// Populate table
				populateDataTable(data);
				
				// Enable buttons
				document.getElementById('<%= btnValidate.ClientID %>').disabled = false;
				document.getElementById('<%= btnDownload.ClientID %>').disabled = false;
				
			} catch (error) {
				console.error('Error displaying data:', error);
				showMessage('Error displaying uploaded data: ' + error.message, true);
			}
		}

		// Update statistics
		function updateStatistics(data) {
			document.getElementById('statTotalRows').textContent = data.length;
			document.getElementById('statJournalCount').textContent = data.length;
			
			let totalAmount = 0;
			let validRows = 0;
			
			data.forEach(row => {
				if (row.Amount) {
					const amount = parseFloat(row.Amount.toString().replace(/,/g, ''));
					if (!isNaN(amount)) {
						totalAmount += amount;
					}
				}
				if (row.IsValid === true || row.IsValid === 'true') {
					validRows++;
				} else if (row.ValidationErrors) {
					try {
						const errors = typeof row.ValidationErrors === 'string' 
							? JSON.parse(row.ValidationErrors) 
							: row.ValidationErrors;
						if (!errors || errors.length === 0) {
							validRows++;
						}
					} catch (e) {
						// If we can't parse, assume invalid
					}
				}
			});
			
			document.getElementById('statTotalAmount').textContent = totalAmount.toLocaleString('en-AU', { 
				minimumFractionDigits: 2, 
				maximumFractionDigits: 2 
			});
			document.getElementById('statValidRows').textContent = validRows;
			
			// Update status badge
			const statusBadge = document.getElementById('uploadStatus');
			if (validRows === data.length) {
				statusBadge.textContent = 'Ready';
				statusBadge.className = 'status-badge success';
			} else {
				statusBadge.textContent = 'Has Errors';
				statusBadge.className = 'status-badge';
			}
		}

		// Populate data table
		function populateDataTable(data) {
			const tbody = document.getElementById('dataTableBody');
			tbody.innerHTML = '';
			
			data.forEach((row, index) => {
				const tr = document.createElement('tr');
				
				// Add validation class
				if (row.ValidationErrors) {
					try {
						const errors = typeof row.ValidationErrors === 'string' 
							? JSON.parse(row.ValidationErrors) 
							: row.ValidationErrors;
						if (errors && errors.length > 0) {
							tr.className = 'error-row';
						} else {
							tr.className = 'success-row';
						}
					} catch (e) {
						tr.className = 'error-row';
					}
				} else if (row.IsValid === true || row.IsValid === 'true') {
					tr.className = 'success-row';
				}
				
				// Build row HTML
				tr.innerHTML = `
					<td class="align-center">${index + 1}</td>
					<td class="align-center">${getStatusIcon(row, index)}</td>
					<td style="font-size:11px; color:#666;">${getValidationText(row)}</td>
					<td>${row['Company Code'] || ''}</td>
					<td>${row['Document Type'] || ''}</td>
					<td>${row['Document Date'] || ''}</td>
					<td>${row['Posting Date'] || ''}</td>
					<td>${row['Currency'] || ''}</td>
					<td>${row['Exchange Rate'] || ''}</td>
					<td>${row['Header Text'] || ''}</td>
					<td>${row['Reference'] || ''}</td>
					<td>${row['WBS Element'] || ''}</td>
					<td>${row['Cost Centre'] || ''}</td>
					<td>${row['GL Account'] || ''}</td>
					<td class="align-right">${formatAmount(row['Amount'])}</td>
					<td>${row['Tax Code'] || ''}</td>
					<td>${row['Line Item Text'] || ''}</td>
					<td>${row['Order Number'] || ''}</td>
					<td>${row['Assignment'] || ''}</td>
					<td>${row['Profit Center'] || ''}</td>
					<td>${row['Trading Partner'] || ''}</td>
					<td>${row['Segment'] || ''}</td>
				`;
				
				tbody.appendChild(tr);
			});
			
			document.getElementById('lblRowCount').textContent = data.length;
		}

		// Get status icon
		function getStatusIcon(row, rowIndex) {
			if (row.ValidationErrors) {
				try {
					const errors = typeof row.ValidationErrors === 'string' 
						? JSON.parse(row.ValidationErrors) 
						: row.ValidationErrors;
					if (errors && errors.length > 0) {
						return '<span style="color:#dc3545; cursor:pointer; text-decoration:underline;" title="Click for details" onclick="showRowErrors(\'' + 
							   encodeURIComponent(JSON.stringify(errors)) + '\', ' + (rowIndex + 1) + ')">X</span>';
					}
				} catch (e) {
					return '<span style="color:#dc3545; cursor:pointer; text-decoration:underline;" title="Click for details" onclick="showRowErrors(\'[&quot;Validation error&quot;]\', ' + (rowIndex + 1) + ')">X</span>';
				}
			}
			if (row.IsValid === true || row.IsValid === 'true') {
				return '<span style="color:#28a745;" title="Valid">OK</span>';
			}
			return '<span style="color:#ffc107;" title="Not validated">?</span>';
		}

		// Get validation text
		function getValidationText(row) {
			if (row.ValidationErrors) {
				try {
					const errors = typeof row.ValidationErrors === 'string' 
						? JSON.parse(row.ValidationErrors) 
						: row.ValidationErrors;
					if (errors && errors.length > 0) {
						return errors.join('; ');
					}
				} catch (e) {
					console.error('Error parsing validation errors:', e);
					return 'Validation error';
				}
			}
			if (row.IsValid === true || row.IsValid === 'true') {
				return 'Valid';
			}
			return 'Not validated';
		}

		// Format amount
		function formatAmount(value) {
			if (!value) return '';
			const num = parseFloat(value.toString().replace(/,/g, ''));
			if (isNaN(num)) return value;
			return num.toLocaleString('en-AU', { 
				minimumFractionDigits: 2, 
				maximumFractionDigits: 2 
			});
		}

		// Clear data
		function clearData() {
			if (confirm('Are you sure you want to clear all uploaded data?')) {
				document.getElementById('<%= hdnUploadedData.ClientID %>').value = '';
				document.getElementById('summaryPanel').style.display = 'none';
				document.getElementById('dataPanel').style.display = 'none';
				document.getElementById('<%= btnValidate.ClientID %>').disabled = true;
				document.getElementById('<%= btnDownload.ClientID %>').disabled = true;
				showMessage('Data cleared', false);
			}
		}

		// Show/hide loading
		function showLoading() {
			document.getElementById('loadingOverlay').style.display = 'block';
		}
		
		function hideLoading() {
			document.getElementById('loadingOverlay').style.display = 'none';
		}

		// Special handling for downloads
		function showLoadingForDownload() {
			const loadingText = document.querySelector('.loading-text');
			if (loadingText) {
				loadingText.textContent = 'Generating multi-journal template...';
			}
			showLoading();
			setTimeout(function() {
				hideLoading();
			}, 3000);
			return true;
		}

		// Show message
		function showMessage(message, isError, isWarning) {
			const panel = document.getElementById('<%= pnlStatus.ClientID %>');
			const label = document.getElementById('<%= lblStatus.ClientID %>');
			
			if (panel && label) {
				panel.style.animation = 'none';
				panel.offsetHeight;
				
				const displayMessage = message.replace(/<br\s*\/?>/gi, '\n');
				label.textContent = displayMessage;
				
				if (isWarning) {
					panel.className = 'status-message status-warning';
				} else {
					panel.className = isError ? 'status-message status-error' : 'status-message status-success';
				}
				
				panel.style.display = 'block';
				panel.style.animation = 'slideInDown 0.3s ease';
				
				setTimeout(() => {
					panel.style.animation = 'slideOutUp 0.3s ease';
					setTimeout(() => {
						panel.style.display = 'none';
					}, 300);
				}, 8000); // Increased from 5000 to 8000 (8 seconds)
			}
		}

		// Show row errors
		function showRowErrors(encodedErrors, rowNumber) {
			try {
				const errors = JSON.parse(decodeURIComponent(encodedErrors));
				const modal = document.getElementById('errorModal');
				const content = document.getElementById('errorModalContent');
				
				// Update modal title with row number
				modal.querySelector('h3').textContent = `Validation Errors - Row ${rowNumber}`;
				
				// Build error list
				let html = '<ul style="margin:0; padding-left:20px;">';
				errors.forEach(error => {
					html += `<li style="margin:8px 0; color:#666;">${error}</li>`;
				});
				html += '</ul>';
				
				content.innerHTML = html;
				modal.style.display = 'block';
			} catch (e) {
				console.error('Error displaying validation details:', e);
				alert('Error displaying validation details');
			}
		}
		
		// Close error modal
		function closeErrorModal() {
			document.getElementById('errorModal').style.display = 'none';
		}

		// Close toast
		function closeToast() {
			const panel = document.getElementById('<%= pnlStatus.ClientID %>');
			if (panel) {
				panel.style.animation = 'slideOutUp 0.3s ease';
				setTimeout(() => {
					panel.style.display = 'none';
				}, 300);
			}
		}
	</script>	
    
</body>
</html>

<script runat="server">
	// Helper class to store journal data
	public class JournalData
	{
		public Dictionary<string, string> HeaderData { get; set; }
		public List<Dictionary<string, string>> LineItems { get; set; }
		public List<string> ValidationErrors { get; set; }
		public bool IsValid { get; set; }
		
		public JournalData()
		{
			HeaderData = new Dictionary<string, string>();
			LineItems = new List<Dictionary<string, string>>();
			ValidationErrors = new List<string>();
			IsValid = true;
		}
	}

    protected void Page_Error(object sender, EventArgs e)
    {
        Exception ex = Server.GetLastError();
        if (ex != null)
        {
            Response.Clear();
            Response.Write("<h2>Error Details:</h2>");
            Response.Write("<pre style='background:#fee;padding:10px;border:1px solid #c00;'>");
            Response.Write("Message: " + HttpUtility.HtmlEncode(ex.Message) + "\n\n");
            Response.Write("Stack Trace: " + HttpUtility.HtmlEncode(ex.StackTrace));
            if (ex.InnerException != null)
            {
                Response.Write("\n\nInner Exception: " + HttpUtility.HtmlEncode(ex.InnerException.Message));
            }
            Response.Write("</pre>");
            Server.ClearError();
        }
    }
    
	protected void Page_Load(object sender, EventArgs e)
	{
		try
		{
			Response.Cache.SetCacheability(HttpCacheability.NoCache);
			Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
			Response.Cache.SetNoStore();
			Response.AppendHeader("Pragma", "no-cache");
			
			if (!IsPostBack)
			{
				// Clear any previous data
				hdnUploadedData.Value = "";
				hdnValidationResults.Value = "";
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Error initializing page: " + ex.Message, true);
		}
	}

	protected void UploadSpreadsheet_Click(object sender, EventArgs e)
	{
		if (!fuSpreadsheet.HasFile)
		{
			ShowMessage("Please select a spreadsheet file to upload.", true);
			return;
		}

		try
		{
			using (var pkg = new ExcelPackage(fuSpreadsheet.FileContent))
			{
				var ws = pkg.Workbook.Worksheets.FirstOrDefault();
				if (ws == null)
				{
					ShowMessage("Error: No worksheet found in the uploaded file.", true);
					return;
				}

				// Build column map from row 1
				var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				int maxCol = 50;
				if (ws.Dimension != null && ws.Dimension.End != null)
				{
					maxCol = ws.Dimension.End.Column;
				}
				
				for (int col = 1; col <= maxCol; col++)
				{
					string header = ws.Cells[1, col].Text.Trim();
					if (!string.IsNullOrEmpty(header))
					{
						// Store original header name
						columnMap[header] = col;
						
						// Also store common variations
						if (header.Contains("WBS")) columnMap["WBS Element"] = col;
						if (header.Contains("Cost Cent")) columnMap["Cost Centre"] = col;
						if (header.Contains("Company")) columnMap["Company Code"] = col;
						if (header.Contains("Doc") && header.Contains("Type")) columnMap["Document Type"] = col;
						if (header.Contains("Group") && header.Contains("ID")) columnMap["Group ID"] = col;
					}
				}

				// Process rows starting from row 2
				var allRows = new List<Dictionary<string, string>>();
				int currentRow = 2;
				int maxRow = 1000;
				if (ws.Dimension != null && ws.Dimension.End != null)
				{
					maxRow = ws.Dimension.End.Row;
				}

				// Get default values from first data row if they exist
				string defaultCompanyCode = GetCellValue(ws, 2, columnMap, "Company Code");
				string defaultDocType = GetCellValue(ws, 2, columnMap, "Document Type") ?? "SA"; // Default to SA
				string defaultCurrency = GetCellValue(ws, 2, columnMap, "Currency");
				string defaultGroupId = GetCellValue(ws, 2, columnMap, "Group ID");
				string defaultDocumentDate = GetCellDate(ws, 2, columnMap, "Document Date");
				string defaultPostingDate = GetCellDate(ws, 2, columnMap, "Posting Date");

				while (currentRow <= maxRow)
				{
					// Check if row has any data
					bool hasData = false;
					for (int col = 1; col <= maxCol && !hasData; col++)
					{
						if (!string.IsNullOrWhiteSpace(ws.Cells[currentRow, col].Text))
						{
							hasData = true;
						}
					}
					
					if (!hasData)
					{
						currentRow++;
						continue;
					}

					// Create row data
					var rowData = new Dictionary<string, string>();
					
					// Get values with defaults
					rowData["Company Code"] = GetCellValue(ws, currentRow, columnMap, "Company Code") ?? defaultCompanyCode;
					rowData["Document Type"] = GetCellValue(ws, currentRow, columnMap, "Document Type", "Doc Type") ?? defaultDocType ?? "SA"; // Default to SA
					rowData["Currency"] = GetCellValue(ws, currentRow, columnMap, "Currency") ?? defaultCurrency;
					rowData["Group ID"] = GetCellValue(ws, currentRow, columnMap, "Group ID", "Group") ?? defaultGroupId;
					rowData["Document Date"] = GetCellDate(ws, currentRow, columnMap, "Document Date", "Doc Date") ?? defaultDocumentDate;
					rowData["Posting Date"] = GetCellDate(ws, currentRow, columnMap, "Posting Date", "Post Date") ?? defaultPostingDate;
					
					// Line specific data
					rowData["WBS Element"] = GetCellValue(ws, currentRow, columnMap, "WBS Element", "WBS", "Project");
					rowData["Cost Centre"] = GetCellValue(ws, currentRow, columnMap, "Cost Centre", "Cost Center", "CC");
					rowData["GL Account"] = GetCellValue(ws, currentRow, columnMap, "GL Account", "G/L Account", "GL", "Account");
					rowData["Amount"] = CleanAmount(GetCellValue(ws, currentRow, columnMap, "Amount", "Value", "Debit", "Credit"));
					rowData["Line Item Text"] = GetCellValue(ws, currentRow, columnMap, "Line Item Text", "Text", "Description");
					
					// Optional fields
					rowData["Exchange Rate"] = GetCellValue(ws, currentRow, columnMap, "Exchange Rate", "Exch Rate") ?? "1.00000";
					rowData["Header Text"] = GetCellValue(ws, currentRow, columnMap, "Header Text", "Doc Header Text", "Document Header Text");
					rowData["Tax Code"] = GetCellValue(ws, currentRow, columnMap, "Tax Code", "Tax");
					rowData["Order Number"] = GetCellValue(ws, currentRow, columnMap, "Order Number", "Order", "Internal Order");
					rowData["Assignment"] = GetCellValue(ws, currentRow, columnMap, "Assignment", "Reference");
					rowData["Reference"] = GetCellValue(ws, currentRow, columnMap, "Reference", "Journal Reference", "Ref");
					rowData["Profit Center"] = GetCellValue(ws, currentRow, columnMap, "Profit Center", "PC");
					rowData["Trading Partner"] = GetCellValue(ws, currentRow, columnMap, "Trading Partner", "TP");
					rowData["Segment"] = GetCellValue(ws, currentRow, columnMap, "Segment");
					
					// Validate row
					var errors = ValidateRow(rowData, currentRow);
					if (errors.Count > 0)
					{
						rowData["ValidationErrors"] = Newtonsoft.Json.JsonConvert.SerializeObject(errors);
						rowData["IsValid"] = "false";
					}
					else
					{
						rowData["ValidationErrors"] = "[]";
						rowData["IsValid"] = "true";
					}
					
					allRows.Add(rowData);
					currentRow++;
				}

				if (allRows.Count == 0)
				{
					ShowMessage("No data rows found in the spreadsheet.", true);
					return;
				}

				// Store uploaded data
				hdnUploadedData.Value = Newtonsoft.Json.JsonConvert.SerializeObject(allRows);
				
				// Count validation results
				int validRows = allRows.Count(r => r["IsValid"] == "True");
				int errorRows = allRows.Count - validRows;
				
				// Update UI
				ScriptManager.RegisterStartupScript(
					this,
					GetType(),
					"displayData",
					"setTimeout(function() { displayUploadedData(); }, 100);",
					true);

				// Show summary message
				string message = string.Format("Successfully loaded {0} rows from spreadsheet.", allRows.Count);
				if (errorRows > 0)
				{
					message += string.Format("\n{0} rows have validation errors.", errorRows);
					ShowMessage(message, false, true); // Warning
				}
				else
				{
					ShowMessage(message, false);
				}
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Error processing spreadsheet: " + ex.Message, true);
		}
	}

	// Helper to get cell value by multiple possible headers
	private string GetCellValue(ExcelWorksheet ws, int row, Dictionary<string, int> columnMap, params string[] possibleHeaders)
	{
		foreach (string header in possibleHeaders)
		{
			if (columnMap.ContainsKey(header))
			{
				return ws.Cells[row, columnMap[header]].Text.Trim();
			}
		}
		return null;
	}

	// Helper to get date value
	private string GetCellDate(ExcelWorksheet ws, int row, Dictionary<string, int> columnMap, params string[] possibleHeaders)
	{
		foreach (string header in possibleHeaders)
		{
			if (columnMap.ContainsKey(header))
			{
				return ParseDate(ws.Cells[row, columnMap[header]]);
			}
		}
		return null;
	}

	// Parse date from Excel cell
	private string ParseDate(ExcelRangeBase cell)
	{
		if (cell == null) return "";
		
		if (cell.Value != null)
		{
			if (cell.Value is DateTime)
			{
				return ((DateTime)cell.Value).ToString("yyyy-MM-dd");
			}
			
			double numValue;
			if (double.TryParse(cell.Value.ToString(), out numValue))
			{
				if (numValue > 25569 && numValue < 2958466)
				{
					try
					{
						return DateTime.FromOADate(numValue).ToString("yyyy-MM-dd");
					}
					catch { }
				}
			}
		}
		
		string text = cell.Text.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			DateTime dt;
			if (DateTime.TryParse(text, out dt))
			{
				return dt.ToString("yyyy-MM-dd");
			}
		}
		
		return "";
	}

	// Clean amount value - remove currency symbols, commas, etc.
	private string CleanAmount(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "";
		
		// Remove common currency symbols and formatting
		value = value.Trim();
		value = System.Text.RegularExpressions.Regex.Replace(value, @"[$£€¥?¢]", ""); // Remove currency symbols
		value = value.Replace(",", ""); // Remove thousands separators
		value = value.Replace(" ", ""); // Remove spaces
		
		// Handle parentheses for negative numbers
		if (value.StartsWith("(") && value.EndsWith(")"))
		{
			value = "-" + value.Substring(1, value.Length - 2);
		}
		
		// Try to parse to ensure it's a valid number
		decimal amount;
		if (decimal.TryParse(value, out amount))
		{
			// Return clean string representation
			return amount.ToString();
		}
		
		return value; // Return original if can't parse
	}

	// Validate a row
	private List<string> ValidateRow(Dictionary<string, string> row, int rowNumber)
	{
		var errors = new List<string>();
		
		// Check required fields
		if (string.IsNullOrWhiteSpace(row["Company Code"]))
			errors.Add("Company Code is required");
		if (string.IsNullOrWhiteSpace(row["Document Type"]))
			errors.Add("Document Type is required");
		if (string.IsNullOrWhiteSpace(row["Currency"]))
			errors.Add("Currency is required");
		if (string.IsNullOrWhiteSpace(row["WBS Element"]))
			errors.Add("WBS Element is required");
		if (string.IsNullOrWhiteSpace(row["Cost Centre"]))
			errors.Add("Cost Centre is required");
		if (string.IsNullOrWhiteSpace(row["Amount"]))
			errors.Add("Amount is required");
		if (string.IsNullOrWhiteSpace(row["Line Item Text"]))
			errors.Add("Line Item Text is required");
		
		// Validate amount is numeric
		if (!string.IsNullOrWhiteSpace(row["Amount"]))
		{
			decimal amount;
			// Amount should already be cleaned, but double-check
			string cleanAmount = row["Amount"].Replace(",", "").Replace("$", "").Trim();
			if (!decimal.TryParse(cleanAmount, out amount))
			{
				errors.Add("Amount must be numeric");
			}
			else if (amount == 0)
			{
				errors.Add("Amount cannot be zero");
			}
		}
		
		// Validate dates if provided
		if (!string.IsNullOrWhiteSpace(row["Document Date"]))
		{
			DateTime dt;
			if (!DateTime.TryParse(row["Document Date"], out dt))
				errors.Add("Invalid Document Date format");
		}
		
		if (!string.IsNullOrWhiteSpace(row["Posting Date"]))
		{
			DateTime dt;
			if (!DateTime.TryParse(row["Posting Date"], out dt))
				errors.Add("Invalid Posting Date format");
		}
		
		return errors;
	}

	protected void GenerateMultiJournal_Click(object sender, EventArgs e)
	{
		try
		{
			string dataJson = hdnUploadedData.Value;
			if (string.IsNullOrEmpty(dataJson))
			{
				ShowMessage("No data to generate. Please upload a spreadsheet first.", true);
				return;
			}
			
			var uploadedRows = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(dataJson);
			if (uploadedRows == null || uploadedRows.Count == 0)
			{
				ShowMessage("No valid data found.", true);
				return;
			}
			
			// Filter valid rows only
			var validRows = uploadedRows.Where(r => r["IsValid"] == "True" || r["IsValid"] == "true").ToList();
			if (validRows.Count == 0)
			{
				ShowMessage("No valid rows found. Please fix validation errors first.", true);
				return;
			}
			
			// Load template
			string templatePath = Server.MapPath("~/JournalEntry_Template.xlsx");
			if (!System.IO.File.Exists(templatePath))
			{
				ShowMessage("Template file not found. Please ensure JournalEntry_Template.xlsx is in the application folder.", true);
				return;
			}
			
			var templateFile = new FileInfo(templatePath);
			using (var pkg = new ExcelPackage(templateFile))
			{
				var ws = pkg.Workbook.Worksheets[1];
				
				// Process each row as a separate journal
				int journalCount = 0;
				int currentRow = 11;  // Start position for first journal header data
				
				foreach (var row in validRows)
				{
					journalCount++;
					
					// If not the first journal, add blank row and new sections
					if (journalCount > 1)
					{
						// Insert blank row
						ws.InsertRow(currentRow, 1);
						currentRow++;
						
						// Insert rows for new journal structure (10 rows total)
						// 1 - Header line
						// 2 - Technical names line  
						// 3 - Column names line
						// 4 - Header data line
						// 5 - Blank line
						// 6 - Line Items line
						// 7 - Transaction Currency line
						// 8 - Line items technical names
						// 9 - Line items column names
						// 10,11 - Two data lines
						ws.InsertRow(currentRow, 11);
						
						// Copy formats from original journal
						// Header section
						ws.Cells[8, 1, 8, 23].Copy(ws.Cells[currentRow, 1]); // Header line
						ws.Cells[9, 1, 9, 23].Copy(ws.Cells[currentRow + 1, 1]); // Technical names
						ws.Cells[10, 1, 10, 23].Copy(ws.Cells[currentRow + 2, 1]); // Column names
						
						// Update Header line with journal number
						ws.Cells[currentRow, 1].Value = journalCount;
						ws.Cells[currentRow, 2].Value = "Header";
						
						// Line items section (5 rows down from Header line)
						ws.Cells[13, 1, 13, 23].Copy(ws.Cells[currentRow + 5, 1]); // Line Items line
						ws.Cells[14, 1, 14, 23].Copy(ws.Cells[currentRow + 6, 1]); // Transaction Currency
						ws.Cells[15, 1, 15, 23].Copy(ws.Cells[currentRow + 7, 1]); // Technical names
						ws.Cells[16, 1, 16, 23].Copy(ws.Cells[currentRow + 8, 1]); // Column names
						
						// Update Line Items line (no journal number for Line Items)
						ws.Cells[currentRow + 5, 1].Value = "";  // Keep column A empty
						ws.Cells[currentRow + 5, 2].Value = "Line Items";
						
						// Position for header data
						currentRow = currentRow + 3;
					}
					
					// Get dates with defaults
					DateTime docDate = DateTime.Today;
					DateTime postDate = DateTime.Today;
					
					if (!string.IsNullOrWhiteSpace(row["Document Date"]))
						DateTime.TryParse(row["Document Date"], out docDate);
					if (!string.IsNullOrWhiteSpace(row["Posting Date"]))
						DateTime.TryParse(row["Posting Date"], out postDate);
					
					// Calculate fiscal period
					int fiscalPeriod = GetAustralianFiscalPeriod(postDate);
					
					// Use reference from spreadsheet
					string reference = row["Reference"] ?? "";
					string headerText = row["Header Text"] ?? "WBS to CC Transfer";
					
					// Write header data (row 11 for first journal, calculated position for others)
					ws.Cells[currentRow, 2].Value = row["Company Code"];
					ws.Cells[currentRow, 3].Value = row["Document Type"];
					ws.Cells[currentRow, 4].Value = docDate;
					ws.Cells[currentRow, 4].Style.Numberformat.Format = "dd/MM/yyyy";
					ws.Cells[currentRow, 5].Value = postDate;
					ws.Cells[currentRow, 5].Style.Numberformat.Format = "dd/MM/yyyy";
					ws.Cells[currentRow, 6].Value = fiscalPeriod.ToString().PadLeft(2, '0');
					ws.Cells[currentRow, 7].Value = headerText; // BKTXT - Document Header Text
					ws.Cells[currentRow, 8].Value = row["Currency"];
					ws.Cells[currentRow, 10].Value = row["Exchange Rate"];
					ws.Cells[currentRow, 12].Value = reference;
					
					// Parse amount
					decimal amount = 0;
					if (!string.IsNullOrWhiteSpace(row["Amount"]))
					{
						decimal.TryParse(row["Amount"].Replace(",", ""), out amount);
					}
					
					// Line items data position
					int lineRow = (journalCount == 1) ? 17 : currentRow + 6;
					
					// Line 1: Debit WBS
					ws.Cells[lineRow, 2].Value = row["Company Code"];
					ws.Cells[lineRow, 3].Value = row["GL Account"] ?? ""; // GL Account from spreadsheet
					ws.Cells[lineRow, 4].Value = row["Line Item Text"];
					ws.Cells[lineRow, 5].Value = amount; // Debit
					ws.Cells[lineRow, 6].Value = ""; // Credit empty
					ws.Cells[lineRow, 9].Value = row["Tax Code"];
					ws.Cells[lineRow, 11].Value = ""; // Cost Centre empty for WBS line
					ws.Cells[lineRow, 12].Value = row["Profit Center"];
					ws.Cells[lineRow, 13].Value = row["Order Number"];
					ws.Cells[lineRow, 14].Value = row["WBS Element"];
					ws.Cells[lineRow, 18].Value = row["Assignment"];
					ws.Cells[lineRow, 19].Value = row["Trading Partner"];
					ws.Cells[lineRow, 20].Value = row["Segment"];
					
					// Line 2: Credit Cost Centre
					lineRow++;
					ws.Cells[lineRow, 2].Value = row["Company Code"];
					ws.Cells[lineRow, 3].Value = row["GL Account"] ?? ""; // GL Account from spreadsheet
					ws.Cells[lineRow, 4].Value = row["Line Item Text"];
					ws.Cells[lineRow, 5].Value = ""; // Debit empty
					ws.Cells[lineRow, 6].Value = amount; // Credit
					ws.Cells[lineRow, 9].Value = row["Tax Code"];
					ws.Cells[lineRow, 11].Value = row["Cost Centre"];
					ws.Cells[lineRow, 12].Value = row["Profit Center"];
					ws.Cells[lineRow, 13].Value = row["Order Number"];
					ws.Cells[lineRow, 14].Value = ""; // WBS empty for CC line
					ws.Cells[lineRow, 18].Value = row["Assignment"];
					ws.Cells[lineRow, 19].Value = row["Trading Partner"];
					ws.Cells[lineRow, 20].Value = row["Segment"];
					
					// Update position for next journal
					if (journalCount > 1)
					{
						currentRow = lineRow + 1; // Position after last line item
					}
					else
					{
						currentRow = 19; // After first journal's line items
					}
				}
				
				// Clean up any extra rows at the end
				if (ws.Dimension != null && ws.Dimension.End.Row > currentRow)
				{
					ws.DeleteRow(currentRow, ws.Dimension.End.Row - currentRow + 1);
				}
				
				// Save and download
				var memStream = new MemoryStream();
				pkg.SaveAs(memStream);
				memStream.Position = 0;
				
				Response.Clear();
				Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
				Response.AddHeader("content-disposition", 
					string.Format("attachment;filename=MultiJournal_WBS_CC_{0}.xlsx", 
						DateTime.Now.ToString("yyyyMMddHHmmss")));
				Response.BinaryWrite(memStream.ToArray());
				Response.End();
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Error generating multi-journal template: " + ex.Message, true);
		}
	}

	// Get Australian fiscal period
	private int GetAustralianFiscalPeriod(DateTime date)
	{
		int month = date.Month;
		if (month >= 7)  // July to December
			return month - 6;
		else  // January to June
			return month + 6;
	}
	
	// Show message with optional warning state
	private void ShowMessage(string message, bool isError, bool isWarning = false)
	{
		pnlStatus.Visible = true;
		lblStatus.Text = message;
		
		if (isWarning)
		{
			pnlStatus.CssClass = "status-message status-warning";
		}
		else
		{
			pnlStatus.CssClass = isError ? "status-message status-error" : "status-message status-success";
		}
		
		pnlStatus.Style["display"] = "block";
		
		string script = @"
			setTimeout(function() {
				var panel = document.getElementById('" + pnlStatus.ClientID + @"');
				if (panel) {
					panel.style.animation = 'slideOutUp 0.3s ease';
					setTimeout(function() {
						panel.style.display = 'none';
					}, 300);
				}
			}, 8000);"; // Increased from 5000 to 8000 (8 seconds)
		
		ScriptManager.RegisterStartupScript(
			this, 
			this.GetType(), 
			"AutoHideMessage" + DateTime.Now.Ticks,
			script, 
			true
		);
	}
</script>