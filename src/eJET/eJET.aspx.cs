using System;
using System.IO;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Xml;
using System.Linq;
using Newtonsoft.Json;
using OfficeOpenXml;
using System.Web.UI.WebControls;

public partial class eJET : System.Web.UI.Page
{
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
	
	private static string ConnStr
    {
        get
        {
			return "File Name=" + HttpContext.Current.Server.MapPath("Database/EJET.udl") + ";";
        }
    }
    
	protected void Page_Load(object sender, EventArgs e)
	{
		try
		{
			// Add cache prevention headers
			Response.Cache.SetCacheability(HttpCacheability.NoCache);
			Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
			Response.Cache.SetNoStore();
			Response.AppendHeader("Pragma", "no-cache");

			if (!IsPostBack)
			{
				// Set default dates
				txtDocumentDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
				txtPostingDate.Text = DateTime.Today.ToString("yyyy-MM-dd");

				// Set AU fiscal period based on today's date
				int fiscalPeriod = GetAustralianFiscalPeriod(DateTime.Today);
				ddlPostingPeriod.SelectedValue = fiscalPeriod.ToString().PadLeft(2, '0');

				// Set current user (if available)
				if (HttpContext.Current.User.Identity.IsAuthenticated)
				{
					txtPreparedBy.Text = HttpContext.Current.User.Identity.Name;
				}

				// default reference
				txtJournalReference.Text = lblEJetReference.Text = "_eJET";

				// Load currencies
				LoadCurrencies();
				LoadGroups();
				LoadMasterData();
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Error initializing page: " + ex.Message, true);
		}
	}
    
    [System.Web.Services.WebMethod]
    public static string GetDescription(string code, string lookupType)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        
        string table = "", column = "", descField = "";
        switch (lookupType.ToLower())
        {
            case "gl":
				// For GL, return a JSON object with all fields
				using (OleDbConnection conn = new OleDbConnection(ConnStr))
                {
                    try
                    {
                        conn.Open();
                        string sql = "SELECT TXT50, IS_CTT_ASSET, IS_CTT_INVENTORY, TAX_CATEGORY FROM tblS4GLAccount WHERE SAKNR = ?";
                        using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("?", code);
                            using (OleDbDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    var glData = new
									{
										desc = reader["TXT50"].ToString(),
										isCttAsset = reader["IS_CTT_ASSET"].ToString(),
										isCttInven = reader["IS_CTT_INVENTORY"].ToString(),
										taxCat = reader["TAX_CATEGORY"].ToString()
                                    };
                                    return Newtonsoft.Json.JsonConvert.SerializeObject(glData);
                                }
                            }
                        }
                    }
                    catch
                    {
                        return "";
                    }
                }
                return "";
                return "";
            case "wbs":
                // For WBS, return a JSON object with all fields                
                using (OleDbConnection conn = new OleDbConnection(ConnStr))
                {
                    try
                    {
                        conn.Open();
                        string sql = "SELECT DESCRIPTION, DELIVERY_MGR, DELIVERY_MGR_NAME, CAPABILITY_MGR, CAPABILITY_MGR_DESC, KEYCATEGORY, CATEGORY FROM tblS4WBS WHERE WBS_ELEMENT = ?";
                        using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("?", code);
                            using (OleDbDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    var wbsData = new
									{
										desc = reader["DESCRIPTION"].ToString(),
										delMgr = reader["DELIVERY_MGR"].ToString(),
										delMgrDsc = reader["DELIVERY_MGR_NAME"].ToString(),
										capMgr = reader["CAPABILITY_MGR"].ToString(),
										capMgrDsc = reader["CAPABILITY_MGR_DESC"].ToString(),
										kic = reader["KEYCATEGORY"].ToString(),
										kicDsc = reader["CATEGORY"].ToString()
                                    };
                                    return Newtonsoft.Json.JsonConvert.SerializeObject(wbsData);
                                }
                            }
                        }
                    }
                    catch
                    {
                        return "";
                    }
                }
                return "";
            case "cc":
                table = "tblS4CostCentre";
                column = "COST_CENTRE";
                descField = "COST_CENTRE_DESCRIPTION";
                break;
            case "order":
                table = "tblS4InternalOrder";
                column = "ORDER_NUMBER";
                descField = "DESCRIPTION";
                break;
            default:
                return "";
        }
        
        // For non-WBS lookups
        using (OleDbConnection conn = new OleDbConnection(ConnStr))
        {
            try
            {
                conn.Open();
                string sql = string.Format("SELECT {0} FROM {1} WHERE {2} = ?", descField, table, column);
                using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("?", code);
                    object result = cmd.ExecuteScalar();
                    return result != null ? result.ToString()  : "";
                }
            }
            catch
            {
                return "";
            }
        }
    }
    
    protected void ValidateJournal_Click(object sender, EventArgs e)
    {
        try
        {
            List<string> errors = new List<string>();
            
            // Validate header fields
            if (string.IsNullOrEmpty(ddlDocumentType.SelectedValue))
                errors.Add("Document Type is required");
            if (string.IsNullOrEmpty(txtDocumentDate.Text))
                errors.Add("Document Date is required");
			if (string.IsNullOrEmpty(txtJournalTitle.Text))
                errors.Add("Journal Title is required");
            if (string.IsNullOrEmpty(txtPostingDate.Text))
                errors.Add("Posting Date is required");
            if (string.IsNullOrEmpty(ddlCompanyCode.SelectedValue))
                errors.Add("Company Code is required");
            if (string.IsNullOrEmpty(ddlPostingPeriod.SelectedValue))
                errors.Add("Posting Period is required");
            
            // Get dynamic line items
            var lineItems = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(hdnLineItemsData.Value))
            {
                try
                {
                    lineItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(hdnLineItemsData.Value);
                }
                catch { }
            }
            
            // Validate line items
            if (lineItems == null || lineItems.Count == 0)
            {
                errors.Add("At least one line item is required");
            }
            else
            {
				int lineNum = 1;
				foreach (var item in lineItems)
				{
					string gl = item.ContainsKey("gl") ? item["gl"] : "";
					string glDesc = item.ContainsKey("glDesc") ? item["glDesc"] : "";
					string text = item.ContainsKey("text") ? item["text"] : "";
					string debit = item.ContainsKey("debit") ? item["debit"] : "";
					string credit = item.ContainsKey("credit") ? item["credit"] : "";
					string cc = item.ContainsKey("cc") ? item["cc"] : "";
					string ccDesc = item.ContainsKey("ccDesc") ? item["ccDesc"] : "";
					string wbs = item.ContainsKey("wbs") ? item["wbs"] : "";
					string wbsDesc = item.ContainsKey("wbsDesc") ? item["wbsDesc"] : "";
					string order = item.ContainsKey("order") ? item["order"] : "";
					string orderDesc = item.ContainsKey("orderDesc") ? item["orderDesc"] : "";
					
					if (!string.IsNullOrEmpty(gl))
					{
						// Check if GL Account exists
						if (string.IsNullOrEmpty(glDesc))
							errors.Add("Line " + lineNum + ": GL Account " + gl + " is invalid or does not exist");
							
						if (string.IsNullOrEmpty(text))
							errors.Add("Line " + lineNum + ": Line Item Text is required");
						if (!string.IsNullOrEmpty(debit) && !string.IsNullOrEmpty(credit))
							errors.Add("Line " + lineNum + ": Cannot have both debit and credit");
							
						// Check if Cost Centre exists
						if (!string.IsNullOrEmpty(cc) && string.IsNullOrEmpty(ccDesc))
							errors.Add("Line " + lineNum + ": Cost Centre " + cc + " is invalid or does not exist");
							
						// Check if WBS Element exists
						if (!string.IsNullOrEmpty(wbs) && string.IsNullOrEmpty(wbsDesc))
							errors.Add("Line " + lineNum + ": WBS Element " + wbs + " is invalid or does not exist");
							
						// Check if Internal Order exists
						if (!string.IsNullOrEmpty(order) && string.IsNullOrEmpty(orderDesc))
							errors.Add("Line " + lineNum + ": Internal Order " + order + " is invalid or does not exist");
					}
					lineNum++;
				}
            }
            
            // Validate totals balance
            decimal totalDebit = GetTotalDebit();
            decimal totalCredit = GetTotalCredit();
            
            if (Math.Abs(totalDebit - totalCredit) >= 0.01M)
                errors.Add("Journal must balance (Total Debit must equal Total Credit)");
            
            if (errors.Count > 0)
            {
                ShowMessage("Validation Errors:<br/>" + string.Join("<br/>", errors), true);
            }
            else
            {
                ShowMessage("Validation successful! Journal is ready to be saved.", false);
            }
        }
        catch (Exception ex)
        {
            ShowMessage("Validation error: " + ex.Message, true);
        }
    }

	protected void Upload_Click(object sender, EventArgs e)
	{
		if (!fuTemplate.HasFile)
		{
			ShowMessage("Please choose the JournalEntry_Template.xlsx file first.", true);
			return;
		}

		try
		{
			using (var pkg = new ExcelPackage(fuTemplate.FileContent))
			{
				var ws = pkg.Workbook.Worksheets.FirstOrDefault();
				if (ws == null)
				{
					ShowMessage("Error: No worksheet found in the uploaded file.", true);
					return;
				}
				
				// Find the Header section and Line Items section
				int headerTechRow = 0;
				int lineItemsTechRow = 0;
				int headerDataRow = 0;
				int lineItemsDataRow = 0;
				
				// Search for "Header" and "Line Items" markers
				int maxRow = 30;
				int maxCol = 10;
				if (ws.Dimension != null)
				{
					maxRow = Math.Min(30, ws.Dimension.End.Row);
					maxCol = Math.Min(10, ws.Dimension.End.Column);
				}

				for (int row = 1; row <= maxRow; row++)
				{
					for (int col = 1; col <= maxCol; col++)
					{
						string cellValue = ws.Cells[row, col].Text.Trim();
						
						if (cellValue.Equals("Header", StringComparison.OrdinalIgnoreCase))
						{
							headerTechRow = row + 1; // Technical names row (BUKRS, BLART, etc.)
							headerDataRow = headerTechRow + 2; // Skip description row to get to data
						}
						else if (cellValue.Equals("Line Items", StringComparison.OrdinalIgnoreCase))
						{
							// Find technical names row (might be 1 or 2 rows after "Line Items" depending on blank row)
							for (int r = row + 1; r <= row + 3; r++)
							{
								if (ws.Cells[r, 2].Text.Trim() == "BUKRS" || 
									ws.Cells[r, 3].Text.Trim() == "SAKNR") // Look for key technical fields
								{
									lineItemsTechRow = r;
									lineItemsDataRow = lineItemsTechRow + 2; // Skip description row to get to data
									break;
								}
							}
						}
					}
					if (headerTechRow > 0 && lineItemsTechRow > 0) break;
				}
				
				if (headerTechRow == 0 || lineItemsTechRow == 0)
				{
					ShowMessage("Error: Could not find Header and Line Items sections in the template.", true);
					return;
				}
				
				// Map technical field names to column positions for header
				Dictionary<string, int> headerColumnMap = new Dictionary<string, int>();
				int headerMaxCol = 30;
				if (ws.Dimension != null)
				{
					headerMaxCol = ws.Dimension.End.Column;
				}
				for (int col = 1; col <= headerMaxCol; col++)
				{
					string techName = ws.Cells[headerTechRow, col].Text.Trim();
					if (!string.IsNullOrEmpty(techName))
					{
						headerColumnMap[techName] = col;
					}
				}
				
				// Map technical field names to column positions for line items
				Dictionary<string, int> lineColumnMap = new Dictionary<string, int>();
				int lineMaxCol = 30;
				if (ws.Dimension != null)
				{
					lineMaxCol = ws.Dimension.End.Column;
				}
				for (int col = 1; col <= lineMaxCol; col++)
				{
					string techName = ws.Cells[lineItemsTechRow, col].Text.Trim();
					if (!string.IsNullOrEmpty(techName))
					{
						lineColumnMap[techName] = col;
					}
				}
				
				// Read header data using technical field names
				string companyCode = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "BUKRS");
				if (ddlCompanyCode.Items.FindByValue(companyCode) != null)
				{
					ddlCompanyCode.SelectedValue = companyCode;
				}
				
				string docType = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "BLART");
				if (ddlDocumentType.Items.FindByValue(docType) != null)
				{
					ddlDocumentType.SelectedValue = docType;
				}
				
				txtDocumentDate.Text = ParseDate(GetTechFieldCell(ws, headerDataRow, headerColumnMap, "BLDAT"));
				txtPostingDate.Text = ParseDate(GetTechFieldCell(ws, headerDataRow, headerColumnMap, "BUDAT"));
				
				// Calculate AU fiscal period from posting date
				DateTime postingDate;
				if (DateTime.TryParse(txtPostingDate.Text, out postingDate))
				{
					int fiscalPeriod = GetAustralianFiscalPeriod(postingDate);
					ddlPostingPeriod.SelectedValue = fiscalPeriod.ToString().PadLeft(2, '0');
				}
				else
				{
					string period = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "MONAT");
					if (!string.IsNullOrEmpty(period))
					{
						ddlPostingPeriod.SelectedValue = period.PadLeft(2, '0');
					}
				}
				
				txtJournalTitle.Text = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "BKTXT");
				
				string currency = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "WAERS");
				if (ddlCurrency.Items.FindByValue(currency) != null)
				{
					ddlCurrency.SelectedValue = currency;
				}
				
				string exchRate = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "KURSF_EXT");
				txtExchangeRate.Text = string.IsNullOrEmpty(exchRate) ? "1.00000" : exchRate;				
				txtJournalReference.Text = GetTechFieldValue(ws, headerDataRow, headerColumnMap, "XBLNR");
				
				// Process line items
				var allLineItems = new List<Dictionary<string, string>>();
				int currentRow = lineItemsDataRow;
				int totalRows = 0;
				int lineItemsMaxRow = ws.Dimension.End.Row;

				
				while (currentRow <= lineItemsMaxRow && !string.IsNullOrWhiteSpace(GetTechFieldValue(ws, currentRow, lineColumnMap, "HKONT")))
				{
					totalRows++;
					
					var li = new Dictionary<string, string>
					{
						// Main fields using SAP technical names - with truncation
						{ "gl", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "HKONT"), 10) },      // GL Account
						{ "text", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "SGTXT"), 50) },   // Line Item Text
						{ "debit", GetTechFieldValue(ws, currentRow, lineColumnMap, "WRSOL") },
						{ "credit", GetTechFieldValue(ws, currentRow, lineColumnMap, "WRHAB") },
						{ "cc", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "KOSTL"), 10) },     // Cost Centre
						{ "wbs", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "PS_POSID"), 24) }, // WBS Element
						
						// Additional fields - with truncation
						{ "amountCC", GetTechFieldValue(ws, currentRow, lineColumnMap, "DMBTR") },
						{ "amountLC2", GetTechFieldValue(ws, currentRow, lineColumnMap, "DMBE2") },
						{ "taxCode", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "MWSKZ"), 2) },
						{ "taxJurisdiction", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "TXJCD"), 15) },
						{ "profitCenter", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "PRCTR"), 10) },
						{ "order", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "AUFNR"), 12) },
						{ "valueDate", ParseDate(GetTechFieldCell(ws, currentRow, lineColumnMap, "VALUT")) },
						{ "houseBank", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "HBKID"), 5) },
						{ "houseBankAccount", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "HKTID"), 5) },
						{ "assignment", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "ZUONR"), 18) },
						{ "tradingPartner", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "VBUND"), 6) },
						{ "segment", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "SEGMENT"), 10) },
						{ "customer", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "PROF_KNDNR"), 10) },  // Customer
						{ "product", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "PROF_ARTNR"), 40) },   // Product
						{ "finTransType", TruncateToLength(GetTechFieldValue(ws, currentRow, lineColumnMap, "RMVCT"), 3) },    // CTT
						
						// Lookup fields (to be populated later)
						{ "glDesc", "" },
						{ "wbsDesc", "" },
						{ "ccDesc", "" },
						{ "orderDesc", "" },
						{ "capMgr", "" },
						{ "capMgrDsc", "" },
						{ "delMgr", "" },
						{ "delMgrDsc", "" },
						{ "kic", "" },
						{ "kicDsc", "" }
					};
					
					allLineItems.Add(li);
					currentRow++;
				}

				// Store all line items for client-side processing
				hdnLineItemsData.Value = allLineItems.Count == 0
										 ? ""
										 : Newtonsoft.Json.JsonConvert.SerializeObject(allLineItems);

				// Perform bulk lookups for all uploaded data
				PerformBulkLookups();
				
				// Check for eJET tab and load if present
				var ejetSheet = pkg.Workbook.Worksheets["eJET"];
				if (ejetSheet != null)
				{
					// Read eJET specific fields
					for (int row = 2; row <= 10; row++)
					{
						string fieldName = ejetSheet.Cells[row, 1].Text.Trim();
						string fieldValue = ejetSheet.Cells[row, 2].Text.Trim();
						
						switch (fieldName)
						{
							case "Journal Description":
								txtJournalDescription.Text = fieldValue;
								break;
							case "Group ID":
								if (ddlGroupId.Items.FindByValue(fieldValue) != null)
									ddlGroupId.SelectedValue = fieldValue;
								break;
							case "Authorised By":
								txtAuthorisedBy.Text = fieldValue;
								break;
						}
					}
				}
				
				// Update client UI
				ScriptManager.RegisterStartupScript(
					this, 
					GetType(), 
					"repaint",
					@"setTimeout(function() {
						// Clear existing lines
						document.getElementById('lineItemsBody').innerHTML = '';
						lineCounter = 0;
						
						// Load all lines
						if(window.loadDynamicLines) { loadDynamicLines(); }
						if(window.updateLineCount) { updateLineCount(); }
						if(window.updateTotals) { updateTotals(); }
						if(window.updateEJetReference) { updateEJetReference(); }
						if(window.updateCurrencyHeaders) { updateCurrencyHeaders(); }
						var detailsTab = document.getElementById('detailsTabBtn');
						if(detailsTab) { detailsTab.click(); }
					}, 100);",
					true);

				ShowMessage("Template loaded successfully. Found " + totalRows + " line items.", false);
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Upload error: " + ex.Message, true);
		}
	}

	// Helper method to get value by SAP technical field name
	private string GetTechFieldValue(ExcelWorksheet ws, int row, Dictionary<string, int> columnMap, string techFieldName)
	{
		if (columnMap.ContainsKey(techFieldName))
		{
			return ws.Cells[row, columnMap[techFieldName]].Text.Trim();
		}
		return "";
	}

	// Helper method to get cell by SAP technical field name (for date parsing)
	private ExcelRangeBase GetTechFieldCell(ExcelWorksheet ws, int row, Dictionary<string, int> columnMap, string techFieldName)
	{
		if (columnMap.ContainsKey(techFieldName))
		{
			return ws.Cells[row, columnMap[techFieldName]];
		}
		return null;
	}
    
    private int GetAustralianFiscalPeriod(DateTime date)
    {
        // Australian fiscal year: July = Period 1, August = Period 2, etc.
        int month = date.Month;
        if (month >= 7)  // July to December
            return month - 6;
        else  // January to June
            return month + 6;
    }
    
    private string ParseDate(ExcelRangeBase cell)
    {
        if (cell == null) return "";
        
        // Try to get the underlying value first
        if (cell.Value != null)
        {
            // Direct DateTime
            if (cell.Value is DateTime)
            {
                return ((DateTime)cell.Value).ToString("yyyy-MM-dd");
            }
            
            // OLE Automation date (numeric)
            double numValue;
            if (double.TryParse(cell.Value.ToString(), out numValue))
            {
                if (numValue > 25569 && numValue < 2958466) // Valid Excel date range
                {
                    try
                    {
                        return DateTime.FromOADate(numValue).ToString("yyyy-MM-dd");
                    }
                    catch { }
                }
            }
        }
        
        // Fall back to text parsing
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
				// Process Header worksheet if it exists
				var headerSheet = pkg.Workbook.Worksheets["Header"] ?? pkg.Workbook.Worksheets["header"];
				if (headerSheet != null)
				{
					ProcessHeaderSheet(headerSheet);
				}

				// Process Line Items worksheet
				var lineItemsSheet = pkg.Workbook.Worksheets["Line Items"] ?? 
									pkg.Workbook.Worksheets["LineItems"] ?? 
									pkg.Workbook.Worksheets["line items"] ?? 
									pkg.Workbook.Worksheets["lineitems"];
				
				if (lineItemsSheet == null)
				{
					ShowMessage("Error: Could not find 'Line Items' worksheet in the uploaded file.", true);
					return;
				}

				// Clear existing data
				hdnLineItemsData.Value = "";
				hdnCompleteLineData.Value = "";

				// Build column map from row 1
				var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				int maxCol = 50;
				if (lineItemsSheet.Dimension != null && lineItemsSheet.Dimension.End != null)
				{
					maxCol = lineItemsSheet.Dimension.End.Column;
				}
				
				for (int col = 1; col <= maxCol; col++)
				{
					string header = lineItemsSheet.Cells[1, col].Text.Trim();
					if (!string.IsNullOrEmpty(header))
					{
						columnMap[header] = col;
					}
				}

				// Process line items starting from row 2
				var allLineItems = new List<Dictionary<string, string>>();
				int currentRow = 2;
				int maxRow = 1000;
				if (lineItemsSheet.Dimension != null && lineItemsSheet.Dimension.End != null)
				{
					maxRow = lineItemsSheet.Dimension.End.Row;
				}

				while (currentRow <= maxRow)
				{
					// Check if GL account exists (primary indicator of valid row)
					string glAccount = GetValueByHeaders(lineItemsSheet, currentRow, columnMap, 
						"GL Account", "G/L Account", "Account", "HKONT");
					
					if (string.IsNullOrWhiteSpace(glAccount))
					{
						currentRow++;
						continue;
					}

					// Create dictionary using traditional syntax
					var li = new Dictionary<string, string>();
					li.Add("gl", TruncateToLength(glAccount, 10));
					li.Add("text", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Line Item Text", "Text", "Description", "SGTXT"), 50));
					li.Add("debit", GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Debit", "Debit Amount", "Dr", "WRSOL"));
					li.Add("credit", GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Credit", "Credit Amount", "Cr", "WRHAB"));
					li.Add("cc", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Cost Centre", "Cost Center", "CC", "KOSTL"), 10));
					li.Add("wbs", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "WBS Element", "WBS", "Project", "PS_POSID"), 24));
					li.Add("order", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Order", "Internal Order", "Order Number", "AUFNR"), 12));
					li.Add("taxCode", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Tax Code", "Tax", "MWSKZ"), 2));
					li.Add("profitCenter", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Profit Center", "Profit Centre", "PC", "PRCTR"), 10));
					li.Add("assignment", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Assignment", "Reference", "ZUONR"), 18));
					li.Add("tradingPartner", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Trading Partner", "TP", "VBUND"), 6));
					li.Add("segment", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Segment", "SEGMENT"), 10));
					li.Add("finTransType", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "CTT", "Fin Trans Type", "RMVCT"), 3));
					li.Add("amountCC", GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Amount CC", "Company Code Amount", "DMBTR"));
					li.Add("amountLC2", GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Amount LC2", "Local Currency 2", "DMBE2"));
					li.Add("taxJurisdiction", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Tax Jurisdiction", "TXJCD"), 15));
					li.Add("valueDate", GetDateByHeaders(lineItemsSheet, currentRow, columnMap, "Value Date", "VALUT"));
					li.Add("houseBank", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "House Bank", "HBKID"), 5));
					li.Add("houseBankAccount", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "House Bank Account", "HKTID"), 5));
					li.Add("customer", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Customer", "PROF_KNDNR"), 10));
					li.Add("product", TruncateToLength(GetValueByHeaders(lineItemsSheet, currentRow, columnMap, "Product", "PROF_ARTNR"), 40));
					
					// Initialize lookup fields
					li.Add("glDesc", "");
					li.Add("wbsDesc", "");
					li.Add("ccDesc", "");
					li.Add("orderDesc", "");
					li.Add("capMgr", "");
					li.Add("capMgrDsc", "");
					li.Add("delMgr", "");
					li.Add("delMgrDsc", "");
					li.Add("kic", "");
					li.Add("kicDsc", "");

					allLineItems.Add(li);
					currentRow++;
				}

				if (allLineItems.Count == 0)
				{
					ShowMessage("No line items found in the spreadsheet.", true);
					return;
				}

				// Store line items
				hdnLineItemsData.Value = Newtonsoft.Json.JsonConvert.SerializeObject(allLineItems);

				// Perform bulk lookups
				PerformBulkLookups();

				// Update client UI
				ScriptManager.RegisterStartupScript(
					this,
					GetType(),
					"loadSpreadsheetData",
					@"setTimeout(function() {
						document.getElementById('lineItemsBody').innerHTML = '';
						lineCounter = 0;
						if(window.loadDynamicLines) { loadDynamicLines(); }
						if(window.updateLineCount) { updateLineCount(); }
						if(window.updateTotals) { updateTotals(); }
						if(window.updateCurrencyHeaders) { updateCurrencyHeaders(); }
						var lineItemsTab = document.querySelector('.modern-tab:nth-child(2)');
						if(lineItemsTab) { lineItemsTab.click(); }
					}, 100);",
					true);

				ShowMessage("Successfully loaded " + allLineItems.Count.ToString() + " line items from spreadsheet.", false);
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Error processing spreadsheet: " + ex.Message, true);
		}
	}

	// Helper method to process header sheet (row 1 = headers, row 2 = data)
	private void ProcessHeaderSheet(ExcelWorksheet ws)
	{
		if (ws.Dimension == null || ws.Dimension.End.Row < 2) return;

		// Build column map from row 1
		var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int col = 1; col <= ws.Dimension.End.Column; col++)
		{
			string header = ws.Cells[1, col].Text.Trim();
			if (!string.IsNullOrEmpty(header))
			{
				columnMap[header] = col;
			}
		}

		// Read values from row 2
		int dataRow = 2;
		
		// Company Code
		string companyCode = GetValueByHeaders(ws, dataRow, columnMap, "Company Code", "BUKRS");
		if (ddlCompanyCode.Items.FindByValue(companyCode) != null)
			ddlCompanyCode.SelectedValue = companyCode;

		// Document Type
		string docType = GetValueByHeaders(ws, dataRow, columnMap, "Document Type", "BLART");
		if (ddlDocumentType.Items.FindByValue(docType) != null)
			ddlDocumentType.SelectedValue = docType;

		// Dates
		txtDocumentDate.Text = GetDateByHeaders(ws, dataRow, columnMap, "Document Date", "BLDAT");
		txtPostingDate.Text = GetDateByHeaders(ws, dataRow, columnMap, "Posting Date", "BUDAT");
		
		// Update fiscal period based on posting date
		DateTime postingDate;
		if (DateTime.TryParse(txtPostingDate.Text, out postingDate))
		{
			int fiscalPeriod = GetAustralianFiscalPeriod(postingDate);
			ddlPostingPeriod.SelectedValue = fiscalPeriod.ToString().PadLeft(2, '0');
		}

		// Other fields
		txtJournalTitle.Text = GetValueByHeaders(ws, dataRow, columnMap, "Journal Title", "Header Text", "BKTXT");
		
		string currency = GetValueByHeaders(ws, dataRow, columnMap, "Currency", "WAERS");
		if (ddlCurrency.Items.FindByValue(currency) != null)
			ddlCurrency.SelectedValue = currency;
		
		string exchRate = GetValueByHeaders(ws, dataRow, columnMap, "Exchange Rate", "KURSF_EXT");
		txtExchangeRate.Text = string.IsNullOrEmpty(exchRate) ? "1.00000" : exchRate;
		
		txtJournalReference.Text = GetValueByHeaders(ws, dataRow, columnMap, "Reference", "Journal Reference", "XBLNR");
		txtJournalDescription.Text = GetValueByHeaders(ws, dataRow, columnMap, "Journal Description", "Description");
		
		string groupId = GetValueByHeaders(ws, dataRow, columnMap, "Group ID", "Group");
		if (ddlGroupId.Items.FindByValue(groupId) != null)
			ddlGroupId.SelectedValue = groupId;
		
		txtAuthorisedBy.Text = GetValueByHeaders(ws, dataRow, columnMap, "Authorised By", "Authorized By");
	}

	// Helper to get value by multiple possible headers
	private string GetValueByHeaders(ExcelWorksheet ws, int row, Dictionary<string, int> columnMap, params string[] possibleHeaders)
	{
		foreach (string header in possibleHeaders)
		{
			if (columnMap.ContainsKey(header))
			{
				return ws.Cells[row, columnMap[header]].Text.Trim();
			}
		}
		return "";
	}

	// Helper to get date value by multiple possible headers
	private string GetDateByHeaders(ExcelWorksheet ws, int row, Dictionary<string, int> columnMap, params string[] possibleHeaders)
	{
		foreach (string header in possibleHeaders)
		{
			if (columnMap.ContainsKey(header))
			{
				return ParseDate(ws.Cells[row, columnMap[header]]);
			}
		}
		return "";
	}
	
	// Helper to truncate string to maximum length
	private string TruncateToLength(string value, int maxLength)
	{
		if (string.IsNullOrEmpty(value))
			return value;
		
		return value.Length > maxLength ? value.Substring(0, maxLength) : value;
	}	
	
	protected void Download_Click(object sender, EventArgs e)
	{
		try
		{
			string templatePath = Server.MapPath("data/JournalEntry_Template.xlsx");
			
			if (!System.IO.File.Exists(templatePath))
			{
				ShowMessage("Template file not found. Please ensure JournalEntry_Template.xlsx is in the application folder.", true);
				return;
			}
			
			// Get line items
			var lineItems = new List<Dictionary<string, string>>();
			if (!string.IsNullOrEmpty(hdnLineItemsData.Value))
			{
				try
				{
					lineItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(hdnLineItemsData.Value);
				}
				catch { }
			}

			if (lineItems == null || lineItems.Count == 0)
			{
				ShowMessage("No line items to download.", true);
				return;
			}

			// Create a new package based on the template
			var templateFile = new FileInfo(templatePath);
			using (var pkg = new ExcelPackage(templateFile))
			{
				ExcelWorksheet ws = pkg.Workbook.Worksheets[1];
				
				// Calculate number of journals needed (999 lines max per journal)
				int totalLines = lineItems.Count;
				int journalsNeeded = (int)Math.Ceiling(totalLines / 999.0);
				
				// Store the original template structure
				// The template has header at row 11 and line items starting at row 17
				// with 2 placeholder line item rows (17-18)
				
				// First, clear everything after row 18 in the original template
				if (ws.Dimension != null && ws.Dimension.End.Row > 18)
				{
					ws.DeleteRow(19, ws.Dimension.End.Row - 18);
				}
				
				// Process each journal
				int currentExcelRow = 11; // Start with first header
				int lineItemIndex = 0;

				for (int journalNum = 0; journalNum < journalsNeeded; journalNum++)
				{
					// For journals after the first, we need to add space
					if (journalNum > 0)
					{
						// Add just 1 blank row for separation (not 2)
						ws.InsertRow(currentExcelRow, 1);
						currentExcelRow += 1;
						
						// Add new header section
						ws.InsertRow(currentExcelRow, 6); // Space for header marker + tech row + desc row + data row + blank rows
						
						// Add journal number in column A and "Header" label in column B
						ws.Cells[currentExcelRow, 1].Value = journalNum + 1; // This will be 2 for second journal
						ws.Cells[currentExcelRow, 2].Value = "Header";
						
						// Copy formatting from original header row (row 8) - extend to full width
						int lastColumn = 30; // Or use ws.Dimension.End.Column to get the actual last column
						ws.Cells[8, 1, 8, lastColumn].Copy(ws.Cells[currentExcelRow, 1, currentExcelRow, lastColumn]);
						
						// Re-set values after copy (since copy overwrites them)
						ws.Cells[currentExcelRow, 1].Value = journalNum + 1;
						ws.Cells[currentExcelRow, 2].Value = "Header";
						
						currentExcelRow++;
						
						// Copy technical field names from original header (row 9)
						ws.Cells[9, 1, 9, 30].Copy(ws.Cells[currentExcelRow, 1]);
						currentExcelRow++;
						
						// Copy field descriptions from original header (row 10)
						ws.Cells[10, 1, 10, 30].Copy(ws.Cells[currentExcelRow, 1]);
						currentExcelRow++;
						
						// This is where header data will go
						int headerDataRow = currentExcelRow;
						
						// Write header data
						ws.Cells[headerDataRow, 2].Value = ddlCompanyCode.SelectedValue;
						ws.Cells[headerDataRow, 3].Value = ddlDocumentType.SelectedValue;
						
						DateTime docDate, postDate;
						if (DateTime.TryParse(txtDocumentDate.Text, out docDate))
						{
							ws.Cells[headerDataRow, 4].Value = docDate;
							ws.Cells[headerDataRow, 4].Style.Numberformat.Format = "dd/MM/yyyy";
						}
						if (DateTime.TryParse(txtPostingDate.Text, out postDate))
						{
							ws.Cells[headerDataRow, 5].Value = postDate;
							ws.Cells[headerDataRow, 5].Style.Numberformat.Format = "dd/MM/yyyy";
						}
						
						ws.Cells[headerDataRow, 6].Value = ddlPostingPeriod.SelectedValue;
						ws.Cells[headerDataRow, 7].Value = txtJournalTitle.Text + " (" + (journalNum + 1) + "/" + journalsNeeded + ")";
						ws.Cells[headerDataRow, 8].Value = ddlCurrency.SelectedValue;
						ws.Cells[headerDataRow, 10].Value = txtExchangeRate.Text;
						ws.Cells[headerDataRow, 12].Value = txtJournalReference.Text + "_" + (journalNum + 1);
						
						currentExcelRow++;
						
						// Add blank row after header data
						currentExcelRow++;
						
						// Add "Line Items" section
						ws.InsertRow(currentExcelRow, 4); // Space for line items marker + transaction currency + tech row + desc row
						
						// Line Items row - column A empty, "Line Items" in column B
						ws.Cells[currentExcelRow, 2].Value = "Line Items";
						
						// Copy formatting from original line items row (row 13) - also extend to full width
						ws.Cells[13, 1, 13, lastColumn].Copy(ws.Cells[currentExcelRow, 1, currentExcelRow, lastColumn]);
						ws.Cells[currentExcelRow, 2].Value = "Line Items"; // Re-set value after copy
						
						currentExcelRow++;
						
						// Add "Transaction Currency" row
						ws.Cells[currentExcelRow, 5].Value = "Transaction Currency";
						ws.Cells[currentExcelRow, 6].Value = "Transaction Currency";
						
						// Copy formatting from original transaction currency row (row 14)
						ws.Cells[14, 5, 14, 6].Copy(ws.Cells[currentExcelRow, 5]);
						ws.Cells[currentExcelRow, 5].Value = "Transaction Currency"; // Re-set values after copy
						ws.Cells[currentExcelRow, 6].Value = "Transaction Currency";
						
						currentExcelRow++;
						
						// Copy technical field names from original line items (row 15)
						ws.Cells[15, 1, 15, 30].Copy(ws.Cells[currentExcelRow, 1]);
						currentExcelRow++;
						
						// Copy field descriptions from original line items (row 16)
						ws.Cells[16, 1, 16, 30].Copy(ws.Cells[currentExcelRow, 1]);
						currentExcelRow++;
					}
					else
					{
						// For first journal, just update the existing header
						ws.Cells["B11"].Value = ddlCompanyCode.SelectedValue;
						ws.Cells["C11"].Value = ddlDocumentType.SelectedValue;
						
						DateTime docDate, postDate;
						if (DateTime.TryParse(txtDocumentDate.Text, out docDate))
						{
							ws.Cells["D11"].Value = docDate;
							ws.Cells["D11"].Style.Numberformat.Format = "dd/MM/yyyy";
						}
						if (DateTime.TryParse(txtPostingDate.Text, out postDate))
						{
							ws.Cells["E11"].Value = postDate;
							ws.Cells["E11"].Style.Numberformat.Format = "dd/MM/yyyy";
						}
						
						ws.Cells["F11"].Value = ddlPostingPeriod.SelectedValue;
						ws.Cells["G11"].Value = journalsNeeded > 1 ? txtJournalTitle.Text + " (1/" + journalsNeeded + ")" : txtJournalTitle.Text;
						ws.Cells["H11"].Value = ddlCurrency.SelectedValue;
						ws.Cells["J11"].Value = txtExchangeRate.Text;
						ws.Cells["L11"].Value = journalsNeeded > 1 ? txtJournalReference.Text + "_1" : txtJournalReference.Text;
						
						currentExcelRow = 17; // Line items start here for first journal
					}
					
					// Calculate how many lines for this journal
					int linesInThisJournal = Math.Min(999, totalLines - lineItemIndex);
					
					// Delete the two placeholder rows if we have actual data
					if (journalNum == 0 && linesInThisJournal > 0)
					{
						ws.DeleteRow(17, 2); // Delete the two template rows
						currentExcelRow = 17; // Reset to where we'll insert
					}
					
					// Insert rows for all line items in this journal
					if (linesInThisJournal > 0)
					{
						ws.InsertRow(currentExcelRow, linesInThisJournal);
					}
					
					// Write line items for this journal
					int lineItemStartRow = currentExcelRow;
					for (int i = 0; i < linesInThisJournal && lineItemIndex < totalLines; i++, lineItemIndex++)
					{
						var item = lineItems[lineItemIndex];
						int xlRow = currentExcelRow;
						
						// Column B: Company Code
						ws.Cells[xlRow, 2].Value = ddlCompanyCode.SelectedValue;
						
						// Column C: G/L Account
						if (item.ContainsKey("gl") && !string.IsNullOrEmpty(item["gl"])) 
							ws.Cells[xlRow, 3].Value = item["gl"];
							
						// Column D: Item Text
						if (item.ContainsKey("text") && !string.IsNullOrEmpty(item["text"])) 
							ws.Cells[xlRow, 4].Value = item["text"];
							
						// Column E: Debit
						if (item.ContainsKey("debit") && !string.IsNullOrWhiteSpace(item["debit"])) 
						{
							decimal debitAmt;
							if (decimal.TryParse(item["debit"], out debitAmt) && debitAmt != 0)
								ws.Cells[xlRow, 5].Value = debitAmt;
						}
						
						// Column F: Credit
						if (item.ContainsKey("credit") && !string.IsNullOrWhiteSpace(item["credit"])) 
						{
							decimal creditAmt;
							if (decimal.TryParse(item["credit"], out creditAmt) && creditAmt != 0)
								ws.Cells[xlRow, 6].Value = creditAmt;
						}
						
						// Columns G-W: Additional fields
						if (item.ContainsKey("amountCC") && !string.IsNullOrEmpty(item["amountCC"])) 
							ws.Cells[xlRow, 7].Value = item["amountCC"];
						if (item.ContainsKey("amountLC2") && !string.IsNullOrEmpty(item["amountLC2"])) 
							ws.Cells[xlRow, 8].Value = item["amountLC2"];
						if (item.ContainsKey("taxCode") && !string.IsNullOrEmpty(item["taxCode"])) 
							ws.Cells[xlRow, 9].Value = item["taxCode"];
						if (item.ContainsKey("taxJurisdiction") && !string.IsNullOrEmpty(item["taxJurisdiction"])) 
							ws.Cells[xlRow, 10].Value = item["taxJurisdiction"];
						if (item.ContainsKey("cc") && !string.IsNullOrEmpty(item["cc"])) 
							ws.Cells[xlRow, 11].Value = item["cc"];
						if (item.ContainsKey("profitCenter") && !string.IsNullOrEmpty(item["profitCenter"])) 
							ws.Cells[xlRow, 12].Value = item["profitCenter"];
						if (item.ContainsKey("order") && !string.IsNullOrEmpty(item["order"])) 
							ws.Cells[xlRow, 13].Value = item["order"];
						if (item.ContainsKey("wbs") && !string.IsNullOrEmpty(item["wbs"])) 
							ws.Cells[xlRow, 14].Value = item["wbs"];
							
						// Value Date
						if (item.ContainsKey("valueDate") && !string.IsNullOrEmpty(item["valueDate"]))
						{
							DateTime valDate;
							if (DateTime.TryParse(item["valueDate"], out valDate))
							{
								ws.Cells[xlRow, 15].Value = valDate;
								ws.Cells[xlRow, 15].Style.Numberformat.Format = "dd/MM/yyyy";
							}
						}
						
						if (item.ContainsKey("houseBank") && !string.IsNullOrEmpty(item["houseBank"])) 
							ws.Cells[xlRow, 16].Value = item["houseBank"];
						if (item.ContainsKey("houseBankAccount") && !string.IsNullOrEmpty(item["houseBankAccount"])) 
							ws.Cells[xlRow, 17].Value = item["houseBankAccount"];
						if (item.ContainsKey("assignment") && !string.IsNullOrEmpty(item["assignment"])) 
							ws.Cells[xlRow, 18].Value = item["assignment"];
						if (item.ContainsKey("tradingPartner") && !string.IsNullOrEmpty(item["tradingPartner"])) 
							ws.Cells[xlRow, 19].Value = item["tradingPartner"];
						if (item.ContainsKey("segment") && !string.IsNullOrEmpty(item["segment"])) 
							ws.Cells[xlRow, 20].Value = item["segment"];
						if (item.ContainsKey("customer") && !string.IsNullOrEmpty(item["customer"])) 
							ws.Cells[xlRow, 21].Value = item["customer"];
						if (item.ContainsKey("product") && !string.IsNullOrEmpty(item["product"])) 
							ws.Cells[xlRow, 22].Value = item["product"];
						if (item.ContainsKey("finTransType") && !string.IsNullOrEmpty(item["finTransType"])) 
							ws.Cells[xlRow, 23].Value = item["finTransType"];
						
						currentExcelRow++;
					}
					
				}
				
				// Add eJET sheet
				var ejetSheet = pkg.Workbook.Worksheets.Add("eJET");

				// Add headers
				ejetSheet.Cells["A1"].Value = "Field";
				ejetSheet.Cells["B1"].Value = "Value";

				// Style headers
				ejetSheet.Cells["A1:B1"].Style.Font.Bold = true;
				ejetSheet.Cells["A1:B1"].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
				ejetSheet.Cells["A1:B1"].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
				ejetSheet.Cells["A1:B1"].Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;

				// Add data
				ejetSheet.Cells["A2"].Value = "Journal Description";
				ejetSheet.Cells["B2"].Value = txtJournalDescription.Text;
				ejetSheet.Cells["A3"].Value = "Group ID";
				ejetSheet.Cells["B3"].Value = ddlGroupId.SelectedValue;
				ejetSheet.Cells["A4"].Value = "Authorised By";
				ejetSheet.Cells["B4"].Value = txtAuthorisedBy.Text;
				ejetSheet.Cells["A5"].Value = "Total Line Items";
				ejetSheet.Cells["B5"].Value = totalLines.ToString();
				ejetSheet.Cells["A6"].Value = "Number of Journals";
				ejetSheet.Cells["B6"].Value = journalsNeeded.ToString();

				// Auto-fit columns
				ejetSheet.Cells[ejetSheet.Dimension.Address].AutoFitColumns();

				// Set minimum column widths
				if (ejetSheet.Column(1).Width < 20) ejetSheet.Column(1).Width = 20;
				if (ejetSheet.Column(2).Width < 30) ejetSheet.Column(2).Width = 30;
				
				// Save to memory stream
				var memStream = new MemoryStream();
				pkg.SaveAs(memStream);
				memStream.Position = 0;
				
				// Send file
				Response.Clear();
				Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
				string filename = journalsNeeded > 1 
					? "JournalEntry_" + journalsNeeded + "Journals_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx"
					: "JournalEntry_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx";
				Response.AddHeader("content-disposition", "attachment;filename=" + filename);
				Response.BinaryWrite(memStream.ToArray());
				Response.End();
			}
		}
		catch (Exception ex)
		{
			ShowMessage("Download error: " + ex.Message, true);
		}
	}

    // Bulk lookup method - Updated to use GL XML
    private Dictionary<string, Dictionary<string, string>> BulkLookupDescriptions(
        List<string> glCodes, List<string> ccCodes, List<string> wbsCodes, List<string> orderCodes)
    {
        var results = new Dictionary<string, Dictionary<string, string>>();
        
        // // Load GL descriptions from XML
        // string glXmlPath = Server.MapPath("~/data/gl-accounts.xml");
        // if (System.IO.File.Exists(glXmlPath))
        // {
        //     XmlDocument glDoc = new XmlDocument();
        //     glDoc.Load(glXmlPath);
            
        //     foreach (string glCode in glCodes)
        //     {
        //         XmlNode glNode = glDoc.SelectSingleNode("//gl_account[saknr='" + glCode + "']");
        //         if (glNode != null)
        //         {
        //             string key = "GL_" + glCode;
        //             if (!results.ContainsKey(key))
        //                 results[key] = new Dictionary<string, string>();
                    
        //             XmlNode txt50Node = glNode.SelectSingleNode("txt50");
        //             if (txt50Node != null)
        //                 results[key]["desc"] = txt50Node.InnerText;
        //         }
        //     }
        // }
        
        // For other lookups, use database
        
        using (OleDbConnection conn = new OleDbConnection(ConnStr))
        {
            conn.Open();

			if (glCodes.Count > 0)
			{
				string glList = string.Join(",", glCodes.Select(c => "'" + c.Replace("'", "''") + "'"));
				string glSql = string.Format("SELECT SAKNR, TXT50, IS_CTT_ASSET, IS_CTT_INVENTORY, TAX_CATEGORY FROM tblS4GLAccount WHERE SAKNR IN ({0})", glList);

				using (OleDbCommand cmd = new OleDbCommand(glSql, conn))
				using (OleDbDataReader reader = cmd.ExecuteReader())
				{
					while (reader.Read())
					{
						string key = "GL_" + reader["SAKNR"].ToString().Trim();
						if (!results.ContainsKey(key))
							results[key] = new Dictionary<string, string>();
						results[key]["desc"] = reader["TXT50"].ToString();
						results[key]["isCttAsset"] = reader["IS_CTT_ASSET"].ToString();
						results[key]["isCttInven"] = reader["IS_CTT_INVENTORY"].ToString();
						results[key]["taxCat"] = reader["TAX_CATEGORY"].ToString().Trim();
					}
				}
            }
			
            // Lookup WBS descriptions
			if (wbsCodes.Count > 0)
			{
				string wbsList = string.Join(",", wbsCodes.Select(w => "'" + w.Replace("'", "''") + "'"));
				string wbsSql = string.Format("SELECT WBS_Element, DESCRIPTION, DELIVERY_MGR, DELIVERY_MGR_NAME, CAPABILITY_MGR, CAPABILITY_MGR_DESC, KEYCATEGORY, CATEGORY FROM tblS4WBS WHERE WBS_Element IN ({0})", wbsList);

				using (OleDbCommand cmd = new OleDbCommand(wbsSql, conn))
				using (OleDbDataReader reader = cmd.ExecuteReader())
				{
					while (reader.Read())
					{
						string key = "WBS_" + reader["WBS_Element"].ToString();
						if (!results.ContainsKey(key))
							results[key] = new Dictionary<string, string>();

						results[key]["desc"] = reader["DESCRIPTION"].ToString();
						results[key]["capMgr"] = reader["DELIVERY_MGR"].ToString();
						results[key]["capMgrDsc"] = reader["DELIVERY_MGR_NAME"].ToString();
						results[key]["delMgr"] = reader["CAPABILITY_MGR"].ToString();
						results[key]["delMgrDsc"] = reader["CAPABILITY_MGR_DESC"].ToString();
						results[key]["kic"] = reader["KEYCATEGORY"].ToString();
						results[key]["kicDsc"] = reader["CATEGORY"].ToString();
					}
				}
			}
            
            // Lookup Cost Centre descriptions
            if (ccCodes.Count > 0)
            {
                string ccList = string.Join(",", ccCodes.Select(c => "'" + c.Replace("'", "''") + "'"));
                string ccSql = string.Format("SELECT COST_CENTRE, COST_CENTRE_DESCRIPTION FROM tblS4CostCentre WHERE COST_CENTRE IN ({0})", ccList);
                
                using (OleDbCommand cmd = new OleDbCommand(ccSql, conn))
                using (OleDbDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = "CC_" + reader["COST_CENTRE"].ToString();
                        if (!results.ContainsKey(key))
                            results[key] = new Dictionary<string, string>();
                        results[key]["desc"] = reader["COST_CENTRE_DESCRIPTION"].ToString();
                    }
                }
            }

            // Lookup Order descriptions
            if (orderCodes.Count > 0)
            {
                string orderList = string.Join(",", orderCodes.Select(o => "'" + o.Replace("'", "''") + "'"));
                string orderSql = string.Format("SELECT ORDER_NUMBER, DESCRIPTION FROM tblS4InternalOrder WHERE ORDER_NUMBER IN ({0})", orderList);
                
                using (OleDbCommand cmd = new OleDbCommand(orderSql, conn))
                using (OleDbDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = "ORDER_" + reader["ORDER_NUMBER"].ToString();
                        if (!results.ContainsKey(key))
                            results[key] = new Dictionary<string, string>();
                        results[key]["desc"] = reader["DESCRIPTION"].ToString();
                    }
                }
            }
        }
        
        return results;
    }

    private void PerformBulkLookups()
    {
        var glCodes = new List<string>();
        var ccCodes = new List<string>();
        var wbsCodes = new List<string>();
        var orderCodes = new List<string>();
        
        // Collect from dynamic rows
        if (!string.IsNullOrEmpty(hdnLineItemsData.Value))
        {
            try
            {
                var dynamicItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(hdnLineItemsData.Value);
                if (dynamicItems != null)
                {
                    foreach (var item in dynamicItems)
                    {
                        if (item.ContainsKey("gl") && !string.IsNullOrEmpty(item["gl"])) 
                            glCodes.Add(item["gl"]);
                        if (item.ContainsKey("cc") && !string.IsNullOrEmpty(item["cc"])) 
                            ccCodes.Add(item["cc"]);
                        if (item.ContainsKey("wbs") && !string.IsNullOrEmpty(item["wbs"])) 
                            wbsCodes.Add(item["wbs"]);
                        if (item.ContainsKey("order") && !string.IsNullOrEmpty(item["order"]))
                            orderCodes.Add(item["order"]);
                    }
                }
            }
            catch { }
        }
        
        // Remove duplicates
        glCodes = glCodes.Distinct().ToList();
        ccCodes = ccCodes.Distinct().ToList();
        wbsCodes = wbsCodes.Distinct().ToList();
        orderCodes = orderCodes.Distinct().ToList();
        
        // Perform bulk lookup
        var lookupResults = BulkLookupDescriptions(glCodes, ccCodes, wbsCodes, orderCodes);
        
        // Update dynamic rows with lookup results
        if (!string.IsNullOrEmpty(hdnLineItemsData.Value))
        {
            try
            {
                var dynamicItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(hdnLineItemsData.Value);
                if (dynamicItems != null)
                {
					foreach (var item in dynamicItems)
					{
						if (item.ContainsKey("gl") && lookupResults.ContainsKey("GL_" + item["gl"]))
						{
							item["glDesc"] = lookupResults["GL_" + item["gl"]]["desc"];
							item["isCttAsset"] = lookupResults["GL_" + item["gl"]]["isCttAsset"];
							item["isCttInven"] = lookupResults["GL_" + item["gl"]]["isCttInven"];
							item["taxCat"] = lookupResults["GL_" + item["gl"]]["taxCat"];
						}

						if (item.ContainsKey("wbs") && lookupResults.ContainsKey("WBS_" + item["wbs"]))
						{
							var wbsData = lookupResults["WBS_" + item["wbs"]];
							item["wbsDesc"] = wbsData["desc"];
							item["capMgr"] = wbsData["capMgr"];
							item["capMgrDsc"] = wbsData["capMgrDsc"];
							item["delMgr"] = wbsData["delMgr"];
							item["delMgrDsc"] = wbsData["delMgrDsc"];
							item["kic"] = wbsData["kic"];
							item["kicDsc"] = wbsData["kicDsc"];
						}
                        
                        if (item.ContainsKey("cc") && lookupResults.ContainsKey("CC_" + item["cc"]))
                            item["ccDesc"] = lookupResults["CC_" + item["cc"]]["desc"];
                            
                        if (item.ContainsKey("order") && lookupResults.ContainsKey("ORDER_" + item["order"]))
                            item["orderDesc"] = lookupResults["ORDER_" + item["order"]]["desc"];
                    }
                    
                    // Save back the updated data
                    hdnLineItemsData.Value = Newtonsoft.Json.JsonConvert.SerializeObject(dynamicItems);
                }
            }
            catch { }
        }
    }
    
    private decimal GetTotalDebit()
    {
        decimal total = 0;
        
        if (!string.IsNullOrEmpty(hdnLineItemsData.Value))
        {
            try
            {
                var lineItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(hdnLineItemsData.Value);
                if (lineItems != null)
                {
                    foreach (var item in lineItems)
                    {
                        if (item.ContainsKey("debit") && !string.IsNullOrEmpty(item["debit"]))
                        {
                            decimal value;
                            if (decimal.TryParse(item["debit"], out value))
                                total += value;
                        }
                    }
                }
            }
            catch { }
        }
        
        return total;
    }

    private decimal GetTotalCredit()
    {
        decimal total = 0;
        
        if (!string.IsNullOrEmpty(hdnLineItemsData.Value))
        {
            try
            {
                var lineItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(hdnLineItemsData.Value);
                if (lineItems != null)
                {
                    foreach (var item in lineItems)
                    {
                        if (item.ContainsKey("credit") && !string.IsNullOrEmpty(item["credit"]))
                        {
                            decimal value;
                            if (decimal.TryParse(item["credit"], out value))
                                total += value;
                        }
                    }
                }
            }
            catch { }
        }
        
        return total;
    }

	private void LoadGroups()
	{
		using (OleDbConnection conn = new OleDbConnection(ConnStr))
		{
			
				conn.Open();
				string sql = "SELECT CODE, DESCRIPTION FROM tblGroup WHERE IS_INACTIVE != 1 ORDER BY CODE";
				using (OleDbCommand cmd = new OleDbCommand(sql, conn))
				{
					using (OleDbDataReader reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							ddlGroupId.Items.Add(new ListItem(reader["DESCRIPTION"].ToString(), reader["CODE"].ToString()));
						}
					}
				}
			
		}
	}

	[System.Web.Services.WebMethod]
    public static string LoadMasterData()
	{
		var masterData = new Dictionary<string, List<Dictionary<string, string>>>();
		var nameValuePairs = new List<Dictionary<string, string>>();
		using (OleDbConnection conn = new OleDbConnection(ConnStr))
		{
			conn.Open();
			string sql = "SELECT CODE, DESCRIPTION FROM tblCttAsset ORDER BY CODE";
			using (OleDbCommand cmd = new OleDbCommand(sql, conn))
			{
				using (OleDbDataReader reader = cmd.ExecuteReader())
				{
					nameValuePairs = new List<Dictionary<string, string>>();
					while (reader.Read())
					{
						nameValuePairs.Add(new Dictionary<string, string>
						{
							{ "code", reader["CODE"].ToString() },
							{ "description", reader["CODE"].ToString() + " (" + reader["DESCRIPTION"].ToString() + ")" }
						});
					}

					masterData["assetCTTs"] = nameValuePairs;
				}
			}

		}

		using (OleDbConnection conn = new OleDbConnection(ConnStr))
		{
			conn.Open();
			string sql = "SELECT CODE, DESCRIPTION FROM tblCttInventory ORDER BY CODE";
			using (OleDbCommand cmd = new OleDbCommand(sql, conn))
			{
				using (OleDbDataReader reader = cmd.ExecuteReader())
				{
					nameValuePairs = new List<Dictionary<string, string>>();
					while (reader.Read())
					{
						nameValuePairs.Add(new Dictionary<string, string>
						{
							{ "code", reader["CODE"].ToString() },
							{ "description", reader["CODE"].ToString() + " (" + reader["DESCRIPTION"].ToString() + ")" }
						});
					}

					masterData["inventoryCTTs"] = nameValuePairs;
				}
			}
		}

		using (OleDbConnection conn = new OleDbConnection(ConnStr))
		{
			conn.Open();
			string sql = "SELECT MWSKZ, TAX_CODE FROM tblTaxCode WHERE INACTIVE != 'YES' ORDER BY MWSKZ ";
			using (OleDbCommand cmd = new OleDbCommand(sql, conn))
			{
				using (OleDbDataReader reader = cmd.ExecuteReader())
				{
					nameValuePairs = new List<Dictionary<string, string>>();
					while (reader.Read())
					{
						nameValuePairs.Add(new Dictionary<string, string>
						{
							{ "code", reader["MWSKZ"].ToString() },
							{ "description", reader["TAX_CODE"].ToString() }
						});
					}

					masterData["taxCodes"] = nameValuePairs;
				}
			}
		}

		return Newtonsoft.Json.JsonConvert.SerializeObject(masterData);
	}

	private void LoadCurrencies()
	{
		using (OleDbConnection conn = new OleDbConnection(ConnStr))
		{
			try
			{
				conn.Open();
				string sql = "SELECT WAERS, CURRENCY FROM tblCurrency ORDER BY PRIORITY desc, CURRENCY";
				using (OleDbCommand cmd = new OleDbCommand(sql, conn))
				{
					using (OleDbDataReader reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							ddlCurrency.Items.Add(new ListItem(reader["CURRENCY"].ToString(), reader["WAERS"].ToString()));
						}
					}
				}
			}
			catch
			{
				LoadDefaultCurrencies();
			}
		}

		// Set default to AUD
		if (ddlCurrency.Items.FindByValue("AUD") != null)
			ddlCurrency.SelectedValue = "AUD";
	}

    private void LoadDefaultCurrencies()
    {
        ddlCurrency.Items.Clear();
        ddlCurrency.Items.Add(new ListItem("AUD (Australian dollar)", "AUD"));
        ddlCurrency.Items.Add(new ListItem("USD (American dollar)", "USD"));
    }
    
    private void ShowMessage(string message, bool isError)
    {
        pnlStatus.Visible = true;
        lblStatus.Text = message;
        pnlStatus.CssClass = isError ? "status-message status-error" : "status-message status-success";
        pnlStatus.Style["display"] = "block";
        
        // Add JavaScript to auto-hide the message after 3 seconds
        string script = @"
            setTimeout(function() {
                var panel = document.getElementById('" + pnlStatus.ClientID + @"');
                if (panel) {
                    panel.style.animation = 'slideOutRight 0.3s ease';
                    setTimeout(function() {
                        panel.style.display = 'none';
                    }, 300);
                }
            }, 15000);";
        
        // Fixed: Added missing parameter for script type
        ScriptManager.RegisterStartupScript(
			this, 
            this.GetType(), 
            "AutoHideMessage" + DateTime.Now.Ticks, // Unique key
            script, 
            true
        );
        
        // Update totals display
        lblTotalDebit.Text = GetTotalDebit().ToString("0.00");
        lblTotalCredit.Text = GetTotalCredit().ToString("0.00");
    }
}