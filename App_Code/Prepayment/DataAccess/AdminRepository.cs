using System.Collections.Generic;
using System.Data;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    public class PPMAdminRepository : IPPMAdminRepository
    {
        public PPMAdminKpis GetKpis()
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.Admin_GetKpis", MapKpis)
                   ?? new PPMAdminKpis();
        }

        public IReadOnlyList<PPMAdminProcessTrackerRow> GetProcessTracker()
        {
            return PPMDbHelper.Query("prepayment.Admin_GetProcessTracker", MapProcessTrackerRow);
        }

        public IReadOnlyList<PPMAdminExceptionRow> GetExceptions()
        {
            return PPMDbHelper.Query("prepayment.Admin_GetExceptions", MapExceptionRow);
        }

        public PPMAdminPeriodSummary GetPeriodSummary()
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.Admin_GetPeriodSummary", MapPeriodSummary)
                   ?? new PPMAdminPeriodSummary { PeriodLabel = "—" };
        }

        // ── §3.4 Admin actions ────────────────────────────────────────────────────

        public PPMAdminActionTargets GetActionTargets()
        {
            IReadOnlyList<PPMAdminStuckItem> stuck = null;
            IReadOnlyList<PPMAdminApprover> approvers = null;
            IReadOnlyList<PPMAdminFailedBatch> batches = null;
            IReadOnlyList<PPMAdminOpenException> exceptions = null;

            PPMDbHelper.QueryMultiple("prepayment.Admin_GetActionTargets", multi =>
            {
                stuck = multi.Read(MapStuckItem);
                approvers = multi.Read(MapApprover);
                batches = multi.Read(MapFailedBatch);
                exceptions = multi.Read(MapOpenException);
            });

            return new PPMAdminActionTargets
            {
                Stuck = stuck,
                Approvers = approvers,
                Batches = batches,
                Exceptions = exceptions,
            };
        }

        public int ForceAdvance(string poNumber, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Admin_ForceAdvance", poNumber, userId);
        }

        public int ReassignApprover(string poNumber, int approverUserId, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Admin_ReassignApprover", poNumber, approverUserId, userId);
        }

        public int ResolveException(long exceptionId, string note, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Admin_ResolveException", exceptionId, note, userId);
        }

        public int ReExportBatch(long batchId, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>("prepayment.Admin_ReExportBatch", batchId, userId);
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMAdminKpis MapKpis(IDataRecord r)
        {
            return new PPMAdminKpis
            {
            TotalRecognised  = PPMRow.GetDecimal(r, "TotalRecognised"),
            TotalAmortised   = PPMRow.GetDecimal(r, "TotalAmortised"),
            AwaitingApproval = PPMRow.GetInt(r, "AwaitingApproval"),
            ExceptionsOpen   = PPMRow.GetInt(r, "ExceptionsOpen"),
            };
        }

private static PPMAdminProcessTrackerRow MapProcessTrackerRow(IDataRecord r)
        {
            return new PPMAdminProcessTrackerRow
            {
            PoNumber          = PPMRow.GetString(r, "PoNumber"),
            VendorName        = PPMRow.GetString(r, "VendorName"),
            TotalValue        = PPMRow.GetDecimal(r, "TotalValue"),
            CapexOpex         = PPMRow.GetString(r, "CapexOpex"),
            PoFlagStage       = PPMRow.GetInt(r, "PoFlagStage"),
            InvoiceStage      = PPMRow.GetInt(r, "InvoiceStage"),
            SetupStage        = PPMRow.GetInt(r, "SetupStage"),
            RecognitionStage  = PPMRow.GetInt(r, "RecognitionStage"),
            AmortisationStage = PPMRow.GetInt(r, "AmortisationStage"),
            ExportStage       = PPMRow.GetInt(r, "ExportStage"),
            };
        }

private static PPMAdminExceptionRow MapExceptionRow(IDataRecord r)
        {
            return new PPMAdminExceptionRow
            {
            Id            = PPMRow.GetLong(r, "Id"),
            Title         = PPMRow.GetString(r, "Title"),
            Detail        = PPMRow.GetString(r, "Detail"),
            ExceptionType = PPMRow.GetString(r, "ExceptionType"),
            Status        = PPMRow.GetString(r, "Status"),
            };
        }

private static PPMAdminPeriodSummary MapPeriodSummary(IDataRecord r)
        {
            return new PPMAdminPeriodSummary
            {
            LinesFlagged         = PPMRow.GetInt(r, "LinesFlagged"),
            InvoicesAssessed     = PPMRow.GetInt(r, "InvoicesAssessed"),
            RecognitionJournals  = PPMRow.GetInt(r, "RecognitionJournals"),
            AmortisationJournals = PPMRow.GetInt(r, "AmortisationJournals"),
            JournalsExported     = PPMRow.GetInt(r, "JournalsExported"),
            PeriodLabel          = PPMRow.GetString(r, "PeriodLabel"),
            };
        }

private static PPMAdminStuckItem MapStuckItem(IDataRecord r)
        {
            return new PPMAdminStuckItem
            {
            PoNumber = PPMRow.GetString(r, "PoNumber"),
            Label    = PPMRow.GetString(r, "Label"),
            };
        }

private static PPMAdminApprover MapApprover(IDataRecord r)
        {
            return new PPMAdminApprover
            {
            Id          = PPMRow.GetInt(r, "Id"),
            DisplayName = PPMRow.GetString(r, "DisplayName"),
            };
        }

private static PPMAdminFailedBatch MapFailedBatch(IDataRecord r)
        {
            return new PPMAdminFailedBatch
            {
            Id       = PPMRow.GetLong(r, "Id"),
            BatchRef = PPMRow.GetString(r, "BatchRef"),
            Label    = PPMRow.GetString(r, "Label"),
            };
        }

private static PPMAdminOpenException MapOpenException(IDataRecord r)
        {
            return new PPMAdminOpenException
            {
            Id            = PPMRow.GetLong(r, "Id"),
            Title         = PPMRow.GetString(r, "Title"),
            Detail        = PPMRow.GetString(r, "Detail"),
            ExceptionType = PPMRow.GetString(r, "ExceptionType"),
            Status        = PPMRow.GetString(r, "Status"),
            };
        }
    }
}
