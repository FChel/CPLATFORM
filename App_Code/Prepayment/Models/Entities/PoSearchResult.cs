using System;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// One row of the Tab 1 "Search Results — New Commitment Lines" grid, returned by
    /// [prepayment].[Tab1_SearchPurchaseOrders]. Joins PPMPurchaseOrder + Vendor + DeliveryGroup
    /// and aggregates the delivery-line prepayment flags.
    /// </summary>
    public class PPMPoSearchResult
    {
        public long PoId { get; set; }
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        public string Project { get; set; }
        /// <summary>WBS element (real feed); same value as Project, named for clarity.</summary>
        public string Wbs { get; set; }
        public string DeliveryGroup { get; set; }
        public string DeliveryGroupName { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string CapabilityManager { get; set; }
        public string DeliveryManager { get; set; }
        public string ManagerProgram { get; set; }
        public decimal PoValue { get; set; }
        public decimal? CurrentCommitment { get; set; }
        public decimal? TotalCommitment { get; set; }
        public string Currency { get; set; }
        public DateTime? PoDate { get; set; }
        public int LinesCount { get; set; }

        /// <summary>Count of lines on this PO still awaiting a decision (Pending flag).</summary>
        public int UnreviewedLines { get; set; }
        /// <summary>Count of lines flagged as Prepayment.</summary>
        public int FlaggedLines { get; set; }
    }
}
