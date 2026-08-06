namespace Prepayment.Web.Models.Entities
{
    /// <summary>KPI counters for the Amortisation Setup page (§3.2). Raw DB numbers.</summary>
    public class PPMAmortisationSetupKpis
    {
        public int NewInvoicesToReview { get; set; }
        public int ExistingBalanceInvoices { get; set; }
        public int AmortisationSetupsPending { get; set; }
        public int SchedulesActive { get; set; }
        public decimal TotalPrepaymentBalance { get; set; }
    }
}
