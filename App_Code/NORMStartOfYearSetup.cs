using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OfficeOpenXml;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

/// <summary>Controlled start-of-year settings and comparative/budget source-document ingestion.</summary>
public static class NORMStartOfYearSetup
{
    public const string PriorDocumentType = "PriorYearFinancialStatements";
    public const string BudgetDocumentType = "PortfolioBudgetStatements";

    public sealed class UploadOutcome
    {
        public long DocumentId;
        public int FigureCount;
        public string Status;
        public string Detail;
    }

    private sealed class SourceRow
    {
        public string Locator;
        public List<string> Cells = new List<string>();
        public string StatementCode;
        public string Text { get { return String.Join(" | ", Cells.Where(x => !String.IsNullOrWhiteSpace(x)).ToArray()); } }
    }

    private sealed class TemplateLine
    {
        public string StatementCode;
        public string LineCode;
        public string Label;
        public string Normalised;
    }

    private sealed class FigureMatch
    {
        public TemplateLine Template;
        public decimal Amount;
        public string Locator;
        public decimal Confidence;
    }

    public static bool IsInstalled()
    {
        object value = NORMHelper.Scalar(
            "SELECT CASE WHEN OBJECT_ID('dbo.tblNORM_YearSetup','U') IS NOT NULL " +
            "AND OBJECT_ID('dbo.tblNORM_YearSetupDocument','U') IS NOT NULL " +
            "AND OBJECT_ID('dbo.tblNORM_YearSetupFigure','U') IS NOT NULL THEN 1 ELSE 0 END");
        return value != null && Convert.ToInt32(value) == 1;
    }

    public static int ResolveCurrentFinancialYear(string entityCode, int fallback)
    {
        if (!IsInstalled()) { return fallback; }
        object value = NORMHelper.Scalar(
            "SELECT TOP 1 CurrentFinancialYear FROM dbo.tblNORM_YearSetup " +
            "WHERE EntityCode=@entity AND IsCurrent=1 AND IsDeactivated=0 ORDER BY UpdatedUtc DESC",
            NORMHelper.P("@entity", entityCode));
        return value == null ? fallback : Convert.ToInt32(value);
    }

    public static int DefaultFinancialYear(string entityCode)
    {
        int resolved = ResolveCurrentFinancialYear(entityCode, 0);
        if (resolved > 0) { return resolved; }
        object value = NORMHelper.Scalar(
            "SELECT TOP 1 FinancialYear FROM dbo.tblNORM_ConfigurationRelease " +
            "WHERE EntityCode=@entity AND StatusCode='Approved' AND IsDeactivated=0 ORDER BY FinancialYear DESC,ConfigurationReleaseId DESC",
            NORMHelper.P("@entity", entityCode));
        return value == null ? DateTime.Today.Year : Convert.ToInt32(value);
    }

