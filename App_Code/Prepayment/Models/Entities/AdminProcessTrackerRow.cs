namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Admin_GetProcessTracker — one row per PO with prepayment activity.</summary>
    public class PPMAdminProcessTrackerRow
    {
        public string  PoNumber    { get; set; }
        public string  VendorName  { get; set; }
        public decimal TotalValue  { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string  CapexOpex   { get; set; }

        /// <summary>PO flag: 1 = Prepayment-flagged, 3 = NotPrepayment, 0 = Pending / none.</summary>
        public int PoFlagStage  { get; set; }
        /// <summary>Invoice: 1 = at least one linked invoice, 3 = none.</summary>
        public int InvoiceStage { get; set; }

        /// <summary>
        /// Stage indicator. 0 = not started, 1 = complete/positive,
        /// 2 = in progress / needs attention, 3 = negative (rejected / not-prepayment / missing).
        /// </summary>
        public int SetupStage        { get; set; }
        public int RecognitionStage  { get; set; }
        public int AmortisationStage { get; set; }
        public int ExportStage       { get; set; }
    }
}
