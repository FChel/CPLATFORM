using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// A delivery schedule line for the Tab 1 classification grid, returned by
    /// [prepayment].[Tab1_GetDeliverySchedule] (second result set). Maps closely to
    /// prepayment.PPMPoDeliveryLine plus a couple of joined display fields.
    /// </summary>
    public class PPMPoDeliveryLine
    {
        public long Id { get; set; }
        public long PurchaseOrderId { get; set; }
        public int LineNumber { get; set; }
        /// <summary>ACCT_ASSIGN_NUMBER — part of the real line identity.</summary>
        public int AcctAssignNumber { get; set; }
        public string Description { get; set; }
        public string ServiceNote { get; set; }
        public string GlAccount { get; set; }
        public string GlDescription { get; set; }
        public string WbsCostCentre { get; set; }
        public string WbsDescription { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? OpenQuantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? LineValue { get; set; }

        /// <summary>'Prepayment' | 'NotPrepayment' | 'Pending'.</summary>
        public string PrepaymentFlag { get; set; }
        public string FlagNote { get; set; }
    }
}
