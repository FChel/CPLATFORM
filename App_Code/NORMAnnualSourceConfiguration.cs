using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using System.Xml;

/// <summary>The approved source rules for one agency and annual reporting year.</summary>
internal sealed class NORMAnnualSourceConfiguration
{
    internal DataRow Entity;
    internal string Snapshot;
    internal string Fingerprint;
    internal int Year, PriorYears;
    internal readonly DataTable Populations = new DataTable();

    internal static NORMAnnualSourceConfiguration Load(string agency, int year, OleDbConnection connection = null,
        OleDbTransaction transaction = null)
    {
        NORMHelper.RequireEntityAccess(agency, connection, transaction);
        const string sql = "SELECT EntityCode, EntityName, FinancialYear, PriorYearsRequired, SourceApprovalSnapshot, SourceApprovalHash " +
            "FROM dbo.tblNORM_ReportingEntity WITH (HOLDLOCK) WHERE EntityCode=@entity AND FinancialYear=@fy AND IsDeactivated=0";
        OleDbParameter[] args = { NORMHelper.P("@entity", agency), NORMHelper.P("@fy", year) };
        DataTable table = connection == null ? NORMHelper.Query(sql, args) : NORMHelper.Query(connection, transaction, sql, args);
        if (table.Rows.Count != 1) { return null; }
        DataRow row = table.Rows[0];
        string snapshot = NORMHelper.Str(row, "SourceApprovalSnapshot");
        if (snapshot == null)
        {
            if (!row.IsNull("PriorYearsRequired"))
            { throw new InvalidOperationException("Approve the annual source configuration before importing."); }
            return null;
        }
        string hash = NORMCrypto.Sha256(Encoding.Unicode.GetBytes(snapshot));
        if (hash != NORMHelper.Str(row, "SourceApprovalHash"))
        { throw new InvalidOperationException("The annual source approval no longer matches its evidence."); }
        XmlDocument xml = new XmlDocument { XmlResolver = null };
        xml.LoadXml(snapshot);
        XmlElement root = xml.DocumentElement;
        int prior = NORMHelper.Int(row, "PriorYearsRequired");
        if (root.Name != "annual" || root.GetAttribute("agency") != agency ||
            root.GetAttribute("year") != year.ToString(CultureInfo.InvariantCulture) ||
            root.GetAttribute("prior") != prior.ToString(CultureInfo.InvariantCulture) || prior < 0 || year - prior < 1900)
        { throw new InvalidOperationException("The annual source approval does not match agency, year and required coverage."); }
        NORMAnnualSourceConfiguration result = new NORMAnnualSourceConfiguration {
            Entity = row, Snapshot = snapshot, Fingerprint = hash, Year = year, PriorYears = prior
        };
        result.Populations.Columns.Add("SourceCompanyCode", typeof(string));
        result.Populations.Columns.Add("SourceLedger", typeof(string));
        result.Populations.Columns.Add("ReportingStream", typeof(string));
        result.Populations.Columns.Add("IsRequired", typeof(bool));
        HashSet<Tuple<string,string,string>> keys = new HashSet<Tuple<string,string,string>>();
        foreach (XmlElement population in root.SelectNodes("population"))
        {
            string company = population.GetAttribute("company");
            string ledger = population.HasAttribute("ledger") ? population.GetAttribute("ledger") : null;
            string stream = population.GetAttribute("stream");
            string required = population.GetAttribute("required");
            if (company.Length == 0 || company.Length > 20 || company.Trim() != company ||
                (ledger != null && (ledger.Length == 0 || ledger.Length > 20 || ledger.Trim() != ledger)) ||
                (stream != "Departmental" && stream != "Administered") || (required != "0" && required != "1") ||
                !keys.Add(Tuple.Create(company.ToUpperInvariant(), ledger == null ? null : ledger.ToUpperInvariant(), stream)))
            { throw new InvalidOperationException("The annual source approval contains an invalid or duplicate population."); }
            result.Populations.Rows.Add(company, ledger, stream, required == "1");
        }
        if (result.Populations.Rows.Count == 0)
        { throw new InvalidOperationException("The annual source approval contains no populations."); }
        return result;
    }

    internal static IEnumerable<int> RequiredYears(int year, int priorYears)
    {
        for (int offset = 0; offset <= priorYears; offset++) { yield return year - offset; }
    }
}
