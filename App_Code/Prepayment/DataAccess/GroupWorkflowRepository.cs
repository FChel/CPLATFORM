using System.Collections.Generic;
using System.Data;
using System.Linq;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// Reads + writes the Group Workflow Control tab (Tab 5) via the prepayment.Group_* stored
    /// procedures. Read values are derived live from the Tab 1/2/3 transactional tables.
    /// </summary>
    public class PPMGroupWorkflowRepository : IPPMGroupWorkflowRepository
    {
        public PPMGroupWorkflowKpis GetKpis()
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.Group_GetKpis", MapKpis)
                   ?? new PPMGroupWorkflowKpis();
        }

        public IReadOnlyList<PPMGroupWorkflowStateRow> GetWorkflow(string statusFilter, string groupName, string preparer)
        {
            return PPMDbHelper.Query(
                "prepayment.Group_GetWorkflow", MapWorkflowStateRow, statusFilter, groupName, preparer);
        }

        public PPMGroupFilterOptions GetFilterOptions()
        {
            List<PPMGroupFilterOption> statuses = null;
            List<PPMGroupFilterOption> groupNames = null;
            List<PPMGroupFilterOption> preparers = null;

            PPMDbHelper.QueryMultiple("prepayment.Group_GetFilterOptions", multi =>
            {
                statuses = multi.Read(MapStatusOptionRow)
                                .Select(r => new PPMGroupFilterOption { Key = r.StatusKey, Label = r.StatusLabel }).ToList();
                // Group names & preparers are single-column result sets — the value IS the label
                // (exact match), so the mapper reads ordinal 0 directly rather than by column name.
                groupNames = multi.Read(MapSingleColumnValue)
                                  .Select(n => new PPMGroupFilterOption { Key = n, Label = n }).ToList();
                preparers = multi.Read(MapSingleColumnValue)
                                 .Select(n => new PPMGroupFilterOption { Key = n, Label = n }).ToList();
            });

            return new PPMGroupFilterOptions
            {
                Statuses   = statuses,
                GroupNames = groupNames,
                Preparers  = preparers,
            };
        }

        public IReadOnlyList<PPMGroupUser> GetUsers()
        {
            return PPMDbHelper.Query("prepayment.Group_GetUsers", MapUser);
        }

        public int Reassign(string groupCode, int? preparerUserId, int? approverUserId, int modifiedBy)
        {
            return PPMDbHelper.ExecuteScalar<int>(
                "prepayment.Group_Reassign", groupCode, preparerUserId, approverUserId, modifiedBy);
        }

        public long Escalate(string groupCode, string note, int userId)
        {
            return PPMDbHelper.ExecuteScalar<long>("prepayment.Group_Escalate", groupCode, note, userId);
        }

        public long SendReminder(string groupCode, int userId)
        {
            return PPMDbHelper.ExecuteScalar<long>("prepayment.Group_SendReminder", groupCode, userId);
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMGroupWorkflowKpis MapKpis(IDataRecord r)
        {
            return new PPMGroupWorkflowKpis
            {
            TotalGroups    = PPMRow.GetInt(r, "TotalGroups"),
            OnTrack        = PPMRow.GetInt(r, "OnTrack"),
            NeedsAttention = PPMRow.GetInt(r, "NeedsAttention"),
            Blocked        = PPMRow.GetInt(r, "Blocked"),
            FullyExported  = PPMRow.GetInt(r, "FullyExported"),
            };
        }

private static PPMGroupWorkflowStateRow MapWorkflowStateRow(IDataRecord r)
        {
            return new PPMGroupWorkflowStateRow
            {
            DeliveryGroupCode = PPMRow.GetString(r, "DeliveryGroupCode"),
            GroupName         = PPMRow.GetString(r, "GroupName"),
            PreparerName      = PPMRow.GetString(r, "PreparerName"),
            ApproverName      = PPMRow.GetString(r, "ApproverName"),
            PoCount           = PPMRow.GetInt(r, "PoCount"),
            InvoiceCount      = PPMRow.GetInt(r, "InvoiceCount"),
            JournalCount      = PPMRow.GetInt(r, "JournalCount"),
            CurrentStageKey   = PPMRow.GetString(r, "CurrentStageKey"),
            StatusKey         = PPMRow.GetString(r, "StatusKey"),
            };
        }

private static PPMGroupStatusOptionRow MapStatusOptionRow(IDataRecord r)
        {
            return new PPMGroupStatusOptionRow
            {
            StatusKey   = PPMRow.GetString(r, "StatusKey"),
            StatusLabel = PPMRow.GetString(r, "StatusLabel"),
            SortOrder   = PPMRow.GetInt(r, "SortOrder"),
            };
        }

        private static string MapSingleColumnValue(IDataRecord r)
        {
            return r.IsDBNull(0) ? null : r.GetValue(0).ToString();
        }

private static PPMGroupUser MapUser(IDataRecord r)
        {
            return new PPMGroupUser
            {
            Id          = PPMRow.GetInt(r, "Id"),
            DisplayName = PPMRow.GetString(r, "DisplayName"),
            RoleName    = PPMRow.GetString(r, "RoleName"),
            };
        }
    }
}
