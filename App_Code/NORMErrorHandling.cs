using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

/// <summary>NORM response boundary only. No public diagnostic/audit-writing API.</summary>
public static class NORMErrorHandling
{
    public const string GenericMessage = "NORM could not complete this operation. Reload and try again, or contact support.";
    private const string PdfMessage = "NORM could not read the PDF. Check the file and selected start page, then try again.";
    private static readonly HashSet<string> BusinessMessages = new HashSet<string>(StringComparer.Ordinal)
    {
        "The selected configuration release contains no account mappings.",
        "The accepted source changed after inspection. Inspect the batch again.",
        "The staged batch population set changed. Inspect the complete batch again.",
        "Choose a supported stream filter.",
        "Select the complete company, optional ledger and stream identity.",
        "The source does not belong to the selected agency/year context.",
        // Closed catalogue of reviewed application validation literals; no runtime discovery.
        "A Defence ERP source is required.",
        "A source detail row was assigned more than once. Inspect again.",
        "A source outside the configured current financial year must cover the complete financial year.",
        "A source population has no resolved context. Inspect again.",
        "A source population is invalid. No population from the batch can be accepted.",
        "A valid document identifier is required.",
        "Acknowledge the mapping warnings before approval.",
        "ActewAGL columns do not match the source reporting-year metadata.",
        "ActewAGL requires four distinct mapped columns.",
        "ActewAGL year/month metadata conflicts with the selected financial year or supported June annual layout.",
        "All selected configuration releases must belong to the same agency.",
        "Ambiguous ERP workbook: more than one supported layout or data header.",
        "Amount totals exceed the supported decimal range.",
        "Amounts must fit existing storage without precision loss.",
        "An active entity and approved configuration for the selected financial year are required.",
        "An inspected population is missing. Inspect again.",
        "An upload batch must belong to one agency. Cross-agency batches are not supported.",
        "Approve the mapping release before recalculating.",
        "Choose a non-empty ERP workbook.",
        "Choose a non-empty Excel mapping workbook.",
        "Choose a non-empty source document.",
        "Choose a source document.",
        "Choose non-empty trial balance files.",
        "Choose the completed mapping workbook.",
        "Configuration changed before acceptance. Inspect again.",
        "Confirmed replacement is not available through this entry point. Use the import preview.",
        "Conflicting Icon Water company range.",
        "Conflicting Icon Water company.",
        "Conflicting Icon Water ledger.",
        "Current-year column only; prior-year column retained in original. Controls are calculated; no report control totals supplied.",
        "Defence ERP account detail, company subtotal and grand total are required.",
        "Designate a current financial year through Year Setup first.",
        "Duplicate or misplaced company subtotal.",
        "Duplicate or misplaced grand total.",
        "Enter a change reason of between 10 and 500 characters.",
        "Enter a reporting name of 1 to 200 characters without control characters.",
        "Enter a valid PDF start page between 1 and 9999.",
        "Enter a version of no more than 30 characters, for example v1.1.",
        "Enter the financial year as exactly four digits, for example 2025.",
        "Enter the PDF page where the financial-statement tables commence.",
        "Financial detail before the recognised ERP report header cannot be excluded as metadata.",
        "FY2025 requires the controlled ROMAN periods 01-10 and ERP periods 11-12 source pair.",
        "Icon Water MAR-16 report requires an FY2016 configuration.",
        "Import not accepted: validation evidence could not be saved. No calculation was created; contact support.",
        "Incomplete ActewAGL CSV.",
        "Inconsistent Icon Water page count.",
        "Inspect the source files before confirming acceptance.",
        "Install the start-of-year database objects first.",
        "Invalid ActewAGL company or financial-year metadata.",
        "Invalid ActewAGL preamble spacer.",
        "Invalid Icon Water final controls.",
        "Invalid or incomplete Icon Water page heading.",
        "Legacy ERP inspection preserves the historical company-in-SourceLedger contract; actual ledger identity is not established.",
        "Missing Icon Water final controls or incomplete pages.",
        "More than one file supplies the same agency, financial year, company, ledger and reporting stream. Select one authoritative file per source scope.",
        "No approved configuration is available. Import a trial balance before preparing its mapping workbook.",
        "No classified accepted original population was found for this import. Historical unknown identities are not inferred.",
        "No mapping changes have been recorded in this draft.",
        "No mapping changes were found in the workbook.",
        "No prior controlled trial balance is available to recalculate. Import a trial balance against this release instead.",
        "No ROMAN rows matched the configured source ledgers.",
        "No rows matched the configured source ledgers for this reporting entity.",
        "No trial balance detail records.",
        "Not every source detail row and population is accounted for. Inspect the complete file again.",
        "Only a draft configuration release can be changed.",
        "Population detail row count does not match its source rows. Inspect again.",
        "Reload the selected entity before saving.",
        "Resolve all mapping validation errors before approval.",
        "Retained accounts are not unique within this population; comparison is ambiguous.",
        "Save an explicit current financial year for this entity in Year Setup before importing.",
        "Select a classified source population.",
        "Select a saved financial-year setup before uploading source documents.",
        "Select an accepted source import.",
        "Select an approved configuration release.",
        "The selected files or agency changed. Inspect the batch again.",
        "Approve the annual source configuration before importing.",
        "NIHRACS requires approved annual source configuration.",
        "Select an approved release to use as the starting point.",
        "Select an available approved configuration release.",
        "Select and load a saved year setup first.",
        "Select Departmental, Administered or all reporting streams.",
        "Select ERP or ROMAN as the source format.",
        "Select only one approved configuration release per financial year. Change the primary selection for its year.",
        "Source amounts or totals exceed the supported decimal range.",
        "Source detail and excluded record counts do not reconcile. No population can be accepted.",
        "Source ledger: Icon Water Limited Ledger; canonical ledger/stream resolved by approved company configuration. Source credits are positive; stored movements use signed credits.",
        "Source not accepted. Rejection audit evidence could not be saved; contact support.",
        "Source record accounting is unavailable. Inspect again.",
        "That version already exists for this entity and financial year.",
        "The approved configuration changed between files. Inspect the batch again.",
        "The approved configuration is no longer available.",
        "The combined files exceed the configured upload limit.",
        "The complete original file population set or validation changed. Inspect every population again.",
        "The configuration release was not found.",
        "The configuration, source identity or validation changed. Inspect again.",
        "The current designation changed. Reload Year Setup before saving.",
        "The current year designation is ambiguous.",
        "The Defence ERP company is not authorised for inspection.",
        "The Defence ERP company or consistent Ledger/Source Ledger identity is missing.",
        "The Defence ERP financial year does not match the expected financial year.",
        "The Defence ERP fiscal year is missing or invalid.",
        "The Defence ERP import requires unscaled dollar units; thousands or other scales are unsupported.",
        "The Defence ERP import supports Australian dollars (AUD) only.",
        "The Defence ERP layout is available for parsing inspection only through this route. Inspect the file and explicitly confirm acceptance.",
        "The Defence ERP trial balance must sum to exactly zero.",
        "The detected company/ledger pair and reporting stream are not authorised.",
        "The document's expanded content is too large to extract safely.",
        "The draft contains no recorded mapping changes.",
        "The entity is not configured for the selected year.",
        "The ERP trial balance does not contain valid posting-date boundaries.",
        "The ERP workbook contains conflicting fiscal-year labels.",
        "The inspected source bytes changed. Inspect again.",
        "The legacy ERP data must be on the first worksheet.",
        "The mapping workbook must be an .xlsx file.",
        "The recorded reporting year is unavailable.",
        "The release contains no account mappings.",
        "The reporting name changed. Reload Year Setup before saving.",
        "The retained original failed its integrity check.",
        "The retained source identity, rows or confirmation context changed. Inspect again.",
        "The ROMAN and ERP reporting periods overlap. No period may be supplied by both files.",
        "The ROMAN trial balance contains an invalid reporting-period range.",
        "The ROMAN trial balance contains conflicting reporting-period headers.",
        "The ROMAN trial balance does not identify its financial year and reporting periods.",
        "The selected configuration has no source ledgers.",
        "The selected files or configuration changed. Inspect the batch again.",
        "The selected population does not belong to this accepted import.",
        "The selected release does not match the source agency and financial year. Inspect again.",
        "The selected version is not an accepted original of this population.",
        "The selected year setup is unavailable.",
        "The source company has ambiguous configured ledger/reporting stream populations.",
        "The source company is not authorised by the active agency configuration.",
        "The source document changed. Reload before uploading a replacement.",
        "The source document exceeds the configured upload limit.",
        "The source does not establish exact reporting-period boundaries. Numeric period labels alone cannot establish coverage.",
        "The source filename cannot be retained within the supported audit contract.",
        "The source filename cannot be retained within the supported storage limit.",
        "The source import was not found.",
        "The source ledger exceeds the supported identity length.",
        "The source population is ambiguous. Supply reporting stream evidence.",
        "The source predecessor context is incomplete. Inspect again.",
        "The source reporting stream is not recognised.",
        "The source reporting-period boundaries must be ordered and within the identified financial year.",
        "The staged batch population set changed. Inspect the complete batch again.",
        "The test-break amount must be non-zero.",
        "The trial balance contains an invalid fiscal-year value.",
        "The trial balance contains conflicting fiscal-year values.",
        "The trial balance does not identify its fiscal year.",
        "The trial balance workbook does not contain a worksheet.",
        "The trial balance worksheet is empty.",
        "The two-file ROMAN/ERP transition is available only for FY2025.",
        "The Word document XML is too large to extract safely.",
        "The workbook does not contain the expected trial-balance columns.",
        "The workbook does not contain the required Mappings sheet.",
        "The year setup or approved configuration changed. Reload before uploading.",
        "There is a gap between the ROMAN and ERP reporting periods.",
        "This import contains several populations. Select its company, optional ledger and stream explicitly.",
        "This is an older mapping workbook layout. Download a fresh workbook for this draft before making changes.",
        "This preview expired, was cancelled or has already been used. Check existing imports before inspecting again.",
        "This preview is invalid or expired. Inspect the batch again.",
        "This preview is invalid or expired. Inspect the files again.",
        "This preview is invalid, expired or already consumed. Inspect again.",
        "This preview is invalid, expired or already consumed. Inspect the files again.",
        "This source file contains multiple populations. Use file inspection to review every population; acceptance is not yet enabled.",
        "This workbook belongs to a different configuration release. Download a fresh workbook for this draft.",
        "Unclosed CSV quoted field.",
        "Unknown source-document type.",
        "Unrecognised financial detail on an additional legacy worksheet cannot be excluded from row reconciliation.",
        "Unsupported binary source layout.",
        "Unsupported ERP content: expected an XLSX workbook.",
        "Unsupported or malformed ERP layout; no unambiguous supported report signature.",
        "Unsupported source layout. Expected Defence XLSX, ActewAGL FinStats CSV or Icon Water fixed-width text.",
        "Unsupported text encoding: expected UTF-8 or ASCII.",
        "Upload a PDF, Word document or Excel workbook.",
    };
    private static readonly string[] BusinessPatterns = {
        @"\A(?:Debits and credits must differ by exactly zero AUD\. Actual: |Trial balance must sum to exactly zero AUD; actual )-?[0-9]+(?:\.[0-9]+)?\.\z",
        @"\A(?:Unrecognised or incomplete Defence ERP detail row |Defence ERP movements do not reconcile exactly at row |Defence ERP report controls disagree with extracted detail in column |Invalid Icon Water positional record at line |Misaligned Icon Water record at line |Unexpected data after Icon Water control totals at line |Unsupported or malformed Icon Water record at line |Invalid, duplicate or unreconciled Icon Water detail at line |Source identifiers or description exceed storage limits at record )[0-9]+\.\z",
        @"\AMissing or invalid Defence ERP amount at row [0-9]+, column [0-9]+\.\z",
        @"\ADuplicate Defence ERP account [0-9]{1,30}\.\z",
        @"\ARow [0-9]+ has (?:no G/L account|an invalid ending balance)\.\z",
        @"\ARow [0-9]+ does not contain valid opening, debit and credit movement amounts\.\z",
        @"\ARow [0-9]+ does not reconcile: opening balance plus movements differs from ending balance by \$-?[0-9,]+\.[0-9]{2}\.\z",
        @"\A(?:Choose a non-empty (?:trial balance|ERP|ROMAN) file|The (?:trial balance|ERP|ROMAN) file exceeds the configured upload limit)\.\z",
        @"\A(?:No active approved configuration release is available for |More than one approved configuration release is available for )[A-Za-z0-9_-]{1,20} FY[0-9]{4}\.(?: Select its applicable release in Additional approved configurations and inspect again\.)?\z",
        @"\AThis exact source file set has already been imported as import [0-9]+\.\z"
    };

