using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;

public sealed class NORMSourceFileEvidence
{
    public int FileId;
    public string FileName, Hash, SourceType;
    public DateTime? CoverageStart, CoverageEnd;
    public bool? IsFullYear;
    public int? PeriodStart, PeriodEnd;
}

public sealed class NORMSourceVersion
{
    public NORMSourceScope Scope;
    public int ReleaseId, PredecessorImportId;
    public string ConfigurationVersion, SourceType, Profile, AcceptedBy, ReplacementEvidence;
    public bool IsCurrent;
    public readonly List<NORMSourceFileEvidence> Files = new List<NORMSourceFileEvidence>();
}

public sealed class NORMSourceAccount
{
    public string Account, Description;
    public int SourceRowNo;
    public long RowId;
    public decimal Balance;
}

public sealed class NORMSourceAccountChange
{
    public string Account, StructuralChange;
    public NORMSourceAccount Previous, Current;
    public decimal Difference;
}

public sealed class NORMSourceComparison
{
    public NORMSourceVersion Current, Previous;
    public readonly List<NORMSourceAccountChange> Structural = new List<NORMSourceAccountChange>();
    public readonly List<NORMSourceAccountChange> Financial = new List<NORMSourceAccountChange>();
    public readonly List<string> MetadataChanges = new List<string>();
    public decimal PreviousNet, CurrentNet, NetMovement, PositiveMovements, NegativeMovements;
}

/// <summary>Read-only accepted-original history and exact-population comparison.</summary>
public static class NORMSourceHistory
{
    private const string Accepted = "i.IsDeactivated=0 AND i.IsTestBreak=0 AND i.ParentImportId IS NULL AND i.StatusCode IN ('Imported','Calculated')";
    private const string Classified = "tb.IsDeactivated=0 AND tb.SourceCompanyCode IS NOT NULL AND tb.ReportingStream IN ('Departmental','Administered')";

    private static void RequirePrepare()
    { if (!NORMHelper.HasPrepareAccess()) { throw new UnauthorizedAccessException("Preparer access is required for source history."); } }

    public static DataTable ApprovedReleases()
    {
        RequirePrepare();
        DataTable all = NORMHelper.Query("SELECT ConfigurationReleaseId,FinancialYear,EntityCode,VersionCode FROM dbo.tblNORM_ConfigurationRelease " +
            "WHERE StatusCode='Approved' AND IsDeactivated=0 ORDER BY FinancialYear DESC,EntityCode,ConfigurationReleaseId");
        DataTable allowed = all.Clone();
        foreach (DataRow row in all.Rows) { if (NORMHelper.CanPrepareEntity(NORMHelper.Str(row,"EntityCode"))) { allowed.ImportRow(row); } }
        return allowed;
    }

    public static NORMSourceScope ScopeForImport(int importId, string company, string ledger, string stream, bool explicitScope)
    {
        RequirePrepare();
        if (importId <= 0) { throw new InvalidOperationException("Select an accepted source import."); }
        DataTable table = NORMHelper.Query("SELECT DISTINCT i.ImportId,i.EntityCode,i.FinancialYear,i.SourceFileName,i.ImportedUtc," +
            "tb.SourceCompanyCode,tb.SourceLedger,tb.ReportingStream FROM dbo.tblNORM_Import i " +
            "INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.ImportId=i.ImportId WHERE i.ImportId=@import AND " + Accepted + " AND " + Classified,
            NORMHelper.P("@import",importId));
        if (table.Rows.Count == 0) { throw new InvalidOperationException("No classified accepted original population was found for this import. Historical unknown identities are not inferred."); }
        NORMHelper.RequireEntityAccess(NORMHelper.Str(table.Rows[0],"EntityCode"));
        if (!explicitScope && table.Rows.Count != 1) { throw new InvalidOperationException("This import contains several populations. Select its company, optional ledger and stream explicitly."); }
        foreach (DataRow row in table.Rows)
        {
            if (!explicitScope || (Same(company,NORMHelper.Str(row,"SourceCompanyCode")) && Same(ledger,NORMHelper.Str(row,"SourceLedger")) && Same(stream,NORMHelper.Str(row,"ReportingStream"))))
            { return Scope(row); }
        }
        throw new InvalidOperationException("The selected population does not belong to this accepted import.");
    }

