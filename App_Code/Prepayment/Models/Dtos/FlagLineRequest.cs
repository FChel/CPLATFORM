namespace Prepayment.Web.Models.Dtos
{
    /// <summary>
    /// Write-back payload from the Tab 1 UI when a user flags / notes a delivery line.
    /// Posted to PPMPoIdentificationHandler.ashx (toggle, save draft).
    /// </summary>
    public class PPMFlagLineRequest
    {
        public long DeliveryLineId { get; set; }
        /// <summary>'Prepayment' | 'NotPrepayment' | 'Pending'.</summary>
        public string PrepaymentFlag { get; set; }
        public string Note { get; set; }
    }
}
