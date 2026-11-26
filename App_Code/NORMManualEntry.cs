using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public static class NORMManualEntry
{
    public sealed class InputException : InvalidOperationException
    {
        public InputException(string message) : base(message) { }
    }

    public sealed class Definition
    {
        public string Title, Note, Section, SectionTitle, Unit, CurrentColumn, PriorColumn;
        public decimal UnitFactor;
        public int Precision;
        public Row[] Rows;
        public Split[] Splits;
    }

    public sealed class Row
    {
        public string Key, Label, Kind, Statement, Line;
        public bool Required, Total;
        public Term[] Terms;
    }

    public sealed class Term { public string Key; public int Sign; }
    public sealed class Split { public string Linked, Entered, Residual; }

    public sealed class Cell
    {
        public decimal? Entered, Exact, Presented;
        public long Revision, OriginalPriorRevision, ResultId;
        public string Unit, Source, User, UpdatedUtc, Reason, Issue, Difference;
        public int Precision;
        public bool Amendment;
    }

    public sealed class Schedule
    {
        public string Code, Entity, Stream;
        public int DefinitionId, Run, PriorRun, Release, Year;
        public Definition Layout;
        public Dictionary<string, Cell> Current, Prior;
        public string ContextToken;
    }

    public sealed class Entry
    {
        public string Amount, Source;
    }

    private const string RunScope =
        " FROM dbo.tblNORM_CalculationRun r JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId" +
        " JOIN dbo.tblNORM_ManualSchedule s{0} ON s.EntityCode=i.EntityCode AND s.EffectiveYear<=i.FinancialYear" +
        " WHERE r.StatusCode='Complete' AND r.IsDeactivated=0 AND i.IsDeactivated=0 AND i.IsTestBreak=0 AND " +
        NORMHelper.AccessibleStatementRunPredicate +
        " AND EXISTS (SELECT 1 FROM dbo.tblNORM_TrialBalanceRow t WHERE t.ImportId=i.ImportId AND t.IsDeactivated=0 AND t.ReportingStream=s.ReportingStream)" +
        " AND NOT EXISTS (SELECT 1 FROM dbo.tblNORM_TrialBalanceRow t WHERE t.ImportId=i.ImportId" +
        " AND t.IsDeactivated=0 AND (t.ReportingStream IS NULL OR t.ReportingStream<>s.ReportingStream))";

    public static bool Installed()
    {
        return Convert.ToInt32(NORMHelper.Scalar("SELECT CASE WHEN OBJECT_ID('dbo.tblNORM_ManualSchedule','U') IS NOT NULL" +
            " AND OBJECT_ID('dbo.tblNORM_ManualValueRevision','U') IS NOT NULL" +
            " AND OBJECT_ID('dbo.tblNORM_ManualScheduleEvidence','U') IS NOT NULL" +
            " AND COL_LENGTH('dbo.tblNORM_TrialBalanceRow','ReportingStream') IS NOT NULL" +
            " AND COL_LENGTH('dbo.tblNORM_LineResult','ReportedAmount') IS NOT NULL THEN 1 ELSE 0 END")) == 1;
    }

    public static List<DataRow> Runs()
    {
        return NORMHelper.Query("SELECT DISTINCT r.CalculationRunId,i.EntityCode,i.FinancialYear,s.ReportingStream" + String.Format(RunScope, "") +
            " ORDER BY i.FinancialYear DESC,r.CalculationRunId DESC").AsEnumerable()
            .Where(row => NORMHelper.CanPrepareEntity(NORMHelper.Str(row, "EntityCode"))).ToList();
    }

    public static List<Schedule> Read(int run)
    {
        var schedules = new List<Schedule>();
        if (!Installed()) return schedules;
        using (var connection = NORMHelper.OpenConnection())
        using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            foreach (DataRow row in Definitions(run, connection, transaction).Rows)
            {
                NORMHelper.RequireEntityAccess(NORMHelper.Str(row, "EntityCode"), connection, transaction);
                schedules.Add(Read(row, connection, transaction));
            }
            transaction.Commit();
        }
        return schedules;
    }

    private static DataTable Definitions(int run, OleDbConnection connection, OleDbTransaction transaction, bool forSave = false)
    {
        return NORMHelper.Query(connection, transaction, "SELECT s.*,i.FinancialYear,r.ConfigurationReleaseId,r.CalculationRunId" +
            String.Format(RunScope, forSave ? " WITH (UPDLOCK)" : "") +
            " AND r.CalculationRunId=@run AND s.EffectiveYear=(SELECT MAX(v.EffectiveYear)" +
            " FROM dbo.tblNORM_ManualSchedule v WHERE v.EntityCode=s.EntityCode AND v.ReportingStream=s.ReportingStream" +
            " AND v.ScheduleCode=s.ScheduleCode AND v.EffectiveYear<=i.FinancialYear) ORDER BY s.ManualScheduleId",
            NORMHelper.P("@run", run));
    }

    private static Schedule Read(DataRow row, OleDbConnection connection, OleDbTransaction transaction)
    {
        var schedule = new Schedule {
            Code = NORMHelper.Str(row, "ScheduleCode"), DefinitionId = NORMHelper.Int(row, "ManualScheduleId"),
            Entity = NORMHelper.Str(row, "EntityCode"), Stream = NORMHelper.Str(row, "ReportingStream"),
            Run = NORMHelper.Int(row, "CalculationRunId"), Release = NORMHelper.Int(row, "ConfigurationReleaseId"),
            Year = NORMHelper.Int(row, "FinancialYear"), Layout = Deserialize<Definition>(NORMHelper.Str(row, "Definition"))
        };
        Validate(schedule.Layout);
        schedule.PriorRun = Convert.ToInt32(NORMHelper.Scalar(connection, transaction, NORMStatementEngine.ComparativeRunSql, NORMHelper.P("@run", schedule.Run)));
        DataTable values = NORMHelper.Query(connection, transaction,
            "SELECT * FROM (SELECT v.*,ROW_NUMBER() OVER(PARTITION BY ReportingYear,InputCode,IsComparative ORDER BY RevisionId DESC) AS Seq" +
            " FROM dbo.tblNORM_ManualValueRevision v WHERE EntityCode=@entity AND ReportingStream=@stream AND ScheduleCode=@code" +
            " AND (ReportingYear=@year OR (ReportingYear=@prior AND IsComparative=0))) latest WHERE Seq=1",
            NORMHelper.P("@entity", schedule.Entity), NORMHelper.P("@stream", schedule.Stream), NORMHelper.P("@code", schedule.Code),
            NORMHelper.P("@year", schedule.Year), NORMHelper.P("@prior", schedule.Year - 1));
        DataTable links = schedule.Layout.Rows.Any(item => item.Kind == "Linked")
            ? NORMHelper.Query(connection, transaction, "SELECT LineResultId,CalculationRunId,StatementCode,LineCode,ComputedAmount,ReportedAmount" +
                " FROM dbo.tblNORM_LineResult WHERE CalculationRunId IN (@run,@prior) AND IsDeactivated=0",
                NORMHelper.P("@run", schedule.Run), NORMHelper.P("@prior", schedule.PriorRun))
            : new DataTable();
        schedule.Current = Evaluate(schedule, values, links, false);
        schedule.Prior = Evaluate(schedule, values, links, true);
        var evidence = new List<object> { schedule.DefinitionId, NORMHelper.Str(row, "Definition"), schedule.Run, schedule.PriorRun, schedule.Release };
        foreach (Row item in schedule.Layout.Rows)
        {
            foreach (Cell cell in new[] { schedule.Current[item.Key], schedule.Prior[item.Key] })
                evidence.Add(new object[] { cell.Revision, cell.ResultId, cell.Exact, cell.Presented, cell.Issue });
        }
        schedule.ContextToken = NORMCrypto.Sha256(Serialize(evidence));
        return schedule;
    }

    private static void Validate(Definition layout)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        if (layout == null || layout.UnitFactor <= 0 || layout.Precision < 0 || layout.Precision > 3 ||
            String.IsNullOrWhiteSpace(layout.Unit) || layout.Unit.Length > 24 || layout.Rows == null || layout.Rows.Length == 0 ||
            String.IsNullOrWhiteSpace(layout.Note) || String.IsNullOrWhiteSpace(layout.Title) ||
            String.IsNullOrWhiteSpace(layout.CurrentColumn) || String.IsNullOrWhiteSpace(layout.PriorColumn))
            throw new InvalidOperationException("Invalid Manual Entry schedule definition.");
        foreach (Row row in layout.Rows)
        {
            if (row == null || !Regex.IsMatch(row.Key ?? "", @"\A[A-Z0-9_]{1,40}\z") || known.ContainsKey(row.Key) ||
                (row.Kind != "Manual" && row.Kind != "Linked" && row.Kind != "Calculated") ||
                (row.Kind == "Linked" && (String.IsNullOrWhiteSpace(row.Statement) || String.IsNullOrWhiteSpace(row.Line))) ||
                (row.Kind == "Calculated" && (row.Terms == null || row.Terms.Length == 0 ||
                    row.Terms.Any(term => term == null || term.Key == null || !known.ContainsKey(term.Key) || Math.Abs(term.Sign) != 1))))
                throw new InvalidOperationException("Invalid Manual Entry row definition.");
            known.Add(row.Key, row.Kind);
        }
        foreach (Split split in layout.Splits ?? new Split[0])
        {
            if (split == null || !known.ContainsKey(split.Linked ?? "") || known[split.Linked] != "Linked" ||
                !known.ContainsKey(split.Entered ?? "") || known[split.Entered] != "Manual" ||
                !known.ContainsKey(split.Residual ?? "") || known[split.Residual] != "Calculated")
                throw new InvalidOperationException("Invalid Manual Entry reconciliation definition.");
        }
    }

    private static Dictionary<string, Cell> Evaluate(Schedule schedule, DataTable values, DataTable links, bool prior)
    {
        Definition layout = schedule.Layout;
        int run = prior ? schedule.PriorRun : schedule.Run;
        var cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
        foreach (Row row in layout.Rows)
        {
            var cell = new Cell();
            if (row.Kind == "Manual")
            {
                DataRow revision = values.AsEnumerable().FirstOrDefault(v => NORMHelper.Str(v, "InputCode") == row.Key &&
                    NORMHelper.Int(v, "ReportingYear") == schedule.Year && Convert.ToBoolean(v["IsComparative"]) == prior);
                if (prior && revision == null)
                    revision = values.AsEnumerable().FirstOrDefault(v => NORMHelper.Str(v, "InputCode") == row.Key &&
                        NORMHelper.Int(v, "ReportingYear") == schedule.Year - 1 && !Convert.ToBoolean(v["IsComparative"]));
                if (revision != null)
                {
                    cell.Entered = NullableAmount(revision, "Amount");
                    cell.Revision = NORMHelper.Long(revision, "RevisionId");
                    cell.Unit = NORMHelper.Str(revision, "Unit");
                    cell.Precision = NORMHelper.Int(revision, "InputPrecision");
                    cell.Source = NORMHelper.Str(revision, "SourceReference");
                    cell.User = NORMHelper.Str(revision, "EnteredBy");
                    cell.UpdatedUtc = Convert.ToDateTime(revision["EnteredUtc"], CultureInfo.InvariantCulture).ToString("u", CultureInfo.InvariantCulture);
                    cell.Amendment = Convert.ToBoolean(revision["IsComparative"]);
                    cell.Reason = NORMHelper.Str(revision, "ChangeReason");
                    cell.OriginalPriorRevision = revision.IsNull("OriginalPriorRevisionId") ? 0 : NORMHelper.Long(revision, "OriginalPriorRevisionId");
                    if (cell.Entered.HasValue && (cell.Unit != layout.Unit || cell.Precision != layout.Precision))
                        cell.Issue = "The saved unit or precision differs from the definition. Re-enter the amount in the configured unit.";
                    else
                        cell.Presented = cell.Entered;
                }
                if (row.Required && !cell.Entered.HasValue) cell.Issue = "Required amount missing.";
            }
            else if (row.Kind == "Linked")
            {
                DataRow link = links.AsEnumerable().FirstOrDefault(v => NORMHelper.Int(v, "CalculationRunId") == run &&
                    NORMHelper.Str(v, "StatementCode") == row.Statement && NORMHelper.Str(v, "LineCode") == row.Line);
                if (link != null)
                {
                    cell.ResultId = NORMHelper.Long(link, "LineResultId");
                    cell.Exact = NORMHelper.Dec(link, "ComputedAmount") * 1000m / layout.UnitFactor;
                    decimal? reported = NullableAmount(link, "ReportedAmount") * 1000m / layout.UnitFactor;
                    if (reported.HasValue && Decimal.Round(reported.Value, layout.Precision) == reported.Value)
                        cell.Presented = reported;
                }
                if (!cell.Presented.HasValue) cell.Issue = "Linked reporting balance is missing or cannot be expressed at the configured precision.";
            }
            else if (row.Terms.All(term => cells[term.Key].Presented.HasValue && cells[term.Key].Issue == null))
                cell.Presented = row.Terms.Sum(term => term.Sign * cells[term.Key].Presented.Value);
            else
                cell.Issue = "Calculation awaits its inputs.";
            cells.Add(row.Key, cell);
        }
        foreach (Split split in layout.Splits ?? new Split[0])
            Reconcile(cells[split.Linked], cells[split.Entered], cells[split.Residual]);
        return cells;
    }

    private static void Reconcile(Cell linked, Cell entered, Cell residual)
    {
        if (!linked.Presented.HasValue || !entered.Presented.HasValue || !residual.Presented.HasValue) return;
        if (entered.Presented.Value < 0 || residual.Presented.Value < 0 ||
            entered.Presented.Value + residual.Presented.Value != linked.Presented.Value)
        {
            residual.Issue = "Invalid classification. The split must be non-negative and reconcile to the applicable liability.";
            residual.Presented = null;
        }
        decimal difference = linked.Exact.Value - linked.Presented.Value;
        if (difference != 0)
            linked.Difference = "Exact balance " + linked.Exact.Value.ToString("0.###", CultureInfo.InvariantCulture) +
                ". Presented at " + linked.Presented.Value.ToString(CultureInfo.InvariantCulture) + ".";
    }

    public static long Save(int run, string code, string expected, string reason,
        Func<string, bool, Entry> readEntry)
    {
        using (var connection = NORMHelper.OpenConnection())
        using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            DataRow definition = Definitions(run, connection, transaction, true).AsEnumerable().SingleOrDefault(row => NORMHelper.Str(row, "ScheduleCode") == code);
            if (definition == null) throw new InputException("Select a schedule on an eligible calculation.");
            NORMHelper.RequireEntityAccess(NORMHelper.Str(definition, "EntityCode"), connection, transaction);
            Schedule before = Read(definition, connection, transaction);
            if (before.ContextToken != expected)
                throw new InputException("The schedule or its source changed. Review the current values before saving again.");
            reason = Limit(reason, 500, "Change reason");
            string user = NORMHelper.CurrentUserId();
            foreach (Row row in before.Layout.Rows.Where(item => item.Kind == "Manual"))
            {
                SaveEntry(connection, transaction, before, row, false, readEntry(row.Key, false), reason, user);
                SaveEntry(connection, transaction, before, row, true, readEntry(row.Key, true), reason, user);
            }
            Schedule after = Read(definition, connection, transaction);
            string snapshot = Serialize(after);
            NORMHelper.Exec(connection, transaction,
                "IF NOT EXISTS(SELECT 1 FROM dbo.tblNORM_ManualScheduleEvidence WITH (UPDLOCK,HOLDLOCK)" +
                " WHERE CalculationRunId=@run AND ManualScheduleId=@definition AND EvidenceHash=@hash)" +
                " INSERT dbo.tblNORM_ManualScheduleEvidence(CalculationRunId,ManualScheduleId,EvidenceHash,EvidenceJson,RecordedBy)" +
                " VALUES(@run,@definition,@hash,@json,@user)",
                NORMHelper.P("@run", run), NORMHelper.P("@definition", after.DefinitionId), NORMHelper.P("@hash", after.ContextToken), LargeText("@json", snapshot), NORMHelper.P("@user", user));
            long evidenceId = Convert.ToInt64(NORMHelper.Scalar(connection, transaction,
                "SELECT EvidenceId FROM dbo.tblNORM_ManualScheduleEvidence WHERE CalculationRunId=@run AND ManualScheduleId=@definition AND EvidenceHash=@hash",
                NORMHelper.P("@run", run), NORMHelper.P("@definition", after.DefinitionId), NORMHelper.P("@hash", after.ContextToken)));
            transaction.Commit();
            return evidenceId;
        }
    }

    private static void SaveEntry(OleDbConnection connection, OleDbTransaction transaction, Schedule schedule,
        Row row, bool prior, Entry entry, string reason, string user)
    {
        if (entry == null || entry.Amount == null || entry.Source == null)
            throw new InputException("Submit the complete schedule.");
        string text = entry.Amount.Trim();
        string source = Limit(entry.Source, 500, "Source reference");
        Cell previous = (prior ? schedule.Prior : schedule.Current)[row.Key];
        decimal? amount = null;
        if (text.Length != 0)
        {
            int precision = schedule.Layout.Precision;
            string pattern = precision == 0 ? @"\A[+-]?\d{1,15}\z" : @"\A[+-]?\d{1,15}(\.\d{1," + precision + @"})?\z";
            decimal parsed;
            if (!Regex.IsMatch(text, pattern) || !Decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out parsed))
                throw new InputException(row.Label + " must be entered in " + schedule.Layout.Unit +
                    (precision == 0 ? " as a whole number." : " with at most " + precision + " decimal places."));
            if (source == null) throw new InputException("Supply a source reference or explanation for " + row.Label + ".");
            amount = parsed;
        }
        else source = null;
        if (amount == previous.Entered && source == previous.Source &&
            (amount == null || (previous.Unit == schedule.Layout.Unit && previous.Precision == schedule.Layout.Precision))) return;
        if ((prior || previous.Revision != 0) && reason == null)
            throw new InputException("Record a change reason for amendments to saved or comparative values.");

        long original = previous.Amendment ? previous.OriginalPriorRevision : previous.Revision;
        NORMHelper.Exec(connection, transaction,
            "INSERT dbo.tblNORM_ManualValueRevision(EntityCode,ReportingStream,ReportingYear,ScheduleCode,InputCode,IsComparative," +
            " ManualScheduleId,Amount,Unit,InputPrecision,SourceReference,PreviousRevisionId,OriginalPriorRevisionId,ChangeReason,EnteredBy)" +
            " VALUES(@entity,@stream,@year,@code,@input,@prior,@definition,CONVERT(decimal(19,3),@amount),@unit,@precision,@source," +
            " NULLIF(@previous,0),NULLIF(@original,0),@reason,@user)",
            NORMHelper.P("@entity", schedule.Entity), NORMHelper.P("@stream", schedule.Stream), NORMHelper.P("@year", schedule.Year),
            NORMHelper.P("@code", schedule.Code), NORMHelper.P("@input", row.Key), NORMHelper.P("@prior", prior), NORMHelper.P("@definition", schedule.DefinitionId),
            NORMHelper.P("@amount", amount.HasValue ? amount.Value.ToString(CultureInfo.InvariantCulture) : null),
            NORMHelper.P("@unit", schedule.Layout.Unit), NORMHelper.P("@precision", schedule.Layout.Precision), NORMHelper.P("@source", source),
            NORMHelper.P("@previous", previous.Revision), NORMHelper.P("@original", prior ? original : 0L), NORMHelper.P("@reason", reason), NORMHelper.P("@user", user));
    }

    public static DataTable Evidence(Schedule schedule)
    {
        return History(schedule, 0);
    }

    public static Schedule Evidence(Schedule schedule, long id)
    {
        DataTable history = History(schedule, id);
        if (history.Rows.Count != 1) throw new InputException("That evidence is unavailable for this schedule.");
        return Deserialize<Schedule>(NORMHelper.Str(history.Rows[0], "EvidenceJson"));
    }

    private static DataTable History(Schedule schedule, long id)
    {
        string fields = id == 0 ? "e.EvidenceId,e.CalculationRunId,e.EvidenceHash,e.RecordedBy,e.RecordedUtc" : "e.EvidenceJson";
        return NORMHelper.Query("SELECT " + fields + " FROM dbo.tblNORM_ManualScheduleEvidence e" +
            " JOIN dbo.tblNORM_ManualSchedule s ON s.ManualScheduleId=e.ManualScheduleId" +
            " JOIN dbo.tblNORM_CalculationRun r ON r.CalculationRunId=e.CalculationRunId" +
            " JOIN dbo.tblNORM_Import i ON i.ImportId=r.ImportId" +
            " WHERE i.FinancialYear=@year AND s.EntityCode=@entity AND s.ReportingStream=@stream AND s.ScheduleCode=@code" +
            " AND (@id=0 OR e.EvidenceId=@id) ORDER BY e.EvidenceId DESC",
            NORMHelper.P("@year", schedule.Year), NORMHelper.P("@entity", schedule.Entity), NORMHelper.P("@stream", schedule.Stream),
            NORMHelper.P("@code", schedule.Code), NORMHelper.P("@id", id));
    }

    private static string Limit(string value, int length, string label)
    {
        string text = (value ?? "").Trim();
        if (text.Length > length) throw new InputException(label + " is too long.");
        return text.Length == 0 ? null : text;
    }

    private static decimal? NullableAmount(DataRow row, string column)
    { return row.IsNull(column) ? (decimal?)null : NORMHelper.Dec(row, column); }

    private static OleDbParameter LargeText(string name, string value)
    {
        OleDbParameter parameter = NORMHelper.P(name, value);
        parameter.OleDbType = OleDbType.LongVarWChar;
        return parameter;
    }

    private static string Serialize(object value) { return new JavaScriptSerializer().Serialize(value); }

    private static T Deserialize<T>(string json) { return new JavaScriptSerializer().Deserialize<T>(json); }

}
