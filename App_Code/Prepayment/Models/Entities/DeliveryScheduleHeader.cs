namespace Prepayment.Web.Models.Entities
{
    /// <summary>
    /// Header summary for a PO's delivery schedule (Tab 1 delivery-schedule card title row),
    /// returned by [prepayment].[Tab1_GetDeliverySchedule] (first result set).
    /// </summary>
    public class PPMDeliveryScheduleHeader
    {
        public long PoId { get; set; }
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        public string DeliveryGroup { get; set; }
        public string Project { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string CapabilityManager { get; set; }
        public string DeliveryManager { get; set; }
        public string Currency { get; set; }
        public decimal TotalValue { get; set; }
        public int LineCount { get; set; }
        public int LinesNeedingClassification { get; set; }

        /// <summary>
        /// A safe placeholder header (all blanks) for binding the hidden schedule placeholder when
        /// no schedule resolved, so its <%# ScheduleHeader.* %> expressions never dereference null.
        /// </summary>
        public static PPMDeliveryScheduleHeader Empty
        {
            get
            {
                return new PPMDeliveryScheduleHeader
                {
                    PoNumber = "", Vendor = "", DeliveryGroup = "", Project = "", Currency = ""
                };
            }
        }
    }
}
