using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

/// <summary>PRIMA/Defence administered schedules and note tables, kept separate from departmental balances.</summary>
public static class NORMAdministeredStatements
{
    public sealed class Definition
    {
        public string StatementCode;
        public int SeqNo;
        public string Type;
        public string LineCode;
        public string Label;
        public string NoteRef;
        public string GroupCode;
        public string GroupTitle;
    }

    public sealed class Row
    {
        public string Type;
        public string Code;
        public string Label;
        public string Note;
        public decimal? Current;
        public decimal? Prior;
        public decimal? Budget;
        public decimal? Published;
        public string Status;
        public long ResultId;
        public string Source;
    }

    public sealed class Statement
    {
        public string Code;
        public string Title;
        public bool AtDate;
        public List<Row> Rows = new List<Row>();
    }

    public sealed class NoteSection
    {
        public string Code;
        public string Title;
        public List<Row> Rows = new List<Row>();
    }

    public sealed class Model
    {
        public List<Statement> Statements = new List<Statement>();
        public List<NoteSection> Notes = new List<NoteSection>();
        public bool UsesPublishedCurrentFallback;
    }

    private static Definition D(string statement, int seq, string type, string code, string label, string note)
    {
        return new Definition { StatementCode = statement, SeqNo = seq, Type = type, LineCode = code, Label = label, NoteRef = note };
    }

    private static Definition N(string statement, int seq, string type, string code, string label, string note,
        string groupCode, string groupTitle)
    {
        Definition value = D(statement, seq, type, code, label, note);
        value.GroupCode = groupCode;
        value.GroupTitle = groupTitle;
        return value;
    }

