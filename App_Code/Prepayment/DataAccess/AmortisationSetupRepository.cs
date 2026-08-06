using System.Collections.Generic;
using System.Data;
using Prepayment.Web.Models.Dtos;
using Prepayment.Web.Models.Entities;

namespace Prepayment.Web.DataAccess
{
    /// <summary>Data-access contract for the Amortisation Setup page (§3.2).</summary>
    public interface IPPMAmortisationSetupRepository
    {
        PPMAmortisationSetupKpis GetKpis();
        IReadOnlyList<PPMNewInvoiceRow> GetNewInvoices();
        IReadOnlyList<PPMExistingBalanceInvoiceRow> GetExistingBalanceInvoices();
        PPMInvoiceSetupDetail GetInvoiceDetail(long invoiceId);
        long? ResolveInvoiceByPo(string poNumber);
        IReadOnlyList<PPMPrepaymentGlOption> GetPrepaymentGlAccounts();
        IReadOnlyList<PPMSchedulePeriodRow> GetScheduleForInvoice(long invoiceId);
        long SaveDraft(PPMAmortisationSetupRequest req, int userId);
        long GenerateScheduleAndJournals(PPMAmortisationSetupRequest req, int userId);
        int SavePeriodAmounts(long invoiceId, string periodsJson, int userId);
    }

    /// <summary>PPMDbHelper implementation. Every call invokes a prepayment.AmortisationSetup_* proc.</summary>
    public class PPMAmortisationSetupRepository : IPPMAmortisationSetupRepository
    {
        public PPMAmortisationSetupKpis GetKpis()
        {
            return PPMDbHelper.QuerySingleOrDefault("prepayment.AmortisationSetup_GetKpis", MapKpis)
                   ?? new PPMAmortisationSetupKpis();
        }

        public IReadOnlyList<PPMNewInvoiceRow> GetNewInvoices()
        {
            return PPMDbHelper.Query("prepayment.AmortisationSetup_GetNewInvoices", MapNewInvoiceRow);
        }

        public IReadOnlyList<PPMExistingBalanceInvoiceRow> GetExistingBalanceInvoices()
        {
            return PPMDbHelper.Query("prepayment.AmortisationSetup_GetExistingBalanceInvoices", MapExistingBalanceInvoiceRow);
        }

        public PPMInvoiceSetupDetail GetInvoiceDetail(long invoiceId)
        {
            return PPMDbHelper.QuerySingleOrDefault(
                "prepayment.AmortisationSetup_GetInvoiceDetail", MapInvoiceSetupDetail, invoiceId);
        }

        public long? ResolveInvoiceByPo(string poNumber)
        {
            return PPMDbHelper.ExecuteScalar<long?>("prepayment.AmortisationSetup_ResolveInvoiceByPo", poNumber);
        }

        public IReadOnlyList<PPMPrepaymentGlOption> GetPrepaymentGlAccounts()
        {
            return PPMDbHelper.Query("prepayment.AmortisationSetup_GetPrepaymentGlAccounts", MapPrepaymentGlOption);
        }

        public IReadOnlyList<PPMSchedulePeriodRow> GetScheduleForInvoice(long invoiceId)
        {
            return PPMDbHelper.Query(
                "prepayment.AmortisationSetup_GetScheduleForInvoice", MapSchedulePeriodRow, invoiceId);
        }

        public long SaveDraft(PPMAmortisationSetupRequest r, int userId)
        {
            return PPMDbHelper.ExecuteScalar<long>("prepayment.AmortisationSetup_SaveDraft", BuildParams(r, userId));
        }

        public long GenerateScheduleAndJournals(PPMAmortisationSetupRequest r, int userId)
        {
            // Proc returns ScheduleId, RecognitionJournalId, PeriodsCreated — take ScheduleId.
            long scheduleId = PPMDbHelper.QuerySingle(
                "prepayment.AmortisationSetup_GenerateScheduleAndJournals",
                record => PPMRow.GetLong(record, "ScheduleId"),
                BuildParams(r, userId));
            return scheduleId;
        }

