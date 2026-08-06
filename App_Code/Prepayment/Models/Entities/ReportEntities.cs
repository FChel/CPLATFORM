using System;
using System.Collections.Generic;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Report_GetKpis — the 5 headline figures for the report period.</summary>
    public class PPMReportKpis
    {
        public string  Period              { get; set; }
        public decimal TotalRecognised     { get; set; }
        public decimal TotalAmortised      { get; set; }
        public decimal Outstanding         { get; set; }
        public int     TotalGroups         { get; set; }
        public int     GroupsWithBalance   { get; set; }
        public int     CompletedThisPeriod { get; set; }
    }

    /// <summary>Row shape for Report_GetGrid — one §3.7 report line per (group, GL).</summary>
    public class PPMReportGridRow
    {
        public long     DeliveryGroupId   { get; set; }
        public string   DeliveryGroupCode { get; set; }
        public string   GroupName         { get; set; }
        public string   GlAccount         { get; set; }
        public string   GlDescription     { get; set; }
        public string   Vendor            { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string   CapexOpex         { get; set; }
        public decimal  Recognised        { get; set; }
        public decimal  Amortised         { get; set; }
        public decimal  Outstanding       { get; set; }
        public decimal  PercentAmortised  { get; set; }
        public int      PeriodsLeft       { get; set; }
        public DateTime? EndDate          { get; set; }
        public string   Status            { get; set; }
    }

    /// <summary>Combined result of Report_GetDrilldown (three result sets), C#5-compatible container in place of a tuple.</summary>
    public class PPMReportDrilldown
    {
        public IReadOnlyList<PPMReportDrilldownPeriod> Periods { get; set; }
        public PPMReportBalanceMovement Movement { get; set; }
        public PPMReportDrilldownHeader Header { get; set; }
    }

    /// <summary>Report_GetDrilldown result set 1 — one amortisation schedule period row.</summary>
    public class PPMReportDrilldownPeriod
    {
        public int      PeriodNumber { get; set; }
        public string   PeriodLabel  { get; set; }
        public DateTime? PeriodDate  { get; set; }
        public decimal  Amount       { get; set; }
        public decimal  Cumulative   { get; set; }
        public string   Status       { get; set; }
        public bool     IsCurrent    { get; set; }
    }

    /// <summary>Report_GetDrilldown result set 2 — the balance-movement figures.</summary>
    public class PPMReportBalanceMovement
    {
        public decimal  Recognised      { get; set; }
        public decimal  AmortisedToDate { get; set; }
        public decimal  ThisPeriod      { get; set; }
        public int      PeriodsTotal    { get; set; }
        public int      PeriodsExported { get; set; }
        public DateTime? StartDate      { get; set; }
        public DateTime? EndDate        { get; set; }
    }

    /// <summary>Report_GetDrilldown result set 3 — the selected group header for the panel titles.</summary>
    public class PPMReportDrilldownHeader
    {
        public string DeliveryGroupCode { get; set; }
        public string GroupName         { get; set; }
        public string GlAccount         { get; set; }
        public string GlDescription     { get; set; }
        public long?  ScheduleId        { get; set; }
        public string Period            { get; set; }
    }

    /// <summary>Report_GetGroups — a delivery-group option for the "Delivery group" filter.</summary>
    public class PPMReportGroupOption
    {
        public long   Id                { get; set; }
        public string DeliveryGroupCode { get; set; }
        public string GroupName         { get; set; }
    }

    /// <summary>Report_GetGlAccounts — a GL option for the "Account type" filter.</summary>
    public class PPMReportGlOption
    {
        public long   Id            { get; set; }
        public string GlAccount     { get; set; }
        public string GlDescription { get; set; }
    }

    /// <summary>Report_GetPeriods — a month option for the "Period" filter.</summary>
    public class PPMReportPeriodOption
    {
        public string PeriodKey   { get; set; }
        public string PeriodLabel { get; set; }
    }
}