    public static List<Definition> PrimaryDefinitions()
    {
        List<Definition> rows = new List<Definition>();
        rows.AddRange(new Definition[] {
            D("ADMIN_SOCI",10,"major",null,"NET COST OF SERVICES",null),
            D("ADMIN_SOCI",20,"subsection",null,"EXPENSES",null),
            D("ADMIN_SOCI",30,"line","ADMIN_SOCI_EMPLOYEE","Employee benefits","2.1A"),
            D("ADMIN_SOCI",40,"line","ADMIN_SOCI_SUBSIDIES","Subsidies","2.1B"),
            D("ADMIN_SOCI",50,"line","ADMIN_SOCI_IMPAIRMENT","Impairment loss allowance on financial instruments","2.1C"),
            D("ADMIN_SOCI",60,"total","ADMIN_SOCI_TOTAL_EXPENSES","Total expenses",null),
            D("ADMIN_SOCI",70,"major",null,"INCOME",null),
            D("ADMIN_SOCI",80,"subsection",null,"Revenue",null),
            D("ADMIN_SOCI",90,"subsection",null,"Non taxation revenue",null),
            D("ADMIN_SOCI",100,"line","ADMIN_SOCI_FEES","Fees and fines","2.2A"),
            D("ADMIN_SOCI",110,"line","ADMIN_SOCI_SUPER_CONTRIB","Military superannuation contributions","2.2B"),
            D("ADMIN_SOCI",120,"line","ADMIN_SOCI_OTHER_REVENUE","Other revenue","2.2C"),
            D("ADMIN_SOCI",130,"total","ADMIN_SOCI_TOTAL_NONTAX","Total non-taxation revenue",null),
            D("ADMIN_SOCI",140,"total","ADMIN_SOCI_TOTAL_INCOME","Total Income",null),
            D("ADMIN_SOCI",150,"total","ADMIN_SOCI_NET_COST","Net (cost of) / contribution by services",null),
            D("ADMIN_SOCI",160,"major",null,"OTHER COMPREHENSIVE INCOME",null),
            D("ADMIN_SOCI",170,"subsection",null,"Items not subject to subsequent reclassification to net cost of services",null),
            D("ADMIN_SOCI",180,"line","ADMIN_SOCI_REVALUATION","Changes in asset revaluation surplus",null),
            D("ADMIN_SOCI",190,"line","ADMIN_SOCI_ACTUARIAL","Actuarial gains / (losses) on defined benefits plans","4.5"),
            D("ADMIN_SOCI",200,"total","ADMIN_SOCI_TOTAL_OCI","Total other comprehensive income / (loss)",null),
            D("ADMIN_SOCI",210,"total","ADMIN_SOCI_TOTAL_COMPREHENSIVE","Total comprehensive (loss) / income",null),

            D("ADMIN_SOFP",10,"major",null,"ASSETS",null),
            D("ADMIN_SOFP",20,"subsection",null,"Financial assets",null),
            D("ADMIN_SOFP",30,"line","ADMIN_SOFP_RECEIVABLES","Trade and other receivables","4.1A"),
            D("ADMIN_SOFP",40,"line","ADMIN_SOFP_INVESTMENTS","Equity accounted investments","4.1B"),
            D("ADMIN_SOFP",50,"total","ADMIN_SOFP_TOTAL_FINANCIAL","Total financial assets",null),
            D("ADMIN_SOFP",60,"subsection",null,"Non-financial assets",null),
            D("ADMIN_SOFP",70,"line","ADMIN_SOFP_OTHER_NONFIN","Other non-financial assets","4.2A"),
            D("ADMIN_SOFP",80,"total","ADMIN_SOFP_TOTAL_NONFIN","Total non-financial assets",null),
            D("ADMIN_SOFP",90,"total","ADMIN_SOFP_TOTAL_ASSETS","Total assets administered on behalf of Government",null),
            D("ADMIN_SOFP",100,"major",null,"LIABILITIES",null),
            D("ADMIN_SOFP",110,"subsection",null,"Payables",null),
            D("ADMIN_SOFP",120,"line","ADMIN_SOFP_OTHER_PAYABLES","Other payables","4.3A"),
            D("ADMIN_SOFP",130,"total","ADMIN_SOFP_TOTAL_PAYABLES","Total payables",null),
            D("ADMIN_SOFP",140,"subsection",null,"Provisions",null),
            D("ADMIN_SOFP",150,"line","ADMIN_SOFP_EMP_PROVISIONS","Employee provisions","4.4A"),
            D("ADMIN_SOFP",160,"total","ADMIN_SOFP_TOTAL_PROVISIONS","Total provisions",null),
            D("ADMIN_SOFP",170,"total","ADMIN_SOFP_TOTAL_LIABILITIES","Total liabilities administered on behalf of Government",null),
            D("ADMIN_SOFP",180,"total","ADMIN_SOFP_NET_LIABILITIES","Net liabilities",null),

            D("ADMIN_RECON",10,"line","ADMIN_RECON_OPENING","Opening assets less liabilities as at 1 July",null),
            D("ADMIN_RECON",20,"subsection",null,"Net (cost of) / contribution by services",null),
            D("ADMIN_RECON",30,"line","ADMIN_RECON_INCOME","Income","2.2A to C"),
            D("ADMIN_RECON",40,"line","ADMIN_RECON_EXPENSES","Payments to entities other than corporate Commonwealth entities","2.1A to C"),
            D("ADMIN_RECON",50,"subsection",null,"Other comprehensive income",null),
            D("ADMIN_RECON",60,"line","ADMIN_RECON_DHA_REVAL","Revaluations taken to / (from) reserves - Defence Housing Australia (DHA)","4.1B"),
            D("ADMIN_RECON",70,"line","ADMIN_RECON_SMALL_REVAL","Revaluations taken to / (from) reserves - Small portfolio entities","4.1B"),
            D("ADMIN_RECON",80,"line","ADMIN_RECON_ACTUARIAL","Actuarial gains / (losses)","4.5"),
            D("ADMIN_RECON",90,"subsection",null,"Transfers (to) / from the Australian Government",null),
            D("ADMIN_RECON",100,"line","ADMIN_RECON_SPECIAL_LIMITED","Special appropriations (limited) - payments to entities other than corporate Commonwealth entities","5.1C"),
            D("ADMIN_RECON",110,"line","ADMIN_RECON_SPECIAL_UNLIMITED","Special appropriations (unlimited) - payments to entities other than corporate Commonwealth entities","5.1C"),
            D("ADMIN_RECON",120,"line","ADMIN_RECON_TO_OPA","Transfers to OPA",null),
            D("ADMIN_RECON",130,"line","ADMIN_RECON_WRITE_OFF","Write off of liabilities",null),
            D("ADMIN_RECON",140,"line","ADMIN_RECON_FUNDED_BENEFITS","Funded benefit payments to the members, not drawn down from special appropriations",null),
            D("ADMIN_RECON",150,"total","ADMIN_RECON_CLOSING","Closing assets less liabilities as at 30 June",null),

            D("ADMIN_CASH",10,"major",null,"OPERATING ACTIVITIES",null),
            D("ADMIN_CASH",20,"subsection",null,"Cash received",null),
            D("ADMIN_CASH",30,"line","ADMIN_CASH_FEES","Fees",null),
            D("ADMIN_CASH",40,"line","ADMIN_CASH_SUPER_CONTRIB","Superannuation contributions",null),
            D("ADMIN_CASH",50,"line","ADMIN_CASH_OTHER_RECEIVED","Other",null),
            D("ADMIN_CASH",60,"total","ADMIN_CASH_RECEIVED_TOTAL","Total cash received",null),
            D("ADMIN_CASH",70,"subsection",null,"Cash used",null),
            D("ADMIN_CASH",80,"line","ADMIN_CASH_SUBSIDIES","Subsidies",null),
            D("ADMIN_CASH",90,"line","ADMIN_CASH_EMPLOYEES","Employees",null),
            D("ADMIN_CASH",100,"total","ADMIN_CASH_USED_TOTAL","Total cash used",null),
            D("ADMIN_CASH",110,"total","ADMIN_CASH_OPERATING_NET","Net cash (used by) operating activities",null),
            D("ADMIN_CASH",120,"major",null,"INVESTING ACTIVITIES",null),
            D("ADMIN_CASH",130,"subsection",null,"Cash received",null),
            D("ADMIN_CASH",140,"line","ADMIN_CASH_DIVIDENDS","Dividends",null),
            D("ADMIN_CASH",150,"total","ADMIN_CASH_INVESTING_NET","Net cash from investing activities",null),
            D("ADMIN_CASH",160,"total","ADMIN_CASH_NET_DECREASE","Net (decrease) in cash held",null),
            D("ADMIN_CASH",170,"line","ADMIN_CASH_OPA_FROM","Cash from the Official Public Account for appropriations",null),
            D("ADMIN_CASH",180,"total","ADMIN_CASH_OPA_FROM_TOTAL","Total cash from Official Public Account",null),
            D("ADMIN_CASH",190,"line","ADMIN_CASH_OPA_TO","Cash to Official Public Account - appropriations",null),
            D("ADMIN_CASH",200,"total","ADMIN_CASH_OPA_TO_TOTAL","Total cash to Official Public Account",null),
            D("ADMIN_CASH",210,"line","ADMIN_CASH_OPEN","Cash and cash equivalents at the beginning of the reporting period",null),
            D("ADMIN_CASH",220,"line","ADMIN_CASH_TRANSFER_DEPT","Transfer to Departmental",null),
            D("ADMIN_CASH",230,"total","ADMIN_CASH_CLOSE","Cash and cash equivalents at the end of the reporting period",null)
        });
        return rows;
    }