    public static int SaveYear(string entityCode, int financialYear, string user)
    {
        ValidateYear(financialYear);
        if (!IsInstalled()) { throw new InvalidOperationException("Install the start-of-year database objects first."); }
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_YearSetup SET IsCurrent=0,UpdatedBy=@user,UpdatedUtc=SYSUTCDATETIME() " +
                    "WHERE EntityCode=@entity AND IsCurrent=1 AND IsDeactivated=0 AND CurrentFinancialYear<>@year",
                    NORMHelper.P("@user", user), NORMHelper.P("@entity", entityCode), NORMHelper.P("@year", financialYear));
                object existing = NORMHelper.Scalar(connection, transaction,
                    "SELECT YearSetupId FROM dbo.tblNORM_YearSetup WHERE EntityCode=@entity AND CurrentFinancialYear=@year",
                    NORMHelper.P("@entity", entityCode), NORMHelper.P("@year", financialYear));
                int id;
                if (existing == null)
                {
                    id = NORMHelper.InsertId(connection, transaction,
                        "INSERT dbo.tblNORM_YearSetup(EntityCode,CurrentFinancialYear,IsCurrent,UpdatedBy) VALUES(@entity,@year,1,@user)",
                        NORMHelper.P("@entity", entityCode), NORMHelper.P("@year", financialYear), NORMHelper.P("@user", user));
                }
                else
                {
                    id = Convert.ToInt32(existing);
                    NORMHelper.Exec(connection, transaction,
                        "UPDATE dbo.tblNORM_YearSetup SET IsCurrent=1,IsDeactivated=0,UpdatedBy=@user,UpdatedUtc=SYSUTCDATETIME() WHERE YearSetupId=@id",
                        NORMHelper.P("@user", user), NORMHelper.P("@id", id));
                }
                WriteAudit(connection, transaction, "YEAR_SETUP_UPDATED", "YearSetup", id.ToString(CultureInfo.InvariantCulture),
                    "Current financial year set to " + financialYear.ToString(CultureInfo.InvariantCulture) +
                    "; comparative year " + (financialYear - 1).ToString(CultureInfo.InvariantCulture) + ".", user);
                transaction.Commit();
                return id;
            }
            catch { transaction.Rollback(); throw; }
        }
    }

    public static int CurrentSetupId(string entityCode)
    {
        if (!IsInstalled()) { return 0; }
        object value = NORMHelper.Scalar(
            "SELECT TOP 1 YearSetupId FROM dbo.tblNORM_YearSetup WHERE EntityCode=@entity AND IsCurrent=1 AND IsDeactivated=0 ORDER BY UpdatedUtc DESC",
            NORMHelper.P("@entity", entityCode));
        return value == null ? 0 : Convert.ToInt32(value);
    }

    public static UploadOutcome Upload(int setupId, string documentType, byte[] content, string fileName, int? requestedStartPage, string user)
    {
        if (!IsInstalled()) { throw new InvalidOperationException("Install the start-of-year database objects first."); }
        if (setupId <= 0) { throw new InvalidOperationException("Save the current financial year before uploading source documents."); }
        if (content == null || content.Length == 0) { throw new InvalidDataException("Choose a non-empty source document."); }
        int maximum = NORMHelper.SettingInt("NORM.MaxUploadBytes", 104857600);
        if (content.Length > maximum) { throw new InvalidDataException("The source document exceeds the configured upload limit."); }
        if (documentType != PriorDocumentType && documentType != BudgetDocumentType) { throw new ArgumentException("Unknown source-document type."); }
        string extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        string[] allowed = { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
        if (!allowed.Contains(extension)) { throw new InvalidDataException("Upload a PDF, Word document or Excel workbook."); }
        if (extension == ".pdf" && (!requestedStartPage.HasValue || requestedStartPage.Value < 1 || requestedStartPage.Value > 9999))
            throw new InvalidDataException("Enter the PDF page where the financial-statement tables commence.");

        DataTable setup = NORMHelper.Query(
            "SELECT YearSetupId,EntityCode,CurrentFinancialYear FROM dbo.tblNORM_YearSetup WHERE YearSetupId=@id AND IsCurrent=1 AND IsDeactivated=0",
            NORMHelper.P("@id", setupId));
        if (setup.Rows.Count == 0) { throw new InvalidOperationException("The active start-of-year setup was not found."); }
        string entity = NORMHelper.Str(setup.Rows[0], "EntityCode");
        int financialYear = NORMHelper.Int(setup.Rows[0], "CurrentFinancialYear");
        int releaseId = ResolveRelease(entity, financialYear);
        List<SourceRow> rows = ExtractRows(content, extension, requestedStartPage);
        string detectedStart;
        AssignStatementScopes(rows, out detectedStart);
        if (extension == ".pdf" && requestedStartPage.HasValue)
        {
            string requested = "Requested PDF page " + requestedStartPage.Value.ToString(CultureInfo.InvariantCulture);
            detectedStart = String.IsNullOrWhiteSpace(detectedStart) ? requested : requested + " · first statement at " + detectedStart;
        }
        List<TemplateLine> templates = LoadTemplates(releaseId);
        List<FigureMatch> figures = MatchFigures(rows, templates);
        string status = figures.Count > 0 ? "Extracted" : "ReviewRequired";
        string detail;
        if (figures.Count > 0)
            detail = figures.Count.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " high-confidence statement figure(s) extracted and applied" +
                (requestedStartPage.HasValue ? " from PDF page " + requestedStartPage.Value.ToString(CultureInfo.InvariantCulture) + " onward" : "") +
                ". Review source locators before sign-off.";
        else if (extension == ".doc" || extension == ".xls")
            detail = "The legacy binary format was retained, but automatic extraction requires a .docx or .xlsx copy. No figures were applied.";
        else if (extension == ".pdf" && rows.Count == 0)
            detail = "No searchable text was found from PDF page " + requestedStartPage.Value.ToString(CultureInfo.InvariantCulture) +
                " onward. The file was retained; check the nominated page or provide a searchable PDF. No figures were applied.";
        else if (extension == ".pdf")
            detail = "NORM read " + rows.Count.ToString("N0", CultureInfo.GetCultureInfo("en-AU")) + " text row(s) from PDF page " +
                requestedStartPage.Value.ToString(CultureInfo.InvariantCulture) +
                " onward, but could not confidently map them to the configured statement lines. No figures were applied.";
        else
            detail = "The document was retained, but NORM could not confidently identify mapped statement rows. No figures were applied; provide a searchable or spreadsheet version for extraction.";

        long documentId;
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_YearSetupDocument SET IsDeactivated=1 WHERE YearSetupId=@setup AND DocumentTypeCode=@type AND IsDeactivated=0",
                    NORMHelper.P("@setup", setupId), NORMHelper.P("@type", documentType));
                OleDbParameter bytes = NORMHelper.P("@content", content);
                bytes.OleDbType = OleDbType.LongVarBinary;
                documentId = Convert.ToInt64(NORMHelper.Scalar(connection, transaction,
                    "INSERT dbo.tblNORM_YearSetupDocument(YearSetupId,DocumentTypeCode,SourceFileName,SourceFileExtension," +
                    "SourceFileHash,SourceFileBytes,FileContent,ExtractionStatus,DetectedStart,ExtractedFigureCount,ExtractionDetail,UploadedBy) " +
                    "VALUES(@setup,@type,@file,@extension,@hash,@length,@content,@status,@start,@count,@detail,@user); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                    NORMHelper.P("@setup", setupId), NORMHelper.P("@type", documentType),
                    NORMHelper.P("@file", Path.GetFileName(fileName)), NORMHelper.P("@extension", extension),
                    NORMHelper.P("@hash", NORMCrypto.Sha256(content)), NORMHelper.P("@length", content.LongLength), bytes,
                    NORMHelper.P("@status", status), NORMHelper.P("@start", EmptyToNull(detectedStart, 300)),
                    NORMHelper.P("@count", figures.Count), NORMHelper.P("@detail", detail), NORMHelper.P("@user", user)));
                string figureType = documentType == PriorDocumentType ? "PriorActual" : "OriginalBudget";
                for (int i = 0; i < figures.Count; i++)
                {
                    FigureMatch figure = figures[i];
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_YearSetupFigure(YearSetupDocumentId,FigureType,StatementCode,LineCode,LineLabel,Amount,SourceLocator,MatchConfidence,ReviewStatus) " +
                        "VALUES(@document,@type,@statement,@line,@label,@amount,@locator,@confidence,'AutoMatched')",
                        NORMHelper.P("@document", documentId), NORMHelper.P("@type", figureType),
                        NORMHelper.P("@statement", figure.Template.StatementCode), NORMHelper.P("@line", figure.Template.LineCode),
                        NORMHelper.P("@label", figure.Template.Label), NORMHelper.P("@amount", figure.Amount),
                        NORMHelper.P("@locator", EmptyToNull(figure.Locator, 300)), NORMHelper.P("@confidence", figure.Confidence));
                }
                WriteAudit(connection, transaction, "YEAR_SETUP_DOCUMENT_UPLOADED", "YearSetup", setupId.ToString(CultureInfo.InvariantCulture),
                    DisplayName(documentType) + " uploaded: " + Path.GetFileName(fileName) + "; " + detail, user);
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }
        return new UploadOutcome { DocumentId = documentId, FigureCount = figures.Count, Status = status, Detail = detail };
    }

    public static DataTable LoadDocuments(int setupId)
    {
        if (!IsInstalled() || setupId <= 0) { return new DataTable(); }
        return NORMHelper.Query(
            "SELECT YearSetupDocumentId,DocumentTypeCode,SourceFileName,SourceFileHash,SourceFileBytes,ExtractionStatus," +
            "DetectedStart,ExtractedFigureCount,ExtractionDetail,UploadedBy,UploadedUtc FROM dbo.tblNORM_YearSetupDocument " +
            "WHERE YearSetupId=@setup AND IsDeactivated=0 ORDER BY UploadedUtc DESC",
            NORMHelper.P("@setup", setupId));
    }

    public static DataTable LoadFigures(int setupId)
    {
        if (!IsInstalled() || setupId <= 0) { return new DataTable(); }
        return NORMHelper.Query(
            "SELECT TOP 120 d.DocumentTypeCode,d.SourceFileName,f.FigureType,f.StatementCode,f.LineCode,f.LineLabel,f.Amount," +
            "f.SourceLocator,f.MatchConfidence,f.ReviewStatus FROM dbo.tblNORM_YearSetupFigure f " +
            "INNER JOIN dbo.tblNORM_YearSetupDocument d ON d.YearSetupDocumentId=f.YearSetupDocumentId " +
            "WHERE d.YearSetupId=@setup AND d.IsDeactivated=0 AND f.IsDeactivated=0 " +
            "ORDER BY CASE f.StatementCode WHEN 'SOCI' THEN 1 WHEN 'SOFP' THEN 2 WHEN 'SOCE' THEN 3 WHEN 'CASH' THEN 4 ELSE 5 END,f.YearSetupFigureId",
            NORMHelper.P("@setup", setupId));
    }

    public static Dictionary<string, decimal> LoadPriorActualFigures(string entityCode)
    {
        return LoadSetupFigures(entityCode, "PriorActual");
    }

    public static Dictionary<string, decimal> LoadOriginalBudgetFigures(string entityCode)
    {
        return LoadSetupFigures(entityCode, "OriginalBudget");
    }

    public static decimal? FigureValue(Dictionary<string, decimal> values, string statementCode, string lineCode, decimal? fallback)
    {
        if (values != null && !String.IsNullOrWhiteSpace(statementCode) && !String.IsNullOrWhiteSpace(lineCode))
        {
            decimal amount;
            if (values.TryGetValue(statementCode + "|" + lineCode, out amount)) { return amount; }
        }
        return fallback;
    }

    public static void OverlayFigures(Dictionary<string, decimal> target, Dictionary<string, decimal> overlay)
    {
        if (target == null || overlay == null) { return; }
        foreach (KeyValuePair<string, decimal> item in overlay) { target[item.Key] = item.Value; }
    }

    private static Dictionary<string, decimal> LoadSetupFigures(string entityCode, string figureType)
    {
        Dictionary<string, decimal> values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!IsInstalled() || String.IsNullOrWhiteSpace(entityCode)) { return values; }
        DataTable table = NORMHelper.Query(
            "SELECT f.StatementCode,f.LineCode,f.Amount FROM dbo.tblNORM_YearSetup y " +
            "INNER JOIN dbo.tblNORM_YearSetupDocument d ON d.YearSetupId=y.YearSetupId AND d.IsDeactivated=0 " +
            "INNER JOIN dbo.tblNORM_YearSetupFigure f ON f.YearSetupDocumentId=d.YearSetupDocumentId AND f.IsDeactivated=0 " +
            "WHERE y.EntityCode=@entity AND y.IsCurrent=1 AND y.IsDeactivated=0 AND f.FigureType=@type " +
            "ORDER BY d.UploadedUtc,f.YearSetupFigureId",
            NORMHelper.P("@entity", entityCode), NORMHelper.P("@type", figureType));
        for (int i = 0; i < table.Rows.Count; i++)
        {
            values[NORMHelper.Str(table.Rows[i], "StatementCode") + "|" + NORMHelper.Str(table.Rows[i], "LineCode")] =
                NORMHelper.Dec(table.Rows[i], "Amount");
        }
        return values;
    }

    private static int ResolveRelease(string entity, int financialYear)
    {
        object value = NORMHelper.Scalar(
            "SELECT TOP 1 ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease WHERE EntityCode=@entity " +
            "AND StatusCode='Approved' AND IsDeactivated=0 ORDER BY CASE WHEN FinancialYear=@year THEN 0 ELSE 1 END,FinancialYear DESC,ConfigurationReleaseId DESC",
            NORMHelper.P("@entity", entity), NORMHelper.P("@year", financialYear));
        if (value == null) { throw new InvalidOperationException("No approved NORM configuration release is available for " + entity + "."); }
        return Convert.ToInt32(value);
    }

    private static List<TemplateLine> LoadTemplates(int releaseId)
    {
        DataTable table = NORMHelper.Query(
            "SELECT StatementCode,LineCode,LineLabel FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release " +
            "AND StatementCode IN ('SOCI','SOFP','SOCE','CASH') AND LineCode IS NOT NULL AND IsDeactivated=0 ORDER BY StatementCode,SeqNo",
            NORMHelper.P("@release", releaseId));
        List<TemplateLine> result = new List<TemplateLine>();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            string label = NORMHelper.Str(table.Rows[i], "LineLabel");
            string normalised = NormaliseLabel(label);
            if (normalised.Length < 3) { continue; }
            result.Add(new TemplateLine { StatementCode = NORMHelper.Str(table.Rows[i], "StatementCode"),
                LineCode = NORMHelper.Str(table.Rows[i], "LineCode"), Label = label, Normalised = normalised });
        }
        if (!result.Any(x => x.StatementCode == "SOCI" && x.LineCode == "Foreign exchange gains"))
        {
            const string gainLabel = "Net foreign exchange gains";
            result.Add(new TemplateLine { StatementCode = "SOCI", LineCode = "Foreign exchange gains",
                Label = gainLabel, Normalised = NormaliseLabel(gainLabel) });
        }
        const string totalIncomeLabel = "Total income";
        result.Add(new TemplateLine { StatementCode = "SOCI", LineCode = "Total own-source income",
            Label = totalIncomeLabel, Normalised = NormaliseLabel(totalIncomeLabel) });
        const string revaluationLabel = "Changes in asset revaluation reserves";
        result.Add(new TemplateLine { StatementCode = "SOCI", LineCode = "OCI_REVALUATION",
            Label = revaluationLabel, Normalised = NormaliseLabel(revaluationLabel) });
        return result;
    }

    private static List<FigureMatch> MatchFigures(List<SourceRow> rows, List<TemplateLine> templates)
    {
        Dictionary<string, FigureMatch> best = new Dictionary<string, FigureMatch>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < rows.Count; r++)
        {
            SourceRow row = rows[r];
            decimal amount;
            if (!TryAmount(row, out amount)) { continue; }
            string candidate = NormaliseLabel(RemoveAmounts(row.Text));
            if (candidate.Length < 3) { continue; }
            for (int t = 0; t < templates.Count; t++)
            {
                TemplateLine template = templates[t];
                if (!String.IsNullOrEmpty(row.StatementCode) && row.StatementCode != template.StatementCode) { continue; }
                decimal confidence = MatchConfidence(candidate, template.Normalised);
                if (confidence < 90m) { continue; }
                string key = template.StatementCode + "|" + template.LineCode;
                FigureMatch existing;
                if (!best.TryGetValue(key, out existing) || confidence > existing.Confidence)
                    best[key] = new FigureMatch { Template = template, Amount = amount, Locator = row.Locator, Confidence = confidence };
            }
        }
        return best.Values.OrderBy(x => StatementOrder(x.Template.StatementCode)).ThenBy(x => x.Template.Label).ToList();
    }

    private static decimal MatchConfidence(string candidate, string expected)
    {
        if (candidate == expected) { return 100m; }
        if (candidate.StartsWith(expected + " ", StringComparison.Ordinal) || candidate.EndsWith(" " + expected, StringComparison.Ordinal)) { return 96m; }
        if (expected.Length >= 8 && candidate.Contains(expected)) { return 93m; }
        if (candidate.Length >= 8 && expected.Contains(candidate)) { return 90m; }
        return 0m;
    }

    private static bool TryAmount(SourceRow row, out decimal amount)
    {
        amount = 0m;
        for (int i = 1; i < row.Cells.Count; i++) if (TryParseAmount(row.Cells[i], out amount)) { return true; }
        MatchCollection matches = Regex.Matches(row.Text, @"(?<![A-Za-z0-9])\(?\$?\s*-?\d{1,3}(?:,\d{3})*(?:\.\d+)?\)?(?![A-Za-z])");
        for (int i = 0; i < matches.Count; i++)
        {
            decimal value;
            if (!TryParseAmount(matches[i].Value, out value)) { continue; }
            string digits = Regex.Replace(matches[i].Value, "[^0-9]", "");
            int year;
            if (digits.Length == 4 && Int32.TryParse(digits, out year) && year >= 1900 && year <= 2999) { continue; }
            amount = value; return true;
        }
        return false;
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        amount = 0m;
        string text = (value ?? "").Trim();
        if (text.Length == 0 || text == "-" || text == "–" || text == "—") { return false; }
        bool negative = text.StartsWith("(") && text.EndsWith(")");
        text = text.Replace("$", "").Replace(",", "").Replace(" ", "").Trim('(', ')');
        decimal parsed;
        if (!Decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed)) { return false; }
        amount = negative ? -Math.Abs(parsed) : parsed;
        return true;
    }

    private static void AssignStatementScopes(List<SourceRow> rows, out string detectedStart)
    {
        detectedStart = null;
        string current = null;
        for (int i = 0; i < rows.Count; i++)
        {
            string detected = DetectStatement(rows[i].Text + " " + rows[i].Locator);
            if (detected != null)
            {
                current = detected;
                if (detectedStart == null) { detectedStart = rows[i].Locator; }
            }
            rows[i].StatementCode = current;
        }
    }

    private static string DetectStatement(string value)
    {
        string text = NormaliseLabel(value);
        if (text.Contains("statement of comprehensive income") || text.Contains("statement of profit or loss")) return "SOCI";
        if (text.Contains("statement of financial position") || text.Contains("balance sheet")) return "SOFP";
        if (text.Contains("statement of changes in equity") || text.Contains("changes in equity")) return "SOCE";
        if (text.Contains("cash flow statement") || text.Contains("statement of cash flows")) return "CASH";
        return null;
    }

    private static List<SourceRow> ExtractRows(byte[] content, string extension, int? requestedStartPage)
    {
        if (extension == ".xlsx") return ExtractExcel(content);
        if (extension == ".docx") return ExtractWord(content);
        if (extension == ".pdf") return ExtractPdf(content, requestedStartPage ?? 1);
        return new List<SourceRow>();
    }

    private static List<SourceRow> ExtractExcel(byte[] content)
    {
        List<SourceRow> rows = new List<SourceRow>();
        using (MemoryStream stream = new MemoryStream(content))
        using (ExcelPackage package = new ExcelPackage(stream))
        {
            foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
            {
                if (sheet.Dimension == null) { continue; }
                int lastRow = Math.Min(sheet.Dimension.End.Row, 20000);
                int lastColumn = Math.Min(sheet.Dimension.End.Column, 100);
                for (int r = sheet.Dimension.Start.Row; r <= lastRow; r++)
                {
                    SourceRow row = new SourceRow { Locator = sheet.Name + "!" + r.ToString(CultureInfo.InvariantCulture) };
                    for (int c = sheet.Dimension.Start.Column; c <= lastColumn; c++)
                    {
                        string text = Convert.ToString(sheet.Cells[r, c].Text, CultureInfo.InvariantCulture).Trim();
                        if (text.Length > 0) row.Cells.Add(text);
                    }
                    if (row.Cells.Count > 0) rows.Add(row);
                }
            }
        }
        return rows;
    }

    private static List<SourceRow> ExtractWord(byte[] content)
    {
        List<SourceRow> rows = new List<SourceRow>();
        byte[] documentXml = ReadZipEntry(content, "word/document.xml");
        if (documentXml == null || documentXml.Length == 0) { return rows; }
        using (MemoryStream xml = new MemoryStream(documentXml))
        {
            XDocument document = XDocument.Load(xml);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            int tableNo = 0;
            foreach (XElement table in document.Descendants(w + "tbl"))
            {
                tableNo++; int rowNo = 0;
                foreach (XElement tr in table.Elements(w + "tr"))
                {
                    rowNo++;
                    SourceRow row = new SourceRow { Locator = "Table " + tableNo.ToString() + ", row " + rowNo.ToString() };
                    foreach (XElement cell in tr.Elements(w + "tc"))
                    {
                        string text = String.Join(" ", cell.Descendants(w + "t").Select(x => x.Value).ToArray()).Trim();
                        if (text.Length > 0) row.Cells.Add(text);
                    }
                    if (row.Cells.Count > 0) rows.Add(row);
                }
            }
            int paragraph = 0;
            foreach (XElement p in document.Descendants(w + "p").Where(x => !x.Ancestors(w + "tbl").Any()))
            {
                paragraph++;
                string text = String.Join("", p.Descendants(w + "t").Select(x => x.Value).ToArray()).Trim();
                if (text.Length > 0) rows.Add(new SourceRow { Locator = "Paragraph " + paragraph.ToString(), Cells = new List<string> { text } });
            }
        }
        return rows;
    }

    private static byte[] ReadZipEntry(byte[] archive, string requestedName)
    {
        if (archive == null || archive.Length < 22) { return null; }
        int eocd = -1;
        for (int i = archive.Length - 22; i >= Math.Max(0, archive.Length - 65557); i--)
        {
            if (ReadUInt32(archive, i) == 0x06054b50U) { eocd = i; break; }
        }
        if (eocd < 0) { return null; }
        int entries = ReadUInt16(archive, eocd + 10);
        int cursor = checked((int)ReadUInt32(archive, eocd + 16));
        for (int entry = 0; entry < entries && cursor + 46 <= archive.Length; entry++)
        {
            if (ReadUInt32(archive, cursor) != 0x02014b50U) { break; }
            int method = ReadUInt16(archive, cursor + 10);
            int compressedSize = checked((int)ReadUInt32(archive, cursor + 20));
            int uncompressedSize = checked((int)ReadUInt32(archive, cursor + 24));
            int fileNameLength = ReadUInt16(archive, cursor + 28);
            int extraLength = ReadUInt16(archive, cursor + 30);
            int commentLength = ReadUInt16(archive, cursor + 32);
            int localOffset = checked((int)ReadUInt32(archive, cursor + 42));
            if (cursor + 46 + fileNameLength > archive.Length) { break; }
            string name = Encoding.UTF8.GetString(archive, cursor + 46, fileNameLength).Replace('\\', '/');
            if (String.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                if (uncompressedSize > 33554432) throw new InvalidDataException("The Word document XML is too large to extract safely.");
                if (localOffset < 0 || localOffset + 30 > archive.Length || ReadUInt32(archive, localOffset) != 0x04034b50U) return null;
                int localName = ReadUInt16(archive, localOffset + 26);
                int localExtra = ReadUInt16(archive, localOffset + 28);
                int dataOffset = localOffset + 30 + localName + localExtra;
                if (dataOffset < 0 || compressedSize < 0 || dataOffset + compressedSize > archive.Length) return null;
                if (method == 0)
                {
                    byte[] stored = new byte[compressedSize]; Buffer.BlockCopy(archive, dataOffset, stored, 0, compressedSize); return stored;
                }
                if (method == 8)
                {
                    using (MemoryStream input = new MemoryStream(archive, dataOffset, compressedSize, false))
                    using (DeflateStream inflater = new DeflateStream(input, CompressionMode.Decompress))
                    using (MemoryStream output = new MemoryStream()) { CopyWithLimit(inflater, output, 33554432); return output.ToArray(); }
                }
                return null;
            }
            cursor += 46 + fileNameLength + extraLength + commentLength;
        }
        return null;
    }

    private static ushort ReadUInt16(byte[] value, int offset)
    {
        if (offset < 0 || offset + 2 > value.Length) return 0;
        return (ushort)(value[offset] | (value[offset + 1] << 8));
    }

    private static uint ReadUInt32(byte[] value, int offset)
    {
        if (offset < 0 || offset + 4 > value.Length) return 0;
        return (uint)(value[offset] | (value[offset + 1] << 8) | (value[offset + 2] << 16) | (value[offset + 3] << 24));
    }

    private static List<SourceRow> ExtractPdf(byte[] content, int startPage)
    {
        List<SourceRow> rows = new List<SourceRow>();
        try
        {
            using (PdfDocument document = PdfDocument.Open(content))
            {
                if (startPage > document.NumberOfPages)
                    throw new InvalidDataException("The nominated start page exceeds the PDF's " + document.NumberOfPages.ToString(CultureInfo.InvariantCulture) + " pages.");
                int extractedCharacters = 0;
                for (int pageNumber = startPage; pageNumber <= document.NumberOfPages && rows.Count < 75000 && extractedCharacters < 12000000; pageNumber++)
                {
                    Page page = document.GetPage(pageNumber);
                    List<Word> words = page.GetWords(NearestNeighbourWordExtractor.Instance)
                        .Where(x => !String.IsNullOrWhiteSpace(x.Text))
                        .OrderByDescending(x => x.BoundingBox.Centroid.Y)
                        .ThenBy(x => x.BoundingBox.Left)
                        .ToList();
                    int pageRow = 0;
                    if (words.Count > 0)
                    {
                        List<Word> line = new List<Word>();
                        double anchorY = 0;
                        for (int i = 0; i < words.Count; i++)
                        {
                            double y = words[i].BoundingBox.Centroid.Y;
                            double tolerance = Math.Max(2.5, Math.Min(7.0, words[i].BoundingBox.Height * 0.65));
                            if (line.Count > 0 && Math.Abs(y - anchorY) > tolerance)
                            {
                                AddPdfRow(rows, line, pageNumber, ref pageRow, ref extractedCharacters);
                                line.Clear();
                            }
                            line.Add(words[i]);
                            anchorY = line.Count == 1 ? y : ((anchorY * (line.Count - 1)) + y) / line.Count;
                        }
                        if (line.Count > 0) AddPdfRow(rows, line, pageNumber, ref pageRow, ref extractedCharacters);
                    }
                    else
                    {
                        string text = ContentOrderTextExtractor.GetText(page);
                        string[] lines = Regex.Split(text ?? "", @"\r?\n");
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string clean = Regex.Replace(lines[i] ?? "", @"\s+", " ").Trim();
                            if (clean.Length == 0) continue;
                            pageRow++;
                            extractedCharacters += clean.Length;
                            rows.Add(new SourceRow { Locator = "PDF page " + pageNumber.ToString(CultureInfo.InvariantCulture) + ", row " + pageRow.ToString(CultureInfo.InvariantCulture), Cells = new List<string> { clean } });
                        }
                    }
                }
            }
        }
        catch (InvalidDataException) { throw; }
        catch (Exception error)
        {
            throw new InvalidDataException("NORM could not read the PDF from page " + startPage.ToString(CultureInfo.InvariantCulture) + ": " + error.Message, error);
        }
        return rows;
    }

    private static void AddPdfRow(List<SourceRow> rows, List<Word> sourceWords, int pageNumber, ref int pageRow, ref int extractedCharacters)
    {
        List<Word> words = sourceWords.OrderBy(x => x.BoundingBox.Left).ToList();
        List<string> cells = new List<string>();
        StringBuilder cell = new StringBuilder();
        double previousRight = 0;
        double previousHeight = 0;
        for (int i = 0; i < words.Count; i++)
        {
            double gap = i == 0 ? 0 : words[i].BoundingBox.Left - previousRight;
            double threshold = Math.Max(12.0, Math.Max(previousHeight, words[i].BoundingBox.Height) * 1.35);
            if (i > 0 && gap > threshold && cell.Length > 0)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            if (cell.Length > 0) cell.Append(' ');
            cell.Append(words[i].Text.Trim());
            previousRight = words[i].BoundingBox.Right;
            previousHeight = words[i].BoundingBox.Height;
        }
        if (cell.Length > 0) cells.Add(cell.ToString().Trim());
        cells = cells.Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
        if (cells.Count == 0) return;
        pageRow++;
        extractedCharacters += cells.Sum(x => x.Length);
        rows.Add(new SourceRow { Locator = "PDF page " + pageNumber.ToString(CultureInfo.InvariantCulture) + ", row " + pageRow.ToString(CultureInfo.InvariantCulture), Cells = cells });
    }

    private static void CopyWithLimit(Stream input, Stream output, int maximumBytes)
    {
        byte[] buffer = new byte[81920];
        int total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("The document's expanded content is too large to extract safely.");
            output.Write(buffer, 0, read);
        }
    }

    private static string DecodePdfString(string value)
    {
        return Regex.Replace(value ?? "", @"\\([nrtbf()\\])", delegate(Match m) {
            switch (m.Groups[1].Value) { case "n": return "\n"; case "r": return "\r"; case "t": return "\t"; case "b": return "\b"; case "f": return "\f"; default: return m.Groups[1].Value; }
        }).Replace("\\ ", " ");
    }

    private static string RemoveAmounts(string value)
    {
        return Regex.Replace(value ?? "", @"(?<![A-Za-z])\(?\$?\s*-?\d[\d,]*(?:\.\d+)?\)?(?![A-Za-z])", " ");
    }

    private static string NormaliseLabel(string value)
    {
        string text = (value ?? "").ToLowerInvariant().Replace("&", " and ");
        text = Regex.Replace(text, @"[^a-z0-9]+", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static int StatementOrder(string code)
    {
        switch (code) { case "SOCI": return 1; case "SOFP": return 2; case "SOCE": return 3; case "CASH": return 4; default: return 5; }
    }

    private static string DisplayName(string type)
    {
        return type == PriorDocumentType ? "Prior Year Financial Statements" : "Portfolio Budget Statements";
    }

    private static void ValidateYear(int year)
    {
        if (year < 1900 || year > 2999 || year.ToString(CultureInfo.InvariantCulture).Length != 4)
            throw new InvalidDataException("Enter the financial year as exactly four digits, for example 2025.");
    }

    private static object EmptyToNull(string value, int maximum)
    {
        if (String.IsNullOrWhiteSpace(value)) { return null; }
        value = value.Trim(); return value.Length <= maximum ? value : value.Substring(0, maximum);
    }

    private static void WriteAudit(OleDbConnection connection, OleDbTransaction transaction,
        string eventCode, string entityType, string entityId, string detail, string user)
    {
        NORMHelper.Exec(connection, transaction,
            "INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy) VALUES(@event,@type,@id,@detail,@user)",
            NORMHelper.P("@event", eventCode), NORMHelper.P("@type", entityType), NORMHelper.P("@id", entityId),
            NORMHelper.P("@detail", detail), NORMHelper.P("@user", user));
    }
}
