namespace Prepayment.Web.Models.Dtos
{
    /// <summary>
    /// Filter inputs for the Tab 1 commitment-data search. All optional; null/empty means
    /// "no filter on that field". Passed to [prepayment].[Tab1_SearchPurchaseOrders].
    /// </summary>
    public class PPMPoSearchCriteria
    {
        public string PoNumber { get; set; }
        public string VendorName { get; set; }
        public string ProjectCode { get; set; }
        public string DeliveryGroupCode { get; set; }
    }
}
