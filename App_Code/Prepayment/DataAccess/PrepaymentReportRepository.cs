using System.Collections.Generic;
using System.Data;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// Reads the Prepayment Report by Group tab (Tab 7) via the prepayment.Report_* stored
    /// procedures. Read-only (§3.7) — the report never writes back to other pages. Balances
    /// share the same basis as Tab 6 / Admin via the SQL fn_FinhubBalance/fn_ReportGroupGl
    /// functions, so the report always agrees with the rest of the app.
    /// </summary>
    public class PPMPrepaymentReportRepository : IPPMPrepaymentReportRepository
    {
        public PPMReportKpis GetKpis(string period, long? groupId, long? glId, string status)
        {
            return PPMDbHelper.QuerySingleOrDefault(
                "prepayment.Report_GetKpis", MapKpis, period, groupId, glId, status) ?? new PPMReportKpis();
        }

        public IReadOnlyList<PPMReportGridRow> GetGrid(string period, long? groupId, long? glId, string status)
        {
            return PPMDbHelper.Query(
                "prepayment.Report_GetGrid", MapGridRow, period, groupId, glId, status);
        }

        public PPMReportDrilldown GetDrilldown(long deliveryGroupId, string period, long? glId)
        {
            IReadOnlyList<PPMReportDrilldownPeriod> periods = null;
            PPMReportBalanceMovement movement = null;
            PPMReportDrilldownHeader header = null;

            PPMDbHelper.QueryMultiple("prepayment.Report_GetDrilldown", multi =>
            {
                periods = multi.Read(MapDrilldownPeriod);
                movement = multi.ReadSingleOrDefault(MapBalanceMovement);
                header = multi.ReadSingleOrDefault(MapDrilldownHeader);
            }, deliveryGroupId, period, glId);

            return new PPMReportDrilldown
            {
                Periods = periods,
                Movement = movement,
                Header = header,
            };
        }

        public IReadOnlyList<PPMReportGroupOption> GetGroups()
        {
            return PPMDbHelper.Query("prepayment.Report_GetGroups", MapGroupOption);
        }

        public IReadOnlyList<PPMReportGlOption> GetGlAccounts()
        {
            return PPMDbHelper.Query("prepayment.Report_GetGlAccounts", MapGlOption);
        }

        public IReadOnlyList<PPMReportPeriodOption> GetPeriods()
        {
            return PPMDbHelper.Query("prepayment.Report_GetPeriods", MapPeriodOption);
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMReportKpis MapKpis(IDataRecord r)
        {
            return new PPMReportKpis
            {
            Period              = PPMRow.GetString(r, "Period"),
            TotalRecognised     = PPMRow.GetDecimal(r, "TotalRecognised"),
            TotalAmortised      = PPMRow.GetDecimal(r, "TotalAmortised"),
            Outstanding         = PPMRow.GetDecimal(r, "Outstanding"),
            TotalGroups         = PPMRow.GetInt(r, "TotalGroups"),
            GroupsWithBalance   = PPMRow.GetInt(r, "GroupsWithBalance"),
            CompletedThisPeriod = PPMRow.GetInt(r, "CompletedThisPeriod"),
            };
        }

private static PPMReportGridRow MapGridRow(IDataRecord r)
        {
            return new PPMReportGridRow
            {
            DeliveryGroupId   = PPMRow.GetLong(r, "DeliveryGroupId"),
            DeliveryGroupCode = PPMRow.GetString(r, "DeliveryGroupCode"),
            GroupName         = PPMRow.GetString(r, "GroupName"),
            GlAccount         = PPMRow.GetString(r, "GlAccount"),
            GlDescription     = PPMRow.GetString(r, "GlDescription"),
            Vendor            = PPMRow.GetString(r, "Vendor"),
            CapexOpex         = PPMRow.GetString(r, "CapexOpex"),
            Recognised        = PPMRow.GetDecimal(r, "Recognised"),
            Amortised         = PPMRow.GetDecimal(r, "Amortised"),
            Outstanding       = PPMRow.GetDecimal(r, "Outstanding"),
            PercentAmortised  = PPMRow.GetDecimal(r, "PercentAmortised"),
            PeriodsLeft       = PPMRow.GetInt(r, "PeriodsLeft"),
            EndDate           = PPMRow.GetDateTimeN(r, "EndDate"),
            Status            = PPMRow.GetString(r, "Status"),
            };
        }

private static PPMReportDrilldownPeriod MapDrilldownPeriod(IDataRecord r)
        {
            return new PPMReportDrilldownPeriod
            {
            PeriodNumber = PPMRow.GetInt(r, "PeriodNumber"),
            PeriodLabel  = PPMRow.GetString(r, "PeriodLabel"),
            PeriodDate   = PPMRow.GetDateTimeN(r, "PeriodDate"),
            Amount       = PPMRow.GetDecimal(r, "Amount"),
            Cumulative   = PPMRow.GetDecimal(r, "Cumulative"),
            Status       = PPMRow.GetString(r, "Status"),
            IsCurrent    = PPMRow.GetBool(r, "IsCurrent"),
            };
        }

private static PPMReportBalanceMovement MapBalanceMovement(IDataRecord r)
        {
            return new PPMReportBalanceMovement
            {
            Recognised      = PPMRow.GetDecimal(r, "Recognised"),
            AmortisedToDate = PPMRow.GetDecimal(r, "AmortisedToDate"),
            ThisPeriod      = PPMRow.GetDecimal(r, "ThisPeriod"),
            PeriodsTotal    = PPMRow.GetInt(r, "PeriodsTotal"),
            PeriodsExported = PPMRow.GetInt(r, "PeriodsExported"),
            StartDate       = PPMRow.GetDateTimeN(r, "StartDate"),
            EndDate         = PPMRow.GetDateTimeN(r, "EndDate"),
            };
        }

private static PPMReportDrilldownHeader MapDrilldownHeader(IDataRecord r)
        {
            return new PPMReportDrilldownHeader
            {
            DeliveryGroupCode = PPMRow.GetString(r, "DeliveryGroupCode"),
            GroupName         = PPMRow.GetString(r, "GroupName"),
            GlAccount         = PPMRow.GetString(r, "GlAccount"),
            GlDescription     = PPMRow.GetString(r, "GlDescription"),
            ScheduleId        = PPMRow.GetLongN(r, "ScheduleId"),
            Period            = PPMRow.GetString(r, "Period"),
            };
        }

private static PPMReportGroupOption MapGroupOption(IDataRecord r)
        {
            return new PPMReportGroupOption
            {
            Id                = PPMRow.GetLong(r, "Id"),
            DeliveryGroupCode = PPMRow.GetString(r, "DeliveryGroupCode"),
            GroupName         = PPMRow.GetString(r, "GroupName"),
            };
        }

private static PPMReportGlOption MapGlOption(IDataRecord r)
        {
            return new PPMReportGlOption
            {
            Id            = PPMRow.GetLong(r, "Id"),
            GlAccount     = PPMRow.GetString(r, "GlAccount"),
            GlDescription = PPMRow.GetString(r, "GlDescription"),
            };
        }

private static PPMReportPeriodOption MapPeriodOption(IDataRecord r)
        {
            return new PPMReportPeriodOption
            {
            PeriodKey   = PPMRow.GetString(r, "PeriodKey"),
            PeriodLabel = PPMRow.GetString(r, "PeriodLabel"),
            };
        }
    }
}
