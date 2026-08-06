using System.Collections.Generic;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    public interface IPPMGroupWorkflowRepository
    {
        PPMGroupWorkflowKpis                     GetKpis();
        IReadOnlyList<PPMGroupWorkflowStateRow>  GetWorkflow(string statusFilter, string groupName, string preparer);
        PPMGroupFilterOptions                    GetFilterOptions();
        IReadOnlyList<PPMGroupUser>              GetUsers();
        int                                   Reassign(string groupCode, int? preparerUserId, int? approverUserId, int modifiedBy);
        long                                  Escalate(string groupCode, string note, int userId);
        long                                  SendReminder(string groupCode, int userId);
    }
}
