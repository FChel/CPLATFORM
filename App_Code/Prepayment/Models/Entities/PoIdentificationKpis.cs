namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// Tab 1 KPI counters, returned as a single row by [prepayment].[Tab1_GetKpis].
    /// Raw numbers from the DB; formatting (e.g. "$14.2m") happens in the service layer.
    /// </summary>
    public class PPMPoIdentificationKpis
    {
        public int NewPosToday { get; set; }
        public int VendorCountToday { get; set; }
        public int FlaggedAsPrepayment { get; set; }
        public int FlaggedVendorCount { get; set; }
        public int AwaitingReview { get; set; }
        public int NotPrepayment { get; set; }
        public decimal TotalCommitmentValue { get; set; }
    }
}
