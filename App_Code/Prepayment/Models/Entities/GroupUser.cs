namespace Prepayment.Web.Models.Entities
{
    /// <summary>Row shape for Group_GetUsers — an app user offered in the reassign picker.</summary>
    public class PPMGroupUser
    {
        public int    Id          { get; set; }
        public string DisplayName { get; set; }
        public string RoleName    { get; set; }
    }
}
