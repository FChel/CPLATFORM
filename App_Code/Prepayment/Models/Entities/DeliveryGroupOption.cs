namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// A delivery-group option for the Tab 1 search dropdown, returned by
    /// [prepayment].[PoIdentification_GetDeliveryGroups]. Code is the real ERP group code
    /// (e.g. 'DIG', 'DPG - MPO'); Name is the full group name.
    /// </summary>
    public class PPMDeliveryGroupOption
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }
}
