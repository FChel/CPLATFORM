namespace Prepayment.Web.Models.Dtos
{
    /// <summary>Payload for journal workflow actions (submit / approve / reject / export).</summary>
    public class PPMJournalActionRequest
    {
        public long JournalId { get; set; }
        public string Comments { get; set; }
        public string ReasonCode { get; set; }   // required on reject
    }
}
