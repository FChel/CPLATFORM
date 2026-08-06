namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// One row of the Tab 1 "Existing Prepayment POs (previously flagged)" grid, returned by
    /// [prepayment].[Tab1_GetExistingPrepaymentPos]. Aggregates recognised / outstanding
    /// amounts from the amortisation schedule for POs that have already been flagged.
    /// </summary>
    public class PPMExistingPrepaymentPoEntity
    {
        public long PoId { get; set; }
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        public string DeliveryGroup { get; set; }
        public decimal RecognisedAmount { get; set; }
        public decimal OutstandingBalance { get; set; }
        /// <summary>e.g. 'Amortising' | 'Ready for export' | 'Pending approval'.</summary>
        public string AmortisationStatus { get; set; }
    }
}
