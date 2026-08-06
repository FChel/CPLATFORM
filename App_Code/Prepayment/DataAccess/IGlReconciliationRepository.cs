using System.Collections.Generic;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    public interface IPPMGlReconciliationRepository
    {
        PPMReconKpis                        GetKpis(string period);
        IReadOnlyList<PPMReconGridRow>      GetGrid(string period, bool variancesOnly);
        IReadOnlyList<PPMReconPeriodOption> GetPeriods();
        IReadOnlyList<PPMReconUser>         GetUsers();
        PPMReconVarianceDetail GetVarianceDetail(long reconciliationId);
        long SaveExtract(string sourceFileName, string period, string balancesJson, int userId);
        int  Resolve(long reconciliationId, string action, string note, int? assignedToUserId, int userId);
    }
}
