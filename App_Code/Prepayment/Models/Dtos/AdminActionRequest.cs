namespace Prepayment.Web.Models.Dtos
{
    /// <summary>
    /// Request body for the Admin Control Tower action handler (Tab 4 / §3.4). Only the fields
    /// relevant to the chosen action are populated.
    /// </summary>
    public class PPMAdminActionRequest
    {
        /// <summary>PO number for force-advance / reassign-approver.</summary>
        public string PoNumber { get; set; }
        /// <summary>New approver user id for reassign-approver.</summary>
        public int    ApproverUserId { get; set; }
        /// <summary>Exception id for clear-exception.</summary>
        public long   ExceptionId { get; set; }
        /// <summary>Resolution note for clear-exception.</summary>
        public string Note { get; set; }
        /// <summary>Export batch id for re-export.</summary>
        public long   BatchId { get; set; }
    }
}
