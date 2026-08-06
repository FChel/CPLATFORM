namespace Prepayment.Web.Services
{
    /// <summary>
    /// Single source of truth for mapping the derived Group Workflow stage / status keys
    /// (emitted by fn_GroupWorkflowDerive) to their human labels. Used by both the on-screen
    /// badges (PPMGroupWorkflowService) and the CSV export (PPMGroupWorkflowExportHandler) so the
    /// two can never disagree on wording.
    /// </summary>
    internal static class PPMGroupWorkflowLabels
    {
        public static string Stage(string key)
        {
            switch (key)
            {
                case "Exported":        return "Completed";
                case "ExportReady":     return "Export ready";
                case "Rejected":        return "Rejected";
                case "PendingApproval": return "Pending approval";
                case "Amortising":      return "Amortising";
                case "Recognised":      return "Recognised";
                case "SetupComplete":   return "Setup complete";
                case "AmortSetup":      return "Amort. setup";
                case "InvoiceReview":   return "Invoice review";
                case "PoFlagging":      return "PO flagging";
                case "NotPrepayment":   return "Not prepayment";
                default:                return "Not started";
            }
        }

        public static string Status(string key)
        {
            switch (key)
            {
                case "Blocked":        return "Blocked";
                case "FullyExported":  return "Fully exported";
                case "NeedsAttention": return "Needs attention";
                default:               return "On track";
            }
        }

        /// <summary>CSS badge modifier for a stage key (s/b/e/w/a or none).</summary>
        public static string StageCss(string key)
        {
            switch (key)
            {
                case "Exported":        return "s";
                case "ExportReady":     return "b";
                case "Rejected":        return "e";
                case "PendingApproval": return "w";
                case "Amortising":      return "b";
                case "Recognised":      return "b";
                case "SetupComplete":   return "s";
                case "AmortSetup":      return "w";
                case "InvoiceReview":   return "b";
                case "PoFlagging":      return "a";
                case "NotPrepayment":   return "e";
                default:                return "";
            }
        }

        /// <summary>CSS badge modifier for a status key.</summary>
        public static string StatusCss(string key)
        {
            switch (key)
            {
                case "Blocked":        return "e";
                case "FullyExported":  return "b";
                case "NeedsAttention": return "w";
                default:               return "s";
            }
        }
    }
}
