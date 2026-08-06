namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// Result of PoIdentification_ConfirmAndAdvance. Status is 'Ok', 'Blocked' (lines still
    /// Pending), or 'NothingFlagged' (no line flagged as a prepayment).
    /// </summary>
    public class PPMConfirmResult
    {
        public string Status { get; set; }
        public int Flagged { get; set; }
        public int Pending { get; set; }
        public int InvoicesLinked { get; set; }
    }
}
