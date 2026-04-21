<%@ Page Language="C#" AutoEventWireup="true" Debug="true" CodeFile="eJET.aspx.cs" Inherits="eJET"%>


<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>eJET - ERP Journal Assist</title>
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
	<link rel="stylesheet" href="css/ejet.css" />	
    
	<style type="text/css">
		

	</style>	
   
</head>
<body>
    <form id="form1" runat="server">
		<asp:ScriptManager ID="ScriptManager1" runat="server" EnablePageMethods="true" />
	
        <!-- Page Header -->
		<div class="app-header">
			<div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
				<div>
					<h1>eJET</h1>
					<p class="app-subtitle">ERP Journal Assist</p>
				</div>
				<button type="button" class="header-support-button" onclick="showSupportOptions()">
					<span>✉️</span> Feedback & Support
				</button>
			</div>
		</div>
        
        <!-- Main Content -->
        <div class="content-wrapper">
            <!-- Status Message -->
            <asp:Panel ID="pnlStatus" runat="server" CssClass="status-message" Style="display:none;">
                <asp:Label ID="lblStatus" runat="server"></asp:Label>
				<span class="toast-close" onclick="closeToast()">×</span>
            </asp:Panel>
            
            <!-- Journal Header -->
            <div class="modern-panel">

                <div class="panel-header">
					<div style="display: flex; align-items: center; gap: 32px;">
						<div class="journal-reference">
							<span class="reference-label">eJET Reference</span>
							<span class="reference-value">
								<asp:Label ID="lblEJetReference" runat="server" Text="-"></asp:Label>
							</span>
						</div>
						<span class="status-badge">Draft</span>
					</div>
                    <div class="totals-section">
                        <div class="total-item">
                            <span class="total-label">Total Debit:</span>
                            <span class="total-value">
                                <asp:Label ID="lblTotalDebit" runat="server" Text="0.00"></asp:Label>
                            </span>
                            <span class="total-label">AUD</span>
                        </div>
                        <div class="total-item">
                            <span class="total-label">Total Credit:</span>
                            <span class="total-value">
                                <asp:Label ID="lblTotalCredit" runat="server" Text="0.00"></asp:Label>
                            </span>
                            <span class="total-label">AUD</span>
                        </div>
                    </div>
                </div>			
                
				<!-- Navigation Tabs -->
				<div class="modern-tabs">
					<button type="button" id="instructionsTabBtn" class="modern-tab" onclick="switchTab(event, 'instructions')">						
						<span>§</span> Instructions
					</button>
					<button type="button" id="detailsTabBtn" class="modern-tab active" onclick="switchTab(event, 'details')">
						<span>⚈</span> Header Details
					</button>
					<button type="button" id="lineItemsTabBtn" class="modern-tab" onclick="switchTab(event, 'lineItems')">
						<span>≡</span> Line Items
					</button>
				</div>
                
                <!-- Tab Content -->
                <div class="panel-content">
					<!-- Instructions Tab -->
					<div id="instructionsTab" class="tab-content">
						<div style="max-width: 1400px; margin: 0 auto;">
							<!-- Header Introduction -->
							<div style="background: linear-gradient(135deg, #f8f9fa 0%, #e9ecef 100%); padding: 20px 24px; border-radius: 8px; margin-bottom: 24px;">
								<h3 style="color: #3a4650; margin-bottom: 10px;">Welcome to eJET - Enhanced Journal Entry Tool</h3>
								<p style="color: #6c757d; font-size: 15px; line-height: 1.6; margin: 0;">
									eJET assists you in preparing journal templates that can be uploaded to SAP S/4HANA. It streamlines the process of creating and validating your journal entries. 
									Once you download the template, use the appropriate SAP upload function (Fiori app "Upload General Journal Entries") to complete the process.
								</p>
							</div>
							
							<!-- Quick Start and Tips Grid -->
							<div style="display: grid; grid-template-columns: 1fr 2fr; gap: 24px; margin-bottom: 24px;">
								<!-- Quick Start Guide -->
								<div style="background: #fff5e6; padding: 20px; border-radius: 8px; border: 1px solid #ffd6a5;">
									<h4 style="color: #cf4520; margin-bottom: 15px;">📋 Quick Start Guide</h4>
									<ol style="line-height: 1.8; color: #495057; margin: 0; padding-left: 20px; font-size: 14px;">
										<li><strong>Fill Header Details:</strong><br/>
											Select document type, company code, dates, and group ID in Details tab</li>
										<li><strong>Add Line Items:</strong><br/>
											Switch to Line Items tab and enter journal lines</li>
										<li><strong>Validate:</strong><br/>
											Click Validate button to check for errors</li>
										<li><strong>Download:</strong><br/>
											Select "Download to ERP Template" for SAP upload file</li>
									</ol>
								</div>
								
								<!-- Working with Line Items -->
								<div style="background: white; padding: 20px; border-radius: 8px; border: 1px solid #e9ecef;">
									<h4 style="color: #5a6770; margin-bottom: 15px;">📊 Working with Line Items</h4>
									<div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px;">
										<div>
											<h5 style="color: #cf4520; font-size: 14px; margin-bottom: 8px;">Essential Operations</h5>
											<ul style="line-height: 1.6; color: #6c757d; padding-left: 20px; margin: 0; font-size: 13px;">
												<li>Use <strong>Add Row</strong> to insert new lines</li>
												<li>GL descriptions fill automatically</li>
												<li>Navigate with <kbd>Tab</kbd> or <kbd>Enter</kbd></li>
												<li>Enter either Debit OR Credit (not both)</li>
												<li><strong>Line Item Text is mandatory</strong></li>
											</ul>
										</div>
										<div>
											<h5 style="color: #cf4520; font-size: 14px; margin-bottom: 8px;">Excel Integration</h5>
											<ul style="line-height: 1.6; color: #6c757d; padding-left: 20px; margin: 0; font-size: 13px;">
												<li>Copy cells in Excel (<kbd>Ctrl+C</kbd>)</li>
												<li>Click target cell in eJET</li>
												<li>Paste with <kbd>Ctrl+V</kbd></li>
												<li>New rows added automatically</li>
												<li>Use "Copy to Excel" for export</li>
											</ul>
										</div>
									</div>
								</div>
							</div>

							<!-- Three Column Grid for Key Features -->
							<div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; margin-bottom: 24px;">
								<!-- Uploading Templates -->
								<div style="background: white; padding: 18px; border-radius: 8px; border: 1px solid #e9ecef;">
									<h5 style="color: #5a6770; margin-bottom: 12px;">📤 Uploading Templates</h5>
									<div style="font-size: 13px; color: #6c757d; line-height: 1.6;">
										<p style="margin: 0 0 8px 0;"><strong>ERP Template:</strong> For SAP-exported files (JournalEntry_Template.xlsx)</p>
										<p style="margin: 0;"><strong>Spreadsheet:</strong> For custom Excel with 'Header' and 'Line Items' worksheets</p>
										<div style="background: #f8f9fa; padding: 8px; border-radius: 4px; margin-top: 10px;">
											<strong>⚠️ Note:</strong> Max 999 lines per journal (auto-split if exceeded)
										</div>
									</div>
								</div>

								<!-- Validation Rules -->
								<div style="background: white; padding: 18px; border-radius: 8px; border: 1px solid #e9ecef;">
									<h5 style="color: #5a6770; margin-bottom: 12px;">✓ Validation Rules</h5>
									<ul style="line-height: 1.6; color: #6c757d; padding-left: 20px; margin: 0; font-size: 13px;">
										<li>All required fields completed</li>
										<li>Debits = Credits (must balance)</li>
										<li>Valid GL accounts & cost centres</li>
										<li>Tax codes match GL requirements</li>
										<li>Exchange rate for non-AUD</li>
									</ul>
								</div>

								<!-- Additional Features -->
								<div style="background: white; padding: 18px; border-radius: 8px; border: 1px solid #e9ecef;">
									<h5 style="color: #5a6770; margin-bottom: 12px;">⚡ Power Features</h5>
									<ul style="line-height: 1.6; color: #6c757d; padding-left: 20px; margin: 0; font-size: 13px;">
										<li><strong>Column Resize:</strong> Drag borders</li>
										<li><strong>Line Details:</strong> Click line #</li>
										<li><strong>Duplicate:</strong> Copy any line</li>
										<li><strong>Multi-select:</strong> Shift+Arrow</li>
										<li><strong>Auto-lookup:</strong> All codes</li>
									</ul>
								</div>
							</div>

							<!-- Keyboard Shortcuts -->
							<div style="background: #f8f9fa; padding: 20px; border-radius: 8px;">
								<h4 style="color: #5a6770; margin-bottom: 16px;">⌨️ Keyboard Shortcuts</h4>
								<div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 20px;">
									<div>
										<h6 style="color: #cf4520; margin-bottom: 8px; font-size: 14px;">Navigation</h6>
										<div style="font-size: 13px; color: #6c757d; line-height: 1.8;">
											<div><kbd>Tab</kbd> Next field</div>
											<div><kbd>Shift+Tab</kbd> Previous field</div>
											<div><kbd>Enter</kbd> Next field</div>
										</div>
									</div>
									<div>
										<h6 style="color: #cf4520; margin-bottom: 8px; font-size: 14px;">Copy & Paste</h6>
										<div style="font-size: 13px; color: #6c757d; line-height: 1.8;">
											<div><kbd>Ctrl+C</kbd> Copy cells</div>
											<div><kbd>Ctrl+V</kbd> Paste from Excel</div>
											<div><kbd>Ctrl+A</kbd> Select all</div>
										</div>
									</div>
									<div>
										<h6 style="color: #cf4520; margin-bottom: 8px; font-size: 14px;">Selection</h6>
										<div style="font-size: 13px; color: #6c757d; line-height: 1.8;">
											<div><kbd>Click+Drag</kbd> Multi-select</div>
											<div><kbd>Shift+Click</kbd> Range select</div>
											<div><kbd>Shift+Arrow</kbd> Extend</div>
										</div>
									</div>
									<div>
										<h6 style="color: #cf4520; margin-bottom: 8px; font-size: 14px;">Quick Actions</h6>
										<div style="font-size: 13px; color: #6c757d; line-height: 1.8;">
											<div><kbd>Delete</kbd> Clear cell</div>
											<div><kbd>Esc</kbd> Cancel edit</div>
											<div><kbd>F2</kbd> Edit cell</div>
										</div>
									</div>
								</div>
							</div>

							<!-- Need Help Section -->
							<div style="margin-top: 24px; text-align: center; padding: 20px; background: white; border-radius: 8px; border: 1px solid #e9ecef;">
								<h5 style="color: #5a6770; margin-bottom: 12px;">Need More Help?</h5>
								<p style="color: #6c757d; font-size: 14px; margin-bottom: 16px;">
									For further assistance, bug reports, or feature requests, click the <strong>"Feedback & Support"</strong> button in the header.
								</p>
								<button type="button" class="modern-button button-secondary" onclick="showSupportOptions()">
									<span>✉️</span> Contact Support
								</button>
							</div>
						</div>
					</div>		
				
                    <!-- Details Tab -->
                    <div id="detailsTab" class="tab-content active">
                        
                        <!-- First Row -->
                        <div class="form-grid">
                            <div class="form-group">
                                <label class="form-label required">Document Type</label>
                                <asp:DropDownList ID="ddlDocumentType" runat="server" CssClass="asp-dropdown">
                                    <asp:ListItem Value="AD">AD - Accrual Deferral Doc</asp:ListItem>
                                    <asp:ListItem Value="SA" Selected="True">SA - G/L Account Document</asp:ListItem>
                                </asp:DropDownList>
                            </div>
                            
                            <div class="form-group">
                                <label class="form-label required">Company Code</label>                                
								<asp:DropDownList ID="ddlCompanyCode" runat="server" CssClass="asp-dropdown">
									<asp:ListItem Value="1000" Selected="True">1000 - Defence</asp:ListItem>
									<asp:ListItem Value="3000">3000 - ASA</asp:ListItem>
									<asp:ListItem Value="5000">5000 - ASD</asp:ListItem>
								</asp:DropDownList>
                            </div>
                            
                            <div class="form-group">
                                <label class="form-label required">Currency</label>
								<asp:DropDownList ID="ddlCurrency" runat="server" CssClass="asp-dropdown" onchange="updateCurrencyHeaders(); updateExchangeRate();">
								</asp:DropDownList>
                            </div>
                            
                            <div class="form-group">
                                <label class="form-label">Exchange Rate</label>
                                <asp:TextBox ID="txtExchangeRate" runat="server" CssClass="asp-textbox" Text="1.00000"></asp:TextBox>
                            </div>
                        </div>
                        
                        <!-- Second Row -->
                        <div class="form-grid">
                            <div class="form-group">
                                <label class="form-label required">Document Date</label>
                                <asp:TextBox ID="txtDocumentDate" runat="server" CssClass="asp-textbox" TextMode="Date" EnableViewState="true"></asp:TextBox>
                            </div>
                            
                            <div class="form-group">
                                <label class="form-label required">Posting Date</label>                                
								<asp:TextBox ID="txtPostingDate" runat="server" CssClass="asp-textbox" TextMode="Date" onchange="updatePostingPeriod()"></asp:TextBox>
                            </div>
                            
                            <div class="form-group">
                                <label class="form-label required">Posting Period</label>
                                <asp:DropDownList ID="ddlPostingPeriod" runat="server" CssClass="asp-dropdown">
                                    <asp:ListItem Value="">Select Period</asp:ListItem>
                                    <asp:ListItem Value="01">Period 01</asp:ListItem>
                                    <asp:ListItem Value="02">Period 02</asp:ListItem>
                                    <asp:ListItem Value="03">Period 03</asp:ListItem>
                                    <asp:ListItem Value="04">Period 04</asp:ListItem>
                                    <asp:ListItem Value="05">Period 05</asp:ListItem>
                                    <asp:ListItem Value="06">Period 06</asp:ListItem>
                                    <asp:ListItem Value="07">Period 07</asp:ListItem>
                                    <asp:ListItem Value="08">Period 08</asp:ListItem>
                                    <asp:ListItem Value="09">Period 09</asp:ListItem>
                                    <asp:ListItem Value="10">Period 10</asp:ListItem>
                                    <asp:ListItem Value="11">Period 11</asp:ListItem>
                                    <asp:ListItem Value="12">Period 12</asp:ListItem>
									<asp:ListItem Value="13">Period 13</asp:ListItem>
									<asp:ListItem Value="14">Period 14</asp:ListItem>
									<asp:ListItem Value="15">Period 15</asp:ListItem>
									<asp:ListItem Value="16">Period 16</asp:ListItem>
                                </asp:DropDownList>
                            </div>
                            
                            <div class="form-group">
                                <label class="form-label required">Group ID</label>								
								<asp:DropDownList ID="ddlGroupId" runat="server" CssClass="asp-dropdown" onchange="updateJournalReference()">									
								</asp:DropDownList>
                            </div>
                        </div>
                        
                        <!-- Third Row -->
						<div class="form-grid">
							<div class="form-group">
								<label class="form-label required">Journal Title</label>
								<asp:TextBox ID="txtJournalTitle" runat="server" CssClass="asp-textbox" MaxLength="25"></asp:TextBox>
							</div>						

							<div class="form-group">
								<label class="form-label">Reference</label>                                
								<asp:TextBox ID="txtJournalReference" runat="server" CssClass="asp-textbox" MaxLength="16" ReadOnly="true" onchange="updateEJetReference()" onkeyup="updateEJetReference()"></asp:TextBox>
							</div>
							
							<div class="form-group">
								<label class="form-label">Prepared By</label>
								<asp:TextBox ID="txtPreparedBy" runat="server" CssClass="asp-textbox" ReadOnly="true"></asp:TextBox>
							</div>
							
							<div class="form-group">
								<label class="form-label">Authorised By</label>
								<asp:TextBox ID="txtAuthorisedBy" runat="server" CssClass="asp-textbox"></asp:TextBox>
							</div>                           
						</div>

						<!-- Description on its own row - full width -->
						<div class="form-group" style="margin-top: 20px;">
							<label class="form-label">Journal Description</label>
							<asp:TextBox ID="txtJournalDescription" runat="server" CssClass="modern-textarea" TextMode="MultiLine" Rows="3"></asp:TextBox>
						</div>
                        
                    </div>
                    
                    <!-- Line Items Tab -->
                    <div id="lineItemsTab" class="tab-content">
						<div class="excel-toolbar">
							<span style="font-weight: 600; margin-right: 16px;">📊 Line Items</span>
							<button type="button" class="excel-button" onclick="addLineItem()">
								➕ Add Row
							</button>
							<button type="button" class="excel-button" onclick="clearAllLines()">
								✖ Clear All
							</button>
							<button type="button" class="excel-button" onclick="copyAllToExcel()">
								📋 Copy to Excel
							</button>
							<button type="button" class="excel-button" onclick="showPasteHelp()">
								📥 Paste Help
							</button>
							<div style="margin-left: auto; color: rgba(255,255,255,0.9); font-size: 13px;">
								Rows: <span id="lblLineCount" style="font-weight: 600;"><asp:Label ID="lblLineCount" runat="server" Text="0"></asp:Label></span>
							</div>
						</div>
						<div class="formula-bar">
							<div class="cell-reference" id="currentCellRef">A1</div>
							<input type="text" class="formula-input" id="formulaBar" placeholder="Select a cell to edit" />
						</div>                        
                        <div class="line-items-container">
                            <table class="line-items-table" id="lineItemsTable">
                                <thead>								
									<tr>
										<th style="width: 1ch;" class="align-center">#</th>
										<th style="width: 5ch;">*Account</th>
										<th style="width: 10ch;">Account Description</th>
										<th style="width: 12ch;">WBS Element</th>
										<th style="width: 10ch;">WBS Element Description</th>
										<th style="width: 6ch;" title="Responsible Cost Centre">Capability Manager</th>
										<th style="width: 6ch;" title="Requesting Cost Centre">Delivery Manager</th>
										<th style="width: 4ch;">KIC</th>
										<th style="width: 6ch;">Cost Centre</th>
										<th style="width: 10ch;">Cost Centre Description</th>
										<th style="width: 6ch;">Order Number</th>
										<th style="width: 10ch;">Order Description</th>
										<th style="width: 6ch;">Tax Code</th>
										<th style="width: 6ch;" title="Consolidated Transaction Type">CTT</th>
										<th style="width: 8ch;" class="align-right" id="thDebit">Debit</th>
										<th style="width: 8ch;" class="align-right" id="thCredit">Credit</th>
										<th style="width: 8ch;" class="align-right" id="thDebitAUD" style="display:none;">Debit (AUD)</th>
										<th style="width: 8ch;" class="align-right" id="thCreditAUD" style="display:none;">Credit (AUD)</th>
										<th style="width: 10ch;">*Line Item Text</th>
										<th style="width: 6ch;" class="align-center">Actions</th>
									</tr>																	
                                </thead>
                                <tbody id="lineItemsBody">
									<!-- All lines will be dynamically added -->
                                </tbody>
                            </table>
                        </div>
						<div class="excel-status-bar">
							<div class="status-item">
								<span>Ready</span>
							</div>
							<!--<div class="status-item" style="margin-left: auto;">
								<span>Sum: </span><span id="selectionSum">0.00</span>
							</div>
							<div class="status-item">
								<span>Count: </span><span id="selectionCount">0</span>
							</div> -->
						</div>						
                    </div>                    
                </div>
            </div>
        </div>
        
        <!-- Floating Footer -->
        <div class="floating-footer">
            <div class="floating-footer-content">
				<div class="footer-left" style="display:flex;gap:16px;align-items:center;">
					<!-- Existing ERP Template upload -->
					<div style="display:flex;gap:8px;align-items:center;">
						<label for="<%= fuTemplate.ClientID %>" style="color:white;margin-right:8px;white-space:nowrap;">Upload ERP Template:</label>
						<asp:FileUpload ID="fuTemplate" runat="server" CssClass="sap-input" accept=".xlsx" onchange="autoUpload()" style="background:white;" />
						<asp:Button ID="btnUpload" runat="server" Text="Upload" CssClass="sap-button sap-button-secondary" OnClick="Upload_Click" style="display:none;" />
					</div>
					
					<!-- New Spreadsheet upload -->
					<div style="display:flex;gap:8px;align-items:center;">
						<label for="<%= fuSpreadsheet.ClientID %>" style="color:white;margin-right:8px;white-space:nowrap;">Upload Spreadsheet:</label>
						<asp:FileUpload ID="fuSpreadsheet" runat="server" CssClass="sap-input" accept=".xlsx,.xls" onchange="autoUploadSpreadsheet()" style="background:white;" />
						<asp:Button ID="btnUploadSpreadsheet" runat="server" Text="Upload Spreadsheet" CssClass="sap-button sap-button-secondary" OnClick="UploadSpreadsheet_Click" style="display:none;" />
					</div>
				</div>
                <div class="footer-right">
					<asp:Button ID="btnValidate" runat="server" Text="✓ Validate" CssClass="modern-button button-secondary" OnClientClick="return validateJournal();" UseSubmitBehavior="false" />
					<asp:Button ID="btnDownload" runat="server" Text="⬇ Download to ERP Template" CssClass="modern-button button-primary" OnClick="Download_Click" OnClientClick="collectLineItemsData(); showLoadingForDownload(); validateJournal();" UseSubmitBehavior="true" />
                </div>
            </div>
        </div>
        
        <!-- Loading Overlay -->
        <div class="loading-overlay" id="loadingOverlay">
            <div class="loading-spinner"></div>
        </div>
        
        <!-- Hidden fields for dynamic line items -->
        <asp:HiddenField ID="hdnLineItemsData" runat="server" />
		
		<!-- Hidden field for complete line item data including non-visible columns -->
		<asp:HiddenField ID="hdnCompleteLineData" runat="server" />

       				
		<!-- Line Details Popup -->
		<div id="lineDetailsPopup" style="display:none; position:fixed; top:50%; left:50%; transform:translate(-50%,-50%); 
			 background:white; border:1px solid #354a5f; box-shadow:0 4px 16px rgba(0,0,0,0.2); 
			 z-index:10000; max-width:800px; width:90%; max-height:80vh; overflow-y:auto;">
			<div style="background:#354a5f; color:white; padding:12px 16px; display:flex; justify-content:space-between; align-items:center;">
				<h3 style="margin:0; font-size:16px;">Line Item Details</h3>
				<button type="button" onclick="closeLineDetails()" style="background:none; border:none; color:white; font-size:20px; cursor:pointer;">&times;</button>
			</div>
			<div style="padding:20px;">
				<div class="form-grid" style="grid-template-columns: repeat(3, 1fr);">
					<div class="form-group">
						<label class="form-label">Amount in LC2</label>
						<input type="text" id="popupAmountLC2" class="modern-input" placeholder="0.00" />
					</div>
					<div class="form-group">
						<label class="form-label">Tax Jurisdiction</label>
						<input type="text" id="popupTaxJurisdiction" class="modern-input" maxlength="15" />
					</div>
					<div class="form-group">
						<label class="form-label">Profit Center</label>
						<input type="text" id="popupProfitCenter" class="modern-input" maxlength="10" />
					</div>
					<div class="form-group">
						<label class="form-label">Value Date</label>
						<input type="date" id="popupValueDate" class="modern-input" />
					</div>
					<div class="form-group">
						<label class="form-label">House Bank</label>
						<input type="text" id="popupHouseBank" class="modern-input" maxlength="5" />
					</div>
					<div class="form-group">
						<label class="form-label">House Bank Account</label>
						<input type="text" id="popupHouseBankAccount" class="modern-input" maxlength="5" />
					</div>
					<div class="form-group">
						<label class="form-label">Assignment</label>
						<input type="text" id="popupAssignment" class="modern-input" maxlength="18" />
					</div>
					<div class="form-group">
						<label class="form-label">Trading Partner</label>
						<input type="text" id="popupTradingPartner" class="modern-input" maxlength="6" />
					</div>
					<div class="form-group">
						<label class="form-label">Segment</label>
						<input type="text" id="popupSegment" class="modern-input" maxlength="10" />
					</div>
					<div class="form-group">
						<label class="form-label">Customer</label>
						<input type="text" id="popupCustomer" class="modern-input" maxlength="10" />
					</div>
					<div class="form-group">
						<label class="form-label">Product Number</label>
						<input type="text" id="popupProduct" class="modern-input" maxlength="40" />
					</div>
				</div>
				<div class="button-group" style="margin-top:20px; justify-content:flex-end;">
					<button type="button" class="modern-button sap-button-secondary" onclick="closeLineDetails()">Cancel</button>
					<button type="button" class="modern-button button-primary" onclick="saveLineDetails()">Save</button>
				</div>
			</div>
		</div>
		<div id="popupOverlay" style="display:none; position:fixed; top:0; left:0; width:100%; height:100%;
			background:rgba(0,0,0,0.5); z-index:9999;" onclick="closeLineDetails()">
		</div>
		
		<!-- Support Dialog -->
		<div id="supportDialog" class="support-dialog">
			<div class="support-dialog-header">
				<h3 style="margin: 0; font-size: 18px;">Get Support</h3>
				<button type="button" onclick="closeSupportDialog()" style="background: none; border: none; color: white; font-size: 24px; cursor: pointer;">&times;</button>
			</div>
			<div class="support-dialog-content">
				<button type="button" class="support-option" onclick="window.location.href='mailto:dfg.dfspi@defence.gov.au?subject=eJET%20Support%20Request'">
					<div class="support-option-title">📧 Email Support</div>
					<div class="support-option-desc">Send an email to the support team</div>
				</button>
				
				<button type="button" class="support-option" onclick="window.location.href='mailto:dfg.dfspi@defence.gov.au?subject=eJET%20Bug%20Report&body=Please%20describe%20the%20issue%3A%0A%0A%0ASteps%20to%20reproduce%3A%0A1.%20%0A2.%20%0A3.%20'">
					<div class="support-option-title">🐛 Report a Bug</div>
					<div class="support-option-desc">Report technical issues or unexpected behavior</div>
				</button>
				
				<button type="button" class="support-option" onclick="window.location.href='mailto:dfg.dfspi@defence.gov.au?subject=eJET%20Feature%20Request'">
					<div class="support-option-title">💡 Request a Feature</div>
					<div class="support-option-desc">Suggest improvements or new functionality</div>
				</button>
				
				<div style="margin-top: 20px; padding-top: 20px; border-top: 1px solid #e9ecef; text-align: center; color: #6c757d; font-size: 13px;">
					Version: 1.0 | Last Updated: <%= DateTime.Now.ToString("dd MMM yyyy") %>
				</div>
			</div>
		</div>
		<div id="supportOverlay" style="display:none; position:fixed; top:0; left:0; width:100%; height:100%; background:rgba(0,0,0,0.5); z-index:10000;" onclick="closeSupportDialog()"></div>		
		
    </form>

	<script type="text/javascript">
		// Configuration constants
		const CONFIG = {
			AUTOSAVE_KEY: 'ejet_draft',
			AUTOSAVE_INTERVAL: 30000,
			DEBOUNCE_DELAY: 300,
			DATE_FORMAT: 'yyyy-MM-dd',
			MAX_LINE_ITEMS: 999,
			VALIDATION: {
				GL_LENGTH: 10,
				WBS_LENGTH: 24,
				CC_LENGTH: 10,
				ORDER_LENGTH: 12,
				TEXT_LENGTH: 50
			},
			MESSAGES: {
				PASTE_SUCCESS: 'Pasted {0} rows from Excel',
				COPY_SUCCESS: 'Copied {0} rows to clipboard',
				SAVE_SUCCESS: 'Draft saved successfully',
				LOAD_SUCCESS: 'Draft loaded successfully',
				DELETE_CONFIRM: 'Are you sure you want to delete this row?',
				CLEAR_CONFIRM: 'Are you sure you want to clear all line items?',
				VALIDATION_ERROR: 'Please fix validation errors before proceeding'
			}
		};

		// Global variables
		let lineCounter = 0;
		let completeLineData = {};
		let currentEditingLine = null;
		let currentCell = null;
		let selectionStart = null;
		let selectedCells = [];
		let isSelecting = false;
		let masterdata = {};
		let statusPanelId = '';
		let statusLabelId = '';

		// Error handling wrapper
		function safeExecute(fn, errorMessage = 'An error occurred') {
			return function(...args) {
				try {
					return fn.apply(this, args);
				} catch (error) {
					console.error(errorMessage, error);
					showMessage(errorMessage + ': ' + error.message, true);
				}
			};
		}

		// String formatting helper
		String.prototype.format = function() {
			const args = arguments;
			return this.replace(/{(\d+)}/g, function(match, number) {
				return typeof args[number] !== 'undefined' ? args[number] : match;
			});
		};

		// Initialize page
		window.onload = function() {
			// Capture the ASP.NET ClientIDs
			statusPanelId = '<%= pnlStatus.ClientID %>';
			statusLabelId = '<%= lblStatus.ClientID %>';		
						
			// Set dates if empty
			const docDateField = document.getElementById('<%= txtDocumentDate.ClientID %>');
			const postDateField = document.getElementById('<%= txtPostingDate.ClientID %>');
			
			if (!docDateField.value) {
				const today = new Date().toISOString().split('T')[0];
				docDateField.value = today;
			}
			
			if (!postDateField.value) {
				const today = new Date().toISOString().split('T')[0];
				postDateField.value = today;
			}
			
			// Initialize features
			updatePostingPeriod();
			
			// Load reference data - critical for validation
			masterdata = {};

			PageMethods.LoadMasterData( 
				function(result) {
					masterdata = JSON.parse(result);
					// Start with one empty line
					lineCounter = 0;
					addLineItem();
					updateTotals();
					updateCurrencyHeaders();
					updateExchangeRate();
					updateJournalReference();
					
					// Initialize features with delay
					setTimeout(() => {
						try {
							ColumnResizer.init();
							initExcelFeatures();
						} catch (e) {
							// Don't show message for UI enhancement failures
						}
					}, 500);
					
					// Add smooth scrolling
					const container = document.querySelector('.line-items-container');
					if (container) {
						container.style.scrollBehavior = 'smooth';
					}
					
					// Fix for dropdown click events
					setTimeout(() => {
						document.querySelectorAll('.line-items-table select').forEach(select => {
							select.addEventListener('mousedown', function(e) {
								e.stopPropagation();
							});
						});
					}, 1000);
				}
			);			
		};


		// Excel-like features initialization
		function initExcelFeatures() {
			const formulaBar = document.getElementById('formulaBar');
			const table = document.getElementById('lineItemsTable');
			
			if (!formulaBar || !table) return;
			
			// Formula bar input handler
			formulaBar.addEventListener('input', function() {
				if (currentCell) {
					currentCell.value = this.value;
					currentCell.dispatchEvent(new Event('change'));
				}
			});
			
			// Mouse down - start selection
			table.addEventListener('mousedown', function(e) {
				const cell = e.target.closest('td');
				if (!cell || cell.classList.contains('line-number') || cell.classList.contains('action-buttons')) return;
				
				clearSelection();
				
				isSelecting = true;
				selectionStart = cell;
				cell.classList.add('cell-selected');
				selectedCells = [cell];
				
				const input = cell.querySelector('input, select');
				if (input) {
					currentCell = input;
					input.focus();
					updateFormulaBar();
				}
				
				e.preventDefault();
			});
			
			// Mouse move - extend selection
			table.addEventListener('mousemove', function(e) {
				if (!isSelecting || !selectionStart) return;
				
				const cell = e.target.closest('td');
				if (!cell || cell.classList.contains('line-number') || cell.classList.contains('action-buttons')) return;
				
				updateSelection(selectionStart, cell);
			});
			
			// Mouse up - end selection
			document.addEventListener('mouseup', function() {
				isSelecting = false;
			});
			
			// Keyboard selection with Shift
			table.addEventListener('keydown', function(e) {
				if (e.shiftKey && (e.key.includes('Arrow') || e.key === 'Tab')) {
					e.preventDefault();
					handleKeyboardSelection(e);
				}
			});
		}

		// Clear cell selection
		function clearSelection() {
			document.querySelectorAll('.cell-selected').forEach(cell => {
				cell.classList.remove('cell-selected');
			});
			document.querySelectorAll('.cell-active').forEach(cell => {
				cell.classList.remove('cell-active');
			});
			selectedCells = [];
		}

		// Update cell selection
		function updateSelection(startCell, endCell) {
			document.querySelectorAll('.cell-selected').forEach(c => c.classList.remove('cell-selected'));
			selectedCells = [];
			
			const startRow = startCell.parentElement;
			const endRow = endCell.parentElement;
			const tbody = startRow.parentElement;
			const rows = Array.from(tbody.children);
			
			const startRowIndex = rows.indexOf(startRow);
			const endRowIndex = rows.indexOf(endRow);
			const startColIndex = Array.from(startRow.children).indexOf(startCell);
			const endColIndex = Array.from(endRow.children).indexOf(endCell);
			
			const minRow = Math.min(startRowIndex, endRowIndex);
			const maxRow = Math.max(startRowIndex, endRowIndex);
			const minCol = Math.min(startColIndex, endColIndex);
			const maxCol = Math.max(startColIndex, endColIndex);
			
			for (let r = minRow; r <= maxRow; r++) {
				const row = rows[r];
				if (row) {
					for (let c = minCol; c <= maxCol; c++) {
						const cell = row.cells[c];
						if (cell && !cell.classList.contains('line-number') && !cell.classList.contains('action-buttons')) {
							cell.classList.add('cell-selected');
							selectedCells.push(cell);
						}
					}
				}
			}
			
			updateSelectionStats();
		}

		// Update selection statistics
		function updateSelectionStats() {
			let sum = 0;
			let count = 0;
			let numericCount = 0;
			
			selectedCells.forEach(cell => {
				const input = cell.querySelector('input');
				if (input) {
					count++;
					const value = parseFloat(input.value);
					if (!isNaN(value) && input.value.trim() !== '') {
						sum += value;
						numericCount++;
					}
				}
			});
			
			//document.getElementById('selectionSum').textContent = numericCount > 0 ? sum.toFixed(2) : '0.00';
			//document.getElementById('selectionCount').textContent = count;
		}

		// Update formula bar
		function updateFormulaBar() {
			const formulaBar = document.getElementById('formulaBar');
			const cellRef = document.getElementById('currentCellRef');
			
			if (currentCell && formulaBar && cellRef) {
				formulaBar.value = currentCell.value;
				formulaBar.disabled = currentCell.readOnly;
				
				const cell = currentCell.closest('td');
				const row = cell.parentElement;
				const tbody = row.parentElement;
				const rowIndex = Array.from(tbody.children).indexOf(row) + 1;
				const colIndex = Array.from(row.children).indexOf(cell);
				const colLetter = getColumnLetter(colIndex);
				cellRef.textContent = colLetter + rowIndex;
			}
		}

		// Number formatting helper
		function formatNumber(value) {
			if (!value || value === '') return '';
			// Remove any existing formatting
			const cleanValue = value.toString().replace(/,/g, '');
			const num = parseFloat(cleanValue);
			if (isNaN(num)) return '';
			// Always show 2 decimal places
			return num.toLocaleString('en-AU', { 
				minimumFractionDigits: 2, 
				maximumFractionDigits: 2,
				useGrouping: true 
			});
		}

		// Parse formatted number back to plain number
		function parseFormattedNumber(value) {
			if (!value || value === '') return '';
			// Remove any formatting (commas, spaces, etc.)
			return value.toString().replace(/[,\s]/g, '');
		}	
		
		// Format amount field
		function formatAmountField(input) {
			const value = parseFormattedNumber(input.value);
			if (value) {
				input.value = formatNumber(value);
			}
		}		
		
		// Get Excel column letter
		function getColumnLetter(index) {
			let letter = '';
			while (index > 0) {
				index--;
				letter = String.fromCharCode(65 + (index % 26)) + letter;
				index = Math.floor(index / 26);
			}
			return letter;
		}

		// Handle keyboard selection
		function handleKeyboardSelection(e) {
			if (!currentCell) return;
			
			const currentTd = currentCell.closest('td');
			if (!selectionStart) selectionStart = currentTd;
			
			const row = currentTd.parentElement;
			const cellIndex = Array.from(row.children).indexOf(currentTd);
			let targetCell = null;
			
			switch(e.key) {
				case 'ArrowUp':
					const prevRow = row.previousElementSibling;
					if (prevRow) targetCell = prevRow.cells[cellIndex];
					break;
				case 'ArrowDown':
					const nextRow = row.nextElementSibling;
					if (nextRow) targetCell = nextRow.cells[cellIndex];
					break;
				case 'ArrowLeft':
					targetCell = currentTd.previousElementSibling;
					break;
				case 'ArrowRight':
					targetCell = currentTd.nextElementSibling;
					break;
			}
			
			if (targetCell && !targetCell.classList.contains('line-number') && !targetCell.classList.contains('action-buttons')) {
				updateSelection(selectionStart, targetCell);
				const input = targetCell.querySelector('input, select');
				if (input) {
					currentCell = input;
					input.focus();
					updateFormulaBar();
				}
			}
		}

		
		// Update Financial Transaction Type dropdown
		function updateFinTransTypeDropdown(lineNum) {
			var glValue = document.getElementById('txtGL' + lineNum).value.trim();
			var cttSelect = document.getElementById('ddlFinTransType' + lineNum);
			var isCttAsset = document.getElementById('txtIsCttAsset' + lineNum).value.trim();
			var isCttInven = document.getElementById('txtIsCttInven' + lineNum).value.trim();

			if (!cttSelect) {
				return;
			}
						
			cttSelect.innerHTML = '<option value=""></option>';
			
			if (isCttInven === "1") {
				cttSelect.disabled = false;
				masterdata.inventoryCTTs.forEach(function(ctt) {
					var option = document.createElement('option');
					option.value = ctt.code;
					option.text = ctt.description;
					cttSelect.appendChild(option);
				});
			} else if (isCttAsset === "1") {
				cttSelect.disabled = false;
				masterdata.assetCTTs.forEach(function(ctt) {
					var option = document.createElement('option');
					option.value = ctt.code;
					option.text = ctt.description;
					cttSelect.appendChild(option);
				});
			} else {
				cttSelect.disabled = true;
				cttSelect.value = '';
			}
		}

		// Tab switching
		function switchTab(event, tabName) {
			collectLineItemsData();
			
			document.querySelectorAll('.modern-tab').forEach(tab => tab.classList.remove('active'));
			document.querySelectorAll('.tab-content').forEach(content => content.classList.remove('active'));
			
			event.target.closest('.modern-tab').classList.add('active');
			document.getElementById(tabName + 'Tab').classList.add('active');
		}

		// Add line item
		function addLineItem() {
			lineCounter++;
			const tbody = document.getElementById('lineItemsBody');
			const newRow = document.createElement('tr');
			newRow.setAttribute('data-line', lineCounter);
			
			const selectedCurrency = document.getElementById('<%= ddlCurrency.ClientID %>').value;
			const isNonAUD = selectedCurrency !== 'AUD';
			
			newRow.innerHTML = `
				<td class="line-number align-center" style="cursor:pointer;" onclick="showLineDetails(${lineCounter})" title="Click for more details">${lineCounter}</td>
				<td><input type="text" class="modern-input" id="txtGL${lineCounter}" placeholder="GL Account" maxlength="10"
					onblur="lookupDynamicField('txtGL${lineCounter}', 'gl', 'txtGLDesc${lineCounter}'); " /></td>
				<td><input type="text" class="modern-input" id="txtGLDesc${lineCounter}" readonly placeholder="" /></td>
				<td><input type="text" class="modern-input" id="txtWBS${lineCounter}" placeholder="WBS" maxlength="24"
					onblur="lookupDynamicField('txtWBS${lineCounter}', 'wbs', 'txtWBSDesc${lineCounter}')" /></td>
				<td><input type="text" class="modern-input" id="txtWBSDesc${lineCounter}" readonly placeholder="" /></td>
				<td><input type="text" class="modern-input" id="txtCapMgr${lineCounter}" readonly placeholder="" /></td>
				<td><input type="text" class="modern-input" id="txtDelMgr${lineCounter}" readonly placeholder="" /></td>
				<td><input type="text" class="modern-input" id="txtKIC${lineCounter}" readonly placeholder="" /></td>
				<td><input type="text" class="modern-input" id="txtCC${lineCounter}" placeholder="Cost Centre" maxlength="10"
					onblur="lookupDynamicField('txtCC${lineCounter}', 'cc', 'txtCCDesc${lineCounter}'); copyToProfitCenter(${lineCounter});" /></td>
				<td><input type="text" class="modern-input" id="txtCCDesc${lineCounter}" readonly placeholder="" /></td>
				<td><input type="text" class="modern-input" id="txtOrder${lineCounter}" placeholder="Order" maxlength="12"
					onblur="lookupDynamicField('txtOrder${lineCounter}', 'order', 'txtOrderDesc${lineCounter}')" /></td>
				<td><input type="text" class="modern-input" id="txtOrderDesc${lineCounter}" readonly placeholder="" /></td>
				<td><select class="modern-select" id="ddlTaxCode${lineCounter}">
					<option value=""></option>
				</select></td>
				<td><select class="modern-select" id="ddlFinTransType${lineCounter}" disabled>
					<option value=""></option>
				</select></td>
				<td class="align-right"><input type="text" class="modern-input amount-input" id="txtDebit${lineCounter}" placeholder="0.00" maxlength="25" 
					onchange="formatAmountField(this); updateTotals();" 
					onfocus="this.value = parseFormattedNumber(this.value)" 
					onblur="this.value = formatNumber(this.value)" /></td>
				<td class="align-right"><input type="text" class="modern-input amount-input" id="txtCredit${lineCounter}" placeholder="0.00" maxlength="25" 
					onchange="formatAmountField(this); updateTotals();" 
					onfocus="this.value = parseFormattedNumber(this.value)" 
					onblur="this.value = formatNumber(this.value)" /></td>
				<td class="align-right" style="display:${isNonAUD ? 'table-cell' : 'none'};"><input type="text" class="modern-input amount-input" id="txtDebitAUD${lineCounter}" placeholder="0.00" maxlength="25" 
					onfocus="this.value = parseFormattedNumber(this.value)" 
					onblur="this.value = formatNumber(this.value)" /></td>				
				<td class="align-right" style="display:${isNonAUD ? 'table-cell' : 'none'};"><input type="text" class="modern-input amount-input" id="txtCreditAUD${lineCounter}" placeholder="0.00" maxlength="25" 
					onfocus="this.value = parseFormattedNumber(this.value)" 
					onblur="this.value = formatNumber(this.value)" /></td>
				<td><input type="text" class="modern-input" id="txtText${lineCounter}" placeholder="Line Item Text" maxlength="50" /></td>
				<td class="action-buttons align-center">
					<button type="button" class="icon-button" onclick="duplicateLine(${lineCounter})" title="Duplicate">🗐</button>
					<button type="button" class="icon-button delete" onclick="deleteLine(${lineCounter})" title="Delete">🗑</button>
				</td>
				<input type="hidden" id="txtIsCttAsset${lineCounter}" />
				<input type="hidden" id="txtIsCttInven${lineCounter}" />
				<input type="hidden" id="txtTaxCat${lineCounter}" />
			`;
			
			tbody.appendChild(newRow);
			var taxSelect = document.getElementById('ddlTaxCode' + lineCounter);
			masterdata.taxCodes.forEach(function(taxCode) {
					var option = document.createElement('option');
					option.value = taxCode.code;
					option.text = taxCode.description;
					taxSelect.appendChild(option);
				});

			updateLineCount();

			taxSelect.addEventListener('mousedown', function(e) {
					e.stopPropagation();
				});

			completeLineData[lineCounter] = {};
		}

		// Show line details popup
		function showLineDetails(lineNum) {
			currentEditingLine = lineNum;
			const lineData = completeLineData[lineNum] || {};
			
			document.getElementById('popupAmountLC2').value = lineData.amountLC2 || '';
			document.getElementById('popupTaxJurisdiction').value = lineData.taxJurisdiction || '';
			document.getElementById('popupProfitCenter').value = lineData.profitCenter || '';
			document.getElementById('popupValueDate').value = lineData.valueDate || '';
			document.getElementById('popupHouseBank').value = lineData.houseBank || '';
			document.getElementById('popupHouseBankAccount').value = lineData.houseBankAccount || '';
			document.getElementById('popupAssignment').value = lineData.assignment || '';
			document.getElementById('popupTradingPartner').value = lineData.tradingPartner || '';
			document.getElementById('popupSegment').value = lineData.segment || '';
			document.getElementById('popupCustomer').value = lineData.customer || '';
			document.getElementById('popupProduct').value = lineData.product || '';
			
			document.getElementById('lineDetailsPopup').style.display = 'block';
			document.getElementById('popupOverlay').style.display = 'block';
		}

		// Close line details popup
		function closeLineDetails() {
			document.getElementById('lineDetailsPopup').style.display = 'none';
			document.getElementById('popupOverlay').style.display = 'none';
			currentEditingLine = null;
		}

		// Save line details
		function saveLineDetails() {
			if (currentEditingLine) {
				completeLineData[currentEditingLine] = {
					amountLC2: document.getElementById('popupAmountLC2').value,
					taxJurisdiction: document.getElementById('popupTaxJurisdiction').value,
					profitCenter: document.getElementById('popupProfitCenter').value,
					valueDate: document.getElementById('popupValueDate').value,
					houseBank: document.getElementById('popupHouseBank').value,
					houseBankAccount: document.getElementById('popupHouseBankAccount').value,
					assignment: document.getElementById('popupAssignment').value,
					tradingPartner: document.getElementById('popupTradingPartner').value,
					segment: document.getElementById('popupSegment').value,
					customer: document.getElementById('popupCustomer').value,
					product: document.getElementById('popupProduct').value
				};
				
				updateCompleteLineData();
				closeLineDetails();
				
				const lineNumCell = document.querySelector(`tr[data-line="${currentEditingLine}"] .line-number`);
				if (lineNumCell) {
					lineNumCell.style.fontWeight = 'bold';
					lineNumCell.style.color = '#cf4520';
				}
			}
		}

		// Update complete line data
		function updateCompleteLineData() {
			document.getElementById('<%= hdnCompleteLineData.ClientID %>').value = JSON.stringify(completeLineData);
		}

		// Copy cost centre to profit center
		function copyToProfitCenter(lineNum) {
			const ccValue = document.getElementById('txtCC' + lineNum).value;
			if (!completeLineData[lineNum]) {
				completeLineData[lineNum] = {};
			}
			completeLineData[lineNum].profitCenter = ccValue;
		}

		// Update currency headers
		function updateCurrencyHeaders() {
			const selectedCurrency = document.getElementById('<%= ddlCurrency.ClientID %>').value;
			const isNonAUD = selectedCurrency !== 'AUD';
			
			document.getElementById('thDebit').textContent = isNonAUD ? `Debit (${selectedCurrency})` : 'Debit';
			document.getElementById('thCredit').textContent = isNonAUD ? `Credit (${selectedCurrency})` : 'Credit';
			
			document.getElementById('thDebitAUD').style.display = isNonAUD ? 'table-cell' : 'none';
			document.getElementById('thCreditAUD').style.display = isNonAUD ? 'table-cell' : 'none';
			
			// Update the total labels in the header
			const totalDebitLabel = document.querySelector('.total-item:first-child .total-label:last-child');
			const totalCreditLabel = document.querySelector('.total-item:last-child .total-label:last-child');
			
			if (totalDebitLabel) totalDebitLabel.textContent = selectedCurrency;
			if (totalCreditLabel) totalCreditLabel.textContent = selectedCurrency;
			
			document.querySelectorAll('#lineItemsBody tr').forEach((row) => {
				const lineNum = row.getAttribute('data-line');
				const debitAUDCell = row.querySelector(`td:has(#txtDebitAUD${lineNum})`);
				const creditAUDCell = row.querySelector(`td:has(#txtCreditAUD${lineNum})`);
				
				if (debitAUDCell) debitAUDCell.style.display = isNonAUD ? 'table-cell' : 'none';
				if (creditAUDCell) creditAUDCell.style.display = isNonAUD ? 'table-cell' : 'none';
			});
		}
		
		// Update exchange rate
		function updateExchangeRate() {
			const currencyField = document.getElementById('<%= ddlCurrency.ClientID %>');
			const exchangeRateField = document.getElementById('<%= txtExchangeRate.ClientID %>');
			
			if (currencyField.value === 'AUD') {
				exchangeRateField.value = '1.00000';
				exchangeRateField.readOnly = true;
				exchangeRateField.style.backgroundColor = '#f8f9fa';
			} else {
				// Clear the field when switching to non-AUD
				if (exchangeRateField.value === '1.00000') {
					exchangeRateField.value = '';
				}
				exchangeRateField.readOnly = false;
				exchangeRateField.style.backgroundColor = '#fff';
				// Focus on the field for user to enter rate
				exchangeRateField.focus();
			}
		}

		// Update journal reference
		function updateJournalReference() {
			const groupField = document.getElementById('<%= ddlGroupId.ClientID %>');
			const referenceField = document.getElementById('<%= txtJournalReference.ClientID %>');
			const eJetLabel = document.getElementById('<%= lblEJetReference.ClientID %>');
			
			if (groupField.value) {
				const newReference = groupField.value + '_eJET';
				referenceField.value = newReference;
				eJetLabel.textContent = newReference;
			} else {
				// Don't use datetime when no group selected
				referenceField.value = '_eJET';
				eJetLabel.textContent = '_eJET';
			}
		}

		// Lookup dynamic field
		function lookupDynamicField(inputId, lookupType, targetId) {
			var code = document.getElementById(inputId).value;
			var lineNum = inputId.match(/\d+$/)[0];
			// If code is empty, clear description and remove any error styling
			if (!code) {
				document.getElementById(targetId).value = '';
				document.getElementById(inputId).style.borderColor = '';
				if (lookupType === 'wbs') {
					document.getElementById('txtCapMgr' + lineNum).value = '';
					document.getElementById('txtCapMgr' + lineNum).title =  '';
					document.getElementById('txtDelMgr' + lineNum).value =  '';
					document.getElementById('txtDelMgr' + lineNum).title =  '';
					document.getElementById('txtKIC' + lineNum).value = '';
					document.getElementById('txtKIC' + lineNum).title = '';
				}
				if(lookupType === "gl"){
					document.getElementById('txtIsCttAsset' + lineNum).value = '';
					document.getElementById('txtIsCttInven' + lineNum).value = '';
					document.getElementById('txtTaxCat' + lineNum).value = '';
					document.getElementById('ddlFinTransType' + lineNum).disabled = true;
					document.getElementById('ddlFinTransType' + lineNum).value = '';
				}
				return;
			}
			
						
			document.getElementById(targetId).value = 'Looking up...';
			
			// Capture inputId in a local variable for use in callbacks
			var currentInputId = inputId;
			var currentTargetId = targetId;
			// var lineNum;
			PageMethods.GetDescription(code, lookupType, 
				function(result) {
					if (lookupType === 'gl' && result) {
						try {
							var glData = JSON.parse(result);
							document.getElementById(currentTargetId).value = glData.desc || '';
							
							// lineNum = currentInputId.match(/\d+$/)[0];
							document.getElementById('txtIsCttAsset' + lineNum).value = glData.isCttAsset || '';
							document.getElementById('txtIsCttInven' + lineNum).valie = glData.isCttInven || '';
							document.getElementById('txtTaxCat' + lineNum).value = glData.taxCat.trim() || '';
							
							// Remove error styling if we got a valid result
							if (glData.desc) {
								document.getElementById(currentInputId).style.borderColor = '';
							} else {
								document.getElementById(currentInputId).style.borderColor = '#dc3545';
							}
							updateFinTransTypeDropdown(lineNum);
						} catch (e) {
							document.getElementById(currentTargetId).value = '';
							// Handle error styling for WBS parsing errors
							document.getElementById(currentInputId).style.borderColor = '#dc3545';
						}
					}
					else if (lookupType === 'wbs' && result) {
						try {
							var wbsData = JSON.parse(result);
							document.getElementById(currentTargetId).value = wbsData.desc || '';
							
							lineNum = currentInputId.match(/\d+$/)[0];
							document.getElementById('txtCapMgr' + lineNum).value = wbsData.capMgr || '';
							document.getElementById('txtCapMgr' + lineNum).title = wbsData.capMgrDsc || '';
							document.getElementById('txtDelMgr' + lineNum).value = wbsData.delMgr || '';
							document.getElementById('txtDelMgr' + lineNum).title = wbsData.delMgrDsc || '';
							document.getElementById('txtKIC' + lineNum).value = wbsData.kic || '';
							document.getElementById('txtKIC' + lineNum).title = wbsData.kicDsc || '';
							
							// Remove error styling if we got a valid result
							if (wbsData.desc) {
								document.getElementById(currentInputId).style.borderColor = '';
							} else {
								document.getElementById(currentInputId).style.borderColor = '#dc3545';
							}
						} catch (e) {
							document.getElementById(currentTargetId).value = '';
							// Handle error styling for WBS parsing errors
							document.getElementById(currentInputId).style.borderColor = '#dc3545';
						}
					} else {
						document.getElementById(currentTargetId).value = result || '';
						// Add/remove error styling based on result
						if (!result || result === '') {
							document.getElementById(currentInputId).style.borderColor = '#dc3545';
						} else {
							document.getElementById(currentInputId).style.borderColor = '';
						}
					}
				},
				function(error) {
					document.getElementById(currentTargetId).value = '';
					// Always show error styling on lookup failure
					document.getElementById(currentInputId).style.borderColor = '#dc3545';
				}
			);
		}

		// Duplicate line
		function duplicateLine(lineNumber) {
			const originalRow = document.querySelector(`tr[data-line="${lineNumber}"]`);
			lineCounter++;
			
			const newRow = originalRow.cloneNode(true);
			newRow.setAttribute('data-line', lineCounter);
			const lineNumTd = newRow.querySelector('.line-number');
			lineNumTd.textContent = lineCounter;
			if(lineNumTd.getAttribute('onclick')){
				lineNumTd.setAttribute('onclick', lineNumTd.getAttribute('onclick').replace(/\(\d+\)/, `(${lineCounter})`));
			}
			
			newRow.querySelectorAll('input').forEach(input => {
				const id = input.id;
				if (id) {
					input.id = id.replace(/\d+$/, lineCounter);
				}
				if(input.getAttribute('onblur')){
					input.setAttribute('onblur', input.getAttribute('onblur').replaceAll(lineNumber, lineCounter));
				}
				if(input.getAttribute('onchange')){
					input.setAttribute('onchange', input.getAttribute('onchange').replaceAll(lineNumber, lineCounter));					
				}
				if(input.getAttribute('onfocus')){
					input.setAttribute('onfocus', input.getAttribute('onfocus').replaceAll(lineNumber, lineCounter));					
				}				
			});
			
			newRow.querySelectorAll('input[id*="Debit"], input[id*="Credit"]').forEach(input => {
				if (input.value) {
					input.value = formatNumber(parseFormattedNumber(input.value));
				}
			});			
			
			newRow.querySelectorAll('select').forEach(select => {
				const id = select.id;
				if (id) {
					select.id = id.replace(/\d+$/, lineCounter);
				}
				select.addEventListener('mousedown', function(e) {
					e.stopPropagation();
				});

				select.value = document.getElementById(id).value;;
			});
			
			newRow.querySelectorAll('button').forEach(button => {
				const onclick = button.getAttribute('onclick');
				if (onclick) {
					button.setAttribute('onclick', onclick.replace(/\(\d+\)/, `(${lineCounter})`));
				}
			});
			
			originalRow.parentNode.insertBefore(newRow, originalRow.nextSibling);
			updateLineCount();
			updateTotals();
			
			const ccValue = newRow.querySelector(`#txtCC${lineCounter}`).value;
			if (ccValue) {
				if (!completeLineData[lineCounter]) {
					completeLineData[lineCounter] = {};
				}
				completeLineData[lineCounter].profitCenter = ccValue;
			}
		}

		// Delete line
		function deleteLine(lineNumber) {
			const row = document.querySelector(`tr[data-line="${lineNumber}"]`);
			if (row && document.querySelectorAll('#lineItemsBody tr').length > 1) {
				row.remove();
				updateLineCount();
				updateTotals();
			} else {
				alert('At least one line item is required.');
			}
		}

		// Update posting period
		function updatePostingPeriod() {
			const postDateField = document.getElementById('<%= txtPostingDate.ClientID %>');
			const periodField = document.getElementById('<%= ddlPostingPeriod.ClientID %>');
			
			if (postDateField.value) {
				const postDate = new Date(postDateField.value);
				const month = postDate.getMonth() + 1;
				
				let fiscalPeriod;
				if (month >= 7) {
					fiscalPeriod = month - 6;
				} else {
					fiscalPeriod = month + 6;
				}
				
				periodField.value = fiscalPeriod.toString().padStart(2, '0');
			}
		}

		// Update totals
		function updateTotals() {
			let totalDebit = 0;
			let totalCredit = 0;
			
			document.querySelectorAll('#lineItemsBody tr').forEach((row) => {
				const debitInput = row.querySelector('input[id^="txtDebit"]');
				const creditInput = row.querySelector('input[id^="txtCredit"]');
				if (debitInput) {
					const debitValue = parseFormattedNumber(debitInput.value);
					totalDebit += parseFloat(debitValue) || 0;
				}
				if (creditInput) {
					const creditValue = parseFormattedNumber(creditInput.value);
					totalCredit += parseFloat(creditValue) || 0;
				}
			});
			
			// Format with commas
			document.getElementById('<%= lblTotalDebit.ClientID %>').textContent = totalDebit.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
			document.getElementById('<%= lblTotalCredit.ClientID %>').textContent = totalCredit.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
			
			const totalDebitElement = document.getElementById('<%= lblTotalDebit.ClientID %>');
			const totalCreditElement = document.getElementById('<%= lblTotalCredit.ClientID %>');
			
			if (Math.abs(totalDebit - totalCredit) < 0.01) {
				totalDebitElement.style.color = '#28a745';
				totalCreditElement.style.color = '#28a745';
			} else {
				totalDebitElement.style.color = '#cf4520';
				totalCreditElement.style.color = '#cf4520';
			}
		}

		// Update eJET reference
		function updateEJetReference() {
			const referenceField = document.getElementById('<%= txtJournalReference.ClientID %>');
			const eJetLabel = document.getElementById('<%= lblEJetReference.ClientID %>');
			
			if (referenceField && eJetLabel) {
				const newValue = referenceField.value.trim();
				if (newValue) {
					eJetLabel.textContent = newValue;
				} else {
					eJetLabel.textContent = '-';
				}
			}
		}
		
		// Clear all lines
		function clearAllLines() {
			if (confirm(CONFIG.MESSAGES.CLEAR_CONFIRM)) {
				const tbody = document.getElementById('lineItemsBody');
				tbody.innerHTML = '';
				
				lineCounter = 0;
				addLineItem();
				
				updateLineCount();
				updateTotals();
			}
		}

		// Collect line items data
		function collectLineItemsData() {
			updateJournalReference();
		
			const lineItems = [];
			document.querySelectorAll('#lineItemsBody tr').forEach((row) => {
				const lineNum = row.getAttribute('data-line');
				const additionalData = completeLineData[lineNum] || {};
				
				const selectedCurrency = document.getElementById('<%= ddlCurrency.ClientID %>').value;
				const isNonAUD = selectedCurrency !== 'AUD';
				
				let amountCC = '';
				if (isNonAUD) {
					const debitAUD = row.querySelector(`#txtDebitAUD${lineNum}`).value;
					const creditAUD = row.querySelector(`#txtCreditAUD${lineNum}`).value;
					if (debitAUD) amountCC = debitAUD;
					else if (creditAUD) amountCC = creditAUD;
				}
				
				const lineData = {
					gl: row.querySelector('input[id^="txtGL"]').value,
					glDesc: row.querySelector('input[id^="txtGLDesc"]').value,
					wbs: row.querySelector('input[id^="txtWBS"]').value,
					wbsDesc: row.querySelector('input[id^="txtWBSDesc"]').value,
					capMgr: row.querySelector('input[id^="txtCapMgr"]').value,
					capMgrDsc: row.querySelector('input[id^="txtCapMgr"]').title,
					delMgr: row.querySelector('input[id^="txtDelMgr"]').value,
					delMgrDsc: row.querySelector('input[id^="txtDelMgr"]').title,
					kic: row.querySelector('input[id^="txtKIC"]').value,
					kicDsc: row.querySelector('input[id^="txtKIC"]').title,
					cc: row.querySelector('input[id^="txtCC"]').value,
					ccDesc: row.querySelector('input[id^="txtCCDesc"]').value,
					order: row.querySelector('input[id^="txtOrder"]').value,
					orderDesc: row.querySelector('input[id^="txtOrderDesc"]').value,
					taxCode: row.querySelector('select[id^="ddlTaxCode"]').value,
					finTransType: row.querySelector('select[id^="ddlFinTransType"]').value,
					debit: parseFormattedNumber(row.querySelector('input[id^="txtDebit"]').value),
					credit: parseFormattedNumber(row.querySelector('input[id^="txtCredit"]').value),
					text: row.querySelector('input[id^="txtText"]').value,
					
					amountCC: amountCC || additionalData.amountCC || '',
					amountLC2: additionalData.amountLC2 || '',
					taxJurisdiction: additionalData.taxJurisdiction || '',
					profitCenter: additionalData.profitCenter || '',
					valueDate: additionalData.valueDate || '',
					houseBank: additionalData.houseBank || '',
					houseBankAccount: additionalData.houseBankAccount || '',
					assignment: additionalData.assignment || '',
					tradingPartner: additionalData.tradingPartner || '',
					segment: additionalData.segment || '',
					customer: additionalData.customer || '',
					product: additionalData.product || ''
				};
				lineItems.push(lineData);
			});
			
			document.getElementById('<%= hdnLineItemsData.ClientID %>').value = JSON.stringify(lineItems);
			document.getElementById('<%= hdnCompleteLineData.ClientID %>').value = JSON.stringify(completeLineData);
		}

		// Update line count
		function updateLineCount() {
			const count = document.querySelectorAll('#lineItemsBody tr').length;
			document.getElementById('<%= lblLineCount.ClientID %>').textContent = count;
		}

		// Load dynamic lines
		function loadDynamicLines() {
			const json = document.getElementById('<%= hdnLineItemsData.ClientID %>').value;
			if (!json || json === '') { 
				return; 
			}

			try {
				const items = JSON.parse(json);
				
				items.forEach((obj, index) => {
					addLineItem();
					const idx = lineCounter;
					
					const fields = [
						{ id: `txtGL${idx}`, value: obj.gl },
						{ id: `txtGLDesc${idx}`, value: obj.glDesc },
						{ id: `txtText${idx}`, value: obj.text },
						{ id: `txtDebit${idx}`, value: obj.debit ? formatNumber(obj.debit) : '' },
						{ id: `txtCredit${idx}`, value: obj.credit ? formatNumber(obj.credit) : '' },
						{ id: `txtCC${idx}`, value: obj.cc },
						{ id: `txtCCDesc${idx}`, value: obj.ccDesc },
						{ id: `txtWBS${idx}`, value: obj.wbs },
						{ id: `txtWBSDesc${idx}`, value: obj.wbsDesc },
						{ id: `txtCapMgr${idx}`, value: obj.capMgr, title: obj.capMgrDsc },
						{ id: `txtDelMgr${idx}`, value: obj.delMgr, title: obj.delMgrDsc},
						{ id: `txtKIC${idx}`, value: obj.kic, title: obj.kicDsc },
						{ id: `ddlFinTransType${idx}`, value: obj.finTransType },
						{ id: `txtOrder${idx}`, value: obj.order },
						{ id: `txtOrderDesc${idx}`, value: obj.orderDesc },
						{ id: `ddlTaxCode${idx}`, value: obj.taxCode },
						{ id: `txtIsCttAsset${idx}`, value: obj.isCttAsset },
						{ id: `txtIsCttInven${idx}`, value: obj.isCttInven },
						{ id: `txtTaxCat${idx}`, value: obj.taxCat }
					];
					
					fields.forEach(field => {
						const elem = document.getElementById(field.id);
						if (elem ) {
							elem.value = field.value || '';
							elem.title = field.title || '';
						}
					});
					
					updateFinTransTypeDropdown(idx);
					
					const cttElem = document.getElementById(`ddlFinTransType${idx}`);
					if (cttElem && obj.finTransType) {
						cttElem.value = obj.finTransType;
					}
					
					completeLineData[idx] = {
						amountCC: obj.amountCC || '',
						amountLC2: obj.amountLC2 || '',
						taxCode: obj.taxCode || '',
						taxJurisdiction: obj.taxJurisdiction || '',
						profitCenter: obj.profitCenter || '',
						order: obj.order || '',
						valueDate: obj.valueDate || '',
						houseBank: obj.houseBank || '',
						houseBankAccount: obj.houseBankAccount || '',
						assignment: obj.assignment || '',
						tradingPartner: obj.tradingPartner || '',
						segment: obj.segment || '',
						customer: obj.customer || '',
						product: obj.product || '',
						finTransType: obj.finTransType || ''
					};
					
					const hasAdditionalData = Object.values(completeLineData[idx]).some(val => val !== '');
					if (hasAdditionalData) {
						const lineNumCell = document.querySelector(`tr[data-line="${idx}"] .line-number`);
						if (lineNumCell) {
							lineNumCell.style.fontWeight = 'bold';
							lineNumCell.style.color = '#cf4520';
						}
					}

					const selectedCurrency = document.getElementById('<%= ddlCurrency.ClientID %>').value;
					if (selectedCurrency !== 'AUD' && obj.amountCC) {
						if (obj.debit) {
							const elem = document.getElementById(`txtDebitAUD${idx}`);
							if (elem) elem.value = formatNumber(obj.amountCC);
						} else if (obj.credit) {
							const elem = document.getElementById(`txtCreditAUD${idx}`);
							if (elem) elem.value = formatNumber(obj.amountCC);
						}
					}
				});
				
				updateCompleteLineData();
			} catch (e) {				
				showMessage('Error loading line items: ' + e.message, true);
				throw e;
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
			showLoading();
			setTimeout(function() {
				hideLoading();
			}, 3000);
			return true;
		}

		// Auto-upload when file is selected
		function autoUpload() {
			var fileInput = document.getElementById('<%= fuTemplate.ClientID %>');
			if (fileInput.files && fileInput.files.length > 0) {
				showLoading();
				document.getElementById('<%= btnUpload.ClientID %>').click();
			}
		}

		// Auto-upload spreadsheet when file is selected
		function autoUploadSpreadsheet() {
			var fileInput = document.getElementById('<%= fuSpreadsheet.ClientID %>');
			if (fileInput.files && fileInput.files.length > 0) {
				showLoading();
				document.getElementById('<%= btnUploadSpreadsheet.ClientID %>').click();
			}
		}
		
		// Show message
		function showMessage(message, isError) {
			try {
				const panel = document.getElementById(statusPanelId);
				const label = document.getElementById(statusLabelId);
				
				if (panel && label) {
					panel.style.animation = 'none';
					panel.offsetHeight;
					
					// Convert HTML breaks to actual line breaks for display
					const displayMessage = message.replace(/<br\s*\/?>/gi, '\n');
					label.textContent = displayMessage;
					
					panel.className = isError ? 'status-message status-error' : 'status-message status-success';
					panel.style.display = 'block';
					panel.style.animation = 'slideInRight 0.3s ease';
					
					// Fix close button position
					const closeBtn = panel.querySelector('.toast-close');
					if (closeBtn) {
						closeBtn.style.position = 'absolute';
						closeBtn.style.top = '8px';
						closeBtn.style.right = '8px';
					}
					
					setTimeout(() => {
						try {
							panel.style.animation = 'slideOutRight 0.3s ease';
							setTimeout(() => {
								panel.style.display = 'none';
							}, 300);
						} catch (e) {
							// Ignore animation errors
						}
					}, 15000);
				}
			} catch (e) {
				// Fallback to alert if message system fails
				alert((isError ? 'Error: ' : 'Success: ') + message.replace(/<br\s*\/?>/gi, '\n'));
			}
		}

		// Close toast
		function closeToast() {
			const panel = document.getElementById('<%= pnlStatus.ClientID %>');
			if (panel) {
				panel.style.animation = 'slideOutRight 0.3s ease';
				setTimeout(() => {
					panel.style.display = 'none';
				}, 300);
			}
		}
		
		// validate and display message(s) in messagetoast
		function validateJournal(onlyShowErrors) {			
			try {
			
				// First collect the line items data
				collectLineItemsData();	
				
				var errors = [];
				var lineErrors = [];
				var warnings = [];
				
				// Validate header fields
				if (!document.getElementById('<%= ddlDocumentType.ClientID %>').value)
					errors.push("Document Type is required");										
				if (!document.getElementById('<%= txtDocumentDate.ClientID %>').value)
					errors.push("Document Date is required");					
				if (!document.getElementById('<%= txtPostingDate.ClientID %>').value)
					errors.push("Posting Date is required");
				if (!document.getElementById('<%= txtJournalTitle.ClientID %>').value)
					errors.push("Journal Title is required");
				if (!document.getElementById('<%= ddlCompanyCode.ClientID %>').value)
					errors.push("Company Code is required");
				if (!document.getElementById('<%= ddlPostingPeriod.ClientID %>').value)
					errors.push("Posting Period is required");
				if (!document.getElementById('<%= ddlGroupId.ClientID %>').value)
					errors.push("Group ID is required");
				
				const dPostDate = new Date(Date.parse(document.getElementById('<%= txtPostingDate.ClientID %>').value));
				const dToday = new Date();
				const month = dToday.getMonth() + 1;
				let sCurPeriod = month >= 7 ? (month - 6) : month + 6;
				sCurPeriod = sCurPeriod.toString().padStart(2, '0')

				if(dPostDate.getMonth() != dToday.getMonth()){
					warnings.push("Posting Date is not in current period.");
				}

				if(sCurPeriod !== document.getElementById('<%= ddlPostingPeriod.ClientID %>').value){
					warnings.push("Posting Period is not in current period.");
				}

				
				// Check if non-AUD currency
				const selectedCurrency = document.getElementById('<%= ddlCurrency.ClientID %>').value;
				const isNonAUD = selectedCurrency !== 'AUD';
				
				// If non-AUD, check exchange rate
				if (isNonAUD) {
					const exchRate = document.getElementById('<%= txtExchangeRate.ClientID %>').value;
					if (!exchRate || exchRate === '1.00000' || parseFloat(exchRate) <= 0) {
						errors.push("Exchange Rate is required for non-AUD currencies");
					}
				}
				
				// Validate line items directly from the DOM
				var hasLineItems = false;
				var lineNum = 1;
				var totalDebit = 0;
				var totalCredit = 0;
				var totalDebitAUD = 0;
				var totalCreditAUD = 0;
				var hasAnyAUDAmounts = false;
				
				document.querySelectorAll('#lineItemsBody tr').forEach((row) => {				
					var lineNumber = row.getAttribute('data-line');
					var gl = document.getElementById('txtGL' + lineNumber).value.trim();
					var glDesc = document.getElementById('txtGLDesc' + lineNumber).value.trim();
					var text = document.getElementById('txtText' + lineNumber).value.trim();
					var debitStr = document.getElementById('txtDebit' + lineNumber).value;
					var creditStr = document.getElementById('txtCredit' + lineNumber).value;
					var debit = parseFormattedNumber(debitStr);
					var credit = parseFormattedNumber(creditStr);
					var taxCode = document.getElementById('ddlTaxCode' + lineNumber).value;
					var taxCategory = document.getElementById('txtTaxCat' + lineNumber).value;
					var cc = document.getElementById('txtCC' + lineNumber).value.trim();
					var ccDesc = document.getElementById('txtCCDesc' + lineNumber).value.trim();
					var wbs = document.getElementById('txtWBS' + lineNumber).value.trim();
					var wbsDesc = document.getElementById('txtWBSDesc' + lineNumber).value.trim();
					var order = document.getElementById('txtOrder' + lineNumber).value.trim();
					var orderDesc = document.getElementById('txtOrderDesc' + lineNumber).value.trim();
					
					// Only validate rows with an entry
					if (gl || text || debitStr || creditStr || taxCode || cc || wbs || order) {
						hasLineItems = true;
						if (!gl)
							lineErrors.push("Line " + lineNumber + ": GL Account is required.");
						// Required field validations
						if (!text)
							lineErrors.push("Line " + lineNumber + ": Line Item Text is required");
							
						// Must have either debit or credit
						if (!debit && !credit)
							lineErrors.push("Line " + lineNumber + ": Must have either debit or credit amount");
							
						// Cannot have both CC and WBS
						if (debit && credit)
							lineErrors.push("Line " + lineNumber + ": Cannot have both debit and credit");
						
						// Must have either CC or WBS
						if (!cc && !wbs)
							lineErrors.push("Line " + lineNumber + ": Must have either WBS Element or Cost Centre");
							
						// Cannot have both debit and credit
						if (cc && wbs)
							lineErrors.push("Line " + lineNumber + ": Cannot have both WBS Element and Cost Centre");

						// Validate lookups exist (have descriptions)
						if (gl && (!glDesc || glDesc === '' || glDesc === 'Looking up...'))
							lineErrors.push("Line " + lineNumber + ": GL Account " + gl + " is invalid or does not exist");
						
						if (cc && (!ccDesc || ccDesc === '' || ccDesc === 'Looking up...'))
							lineErrors.push("Line " + lineNumber + ": Cost Centre " + cc + " is invalid or does not exist");
						
						if (wbs && (!wbsDesc || wbsDesc === '' || wbsDesc === 'Looking up...'))
							lineErrors.push("Line " + lineNumber + ": WBS Element " + wbs + " is invalid or does not exist");
						
						if (order && (!orderDesc || orderDesc === '' || orderDesc === 'Looking up...'))
							lineErrors.push("Line " + lineNumber + ": Internal Order " + order + " is invalid or does not exist");
						
						// Validate tax code based on GL account
						if (taxCode) {
							
							// Check for GST control accounts (warning only)
							if (taxCategory === '<' || taxCategory === '>') {
								warnings.push("Line " + lineNumber + ": " + gl + " is a control account and should not be used directly");
							}
							// Check tax code compatibility
							else if (taxCategory === '+' && taxCode && !taxCode.startsWith('S')) {
								lineErrors.push("Line " + lineNumber + ": Only output tax codes (S*) allowed for account " + gl);
							}
							else if (taxCategory === '-' && taxCode && !taxCode.startsWith('P')) {
								lineErrors.push("Line " + lineNumber + ": Only input tax codes (P*) allowed for account " + gl);
							}
							else if (taxCategory && taxCategory !== '*' && taxCategory !== '+' && taxCategory !== '-' && taxCode && taxCategory !== taxCode) {
								lineErrors.push("Line " + lineNumber + ": Only tax code " + taxCategory + " allowed for account " + gl);
							}
						}
						
						// Track totals
						if (debit) totalDebit += parseFloat(debit) || 0;
						if (credit) totalCredit += parseFloat(credit) || 0;
						
						// If non-AUD, check AUD amounts if provided
						if (isNonAUD) {
							var debitAUDStr = document.getElementById('txtDebitAUD' + lineNumber).value;
							var creditAUDStr = document.getElementById('txtCreditAUD' + lineNumber).value;
							var debitAUD = parseFormattedNumber(debitAUDStr);
							var creditAUD = parseFormattedNumber(creditAUDStr);
							
							if (debitAUD) {
								totalDebitAUD += parseFloat(debitAUD) || 0;
								hasAnyAUDAmounts = true;
							}
							if (creditAUD) {
								totalCreditAUD += parseFloat(creditAUD) || 0;
								hasAnyAUDAmounts = true;
							}
						}
					}
					
					lineNum++;
				});
								
				if (!hasLineItems) {
					errors.push("At least one line item is required");
				}
												
				// Validate totals balance
				if (Math.abs(totalDebit - totalCredit) >= 0.01) {
					errors.push("Journal must balance (Total Debit: " + totalDebit.toFixed(2) + 
							   " must equal Total Credit: " + totalCredit.toFixed(2) + ")");
				}
										
				// If non-AUD AND some AUD amounts were provided, check they balance
				if (isNonAUD && hasAnyAUDAmounts && Math.abs(totalDebitAUD - totalCreditAUD) >= 0.01) {
					errors.push("AUD amounts must balance when provided (Total Debit AUD: " + 
							   totalDebitAUD.toFixed(2) + " must equal Total Credit AUD: " + 
							   totalCreditAUD.toFixed(2) + ")");
				}
												
				// Display results
				if (errors.length > 0 || lineErrors.length > 0) {
					var message = "Validation Errors:<br/>" + errors.join("<br/>") + "<br/>" +  lineErrors.join("<br/>");
					if (warnings.length > 0) {
						message += "<br/><br/>Warnings:<br/>" + warnings.join("<br/>");
					}					
					showMessage(message, true);
					return false;
				} else if (warnings.length > 0) {
					if(!onlyShowErrors){
						showMessage("Validation successful with warnings:<br/>" + warnings.join("<br/>") + 
							   "<br/><br/>Journal template is ready for download and upload to ERP.", false);
					}
					return true;
				} else {
					if(!onlyShowErrors){
						showMessage("Validation successful! Journal template is ready for download and upload to ERP.", false);
					}
					return true;
				}
								
			} catch (error) {
				showMessage('Validation error: ' + error.message, true);
				return false;
			}
			
			return false; // Prevent postback
		}

		// Prevent Enter key from submitting form
		document.addEventListener('keydown', function(e) {
			if (e.key === 'Enter' && e.target.tagName !== 'TEXTAREA') {
				const isButton = e.target.tagName === 'BUTTON' || 
								(e.target.tagName === 'INPUT' && e.target.type === 'button') ||
								(e.target.tagName === 'INPUT' && e.target.type === 'submit');
				
				if (!isButton) {
					e.preventDefault();
					
					// Move to next input field instead
					const inputs = Array.from(document.querySelectorAll('input:not([type="hidden"]), select, textarea'));
					const currentIndex = inputs.indexOf(e.target);
					if (currentIndex > -1 && currentIndex < inputs.length - 1) {
						const nextInput = inputs[currentIndex + 1];
						if (nextInput && !nextInput.readOnly && !nextInput.disabled) {
							nextInput.focus();
							if (nextInput.select) nextInput.select();
						}
					}
				}
			}
		});		
		
		// Show support dialog
		function showSupportOptions() {
			document.getElementById('supportDialog').style.display = 'block';
			document.getElementById('supportOverlay').style.display = 'block';
		}

		// Close support dialog
		function closeSupportDialog() {
			document.getElementById('supportDialog').style.display = 'none';
			document.getElementById('supportOverlay').style.display = 'none';
		}		
		
		// Handle paste from Excel
		document.getElementById('lineItemsTable').addEventListener('paste', function(e) {
			const target = e.target;
			
			if (!target.matches('input, select')) return;
			
			e.preventDefault();
			
			const pasteData = e.clipboardData.getData('text');
			const rows = pasteData.split('\n').filter(row => row.trim());
			const data = rows.map(row => row.split('\t'));
			
			const currentCell = target.closest('td');
			const currentRow = currentCell.closest('tr');
			const cellIndex = Array.from(currentRow.cells).indexOf(currentCell);
			const rowIndex = Array.from(document.querySelectorAll('#lineItemsBody tr')).indexOf(currentRow);
			
			data.forEach((rowData, i) => {
				const targetRowIndex = rowIndex + i;
				let targetRow = document.querySelectorAll('#lineItemsBody tr')[targetRowIndex];
				
				if (!targetRow && rowData.some(cell => cell.trim())) {
					addLineItem();
					targetRow = document.querySelectorAll('#lineItemsBody tr')[targetRowIndex];
				}
				
				if (targetRow) {
					rowData.forEach((cellData, j) => {
						const targetCellIndex = cellIndex + j;
						const targetCell = targetRow.cells[targetCellIndex];
						
						if (targetCell) {
							const input = targetCell.querySelector('input, select');
							if (input && !input.readOnly) {
								input.value = cellData.trim();
								
								if (input.id.includes('Debit') || input.id.includes('Credit')) {
									input.value = formatNumber(parseFormattedNumber(input.value));
								}								
								
								if (input.onchange) input.onchange();
								if (input.onblur) {
									const inputId = input.id;
									if (inputId.includes('txtGL')) {
										lookupDynamicField(inputId, 'gl', inputId.replace('txtGL', 'txtGLDesc'));
										// updateFinTransTypeDropdown(inputId.match(/\d+$/)[0]);
									} else if (inputId.includes('txtWBS')) {
										lookupDynamicField(inputId, 'wbs', inputId.replace('txtWBS', 'txtWBSDesc'));
									} else if (inputId.includes('txtCC')) {
										lookupDynamicField(inputId, 'cc', inputId.replace('txtCC', 'txtCCDesc'));
										const lineNum = inputId.match(/\d+$/)[0];
										copyToProfitCenter(lineNum);
									} else if (inputId.includes('txtOrder')) {
										lookupDynamicField(inputId, 'order', inputId.replace('txtOrder', 'txtOrderDesc'));
									}
								}
							}
						}
					});
				}
			});
			
			updateTotals();
			showMessage(CONFIG.MESSAGES.PASTE_SUCCESS.format(data.length), false);
		});
				
		// Copy all to Excel
		function copyAllToExcel() {
			const rows = [];
			
			document.querySelectorAll('#lineItemsBody tr').forEach(row => {
				const rowData = [];
				
				row.querySelectorAll('td').forEach((cell, index) => {
					if (index > 0 && index < row.cells.length - 1) {
						const input = cell.querySelector('input, select');
						if (input) {
							rowData.push(input.value);
						}
					}
				});
				
				if (rowData.some(cell => cell)) {
					rows.push(rowData.join('\t'));
				}
			});
			
			const textData = rows.join('\n');
			
			if (navigator.clipboard && navigator.clipboard.writeText) {
				navigator.clipboard.writeText(textData).then(() => {
					showMessage(CONFIG.MESSAGES.COPY_SUCCESS.format(rows.length), false);
				});
			} else {
				const textarea = document.createElement('textarea');
				textarea.value = textData;
				textarea.style.position = 'fixed';
				textarea.style.opacity = '0';
				document.body.appendChild(textarea);
				textarea.select();
				document.execCommand('copy');
				document.body.removeChild(textarea);
				showMessage(CONFIG.MESSAGES.COPY_SUCCESS.format(rows.length), false);
			}
		}

		// Show paste help
		function showPasteHelp() {
			alert(`To paste from Excel:
	1. Copy cells in Excel (Ctrl+C)
	2. Click on the cell where you want to start pasting
	3. Press Ctrl+V

	To copy to Excel:
	1. Click "Copy to Excel" button
	2. Open Excel and press Ctrl+V

	Tips:
	- New rows are added automatically when pasting
	- Descriptions are looked up automatically
	- Only editable fields will be updated`);
		}

		// Column resizer
		const ColumnResizer = {
			isResizing: false,
			currentColumn: null,
			startX: 0,
			startWidth: 0,
			minWidth: 50,
			
			init: function() {
				const headers = document.querySelectorAll('.line-items-table th');
				headers.forEach((header, index) => {
					if (index < headers.length - 1) {
						this.addResizeHandle(header);
					}
				});
				
				document.addEventListener('mousemove', this.handleMouseMove.bind(this));
				document.addEventListener('mouseup', this.handleMouseUp.bind(this));
				
				this.loadColumnWidths();
			},
			
			addResizeHandle: function(header) {
				const resizer = document.createElement('div');
				resizer.className = 'column-resizer';
				
				resizer.addEventListener('mousedown', (e) => this.handleMouseDown(e, header));
				
				header.style.position = 'relative';
				header.appendChild(resizer);
			},
			
			handleMouseDown: function(e, column) {
				this.isResizing = true;
				this.currentColumn = column;
				this.startX = e.pageX;
				this.startWidth = column.offsetWidth;
				
				document.body.style.cursor = 'col-resize';
				document.body.style.userSelect = 'none';
				
				e.preventDefault();
			},
			
			handleMouseMove: function(e) {
				if (!this.isResizing) return;
				
				const diff = e.pageX - this.startX;
				const newWidth = Math.max(this.minWidth, this.startWidth + diff);
				
				this.currentColumn.style.width = newWidth + 'px';
				this.currentColumn.style.minWidth = newWidth + 'px';
				this.currentColumn.style.maxWidth = newWidth + 'px';
				
				const columnIndex = Array.from(this.currentColumn.parentNode.children).indexOf(this.currentColumn);
				const rows = document.querySelectorAll('.line-items-table tr');
				
				rows.forEach(row => {
					const cell = row.cells[columnIndex];
					if (cell) {
						cell.style.width = newWidth + 'px';
						cell.style.minWidth = newWidth + 'px';
						cell.style.maxWidth = newWidth + 'px';
					}
				});
			},
			
			handleMouseUp: function() {
				if (!this.isResizing) return;
				
				this.isResizing = false;
				this.currentColumn = null;
				
				document.body.style.cursor = '';
				document.body.style.userSelect = '';
				
				this.saveColumnWidths();
			},
			
			saveColumnWidths: function() {
				const widths = {};
				const headers = document.querySelectorAll('.line-items-table th');
				
				headers.forEach((header, index) => {
					widths[index] = header.offsetWidth;
				});
				
				localStorage.setItem('ejet_column_widths', JSON.stringify(widths));
			},
			
			loadColumnWidths: function() {
				const saved = localStorage.getItem('ejet_column_widths');
				if (!saved) return;
				
				try {
					const widths = JSON.parse(saved);
					const headers = document.querySelectorAll('.line-items-table th');
					
					headers.forEach((header, index) => {
						if (widths[index]) {
							header.style.width = widths[index] + 'px';
							header.style.minWidth = widths[index] + 'px';
							header.style.maxWidth = widths[index] + 'px';
							
							const rows = document.querySelectorAll('.line-items-table tr');
							rows.forEach(row => {
								const cell = row.cells[index];
								if (cell) {
									cell.style.width = widths[index] + 'px';
									cell.style.minWidth = widths[index] + 'px';
									cell.style.maxWidth = widths[index] + 'px';
								}
							});
						}
					});
				} catch (e) {
					console.error('Failed to load column widths:', e);
				}
			},
			
			resetColumnWidths: function() {
				localStorage.removeItem('ejet_column_widths');
				document.querySelectorAll('.line-items-table th, .line-items-table td').forEach(cell => {
					cell.style.width = '';
					cell.style.minWidth = '';
					cell.style.maxWidth = '';
				});
			}
					
		};
	</script>
    
</body>
</html>

<script runat="server">
    
</script>