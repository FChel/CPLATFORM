using System.Collections.Generic;
using System.Data;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>
    /// Reads + writes the GL Balance Reconciliation tab (Tab 6) via the prepayment.Recon_*
    /// stored procedures. The SAP side comes from the uploaded extract; the FINHUB side is
    /// derived live from Tab 2/3.
    /// </summary>
    public class PPMGlReconciliationRepository : IPPMGlReconciliationRepository
    {
        public PPMReconKpis GetKpis(string period)
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.Recon_GetKpis", MapKpis, period)
                   ?? new PPMReconKpis();
        }

        public IReadOnlyList<PPMReconGridRow> GetGrid(string period, bool variancesOnly)
        {
            return PPMDbHelper.Query("prepayment.Recon_GetGrid", MapGridRow, period, variancesOnly);
        }

        public IReadOnlyList<PPMReconPeriodOption> GetPeriods()
        {
            return PPMDbHelper.Query("prepayment.Recon_GetPeriods", MapPeriodOption);
        }

        public IReadOnlyList<PPMReconUser> GetUsers()
        {
            return PPMDbHelper.Query("prepayment.Recon_GetUsers", MapUser);
        }

        public PPMReconVarianceDetail GetVarianceDetail(long reconciliationId)
        {
            PPMReconGlExtractDetail extract = null;
            IReadOnlyList<PPMReconInvoiceRecognised> invoices = null;
            PPMReconFinhubDetail finhub = null;
            PPMReconDetailHeader header = null;

            PPMDbHelper.QueryMultiple("prepayment.Recon_GetVarianceDetail", multi =>
            {
                extract = multi.ReadSingleOrDefault(MapGlExtractDetail);
                invoices = multi.Read(MapInvoiceRecognised);
                finhub = multi.ReadSingleOrDefault(MapFinhubDetail);
                header = multi.ReadSingleOrDefault(MapDetailHeader);
            }, reconciliationId);

            return new PPMReconVarianceDetail
            {
                Extract = extract,
                Invoices = invoices,
                Finhub = finhub,
                Header = header,
            };
        }

        public long SaveExtract(string sourceFileName, string period, string balancesJson, int userId)
        {
            return PPMDbHelper.ExecuteWithBigIntOutput(
                "prepayment.Recon_SaveExtract", sourceFileName, period, balancesJson, userId);
        }

        public int Resolve(long reconciliationId, string action, string note, int? assignedToUserId, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>(
                "prepayment.Recon_Resolve", reconciliationId, action, note, assignedToUserId, userId);
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMReconKpis MapKpis(IDataRecord r)
        {
            return new PPMReconKpis
            {
            LastFileName       = PPMRow.GetString(r, "LastFileName"),
            LastLoadedBy       = PPMRow.GetString(r, "LastLoadedBy"),
            LastLoadedDate     = PPMRow.GetDateTimeN(r, "LastLoadedDate"),
            GroupCount         = PPMRow.GetIntN(r, "GroupCount"),
            AccountCount       = PPMRow.GetIntN(r, "AccountCount"),
            TotalGroups        = PPMRow.GetInt(r, "TotalGroups"),
            GroupsReconciled   = PPMRow.GetInt(r, "GroupsReconciled"),
            VariancesFound     = PPMRow.GetInt(r, "VariancesFound"),
            TotalSapBalance    = PPMRow.GetDecimal(r, "TotalSapBalance"),
            TotalFinhubBalance = PPMRow.GetDecimal(r, "TotalFinhubBalance"),
            Period             = PPMRow.GetString(r, "Period"),
            };
        }

private static PPMReconGridRow MapGridRow(IDataRecord r)
        {
            return new PPMReconGridRow
            {
            ReconciliationId  = PPMRow.GetLong(r, "ReconciliationId"),
            DeliveryGroupCode = PPMRow.GetString(r, "DeliveryGroupCode"),
            GroupName         = PPMRow.GetString(r, "GroupName"),
            GlAccount         = PPMRow.GetString(r, "GlAccount"),
            GlDescription     = PPMRow.GetString(r, "GlDescription"),
            SapBalance        = PPMRow.GetDecimal(r, "SapBalance"),
            PrepaymentBalance = PPMRow.GetDecimal(r, "PrepaymentBalance"),
            Variance          = PPMRow.GetDecimal(r, "Variance"),
            Status            = PPMRow.GetString(r, "Status"),
            Period            = PPMRow.GetString(r, "Period"),
            };
        }

private static PPMReconPeriodOption MapPeriodOption(IDataRecord r)
        {
            return new PPMReconPeriodOption
            {
            PeriodKey   = PPMRow.GetString(r, "PeriodKey"),
            PeriodLabel = PPMRow.GetString(r, "PeriodLabel"),
            };
        }

private static PPMReconUser MapUser(IDataRecord r)
        {
            return new PPMReconUser
            {
            Id          = PPMRow.GetInt(r, "Id"),
            DisplayName = PPMRow.GetString(r, "DisplayName"),
            };
        }

private static PPMReconGlExtractDetail MapGlExtractDetail(IDataRecord r)
        {
            return new PPMReconGlExtractDetail
            {
            OpeningBalance = PPMRow.GetDecimal(r, "OpeningBalance"),
            PeriodDebit    = PPMRow.GetDecimal(r, "PeriodDebit"),
            PeriodCredit   = PPMRow.GetDecimal(r, "PeriodCredit"),
            ClosingBalance = PPMRow.GetDecimal(r, "ClosingBalance"),
            CompanyCode    = PPMRow.GetString(r, "CompanyCode"),
            ExtractDate    = PPMRow.GetDateTimeN(r, "ExtractDate"),
            };
        }

private static PPMReconInvoiceRecognised MapInvoiceRecognised(IDataRecord r)
        {
            return new PPMReconInvoiceRecognised
            {
            InvoiceNo  = PPMRow.GetString(r, "InvoiceNo"),
            Recognised = PPMRow.GetDecimal(r, "Recognised"),
            };
        }

private static PPMReconFinhubDetail MapFinhubDetail(IDataRecord r)
        {
            return new PPMReconFinhubDetail
            {
            Recognised  = PPMRow.GetDecimal(r, "Recognised"),
            Amortised   = PPMRow.GetDecimal(r, "Amortised"),
            Outstanding = PPMRow.GetDecimal(r, "Outstanding"),
            SapBalance  = PPMRow.GetDecimal(r, "SapBalance"),
            Variance    = PPMRow.GetDecimal(r, "Variance"),
            };
        }

private static PPMReconDetailHeader MapDetailHeader(IDataRecord r)
        {
            return new PPMReconDetailHeader
            {
            Id                = PPMRow.GetLong(r, "Id"),
            Period            = PPMRow.GetString(r, "Period"),
            Status            = PPMRow.GetString(r, "Status"),
            Variance          = PPMRow.GetDecimal(r, "Variance"),
            InvestigationNote = PPMRow.GetString(r, "InvestigationNote"),
            ResolutionAction  = PPMRow.GetString(r, "ResolutionAction"),
            AssignedToUserId  = PPMRow.GetIntN(r, "AssignedToUserId"),
            AssignedTo        = PPMRow.GetString(r, "AssignedTo"),
            DeliveryGroupCode = PPMRow.GetString(r, "DeliveryGroupCode"),
            GroupName         = PPMRow.GetString(r, "GroupName"),
            GlAccount         = PPMRow.GetString(r, "GlAccount"),
            GlDescription     = PPMRow.GetString(r, "GlDescription"),
            };
        }
    }
}