    public static bool IsBusiness(Exception error)
    {
        if (error is InvalidDataException && error.Message == PdfMessage) { return true; }
        if (error == null || error.InnerException != null || !(error is InvalidDataException || error is InvalidOperationException || error is ArgumentException)) { return false; }
        return IsBusinessText(error.Message);
    }

    private static bool IsBusinessText(string message)
    {
        if (String.IsNullOrEmpty(message) || message.Length > 2000) { return false; }
        if (BusinessMessages.Contains(message)) { return true; }
        foreach (string pattern in BusinessPatterns)
        { if (Regex.IsMatch(message, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) { return true; } }
        return false;
    }

    public static string StoredCalculationFailure(string message)
    { return IsBusinessText(message) ? message : "Calculation failed. Review the failed calculation or contact support; do not upload the source again."; }

    public static string UserMessage(Exception error, HttpContext context, string operation)
    {
        bool denied = error is UnauthorizedAccessException;
        string reviewed = IsBusiness(error) ? error.Message : null;
        // These two controlled aggregate families use fixed guidance; their suffix is never rendered.
        if (error != null && error.InnerException == null && (error is InvalidOperationException || error is InvalidDataException))
        {
            if (operation == "Mapping" && error.Message.StartsWith("The mapping workbook was not applied: ",StringComparison.Ordinal))
            { reviewed = "The mapping workbook was not applied. Check its headers, release, account codes and mapping values, then try again."; }
            if (error.Message.StartsWith("Source rejected. Rejection audit evidence could not be saved; contact support. ",StringComparison.Ordinal))
            { reviewed = "Source not accepted. Rejection audit evidence could not be saved; contact support."; }
        }
        bool business = reviewed != null;
        if (context != null && !context.Response.HeadersWritten)
        { context.Response.StatusCode = denied ? 403 : business ? 400 : 500; context.Response.TrySkipIisCustomErrors = true; }
        if (denied) { return "You do not have access to this NORM entity."; }
        if (business)
        {
            if (error.InnerException != null) { RecordDiagnostic(error.InnerException,context,operation); }
            return reviewed;
        }
        string reference = RecordDiagnostic(error, context, operation);
        return GenericMessage + (reference == null ? "" : " Reference: " + reference + ".");
    }

    public static void HandlerFailure(HttpContext context, Exception error, string operation)
    {
        string message = UserMessage(error, context, operation);
        if (context.Response.HeadersWritten)
        { context.Response.SuppressContent = true; if (context.ApplicationInstance != null) { context.ApplicationInstance.CompleteRequest(); } return; }
        context.Response.Clear();
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Cache.SetNoStore();
        context.Response.Write(message);
    }

    private static string RecordDiagnostic(Exception error, HttpContext context, string operation)
    {
        const string key = "NORM.SafeError.Diagnostic";
        if (context == null) { return null; }
        if (context.Items.Contains(key)) { return context.Items[key] as string; }
        context.Items[key] = null; // Also suppress retries/recursive failures when persistence fails.
        try
        {
            if (context.User == null || context.User.Identity == null || !context.User.Identity.IsAuthenticated) { return null; }
            string actor = NORMHelper.CurrentUserId();
            if (actor != context.User.Identity.Name || Convert.ToInt32(NORMHelper.Scalar(
                "SELECT COUNT(*) FROM dbo.tblNORM_AdminUser WHERE LOWER(UserId)=LOWER(@user) AND IsDeactivated=0 AND RoleCode IN ('Preparer','Administrator')",
                NORMHelper.P("@user", actor))) != 1) { return null; }
            string[] allowed = { "Request", "ImportInspection", "ImportConfirmation", "YearSetup", "Mapping", "SourceHistory", "MappingWorkbook", "SourceFile", "FinancialStatementsExcel", "WordExport", "MappingExport", "ReviewPack", "YearSetupDocument" };
            if (Array.IndexOf(allowed, operation) < 0) { operation = "Request"; }
            string reference = Guid.NewGuid().ToString("N");
            StringBuilder detail = new StringBuilder("Operation=" + operation + ";Outcome=Failed");
            if (error != null)
            {
                detail.Append(";Type=").Append(error.GetType().FullName).Append(";HResult=").Append(error.HResult.ToString(CultureInfo.InvariantCulture));
                StackFrame[] frames = new StackTrace(error, false).GetFrames();
                if (frames != null) { for (int i=0; i<Math.Min(frames.Length,6); i++) { var method=frames[i].GetMethod(); if(method!=null) { detail.Append(";Method=").Append(method.DeclaringType==null ? "" : method.DeclaringType.FullName).Append('.').Append(method.Name); } } }
            }
            string bounded = detail.ToString(); if (bounded.Length>1800) { bounded=bounded.Substring(0,1800); }
            // Deliberately no exception.Message/ToString, request, cookie, filename, source rows or entity payload.
            NORMHelper.Exec("INSERT dbo.tblNORM_AuditEvent (EventCode,EntityType,EntityId,DetailText,PerformedBy) VALUES ('NORM_REQUEST_FAILED','NORMRequest',@id,@detail,@user)",
                NORMHelper.P("@id", reference),NORMHelper.P("@detail", bounded),NORMHelper.P("@user",actor));
            context.Items[key] = reference;
            return reference;
        }
        catch { try { Trace.TraceError("NORM diagnostic persistence unavailable."); } catch { } return null; }
    }
}
