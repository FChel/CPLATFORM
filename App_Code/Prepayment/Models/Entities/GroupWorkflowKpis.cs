namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Group_GetKpis — one row of delivery-group status buckets.</summary>
    public class PPMGroupWorkflowKpis
    {
        public int TotalGroups    { get; set; }
        public int OnTrack        { get; set; }
        public int NeedsAttention { get; set; }
        public int Blocked        { get; set; }
        public int FullyExported  { get; set; }
    }
}
