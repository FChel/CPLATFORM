namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Admin_GetKpis — one row returned by the stored proc.</summary>
    public class PPMAdminKpis
    {
        public decimal TotalRecognised  { get; set; }
        public decimal TotalAmortised   { get; set; }
        public int     AwaitingApproval { get; set; }
        public int     ExceptionsOpen   { get; set; }

        public decimal OutstandingBalance
        {
            get { return TotalRecognised - TotalAmortised; }
        }
    }
}
