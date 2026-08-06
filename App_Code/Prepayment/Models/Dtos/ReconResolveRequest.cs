namespace Prepayment.Web.Models.Dtos
{
    /// <summary>Body for the GL reconciliation "Mark as explained" / "Raise adjustment" action.</summary>
    public class PPMReconResolveRequest
    {
        public long   ReconciliationId { get; set; }
        /// <summary>"MarkExplained" | "RaiseAdjustment".</summary>
        public string Action { get; set; }
        public string Note   { get; set; }
        /// <summary>Optional new assignee (0 / null = leave unchanged).</summary>
        public int?   AssignedToUserId { get; set; }
    }
}
