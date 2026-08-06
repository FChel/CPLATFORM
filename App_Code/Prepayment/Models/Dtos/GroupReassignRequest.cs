using System.Collections.Generic;

namespace Prepayment.Web.Models.Dtos
{
    /// <summary>Body for the Group Workflow reassign action.</summary>
    public class PPMGroupReassignRequest
    {
        /// <summary>One or more delivery group codes to reassign (bulk reassign sends several).</summary>
        public List<string> Groups { get; set; }

        /// <summary>New preparer user id (0 / null = leave unchanged).</summary>
        public int? PreparerUserId { get; set; }

        /// <summary>New approver user id (0 / null = leave unchanged).</summary>
        public int? ApproverUserId { get; set; }
    }
}
