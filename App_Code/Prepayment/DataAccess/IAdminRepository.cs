using System.Collections.Generic;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    public interface IPPMAdminRepository
    {
        PPMAdminKpis                             GetKpis();
        IReadOnlyList<PPMAdminProcessTrackerRow> GetProcessTracker();
        IReadOnlyList<PPMAdminExceptionRow>      GetExceptions();
        PPMAdminPeriodSummary                    GetPeriodSummary();

        // §3.4 Admin actions — pickers + write paths.
        PPMAdminActionTargets GetActionTargets();

        int ForceAdvance(string poNumber, int userId);
        int ReassignApprover(string poNumber, int approverUserId, int userId);
        int ResolveException(long exceptionId, string note, int userId);
        int ReExportBatch(long batchId, int userId);
    }
}
