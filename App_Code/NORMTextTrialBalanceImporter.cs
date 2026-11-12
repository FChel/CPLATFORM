using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Two approved native text layouts; no persistence or agency selection.</summary>
public static class NORMTextTrialBalanceImporter
{
    public const string CsvProfile = "ACTEWAGL-FINSTATS/1";
    public const string FixedProfile = "ICON-WATER-FIXED/1";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex Number = new Regex(@"^-?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$");

    public static NORMParsedImport Parse(byte[] bytes, int expectedYear)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidDataException("Unsupported text encoding: expected UTF-8 or ASCII."); }
        if (text.IndexOf('\0') >= 0) { throw new InvalidDataException("Unsupported binary source layout."); }
        try
        {
            if (text.StartsWith("\"Company : \",", StringComparison.Ordinal)) { return Csv(text, expectedYear); }
            if (text.Contains("Icon Water Limited Ledger") && text.Contains("Trial Balance - Total Currency")) { return Fixed(text, expectedYear); }
            throw new InvalidDataException("Unsupported source layout. Expected Defence XLSX, ActewAGL FinStats CSV or Icon Water fixed-width text.");
        }
        catch (OverflowException) { throw new InvalidDataException("Source amounts or totals exceed the supported decimal range."); }
    }

    private static decimal Amount(string value, int row)
    {
        decimal result;
        if (!Number.IsMatch(value) || !Decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, Inv, out result))
        { throw new InvalidDataException("Invalid decimal amount at source record " + row + "."); }
        // Decimal.Parse may round more than 28 significant digits. Compare the
        // exact numeric token after insignificant zero removal, never round it.
        string canonical = value.Replace(",", "");
        bool negative = canonical.StartsWith("-", StringComparison.Ordinal);
        if (negative) { canonical = canonical.Substring(1); }
        string[] parts = canonical.Split('.');
        string whole = parts[0].TrimStart('0');
        if (whole.Length == 0) { whole = "0"; }
        string fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        canonical = whole + (fraction.Length == 0 ? "" : "." + fraction);
        if (negative && canonical != "0") { canonical = "-" + canonical; }
        if (result.ToString("0.############################", Inv) != canonical)
        { throw new InvalidDataException("Source amount exceeds lossless decimal precision at record " + row + "."); }
        return result;
    }

    // Strict CSV tokenisation: escaped quotes/newlines supported, malformed quotes fail.
    private static List<List<string>> Records(string text)
    {
        List<List<string>> records = new List<List<string>>();
        List<string> row = new List<string>(); StringBuilder field = new StringBuilder();
        bool quoted = false, closed = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(c); }
                continue;
            }
            if (c == '"' && field.Length == 0 && !closed) { quoted = true; continue; }
            if (c == ',' || c == '\r' || c == '\n')
            {
                row.Add(field.ToString()); field.Length = 0; closed = false;
                if (c != ',')
                {
                    records.Add(row); row = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { i++; }
                }
            }
            else
            {
                if (closed || c == '"') { throw new InvalidDataException("Malformed CSV quoting at record " + (records.Count + 1) + "."); }
                field.Append(c);
            }
        }
        if (quoted) { throw new InvalidDataException("Unclosed CSV quoted field."); }
        if (closed || field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); records.Add(row); }
        return records;
    }

    private static NORMParsedImport Csv(string text, int expectedYear)
    {
        List<List<string>> records = Records(text);
        if (records.Count < 8) { throw new InvalidDataException("Incomplete ActewAGL CSV."); }
        string[] labels = { "Company : ", "CY : ", "CY : ", "PY : ", "PY : " };
        for (int i = 0; i < labels.Length; i++)
        { if (records[i].Count != 2 || records[i][0] != labels[i]) { throw new InvalidDataException("Invalid ActewAGL preamble record " + (i + 1) + "."); } }
        Match company = Regex.Match(records[0][1], @"^(\d+) - ActewAGL Distribution Partnership FinStats$");
        Match cy = Regex.Match(records[1][1], @"^(\d{4})/(\d{2})$");
        Match py = Regex.Match(records[3][1], @"^(\d{4})/(\d{2})$");
        int start;
        if (!company.Success || !cy.Success || !py.Success || !Int32.TryParse(cy.Groups[1].Value, out start))
        { throw new InvalidDataException("Invalid ActewAGL company or financial-year metadata."); }
        int year = start + 1;
        if (cy.Groups[2].Value != (year % 100).ToString("00", Inv) ||
            py.Groups[1].Value != (start - 1).ToString(Inv) || py.Groups[2].Value != (start % 100).ToString("00", Inv) ||
            year != expectedYear || records[2][1] != "Jun" || records[4][1] != "Jun")
        { throw new InvalidDataException("ActewAGL year/month metadata conflicts with the selected financial year or supported June annual layout."); }
        if (records[5].Count != 1 || !String.IsNullOrWhiteSpace(records[5][0])) { throw new InvalidDataException("Invalid ActewAGL preamble spacer."); }
        List<string> header = records[6];
        if (header.Count != 4) { throw new InvalidDataException("ActewAGL requires four distinct mapped columns."); }
        int account = header.IndexOf("Account"), description = header.IndexOf("Account Desc");
        int current = header.IndexOf("Jun " + records[1][1]), prior = header.IndexOf("Jun " + records[3][1]);
        if (account < 0 || description < 0 || current < 0 || prior < 0 || new HashSet<string>(header).Count != 4)
        { throw new InvalidDataException("ActewAGL columns do not match the source reporting-year metadata."); }
        NORMParsedImport parsed = new NORMParsedImport { ProfileVersion = CsvProfile, FinancialYear = year,
            PeriodStart = 1, PeriodEnd = 12, CoverageStart = new DateTime(start, 7, 1), CoverageEnd = new DateTime(year, 6, 30) };
        HashSet<string> accounts = new HashSet<string>();
        for (int i = 7; i < records.Count; i++)
        {
            List<string> row = records[i];
            if (row.Count == 1 && String.IsNullOrWhiteSpace(row[0])) { continue; }
            if (row.Count != 4 || !Regex.IsMatch(row[account], @"^\d+$") || String.IsNullOrWhiteSpace(row[description]) || !accounts.Add(row[account]))
            { throw new InvalidDataException("Invalid, duplicate or incomplete ActewAGL detail at record " + (i + 1) + "."); }
            decimal balance = Amount(row[current], i + 1);
            Amount(row[prior], i + 1); // Validate the retained comparative token; do not import or net-test PY.
            Add(parsed, i + 1, company.Groups[1].Value, row[account], row[description], null, null, null, balance);
        }
        Finish(parsed);
        parsed.Warnings.Add("Current-year column only; prior-year column retained in original. Controls are calculated; no report control totals supplied.");
        return parsed;
    }

    private static NORMParsedImport Fixed(string text, int expectedYear)
    {
        NORMParsedImport parsed = new NORMParsedImport { ProfileVersion = FixedProfile, FinancialYear = 2016,
            PeriodStart = 1, PeriodEnd = 9, CoverageStart = new DateTime(2015, 7, 1), CoverageEnd = new DateTime(2016, 3, 31) };
        if (expectedYear != 2016) { throw new InvalidDataException("Icon Water MAR-16 report requires an FY2016 configuration."); }
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string company = null, companyName = null, ledger = null;
        int page = 0, pageCount = 0, stage = 0; bool footer = false;
        bool period = false, currency = false, ytd = false, range = false, ledgerSeen = false, companySeen = false;
        HashSet<string> accounts = new HashSet<string>(); decimal[] sums = new decimal[4];
        string rule = new string('-', 15) + " " + new string('-', 31) + " " + new string('-', 20) + " " + new string('-', 20) + " " + new string('-', 20) + " " + new string('-', 20);
        string controlRule = new string(' ', 48) + new string('-', 20) + " " + new string('-', 20) + " " + new string('-', 20) + " " + new string('-', 20);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (String.IsNullOrWhiteSpace(line)) { continue; }
            if (line.Trim() == "\f") { continue; }
            if (footer) { throw new InvalidDataException("Unexpected data after Icon Water control totals at line " + (i + 1) + "."); }
            if (line.StartsWith("Icon Water Limited Ledger", StringComparison.Ordinal))
            {
                if ((page > 0 && stage != 2) || !Regex.IsMatch(line, @"^Icon Water Limited Ledger\s+Trial Balance - Total Currency\s+Report Date:\s+\d{2}-[A-Z]{3}-\d{4} \d{2}:\d{2}$"))
                { throw new InvalidDataException("Invalid or incomplete Icon Water page heading."); }
                page++; stage = 0; period = currency = ytd = range = ledgerSeen = companySeen = false; continue;
            }
            Match m;
            if (page > 0 && stage == 0)
            {
                m = Regex.Match(line, @"^\s+Period:\s+MAR-16\s+Page:\s+(\d+)\s+of\s+(\d+)$");
                if (m.Success && !period)
                {
                    int n = Int32.Parse(m.Groups[1].Value, Inv), total = Int32.Parse(m.Groups[2].Value, Inv);
                    if (n != page || total < n || (pageCount != 0 && pageCount != total)) { throw new InvalidDataException("Inconsistent Icon Water page count."); }
                    pageCount = total; period = true; continue;
                }
                if (line.Trim() == "Currency:  AUD" && !currency) { currency = true; continue; }
                if (line.Trim() == "Balance Type:  Year to Date" && !ytd) { ytd = true; continue; }
                m = Regex.Match(line, @"^\s+Company Range:\s+(\d+) to (\d+)$");
                if (m.Success && !range)
                {
                    if (m.Groups[1].Value != m.Groups[2].Value || (company != null && company != m.Groups[1].Value)) { throw new InvalidDataException("Conflicting Icon Water company range."); }
                    company = m.Groups[1].Value; range = true; continue;
                }
                m = Regex.Match(line, @"^\s+Ledger:\s+(.+)$");
                if (m.Success && !ledgerSeen)
                {
                    if (m.Groups[1].Value != "Icon Water Limited Ledger" || (ledger != null && ledger != m.Groups[1].Value)) { throw new InvalidDataException("Conflicting Icon Water ledger."); }
                    ledger = m.Groups[1].Value; ledgerSeen = true; continue;
                }
                m = Regex.Match(line, @"^\s+Company:\s+(\d+) (.+)$");
                if (m.Success && !companySeen)
                {
                    if (company != m.Groups[1].Value || (companyName != null && companyName != m.Groups[2].Value)) { throw new InvalidDataException("Conflicting Icon Water company."); }
                    companyName = m.Groups[2].Value; companySeen = true; continue;
                }
                if (line == "Account         Description                        Beginning Balance               Debits              Credits       Ending Balance" && period && ytd && range && ledgerSeen && companySeen)
                { stage = 1; continue; }
            }
            else if (stage == 1 && line == rule) { stage = 2; continue; }
            else if (stage == 2 && line == controlRule) { stage = 3; continue; }
            else if (stage == 2 || stage == 3)
            {
                bool control = stage == 3;
                if (line.Length > 131 || line.Length < 112) { throw new InvalidDataException("Invalid Icon Water positional record at line " + (i + 1) + "."); }
                string padded = line.PadRight(131);
                if (padded[15] != ' ' || padded[47] != ' ' || padded[68] != ' ' || padded[89] != ' ' || padded[110] != ' ')
                { throw new InvalidDataException("Misaligned Icon Water record at line " + (i + 1) + "."); }
                decimal[] v = new decimal[4]; int[] offsets = { 48, 69, 90, 111 };
                for (int k = 0; k < 4; k++) { v[k] = Amount(padded.Substring(offsets[k], 20).Trim(), i + 1); }
                if (control)
                {
                    if (padded.Substring(0, 48).Trim().Length != 0 || page != pageCount || parsed.Rows.Count == 0) { throw new InvalidDataException("Invalid Icon Water final controls."); }
                    for (int k = 0; k < 4; k++) { if (v[k] != sums[k]) { throw new InvalidDataException("Icon Water detail disagrees with printed control column " + (k + 1) + "."); } }
                    footer = true; continue;
                }
                string account = padded.Substring(0, 15).Trim(), description = padded.Substring(16, 31).Trim();
                if (!Regex.IsMatch(account, @"^\d{5}$") || description.Length == 0 || !accounts.Add(account) || v[1] < 0 || v[2] < 0 || v[0] + v[1] - v[2] != v[3])
                { throw new InvalidDataException("Invalid, duplicate or unreconciled Icon Water detail at line " + (i + 1) + "."); }
                for (int k = 0; k < 4; k++) { sums[k] += v[k]; }
                // Canonical movement contract uses signed credits; original positive credit is retained in bytes.
                Add(parsed, i + 1, company, account, description, v[0], v[1], -v[2], v[3]);
                continue;
            }
            throw new InvalidDataException("Unsupported or malformed Icon Water record at line " + (i + 1) + ".");
        }
        if (!footer) { throw new InvalidDataException("Missing Icon Water final controls or incomplete pages."); }
        // The printed ledger name cannot fit the existing 20-character identity.
        // Company uniquely resolves its canonical optional ledger/stream in configuration.
        parsed.Warnings.Add("Source ledger: Icon Water Limited Ledger; canonical ledger/stream resolved by approved company configuration. Source credits are positive; stored movements use signed credits.");
        Finish(parsed); return parsed;
    }

    private static void Add(NORMParsedImport p, int record, string company, string account, string description,
        decimal? opening, decimal? debit, decimal? credit, decimal balance)
    {
        if (company.Length > 20 || account.Length > 30 || description.Length > 300)
        { throw new InvalidDataException("Source identifiers or description exceed storage limits at record " + record + "."); }
        p.Rows.Add(new NORMTrialBalanceRow { SourceRowNo = record, SourceCompanyCode = company,
            GlAccount = account, GlText = description, OpeningBalance = opening, DebitMovement = debit,
            CreditMovement = credit, AccumBalance = balance,
            RowHash = NORMCrypto.Sha256(record.ToString(Inv) + "|" + company + "|" + account + "|" + description + "|" +
                (opening.HasValue ? opening.Value.ToString("G29", Inv) : "") + "|" +
                (debit.HasValue ? debit.Value.ToString("G29", Inv) : "") + "|" +
                (credit.HasValue ? credit.Value.ToString("G29", Inv) : "") + "|" + balance.ToString("G29", Inv)) });
    }

    private static void Finish(NORMParsedImport parsed)
    {
        if (parsed.Rows.Count == 0) { throw new InvalidDataException("No trial balance detail records."); }
        decimal net = 0; foreach (NORMTrialBalanceRow row in parsed.Rows) { net += row.AccumBalance; }
        if (net != 0) { throw new InvalidDataException("Trial balance must sum to exactly zero AUD; actual " + net.ToString("G29", Inv) + "."); }
    }
}
