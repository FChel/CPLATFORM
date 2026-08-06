using System;
using System.Collections.Generic;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>Combined result of Recon_GetVarianceDetail (four result sets), C#5-compatible container in place of a tuple.</summary>
    public class PPMReconVarianceDetail
    {
        public PPMReconGlExtractDetail Extract { get; set; }
        public IReadOnlyList<PPMReconInvoiceRecognised> Invoices { get; set; }
        public PPMReconFinhubDetail Finhub { get; set; }
        public PPMReconDetailHeader Header { get; set; }
    }

    /// <summary>Row shape for Recon_GetKpis — one row of GL-reconciliation headline figures.</summary>
    public class PPMReconKpis
    {
        public string    LastFileName       { get; set; }
        public string    LastLoadedBy       { get; set; }
        public DateTime? LastLoadedDate     { get; set; }
        public int?      GroupCount         { get; set; }
        public int?      AccountCount       { get; set; }
        public int       TotalGroups        { get; set; }
        public int       GroupsReconciled   { get; set; }
        public int       VariancesFound     { get; set; }
        public decimal   TotalSapBalance    { get; set; }
        public decimal   TotalFinhubBalance { get; set; }
        public string    Period             { get; set; }
    }

    /// <summary>Row shape for Recon_GetGrid — one reconciliation row per group + GL.</summary>
    public class PPMReconGridRow
    {
        public long    ReconciliationId  { get; set; }
        public string  DeliveryGroupCode { get; set; }
        public string  GroupName         { get; set; }
        public string  GlAccount         { get; set; }
        public string  GlDescription     { get; set; }
        public decimal SapBalance        { get; set; }
        public decimal PrepaymentBalance { get; set; }
        public decimal Variance          { get; set; }
        public string  Status            { get; set; }
        public string  Period            { get; set; }
    }

    /// <summary>Recon_GetVarianceDetail result set 1 — the SAP GL extract breakdown.</summary>
    public class PPMReconGlExtractDetail
    {
        public decimal   OpeningBalance { get; set; }
        public decimal   PeriodDebit    { get; set; }
        public decimal   PeriodCredit   { get; set; }
        public decimal   ClosingBalance { get; set; }
        public string    CompanyCode    { get; set; }
        public DateTime? ExtractDate    { get; set; }
    }

    /// <summary>Recon_GetVarianceDetail result set 2a — one recognised amount per invoice (§3.6).</summary>
    public class PPMReconInvoiceRecognised
    {
        public string  InvoiceNo  { get; set; }
        public decimal Recognised { get; set; }
    }

    /// <summary>Recon_GetVarianceDetail result set 2b — the live FINHUB (prepayment) totals.</summary>
    public class PPMReconFinhubDetail
    {
        public decimal Recognised  { get; set; }
        public decimal Amortised   { get; set; }
        public decimal Outstanding { get; set; }
        public decimal SapBalance  { get; set; }
        public decimal Variance    { get; set; }
    }

    /// <summary>Recon_GetVarianceDetail result set 3 — the selected row header for the panel titles.</summary>
    public class PPMReconDetailHeader
    {
        public long    Id                { get; set; }
        public string  Period            { get; set; }
        public string  Status            { get; set; }
        public decimal Variance          { get; set; }
        public string  InvestigationNote { get; set; }
        public string  ResolutionAction  { get; set; }
        public int?    AssignedToUserId  { get; set; }
        public string  AssignedTo        { get; set; }
        public string  DeliveryGroupCode { get; set; }
        public string  GroupName         { get; set; }
        public string  GlAccount         { get; set; }
        public string  GlDescription     { get; set; }
    }

    /// <summary>Recon_GetUsers — an app user offered in the "assign to" picker.</summary>
    public class PPMReconUser
    {
        public int    Id          { get; set; }
        public string DisplayName { get; set; }
    }

    /// <summary>Recon_GetPeriods — a period option for the reporting-period dropdown.</summary>
    public class PPMReconPeriodOption
    {
        public string PeriodKey   { get; set; }
        public string PeriodLabel { get; set; }
    }

    /// <summary>One parsed CSV line, sent to Recon_SaveExtract as a JSON array (§3.6 columns).</summary>
    public class PPMReconBalanceLine
    {
        public string  GroupCode      { get; set; }
        public string  GlAccount      { get; set; }
        public string  CompanyCode    { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal PeriodDebit    { get; set; }
        public decimal PeriodCredit   { get; set; }
        public decimal ClosingBalance { get; set; }
        public string  ExtractDate    { get; set; }
    }
}
