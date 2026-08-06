namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// Row shape for Group_GetWorkflow — one row per delivery group (§3.5 columns):
    /// code, name, preparer, approver, live #POs / #Invoices / #Journals, and the derived
    /// current-stage / status keys.
    /// </summary>
    public class PPMGroupWorkflowStateRow
    {
        public string DeliveryGroupCode { get; set; }
        public string GroupName         { get; set; }
        public string PreparerName      { get; set; }
        public string ApproverName      { get; set; }

        public int PoCount      { get; set; }
        public int InvoiceCount { get; set; }
        public int JournalCount { get; set; }

        /// <summary>Derived pipeline stage key, e.g. "Amortising", "ExportReady", "Rejected".</summary>
        public string CurrentStageKey { get; set; }
        /// <summary>Derived health key: OnTrack | NeedsAttention | Blocked | FullyExported.</summary>
        public string StatusKey { get; set; }
    }
}
