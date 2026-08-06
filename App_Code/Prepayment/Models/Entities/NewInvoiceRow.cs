using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>A row of the "New Invoices — vendor line item level" grid (§3.2).</summary>
    public class PPMNewInvoiceRow
    {
        public long InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public string PoNumber { get; set; }
        public int? LineNumber { get; set; }
        public string Vendor { get; set; }
        public string GlAccount { get; set; }
        /// <summary>Offset / expense (cash) GL.</summary>
        public string CashGlAccount { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public decimal Amount { get; set; }        // AUD
        public decimal? AmountDoc { get; set; }    // document/foreign amount
        public decimal? FxRate { get; set; }
        public string ForeignCurrency { get; set; }
        public string Description { get; set; }
        public string Flag { get; set; }          // Prepayment | UnderReview | NotPrepayment
        public string SetupStatus { get; set; }    // AmortisationNeeded | DraftInProgress | ...
    }
}
