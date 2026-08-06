using System.Collections.Generic;
using Prepayment.Web.Models.Dtos;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// Data-access contract for Tab 1 (PO Identification). One method per stored procedure.
    /// Coding to the interface keeps the service layer testable and the SQL swappable.
    /// </summary>
    public interface IPPMPoIdentificationRepository
    {
        // ── Reads ──────────────────────────────────────────────────────────────
        PPMPoIdentificationKpis GetKpis();
        IReadOnlyList<PPMPoSearchResult> SearchPurchaseOrders(PPMPoSearchCriteria criteria);

        /// <summary>Header + lines for one PO's delivery schedule (two result sets).</summary>
        PPMDeliveryScheduleHeader GetDeliverySchedule(long poId, out IReadOnlyList<PPMPoDeliveryLine> lines);

        IReadOnlyList<PPMExistingPrepaymentPoEntity> GetExistingPrepaymentPos(string vendorNames = null);

        /// <summary>Distinct active delivery groups for the search dropdown.</summary>
        IReadOnlyList<PPMDeliveryGroupOption> GetDeliveryGroups();

        // ── Writes ─────────────────────────────────────────────────────────────
        /// <summary>Sets the prepayment flag (and optional note) on a single delivery line.</summary>
        int UpdateLineFlag(PPMFlagLineRequest request, int userId);

        /// <summary>
        /// Confirms a PO's classification: blocks if any line is still Pending, else pushes the
        /// flagged lines' invoices to Page 2 and advances the workflow stage.
        /// </summary>
        PPMConfirmResult ConfirmAndAdvance(long poId, int userId);
    }
}