    public static List<Definition> NoteDefinitions()
    {
        List<Definition> rows = new List<Definition>();
        string g2 = "2. Income and Expenses Administered on Behalf of Government";
        rows.AddRange(new Definition[] {
            N("ADMIN_NOTE_2",10,"subsection",null,"2.1 Administered - Expenses",null,"2",g2),
            N("ADMIN_NOTE_2",20,"lead",null,"2.1A: Employee benefits",null,"2",g2),
            N("ADMIN_NOTE_2",30,"line","ADMIN_N2_SERVICE_COST","Net service cost",null,"2",g2),
            N("ADMIN_NOTE_2",40,"line","ADMIN_N2_INTEREST_COST","Net interest cost",null,"2",g2),
            N("ADMIN_NOTE_2",50,"line","ADMIN_N2_RETENTION","Retention benefits",null,"2",g2),
            N("ADMIN_NOTE_2",60,"total","ADMIN_N2_TOTAL_EMPLOYEE","Total employee benefits","2.1A","2",g2),
            N("ADMIN_NOTE_2",70,"lead",null,"2.1B: Subsidies",null,"2",g2),
            N("ADMIN_NOTE_2",80,"line","ADMIN_N2_DHOAS","Defence Home Ownership Assistance Scheme",null,"2",g2),
            N("ADMIN_NOTE_2",90,"total","ADMIN_N2_TOTAL_SUBSIDIES","Total subsidies","2.1B","2",g2),
            N("ADMIN_NOTE_2",100,"lead",null,"2.1C: Impairment loss allowance on financial instruments",null,"2",g2),
            N("ADMIN_NOTE_2",110,"line","ADMIN_N2_IMPAIRMENT","Impairment on trade and other receivables",null,"2",g2),
            N("ADMIN_NOTE_2",120,"total","ADMIN_N2_TOTAL_IMPAIRMENT","Total impairment loss allowance on financial instruments","2.1C","2",g2),
            N("ADMIN_NOTE_2",130,"subsection",null,"2.2 Administered - Income",null,"2",g2),
            N("ADMIN_NOTE_2",140,"line","ADMIN_N2_FEES","Licence fees","2.2A","2",g2),
            N("ADMIN_NOTE_2",150,"line","ADMIN_N2_SUPER_CONTRIB","Military superannuation contributions","2.2B","2",g2),
            N("ADMIN_NOTE_2",160,"line","ADMIN_N2_DHA_REVENUE","Competitive neutrality revenue - Defence Housing Australia","2.2C","2",g2),
            N("ADMIN_NOTE_2",170,"line","ADMIN_N2_OTHER_REVENUE","Other","2.2C","2",g2),
            N("ADMIN_NOTE_2",180,"total","ADMIN_N2_TOTAL_OTHER_REVENUE","Total other revenue","2.2C","2",g2)
        });

        string g4 = "4. Assets and Liabilities Administered on Behalf of Government";
        rows.AddRange(new Definition[] {
            N("ADMIN_NOTE_4",10,"subsection",null,"4.1 Administered - Financial Assets",null,"4",g4),
            N("ADMIN_NOTE_4",20,"lead",null,"4.1A: Trade and other receivables",null,"4",g4),
            N("ADMIN_NOTE_4",30,"line","ADMIN_N4_EXTERNAL_RECEIVABLES","In connection with - external parties",null,"4",g4),
            N("ADMIN_NOTE_4",40,"total","ADMIN_N4_GROSS_RECEIVABLES","Total trade and other receivables (gross)",null,"4",g4),
            N("ADMIN_NOTE_4",50,"line","ADMIN_N4_IMPAIRMENT","Total impairment allowance",null,"4",g4),
            N("ADMIN_NOTE_4",60,"total","ADMIN_N4_NET_RECEIVABLES","Total trade and other receivables (net)","4.1A","4",g4),
            N("ADMIN_NOTE_4",70,"lead",null,"4.1B: Equity accounted investments",null,"4",g4),
            N("ADMIN_NOTE_4",80,"line","ADMIN_N4_DHA_INVESTMENT","Investments in Defence Housing Australia",null,"4",g4),
            N("ADMIN_NOTE_4",90,"line","ADMIN_N4_SMALL_INVESTMENTS","Investments in other small portfolio entities",null,"4",g4),
            N("ADMIN_NOTE_4",100,"total","ADMIN_N4_TOTAL_INVESTMENTS","Total equity accounted investments","4.1B","4",g4),
            N("ADMIN_NOTE_4",110,"subsection",null,"4.2 Administered - Non-financial Assets",null,"4",g4),
            N("ADMIN_NOTE_4",120,"total","ADMIN_N4_PREPAYMENTS","Other non-financial assets","4.2A","4",g4),
            N("ADMIN_NOTE_4",130,"subsection",null,"4.3 Administered - Payables",null,"4",g4),
            N("ADMIN_NOTE_4",140,"total","ADMIN_N4_PAYABLES","Other payables","4.3A","4",g4),
            N("ADMIN_NOTE_4",150,"subsection",null,"4.4 Administered - Provisions",null,"4",g4),
            N("ADMIN_NOTE_4",160,"total","ADMIN_N4_PROVISIONS","Employee provisions","4.4A","4",g4)
        });

        string g73 = "7.3 Administered - Financial Instruments";
        rows.AddRange(new Definition[] {
            N("ADMIN_NOTE_7_3",10,"line","ADMIN_N73_RECEIVABLES","Trade and other receivables","4.1A","7.3",g73),
            N("ADMIN_NOTE_7_3",20,"total","ADMIN_N73_AMORTISED_ASSETS","Total financial assets at amortised cost",null,"7.3",g73),
            N("ADMIN_NOTE_7_3",30,"line","ADMIN_N73_DHA","Investment in Defence Housing Australia","4.1B","7.3",g73),
            N("ADMIN_NOTE_7_3",40,"line","ADMIN_N73_SMALL","Investment in other small portfolio bodies","4.1B","7.3",g73),
            N("ADMIN_NOTE_7_3",50,"total","ADMIN_N73_FVOCI","Total financial assets at fair value through other comprehensive income (investments in equity instruments)",null,"7.3",g73),
            N("ADMIN_NOTE_7_3",60,"total","ADMIN_N73_ASSETS","Carrying amount of financial assets",null,"7.3",g73),
            N("ADMIN_NOTE_7_3",70,"line","ADMIN_N73_PAYABLES","Other payables","4.3A","7.3",g73),
            N("ADMIN_NOTE_7_3",80,"total","ADMIN_N73_LIABILITIES","Carrying amount of financial liabilities",null,"7.3",g73)
        });

        string g75 = "7.5 Administered - Fair Value Measurements";
        rows.AddRange(new Definition[] {
            N("ADMIN_NOTE_7_5",10,"line","ADMIN_N75_INVESTMENT","Administered Investment","4.1B","7.5",g75),
            N("ADMIN_NOTE_7_5",20,"total","ADMIN_N75_TOTAL_ASSETS","Total financial assets",null,"7.5",g75)
        });

        string g82 = "8.2B Administered - Current/non-current distinction";
        rows.AddRange(new Definition[] {
            N("ADMIN_NOTE_8_2B",10,"subsection",null,"Assets expected to be recovered in no more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",20,"line","ADMIN_N82_CURR_RECEIVABLES","Trade and other receivables",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",30,"line","ADMIN_N82_CURR_PREPAYMENTS","Prepayments - no more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",40,"total","ADMIN_N82_CURR_ASSETS","Total no more than 12 months - assets",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",50,"subsection",null,"Assets expected to be recovered in more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",60,"line","ADMIN_N82_NONCURR_INVESTMENTS","Equity accounted investments",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",70,"line","ADMIN_N82_NONCURR_PREPAYMENTS","Prepayments - more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",80,"total","ADMIN_N82_NONCURR_ASSETS","Total more than 12 months - assets",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",90,"total","ADMIN_N82_TOTAL_ASSETS","Total assets",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",100,"subsection",null,"Liabilities expected to be settled in no more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",110,"line","ADMIN_N82_CURR_PAYABLES","Other payables",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",120,"line","ADMIN_N82_CURR_PROVISIONS","Employee provisions - no more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",130,"total","ADMIN_N82_CURR_LIABILITIES","Total no more than 12 months - liabilities",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",140,"subsection",null,"Liabilities expected to be settled in more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",150,"line","ADMIN_N82_NONCURR_PROVISIONS","Employee provisions - more than 12 months",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",160,"total","ADMIN_N82_NONCURR_LIABILITIES","Total more than 12 months - liabilities",null,"8.2B",g82),
            N("ADMIN_NOTE_8_2B",170,"total","ADMIN_N82_TOTAL_LIABILITIES","Total liabilities",null,"8.2B",g82)
        });
        return rows;
    }