        public int SavePeriodAmounts(long invoiceId, string periodsJson, int userId)
        {
            return PPMDbHelper.ExecuteScalar<int>(
                "prepayment.AmortisationSetup_SavePeriodAmounts", invoiceId, periodsJson, userId);
        }

        private static object[] BuildParams(PPMAmortisationSetupRequest r, int userId)
        {
            return new object[]
        {
            r.InvoiceId,
            r.AssetClassification,
            r.ExpenditureType,
            r.AmortisationType,
            r.StartDate,
            r.EndDate,
            r.Periods,
            r.Frequency,
            r.PrepaymentGlId,
            r.ExpenseGlAccount,
            r.CostCentreWbs,
            r.CompanyCode,
            userId
        };
        }

        // ── Manual reader-to-object mappers (Implementation_Plan_CPlatform_Port.md §6) ─────

private static PPMAmortisationSetupKpis MapKpis(IDataRecord r)
        {
            return new PPMAmortisationSetupKpis
            {
            NewInvoicesToReview       = PPMRow.GetInt(r, "NewInvoicesToReview"),
            ExistingBalanceInvoices   = PPMRow.GetInt(r, "ExistingBalanceInvoices"),
            AmortisationSetupsPending = PPMRow.GetInt(r, "AmortisationSetupsPending"),
            SchedulesActive           = PPMRow.GetInt(r, "SchedulesActive"),
            TotalPrepaymentBalance    = PPMRow.GetDecimal(r, "TotalPrepaymentBalance"),
            };
        }

private static PPMNewInvoiceRow MapNewInvoiceRow(IDataRecord r)
        {
            return new PPMNewInvoiceRow
            {
            InvoiceId       = PPMRow.GetLong(r, "InvoiceId"),
            InvoiceNo       = PPMRow.GetString(r, "InvoiceNo"),
            PoNumber        = PPMRow.GetString(r, "PoNumber"),
            LineNumber      = PPMRow.GetIntN(r, "LineNumber"),
            Vendor          = PPMRow.GetString(r, "Vendor"),
            GlAccount       = PPMRow.GetString(r, "GlAccount"),
            CashGlAccount   = PPMRow.GetString(r, "CashGlAccount"),
            CapexOpex       = PPMRow.GetString(r, "CapexOpex"),
            InvoiceDate     = PPMRow.GetDateTimeN(r, "InvoiceDate"),
            Amount          = PPMRow.GetDecimal(r, "Amount"),
            AmountDoc       = PPMRow.GetDecimalN(r, "AmountDoc"),
            FxRate          = PPMRow.GetDecimalN(r, "FxRate"),
            ForeignCurrency = PPMRow.GetString(r, "ForeignCurrency"),
            Description     = PPMRow.GetString(r, "Description"),
            Flag            = PPMRow.GetString(r, "Flag"),
            SetupStatus     = PPMRow.GetString(r, "SetupStatus"),
            };
        }

private static PPMExistingBalanceInvoiceRow MapExistingBalanceInvoiceRow(IDataRecord r)
        {
            return new PPMExistingBalanceInvoiceRow
            {
            InvoiceId        = PPMRow.GetLong(r, "InvoiceId"),
            InvoiceNo        = PPMRow.GetString(r, "InvoiceNo"),
            PoNumber         = PPMRow.GetString(r, "PoNumber"),
            LineNumber       = PPMRow.GetIntN(r, "LineNumber"),
            Vendor           = PPMRow.GetString(r, "Vendor"),
            GlAccount        = PPMRow.GetString(r, "GlAccount"),
            CapexOpex        = PPMRow.GetString(r, "CapexOpex"),
            InvoiceDate      = PPMRow.GetDateTimeN(r, "InvoiceDate"),
            Amount           = PPMRow.GetDecimal(r, "Amount"),
            RecognisedAmount = PPMRow.GetDecimal(r, "RecognisedAmount"),
            AmortisedToDate  = PPMRow.GetDecimal(r, "AmortisedToDate"),
            Periods          = PPMRow.GetIntN(r, "Periods"),
            ScheduleStatus   = PPMRow.GetString(r, "ScheduleStatus"),
            };
        }

private static PPMInvoiceSetupDetail MapInvoiceSetupDetail(IDataRecord r)
        {
            return new PPMInvoiceSetupDetail
            {
            InvoiceId           = PPMRow.GetLong(r, "InvoiceId"),
            InvoiceNo           = PPMRow.GetString(r, "InvoiceNo"),
            PoNumber            = PPMRow.GetString(r, "PoNumber"),
            LineNumber          = PPMRow.GetIntN(r, "LineNumber"),
            Vendor              = PPMRow.GetString(r, "Vendor"),
            Amount              = PPMRow.GetDecimal(r, "Amount"),
            AmountDoc           = PPMRow.GetDecimalN(r, "AmountDoc"),
            FxRate              = PPMRow.GetDecimalN(r, "FxRate"),
            ForeignCurrency     = PPMRow.GetString(r, "ForeignCurrency"),
            Description         = PPMRow.GetString(r, "Description"),
            OriginalGl          = PPMRow.GetString(r, "OriginalGl"),
            CashGlAccount       = PPMRow.GetString(r, "CashGlAccount"),
            CapexOpex           = PPMRow.GetString(r, "CapexOpex"),
            ProfitCentre        = PPMRow.GetString(r, "ProfitCentre"),
            ProfitCentreDesc    = PPMRow.GetString(r, "ProfitCentreDesc"),
            WbsCostCentre       = PPMRow.GetString(r, "WbsCostCentre"),
            CompanyCode         = PPMRow.GetString(r, "CompanyCode"),
            DeliveryGroupId     = PPMRow.GetLongN(r, "DeliveryGroupId"),
            DeliveryGroup       = PPMRow.GetString(r, "DeliveryGroup"),
            SetupStatus         = PPMRow.GetString(r, "SetupStatus"),
            ScheduleId          = PPMRow.GetLongN(r, "ScheduleId"),
            AssetClassification = PPMRow.GetString(r, "AssetClassification"),
            ExpenditureType     = PPMRow.GetString(r, "ExpenditureType"),
            AmortisationType    = PPMRow.GetString(r, "AmortisationType"),
            StartDate           = PPMRow.GetDateTimeN(r, "StartDate"),
            EndDate             = PPMRow.GetDateTimeN(r, "EndDate"),
            Periods             = PPMRow.GetIntN(r, "Periods"),
            Frequency           = PPMRow.GetString(r, "Frequency"),
            PrepaymentGlId      = PPMRow.GetLongN(r, "PrepaymentGlId"),
            ExpenseGlAccount    = PPMRow.GetString(r, "ExpenseGlAccount"),
            };
        }

private static PPMPrepaymentGlOption MapPrepaymentGlOption(IDataRecord r)
        {
            return new PPMPrepaymentGlOption
            {
            PrepaymentGlId      = PPMRow.GetLong(r, "PrepaymentGlId"),
            GlAccount           = PPMRow.GetString(r, "GlAccount"),
            GlDescription       = PPMRow.GetString(r, "GlDescription"),
            AssetClassification = PPMRow.GetString(r, "AssetClassification"),
            ExpenditureType     = PPMRow.GetString(r, "ExpenditureType"),
            };
        }

private static PPMSchedulePeriodRow MapSchedulePeriodRow(IDataRecord r)
        {
            return new PPMSchedulePeriodRow
            {
            PeriodId     = PPMRow.GetLong(r, "PeriodId"),
            PeriodNumber = PPMRow.GetInt(r, "PeriodNumber"),
            PeriodDate   = PPMRow.GetDateTimeN(r, "PeriodDate"),
            Amount       = PPMRow.GetDecimal(r, "Amount"),
            Status       = PPMRow.GetString(r, "Status"),
            };
        }
    }
}
