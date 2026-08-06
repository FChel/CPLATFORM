using System.Collections.Generic;
using System.Data;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>Data-access contract for the Journal Generation page (§3.3).</summary>
    public interface IPPMJournalRepository
    {
        PPMJournalKpis GetKpis();
        IReadOnlyList<PPMRecognitionJournalRow> GetRecognitionQueue(string vendorNames = null);
        IReadOnlyList<PPMAmortisationJournalRow> GetAmortisationQueue(string vendorNames = null);
        PPMJournalDetailHeader GetDetail(long journalId,
            out IReadOnlyList<PPMJournalEntryRow> entries, out IReadOnlyList<PPMJournalAuditRow> audit);
        long? ResolveJournalByPo(string poNumber);
        int Submit(long journalId, int userId);
        int Approve(long journalId, int userId, string comments);
        int Reject(long journalId, int userId, string comments);
        int Export(long? journalId, int userId);
        int ApproveAllReady(string journalType, int userId);
    }

    /// <summary>PPMDbHelper implementation. Every call invokes a prepayment.Journal_* proc.</summary>
    public class PPMJournalRepository : IPPMJournalRepository
    {
        public PPMJournalKpis GetKpis()
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.Journal_GetKpis", MapKpis)
                   ?? new PPMJournalKpis();
        }

        public IReadOnlyList<PPMRecognitionJournalRow> GetRecognitionQueue(string vendorNames = null)
        {
            return PPMDbHelper.Query(
                "prepayment.Journal_GetRecognitionQueue", MapRecognitionJournalRow, NullIfEmpty(vendorNames));
        }

        public IReadOnlyList<PPMAmortisationJournalRow> GetAmortisationQueue(string vendorNames = null)
        {
            return PPMDbHelper.Query(
                "prepayment.Journal_GetAmortisationQueue", MapAmortisationJournalRow, NullIfEmpty(vendorNames));
        }

        public PPMJournalDetailHeader GetDetail(long journalId,
            out IReadOnlyList<PPMJournalEntryRow> entries, out IReadOnlyList<PPMJournalAuditRow> audit)
        {
            PPMJournalDetailHeader header = null;
            IReadOnlyList<PPMJournalEntryRow> entriesLocal = null;
            IReadOnlyList<PPMJournalAuditRow> auditLocal = null;

            PPMDbHelper.QueryMultiple("prepayment.Journal_GetDetail", multi =>
            {
                header = multi.ReadSingleOrDefault(MapDetailHeader);
                entriesLocal = multi.Read(MapEntryRow);
                auditLocal = multi.Read(MapAuditRow);
            }, journalId);

            entries = entriesLocal;
            audit = auditLocal;
            return header;
        }

        public long? ResolveJournalByPo(string poNumber)
        {
            return PPMDbHelper.ExecuteScalar<long?>("prepayment.Journal_ResolveJournalByPo", poNumber);
        }

        public int Submit(long journalId, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Journal_Submit", journalId, userId);
        }

        public int Approve(long journalId, int userId, string comments)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Journal_Approve", journalId, userId, comments);
        }

        public int Reject(long journalId, int userId, string comments)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Journal_Reject", journalId, userId, comments);
        }

        public int Export(long? journalId, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Journal_Export", journalId, userId);
        }

        public int ApproveAllReady(string journalType, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Journal_ApproveAllReady", journalType, userId);
        }

        private static string NullIfEmpty(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMJournalKpis MapKpis(IDataRecord r)
        {
            return new PPMJournalKpis
            {
            RecognitionJournalsReady  = PPMRow.GetInt(r, "RecognitionJournalsReady"),
            AmortisationJournalsReady = PPMRow.GetInt(r, "AmortisationJournalsReady"),
            PendingApproval           = PPMRow.GetInt(r, "PendingApproval"),
            ApprovedExportReady       = PPMRow.GetInt(r, "ApprovedExportReady"),
            ExportedThisPeriod        = PPMRow.GetInt(r, "ExportedThisPeriod"),
            };
        }

private static PPMRecognitionJournalRow MapRecognitionJournalRow(IDataRecord r)
        {
            return new PPMRecognitionJournalRow
            {
            JournalId  = PPMRow.GetLong(r, "JournalId"),
            JournalRef = PPMRow.GetString(r, "JournalRef"),
            PoNumber   = PPMRow.GetString(r, "PoNumber"),
            InvoiceNo  = PPMRow.GetString(r, "InvoiceNo"),
            Vendor     = PPMRow.GetString(r, "Vendor"),
            CapexOpex  = PPMRow.GetString(r, "CapexOpex"),
            DrAccount  = PPMRow.GetString(r, "DrAccount"),
            CrAccount  = PPMRow.GetString(r, "CrAccount"),
            Amount     = PPMRow.GetDecimal(r, "Amount"),
            Period     = PPMRow.GetString(r, "Period"),
            Status     = PPMRow.GetString(r, "Status"),
            };
        }

private static PPMAmortisationJournalRow MapAmortisationJournalRow(IDataRecord r)
        {
            return new PPMAmortisationJournalRow
            {
            JournalId        = PPMRow.GetLong(r, "JournalId"),
            JournalRef       = PPMRow.GetString(r, "JournalRef"),
            PoNumber         = PPMRow.GetString(r, "PoNumber"),
            Vendor           = PPMRow.GetString(r, "Vendor"),
            CapexOpex        = PPMRow.GetString(r, "CapexOpex"),
            Period           = PPMRow.GetString(r, "Period"),
            DrAccount        = PPMRow.GetString(r, "DrAccount"),
            CrAccount        = PPMRow.GetString(r, "CrAccount"),
            PeriodAmount     = PPMRow.GetDecimal(r, "PeriodAmount"),
            RemainingBalance = PPMRow.GetDecimalN(r, "RemainingBalance"),
            Status           = PPMRow.GetString(r, "Status"),
            };
        }

private static PPMJournalDetailHeader MapDetailHeader(IDataRecord r)
        {
            return new PPMJournalDetailHeader
            {
            JournalId        = PPMRow.GetLong(r, "JournalId"),
            JournalRef       = PPMRow.GetString(r, "JournalRef"),
            JournalType      = PPMRow.GetString(r, "JournalType"),
            PoNumber         = PPMRow.GetString(r, "PoNumber"),
            LineNumber       = PPMRow.GetIntN(r, "LineNumber"),
            InvoiceNo        = PPMRow.GetString(r, "InvoiceNo"),
            Vendor           = PPMRow.GetString(r, "Vendor"),
            Amount           = PPMRow.GetDecimal(r, "Amount"),
            Period           = PPMRow.GetString(r, "Period"),
            Status           = PPMRow.GetString(r, "Status"),
            OriginalGl       = PPMRow.GetString(r, "OriginalGl"),
            CostObject       = PPMRow.GetString(r, "CostObject"),
            CompanyCode      = PPMRow.GetString(r, "CompanyCode"),
            PreparerName     = PPMRow.GetString(r, "PreparerName"),
            ApproverName     = PPMRow.GetString(r, "ApproverName"),
            RemainingBalance = PPMRow.GetDecimalN(r, "RemainingBalance"),
            ScheduleId       = PPMRow.GetLongN(r, "ScheduleId"),
            Periods          = PPMRow.GetIntN(r, "Periods"),
            };
        }

private static PPMJournalEntryRow MapEntryRow(IDataRecord r)
        {
            return new PPMJournalEntryRow
            {
            DebitCredit = PPMRow.GetString(r, "DebitCredit"),
            Account     = PPMRow.GetString(r, "Account"),
            Description = PPMRow.GetString(r, "Description"),
            CostObject  = PPMRow.GetString(r, "CostObject"),
            Amount      = PPMRow.GetDecimal(r, "Amount"),
            };
        }

private static PPMJournalAuditRow MapAuditRow(IDataRecord r)
        {
            return new PPMJournalAuditRow
            {
            Action     = PPMRow.GetString(r, "Action"),
            ActionBy   = PPMRow.GetString(r, "ActionBy"),
            Comments   = PPMRow.GetString(r, "Comments"),
            ActionDate = PPMRow.GetDateTimeN(r, "ActionDate"),
            };
        }
    }
}
