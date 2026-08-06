using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// DB-first mapping of prepayment.PPMPurchaseOrder (header). Used where a single PO row is
    /// needed (e.g. building the delivery-schedule header). Not every column is consumed by Tab 1.
    /// </summary>
    public class PPMPurchaseOrder
    {
        public long Id { get; set; }
        public string PoNumber { get; set; }
        public long? VendorId { get; set; }
        public long? DeliveryGroupId { get; set; }
        public string ProjectCode { get; set; }
        public decimal TotalValue { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime? PoDate { get; set; }
        public long? CompanyCodeId { get; set; }
        public int LinesCount { get; set; }
        public DateTime? SourceLoadDate { get; set; }
    }
}
