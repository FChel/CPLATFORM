using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

public static class NORMNarrativeBlocks
{
    private static readonly HashSet<string> Formats = new HashSet<string>(StringComparer.Ordinal)
        { "p", "br", "strong", "em", "u", "h3", "h4", "ul", "ol", "li", "sup", "sub" };
    private static readonly HashSet<string> Discard = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "iframe", "svg", "math", "object", "template", "embed" };
    private static readonly string[] Types = { "Disclosure", "AccountingPolicy", "EntityOverview", "SectionIntroduction" };
    private const string Columns = "NarrativeBlockId,DestinationKind,DestinationKey,ContentType,Placement,SequenceNo," +
        "ContentHtml,StatusCode,SourceBlockId,CONVERT(binary(8),Revision) AS EditTokenBytes";
    private const string Insert = "INSERT dbo.tblNORM_NarrativeBlock " +
        "(ConfigurationReleaseId,DestinationKind,DestinationKey,ContentType,Placement,SequenceNo,ContentHtml,StatusCode,SourceBlockId,UpdatedBy) " +
        "VALUES (@release,@kind,@key,@type,@placement,@sequence,@html,@status,@source,@user)";

    public sealed class Block
    {
        // WebForms serialises the ID as text to preserve bigint precision in the editor.
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Key { get; set; }
        public string Type { get; set; }
        public string Placement { get; set; }
        public int Sequence { get; set; }
        public string Html { get; set; }
        public string Status { get; set; }
        public string EditToken { get; set; }
        public bool RolledForward { get; set; }
    }

    public sealed class Destination
    {
        public string Kind;
        public string Key;
        public string Label;
        public string Value { get { return Kind + "|" + Key; } }
    }

    public static bool Installed()
    {
        return Convert.ToInt32(NORMHelper.Scalar(
            "SELECT CASE WHEN OBJECT_ID('dbo.tblNORM_NarrativeBlock','U') IS NULL THEN 0 ELSE 1 END")) == 1;
    }

    public static List<Block> Load(int releaseId)
    {
        if (!Installed()) return new List<Block>();
        return Read(NORMHelper.Query("SELECT " + Columns + " FROM dbo.tblNORM_NarrativeBlock " +
            "WHERE ConfigurationReleaseId=@release ORDER BY SequenceNo,NarrativeBlockId", NORMHelper.P("@release", releaseId)));
    }

    private static List<Block> Read(DataTable table)
    {
        return table.AsEnumerable().Select(row => new Block {
            Id = NORMHelper.Long(row, "NarrativeBlockId").ToString(CultureInfo.InvariantCulture),
            Kind = NORMHelper.Str(row, "DestinationKind"), Key = NORMHelper.Str(row, "DestinationKey"),
            Type = NORMHelper.Str(row, "ContentType"), Placement = NORMHelper.Str(row, "Placement"),
            Sequence = NORMHelper.Int(row, "SequenceNo"), Html = SafeStoredHtml(NORMHelper.Str(row, "ContentHtml")),
            Status = NORMHelper.Str(row, "StatusCode"), RolledForward = !row.IsNull("SourceBlockId"),
            EditToken = BitConverter.ToString((byte[])row["EditTokenBytes"]).Replace("-", "")
        }).ToList();
    }

    private static string SafeStoredHtml(string html)
    {
        try { return Sanitize(html); }
        catch (InvalidOperationException) { return ""; }
    }

    public static string Sanitize(string html)
    {
        if (String.IsNullOrWhiteSpace(html) || html.Length > 20000)
            throw new InvalidOperationException("Enter narrative text of no more than 20,000 characters.");
        // The browser writes HTML void elements and non-breaking spaces. Parse a strict fragment
        // and rebuild it entirely from the allowlist, including when reading stored content.
        html = Regex.Replace(html.Replace("&nbsp;", "&#160;"), @"<br\s*>", "<br/>", RegexOptions.IgnoreCase);
        var document = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
        try
        {
            using (var reader = XmlReader.Create(new StringReader("<root>" + html + "</root>"),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 25000 }))
                document.Load(reader);
        }
        catch (XmlException)
        {
            throw new InvalidOperationException("The submitted format could not be read. Paste as plain text and reapply formatting.");
        }
        var output = new StringBuilder();
        foreach (XmlNode node in document.DocumentElement.ChildNodes) AppendSafe(node, output, 0);
        if (String.IsNullOrWhiteSpace(document.DocumentElement.InnerText) ||
            String.IsNullOrWhiteSpace(System.Web.HttpUtility.HtmlDecode(Regex.Replace(output.ToString(), "<[^>]*>", ""))) ||
            output.Length > 20000)
            throw new InvalidOperationException("Enter supported narrative text of no more than 20,000 characters.");
        return output.ToString();
    }

    private static void AppendSafe(XmlNode node, StringBuilder output, int depth)
    {
        if (depth > 40) throw new InvalidOperationException("Simplify the nested narrative formatting before saving.");
        if (node.NodeType == XmlNodeType.Text || node.NodeType == XmlNodeType.CDATA ||
            node.NodeType == XmlNodeType.SignificantWhitespace || node.NodeType == XmlNodeType.Whitespace)
        {
            output.Append(System.Security.SecurityElement.Escape(node.Value));
            return;
        }
        if (node.NodeType != XmlNodeType.Element || Discard.Contains(node.LocalName)) return;
        string name = node.Name.ToLowerInvariant();
        if (name == "div") name = "p";
        if (name == "b") name = "strong";
        if (name == "i") name = "em";
        if (name == "br") { output.Append("<br/>"); return; }
        bool allowed = Formats.Contains(name);
        if (allowed) output.Append('<').Append(name).Append('>');
        foreach (XmlNode child in node.ChildNodes) AppendSafe(child, output, depth + 1);
        if (allowed) output.Append("</").Append(name).Append('>');
    }

    public static List<Destination> Destinations(int releaseId)
    {
        return Destinations(releaseId, null, null);
    }

    private static List<Destination> Destinations(int releaseId, OleDbConnection connection, OleDbTransaction transaction)
    {
        const string rules = "SELECT DisclosureCode,DisclosureTitle,SectionCode,SectionTitle,NoteRef " +
            "FROM dbo.tblNORM_DisclosureRule WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 ORDER BY SortOrder";
        DataTable notes = connection == null ? NORMHelper.Query(rules, NORMHelper.P("@release", releaseId)) :
            NORMHelper.Query(connection, transaction, rules, NORMHelper.P("@release", releaseId));
        const string statements = "SELECT DISTINCT StatementCode FROM dbo.tblNORM_StatementLine " +
            "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0";
        DataTable configured = connection == null ? NORMHelper.Query(statements, NORMHelper.P("@release", releaseId)) :
            NORMHelper.Query(connection, transaction, statements, NORMHelper.P("@release", releaseId));
        var statementKeys = new HashSet<string>(configured.AsEnumerable().Select(row =>
            NORMHelper.Str(row, "StatementCode")), StringComparer.OrdinalIgnoreCase);
        if (statementKeys.Contains("SCFREF") || statementKeys.Contains("SCF")) statementKeys.Add("CASH");
        var values = new List<Destination> { new Destination { Kind = "Overview", Key = "OVERVIEW", Label = "Overview" } };
        values.AddRange(new[] {
            new Destination { Kind = "Statement", Key = "SOCI", Label = "Statement · Comprehensive income" },
            new Destination { Kind = "Statement", Key = "SOFP", Label = "Statement · Financial position" },
            new Destination { Kind = "Statement", Key = "SOCE", Label = "Statement · Changes in equity" },
            new Destination { Kind = "Statement", Key = "CASH", Label = "Statement · Cash flows" }
        }.Where(item => statementKeys.Contains(item.Key)));
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in notes.Rows)
        {
            string section = NORMHelper.Str(row, "SectionCode");
            if (!String.IsNullOrEmpty(section) && sections.Add(section))
                values.Add(new Destination { Kind = "Section", Key = section, Label = "Section " + section + " · " + NORMHelper.Str(row, "SectionTitle") });
            string reference = NORMHelper.Str(row, "NoteRef");
            if (String.IsNullOrWhiteSpace(reference)) continue;
            string key = NORMHelper.Str(row, "DisclosureCode");
            string label = reference + " · " + NORMHelper.Str(row, "DisclosureTitle");
            values.Add(new Destination { Kind = "Note", Key = key, Label = "Note " + label });
            values.Add(new Destination { Kind = "Schedule", Key = key, Label = "Table or schedule " + label });
        }
        return values;
    }

    public static DataTable PriorReleases(int releaseId)
    {
        return NORMHelper.Query(
            "SELECT source.ConfigurationReleaseId,source.FinancialYear,source.VersionCode FROM dbo.tblNORM_ConfigurationRelease target " +
            "JOIN dbo.tblNORM_ConfigurationRelease source ON source.EntityCode=target.EntityCode AND source.FinancialYear=target.FinancialYear-1 " +
            "WHERE target.ConfigurationReleaseId=@target AND source.IsDeactivated=0 AND source.StatusCode IN ('Draft','Approved') " +
            "AND EXISTS (SELECT 1 FROM dbo.tblNORM_NarrativeBlock b WHERE b.ConfigurationReleaseId=source.ConfigurationReleaseId) " +
            "ORDER BY source.ConfigurationReleaseId DESC", NORMHelper.P("@target", releaseId));
    }

    internal static void CopySuccessor(OleDbConnection connection, OleDbTransaction transaction, int source, int target, string user)
    {
        NORMHelper.Exec(connection, transaction,
            "INSERT dbo.tblNORM_NarrativeBlock (ConfigurationReleaseId,DestinationKind,DestinationKey,ContentType," +
            "Placement,SequenceNo,ContentHtml,StatusCode,SourceBlockId,UpdatedBy) " +
            "SELECT @target,DestinationKind,DestinationKey,ContentType,Placement,SequenceNo,ContentHtml," +
            "'Draft',SourceBlockId,@user FROM dbo.tblNORM_NarrativeBlock WITH (HOLDLOCK) WHERE ConfigurationReleaseId=@source",
            NORMHelper.P("@target", target), NORMHelper.P("@user", user), NORMHelper.P("@source", source));
    }

    public static void Save(int releaseId, long id, string editToken, string destination, string type,
        string placement, int sequence, string html, string status)
    {
        if (!Types.Contains(type) || (placement != "Before" && placement != "After") || sequence < 1 || sequence > 999 ||
            (status != "Draft" && status != "Prepared"))
            throw new InvalidOperationException("Choose a valid content type, placement, sequence from 1 to 999, and preparation status.");
        html = Sanitize(html);
        string user = NORMHelper.CurrentUserId();
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            LockRelease(connection, transaction, releaseId, true);
            Destination selected = Destinations(releaseId, connection, transaction).Find(item => item.Value == destination);
            if (selected == null) throw new InvalidOperationException("The selected destination is not configured for this release.");
            var block = new Block { Kind = selected.Kind, Key = selected.Key, Type = type, Placement = placement,
                Sequence = sequence, Html = html, Status = status };
            if (PositionOccupied(connection, transaction, releaseId, block, id))
                throw new InvalidOperationException("That position and sequence are already used at this destination.");
            if (id > 0)
            {
                if (!Regex.IsMatch(editToken ?? "", @"\A[0-9a-fA-F]{16}\z"))
                    throw new InvalidOperationException("Reload the narrative before editing.");
                int changed = NORMHelper.Exec(connection, transaction,
                    "UPDATE dbo.tblNORM_NarrativeBlock SET DestinationKind=@kind,DestinationKey=@key,ContentType=@type," +
                    "Placement=@placement,SequenceNo=@sequence,ContentHtml=@html,StatusCode=@status,UpdatedBy=@user," +
                    "UpdatedUtc=SYSUTCDATETIME() WHERE ConfigurationReleaseId=@release AND NarrativeBlockId=@id " +
                    "AND Revision=CONVERT(binary(8),CONVERT(varchar(16),@revision),2)",
                    NORMHelper.P("@kind", block.Kind), NORMHelper.P("@key", block.Key), NORMHelper.P("@type", block.Type),
                    NORMHelper.P("@placement", block.Placement), NORMHelper.P("@sequence", block.Sequence), NORMHelper.P("@html", html),
                    NORMHelper.P("@status", status), NORMHelper.P("@user", user), NORMHelper.P("@release", releaseId),
                    NORMHelper.P("@id", id), NORMHelper.P("@revision", editToken));
                if (changed != 1) throw new InvalidOperationException("This narrative changed since you opened it. Reload it before saving.");
            }
            else InsertBlock(connection, transaction, releaseId, block, null, user);
            Audit(connection, transaction, releaseId, "NARRATIVE_BLOCK_SAVED",
                selected.Value + ", " + placement + ", sequence " + sequence.ToString(CultureInfo.InvariantCulture), user);
            transaction.Commit();
        }
    }

    public static string RollForward(int targetId, int sourceId)
    {
        string user = NORMHelper.CurrentUserId();
        var conflicts = new List<string>();
        int added = 0, existing = 0;
        using (OleDbConnection connection = NORMHelper.OpenConnection())
        using (OleDbTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            DataRow target = LockRelease(connection, transaction, targetId, true);
            DataRow source = LockRelease(connection, transaction, sourceId, false);
            if (!String.Equals(NORMHelper.Str(source, "EntityCode"), NORMHelper.Str(target, "EntityCode"), StringComparison.OrdinalIgnoreCase) ||
                NORMHelper.Int(source, "FinancialYear") != NORMHelper.Int(target, "FinancialYear") - 1)
                throw new InvalidOperationException("Select a release from the preceding year for the same entity.");
            var allowed = new HashSet<string>(Destinations(targetId, connection, transaction).Select(item => item.Value), StringComparer.Ordinal);
            var blocks = Read(NORMHelper.Query(connection, transaction, "SELECT " + Columns +
                " FROM dbo.tblNORM_NarrativeBlock WHERE ConfigurationReleaseId=@release ORDER BY SequenceNo,NarrativeBlockId",
                NORMHelper.P("@release", sourceId)));
            foreach (Block block in blocks)
            {
                long sourceBlock = Int64.Parse(block.Id, CultureInfo.InvariantCulture);
                int copies = Convert.ToInt32(NORMHelper.Scalar(connection, transaction,
                    "SELECT COUNT(*) FROM dbo.tblNORM_NarrativeBlock WHERE ConfigurationReleaseId=@target AND SourceBlockId=@source",
                    NORMHelper.P("@target", targetId), NORMHelper.P("@source", sourceBlock)));
                if (copies > 0) { existing++; continue; }
                if (!allowed.Contains(block.Kind + "|" + block.Key) || String.IsNullOrEmpty(block.Html) ||
                    PositionOccupied(connection, transaction, targetId, block, 0))
                {
                    conflicts.Add(block.Kind + " " + block.Key + " (" + block.Placement + " " + block.Sequence + ")");
                    continue;
                }
                block.Status = "Draft";
                InsertBlock(connection, transaction, targetId, block, sourceBlock, user);
                added++;
            }
            if (added > 0)
                Audit(connection, transaction, targetId, "NARRATIVE_BLOCKS_ROLLED_FORWARD",
                    added.ToString(CultureInfo.InvariantCulture) + " blocks from release " + sourceId.ToString(CultureInfo.InvariantCulture), user);
            transaction.Commit();
        }
        return String.Format(CultureInfo.InvariantCulture, "{0} carried forward. {1} already copied.", added, existing) +
            (conflicts.Count == 0 ? "" : " Check destination, content or occupied sequence for " + String.Join(", ", conflicts) + ".");
    }

    private static DataRow LockRelease(OleDbConnection connection, OleDbTransaction transaction, int releaseId, bool draftOnly)
    {
        DataTable releases = NORMHelper.Query(connection, transaction,
            "SELECT EntityCode,FinancialYear,StatusCode FROM dbo.tblNORM_ConfigurationRelease WITH (UPDLOCK,HOLDLOCK) " +
            "WHERE ConfigurationReleaseId=@release AND IsDeactivated=0 AND StatusCode IN ('Draft','Approved')",
            NORMHelper.P("@release", releaseId));
        if (releases.Rows.Count != 1 || (draftOnly && NORMHelper.Str(releases.Rows[0], "StatusCode") != "Draft"))
            throw new InvalidOperationException("Select an eligible configuration draft before changing narrative content.");
        DataRow release = releases.Rows[0];
        NORMHelper.RequireEntityAccess(NORMHelper.Str(release, "EntityCode"), connection, transaction);
        return release;
    }

    private static bool PositionOccupied(OleDbConnection connection, OleDbTransaction transaction, int releaseId, Block block, long except)
    {
        return Convert.ToInt32(NORMHelper.Scalar(connection, transaction,
            "SELECT COUNT(*) FROM dbo.tblNORM_NarrativeBlock WHERE ConfigurationReleaseId=@release AND DestinationKind=@kind " +
            "AND DestinationKey=@key AND Placement=@placement AND SequenceNo=@sequence AND NarrativeBlockId<>@id",
            NORMHelper.P("@release", releaseId), NORMHelper.P("@kind", block.Kind), NORMHelper.P("@key", block.Key),
            NORMHelper.P("@placement", block.Placement), NORMHelper.P("@sequence", block.Sequence), NORMHelper.P("@id", except))) > 0;
    }

    private static void InsertBlock(OleDbConnection connection, OleDbTransaction transaction, int releaseId,
        Block block, long? source, string user)
    {
        NORMHelper.Exec(connection, transaction, Insert,
            NORMHelper.P("@release", releaseId), NORMHelper.P("@kind", block.Kind), NORMHelper.P("@key", block.Key),
            NORMHelper.P("@type", block.Type), NORMHelper.P("@placement", block.Placement), NORMHelper.P("@sequence", block.Sequence),
            NORMHelper.P("@html", block.Html), NORMHelper.P("@status", block.Status), NORMHelper.P("@source", source), NORMHelper.P("@user", user));
    }

    private static void Audit(OleDbConnection connection, OleDbTransaction transaction, int releaseId,
        string code, string detail, string user)
    {
        NORMHelper.Exec(connection, transaction,
            "INSERT dbo.tblNORM_AuditEvent(EventCode,EntityType,EntityId,DetailText,PerformedBy) " +
            "VALUES (@code,'NarrativeRelease',@release,@detail,@user)", NORMHelper.P("@code", code),
            NORMHelper.P("@release", releaseId.ToString(CultureInfo.InvariantCulture)), NORMHelper.P("@detail", detail), NORMHelper.P("@user", user));
    }
}
