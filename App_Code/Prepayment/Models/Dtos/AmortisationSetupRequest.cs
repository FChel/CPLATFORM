using System;

namespace Prepayment.Web.Models.Dtos
{
    /// <summary>
    /// Setup-panel inputs posted from the Amortisation Setup page (save draft / generate).
    /// Mirrors the classification + amortisation + account-assignment fields in §3.2.
    /// </summary>
    public class PPMAmortisationSetupRequest
    {
        public long InvoiceId { get; set; }
        public string AssetClassification { get; set; }   // Current | Non-current
        public string ExpenditureType { get; set; }       // Operational | Capital | Lease
        public string AmortisationType { get; set; }       // OneOff | Scheduled
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? Periods { get; set; }
        public string Frequency { get; set; }              // Monthly | Quarterly | Annual
        public long? PrepaymentGlId { get; set; }
        public string ExpenseGlAccount { get; set; }
        public string CostCentreWbs { get; set; }
        public string CompanyCode { get; set; }
    }

    /// <summary>Request body for the saveperiods action — edited period amounts from Tab 2.</summary>
    public class PPMSavePeriodsRequest
    {
        public long InvoiceId { get; set; }
        public PPMPeriodAmountItem[] Periods { get; set; }
    }

    public class PPMPeriodAmountItem
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
    }
}
