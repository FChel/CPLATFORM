using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>A row of the "Existing Prepayment Balance Invoices" grid (§3.2).</summary>
    public class PPMExistingBalanceInvoiceRow
    {
        public long InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public string PoNumber { get; set; }
        public int? LineNumber { get; set; }
        public string Vendor { get; set; }
        public string GlAccount { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public decimal Amount { get; set; }
        public decimal RecognisedAmount { get; set; }
        public decimal AmortisedToDate { get; set; }
        public int? Periods { get; set; }
        public string ScheduleStatus { get; set; }
    }
}
