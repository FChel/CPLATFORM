using System.Collections.Generic;

namespace Prepayment.Web.Services.ExcelImport
{
    /// <summary>
    /// In-memory dataset parsed from the real Excel workbook (Prepayment Dashboard_2026.xlsx),
    /// ready for the full-replace load. Mirrors the master + transactional sets the seed builds.
    /// </summary>
    public class PPMImportDataset
    {
        public List<PPMGlRow> Gls { get; private set; }
        public List<PPMManagerRow> Managers { get; private set; }
        public List<PPMVendorRow> Vendors { get; private set; }
        public List<PPMGroupRow> Groups { get; private set; }
        public List<PPMUserRow> Users { get; private set; }
        public List<PPMPoRow> PurchaseOrders { get; private set; }
        public List<PPMLineRow> Lines { get; private set; }
        public List<PPMInvoiceRow> Invoices { get; private set; }
        public List<PPMGlBalanceRow> GlBalances { get; private set; }

        public PPMImportDataset()
        {
            Gls = new List<PPMGlRow>();
            Managers = new List<PPMManagerRow>();
            Vendors = new List<PPMVendorRow>();
            Groups = new List<PPMGroupRow>();
            Users = new List<PPMUserRow>();
            PurchaseOrders = new List<PPMPoRow>();
            Lines = new List<PPMLineRow>();
            Invoices = new List<PPMInvoiceRow>();
            GlBalances = new List<PPMGlBalanceRow>();
        }
    }

    public class PPMGlRow
    {
        public string GlAccount { get; set; }
        public string GlDescription { get; set; }
        public string AssetClassification { get; set; }
        public string ExpenditureType { get; set; }
        public string AasbReference { get; set; }
    }

    public class PPMManagerRow
    {
        public long Id { get; set; }
        public string ManagerDesc { get; set; }
        public string Program { get; set; }
    }

    public class PPMVendorRow
    {
        public string VendorCode { get; set; }
        public string VendorName { get; set; }
    }

    /// <summary>A delivery group + its resolved preparer/approver user ids + default GL.</summary>
    public class PPMGroupRow
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public int PreparerUserId { get; set; }
        public int ApproverUserId { get; set; }
    }

    public class PPMUserRow
    {
        public int Id { get; set; }
        public string WindowsAccount { get; set; }
        public string DisplayName { get; set; }
        public string RoleName { get; set; }
    }

    public class PPMPoRow
    {
        public string PoNumber { get; set; }
        public string VendorCode { get; set; }
        public string GroupCode { get; set; }
        public string Wbs { get; set; }
        public string CapexOpex { get; set; }
        public string GrIndicator { get; set; }
        public string IrIndicator { get; set; }
        public string ProcessControl { get; set; }
        public string SourceSystem { get; set; }
        public decimal? TotalCommitment { get; set; }
        public decimal? CurrentCommitment { get; set; }
        public long? CapabilityMgrId { get; set; }
        public long? DeliveryMgrId { get; set; }
        public string PoDate { get; set; }
    }

    public class PPMLineRow
    {
        public string PoNumber { get; set; }
        public int LineNumber { get; set; }
        public int AcctAssignNumber { get; set; }
        public string Description { get; set; }
        public string GlAccount { get; set; }
        public string GlDescription { get; set; }
        public string Wbs { get; set; }
        public string WbsDescription { get; set; }
        public string CapexOpex { get; set; }
        public string ScheduledDate { get; set; }
        public string Flag { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? OpenQuantity { get; set; }
        public decimal? LineValue { get; set; }
    }

    public class PPMInvoiceRow
    {
        public string InvoiceNo { get; set; }
        public string PoNumber { get; set; }
        public string GlAccount { get; set; }
        public string PrepaymentGlDesc { get; set; }
        public string CashGlAccount { get; set; }
        public string CashGlDescription { get; set; }
        public string ProfitCentre { get; set; }
        public string ProfitCentreDesc { get; set; }
        public string WbsElement { get; set; }
        public string WbsDescription { get; set; }
        public string CapexOpex { get; set; }
        public string InvoiceDate { get; set; }
        public string PaymentRunDate { get; set; }
        public string ForeignCurrency { get; set; }
        public string Description { get; set; }
        public string SetupStatus { get; set; }
        public string SourceSystem { get; set; }
        public string VendorCode { get; set; }
        public int? LineNumber { get; set; }
        public int? PostFiscalYear { get; set; }
        public int? PostFiscalPeriod { get; set; }
        public decimal Amount { get; set; }
        public decimal? AmountDoc { get; set; }
        public decimal? FxRate { get; set; }
    }

    public class PPMGlBalanceRow
    {
        public string GroupCode { get; set; }
        public string GlAccount { get; set; }
        public int FiscalYear { get; set; }
        public int FiscalPeriod { get; set; }
        public decimal Closing { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
