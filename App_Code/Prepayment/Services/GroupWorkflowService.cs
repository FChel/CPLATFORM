using System.Collections.Generic;
using System.Linq;
using Prepayment.Web.DataAccess;
using Prepayment.Web.Models;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.Services
{
    // ── View model returned by PPMGroupWorkflowService ──────────────────────────────────

    public class PPMGroupWorkflowControlViewModel
    {
        public List<PPMKpi>                Kpis         { get; set; }
        public List<PPMGroupWorkflowRow>   Rows         { get; set; }
        public PPMGroupWorkflowTotals      Footer       { get; set; }
        /// <summary>Echoes the active filters so the dropdowns keep their selection after a refresh.</summary>
        public string                   StatusFilter   { get; set; }
        public string                   GroupNameFilter { get; set; }
        public string                   PreparerFilter { get; set; }
        /// <summary>Data-driven dropdown options (only values present in live data).</summary>
        public List<PPMGroupFilterOption>  StatusOptions    { get; set; }
        public List<PPMGroupFilterOption>  GroupNameOptions { get; set; }
        public List<PPMGroupFilterOption>  PreparerOptions  { get; set; }
    }

    // ── Service ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the Group Workflow Control (Tab 5 / §3.5) view model. Each delivery group's row is a
    /// live roll-up of its POs / invoices / journals: code, name, preparer, approver, the count of
    /// POs / invoices / journals, and a derived current-stage + status badge. Filterable by status
    /// and a free-text search over group name / preparer.
    /// </summary>
    public class PPMGroupWorkflowService
    {
        private readonly IPPMGroupWorkflowRepository _repo;

        public PPMGroupWorkflowService() : this(new PPMGroupWorkflowRepository()) { }
        public PPMGroupWorkflowService(IPPMGroupWorkflowRepository repo) { _repo = repo; }

        public PPMGroupWorkflowControlViewModel Build(string statusFilter = null, string groupName = null, string preparer = null)
        {
            statusFilter = Normalise(statusFilter);
            groupName    = Normalise(groupName);
            preparer     = Normalise(preparer);

            var kpis    = _repo.GetKpis();
            var options = _repo.GetFilterOptions();
            var rows    = _repo.GetWorkflow(statusFilter, groupName, preparer).Select(MapRow).ToList();

            return new PPMGroupWorkflowControlViewModel
            {
                Kpis            = BuildKpis(kpis),
                Rows            = rows,
                StatusFilter    = statusFilter,
                GroupNameFilter = groupName,
                PreparerFilter  = preparer,
                StatusOptions    = options.Statuses,
                GroupNameOptions = options.GroupNames,
                PreparerOptions  = options.Preparers,
                Footer = new PPMGroupWorkflowTotals
                {
                    Total = kpis.TotalGroups != 0 ? kpis.TotalGroups : rows.Count,
                },
            };
        }

        /// <summary>Active app users for the reassign picker.</summary>
        public IReadOnlyList<PPMGroupUser> GetUsers()
        {
            return _repo.GetUsers();
        }

        /// <summary>Reassigns a group's preparer / approver. Returns rows updated (0 if not found).</summary>
        public int Reassign(string groupCode, int? preparerUserId, int? approverUserId, int modifiedBy)
        {
            if (string.IsNullOrWhiteSpace(groupCode))
                throw new System.ArgumentException("A delivery group code is required.");
            if ((preparerUserId ?? 0) <= 0 && (approverUserId ?? 0) <= 0)
                throw new System.ArgumentException("Pick a new preparer or approver.");
            return _repo.Reassign(groupCode.Trim(), preparerUserId, approverUserId, modifiedBy);
        }

        /// <summary>§3.5 "Escalate" — raise an Admin exception for the group (feeds Tab 4).</summary>
        public long Escalate(string groupCode, string note, int userId)
        {
            if (string.IsNullOrWhiteSpace(groupCode))
                throw new System.ArgumentException("A delivery group code is required.");
            return _repo.Escalate(groupCode.Trim(), note, userId);
        }

        /// <summary>§3.5 "Send reminder" — record a workflow reminder for the group preparer.</summary>
        public long SendReminder(string groupCode, int userId)
        {
            if (string.IsNullOrWhiteSpace(groupCode))
                throw new System.ArgumentException("A delivery group code is required.");
            return _repo.SendReminder(groupCode.Trim(), userId);
        }

        private static string Normalise(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        // ── KPI mapping ────────────────────────────────────────────────────────────────

        private static List<PPMKpi> BuildKpis(PPMGroupWorkflowKpis k)
        {
            return new List<PPMKpi>
            {
                new PPMKpi { Label = "Total groups",    Value = k.TotalGroups.ToString(),    Sub = "All delivery groups",  ValueClass = "" },
                new PPMKpi { Label = "On track",        Value = k.OnTrack.ToString(),        Sub = "No action required",   ValueClass = "green" },
                new PPMKpi { Label = "Needs attention", Value = k.NeedsAttention.ToString(), Sub = "Stalled or overdue",   ValueClass = k.NeedsAttention > 0 ? "amber" : "" },
                new PPMKpi { Label = "Blocked",         Value = k.Blocked.ToString(),        Sub = "Exceptions raised",    ValueClass = "",
                          ValueStyle = k.Blocked > 0 ? "color:var(--error)" : "" },
                new PPMKpi { Label = "Fully exported",  Value = k.FullyExported.ToString(),  Sub = "Period complete",      ValueClass = "blue" },
            };
        }

        // ── Row mapping ──────────────────────────────────────────────────────────────────

        private static PPMGroupWorkflowRow MapRow(PPMGroupWorkflowStateRow r)
        {
            var status = StatusBadge(r.StatusKey);
            bool blocked = r.StatusKey == "Blocked";

            return new PPMGroupWorkflowRow
            {
                Group        = r.DeliveryGroupCode,
                GroupName    = r.GroupName,
                Preparer     = r.PreparerName,
                Approver     = r.ApproverName,
                PoCount      = r.PoCount,
                InvoiceCount = r.InvoiceCount,
                JournalCount = r.JournalCount,
                Stage        = StageBadge(r.CurrentStageKey),
                Status       = status,
                ActionTarget = ActionTarget(r.CurrentStageKey, blocked),
                RowStyle     = blocked ? "background:#fdecea"
                             : (r.StatusKey == "FullyExported" ? "opacity:.65" : ""),
            };
        }

        // Stage / status badges — text + colour from the shared PPMGroupWorkflowLabels map, so the
        // grid and the CSV export can never disagree on wording.
        private static PPMBadge StageBadge(string key)
        {
            return new PPMBadge(PPMGroupWorkflowLabels.Stage(key), PPMGroupWorkflowLabels.StageCss(key));
        }

        private static PPMBadge StatusBadge(string key)
        {
            return new PPMBadge(PPMGroupWorkflowLabels.Status(key), PPMGroupWorkflowLabels.StatusCss(key));
        }

        // ── "View detail" drill-down target — which tab opens for this group ─────────────
        private static string ActionTarget(string stageKey, bool blocked)
        {
            if (blocked) return "admin";   // a blocked group is resolved from the Admin Control Tower
            switch (stageKey)
            {
                case "Exported":
                case "ExportReady":
                case "PendingApproval":
                case "Amortising":
                case "Recognised":   return "journals";       // journal activity → Journals tab
                case "SetupComplete":
                case "AmortSetup":
                case "InvoiceReview": return "amortisation";   // setup / invoice → Amortisation tab
                default:              return "po";             // flagging stage → PO Identification
            }
        }
    }
}
