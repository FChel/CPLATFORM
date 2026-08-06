using System.Collections.Generic;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>Combined result of Admin_GetActionTargets (four result sets), C#5-compatible container in place of a tuple.</summary>
    public class PPMAdminActionTargets
    {
        public IReadOnlyList<PPMAdminStuckItem> Stuck { get; set; }
        public IReadOnlyList<PPMAdminApprover> Approvers { get; set; }
        public IReadOnlyList<PPMAdminFailedBatch> Batches { get; set; }
        public IReadOnlyList<PPMAdminOpenException> Exceptions { get; set; }
    }

    /// <summary>Admin_GetActionTargets set 1 — a PO with a non-exported journal (force-advance picker).</summary>
    public class PPMAdminStuckItem
    {
        public string PoNumber { get; set; }
        public string Label    { get; set; }
    }

    /// <summary>Admin_GetActionTargets set 2 — an approver/admin user (reassign picker).</summary>
    public class PPMAdminApprover
    {
        public int    Id          { get; set; }
        public string DisplayName { get; set; }
    }

    /// <summary>Admin_GetActionTargets set 3 — a failed export batch (re-export picker).</summary>
    public class PPMAdminFailedBatch
    {
        public long   Id       { get; set; }
        public string BatchRef { get; set; }
        public string Label    { get; set; }
    }

    /// <summary>Admin_GetActionTargets set 4 — a stored exception that can be cleared (with its real id).</summary>
    public class PPMAdminOpenException
    {
        public long   Id            { get; set; }
        public string Title         { get; set; }
        public string Detail        { get; set; }
        public string ExceptionType { get; set; }
        public string Status        { get; set; }
    }
}