    public static IList<NORMSourceScope> CurrentPopulations(string entity, int year, string stream)
    {
        RequirePrepare(); NORMHelper.RequireEntityAccess(entity); ValidateStream(stream,true);
        string sql = "SELECT DISTINCT i.ImportId,i.EntityCode,i.FinancialYear,i.SourceFileName,i.ImportedUtc,tb.SourceCompanyCode,tb.SourceLedger,tb.ReportingStream " +
            "FROM dbo.tblNORM_Import i INNER JOIN dbo.tblNORM_TrialBalanceRow tb ON tb.ImportId=i.ImportId " +
            "WHERE i.EntityCode=@entity AND i.FinancialYear=@fy AND " + Accepted + " AND " + Classified +
            (String.IsNullOrEmpty(stream) ? "" : " AND tb.ReportingStream=@stream") + " ORDER BY i.ImportId DESC";
        List<OleDbParameter> args = new List<OleDbParameter> { NORMHelper.P("@entity",entity),NORMHelper.P("@fy",year) };
        if (!String.IsNullOrEmpty(stream)) { args.Add(NORMHelper.P("@stream",stream)); }
        DataTable table = NORMHelper.Query(sql,args.ToArray());
        List<NORMSourceScope> result = new List<NORMSourceScope>(); HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in table.Rows) { NORMSourceScope scope = Scope(row); if (seen.Add(Key(scope))) { result.Add(scope); } }
        return result;
    }

    public static IList<NORMSourceVersion> Versions(NORMSourceScope scope)
    {
        RequireScope(scope);
        List<OleDbParameter> args; string predicate = ScopePredicate(scope,out args);
        DataTable table = NORMHelper.Query("SELECT i.ImportId,i.EntityCode,i.FinancialYear,i.SourceFileName,i.ImportedUtc,i.ImportedBy,i.SourceType," +
            "i.ConfigurationReleaseId,c.VersionCode,f.ImportFileId,f.SourceFileName AS EvidenceFileName,f.SourceFileHash,f.SourceType AS FileSourceType," +
            "f.PeriodStart,f.PeriodEnd,f.CoverageStart,f.CoverageEnd,f.IsFullYear," +
            "(SELECT TOP 1 a.DetailText FROM dbo.tblNORM_AuditEvent a WHERE a.EntityType='Import' AND a.EntityId=CONVERT(VARCHAR(20),i.ImportId) " +
            "AND a.EventCode='IMPORT_SOURCE_PROFILE' ORDER BY a.AuditEventId DESC) AS ProfileEvidence," +
            "(SELECT TOP 1 a.DetailText FROM dbo.tblNORM_AuditEvent a WHERE a.EntityType='Import' AND a.EntityId=CONVERT(VARCHAR(20),i.ImportId) " +
            "AND a.EventCode='IMPORT_SOURCE_REPLACED' ORDER BY a.AuditEventId DESC) AS ReplacementEvidence " +
            "FROM dbo.tblNORM_Import i LEFT JOIN dbo.tblNORM_ConfigurationRelease c ON c.ConfigurationReleaseId=i.ConfigurationReleaseId " +
            "LEFT JOIN dbo.tblNORM_ImportFile f ON f.ImportId=i.ImportId WHERE " + Accepted + " AND " + predicate +
            " ORDER BY i.ImportId DESC,f.ImportFileId",args.ToArray());
        List<NORMSourceVersion> versions = new List<NORMSourceVersion>(); NORMSourceVersion current = null;
        foreach (DataRow row in table.Rows)
        {
            int id = NORMHelper.Int(row,"ImportId");
            if (current == null || current.Scope.ImportId != id)
            {
                current = new NORMSourceVersion { Scope = new NORMSourceScope { Entity=scope.Entity,FinancialYear=scope.FinancialYear,Company=scope.Company,Ledger=scope.Ledger,
                    ReportingStream=scope.ReportingStream,ImportId=id,SourceFileName=NORMHelper.Str(row,"SourceFileName"),ImportedUtc=(DateTime)row["ImportedUtc"] },
                    ReleaseId=NORMHelper.Int(row,"ConfigurationReleaseId"),ConfigurationVersion=NORMHelper.Str(row,"VersionCode"),AcceptedBy=NORMHelper.Str(row,"ImportedBy"),
                    SourceType=NORMHelper.Str(row,"SourceType"),Profile=Profile(NORMHelper.Str(row,"ProfileEvidence")),ReplacementEvidence=NORMHelper.Str(row,"ReplacementEvidence") };
                versions.Add(current);
            }
            if (!row.IsNull("ImportFileId"))
            { current.Files.Add(new NORMSourceFileEvidence { FileId=NORMHelper.Int(row,"ImportFileId"),FileName=NORMHelper.Str(row,"EvidenceFileName"),Hash=NORMHelper.Str(row,"SourceFileHash"),
                SourceType=NORMHelper.Str(row,"FileSourceType"),CoverageStart=row.IsNull("CoverageStart") ? (DateTime?)null : (DateTime)row["CoverageStart"],
                CoverageEnd=row.IsNull("CoverageEnd") ? (DateTime?)null : (DateTime)row["CoverageEnd"],IsFullYear=row.IsNull("IsFullYear") ? (bool?)null : Convert.ToBoolean(row["IsFullYear"]),
                PeriodStart=row.IsNull("PeriodStart") ? (int?)null : NORMHelper.Int(row,"PeriodStart"),PeriodEnd=row.IsNull("PeriodEnd") ? (int?)null : NORMHelper.Int(row,"PeriodEnd") }); }
        }
        for (int i=0;i<versions.Count;i++) { versions[i].IsCurrent=i==0; versions[i].PredecessorImportId=i+1<versions.Count ? versions[i+1].Scope.ImportId : 0; }
        return versions;
    }

    public static NORMSourceComparison Compare(NORMSourceScope scope, int importId)
    {
        IList<NORMSourceVersion> versions = Versions(scope); NORMSourceVersion selected = null, previous = null;
        foreach (NORMSourceVersion version in versions) { if (version.Scope.ImportId==importId) { selected=version; } }
        if (selected==null) { throw new InvalidOperationException("The selected version is not an accepted original of this population."); }
        foreach (NORMSourceVersion version in versions) { if (version.Scope.ImportId==selected.PredecessorImportId) { previous=version; } }
        NORMSourceComparison result = new NORMSourceComparison { Current=selected,Previous=previous };
        if (previous==null) { return result; }
        Dictionary<string,NORMSourceAccount> a=Accounts(scope,previous.Scope.ImportId), b=Accounts(scope,importId);
        SortedSet<string> keys = new SortedSet<string>(a.Keys,StringComparer.OrdinalIgnoreCase); keys.UnionWith(b.Keys);
        foreach (string account in keys)
        {
            NORMSourceAccount oldRow,newRow; a.TryGetValue(account,out oldRow); b.TryGetValue(account,out newRow);
            NORMSourceAccountChange change = new NORMSourceAccountChange { Account=account,Previous=oldRow,Current=newRow,
                StructuralChange=oldRow==null ? "Added account" : newRow==null ? "Removed account" : !String.Equals(oldRow.Description,newRow.Description,StringComparison.Ordinal) ? "Description changed" : null,
                Difference=(newRow==null ? 0m : newRow.Balance)-(oldRow==null ? 0m : oldRow.Balance) };
            if (change.StructuralChange!=null) { result.Structural.Add(change); }
            result.PreviousNet+=oldRow==null ? 0m : oldRow.Balance; result.CurrentNet+=newRow==null ? 0m : newRow.Balance;
            if (change.Difference!=0m) { result.Financial.Add(change); if(change.Difference>0m) { result.PositiveMovements+=change.Difference; } else { result.NegativeMovements+=change.Difference; } }
        }
        result.NetMovement=result.CurrentNet-result.PreviousNet;
        Change(result.MetadataChanges,"Configuration release",previous.ReleaseId.ToString(CultureInfo.InvariantCulture),selected.ReleaseId.ToString(CultureInfo.InvariantCulture));
        Change(result.MetadataChanges,"Source type",previous.SourceType,selected.SourceType); Change(result.MetadataChanges,"Source profile",previous.Profile,selected.Profile);
        Change(result.MetadataChanges,"Coverage and periods",Coverage(previous),Coverage(selected));
        return result;
    }

    private static Dictionary<string,NORMSourceAccount> Accounts(NORMSourceScope scope, int importId)
    {
        List<OleDbParameter> args; string predicate=ScopePredicate(scope,out args);
        args.Add(NORMHelper.P("@import",importId));
        DataTable table=NORMHelper.Query("SELECT tb.TbRowId,tb.SourceRowNo,tb.GlAccount,tb.GlText,tb.AccumBalance FROM dbo.tblNORM_TrialBalanceRow tb " +
            "INNER JOIN dbo.tblNORM_Import i ON i.ImportId=tb.ImportId WHERE " + Accepted + " AND " + predicate + " AND i.ImportId=@import AND " +
            "tb.IsDeactivated=0 AND tb.SourceCompanyCode=@company AND " + (scope.Ledger==null ? "tb.SourceLedger IS NULL" : "tb.SourceLedger=@ledger") +
            " AND tb.ReportingStream=@stream ORDER BY tb.SourceRowNo,tb.TbRowId",args.ToArray());
        Dictionary<string,NORMSourceAccount> result=new Dictionary<string,NORMSourceAccount>(StringComparer.OrdinalIgnoreCase);
        foreach(DataRow row in table.Rows)
        {
            string account=NORMHelper.Str(row,"GlAccount");
            if(String.IsNullOrWhiteSpace(account) || result.ContainsKey(account)) { throw new InvalidOperationException("Retained accounts are not unique within this population; comparison is ambiguous."); }
            result.Add(account,new NORMSourceAccount { Account=account,Description=NORMHelper.Str(row,"GlText"),Balance=NORMHelper.Dec(row,"AccumBalance"),SourceRowNo=NORMHelper.Int(row,"SourceRowNo"),RowId=NORMHelper.Long(row,"TbRowId") });
        }
        return result;
    }

    private static string ScopePredicate(NORMSourceScope scope,out List<OleDbParameter> args)
    {
        args=new List<OleDbParameter> { NORMHelper.P("@entity",scope.Entity),NORMHelper.P("@fy",scope.FinancialYear),NORMHelper.P("@company",scope.Company) };
        if(scope.Ledger!=null) { args.Add(NORMHelper.P("@ledger",scope.Ledger)); } args.Add(NORMHelper.P("@stream",scope.ReportingStream));
        return "i.EntityCode=@entity AND i.FinancialYear=@fy AND EXISTS (SELECT 1 FROM dbo.tblNORM_TrialBalanceRow s WHERE s.ImportId=i.ImportId " +
            "AND s.IsDeactivated=0 AND s.SourceCompanyCode=@company AND " + (scope.Ledger==null ? "s.SourceLedger IS NULL" : "s.SourceLedger=@ledger") + " AND s.ReportingStream=@stream)";
    }
    private static void RequireScope(NORMSourceScope scope)
    {
        RequirePrepare(); if(scope==null || String.IsNullOrWhiteSpace(scope.Company) || scope.Ledger=="") { throw new InvalidOperationException("Select a classified source population."); }
        ValidateStream(scope.ReportingStream,false); NORMHelper.RequireEntityAccess(scope.Entity);
    }
    private static void ValidateStream(string stream,bool all)
    { if(!(all && String.IsNullOrEmpty(stream)) && stream!="Departmental" && stream!="Administered") { throw new InvalidOperationException("Select Departmental, Administered or all reporting streams."); } }
    private static bool Same(string a,string b) { return String.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
    private static string Key(NORMSourceScope scope)
    { return Uri.EscapeDataString(scope.Entity)+"|"+scope.FinancialYear+"|"+Uri.EscapeDataString(scope.Company)+"|"+(scope.Ledger==null ? "N" : "V"+Uri.EscapeDataString(scope.Ledger))+"|"+scope.ReportingStream; }
    private static NORMSourceScope Scope(DataRow row)
    { return new NORMSourceScope { Entity=NORMHelper.Str(row,"EntityCode"),FinancialYear=NORMHelper.Int(row,"FinancialYear"),Company=NORMHelper.Str(row,"SourceCompanyCode"),Ledger=NORMHelper.Str(row,"SourceLedger"),ReportingStream=NORMHelper.Str(row,"ReportingStream"),ImportId=NORMHelper.Int(row,"ImportId"),SourceFileName=NORMHelper.Str(row,"SourceFileName"),ImportedUtc=(DateTime)row["ImportedUtc"] }; }
    private static string Profile(string evidence)
    {
        if(String.IsNullOrEmpty(evidence)) { return "Unknown (legacy evidence)"; }
        foreach(string part in evidence.Split(';')) { if(part.StartsWith("Profile=",StringComparison.Ordinal)) { return part.Substring(8); } }
        return "Unknown (legacy evidence)";
    }
    public static string Coverage(NORMSourceVersion version)
    {
        List<string> values=new List<string>();
        foreach(NORMSourceFileEvidence f in version.Files) { values.Add((f.CoverageStart.HasValue ? f.CoverageStart.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) : "Unknown")+" to "+
            (f.CoverageEnd.HasValue ? f.CoverageEnd.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) : "Unknown")+"; "+(f.IsFullYear.HasValue ? f.IsFullYear.Value ? "Full year" : "Partial year" : "Unknown coverage")+"; periods "+f.PeriodStart+"-"+f.PeriodEnd); }
        return values.Count==0 ? "Unknown (no retained file metadata)" : String.Join(" / ",values.ToArray());
    }
    private static void Change(List<string> changes,string label,string a,string b)
    { if(!String.Equals(a,b,StringComparison.Ordinal)) { changes.Add(label+": "+a+" -> "+b); } }
}
