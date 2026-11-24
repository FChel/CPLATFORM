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
        public bool HierarchyChanged;
        public int UnmappedCount;
        public int ErrorCount;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool CanApprove { get { return ErrorCount == 0; } }
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
        private const int HeaderRow = 4;
        private const int LegacyHeaderRow = 7;
        private const string WorkbookMetadata = "_NORM";
        private const string WorkbookFormat = "4";
        private const string HierarchyWorkbookFormat = "5";
        private const string DisplaySeparator = " | ";
        private const string CashFlowReferenceDetail = "StatementCode='SCFREF' AND LineType='cashflow-detail'";
        internal const string EmployeeHierarchyEvent = "EMPLOYEE_HIERARCHY_CHANGED";
        private const string ReportingHierarchyEvent = "NIHRACS_HIERARCHY_CHANGED";
        private const string EmployeeParentFilter =
            "ConfigurationReleaseId=@release AND StatementCode='SOCI' AND LineCode='Employee benefits' AND IsDeactivated=0";
        private static readonly string[] WorkbookHeaders = {
            "G/L account", "Description", "Account type", "Face statement line code",
            "Note sub-line", "Cash-flow class", "Change reason"
        };
        private static readonly string[] HierarchyWorkbookHeaders = {
            "GL Account", "Description", "Reporting Classification", "Cash Flow Classification", "Change Reason"
        };
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
            NORMHelper.RequireEntityAccess(NORMHelper.Str(parent, "EntityCode"));
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

                    CopyReleaseContent(connection, transaction, parentReleaseId, releaseId, user, IsNihracs(parent));
                    Audit(connection, transaction, "MAPPING_DRAFT_CREATED", releaseId,
                        "Draft " + version + " created from approved release " + parentReleaseId.ToString(CultureInfo.InvariantCulture) + ". Reason: " + reason, user);
                    transaction.Commit();
                    return releaseId;
                }
                catch { transaction.Rollback(); throw; }
            }
        }

        public static int GetOrCreateWorkingDraft(string entity, int year, string user)
        {
            NORMHelper.RequireEntityAccess(entity);
            DataTable starts = NORMHelper.Query(
                "SELECT TOP 1 c.ConfigurationReleaseId,c.StatusCode FROM dbo.tblNORM_ConfigurationRelease c " +
                "OUTER APPLY (SELECT MAX(m.MappingChangeId) LastChange,MAX(m.ChangedUtc) ChangedUtc FROM dbo.tblNORM_MappingChange m " +
                "WHERE m.ConfigurationReleaseId=c.ConfigurationReleaseId) submitted " +
                "OUTER APPLY (SELECT MAX(a.PerformedUtc) ChangedUtc FROM dbo.tblNORM_AuditEvent a " +
                "WHERE a.EntityType='ConfigurationRelease' AND a.EntityId=CONVERT(varchar(20),c.ConfigurationReleaseId) " +
                "AND a.EventCode IN ('" + EmployeeHierarchyEvent + "','" + ReportingHierarchyEvent + "')) hierarchy " +
                "WHERE c.EntityCode=@entity AND c.FinancialYear=@year AND c.IsDeactivated=0 " +
                "AND (c.StatusCode='Approved' OR (c.StatusCode='Draft' AND (submitted.LastChange IS NOT NULL OR hierarchy.ChangedUtc IS NOT NULL))) " +
                "ORDER BY CASE WHEN MAX(hierarchy.ChangedUtc) OVER () IS NOT NULL THEN " +
                "CASE WHEN hierarchy.ChangedUtc > submitted.ChangedUtc THEN hierarchy.ChangedUtc " +
                "ELSE COALESCE(submitted.ChangedUtc,hierarchy.ChangedUtc) END END DESC," +
                "submitted.LastChange DESC,CASE WHEN EXISTS (SELECT 1 FROM dbo.tblNORM_AccountMap a " +
                "WHERE a.ConfigurationReleaseId=c.ConfigurationReleaseId AND a.IsDeactivated=0) THEN 0 ELSE 1 END,c.ConfigurationReleaseId DESC",
                NORMHelper.P("@entity", entity), NORMHelper.P("@year", year));
            if (starts.Rows.Count == 0)
                return Convert.ToInt32(NORMHelper.Scalar(
                    "SELECT TOP 1 ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease " +
                    "WHERE EntityCode=@entity AND FinancialYear=@year AND StatusCode='Draft' AND IsDeactivated=0 " +
                    "ORDER BY ConfigurationReleaseId DESC",
                    NORMHelper.P("@entity", entity), NORMHelper.P("@year", year)));

            DataRow start = starts.Rows[0];
            int parentId = NORMHelper.Int(start, "ConfigurationReleaseId");
            if (Text(start, "StatusCode") == "Draft") return parentId;
            object draftValue = NORMHelper.Scalar(
                "SELECT TOP 1 ConfigurationReleaseId FROM dbo.tblNORM_ConfigurationRelease " +
                "WHERE ParentConfigurationReleaseId=@parent AND StatusCode='Draft' AND IsDeactivated=0 ORDER BY ConfigurationReleaseId DESC",
                NORMHelper.P("@parent", parentId));
            if (draftValue != null) return Convert.ToInt32(draftValue);

            string version = "map-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            return CreateDraft(parentId, version, "Account mapping workbook update.", user);
        }

        public static int CreateInitialDraft(string entity, int year, string user)
        {
            if (!NORMHelper.HasAdminAccess())
                throw new UnauthorizedAccessException("Administrator access is required to create an initial mapping draft.");
            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    if (NORMAnnualSourceConfiguration.Load(entity, year, connection, transaction) == null)
                        throw new InvalidOperationException("An approved annual source configuration is required for the initial mapping draft.");
                    DataTable existing = NORMHelper.Query(connection, transaction,
                        "SELECT TOP 1 ConfigurationReleaseId,ParentConfigurationReleaseId,StatusCode " +
                        "FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK) " +
                        "WHERE EntityCode=@entity AND FinancialYear=@year AND StatusCode IN ('Draft','Approved') " +
                        "AND IsDeactivated=0 ORDER BY ConfigurationReleaseId DESC",
                        NORMHelper.P("@entity", entity), NORMHelper.P("@year", year));
                    if (existing.Rows.Count > 0)
                    {
                        DataRow draft = existing.Rows[0];
                        if (!IsParentlessDraft(draft))
                            throw new InvalidOperationException("A mapping configuration already exists for this agency and year. Select it again.");
                        transaction.Commit();
                        return NORMHelper.Int(draft, "ConfigurationReleaseId");
                    }

                    const string reason = "Initial account mapping workbook.";
                    int releaseId = NORMHelper.InsertId(connection, transaction,
                        "INSERT dbo.tblNORM_ConfigurationRelease " +
                        "(FinancialYear,EntityCode,VersionCode,ReleaseLabel,StatusCode,CreatedBy,ChangeReason) " +
                        "VALUES (@year,@entity,@version,'Initial mapping draft','Draft',@user,@reason)",
                        NORMHelper.P("@year", year), NORMHelper.P("@entity", entity),
                        NORMHelper.P("@version", "map-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)),
                        NORMHelper.P("@user", user), NORMHelper.P("@reason", reason));
                    Audit(connection, transaction, "MAPPING_DRAFT_CREATED", releaseId, reason, user);
                    transaction.Commit();
                    return releaseId;
                }
                catch { transaction.Rollback(); throw; }
            }
        }

        private static bool IsParentlessDraft(DataRow release)
        {
            return release.IsNull("ParentConfigurationReleaseId") && Text(release, "StatusCode") == "Draft";
        }

        private static void CopyReleaseContent(OleDbConnection c, OleDbTransaction t, int source, int target, string user, bool nihracs)
        {
            NORMHelper.Exec(c, t,
                "INSERT dbo.tblNORM_EntityLedger (ConfigurationReleaseId,FinancialYear,EntityCode,SourceCompanyCode,SourceLedger,ReportingStream,IsRequired,IsDeactivated) " +
                "SELECT @target,FinancialYear,EntityCode,SourceCompanyCode,SourceLedger,ReportingStream,IsRequired,IsDeactivated " +
                "FROM dbo.tblNORM_EntityLedger WHERE ConfigurationReleaseId=@source",
                NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            NORMHelper.Exec(c, t,
                "INSERT dbo.tblNORM_AccountMap (ConfigurationReleaseId,FinancialYear,EntityCode,GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass,MappingRationale,IsDeactivated) " +
                "SELECT @target,FinancialYear,EntityCode,GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass,MappingRationale,IsDeactivated " +
                "FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@source",
                NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            string hierarchyColumn = NORMStatementEngine.HasEmployeeHierarchy(c, t) ? ",ParentLineCode" : "";
            if (HasMappingDescriptions(c, t)) hierarchyColumn += ",MappingDescription";
            NORMHelper.Exec(c, t,
                "INSERT dbo.tblNORM_StatementLine (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,NaturalSign,CalculationKind,FormulaSpec,IsClickable,IsDeactivated" + hierarchyColumn + ") " +
                "SELECT @target,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,NoteRef,NaturalSign,CalculationKind,FormulaSpec,IsClickable,IsDeactivated" + hierarchyColumn + " " +
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
            {
                string noteSource = nihracs && NORMReportingFramework.HasHierarchyNoteSource(c, t) ? ",NoteSourceLineCode" : "";
                NORMHelper.Exec(c, t,
                    "INSERT dbo.tblNORM_DisclosureRule (ConfigurationReleaseId,DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,TriggerCode,IsBaseRequired,RequiresNarrative,SortOrder,GuidanceText,IsDeactivated" + noteSource + ") " +
                    "SELECT @target,DisclosureCode,SectionCode,SectionTitle,NoteRef,DisclosureTitle,TriggerCode,IsBaseRequired,RequiresNarrative,SortOrder,GuidanceText,IsDeactivated" + noteSource + " FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=@source",
                    NORMHelper.P("@target", target), NORMHelper.P("@source", source));
            }
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
            Dictionary<string, string> hierarchy = IsNihracs(release) ? NORMStatementEngine.LoadHierarchyAccountTypes(releaseId) : null;
            DataTable mappings = NORMHelper.Query(
                "SELECT GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass " +
                "FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY GlCode",
                NORMHelper.P("@release", releaseId));
            if (hierarchy != null)
            {
                DataTable options = NORMHelper.Query(
                    "SELECT s.LineCode,s.LineLabel,p.LineLabel AS ParentLabel," +
                    (HasMappingDescriptions(null, null) ? "s.MappingDescription" : "NULL AS MappingDescription") +
                    " FROM dbo.tblNORM_StatementLine s LEFT JOIN dbo.tblNORM_StatementLine p ON p.ConfigurationReleaseId=s.ConfigurationReleaseId " +
                    "AND p.LineCode=s.ParentLineCode AND p.IsDeactivated=0 WHERE s.ConfigurationReleaseId=@release AND s.IsDeactivated=0 " +
                    "AND s.LineType='hierarchy-detail' AND s.CalculationKind='Mapped' ORDER BY s.StatementCode,s.SeqNo",
                    NORMHelper.P("@release", releaseId));
                var reporting = options.AsEnumerable().ToDictionary(row => Text(row, "LineCode"), MappingDescription, StringComparer.OrdinalIgnoreCase);
                return BuildWorkbook(releaseId, mappings, null, null, null, null, reporting, CashFlowOptions(releaseId));
            }
            DataTable cash = NORMHelper.Query(
                "SELECT DISTINCT CashFlowClass FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND CashFlowClass IS NOT NULL AND LTRIM(RTRIM(CashFlowClass))<>'' AND IsDeactivated=0 ORDER BY CashFlowClass",
                NORMHelper.P("@release", releaseId));
            DataTable lines = NORMHelper.Query(
                "SELECT LineCode,StatementCode,LineLabel FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND LineCode IS NOT NULL AND CalculationKind='Mapped' AND IsDeactivated=0 ORDER BY StatementCode,SeqNo",
                NORMHelper.P("@release", releaseId));
            DataTable typeLines = NORMHelper.Query(
                "SELECT DISTINCT m.AccountType,s.LineCode,s.StatementCode,s.LineLabel,s.SeqNo FROM dbo.tblNORM_AccountMap m " +
                "INNER JOIN dbo.tblNORM_StatementLine s ON s.ConfigurationReleaseId=m.ConfigurationReleaseId AND s.LineCode=m.StatementLine AND s.CalculationKind='Mapped' AND s.IsDeactivated=0 " +
                "WHERE m.ConfigurationReleaseId=@release AND m.IsDeactivated=0 AND m.AccountType IN ('Asset','Liability','Equity','Income','Expense') " +
                "UNION SELECT 'Expense',LineCode,StatementCode,LineLabel,SeqNo FROM dbo.tblNORM_StatementLine " +
                "WHERE ConfigurationReleaseId=@detailRelease AND IsDeactivated=0 AND LineType='employee-detail' AND CalculationKind='Mapped' " +
                "ORDER BY AccountType,StatementCode,SeqNo,LineCode",
                NORMHelper.P("@release", releaseId), NORMHelper.P("@detailRelease", releaseId));
            DataTable noteLines = NORMHelper.Query(
                "SELECT DISTINCT StatementLine,NoteSubLine FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND StatementLine IS NOT NULL AND NoteSubLine IS NOT NULL AND LTRIM(RTRIM(NoteSubLine))<>'' ORDER BY StatementLine,NoteSubLine",
                NORMHelper.P("@release", releaseId));
            return BuildWorkbook(releaseId, mappings, lines, typeLines, noteLines, cash);
        }

        public static byte[] BuildBlankWorkbook()
        {
            return BuildWorkbook(0, new DataTable(), new DataTable(), new DataTable(), new DataTable(), new DataTable());
        }

        private static Dictionary<string, string> CashFlowOptions(int releaseId)
        {
            DataTable lines = NORMHelper.Query(
                "SELECT LineCode,LineLabel," + MappingDescriptionColumn(null, null) + " FROM dbo.tblNORM_StatementLine " +
                "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 AND LineCode IS NOT NULL AND ((" +
                CashFlowReferenceDetail + ") OR (StatementCode='SCF' AND CalculationKind<>'Heading')) " +
                "ORDER BY CASE WHEN StatementCode='SCFREF' THEN 0 ELSE 1 END,SeqNo", NORMHelper.P("@release", releaseId));
            return lines.AsEnumerable()
                .GroupBy(row => Text(row, "LineCode"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group =>
                {
                    DataRow row = group.First();
                    string description = Text(row, "MappingDescription");
                    return description.Length == 0 ? Text(row, "LineLabel") : description;
                }, StringComparer.OrdinalIgnoreCase);
        }

        private static byte[] BuildWorkbook(int releaseId, DataTable mappings, DataTable lines, DataTable typeLines,
            DataTable noteLines, DataTable cash, Dictionary<string, string> reporting = null, Dictionary<string, string> cashOptions = null)
        {
            bool detailed = reporting != null;
            string[] headers = detailed ? HierarchyWorkbookHeaders : WorkbookHeaders;
            int width = headers.Length;
            using (ExcelPackage package = new ExcelPackage())
            {
                package.Workbook.Properties.Title = "NORM editable account mapping";
                package.Workbook.Properties.Author = NORMHelper.CurrentUserId() ?? "unknown";
                ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Mappings");
                sheet.Cells[1, 1].Value = "NORM controlled account mapping";
                sheet.Cells[1, 1, 1, width].Merge = true;
                sheet.Cells[1, 1].Style.Font.Bold = true; sheet.Cells[1, 1].Style.Font.Size = 18; sheet.Cells[1, 1].Style.Font.Color.SetColor(Color.White);
                sheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid; sheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(20, 47, 65));
                sheet.Cells[2, 1].Value = "Instructions";
                sheet.Cells[2, 2].Value = releaseId == 0
                    ? "Use this sheet to prepare your first mapping. Upload it into the agency's configured empty draft when available."
                    : detailed ? "Select a reporting classification. NORM derives the account type and note. Enter a reason for each change." :
                    "Keep existing G/L accounts. Add accounts as needed. Enter a reason for each changed mapping.";
                sheet.Cells[2, 2, 2, width].Merge = true;
                sheet.Cells[2, 2].Style.WrapText = true;
                for (int i = 0; i < width; i++) sheet.Cells[HeaderRow, i + 1].Value = headers[i];
                sheet.Cells[HeaderRow, 1, HeaderRow, width].Style.Font.Bold = true; sheet.Cells[HeaderRow, 1, HeaderRow, width].Style.Font.Color.SetColor(Color.White);
                sheet.Cells[HeaderRow, 1, HeaderRow, width].Style.Fill.PatternType = ExcelFillStyle.Solid; sheet.Cells[HeaderRow, 1, HeaderRow, width].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(43, 109, 91));
                sheet.Column(1).Style.Numberformat.Format = "@";
                int row = HeaderRow + 1;
                foreach (DataRow source in mappings.Rows)
                {
                    sheet.Cells[row, 1].Value = Text(source, "GlCode"); sheet.Cells[row, 2].Value = Text(source, "GlDescription");
                    if (detailed)
                    {
                        sheet.Cells[row, 3].Value = DisplayOption(Text(source, "StatementLine"), reporting);
                        sheet.Cells[row, 4].Value = DisplayOption(Text(source, "CashFlowClass"), cashOptions);
                    }
                    else
                    {
                        sheet.Cells[row, 3].Value = Text(source, "AccountType"); sheet.Cells[row, 4].Value = Text(source, "StatementLine");
                        sheet.Cells[row, 5].Value = Text(source, "NoteSubLine"); sheet.Cells[row, 6].Value = Text(source, "CashFlowClass");
                    }
                    row++;
                }
                int last = Math.Max(HeaderRow + 1, row - 1);
                sheet.Cells[HeaderRow + 1, 3, last, width].Style.Fill.PatternType = ExcelFillStyle.Solid;
                sheet.Cells[HeaderRow + 1, 3, last, width].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(221, 235, 247));
                sheet.View.FreezePanes(HeaderRow + 1, 1); sheet.Cells[HeaderRow, 1, last, width].AutoFilter = true;

                if (detailed)
                {
                    sheet.Column(1).Width = 18; sheet.Column(2).Width = 34; sheet.Column(3).Width = 62;
                    sheet.Column(4).Width = 48; sheet.Column(5).Width = 42;
                    ExcelWorksheet lists = package.Workbook.Worksheets.Add("Reference lists");
                    AddClassificationList(package.Workbook, sheet, lists, reporting, "Reporting Classification", "ReportingClassifications", "C", 1);
                    AddClassificationList(package.Workbook, sheet, lists, cashOptions, "Cash Flow Classification", "CashFlowClassifications", "D", 3);
                    AddWorkbookMetadata(package, releaseId, HierarchyWorkbookFormat);
                    return package.GetAsByteArray();
                }
                sheet.Column(1).Width = 18; sheet.Column(2).Width = 34; sheet.Column(3).Width = 16; sheet.Column(4).Width = 34;
                sheet.Column(5).Width = 34; sheet.Column(6).Width = 28; sheet.Column(7).Width = 42;

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
                int entryRow = HeaderRow + 1;
                var typeValidation = sheet.DataValidations.AddListValidation("C" + entryRow + ":C" + last);
                typeValidation.Formula.ExcelFormula = "'Reference lists'!$A$2:$A$6";
                if (lines.Rows.Count > 0)
                {
                    var lineValidation = sheet.DataValidations.AddListValidation("D" + entryRow + ":D" + last);
                    lineValidation.Formula.ExcelFormula = mappings.Rows.Count == 0
                        ? "'Reference lists'!$C$2:$C$" + (lines.Rows.Count + 1).ToString(CultureInfo.InvariantCulture)
                        : "INDIRECT(\"FaceLines_\"&$C" + entryRow.ToString(CultureInfo.InvariantCulture) + ")";
                }
                if (lines.Rows.Count > 0 && mappings.Rows.Count > 0) { var noteValidation = sheet.DataValidations.AddListValidation("E" + entryRow + ":E" + last); noteValidation.Formula.ExcelFormula = "INDIRECT(VLOOKUP($D" + entryRow.ToString(CultureInfo.InvariantCulture) + ",NoteLineLookup,2,FALSE))"; noteValidation.AllowBlank = true; }
                if (cash.Rows.Count > 0) { var cashValidation = sheet.DataValidations.AddListValidation("F" + entryRow + ":F" + last); cashValidation.Formula.ExcelFormula = "'Reference lists'!$G$2:$G$" + (cash.Rows.Count + 1).ToString(CultureInfo.InvariantCulture); cashValidation.AllowBlank = true; }
                AddWorkbookMetadata(package, releaseId, WorkbookFormat);
                return package.GetAsByteArray();
            }
        }

        private static void AddWorkbookMetadata(ExcelPackage package, int releaseId, string format)
        {
            ExcelWorksheet metadata = package.Workbook.Worksheets.Add(WorkbookMetadata);
            metadata.Cells[1, 1].Value = releaseId;
            metadata.Cells[1, 2].Value = format;
            metadata.Hidden = eWorkSheetHidden.VeryHidden;
        }

        private static void AddClassificationList(ExcelWorkbook workbook, ExcelWorksheet sheet, ExcelWorksheet reference,
            Dictionary<string, string> options, string heading, string rangeName, string targetColumn, int listColumn)
        {
            reference.Cells[1, listColumn].Value = heading;
            reference.Cells[1, listColumn].Style.Font.Bold = true;
            reference.Column(listColumn).Width = 62;
            int row = 2;
            foreach (string key in options.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                reference.Cells[row++, listColumn].Value = DisplayOption(key, options);
            if (row == 2) return;
            workbook.Names.Add(rangeName, reference.Cells[2, listColumn, row - 1, listColumn]);
            var validation = sheet.DataValidations.AddListValidation(targetColumn + (HeaderRow + 1) + ":" + targetColumn + ExcelPackage.MaxRows);
            validation.Formula.ExcelFormula = rangeName;
            validation.AllowBlank = true;
            validation.ShowErrorMessage = false;
        }

        private static string DisplayOption(string key, Dictionary<string, string> options)
        {
            string description;
            return options.TryGetValue(key, out description) && description.Length > 0 ? key + DisplaySeparator + description : key;
        }

        private static string ClassificationKey(string value)
        {
            int separator = value.IndexOf(DisplaySeparator, StringComparison.Ordinal);
            return separator < 0 ? value : value.Substring(0, separator).Trim();
        }

        public static NORMMappingUploadOutcome ApplyWorkbook(int releaseId, byte[] content, string fileName, string user)
        {
            if (content == null || content.Length == 0) throw new InvalidOperationException("Choose a non-empty Excel mapping workbook.");
            if (!String.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The mapping workbook must be an .xlsx file.");
            DataRow release = DraftRelease(releaseId);
            int parentId = NORMHelper.Int(release, "ParentConfigurationReleaseId");
            Dictionary<string, string> hierarchy = IsNihracs(release) ? NORMStatementEngine.LoadHierarchyAccountTypes(releaseId) : null;
            int importId = MappingSourceImportId(parentId, release);
            string hash = NORMCrypto.Sha256(content);
            Dictionary<string, DataRow> existing = NORMHelper.Query("SELECT * FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0", NORMHelper.P("@release", releaseId))
                .AsEnumerable().ToDictionary(x => Text(x, "GlCode"), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DataRow> trialBalanceAccounts = NORMHelper.Query(
                "SELECT GlAccount,MAX(GlText) GlDescription FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId=@import AND IsDeactivated=0 GROUP BY GlAccount",
                NORMHelper.P("@import", importId)).AsEnumerable().ToDictionary(x => Text(x, "GlAccount"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> validLines = new HashSet<string>(NORMHelper.Query(
                "SELECT LineCode FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND LineCode IS NOT NULL AND CalculationKind='Mapped' AND IsDeactivated=0",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "LineCode")), StringComparer.OrdinalIgnoreCase);
            HashSet<string> validTypeLines = new HashSet<string>(NORMHelper.Query(
                "SELECT DISTINCT AccountType,StatementLine FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND AccountType IN ('Asset','Liability','Equity','Income','Expense') AND StatementLine IS NOT NULL",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "AccountType") + "|" + Text(x, "StatementLine")), StringComparer.OrdinalIgnoreCase);
            HashSet<string> employeeDetails = new HashSet<string>(NORMHelper.Query(
                "SELECT LineCode FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND LineType='employee-detail' AND CalculationKind='Mapped'",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "LineCode")), StringComparer.OrdinalIgnoreCase);
            foreach (string detail in employeeDetails) validTypeLines.Add("Expense|" + detail);
            HashSet<string> validNoteLines = new HashSet<string>(NORMHelper.Query(
                "SELECT DISTINCT StatementLine,NoteSubLine FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 " +
                "AND StatementLine IS NOT NULL AND NoteSubLine IS NOT NULL AND LTRIM(RTRIM(NoteSubLine))<>''",
                NORMHelper.P("@release", releaseId)).AsEnumerable().Select(x => Text(x, "StatementLine") + "|" + Text(x, "NoteSubLine")), StringComparer.OrdinalIgnoreCase);
            List<MappingRow> rows = new List<MappingRow>();
            List<string> errors = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool firstMapping = existing.Count == 0;
            using (ExcelPackage package = new ExcelPackage(new MemoryStream(content)))
            {
                ExcelWorksheet sheet = package.Workbook.Worksheets["Mappings"];
                if (sheet == null) throw new InvalidOperationException("The workbook does not contain the required Mappings sheet.");
                ExcelWorksheet metadata = package.Workbook.Worksheets[WorkbookMetadata];
                bool legacy = metadata == null;
                string format = legacy ? Cell(sheet, 5, 2) : Cell(metadata, 1, 2);
                bool compact = !legacy && format == HierarchyWorkbookFormat && hierarchy != null;
                int workbookRelease;
                if (!Int32.TryParse(legacy ? Cell(sheet, 2, 2) : Cell(metadata, 1, 1), out workbookRelease) ||
                    (workbookRelease != releaseId && (legacy || workbookRelease != 0 || !firstMapping)))
                    throw new InvalidOperationException("This workbook belongs to a different configuration release. Download a fresh workbook for this draft.");
                if (legacy ? format != "3" : !compact && format != WorkbookFormat)
                    throw new InvalidOperationException("This mapping workbook layout does not apply to the selected draft. Download a fresh workbook.");
                string[] headers = compact ? HierarchyWorkbookHeaders : WorkbookHeaders;
                if (!legacy && headers.Where((header, index) => !Same(Cell(sheet, HeaderRow, index + 1), header)).Any())
                    throw new InvalidOperationException("The mapping workbook headers have changed. Download a fresh workbook for this draft.");
                int headerRow = legacy ? LegacyHeaderRow : HeaderRow;
                int mappingColumn = legacy ? 4 : 3;
                int last = sheet.Dimension == null ? headerRow : sheet.Dimension.End.Row;
                for (int rowNumber = headerRow + 1; rowNumber <= last; rowNumber++)
                {
                    string gl = Cell(sheet, rowNumber, 1); if (gl.Length == 0) continue;
                    if (!seen.Add(gl)) { errors.Add("Row " + rowNumber + ": G/L " + gl + " appears more than once."); continue; }
                    if (gl.Length > 20) { errors.Add("Row " + rowNumber + ": G/L account exceeds 20 characters."); continue; }
                    DataRow before; existing.TryGetValue(gl, out before);
                    DataRow trialBalanceAccount; trialBalanceAccounts.TryGetValue(gl, out trialBalanceAccount);
                    string description = before == null ? Text(trialBalanceAccount, "GlDescription") : Text(before, "GlDescription");
                    if (before == null && description.Length == 0) description = Cell(sheet, rowNumber, 2);
                    if (before == null && description.Length > 200) errors.Add("Row " + rowNumber + ": description exceeds 200 characters.");
                    MappingRow item = new MappingRow {
                        RowNumber = rowNumber, GlCode = gl, GlDescription = description, Before = before,
                        AccountType = compact ? "" : Cell(sheet, rowNumber, mappingColumn),
                        StatementLine = compact ? ClassificationKey(Cell(sheet, rowNumber, 3)) : Cell(sheet, rowNumber, mappingColumn + 1),
                        NoteSubLine = compact ? "" : Cell(sheet, rowNumber, mappingColumn + 2),
                        CashFlowClass = compact ? ClassificationKey(Cell(sheet, rowNumber, 4)) : Cell(sheet, rowNumber, mappingColumn + 3),
                        Reason = Cell(sheet, rowNumber, compact ? 5 : mappingColumn + 4)
                    };
                    string expectedType;
                    if (hierarchy != null)
                        item.AccountType = hierarchy.TryGetValue(item.StatementLine, out expectedType) ? expectedType : "";
                    else if (employeeDetails.Contains(item.StatementLine))
                        item.AccountType = "Expense";
                    else if (item.AccountType.Length > 0)
                    {
                        expectedType = AccountTypes.FirstOrDefault(type => Same(type, item.AccountType));
                        if (expectedType == null) errors.Add("Row " + rowNumber + ": account type is not valid.");
                        else item.AccountType = expectedType;
                    }
                    if (item.StatementLine.Length > 120) errors.Add("Row " + rowNumber + ": face statement line exceeds 120 characters.");
                    if (hierarchy != null && item.NoteSubLine.Length > 0)
                        errors.Add("Row " + rowNumber + ": leave Note sub-line blank when you select a detail classification.");
                    bool activeLine = validLines.Contains(item.StatementLine);
                    if (hierarchy == null && !firstMapping && activeLine && item.AccountType.Length > 0 && !validTypeLines.Contains(item.AccountType + "|" + item.StatementLine)) errors.Add("Row " + rowNumber + ": face statement line '" + item.StatementLine + "' is not available for account type " + item.AccountType + ".");
                    if (item.NoteSubLine.Length > 0 && item.StatementLine.Length == 0) errors.Add("Row " + rowNumber + ": select a face statement line before selecting a note sub-line.");
                    if (!firstMapping && activeLine && item.NoteSubLine.Length > 0 && !validNoteLines.Contains(item.StatementLine + "|" + item.NoteSubLine)) errors.Add("Row " + rowNumber + ": note sub-line '" + item.NoteSubLine + "' is not available for face statement line '" + item.StatementLine + "'.");
                    if (item.NoteSubLine.Length > 240) errors.Add("Row " + rowNumber + ": note sub-line exceeds 240 characters.");
                    if (item.CashFlowClass.Length > 120) errors.Add("Row " + rowNumber + ": cash-flow class exceeds 120 characters.");
                    item.Changed = before == null || Different(before, item);
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
                    DataTable current = NORMHelper.Query(connection, transaction,
                        "SELECT ContentHash FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK) " +
                        "WHERE ConfigurationReleaseId=@release AND StatusCode='Draft' AND IsDeactivated=0",
                        NORMHelper.P("@release", releaseId));
                    if (current.Rows.Count != 1 || !Same(Text(current.Rows[0], "ContentHash"), Text(release, "ContentHash")))
                        throw new InvalidOperationException("The mapping draft changed during validation. Download a fresh workbook and try again.");
                    NORMHelper.RequireEntityAccess(Text(release, "EntityCode"), connection, transaction);
                    foreach (MappingRow item in rows.Where(x => x.Changed))
                    {
                        if (item.Before == null)
                            NORMHelper.Exec(connection, transaction,
                                "INSERT dbo.tblNORM_AccountMap (ConfigurationReleaseId,FinancialYear,EntityCode,GlCode,GlDescription,AccountType,StatementLine,NoteSubLine,CashFlowClass,MappingRationale,IsDeactivated) " +
                                "VALUES (@release,@fy,@entity,@gl,@description,@type,@line,@note,@cash,@reason,0)",
                                NORMHelper.P("@release", releaseId), NORMHelper.P("@fy", NORMHelper.Int(release, "FinancialYear")), NORMHelper.P("@entity", NORMHelper.Str(release, "EntityCode")),
                                NORMHelper.P("@gl", item.GlCode), NORMHelper.P("@description", Null(item.GlDescription)), NORMHelper.P("@type", Null(item.AccountType)), NORMHelper.P("@line", Null(item.StatementLine)),
                                NORMHelper.P("@note", Null(item.NoteSubLine)), NORMHelper.P("@cash", Null(item.CashFlowClass)), NORMHelper.P("@reason", item.Reason));
                        else
                            NORMHelper.Exec(connection, transaction,
                                "UPDATE dbo.tblNORM_AccountMap SET AccountType=@type,StatementLine=@line,NoteSubLine=@note,CashFlowClass=@cash,MappingRationale=@reason WHERE ConfigurationReleaseId=@release AND GlCode=@gl AND IsDeactivated=0",
                                NORMHelper.P("@type", Null(item.AccountType)), NORMHelper.P("@line", Null(item.StatementLine)), NORMHelper.P("@note", Null(item.NoteSubLine)), NORMHelper.P("@cash", Null(item.CashFlowClass)), NORMHelper.P("@reason", item.Reason), NORMHelper.P("@release", releaseId), NORMHelper.P("@gl", item.GlCode));
                        NORMHelper.Exec(connection, transaction,
                            "INSERT dbo.tblNORM_MappingChange (ConfigurationReleaseId,GlCode,BeforeAccountType,AfterAccountType,BeforeStatementLine,AfterStatementLine,BeforeNoteSubLine,AfterNoteSubLine,BeforeCashFlowClass,AfterCashFlowClass,ChangeReason,WorkbookHash,ChangedBy) " +
                            "VALUES (@release,@gl,@bt,@at,@bl,@al,@bn,@an,@bc,@ac,@reason,@hash,@user)",
                            NORMHelper.P("@release", releaseId), NORMHelper.P("@gl", item.GlCode), NORMHelper.P("@bt", Before(item, "AccountType")), NORMHelper.P("@at", Null(item.AccountType)),
                            NORMHelper.P("@bl", Before(item, "StatementLine")), NORMHelper.P("@al", Null(item.StatementLine)), NORMHelper.P("@bn", Before(item, "NoteSubLine")), NORMHelper.P("@an", Null(item.NoteSubLine)),
                            NORMHelper.P("@bc", Before(item, "CashFlowClass")), NORMHelper.P("@ac", Null(item.CashFlowClass)), NORMHelper.P("@reason", item.Reason), NORMHelper.P("@hash", hash), NORMHelper.P("@user", user));
                    }
                    NORMHelper.Exec(connection, transaction, "UPDATE dbo.tblNORM_ConfigurationRelease SET ContentHash=@hash,ReviewedBy=NULL,ReviewedUtc=NULL WHERE ConfigurationReleaseId=@release AND StatusCode='Draft'", NORMHelper.P("@hash", hash), NORMHelper.P("@release", releaseId));
                    Audit(connection, transaction, "MAPPING_WORKBOOK_APPLIED", releaseId, changed.ToString(CultureInfo.InvariantCulture) + " account mapping(s) changed from workbook " + Path.GetFileName(fileName) + "; SHA-256 " + hash + ".", user);
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
            return new NORMMappingUploadOutcome { ChangedCount = changed, WorkbookHash = hash, Validation = Validate(releaseId) };
        }

        public static DataTable EmployeeCategories(int releaseId)
        {
            DataRow release = DraftRelease(releaseId);
            if (!NORMStatementEngine.HasEmployeeHierarchy(null, null) || !NORMStatementEngine.HasReportedAmounts())
                return null;
            if (IsNihracs(release))
            {
                if (!NORMReportingFramework.HasHierarchyNoteSource(null, null)) return null;
                return NORMHelper.Query("SELECT LineCode,LineLabel,IsDeactivated,StatementCode," + MappingDescriptionColumn(null, null) + " FROM dbo.tblNORM_StatementLine " +
                    "WHERE ConfigurationReleaseId=@release AND ((LineType='hierarchy-detail' AND CalculationKind='Mapped') OR (" +
                    CashFlowReferenceDetail + ")) ORDER BY StatementCode,SeqNo",
                    NORMHelper.P("@release", releaseId));
            }
            DataTable parent = NORMHelper.Query("SELECT CalculationKind,FormulaSpec,ParentLineCode FROM dbo.tblNORM_StatementLine WHERE " +
                EmployeeParentFilter, NORMHelper.P("@release", releaseId));
            if (!CanEditEmployeeParent(parent)) return null;
            return NORMHelper.Query("SELECT LineCode,LineLabel,IsDeactivated,StatementCode," + MappingDescriptionColumn(null, null) + " FROM dbo.tblNORM_StatementLine " +
                "WHERE ConfigurationReleaseId=@release AND LineType='employee-detail' AND StatementCode='SOCI' " +
                "ORDER BY SeqNo", NORMHelper.P("@release", releaseId));
        }

        public static DataTable ClassificationAudit(int releaseId)
        {
            Release(releaseId);
            return NORMHelper.Query("SELECT TOP 30 DetailText,PerformedBy,PerformedUtc FROM dbo.tblNORM_AuditEvent " +
                "WHERE EntityType='ConfigurationRelease' AND EntityId=@id AND EventCode IN (@reporting,@employee) ORDER BY AuditEventId DESC",
                NORMHelper.P("@id", releaseId.ToString(CultureInfo.InvariantCulture)), NORMHelper.P("@reporting", ReportingHierarchyEvent),
                NORMHelper.P("@employee", EmployeeHierarchyEvent));
        }

        private const string ReportingParentsSql =
            "SELECT s.LineCode,s.LineLabel,s.StatementCode FROM dbo.tblNORM_StatementLine s WHERE s.ConfigurationReleaseId=@release " +
            "AND s.IsDeactivated=0 AND s.CalculationKind='Formula' AND s.FormulaSpec IS NULL AND " +
            "(s.LineType='hierarchy-detail' OR EXISTS (SELECT 1 FROM dbo.tblNORM_DisclosureRule d " +
            "WHERE d.ConfigurationReleaseId=s.ConfigurationReleaseId AND d.IsDeactivated=0 AND d.NoteSourceLineCode=s.LineCode))";

        public static DataTable NihracsCategoryParents(int releaseId)
        {
            if (!IsNihracs(DraftRelease(releaseId)) || !NORMReportingFramework.HasHierarchyNoteSource(null, null)) return null;
            return NORMHelper.Query(ReportingParentsSql + " ORDER BY s.StatementCode,s.SeqNo",
                NORMHelper.P("@release", releaseId));
        }

        public static void SaveEmployeeCategory(int releaseId, string parentCode, string code, string label, bool deactivate, string user)
        {
            if (!NORMHelper.HasAdminAccess()) throw new UnauthorizedAccessException("Administrator access is required to change category structure.");
            code = (code ?? "").Trim();
            label = (label ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"\A[A-Za-z0-9_]{1,120}\z"))
                throw new InvalidOperationException("Select Employee benefits as the parent and enter a stable key using letters, digits or underscores.");
            if (!deactivate && (label.Length == 0 || label.Length > 240))
                throw new InvalidOperationException("Enter a display description between 1 and 240 characters.");
            if (!NORMStatementEngine.HasReportedAmounts())
                throw new InvalidOperationException("The Employee benefits pilot schema is required.");

            using (OleDbConnection c = NORMHelper.OpenConnection())
            using (OleDbTransaction t = c.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    DataTable releases = NORMHelper.Query(c, t, "SELECT EntityCode,FinancialYear FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK) " +
                        "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 AND StatusCode='Draft'", NORMHelper.P("@release", releaseId));
                    if (releases.Rows.Count != 1)
                        throw new InvalidOperationException("Only a draft configuration release can be changed.");
                    string entity = Text(releases.Rows[0], "EntityCode");
                    NORMHelper.RequireEntityAccess(entity, c, t);
                    if (!NORMStatementEngine.HasEmployeeHierarchy(c, t))
                        throw new InvalidOperationException("The Employee benefits pilot schema is required.");
                    bool nihracs = IsNihracs(releases.Rows[0]);
                    string statement = "SOCI";
                    string lineType = nihracs ? NORMStatementEngine.HierarchyDetailType : NORMStatementEngine.EmployeeDetailType;
                    if (nihracs)
                    {
                        if (!NORMReportingFramework.HasHierarchyNoteSource(c, t))
                            throw new InvalidOperationException("The NIHRACS reporting hierarchy schema is required.");
                        DataTable parents = NORMHelper.Query(c, t, ReportingParentsSql + " AND s.LineCode=@parent",
                            NORMHelper.P("@release", releaseId), NORMHelper.P("@parent", parentCode));
                        if (parents.Rows.Count != 1)
                            throw new InvalidOperationException("Select an active additive note parent.");
                        parentCode = Text(parents.Rows[0], "LineCode");
                        statement = Text(parents.Rows[0], "StatementCode");
                    }
                    else
                    {
                        if (!Same(parentCode, NORMStatementEngine.EmployeeBenefitsCode))
                            throw new InvalidOperationException("Select Employee benefits as the parent and enter a stable key using letters, digits or underscores.");
                        DataTable parent = NORMHelper.Query(c, t, "SELECT CalculationKind,FormulaSpec,ParentLineCode FROM dbo.tblNORM_StatementLine WHERE " +
                            EmployeeParentFilter, NORMHelper.P("@release", releaseId));
                        if (!CanEditEmployeeParent(parent))
                            throw new InvalidOperationException("This draft does not have a compatible Employee benefits parent.");
                    }

                    DataTable matches = NORMHelper.Query(c, t, "SELECT StatementLineId,LineCode,LineLabel,StatementCode,LineType,CalculationKind,ParentLineCode,IsDeactivated " +
                        "FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND LineCode=@code",
                        NORMHelper.P("@release", releaseId), NORMHelper.P("@code", code));
                    DataRow existing = matches.Rows.Count == 1 ? matches.Rows[0] : null;
                    if (matches.Rows.Count > 1 || (existing != null && (Text(existing, "LineType") != lineType ||
                        Text(existing, "StatementCode") != statement || Text(existing, "CalculationKind") != "Mapped" ||
                        !Same(Text(existing, "ParentLineCode"), parentCode))))
                        throw new InvalidOperationException("That key already belongs to another statement classification.");

                    bool inactive = existing != null && Convert.ToBoolean(existing["IsDeactivated"], CultureInfo.InvariantCulture);
                    if (deactivate && (existing == null || inactive))
                        throw new InvalidOperationException("Enter the key of an active Employee benefits category.");
                    if (!deactivate && existing != null && !inactive && String.Equals(Text(existing, "LineLabel"), label, StringComparison.Ordinal))
                        throw new InvalidOperationException("The category already has that description.");

                    string action;
                    if (existing == null)
                    {
                        if (!nihracs) NORMHelper.Exec(c, t, "UPDATE dbo.tblNORM_StatementLine SET CalculationKind='Formula',FormulaSpec=NULL WHERE " +
                            EmployeeParentFilter + " AND CalculationKind='Mapped'", NORMHelper.P("@release", releaseId));
                        int seq = Convert.ToInt32(NORMHelper.Scalar(c, t, "SELECT ISNULL(MAX(SeqNo),0)+1 FROM dbo.tblNORM_StatementLine " +
                            "WHERE ConfigurationReleaseId=@release AND StatementCode=@statement",
                            NORMHelper.P("@release", releaseId), NORMHelper.P("@statement", statement)));
                        NORMHelper.Exec(c, t, "INSERT dbo.tblNORM_StatementLine (ConfigurationReleaseId,FinancialYear,EntityCode,StatementCode,SeqNo,LineType,LineCode,LineLabel,CalculationKind,ParentLineCode) " +
                            "VALUES(@release,@year,@entity,@statement,@seq,@type,@code,@label,'Mapped',@parent)",
                            NORMHelper.P("@release", releaseId), NORMHelper.P("@year", NORMHelper.Int(releases.Rows[0], "FinancialYear")),
                            NORMHelper.P("@entity", entity), NORMHelper.P("@seq", seq), NORMHelper.P("@code", code), NORMHelper.P("@label", label),
                            NORMHelper.P("@parent", parentCode), NORMHelper.P("@type", lineType), NORMHelper.P("@statement", statement));
                        action = "Added " + code + " (" + label + ")";
                    }
                    else
                    {
                        if (deactivate) label = Text(existing, "LineLabel");
                        else if (!String.Equals(Text(existing, "LineLabel"), label, StringComparison.Ordinal))
                            PreserveMappingDescription(c, t, releaseId, NORMHelper.Int(existing, "StatementLineId"), nihracs);
                        NORMHelper.Exec(c, t, "UPDATE dbo.tblNORM_StatementLine SET LineLabel=@label,IsDeactivated=@inactive WHERE StatementLineId=@line",
                            NORMHelper.P("@label", label), NORMHelper.P("@inactive", deactivate), NORMHelper.P("@line", NORMHelper.Int(existing, "StatementLineId")));
                        action = (deactivate ? "Deactivated " : inactive ? "Reactivated " : "Updated ") + Text(existing, "LineCode") +
                            " (" + Text(existing, "LineLabel") + (deactivate ? ")" : " → " + label + ")");
                    }
                    NORMHelper.Exec(c, t, "UPDATE dbo.tblNORM_ConfigurationRelease SET ContentHash=@hash,ReviewedBy=NULL,ReviewedUtc=NULL " +
                        "WHERE ConfigurationReleaseId=@release", NORMHelper.P("@hash", NORMCrypto.Sha256(Guid.NewGuid().ToString())),
                        NORMHelper.P("@release", releaseId));
                    Audit(c, t, nihracs ? ReportingHierarchyEvent : EmployeeHierarchyEvent, releaseId, action + " under " + parentCode + ".", user);
                    t.Commit();
                }
                catch { t.Rollback(); throw; }
            }
        }

        public static void SaveClassificationText(int releaseId, string code, string text, bool editMappingDescription, string user)
        {
            code = (code ?? "").Trim();
            text = (text ?? "").Trim();
            if (code.Length == 0 || code.Length > 120 || text.Length > 240 || (!editMappingDescription && text.Length == 0))
                throw new InvalidOperationException("Select a classification key and enter up to 240 characters. A statement label is required.");
            using (OleDbConnection c = NORMHelper.OpenConnection())
            using (OleDbTransaction t = c.BeginTransaction(IsolationLevel.Serializable))
            {
                DataTable releases = NORMHelper.Query(c, t, "SELECT EntityCode,FinancialYear FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK) " +
                    "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 AND StatusCode='Draft'", NORMHelper.P("@release", releaseId));
                if (releases.Rows.Count != 1) throw new InvalidOperationException("Only a draft configuration release can be changed.");
                NORMHelper.RequireEntityAccess(Text(releases.Rows[0], "EntityCode"), c, t);

                bool nihracs = IsNihracs(releases.Rows[0]);
                string column = editMappingDescription ? "MappingDescription" : "LineLabel";
                if (editMappingDescription && !HasMappingDescriptions(c, t))
                    throw new InvalidOperationException("Install the mapping descriptions schema before editing mapping descriptions.");
                // Only the draft's agency detail rows are editable. Face and framework lines stay protected.
                DataTable matches = NORMHelper.Query(c, t, "SELECT StatementLineId," + column + " FROM dbo.tblNORM_StatementLine " +
                    "WHERE ConfigurationReleaseId=@release AND LineCode=@code AND IsDeactivated=0 AND ((LineType=@type AND CalculationKind='Mapped')" +
                    (nihracs && editMappingDescription ? " OR (" + CashFlowReferenceDetail + ")" : "") + ")",
                    NORMHelper.P("@release", releaseId), NORMHelper.P("@code", code),
                    NORMHelper.P("@type", nihracs ? NORMStatementEngine.HierarchyDetailType : NORMStatementEngine.EmployeeDetailType));
                if (matches.Rows.Count != 1)
                    throw new InvalidOperationException("Choose an active, editable detail classification in this draft.");
                string previous = Text(matches.Rows[0], column);
                if (String.Equals(previous, text, StringComparison.Ordinal))
                    throw new InvalidOperationException("The classification already has that wording.");
                int lineId = NORMHelper.Int(matches.Rows[0], "StatementLineId");
                if (!editMappingDescription) PreserveMappingDescription(c, t, releaseId, lineId, nihracs);
                NORMHelper.Exec(c, t, "UPDATE dbo.tblNORM_StatementLine SET " + column + "=@text WHERE StatementLineId=@line AND ConfigurationReleaseId=@release",
                    NORMHelper.P("@text", Null(text)), NORMHelper.P("@line", lineId), NORMHelper.P("@release", releaseId));
                NORMHelper.Exec(c, t, "UPDATE dbo.tblNORM_ConfigurationRelease SET ContentHash=@hash,ReviewedBy=NULL,ReviewedUtc=NULL WHERE ConfigurationReleaseId=@release",
                    NORMHelper.P("@hash", NORMCrypto.Sha256(Guid.NewGuid().ToString())), NORMHelper.P("@release", releaseId));
                Audit(c, t, nihracs ? ReportingHierarchyEvent : EmployeeHierarchyEvent,
                    releaseId, code + ": " + (editMappingDescription ? "Mapping Description" : "Statement Label") +
                    " changed from \"" + previous + "\" to \"" + text + "\".", user);
                t.Commit();
            }
        }

        private static void PreserveMappingDescription(OleDbConnection c, OleDbTransaction t, int releaseId, int lineId, bool nihracs)
        {
            if (!HasMappingDescriptions(c, t)) return;
            DataTable rows = NORMHelper.Query(c, t, "SELECT s.LineLabel,s.MappingDescription," +
                (nihracs ? "p.LineLabel" : "NULL") + " AS ParentLabel FROM dbo.tblNORM_StatementLine s " +
                "LEFT JOIN dbo.tblNORM_StatementLine p ON p.ConfigurationReleaseId=s.ConfigurationReleaseId AND p.LineCode=s.ParentLineCode " +
                "AND p.IsDeactivated=0 WHERE s.ConfigurationReleaseId=@release AND s.StatementLineId=@line AND s.MappingDescription IS NULL",
                NORMHelper.P("@release", releaseId), NORMHelper.P("@line", lineId));
            if (rows.Rows.Count == 1)
                NORMHelper.Exec(c, t, "UPDATE dbo.tblNORM_StatementLine SET MappingDescription=@text WHERE StatementLineId=@line AND ConfigurationReleaseId=@release",
                    NORMHelper.P("@text", Null(MappingDescription(rows.Rows[0]))), NORMHelper.P("@line", lineId), NORMHelper.P("@release", releaseId));
        }

        private static bool HasMappingDescriptions(OleDbConnection c, OleDbTransaction t)
        {
            const string sql = "SELECT CASE WHEN COL_LENGTH('dbo.tblNORM_StatementLine','MappingDescription') IS NULL THEN 0 ELSE 1 END";
            return Convert.ToInt32(c == null ? NORMHelper.Scalar(sql) : NORMHelper.Scalar(c, t, sql)) == 1;
        }

        private static string MappingDescriptionColumn(OleDbConnection c, OleDbTransaction t)
        {
            return HasMappingDescriptions(c, t) ? "MappingDescription" : "NULL AS MappingDescription";
        }

        private static string MappingDescription(DataRow row)
        {
            string description = Text(row, "MappingDescription");
            if (description.Length != 0) return description;
            string label = Text(row, "LineLabel");
            string parent = Text(row, "ParentLabel");
            return parent.Length == 0 || Same(label, parent) || label.Length + parent.Length + 3 > 240 ? label : label + " (" + parent + ")";
        }

        private static bool CanEditEmployeeParent(DataTable parent)
        {
            return parent.Rows.Count == 1 && Text(parent.Rows[0], "ParentLineCode").Length == 0 &&
                Text(parent.Rows[0], "FormulaSpec").Length == 0 &&
                (Text(parent.Rows[0], "CalculationKind") == "Mapped" || Text(parent.Rows[0], "CalculationKind") == "Formula");
        }

        public static NORMMappingValidation Validate(int releaseId)
        {
            DataRow release = Release(releaseId);
            var result = new NORMMappingValidation();
            DataTable mappings = NORMHelper.Query(
                "SELECT GlCode,AccountType,StatementLine,NoteSubLine,CashFlowClass FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0",
                NORMHelper.P("@release", releaseId));
            DataTable lines = NORMHelper.Query(
                "SELECT LineCode,LineType FROM dbo.tblNORM_StatementLine WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 AND CalculationKind='Mapped' AND LineCode IS NOT NULL",
                NORMHelper.P("@release", releaseId));
            result.MappingCount = mappings.Rows.Count;
            int duplicates = mappings.AsEnumerable().GroupBy(row => Text(row, "GlCode"), StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1);
            if (duplicates > 0) result.Errors.Add(duplicates + " G/L account(s) have duplicate active mappings.");
            Dictionary<string, string> hierarchy = null;
            string hierarchyEvent = null;
            try
            {
                if (NORMStatementEngine.HasEmployeeHierarchy(null, null))
                {
                    hierarchy = IsNihracs(release) ? NORMStatementEngine.LoadHierarchyAccountTypes(releaseId) : null;
                    if (hierarchy != null)
                    {
                        if (!NORMReportingFramework.HasHierarchyNoteSource(null, null) || !NORMStatementEngine.HasReportedAmounts())
                            result.Errors.Add("The NIHRACS reporting hierarchy schema is required.");
                        hierarchyEvent = ReportingHierarchyEvent;
                    }
                    else if (NORMStatementEngine.ValidateEmployeeHierarchy(releaseId) != null)
                    {
                        if (!NORMStatementEngine.HasReportedAmounts())
                            result.Errors.Add("The reported amounts schema is required.");
                        hierarchyEvent = EmployeeHierarchyEvent;
                    }
                }
            }
            catch (InvalidOperationException error) { result.Errors.Add(error.Message); }

            if (IsParentlessDraft(release) && lines.Rows.Count == 0 && hierarchy == null)
                result.Errors.Add("Configure the statement lines before approving this initial mapping draft.");
            if (hierarchyEvent != null)
                result.HierarchyChanged = Convert.ToInt32(NORMHelper.Scalar(
                    "SELECT COUNT(*) FROM dbo.tblNORM_AuditEvent WHERE EventCode=@event AND EntityType='ConfigurationRelease' AND EntityId=@id",
                    NORMHelper.P("@event", hierarchyEvent), NORMHelper.P("@id", releaseId.ToString(CultureInfo.InvariantCulture)))) > 0;
            result.ChangedCount = Convert.ToInt32(NORMHelper.Scalar(
                "SELECT COUNT(DISTINCT GlCode) FROM dbo.tblNORM_MappingChange WHERE ConfigurationReleaseId=@release", NORMHelper.P("@release", releaseId)));

            var activeLines = lines.AsEnumerable().ToLookup(line => Text(line, "LineCode"), StringComparer.OrdinalIgnoreCase);
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var incomplete = new List<string>();
            foreach (DataRow mapping in mappings.Rows)
            {
                string gl = Text(mapping, "GlCode");
                string code = Text(mapping, "StatementLine");
                string type = Text(mapping, "AccountType");
                bool complete = hierarchy != null ? hierarchy.ContainsKey(code) :
                    activeLines[code].Any(line => Text(line, "LineType") == NORMStatementEngine.EmployeeDetailType || NORMStatementEngine.IsKnownType(type));
                if (hierarchy != null && Text(mapping, "NoteSubLine").Length != 0)
                    result.Errors.Add("Leave Note sub-line blank for " + gl + " in the reporting hierarchy.");
                if (complete) mapped.Add(gl);
                else incomplete.Add(code.Length == 0 ? gl : gl + " (" + code + ")");
            }
            if (incomplete.Count > 0)
                result.Warnings.Add(incomplete.Count + " saved mapping row(s) have an incomplete classification. " +
                    NORMStatementEngine.MappingAccountList(incomplete));
            if (hierarchy != null)
            {
                var cashFlow = CashFlowOptions(releaseId);
                var unresolvedCash = mappings.AsEnumerable().Where(row => Text(row, "CashFlowClass").Length != 0 &&
                    !cashFlow.ContainsKey(Text(row, "CashFlowClass")))
                    .Select(row => Text(row, "GlCode") + " (" + Text(row, "CashFlowClass") + ")").ToList();
                if (unresolvedCash.Count > 0)
                    result.Warnings.Add(unresolvedCash.Count + " saved mapping row(s) use an undefined cash-flow classification. " +
                        NORMStatementEngine.MappingAccountList(unresolvedCash));
            }

            int year = NORMHelper.Int(release, "FinancialYear");
            int importId = MappingSourceImportId(NORMHelper.Int(release, "ParentConfigurationReleaseId"), release);
            result.UnmappedCount = ValidateSourceMappings(result, mapped, importId, hierarchy != null, year);
            if (hierarchy != null)
            {
                int comparativeImport = 0;
                try { comparativeImport = NORMImportService.FindAnnualDepartmentalImport(Text(release, "EntityCode"), year, year - 1); }
                catch (InvalidOperationException error) { result.Errors.Add(error.Message); }
                ValidateSourceMappings(result, mapped, comparativeImport, true, year - 1);
            }
            if (NORMHelper.Str(release, "StatusCode") == "Draft" && result.ChangedCount == 0 && !result.HierarchyChanged)
                result.Warnings.Add("No mapping changes have been recorded in this draft.");
            result.ErrorCount = result.Errors.Count;
            return result;
        }

        private static int ValidateSourceMappings(NORMMappingValidation result, HashSet<string> mapped, int importId,
            bool annual, int year)
        {
            if (importId <= 0) return 0;
            DataTable source = NORMHelper.Query(
                "SELECT GlAccount,SourceCompanyCode,SourceLedger,ReportingStream,AccumBalance,DebitMovement,CreditMovement" +
                (annual ? ",SourceDebit,SourceCredit" : "") +
                " FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId=@import AND IsDeactivated=0", NORMHelper.P("@import", importId));
            var unresolved = NORMStatementEngine.AssessUnmapped(source.AsEnumerable()
                .Where(row => !mapped.Contains(Text(row, "GlAccount"))), annual);
            string period = annual ? "FY" + year + ": " : "";
            if (unresolved.Blocking.Count > 0)
                result.Errors.Add(period + unresolved.Blocking.Count + " unmapped G/L account(s) have a non-zero balance or net movement. " +
                    NORMStatementEngine.MappingAccountList(unresolved.Blocking));
            if (unresolved.Offsetting.Count > 0)
                result.Warnings.Add(period + unresolved.Offsetting.Count + " unmapped G/L account(s) have activity that offsets to zero. Review the activity. " +
                    NORMStatementEngine.MappingAccountList(unresolved.Offsetting));
            return unresolved.Count;
        }

        public static DataTable Impact(int releaseId)
        {
            DataRow release = Release(releaseId); int parent = NORMHelper.Int(release, "ParentConfigurationReleaseId");
            if (parent == 0) return new DataTable();
            int importId = LatestImportId(parent, NORMHelper.Int(release, "FinancialYear"), NORMHelper.Str(release, "EntityCode"));
            return NORMHelper.Query(
                "SELECT d.GlCode,ISNULL(tb.Balance,0) Balance,ISNULL(p.StatementLine,'Unmapped') PreviousLine,ISNULL(d.StatementLine,'Unmapped') DraftLine," +
                "ISNULL(p.NoteSubLine,'No note mapping') PreviousNote,ISNULL(d.NoteSubLine,'No note mapping') DraftNote,ISNULL(d.MappingRationale,'') ChangeReason " +
                "FROM dbo.tblNORM_AccountMap d LEFT JOIN dbo.tblNORM_AccountMap p ON p.ConfigurationReleaseId=@parent AND p.GlCode=d.GlCode AND p.IsDeactivated=0 " +
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
            if (validation.ChangedCount == 0 && !validation.HierarchyChanged) throw new InvalidOperationException("The draft contains no recorded mapping or Employee benefits category changes.");
            if (validation.Warnings.Count > 0 && !warningsAcknowledged) throw new InvalidOperationException("Acknowledge the mapping warnings before approval.");
            string contentHash = MappingHash(releaseId);
            if (IsNihracs(release) && NORMStatementEngine.LoadHierarchyAccountTypes(releaseId) != null)
                contentHash = NORMCrypto.Sha256(contentHash + "|" + NORMStatementEngine.ConfigurationFingerprint(releaseId));
            using (OleDbConnection connection = NORMHelper.OpenConnection())
            using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    DataTable current = NORMHelper.Query(connection, transaction,
                        "SELECT ContentHash FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK) " +
                        "WHERE ConfigurationReleaseId=@release AND StatusCode='Draft' AND IsDeactivated=0",
                        NORMHelper.P("@release", releaseId));
                    if (current.Rows.Count != 1 || !Same(Text(current.Rows[0], "ContentHash"), Text(release, "ContentHash")))
                        throw new InvalidOperationException("The draft changed during validation. Reload and review it before approval.");
                    NORMHelper.Exec(connection, transaction,
                        "UPDATE dbo.tblNORM_ConfigurationRelease SET StatusCode='Approved',ContentHash=@hash,ReviewedBy=@user,ReviewedUtc=SYSUTCDATETIME(),ApprovedBy=@user,ApprovedUtc=SYSUTCDATETIME() WHERE ConfigurationReleaseId=@release AND StatusCode='Draft'",
                        NORMHelper.P("@hash", contentHash), NORMHelper.P("@user", user), NORMHelper.P("@release", releaseId));
                    Audit(connection, transaction, "MAPPING_RELEASE_APPROVED", releaseId,
                        "Release " + NORMHelper.Str(release, "VersionCode") + " approved with " + validation.ChangedCount + " changed account(s)" +
                        (validation.HierarchyChanged ? (IsNihracs(release) ? " and reporting category changes" : " and Employee benefits category changes") : "") +
                        "; content SHA-256 " + contentHash + ".", user);
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
        }

        public static NORMImportOutcome RecalculateLatest(int releaseId, string user)
        {
            DataRow release = Release(releaseId);
            if (!String.Equals(NORMHelper.Str(release, "StatusCode"), "Approved", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Approve the mapping release before recalculating.");
            if (IsNihracs(release) && NORMStatementEngine.LoadHierarchyAccountTypes(releaseId) != null)
            {
                string entity = Text(release, "EntityCode");
                int year = NORMHelper.Int(release, "FinancialYear");
                int retainedImport = NORMImportService.FindCurrentAnnualDepartmentalImport(entity, year);
                int comparativeImport = NORMImportService.FindAnnualDepartmentalImport(entity, year, year - 1);
                return new NORMImportOutcome {
                    ImportId = retainedImport,
                    CalculationRunId = NORMStatementEngine.RunAnnualSource(retainedImport, releaseId, user, comparativeImport)
                };
            }
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
                        "INSERT dbo.tblNORM_ImportFile (ImportId,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,CoverageStart,CoverageEnd,IsFullYear,[RowCount],IsStatementInput,FileContent) " +
                        "SELECT @target,SourceType,SourceFileName,SourceFileHash,SourceFileBytes,PeriodStart,PeriodEnd,CoverageStart,CoverageEnd,IsFullYear,[RowCount],IsStatementInput,FileContent FROM dbo.tblNORM_ImportFile WHERE ImportId=@source",
                        NORMHelper.P("@target", importId), NORMHelper.P("@source", sourceImportId));
                    NORMHelper.Exec(connection, transaction,
                        "INSERT dbo.tblNORM_TrialBalanceRow (ImportId,SourceRowNo,SourceCompanyCode,SourceLedger,ReportingStream,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic) " +
                        "SELECT @target,SourceRowNo,SourceCompanyCode,SourceLedger,ReportingStream,GlAccount,GlText,OpeningBalance,DebitMovement,CreditMovement,AccumBalance,RowHash,IsSynthetic FROM dbo.tblNORM_TrialBalanceRow WHERE ImportId=@source AND IsDeactivated=0",
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

        public static int CalculateDraftReview(int releaseId, string user)
        {
            DataRow release = DraftRelease(releaseId);
            string entity = Text(release, "EntityCode");
            int year = NORMHelper.Int(release, "FinancialYear");
            if (!IsNihracs(release) || !NORMReportingFramework.HasHierarchyNoteSource(null, null) ||
                !NORMReportingFramework.UsesNihracsHierarchy(entity, year, releaseId))
                throw new InvalidOperationException("Draft calculation review requires the NIHRACS reporting hierarchy.");
            int importId = NORMImportService.FindCurrentAnnualDepartmentalImport(entity, year);
            int comparativeImport = NORMImportService.FindAnnualDepartmentalImport(entity, year, year - 1);
            return NORMStatementEngine.RunAnnualDraftReview(importId, comparativeImport, releaseId, user);
        }

        private static bool IsNihracs(DataRow release)
        {
            return Same(Text(release, "EntityCode"), "NIHRACS") && NORMHelper.Int(release, "FinancialYear") == 2026;
        }

        private static string MappingHash(int releaseId)
        {
            DataTable rows = NORMHelper.Query("SELECT GlCode,ISNULL(AccountType,'') AccountType,ISNULL(StatementLine,'') StatementLine,ISNULL(NoteSubLine,'') NoteSubLine,ISNULL(CashFlowClass,'') CashFlowClass FROM dbo.tblNORM_AccountMap WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY GlCode", NORMHelper.P("@release", releaseId));
            StringBuilder value = new StringBuilder(); foreach (DataRow row in rows.Rows) value.Append(Text(row, "GlCode")).Append('|').Append(Text(row, "AccountType")).Append('|').Append(Text(row, "StatementLine")).Append('|').Append(Text(row, "NoteSubLine")).Append('|').Append(Text(row, "CashFlowClass")).Append('\n');
            if (NORMStatementEngine.HasEmployeeHierarchy(null, null))
            {
                DataTable hierarchy = NORMHelper.Query("SELECT LineCode,LineLabel,ParentLineCode,SeqNo,IsDeactivated FROM dbo.tblNORM_StatementLine " +
                    "WHERE ConfigurationReleaseId=@release AND StatementCode='SOCI' AND (LineType='employee-detail' OR " +
                    "(LineCode='Employee benefits' AND CalculationKind='Formula' AND FormulaSpec IS NULL)) ORDER BY LineCode",
                    NORMHelper.P("@release", releaseId));
                foreach (DataRow row in hierarchy.Rows)
                    value.Append("Employee hierarchy|").Append(Text(row, "LineCode")).Append('|').Append(Text(row, "ParentLineCode"))
                        .Append('|').Append(row["SeqNo"]).Append('|').Append(row["IsDeactivated"])
                        .Append('|').Append(Text(row, "LineLabel").Length).Append(':').Append(Text(row, "LineLabel")).Append('\n');
            }
            if (HasMappingDescriptions(null, null))
            {
                DataTable descriptions = NORMHelper.Query("SELECT LineCode,MappingDescription FROM dbo.tblNORM_StatementLine " +
                    "WHERE ConfigurationReleaseId=@release AND MappingDescription IS NOT NULL ORDER BY StatementCode,SeqNo",
                    NORMHelper.P("@release", releaseId));
                foreach (DataRow row in descriptions.Rows)
                {
                    string description = Text(row, "MappingDescription");
                    value.Append(Text(row, "LineCode")).Append('|').Append(description.Length).Append(':').Append(description).Append('\n');
                }
            }
            return NORMCrypto.Sha256(value.ToString());
        }

        private static DataRow DraftRelease(int releaseId)
        {
            DataRow row = Release(releaseId); if (!String.Equals(NORMHelper.Str(row, "StatusCode"), "Draft", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only a draft configuration release can be changed."); return row;
        }
        private static DataRow Release(int releaseId)
        {
            DataTable table = NORMHelper.Query("SELECT * FROM dbo.tblNORM_ConfigurationRelease WHERE ConfigurationReleaseId=@release AND IsDeactivated=0", NORMHelper.P("@release", releaseId));
            if (table.Rows.Count == 0) throw new InvalidOperationException("The configuration release was not found.");
            NORMHelper.RequireEntityAccess(NORMHelper.Str(table.Rows[0], "EntityCode"));
            return table.Rows[0];
        }
        private static int LatestImportId(int preferredRelease, int financialYear, string entity)
        {
            object value = NORMHelper.Scalar("SELECT TOP 1 i.ImportId FROM dbo.tblNORM_Import i INNER JOIN dbo.tblNORM_CalculationRun r ON r.ImportId=i.ImportId WHERE i.FinancialYear=@fy AND i.EntityCode=@entity AND i.IsTestBreak=0 AND i.IsDeactivated=0 AND r.StatusCode='Complete' AND r.IsDeactivated=0 AND " +
                NORMHelper.StandardRunPredicate + " ORDER BY CASE WHEN i.ConfigurationReleaseId=@preferred THEN 0 ELSE 1 END,i.ImportId DESC",
                NORMHelper.P("@fy", financialYear), NORMHelper.P("@entity", entity), NORMHelper.P("@preferred", preferredRelease));
            return value == null ? 0 : Convert.ToInt32(value);
        }
        private static int MappingSourceImportId(int preferredRelease, DataRow release)
        {
            int year = NORMHelper.Int(release, "FinancialYear");
            string entity = Text(release, "EntityCode");
            if (NORMReportingFramework.UsesNihracsHierarchy(entity, year, NORMHelper.Int(release, "ConfigurationReleaseId")))
                return NORMImportService.FindCurrentAnnualDepartmentalImport(entity, year);
            int importId = LatestImportId(preferredRelease, year, entity);
            if (importId != 0) return importId;
            return Convert.ToInt32(NORMHelper.Scalar(
                "SELECT TOP 1 ImportId FROM dbo.tblNORM_Import WHERE EntityCode=@entity AND FinancialYear=@fy " +
                "AND IsDeactivated=0 AND IsTestBreak=0 AND ParentImportId IS NULL AND StatusCode IN ('Imported','Calculated') ORDER BY ImportId DESC",
                NORMHelper.P("@entity", entity), NORMHelper.P("@fy", year)));
        }
        private static bool TableExists(OleDbConnection c, OleDbTransaction t, string name) { return Convert.ToInt32(NORMHelper.Scalar(c, t, "SELECT CASE WHEN OBJECT_ID('dbo." + name.Replace("'", "''") + "','U') IS NULL THEN 0 ELSE 1 END")) == 1; }
        private static void Audit(OleDbConnection c, OleDbTransaction t, string code, int release, string detail, string user) { NORMHelper.Exec(c, t, "INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy) VALUES(@code,'ConfigurationRelease',@id,@detail,@user)", NORMHelper.P("@code", code), NORMHelper.P("@id", release.ToString(CultureInfo.InvariantCulture)), NORMHelper.P("@detail", detail.Length > 2000 ? detail.Substring(0, 2000) : detail), NORMHelper.P("@user", user)); }
        private static string Text(DataRow row, string column) { return row == null ? "" : (NORMHelper.Str(row, column) ?? ""); }
        private static string Cell(ExcelWorksheet sheet, int row, int column) { return Convert.ToString(sheet.Cells[row, column].Value, CultureInfo.InvariantCulture).Trim(); }
        private static object Null(string value) { return String.IsNullOrWhiteSpace(value) ? null : (object)value.Trim(); }
        private static string Before(MappingRow row, string column) { return row.Before == null ? "" : Text(row.Before, column); }
        private static bool Different(DataRow before, MappingRow after) { return !Same(Text(before, "AccountType"), after.AccountType) || !Same(Text(before, "StatementLine"), after.StatementLine) || !Same(Text(before, "NoteSubLine"), after.NoteSubLine) || !Same(Text(before, "CashFlowClass"), after.CashFlowClass); }
        private static bool Same(string a, string b) { return String.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase); }
        private sealed class MappingRow { public int RowNumber; public string GlCode; public string GlDescription; public string AccountType; public string StatementLine; public string NoteSubLine; public string CashFlowClass; public string Reason; public DataRow Before; public bool Changed; }
    }
}