    public static List<Definition> ExtractionDefinitions()
    {
        return PrimaryDefinitions().Concat(NoteDefinitions())
            .Where(x => !String.IsNullOrWhiteSpace(x.LineCode)).ToList();
    }

    public static bool Required(NORMReportingFramework.ReportingProfile profile)
    {
        return profile != null && profile.Requirements != null &&
            profile.Requirements.ContainsKey("ADMINISTERED_ACTIVITIES") && profile.Requirements["ADMINISTERED_ACTIVITIES"];
    }

    public static Model Load(int runId, int releaseId, string entityCode)
    {
        Dictionary<string, decimal> calculated = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long> resultIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        DataTable results = NORMHelper.Query(
            "SELECT StatementCode,LineCode,LineResultId,ComputedAmount FROM dbo.tblNORM_LineResult " +
            "WHERE CalculationRunId=@run AND StatementCode LIKE 'ADMIN[_]%' AND IsDeactivated=0",
            NORMHelper.P("@run", runId));
        for (int i = 0; i < results.Rows.Count; i++)
        {
            string key = NORMHelper.Str(results.Rows[i], "StatementCode") + "|" + NORMHelper.Str(results.Rows[i], "LineCode");
            if (!results.Rows[i].IsNull("ComputedAmount")) calculated[key] = NORMHelper.Dec(results.Rows[i], "ComputedAmount");
            resultIds[key] = NORMHelper.Long(results.Rows[i], "LineResultId");
        }

        Dictionary<string, decimal> audited = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, decimal> baselinePrior = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, decimal> baselineBudget = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (NORMStatementEnhancements.IsInstalled())
        {
            DataTable sources = NORMHelper.Query(
                "SELECT StatementCode,LineCode,FigureType,Amount FROM dbo.tblNORM_SourceFigure " +
                "WHERE ConfigurationReleaseId=@release AND StatementCode LIKE 'ADMIN[_]%' AND IsDeactivated=0",
                NORMHelper.P("@release", releaseId));
            for (int i = 0; i < sources.Rows.Count; i++)
            {
                string key = NORMHelper.Str(sources.Rows[i], "StatementCode") + "|" + NORMHelper.Str(sources.Rows[i], "LineCode");
                string type = NORMHelper.Str(sources.Rows[i], "FigureType");
                if (type == "AuditedActual") audited[key] = NORMHelper.Dec(sources.Rows[i], "Amount");
                else if (type == "PriorActual") baselinePrior[key] = NORMHelper.Dec(sources.Rows[i], "Amount");
                else if (type == "OriginalBudget") baselineBudget[key] = NORMHelper.Dec(sources.Rows[i], "Amount");
            }
        }
        Dictionary<string, decimal> setupPrior = NORMStartOfYearSetup.LoadPriorActualFigures(entityCode);
        Dictionary<string, decimal> setupBudget = NORMStartOfYearSetup.LoadOriginalBudgetFigures(entityCode);

        Model model = new Model();
        string[,] primary = new string[,] {
            { "ADMIN_SOCI", "Administered Schedule of Comprehensive Income", "false" },
            { "ADMIN_SOFP", "Administered Schedule of Assets and Liabilities", "true" },
            { "ADMIN_RECON", "Administered Reconciliation Schedule", "false" },
            { "ADMIN_CASH", "Administered Cash Flow Statement", "false" }
        };
        List<Definition> definitions = PrimaryDefinitions();
        for (int p = 0; p < primary.GetLength(0); p++)
        {
            Statement statement = new Statement { Code = primary[p, 0], Title = primary[p, 1], AtDate = primary[p, 2] == "true" };
            foreach (Definition definition in definitions.Where(x => x.StatementCode == statement.Code).OrderBy(x => x.SeqNo))
                statement.Rows.Add(BuildRow(definition, calculated, resultIds, audited, setupPrior, setupBudget, baselinePrior, baselineBudget, model));
            model.Statements.Add(statement);
        }

        foreach (IGrouping<string, Definition> group in NoteDefinitions().GroupBy(x => x.GroupCode))
        {
            Definition first = group.First();
            NoteSection note = new NoteSection { Code = first.GroupCode, Title = first.GroupTitle };
            foreach (Definition definition in group.OrderBy(x => x.SeqNo))
                note.Rows.Add(BuildRow(definition, calculated, resultIds, audited, setupPrior, setupBudget, baselinePrior, baselineBudget, model));
            model.Notes.Add(note);
        }
        return model;
    }

