namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Admin_GetPeriodSummary — single row of period-to-date counts.</summary>
    public class PPMAdminPeriodSummary
    {
        public int    LinesFlagged         { get; set; }
        public int    InvoicesAssessed     { get; set; }
        public int    RecognitionJournals  { get; set; }
        public int    AmortisationJournals { get; set; }
        public int    JournalsExported     { get; set; }
        public string PeriodLabel          { get; set; }   // e.g. "Jun 2026"
    }
}
