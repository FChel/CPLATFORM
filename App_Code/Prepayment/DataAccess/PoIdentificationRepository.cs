using System.Collections.Generic;
using System.Data;
using Prepayment.Web.Models.Dtos;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// PPMDbHelper implementation of <see cref="IPPMPoIdentificationRepository"/>. Every call
    /// invokes a stored procedure in the [prepayment] schema (DB-first). No SQL strings live here.
    /// </summary>
    public class PPMPoIdentificationRepository : IPPMPoIdentificationRepository
    {
        public PPMPoIdentificationKpis GetKpis()
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.PoIdentification_GetKpis", MapKpis)
                   ?? new PPMPoIdentificationKpis();
        }

        public IReadOnlyList<PPMPoSearchResult> SearchPurchaseOrders(PPMPoSearchCriteria criteria)
        {
            string poNumber = criteria != null ? criteria.PoNumber : null;
            string vendorName = criteria != null ? criteria.VendorName : null;
            string projectCode = criteria != null ? criteria.ProjectCode : null;
            string deliveryGroupCode = criteria != null ? criteria.DeliveryGroupCode : null;

            return PPMDbHelper.Query(
                "prepayment.PoIdentification_SearchPurchaseOrders",
                MapSearchResult,
                NullIfEmpty(poNumber),
                NullIfEmpty(vendorName),
                NullIfEmpty(projectCode),
                NullIfEmpty(deliveryGroupCode));
        }

        public PPMDeliveryScheduleHeader GetDeliverySchedule(long poId, out IReadOnlyList<PPMPoDeliveryLine> lines)
        {
            PPMDeliveryScheduleHeader header = null;
            IReadOnlyList<PPMPoDeliveryLine> linesLocal = null;

            PPMDbHelper.QueryMultiple("prepayment.PoIdentification_GetDeliverySchedule", multi =>
            {
                header = multi.ReadSingleOrDefault(MapScheduleHeader);
                linesLocal = multi.Read(MapDeliveryLine);
            }, poId);

            lines = linesLocal;
            return header;
        }

        public IReadOnlyList<PPMExistingPrepaymentPoEntity> GetExistingPrepaymentPos(string vendorNames = null)
        {
            return PPMDbHelper.Query(
                "prepayment.PoIdentification_GetExistingPrepaymentPos",
                MapExistingPrepaymentPo,
                string.IsNullOrWhiteSpace(vendorNames) ? null : vendorNames);
        }

        public IReadOnlyList<PPMDeliveryGroupOption> GetDeliveryGroups()
        {
            return PPMDbHelper.Query("prepayment.PoIdentification_GetDeliveryGroups", MapDeliveryGroupOption);
        }

        public int UpdateLineFlag(PPMFlagLineRequest request, int userId)
        {
            // The proc SELECTs @@ROWCOUNT, so read that scalar (Execute would return -1
            // under SET NOCOUNT ON and hide the real affected-row count).
            return PPMDbHelper.ExecuteScalar<int>(
                "prepayment.PoIdentification_UpdateLineFlag",
                request.DeliveryLineId,
                request.PrepaymentFlag,
                NullIfEmpty(request.Note),
                userId);
        }

        public PPMConfirmResult ConfirmAndAdvance(long poId, int userId)
        {
            // The proc returns Status/Flagged/Pending/InvoicesLinked.
            return PPMDbHelper.QuerySingle(
                "prepayment.PoIdentification_ConfirmAndAdvance", MapConfirmResult, poId, userId);
        }

        private static string NullIfEmpty(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMPoIdentificationKpis MapKpis(IDataRecord r)
        {
            return new PPMPoIdentificationKpis
            {
            NewPosToday           = PPMRow.GetInt(r, "NewPosToday"),
            VendorCountToday      = PPMRow.GetInt(r, "VendorCountToday"),
            FlaggedAsPrepayment   = PPMRow.GetInt(r, "FlaggedAsPrepayment"),
            FlaggedVendorCount    = PPMRow.GetInt(r, "FlaggedVendorCount"),
            AwaitingReview        = PPMRow.GetInt(r, "AwaitingReview"),
            NotPrepayment         = PPMRow.GetInt(r, "NotPrepayment"),
            TotalCommitmentValue  = PPMRow.GetDecimal(r, "TotalCommitmentValue"),
            };
        }

private static PPMPoSearchResult MapSearchResult(IDataRecord r)
        {
            return new PPMPoSearchResult
            {
            PoId               = PPMRow.GetLong(r, "PoId"),
            PoNumber           = PPMRow.GetString(r, "PoNumber"),
            Vendor             = PPMRow.GetString(r, "Vendor"),
            Project            = PPMRow.GetString(r, "Project"),
            Wbs                = PPMRow.GetString(r, "Wbs"),
            DeliveryGroup      = PPMRow.GetString(r, "DeliveryGroup"),
            DeliveryGroupName  = PPMRow.GetString(r, "DeliveryGroupName"),
            CapexOpex          = PPMRow.GetString(r, "CapexOpex"),
            CapabilityManager  = PPMRow.GetString(r, "CapabilityManager"),
            DeliveryManager    = PPMRow.GetString(r, "DeliveryManager"),
            ManagerProgram     = PPMRow.GetString(r, "ManagerProgram"),
            PoValue            = PPMRow.GetDecimal(r, "PoValue"),
            CurrentCommitment  = PPMRow.GetDecimalN(r, "CurrentCommitment"),
            TotalCommitment    = PPMRow.GetDecimalN(r, "TotalCommitment"),
            Currency           = PPMRow.GetString(r, "Currency"),
            PoDate             = PPMRow.GetDateTimeN(r, "PoDate"),
            LinesCount         = PPMRow.GetInt(r, "LinesCount"),
            UnreviewedLines    = PPMRow.GetInt(r, "UnreviewedLines"),
            FlaggedLines       = PPMRow.GetInt(r, "FlaggedLines"),
            };
        }

private static PPMDeliveryScheduleHeader MapScheduleHeader(IDataRecord r)
        {
            return new PPMDeliveryScheduleHeader
            {
            PoId                        = PPMRow.GetLong(r, "PoId"),
            PoNumber                    = PPMRow.GetString(r, "PoNumber"),
            Vendor                      = PPMRow.GetString(r, "Vendor"),
            DeliveryGroup               = PPMRow.GetString(r, "DeliveryGroup"),
            Project                     = PPMRow.GetString(r, "Project"),
            CapexOpex                   = PPMRow.GetString(r, "CapexOpex"),
            CapabilityManager           = PPMRow.GetString(r, "CapabilityManager"),
            DeliveryManager             = PPMRow.GetString(r, "DeliveryManager"),
            Currency                    = PPMRow.GetString(r, "Currency"),
            TotalValue                  = PPMRow.GetDecimal(r, "TotalValue"),
            LineCount                   = PPMRow.GetInt(r, "LineCount"),
            LinesNeedingClassification  = PPMRow.GetInt(r, "LinesNeedingClassification"),
            };
        }

private static PPMPoDeliveryLine MapDeliveryLine(IDataRecord r)
        {
            return new PPMPoDeliveryLine
            {
            Id               = PPMRow.GetLong(r, "Id"),
            PurchaseOrderId  = PPMRow.GetLong(r, "PurchaseOrderId"),
            LineNumber       = PPMRow.GetInt(r, "LineNumber"),
            AcctAssignNumber = PPMRow.GetInt(r, "AcctAssignNumber"),
            Description      = PPMRow.GetString(r, "Description"),
            ServiceNote      = PPMRow.GetString(r, "ServiceNote"),
            GlAccount        = PPMRow.GetString(r, "GlAccount"),
            GlDescription    = PPMRow.GetString(r, "GlDescription"),
            WbsCostCentre    = PPMRow.GetString(r, "WbsCostCentre"),
            WbsDescription   = PPMRow.GetString(r, "WbsDescription"),
            CapexOpex        = PPMRow.GetString(r, "CapexOpex"),
            ScheduledDate    = PPMRow.GetDateTimeN(r, "ScheduledDate"),
            Quantity         = PPMRow.GetDecimalN(r, "Quantity"),
            OpenQuantity     = PPMRow.GetDecimalN(r, "OpenQuantity"),
            UnitPrice        = PPMRow.GetDecimalN(r, "UnitPrice"),
            LineValue        = PPMRow.GetDecimalN(r, "LineValue"),
            PrepaymentFlag   = PPMRow.GetString(r, "PrepaymentFlag"),
            FlagNote         = PPMRow.GetString(r, "FlagNote"),
            };
        }

private static PPMExistingPrepaymentPoEntity MapExistingPrepaymentPo(IDataRecord r)
        {
            return new PPMExistingPrepaymentPoEntity
            {
            PoId               = PPMRow.GetLong(r, "PoId"),
            PoNumber           = PPMRow.GetString(r, "PoNumber"),
            Vendor             = PPMRow.GetString(r, "Vendor"),
            DeliveryGroup      = PPMRow.GetString(r, "DeliveryGroup"),
            RecognisedAmount   = PPMRow.GetDecimal(r, "RecognisedAmount"),
            OutstandingBalance = PPMRow.GetDecimal(r, "OutstandingBalance"),
            AmortisationStatus = PPMRow.GetString(r, "AmortisationStatus"),
            };
        }

private static PPMDeliveryGroupOption MapDeliveryGroupOption(IDataRecord r)
        {
            return new PPMDeliveryGroupOption
            {
            Code = PPMRow.GetString(r, "Code"),
            Name = PPMRow.GetString(r, "Name"),
            };
        }

private static PPMConfirmResult MapConfirmResult(IDataRecord r)
        {
            return new PPMConfirmResult
            {
            Status         = PPMRow.GetString(r, "Status"),
            Flagged        = PPMRow.GetInt(r, "Flagged"),
            Pending        = PPMRow.GetInt(r, "Pending"),
            InvoicesLinked = PPMRow.GetInt(r, "InvoicesLinked"),
            };
        }
    }
}