    private static Row BuildRow(Definition definition, Dictionary<string, decimal> calculated,
        Dictionary<string, long> resultIds, Dictionary<string, decimal> audited,
        Dictionary<string, decimal> setupPrior, Dictionary<string, decimal> setupBudget,
        Dictionary<string, decimal> baselinePrior, Dictionary<string, decimal> baselineBudget, Model model)
    {
        Row row = new Row { Type = definition.Type, Code = definition.LineCode, Label = definition.Label,
            Note = definition.NoteRef, Status = "Mapped", Source = "Heading" };
        if (String.IsNullOrWhiteSpace(definition.LineCode)) return row;
        string key = definition.StatementCode + "|" + definition.LineCode;
        decimal value;
        if (calculated.TryGetValue(key, out value))
        {
            row.Current = value;
            row.ResultId = resultIds.ContainsKey(key) ? resultIds[key] : 0L;
            row.Source = "Trial balance";
        }
        else if (audited.TryGetValue(key, out value))
        {
            row.Current = value;
            row.Published = value;
            row.Status = "Published";
            row.Source = "Published current fallback";
            model.UsesPublishedCurrentFallback = true;
        }
        decimal published;
        if (audited.TryGetValue(key, out published)) row.Published = published;
        if (!setupPrior.TryGetValue(key, out value) && !baselinePrior.TryGetValue(key, out value)) value = Decimal.MinValue;
        if (value != Decimal.MinValue) row.Prior = value;
        if (!setupBudget.TryGetValue(key, out value) && !baselineBudget.TryGetValue(key, out value)) value = Decimal.MinValue;
        if (value != Decimal.MinValue) row.Budget = value;
        return row;
    }
}
