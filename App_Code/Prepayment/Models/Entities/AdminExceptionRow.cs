namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Admin_GetExceptions — one row per open exception item.</summary>
    public class PPMAdminExceptionRow
    {
        public long   Id            { get; set; }
        public string Title         { get; set; }
        public string Detail        { get; set; }
        public string ExceptionType { get; set; }
        public string Status        { get; set; }
    }
}
