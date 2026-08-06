using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>KPI counters for the Journal Generation page (§3.3).</summary>
    public class PPMJournalKpis
    {
        public int RecognitionJournalsReady { get; set; }
        public int AmortisationJournalsReady { get; set; }
        public int PendingApproval { get; set; }
        public int ApprovedExportReady { get; set; }
        public int ExportedThisPeriod { get; set; }
    }

    /// <summary>A row of the Part A recognition journal queue.</summary>
    public class PPMRecognitionJournalRow
    {
        public long JournalId { get; set; }
        public string JournalRef { get; set; }
        public string PoNumber { get; set; }
        public string InvoiceNo { get; set; }
        public string Vendor { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string DrAccount { get; set; }
        public string CrAccount { get; set; }
        public decimal Amount { get; set; }
        public string Period { get; set; }
        public string Status { get; set; }
    }

    /// <summary>A row of the Part B amortisation journal queue.</summary>
    public class PPMAmortisationJournalRow
    {
        public long JournalId { get; set; }
        public string JournalRef { get; set; }
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string Period { get; set; }
        public string DrAccount { get; set; }
        public string CrAccount { get; set; }
        public decimal PeriodAmount { get; set; }
        public decimal? RemainingBalance { get; set; }
        public string Status { get; set; }
    }

    /// <summary>Journal drill-down header (source PO + approval fields).</summary>
    public class PPMJournalDetailHeader
    {
        public long JournalId { get; set; }
        public string JournalRef { get; set; }
        public string JournalType { get; set; }
        public string PoNumber { get; set; }
        public int? LineNumber { get; set; }
        public string InvoiceNo { get; set; }
        public string Vendor { get; set; }
        public decimal Amount { get; set; }
        public string Period { get; set; }
        public string Status { get; set; }
        public string OriginalGl { get; set; }
        public string CostObject { get; set; }
        public string CompanyCode { get; set; }
        public string PreparerName { get; set; }
        public string ApproverName { get; set; }
        public decimal? RemainingBalance { get; set; }
        public long? ScheduleId { get; set; }
        public int? Periods { get; set; }
    }

    /// <summary>A Dr/Cr posting line in a journal.</summary>
    public class PPMJournalEntryRow
    {
        public string DebitCredit { get; set; }
        public string Account { get; set; }
        public string Description { get; set; }
        public string CostObject { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>An audit-trail entry for a journal.</summary>
    public class PPMJournalAuditRow
    {
        public string Action { get; set; }
        public string ActionBy { get; set; }
        public string Comments { get; set; }
        public DateTime? ActionDate { get; set; }
    }
}
