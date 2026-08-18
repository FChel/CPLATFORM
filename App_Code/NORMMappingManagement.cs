using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace CPlatform.NORM
{
    public sealed class NORMMappingValidation
    {
        public int MappingCount;
        public int ChangedCount;
        public int UnmappedCount;
        public int ErrorCount;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool CanApprove { get { return ErrorCount == 0 && MappingCount > 0; } }
    }

    public sealed class NORMMappingUploadOutcome
    {
        public int ChangedCount;
        public string WorkbookHash;
        public NORMMappingValidation Validation;
    }

    /// <summary>Draft, workbook, validation, approval and replay workflow for NORM account mappings.</summary>
    public static class NORMMappingManagement
    {
        private const int HeaderRow = 7;
        private static readonly HashSet<string> AccountTypes = new HashSet<string>(
            new[] { "Asset", "Liability", "Equity", "Income", "Expense" }, StringComparer.OrdinalIgnoreCase);

        public static int CreateDraft(int parentReleaseId, string version, string reason, string user)
        {
            version = (version ?? "").Trim();
            reason = (reason ?? "").Trim();
            if (version.Length == 0 || version.Length > 30) throw new InvalidOperationException("Enter a version of no more than 30 characters, for example v1.1.");
            if (reason.Length < 10 || reason.Length > 500) throw new InvalidOperationException("Enter a change reason of between 10 and 500 characters.");
            DataTable parentTable = NORMHelper.Query(
                "SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId=@release AND StatusCode='Approved' AND IsDeactivated=0",
                NORMHelper.P("@release", parentReleaseId));
            if (parentTable.Rows.Count == 0) throw new InvalidOperationException("Select an approved release to use as the starting point.");
            DataRow parent = parentTable.Rows[0];
            object duplicate = NORMHelper.Scalar(
                "SELECT ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease WHERE FinancialYear=@fy AND EntityCode=@entity AND VersionCode=@version",
                NORMHelper.P("@fy", NORMHelper.Int(parent, "FinancialYear")), NORMHelper.P("@entity", NORMHelper.Str(parent, "EntityCode")), NORMHelper.P("@version", version));
            if (duplicate != null) throw new InvalidOperationException("That version already exists for this entity and financial year.");

            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    int releaseId = NORMHelper.InsertId(connection, transaction,
                        "INSERT dbo.tblNORM_ConfigurationRelease " +
                        "(FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy,ParentConfigurationReleaseId,ChangeReason) " +
                        "VALUES (@fy,@entity,@version,@label,'Draft',@user,@parent,@reason)",
                        NORMHelper.P("@fy", NORMHelper.Int(parent, "FinancialYear")), NORMHelper.P("@entity", NORMHelper.Str(parent, "EntityCode")),
                        NORMHelper.P("@version", version), NORMHelper.P("@label", "Mapping change - " + reason),
                        NORMHelper.P("@user", user), NORMHelper.P("@parent", parentReleaseId), NORMHelper.P("@reason", reason));

                    CopyReleaseContent(connection, transaction, parentReleaseId, releaseId, user);
                    Audit(connection, transaction, "MAPPING_DRAFT_CREATED", releaseId,
                        "Draft " + version + " created from approved release " + parentReleaseId.ToString(CultureInfo.InvariantCulture) + ". Reason: " + reason, user);
                    transaction.Commit();
                    return releaseId;
                }
                catch { transaction.Rollback(); throw; }
            }
        }

        private static void CopyReleaseContent(OleDbConnection c, OleDbTransaction t, int source, int target, string user)
        {
            NORMHelper.Exec(c, t,
                "INSERT dbo.tblNORM_AccountMap (ConfigurationReleaseId,FinancialYear,EntityCode,GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass,MappingRationale,IsDeactivated) " +
                "SELECT @target,FinancialYear,EntityCode,GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass,MappingRationale,IsDeactivated " +
                "FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@source",
                NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            NORMHelper.Exec(c, t,
                "INSERT dbo.tblNORM_StatementLine (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,NaturalSign,CalculationKind,FormulaSpec,IsClickable,IsDeactivated) " +
                "SELECT @target,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,NaturalSign,CalculationKind,FormulaSpec,IsClickable,IsDeactivated " +
                "FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@source",
                NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            NORMHelper.Exec(c, t,
                "INSERT dbo.tblNORM_PublishedFigure (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,AmountCurrent,AmountPrior,SourceReference,IsDeactivated) " +
                "SELECT @target,FinancialYear,EntityCode,StatementCode,LineCode,AmountCurrent,AmountPrior,SourceReference,IsDeactivated " +
                "FROM dbo.tblNORM_PublishedFigure WHERE ConfigurationReleaseId=@source",
                NORMHelper.P("@target", target), NORMHelper.P("@source", source));

            if (TableExists(c, t, "tblNORM_ReportingProfile"))
                NORMHelper.Exec(c, t,
                    "INSERT dbo.tblNORM_ReportingProfile (ConfigurationReleaseId,EntityTypeCode,ReportingBasisCode,DisclosureTierCode,MaterialityBasis,UpdatedBy,IsDeactivated,OverallMateriality,PerformanceMateriality,ClearlyTrivialThreshold,BudgetVarianceThreshold,QualitativeConsiderations) " +
                    "SELECT @target,EntityTypeCode,ReportingBasisCode,DisclosureTierCode,MaterialityBasis,@user,IsDeactivated,OverallMateriality,PerformanceMateriality,ClearlyTrivialThreshold,BudgetVarianceThreshold,QualitativeConsiderations " +
                    "FROM dbo.tblNORM_ReportingProfile WHERE ConfigurationReleaseId=@source",
                    NORMHelper.P("@target", target), NORMHelper.P("@user", user), NORMHelper.P("@source", source));
            if (TableExists(c, t, "tblNORM_RequirementSelection"))
                NORMHelper.Exec(c, t,
                    "INSERT dbo.tblNORM_RequirementSelection (ConfigurationReleaseId,CapabilityCode,IsRequired,Rationale,UpdatedBy,IsDeactivated) " +
                    "SELECT @target,CapabilityCode,IsRequired,Rationale,@user,IsDeactivated FROM dbo.tblNORM_RequirementSelection WHERE ConfigurationReleaseId=@source",
                    NORMHelper.P("@target", target), NORMHelper.P("@user", user), NORMHelper.P("@source", source));
            if (TableExists(c, t, "tblNORM_DisclosureRule"))
                NORMHelper.Exec(c, t,
                    "INSERT dbo.tblNORM_DisclosureRule (ConfigurationReleaseId,DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,TriggerCode,IsBaseRequired,RequiresNarrative,SortOrder,GuidanceText,IsDeactivated) " +
                    "SELECT @target,DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,TriggerCode,IsBaseRequired,RequiresNarrative,SortOrder,GuidanceText,IsDeactivated FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=@source",
                    NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            if (TableExists(c, t, "tblNORM_NarrativeTemplate"))
                NORMHelper.Exec(c, t,
                    "INSERT dbo.tblNORM_NarrativeTemplate (ConfigurationReleaseId,DisclosureCode,NarrativeType,TemplateText,IsDeactivated) " +
                    "SELECT @target,DisclosureCode,NarrativeType,TemplateText,IsDeactivated FROM dbo.tblNORM_NarrativeTemplate WHERE ConfigurationReleaseId=@source",
                    NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            if (TableExists(c, t, "tblNORM_SourceFigure"))
                NORMHelper.Exec(c, t,
                    "INSERT dbo.tblNORM_SourceFigure (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl,IsDeactivated) " +
                    "SELECT @target,FinancialYear,EntityCode,StatementCode,LineCode,FigureType,Amount,SourceReference,SourceUrl,IsDeactivated FROM dbo.tblNORM_SourceFigure WHERE ConfigurationReleaseId=@source",
                    NORMHelper.P("@target", target), NORMHelper.P("@source", source));
        }

        public static byte[] BuildEditableWorkbook(int releaseId)
        {
            DataRow release = DraftRelease(releaseId);
            int parentId = NORMHelper.Int(release, "ParentConfigurationReleaseId");
            int importId = LatestImportId(parentId, NORMHelper.Int(release, "FinancialYear"), NORMHelper.Str(release, "EntityCode"));
            DataTable mappings = NORMHelper.Query(
                "SELECT m.GlCode,m.GlDescription,m.AccountType,m.StatementLine,m.NoteSubLine,m.CashFlowClass,m.MappingRationale," +
                "ISNULL(tb.Balance,0) Balance FROM dbo.tblNORM_AccountMap m " +
                "LEFT JOIN (SELECT GlAccount,SUM(AccumBalance) Balance FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId=@import AND IsDeactivated=0 GROUP BY GlAccount) tb ON tb.GlAccount=m.GlCode " +
                "WHERE m.ConfigurationReleaseId=@release AND m.IsDeactivated=0 ORDER BY m.GlCode",
                NORMHelper.P("@import", importId), NORMHelper.P("@release", releaseId));
            DataTable lines = NORMHelper.Query(
                "SELECT LineCode,StatementCode,LineLabel FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND LineCode IS NOT NULL AND CalculationKind='Mapped' AND IsDeactivated=0 ORDER BY StatementCode,SeqNo",
                NORMHelper.P("@release", releaseId));
            DataTable typeLines = NORMHelper.Query(
                "SELECT DISTINCT m.AccountType,s.LineCode,s.StatementCode,s.LineLabel,s.SeqNo FROM dbo.tblNORM_AccountMap m " +
                "INNER JOIN dbo.tblNORM_StatementLine s ON s.ConfigurationReleaseId=m.ConfigurationReleaseId AND s.LineCode=m.StatementLine AND s.CalculationKind='Mapped' AND s.IsDeactivated=0 " +
                "WHERE m.ConfigurationReleaseId=@release AND m.IsDeactivated=0 AND m.AccountType IN ('Asset','Liability','Equity','Income','Expense') " +
                "ORDER BY m.AccountType,s.StatementCode,s.SeqNo,s.LineCode",
                NORMHelper.P("@release", releaseId));
            DataTable noteLines = NORMHelper.Query(
                "SELECT DISTINCT StatementLine,NoteSubLine FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND StatementLine IS NOT NULL AND NoteSubLine IS NOT NULL AND LTRIM(RTRIM(NoteSubLine))<>'' ORDER BY StatementLine,NoteSubLine",
                NORMHelper.P("@release", releaseId));
            DataTable cash = NORMHelper.Query(
                "SELECT DISTINCT CashFlowClass FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND CashFlowClass IS NOT NULL AND LTRIM(RTRIM(CashFlowClass))<>'' AND IsDeactivated=0 ORDER BY CashFlowClass",
                NORMHelper.P("@release", releaseId));

            using (ExcelPackage package = new ExcelPackage())
            {
                package.Workbook.Properties.Title = "NORM editable account mapping - " + NORMHelper.Str(release, "VersionCode");
                package.Workbook.Properties.Author = NORMHelper.CurrentUserId() ?? "unknown";
                ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Mappings");
                sheet.Cells[1, 1].Value = "NORM controlled account mapping";
                sheet.Cells[1, 1, 1, 8].Merge = true;
                sheet.Cells[1, 1].Style.Font.Bold = true; sheet.Cells[1, 1].Style.Font.Size = 18; sheet.Cells[1, 1].Style.Font.Color.SetColor(Color.White);
                sheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid; sheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(20, 47, 65));
                sheet.Cells[2, 1].Value = "Release ID"; sheet.Cells[2, 2].Value = releaseId;
                sheet.Cells[3, 1].Value = "Version"; sheet.Cells[3, 2].Value = NORMHelper.Str(release, "VersionCode");
                sheet.Cells[4, 1].Value = "Instructions"; sheet.Cells[4, 2].Value = "Edit the blue columns only. Use stable face-statement line codes from the Reference lists sheet. A reason is required for every changed row.";
                sheet.Cells[4, 2, 4, 8].Merge = true; sheet.Cells[4, 2].Style.WrapText = true;
                sheet.Cells[5, 1].Value = "Workbook format"; sheet.Cells[5, 2].Value = 3;
                string[] headers = { "G/L account", "Description", "Current TB balance ($)", "Account type", "Face statement line code", "Note sub-line", "Cash-flow class", "Change reason" };
                for (int i = 0; i < headers.Length; i++) sheet.Cells[HeaderRow, i + 1].Value = headers[i];
                sheet.Cells[HeaderRow, 1, HeaderRow, 8].Style.Font.Bold = true; sheet.Cells[HeaderRow, 1, HeaderRow, 8].Style.Font.Color.SetColor(Color.White);
                sheet.Cells[HeaderRow, 1, HeaderRow, 8].Style.Fill.PatternType = ExcelFillStyle.Solid; sheet.Cells[HeaderRow, 1, HeaderRow, 8].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(43, 109, 91));
                int row = HeaderRow + 1;
                foreach (DataRow source in mappings.Rows)
                {
                    sheet.Cells[row, 1].Value = Text(source, "GlCode"); sheet.Cells[row, 2].Value = Text(source, "GlDescription");
                    sheet.Cells[row, 3].Value = NORMHelper.Dec(source, "Balance"); sheet.Cells[row, 3].Style.Numberformat.Format = "#,##0.00;[Red](#,##0.00);-";
                    sheet.Cells[row, 4].Value = Text(source, "AccountType"); sheet.Cells[row, 5].Value = Text(source, "StatementLine");
                    sheet.Cells[row, 6].Value = Text(source, "NoteSubLine"); sheet.Cells[row, 7].Value = Text(source, "CashFlowClass"); sheet.Cells[row, 8].Value = "";
                    sheet.Cells[row, 4, row, 5].Style.Fill.PatternType = ExcelFillStyle.Solid; sheet.Cells[row, 4, row, 5].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(221, 235, 247));
                    sheet.Cells[row, 6, row, 8].Style.Fill.PatternType = ExcelFillStyle.Solid; sheet.Cells[row, 6, row, 8].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(221, 235, 247));
                    row++;
                }
                int last = Math.Max(HeaderRow + 1, row - 1);
                sheet.View.FreezePanes(HeaderRow + 1, 1); sheet.Cells[HeaderRow, 1, last, 8].AutoFilter = true;
                sheet.Column(1).Width = 18; sheet.Column(2).Width = 34; sheet.Column(3).Width = 19; sheet.Column(4).Width = 16; sheet.Column(5).Width = 34;
                sheet.Column(6).Width = 34; sheet.Column(7).Width = 28; sheet.Column(8).Width = 42;

                ExcelWorksheet reference = package.Workbook.Worksheets.Add("Reference lists");
                reference.Cells[1, 1].Value = "Account types"; reference.Cells[1, 3].Value = "Face statement line code"; reference.Cells[1, 4].Value = "Statement"; reference.Cells[1, 5].Value = "Label"; reference.Cells[1, 7].Value = "Cash-flow classes";
                string[] types = { "Asset", "Liability", "Equity", "Income", "Expense" };
                for (int i = 0; i < types.Length; i++) reference.Cells[i + 2, 1].Value = types[i];
                for (int i = 0; i < lines.Rows.Count; i++) { reference.Cells[i + 2, 3].Value = Text(lines.Rows[i], "LineCode"); reference.Cells[i + 2, 4].Value = Text(lines.Rows[i], "StatementCode"); reference.Cells[i + 2, 5].Value = Text(lines.Rows[i], "LineLabel"); }
                for (int i = 0; i < cash.Rows.Count; i++) reference.Cells[i + 2, 7].Value = Text(cash.Rows[i], "CashFlowClass");
                for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    int column = 11 + typeIndex;
                    reference.Cells[1, column].Value = types[typeIndex] + " face lines";
                    int targetRow = 2;
                    for (int lineIndex = 0; lineIndex < typeLines.Rows.Count; lineIndex++)
                    {
                        if (!String.Equals(Text(typeLines.Rows[lineIndex], "AccountType"), types[typeIndex], StringComparison.OrdinalIgnoreCase)) continue;
                        reference.Cells[targetRow++, column].Value = Text(typeLines.Rows[lineIndex], "LineCode");
                    }
                    if (targetRow == 2) reference.Cells[targetRow++, column].Value = "";
                    package.Workbook.Names.Add("FaceLines_" + types[typeIndex], reference.Cells[2, column, targetRow - 1, column]);
                }
                reference.Cells[1, 17].Value = "Face statement line code";
                reference.Cells[1, 18].Value = "Dependent note list";
                for (int lineIndex = 0; lineIndex < lines.Rows.Count; lineIndex++)
                {
                    string lineCode = Text(lines.Rows[lineIndex], "LineCode");
                    string rangeName = "NoteSubLines_" + (lineIndex + 1).ToString("0000", CultureInfo.InvariantCulture);
                    reference.Cells[lineIndex + 2, 17].Value = lineCode;
                    reference.Cells[lineIndex + 2, 18].Value = rangeName;
                    int listColumn = 20 + lineIndex;
                    reference.Cells[1, listColumn].Value = lineCode + " note sub-lines";
                    int targetRow = 2;
                    for (int noteIndex = 0; noteIndex < noteLines.Rows.Count; noteIndex++)
                    {
                        if (!String.Equals(Text(noteLines.Rows[noteIndex], "StatementLine"), lineCode, StringComparison.OrdinalIgnoreCase)) continue;
                        reference.Cells[targetRow++, listColumn].Value = Text(noteLines.Rows[noteIndex], "NoteSubLine");
                    }
                    if (targetRow == 2) reference.Cells[targetRow++, listColumn].Value = "";
                    package.Workbook.Names.Add(rangeName, reference.Cells[2, listColumn, targetRow - 1, listColumn]);
                }
                if (lines.Rows.Count > 0) package.Workbook.Names.Add("NoteLineLookup", reference.Cells[2, 17, lines.Rows.Count + 1, 18]);
                reference.Cells[1, 1, 1, Math.Max(18, 19 + lines.Rows.Count)].Style.Font.Bold = true; reference.Cells.AutoFitColumns();
                for (int hiddenColumn = 11; hiddenColumn <= 19 + lines.Rows.Count; hiddenColumn++) reference.Column(hiddenColumn).Hidden = true;
                if (mappings.Rows.Count > 0)
                {
                    var typeValidation = sheet.DataValidations.AddListValidation("D" + (HeaderRow + 1) + ":D" + last); typeValidation.Formula.ExcelFormula = "'Reference lists'!$A$2:$A$6";
                    var lineValidation = sheet.DataValidations.AddListValidation("E" + (HeaderRow + 1) + ":E" + last); lineValidation.Formula.ExcelFormula = "INDIRECT(\"FaceLines_\"&$D" + (HeaderRow + 1).ToString(CultureInfo.InvariantCulture) + ")";
                    if (lines.Rows.Count > 0) { var noteValidation = sheet.DataValidations.AddListValidation("F" + (HeaderRow + 1) + ":F" + last); noteValidation.Formula.ExcelFormula = "INDIRECT(VLOOKUP($E" + (HeaderRow + 1).ToString(CultureInfo.InvariantCulture) + ",NoteLineLookup,2,FALSE))"; noteValidation.AllowBlank = true; }
                    if (cash.Rows.Count > 0) { var cashValidation = sheet.DataValidations.AddListValidation("G" + (HeaderRow + 1) + ":G" + last); cashValidation.Formula.ExcelFormula = "'Reference lists'!$G$2:$G$" + (cash.Rows.Count + 1).ToString(CultureInfo.InvariantCulture); cashValidation.AllowBlank = true; }
                }
                return package.GetAsByteArray();
            }
        }

        public static NORMMappingUploadOutcome ApplyWorkbook(int releaseId, byte[] content, string fileName, string user)
        {
            if (content == null || content.Length == 0) throw new InvalidOperationException("Choose a non-empty Excel mapping workbook.");
            if (!String.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The mapping workbook must be an .xlsx file.");
            DraftRelease(releaseId);
            string hash = NORMCrypto.Sha256(content);
            Dictionary<string, DataRow> existing = NORMHelper.Query("SELECT * FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0", NORMHelper.P("@release", releaseId))
                .AsEnumerable().ToDictionary(x => Text(x, "GlCode"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> validLines = new HashSet<string>(NORMHelper.Query(
                "SELECT LineCode FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND LineCode IS NOT NULL AND CalculationKind='Mapped' AND IsDeactivated=0",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "LineCode")), StringComparer.OrdinalIgnoreCase);
            HashSet<string> validTypeLines = new HashSet<string>(NORMHelper.Query(
                "SELECT DISTINCT AccountType,StatementLine FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND AccountType IN ('Asset','Liability','Equity','Income','Expense') AND StatementLine IS NOT NULL",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "AccountType") + "|" + Text(x, "StatementLine")), StringComparer.OrdinalIgnoreCase);
            HashSet<string> validNoteLines = new HashSet<string>(NORMHelper.Query(
                "SELECT DISTINCT StatementLine,NoteSubLine FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND StatementLine IS NOT NULL AND NoteSubLine IS NOT NULL AND LTRIM(RTRIM(NoteSubLine))<>''",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "StatementLine") + "|" + Text(x, "NoteSubLine")), StringComparer.OrdinalIgnoreCase);
            List<MappingRow> rows = new List<MappingRow>();
            List<string> errors = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (ExcelPackage package = new ExcelPackage(new MemoryStream(content)))
            {
                ExcelWorksheet sheet = package.Workbook.Worksheets["Mappings"];
                if (sheet == null) throw new InvalidOperationException("The workbook does not contain the required Mappings sheet.");
                int workbookRelease; if (!Int32.TryParse(Convert.ToString(sheet.Cells[2, 2].Value), out workbookRelease) || workbookRelease != releaseId)
                    throw new InvalidOperationException("This workbook belongs to a different configuration release. Download a fresh workbook for this draft.");
                if (!String.Equals(Cell(sheet, 5, 2), "3", StringComparison.Ordinal))
                    throw new InvalidOperationException("This is an older mapping workbook layout. Download a fresh workbook for this draft before making changes.");
                int last = sheet.Dimension == null ? HeaderRow : sheet.Dimension.End.Row;
                for (int rowNumber = HeaderRow + 1; rowNumber <= last; rowNumber++)
                {
                    string gl = Cell(sheet, rowNumber, 1); if (gl.Length == 0) continue;
                    if (!seen.Add(gl)) { errors.Add("Row " + rowNumber + ": G/L " + gl + " appears more than once."); continue; }
                    DataRow before; if (!existing.TryGetValue(gl, out before)) { errors.Add("Row " + rowNumber + ": G/L " + gl + " is not part of this draft release."); continue; }
                    MappingRow item = new MappingRow { RowNumber = rowNumber, GlCode = gl, AccountType = Cell(sheet, rowNumber, 4), StatementLine = Cell(sheet, rowNumber, 5), NoteSubLine = Cell(sheet, rowNumber, 6), CashFlowClass = Cell(sheet, rowNumber, 7), Reason = Cell(sheet, rowNumber, 8), Before = before };
                    if (item.AccountType.Length > 0 && !AccountTypes.Contains(item.AccountType)) errors.Add("Row " + rowNumber + ": account type is not valid.");
                    if (item.StatementLine.Length > 0 && !validLines.Contains(item.StatementLine)) errors.Add("Row " + rowNumber + ": face statement line code '" + item.StatementLine + "' is not valid for this release.");
                    if (item.AccountType.Length > 0 && item.StatementLine.Length > 0 && !validTypeLines.Contains(item.AccountType + "|" + item.StatementLine)) errors.Add("Row " + rowNumber + ": face statement line '" + item.StatementLine + "' is not available for account type " + item.AccountType + ".");
                    if (item.NoteSubLine.Length > 0 && item.StatementLine.Length == 0) errors.Add("Row " + rowNumber + ": select a face statement line before selecting a note sub-line.");
                    if (item.NoteSubLine.Length > 0 && item.StatementLine.Length > 0 && !validNoteLines.Contains(item.StatementLine + "|" + item.NoteSubLine)) errors.Add("Row " + rowNumber + ": note sub-line '" + item.NoteSubLine + "' is not available for face statement line '" + item.StatementLine + "'.");
                    if (item.NoteSubLine.Length > 240) errors.Add("Row " + rowNumber + ": note sub-line exceeds 240 characters.");
                    if (item.CashFlowClass.Length > 120) errors.Add("Row " + rowNumber + ": cash-flow class exceeds 120 characters.");
                    item.Changed = Different(before, item);
                    if (item.Changed && (item.Reason.Length < 5 || item.Reason.Length > 500)) errors.Add("Row " + rowNumber + ": enter a change reason of between 5 and 500 characters.");
                    rows.Add(item);
                }
            }
            foreach (string gl in existing.Keys) if (!seen.Contains(gl)) errors.Add("G/L " + gl + " is missing from the workbook.");
            if (errors.Count > 0) throw new InvalidOperationException("The mapping workbook was not applied: " + String.Join(" ", errors.Take(12).ToArray()) + (errors.Count > 12 ? " " + (errors.Count - 12) + " more error(s)." : ""));
            int changed = rows.Count(x => x.Changed);
            if (changed == 0) throw new InvalidOperationException("No mapping changes were found in the workbook.");

            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    foreach (MappingRow item in rows.Where(x => x.Changed))
                    {
                        NORMHelper.Exec(connection, transaction,
                            "UPDATE dbo.tblNORM_AccountMap SET AccountType=@type,StatementLine=@line,NoteSubLine=@note,CashFlowClass=@cash,MappingRationale=@reason WHERE ConfigurationReleaseId=@release AND GlCode=@gl AND IsDeactivated=0",
                            NORMHelper.P("@type", Null(item.AccountType)), NORMHelper.P("@line", Null(item.StatementLine)), NORMHelper.P("@note", Null(item.NoteSubLine)), NORMHelper.P("@cash", Null(item.CashFlowClass)), NORMHelper.P("@reason", item.Reason), NORMHelper.P("@release", releaseId), NORMHelper.P("@gl", item.GlCode));
                        NORMHelper.Exec(connection, transaction,
                            "INSERT dbo.tblNORM_MappingChange (ConfigurationReleaseId,GlCode,BeforeAccountType,AfterAccountType,BeforeStatementLine,AfterStatementLine,BeforeNoteSubLine,AfterNoteSubLine,BeforeCashFlowClass,AfterCashFlowClass,ChangeReason,WorkbookHash,ChangedBy) " +
                            "VALUES (@release,@gl,@bt,@at,@bl,@al,@bn,@an,@bc,@ac,@reason,@hash,@user)",
                            NORMHelper.P("@release", releaseId), NORMHelper.P("@gl", item.GlCode), NORMHelper.P("@bt", Text(item.Before, "AccountType")), NORMHelper.P("@at", Null(item.AccountType)),
                            NORMHelper.P("@bl", Text(item.Before, "StatementLine")), NORMHelper.P("@al", Null(item.StatementLine)), NORMHelper.P("@bn", Text(item.Before, "NoteSubLine")), NORMHelper.P("@an", Null(item.NoteSubLine)),
                            NORMHelper.P("@bc", Text(item.Before, "CashFlowClass")), NORMHelper.P("@ac", Null(item.CashFlowClass)), NORMHelper.P("@reason", item.Reason), NORMHelper.P("@hash", hash), NORMHelper.P("@user", user));
                    }
                    NORMHelper.Exec(connection, transaction, "UPDATE dbo.tblNORM_ConfigurationRelease SET ContentHash=@hash,ReviewedBy=NULL,ReviewedUtc=NULL WHERE ConfigurationReleaseId=@release AND StatusCode='Draft'", NORMHelper.P("@hash", hash), NORMHelper.P("@release", releaseId));
                    Audit(connection, transaction, "MAPPING_WORKBOOK_APPLIED", releaseId, changed.ToString(CultureInfo.InvariantCulture) + " account mapping(s) changed from workbook " + Path.GetFileName(fileName) + "; SHA-256 " + hash + ".", user);
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
            return new NORMMappingUploadOutcome { ChangedCount = changed, WorkbookHash = hash, Validation = Validate(releaseId) };
        }

        public static NORMMappingValidation Validate(int releaseId)
        {
            DataRow release = Release(releaseId);
            NORMMappingValidation result = new NORMMappingValidation();
            DataTable checks = NORMHelper.Query(
                "SELECT COUNT(*) MappingCount," +
                "SUM(CASE WHEN (AccountType IS NULL OR LTRIM(RTRIM(AccountType))='' OR StatementLine IS NULL OR LTRIM(RTRIM(StatementLine))='') THEN 1 ELSE 0 END) UnmappedCount," +
                "SUM(CASE WHEN AccountType IS NOT NULL AND AccountType NOT IN ('Asset','Liability','Equity','Income','Expense') THEN 1 ELSE 0 END) InvalidTypes " +
                "FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0",
                NORMHelper.P("@release", releaseId));
            DataRow row = checks.Rows[0]; result.MappingCount = NORMHelper.Int(row, "MappingCount"); result.UnmappedCount = NORMHelper.Int(row, "UnmappedCount");
            int invalid = NORMHelper.Int(row, "InvalidTypes");
            int duplicates = Convert.ToInt32(NORMHelper.Scalar("SELECT COUNT(*) FROM (SELECT GlCode FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 GROUP BY GlCode HAVING COUNT(*)>1) d", NORMHelper.P("@release", releaseId)));
            int unknownLines = Convert.ToInt32(NORMHelper.Scalar(
                "SELECT COUNT(*) FROM dbo.tblNORM_AccountMap m LEFT JOIN dbo.tblNORM_StatementLine s ON s.ConfigurationReleaseId=m.ConfigurationReleaseId AND s.LineCode=m.StatementLine AND s.IsDeactivated=0 " +
                "WHERE m.ConfigurationReleaseId=@release AND m.IsDeactivated=0 AND m.StatementLine IS NOT NULL AND s.StatementLineId IS NULL", NORMHelper.P("@release", releaseId)));
            result.ChangedCount = Convert.ToInt32(NORMHelper.Scalar("SELECT COUNT(DISTINCT GlCode) FROM dbo.tblNORM_MappingChange WHERE ConfigurationReleaseId=@release", NORMHelper.P("@release", releaseId)));
            if (result.MappingCount == 0) result.Errors.Add("The release contains no account mappings.");
            if (invalid > 0) result.Errors.Add(invalid + " mapping(s) have an invalid account type.");
            if (duplicates > 0) result.Errors.Add(duplicates + " G/L account(s) have duplicate active mappings.");
            if (unknownLines > 0) result.Errors.Add(unknownLines + " mapping(s) refer to a line that is not in the statement template.");
            if (result.UnmappedCount > 0) result.Warnings.Add(result.UnmappedCount + " account(s) do not yet have both an account type and face-statement line. They remain visible in the unmapped pool.");
            if (NORMHelper.Str(release, "StatusCode") == "Draft" && result.ChangedCount == 0) result.Warnings.Add("No mapping changes have been recorded in this draft.");
            result.ErrorCount = result.Errors.Count;
            return result;
        }

        public static DataTable Impact(int releaseId)
        {
            DataRow release = Release(releaseId); int parent = NORMHelper.Int(release, "ParentConfigurationReleaseId");
            if (parent == 0) return new DataTable();
            int importId = LatestImportId(parent, NORMHelper.Int(release, "FinancialYear"), NORMHelper.Str(release, "EntityCode"));
            return NORMHelper.Query(
                "SELECT d.GlCode,ISNULL(tb.Balance,0) Balance,ISNULL(p.StatementLine,'Unmapped') PreviousLine,ISNULL(d.StatementLine,'Unmapped') DraftLine," +
                "ISNULL(p.NoteSubLine,'No note mapping') PreviousNote,ISNULL(d.NoteSubLine,'No note mapping') DraftNote,ISNULL(d.MappingRationale,'') ChangeReason " +
                "FROM dbo.tblNORM_AccountMap d INNER JOIN dbo.tblNORM_AccountMap p ON p.ConfigurationReleaseId=@parent AND p.GlCode=d.GlCode AND p.IsDeactivated=0 " +
                "LEFT JOIN (SELECT GlAccount,SUM(AccumBalance) Balance FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId=@import AND IsDeactivated=0 GROUP BY GlAccount) tb ON tb.GlAccount=d.GlCode " +
                "WHERE d.ConfigurationReleaseId=@release AND d.IsDeactivated=0 AND (ISNULL(d.AccountType,'')<>ISNULL(p.AccountType,'') OR ISNULL(d.StatementLine,'')<>ISNULL(p.StatementLine,'') OR ISNULL(d.NoteSubLine,'')<>ISNULL(p.NoteSubLine,'') OR ISNULL(d.CashFlowClass,'')<>ISNULL(p.CashFlowClass,'')) " +
                "ORDER BY ABS(ISNULL(tb.Balance,0)) DESC,d.GlCode",
                NORMHelper.P("@parent", parent), NORMHelper.P("@import", importId), NORMHelper.P("@release", releaseId));
        }

        public static void Approve(int releaseId, bool warningsAcknowledged, string user)
        {
            if (!NORMHelper.HasAdminAccess()) throw new UnauthorizedAccessException("Administrator access is required to approve a mapping release.");
            DataRow release = DraftRelease(releaseId); NORMMappingValidation validation = Validate(releaseId);
            if (!validation.CanApprove) throw new InvalidOperationException("Resolve all mapping validation errors before approval.");
            if (validation.ChangedCount == 0) throw new InvalidOperationException("The draft contains no recorded mapping changes.");
            if (validation.Warnings.Count > 0 && !warningsAcknowledged) throw new InvalidOperationException("Acknowledge the mapping warnings before approval.");
            string contentHash = MappingHash(releaseId);
            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    NORMHelper.Exec(connection, transaction,
                        "UPDATE dbo.tblNORM_ConfigurationRelease SET StatusCode='Approved',ContentHash=@hash,ReviewedBy=@user,ReviewedUtc=SYSUTCDATETIME(),ApprovedBy=@user,ApprovedUtc=SYSUTCDATETIME() WHERE ConfigurationReleaseId=@release AND StatusCode='Draft'",
                        NORMHelper.P("@hash", contentHash), NORMHelper.P("@user", user), NORMHelper.P("@release", releaseId));
                    Audit(connection, transaction, "MAPPING_RELEASE_APPROVED", releaseId,
                        "Release " + NORMHelper.Str(release, "VersionCode") + " approved with " + validation.ChangedCount + " changed account(s); content SHA-256 " + contentHash + ".", user);
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
        }

        public static NORMImportOutcome RecalculateLatest(int releaseId, string user)
        {
            DataRow release = Release(releaseId);
            if (!String.Equals(NORMHelper.Str(release, "StatusCode"), "Approved", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Approve the mapping release before recalculating.");
            int parent = NORMHelper.Int(release, "ParentConfigurationReleaseId");
            int sourceImportId = LatestImportId(parent, NORMHelper.Int(release, "FinancialYear"), NORMHelper.Str(release, "EntityCode"));
            if (sourceImportId == 0) throw new InvalidOperationException("No prior controlled trial balance is available to recalculate. Import a trial balance against this release instead.");
            DataRow source = NORMHelper.Query("SELECT * FROM dbo.tblNORM_Import WHERE ImportId=@import", NORMHelper.P("@import", sourceImportId)).Rows[0];
            int importId;
            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    string fingerprint = NORMCrypto.Sha256(Text(source, "DataFingerprint") + "|RELEASE|" + releaseId.ToString(CultureInfo.InvariantCulture));
                    importId = NORMHelper.InsertId(connection, transaction,
                        "INSERT dbo.tblNORM_Import (ConfigurationReleaseId,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,DataFingerprint,SourceFileBytes,[RowCount],TotalDebit,TotalCredit,NetBalance,StatusCode,IsTestBreak,ParentImportId,ImportedBy) " +
                        "SELECT @release,FinancialYear,EntityCode,SourceType,SourceFileName,SourceFileHash,@fingerprint,SourceFileBytes,[RowCount],TotalDebit,TotalCredit,NetBalance,'Imported',0,ImportId,@user FROM dbo.tblNORM_Import WHERE ImportId=@source",
                        NORMHelper.P("@release", releaseId), NORMHelper.P("@fingerprint", fingerprint), NORMHelper.P("@user", user), NORMHelper.P("@source", sourceImportId));
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_ImportFile (ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,[RowCount],IsStatementInput,FileContent) " +
                        "SELECT @target,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,[RowCount],IsStatementInput,FileContent FROM dbo.tblNORM_ImportFile WHERE ImportId=@source",
                        NORMHelper.P("@target", importId), NORMHelper.P("@source", sourceImportId));
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_TrialBalanceRow (ImportId,SourceRowNo,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic) " +
                        "SELECT @target,SourceRowNo,SourceLedger,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId=@source AND IsDeactivated=0",
                        NORMHelper.P("@target", importId), NORMHelper.P("@source", sourceImportId));
                    Audit(connection, transaction, "MAPPING_RECALCULATION_CREATED", releaseId,
                        "Source import " + sourceImportId + " copied without alteration to import " + importId + " for recalculation under approved release " + releaseId + ".", user);
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
            int runId = NORMStatementEngine.Run(importId, user);
            return new NORMImportOutcome { ImportId = importId, CalculationRunId = runId, RowCount = NORMHelper.Int(source, "RowCount"), TotalDebit = NORMHelper.Dec(source, "TotalDebit"), TotalCredit = NORMHelper.Dec(source, "TotalCredit"), NetBalance = NORMHelper.Dec(source, "NetBalance") };
        }

        private static string MappingHash(int releaseId)
        {
            DataTable rows = NORMHelper.Query("SELECT GlCode,ISNULL(AccountType,'') AccountType,ISNULL(StatementLine,'') StatementLine,ISNULL(NoteSubLine,'') NoteSubLine,ISNULL(CashFlowClass,'') CashFlowClass FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY GlCode", NORMHelper.P("@release", releaseId));
            StringBuilder value = new StringBuilder(); foreach (DataRow row in rows.Rows) value.Append(Text(row, "GlCode")).Append('|').Append(Text(row, "AccountType")).Append('|').Append(Text(row, "StatementLine")).Append('|').Append(Text(row, "NoteSubLine")).Append('|').Append(Text(row, "CashFlowClass")).Append('\n');
            return NORMCrypto.Sha256(value.ToString());
        }

        private static DataRow DraftRelease(int releaseId)
        {
            DataRow row = Release(releaseId); if (!String.Equals(NORMHelper.Str(row, "StatusCode"), "Draft", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only a draft configuration release can be changed."); return row;
        }
        private static DataRow Release(int releaseId)
        {
            DataTable table = NORMHelper.Query("SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId=@release AND IsDeactivated=0", NORMHelper.P("@release", releaseId));
            if (table.Rows.Count == 0) throw new InvalidOperationException("The configuration release was not found."); return table.Rows[0];
        }
        private static int LatestImportId(int preferredRelease, int financialYear, string entity)
        {
            object value = NORMHelper.Scalar("SELECT TOP 1 i.ImportId FROM dbo.tblNORM_Import i INNER JOIN dbo.tblNORM_CalculationRun r ON r.ImportId=i.ImportId WHERE i.FinancialYear=@fy AND i.EntityCode=@entity AND i.IsTestBreak=0 AND i.IsDeactivated=0 AND r.StatusCode='Complete' AND r.IsDeactivated=0 ORDER BY CASE WHEN i.ConfigurationReleaseId=@preferred THEN 0 ELSE 1 END,i.ImportId DESC", NORMHelper.P("@fy", financialYear), NORMHelper.P("@entity", entity), NORMHelper.P("@preferred", preferredRelease));
            return value == null ? 0 : Convert.ToInt32(value);
        }
        private static bool TableExists(OleDbConnection c, OleDbTransaction t, string name) { return Convert.ToInt32(NORMHelper.Scalar(c, t, "SELECT CASE WHEN OBJECT_ID('dbo." + name.Replace("'", "''") + "','U') IS NULL THEN 0 ELSE 1 END")) == 1; }
        private static void Audit(OleDbConnection c, OleDbTransaction t, string code, int release, string detail, string user) { NORMHelper.Exec(c, t, "INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy) VALUES(@code,'ConfigurationRelease',@id,@detail,@user)", NORMHelper.P("@code", code), NORMHelper.P("@id", release.ToString(CultureInfo.InvariantCulture)), NORMHelper.P("@detail", detail.Length > 2000 ? detail.Substring(0, 2000) : detail), NORMHelper.P("@user", user)); }
        private static string Text(DataRow row, string column) { return NORMHelper.Str(row, column) ?? ""; }
        private static string Cell(ExcelWorksheet sheet, int row, int column) { return Convert.ToString(sheet.Cells[row, column].Value, CultureInfo.InvariantCulture).Trim(); }
        private static object Null(string value) { return String.IsNullOrWhiteSpace(value) ? null : (object)value.Trim(); }
        private static bool Different(DataRow before, MappingRow after) { return !Same(Text(before, "AccountType"), after.AccountType) || !Same(Text(before, "StatementLine"), after.StatementLine) || !Same(Text(before, "NoteSubLine"), after.NoteSubLine) || !Same(Text(before, "CashFlowClass"), after.CashFlowClass); }
        private static bool Same(string a, string b) { return String.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase); }
        private sealed class MappingRow { public int RowNumber; public string GlCode; public string AccountType; public string StatementLine; public string NoteSubLine; public string CashFlowClass; public string Reason; public DataRow Before; public bool Changed; }
    }
}
