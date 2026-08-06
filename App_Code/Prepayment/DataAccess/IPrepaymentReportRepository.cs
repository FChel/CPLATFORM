using System.Collections.Generic;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// Read-only data access for the Prepayment Report by Group tab (Tab 7 / §3.7). §3.7 is
    /// read-only — there are no write methods.
    /// </summary>
    public interface IPPMPrepaymentReportRepository
    {
        PPMReportKpis GetKpis(string period, long? groupId, long? glId, string status);

        IReadOnlyList<PPMReportGridRow> GetGrid(string period, long? groupId, long? glId, string status);

        PPMReportDrilldown GetDrilldown(long deliveryGroupId, string period, long? glId);

        IReadOnlyList<PPMReportGroupOption>  GetGroups();
        IReadOnlyList<PPMReportGlOption>     GetGlAccounts();
        IReadOnlyList<PPMReportPeriodOption> GetPeriods();
    }
}
