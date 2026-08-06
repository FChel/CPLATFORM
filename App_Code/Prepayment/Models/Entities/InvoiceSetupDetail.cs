using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// Header detail for the selected invoice's setup panel (§3.2), including the original
    /// PO-line GL (to auto-suggest the expense GL) and any pre-existing schedule values.
    /// </summary>
    public class PPMInvoiceSetupDetail
    {
        public long InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public string PoNumber { get; set; }
        public int? LineNumber { get; set; }
        public string Vendor { get; set; }
        public decimal Amount { get; set; }        // AUD
        public decimal? AmountDoc { get; set; }    // document/foreign amount
        public decimal? FxRate { get; set; }
        public string ForeignCurrency { get; set; }
        public string Description { get; set; }
        public string OriginalGl { get; set; }
        public string CashGlAccount { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string ProfitCentre { get; set; }
        public string ProfitCentreDesc { get; set; }
        public string WbsCostCentre { get; set; }
        public string CompanyCode { get; set; }
        public long? DeliveryGroupId { get; set; }
        public string DeliveryGroup { get; set; }
        public string SetupStatus { get; set; }

        // Pre-existing schedule (null when not yet set up)
        public long? ScheduleId { get; set; }
        public string AssetClassification { get; set; }
        public string ExpenditureType { get; set; }
        public string AmortisationType { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? Periods { get; set; }
        public string Frequency { get; set; }
        public long? PrepaymentGlId { get; set; }
        public string ExpenseGlAccount { get; set; }
    }

    /// <summary>A prepayment GL account (514xxx) for the setup dropdown.</summary>
    public class PPMPrepaymentGlOption
    {
        public long PrepaymentGlId { get; set; }
        public string GlAccount { get; set; }
        public string GlDescription { get; set; }
        public string AssetClassification { get; set; }
        public string ExpenditureType { get; set; }
    }

    /// <summary>A generated amortisation-schedule period (preview table).</summary>
    public class PPMSchedulePeriodRow
    {
        public long PeriodId { get; set; }
        public int PeriodNumber { get; set; }
        public DateTime? PeriodDate { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
    }
}
