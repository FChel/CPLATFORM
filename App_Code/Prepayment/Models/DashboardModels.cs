using System.Collections.Generic;

namespace Prepayment.Web.Models
{
    // ── Shared ────────────────────────────────────────────────────────────────

    /// <summary>A KPI card (label / value / sub-text / colour class for the value).</summary>
    public class PPMKpi
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string Sub { get; set; }
        /// <summary>CSS modifier for the value: "" | "green" | "amber" | "blue".</summary>
        public string ValueClass { get; set; }
        /// <summary>Optional inline style on the value (used for the red error colour).</summary>
        public string ValueStyle { get; set; }
    }

    /// <summary>A pill badge (text + CSS modifier: w/s/e/a/b).</summary>
    public class PPMBadge
    {
        public string Text { get; set; }
        public string Cls { get; set; }
        public PPMBadge() { }
        public PPMBadge(string text, string cls) { Text = text; Cls = cls; }
    }

    // ── Tab 1 — PO Identification ──────────────────────────────────────────────

    public class PPMSearchResultPo
    {
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        public string Project { get; set; }
        /// <summary>WBS element (real feed).</summary>
        public string Wbs { get; set; }
        public string DeliveryGroup { get; set; }
        public string DeliveryGroupName { get; set; }
        /// <summary>'CAPEX' | 'OPEX' badge text.</summary>
        public string CapexOpex { get; set; }
        public string CapabilityManager { get; set; }
        public string DeliveryManager { get; set; }
        public string PoValue { get; set; }
        /// <summary>Open/remaining commitment, formatted.</summary>
        public string CurrentCommitment { get; set; }
        public string Currency { get; set; }
        public string PoDate { get; set; }
        public string Lines { get; set; }
        public PPMBadge PrepaymentLines { get; set; }
        public string ActionText { get; set; }
        public bool ActionPrimary { get; set; }
        public string RowStyle { get; set; }
    }

    public class PPMDeliveryLine
    {
        /// <summary>DB primary key of the underlying prepayment.PPMPoDeliveryLine row (for write-back).</summary>
        public long DeliveryLineId { get; set; }
        public string Line { get; set; }
        /// <summary>Account assignment number (real line identity component).</summary>
        public string AcctAssign { get; set; }
        public string Description { get; set; }
        public string ServiceNote { get; set; }
        public string GlAccount { get; set; }
        public string GlDescription { get; set; }
        public string Wbs { get; set; }
        public string WbsDescription { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string ScheduledDate { get; set; }
        public string Qty { get; set; }
        public string UnitPrice { get; set; }
        public string LineValue { get; set; }
        public bool IsPrepayment { get; set; }
        public bool Decided { get; set; }
        /// <summary>Raw flag value for the §3.1 3-state selector: "Prepayment" | "NotPrepayment" | "Pending".</summary>
        public string Flag { get; set; }
        /// <summary>If the line is decided, show this static note; otherwise an editable input is rendered.</summary>
        public string Note { get; set; }
        public string RowStyle { get; set; }
    }

    public class PPMExistingPrepaymentPo
    {
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        public string DeliveryGroup { get; set; }
        public string RecognisedAmount { get; set; }
        public PPMBadge AmortisationStatus { get; set; }
        public string OutstandingBalance { get; set; }
        public string ActionText { get; set; }
        /// <summary>Where the action button navigates: "journals" (Tab 3) or "schedule" (Tab 2).</summary>
        public string ActionTarget { get; set; }
    }

    // ── Tab 2 — Prepayment & Amortisation ──────────────────────────────────────

    public class PPMNewInvoice
    {
        /// <summary>DB primary key of the underlying prepayment.Invoice row (for selection/write-back).</summary>
        public long InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public string PoLine { get; set; }
        public string Vendor { get; set; }
        public string GlAccount { get; set; }
        /// <summary>Offset / cash (expense) GL.</summary>
        public string CashGlAccount { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string InvoiceDate { get; set; }
        public string Amount { get; set; }
        /// <summary>Foreign amount + currency when the invoice is in a foreign currency (else blank).</summary>
        public string ForeignAmount { get; set; }
        public string Description { get; set; }
        public PPMBadge Flag { get; set; }
        public PPMBadge SetupStatus { get; set; }
        public string ActionText { get; set; }
        public bool ActionPrimary { get; set; }
        public string RowStyle { get; set; }
    }

    public class PPMExistingBalanceInvoice
    {
        /// <summary>DB primary key of the underlying prepayment.Invoice row (for selection/navigation).</summary>
        public long InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public string PoNumber { get; set; }
        public string PoLine { get; set; }
        public string Vendor { get; set; }
        public string GlAccount { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string InvoiceDate { get; set; }
        public string Amount { get; set; }
        public string RecognisedAmount { get; set; }
        public PPMBadge AmortisationStatus { get; set; }
        public string ActionText { get; set; }
        /// <summary>Where the action button navigates: "journals" (Tab 3) or "invoice" (stay on Tab 2).</summary>
        public string ActionTarget { get; set; }
    }

    public class PPMScheduleRow
    {
        public long PeriodId { get; set; }
        public string Num { get; set; }
        public string Period { get; set; }
        public PPMBadge Status { get; set; }
        public string Amount { get; set; }
    }

    // ── Tab 3 — Journals ───────────────────────────────────────────────────────

    public class PPMRecognitionJournal
    {
        /// <summary>DB primary key of the underlying prepayment.Journal row (for selection/write-back).</summary>
        public long JournalId { get; set; }
        public string JournalRef { get; set; }
        public string PoInvoice { get; set; }
        public string Vendor { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string DrAsset { get; set; }
        public string CrExpense { get; set; }
        public string Amount { get; set; }
        public string Period { get; set; }
        public PPMBadge Status { get; set; }
        public string ActionText { get; set; }
        public bool ActionPrimary { get; set; }
        public bool ActionSuccess { get; set; }
    }

    public class PPMAmortisationJournal
    {
        /// <summary>DB primary key of the underlying prepayment.Journal row (for selection/write-back).</summary>
        public long JournalId { get; set; }
        public string JournalRef { get; set; }
        public string PoPrepayment { get; set; }
        public string Vendor { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string Period { get; set; }
        public string DrExpense { get; set; }
        public string CrAsset { get; set; }
        public string PeriodAmount { get; set; }
        public string RemainingBalance { get; set; }
        public PPMBadge Status { get; set; }
        public string ActionText { get; set; }
        public bool ActionPrimary { get; set; }
        public bool ActionSuccess { get; set; }
    }

    /// <summary>A double-entry posting line in a journal detail table.</summary>
    public class PPMJournalEntry
    {
        /// <summary>"Dr" or "Cr".</summary>
        public string Dc { get; set; }
        public string Account { get; set; }
        public string Description { get; set; }
        public string CostObject { get; set; }
        public string Amount { get; set; }
        /// <summary>Numeric amount, used to compute the balanced journal total.</summary>
        public decimal AmountValue { get; set; }
    }

    /// <summary>Generic label/value pair for the detail panels.</summary>
    public class PPMLabelValue
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string ValueStyle { get; set; }
        public PPMLabelValue() { }
        public PPMLabelValue(string label, string value, string valueStyle = null)
        {
            Label = label; Value = value; ValueStyle = valueStyle;
        }
    }

    // ── Tab 4 — Admin ──────────────────────────────────────────────────────────

    public class PPMProcessTrackerRow
    {
        public string PoNumber { get; set; }
        public string Vendor { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string Amount { get; set; }
        public string PoFlag { get; set; }
        public string Invoice { get; set; }
        public string Setup { get; set; }
        public string Recognised { get; set; }
        public string Amortising { get; set; }
        public string Export { get; set; }
        public PPMBadge Status { get; set; }
        // Optional inline styles for the cells that can show a negative (❌) or warning (⚠) state.
        public string PoFlagStyle { get; set; }
        public string InvoiceStyle { get; set; }
        public string SetupStyle { get; set; }
        public string RecognisedStyle { get; set; }
        public string AmortisingStyle { get; set; }
        public string ExportStyle { get; set; }
    }

    // ── Tab 5 — Group Workflow Control ─────────────────────────────────────────

    /// <summary>
    /// A delivery-group workflow row. The six stage cells (PoFlag … Exported) each
    /// carry a glyph (✅ / ⚠ / ✗ / —) plus an optional inline style for the colour.
    /// </summary>
    public class PPMGroupWorkflowRow
    {
        public string Group { get; set; }
        public string GroupName { get; set; }
        public string Preparer { get; set; }
        public string Approver { get; set; }
        /// <summary>Live count of purchase orders in the group (§3.5).</summary>
        public int PoCount { get; set; }
        /// <summary>Live count of invoices on the group's POs (§3.5).</summary>
        public int InvoiceCount { get; set; }
        /// <summary>Live count of journals in the group (§3.5).</summary>
        public int JournalCount { get; set; }
        public PPMBadge Stage { get; set; }
        public PPMBadge Status { get; set; }
        /// <summary>Drill-down target tab for the "View detail" action: "po" | "amortisation" | "journals" | "admin".</summary>
        public string ActionTarget { get; set; }
        public string RowStyle { get; set; }
    }

    // ── Tab 6 — GL Balance Reconciliation ──────────────────────────────────────

    /// <summary>A reconciliation line comparing the SAP GL extract to FINHUB.</summary>
    public class PPMReconciliationRow
    {
        public string Group { get; set; }
        public string GroupName { get; set; }
        public string GlAccount { get; set; }
        public string SapBalance { get; set; }
        public string FinhubBalance { get; set; }
        public string Variance { get; set; }
        /// <summary>Inline style for the variance cell (green / warn / error).</summary>
        public string VarianceStyle { get; set; }
        public PPMBadge Status { get; set; }
        public string ActionText { get; set; }
        public string ActionStyle { get; set; }
        public string RowStyle { get; set; }
        // Numeric values, used to compute the reconciliation totals row in the back-end.
        public decimal SapValue { get; set; }
        public decimal FinhubValue { get; set; }
        /// <summary>True if this row is counted as an open variance (drives the totals badge).</summary>
        public bool IsVariance { get; set; }
        /// <summary>DB id of the underlying Reconciliation row, for the Detail / Investigate drill-down.</summary>
        public long ReconciliationId { get; set; }
        public string GlDescription { get; set; }
    }

    // ── Tab 7 — Prepayment Report by Group ─────────────────────────────────────

    /// <summary>A per-group balance line in the prepayment report.</summary>
    public class PPMReportRow
    {
        /// <summary>DB id of the delivery group, used to load the drill-down on row click.</summary>
        public long DeliveryGroupId { get; set; }
        public string Group { get; set; }
        public string GroupName { get; set; }
        public string GlAccount { get; set; }
        public string Vendor { get; set; }
        /// <summary>'CAPEX' | 'OPEX'.</summary>
        public string CapexOpex { get; set; }
        public string Recognised { get; set; }
        public string Amortised { get; set; }
        public string AmortisedStyle { get; set; }
        public string Outstanding { get; set; }
        public string OutstandingStyle { get; set; }
        /// <summary>"% amortised" progress bar (width string, e.g. "66.8%") + display label.</summary>
        public string PercentLabel { get; set; }
        public string PercentWidth { get; set; }
        public string PeriodsLeft { get; set; }
        public string EndDate { get; set; }
        public PPMBadge Status { get; set; }
        /// <summary>Inline style on the row; highlights the group currently shown in the drill-down.</summary>
        public string RowStyle { get; set; }
    }

    /// <summary>A drill-down amortisation schedule row (with cumulative column).</summary>
    public class PPMDrilldownScheduleRow
    {
        public string Num { get; set; }
        public string Period { get; set; }
        public string Amount { get; set; }
        public string Cumulative { get; set; }
        public PPMBadge Status { get; set; }
        public string RowStyle { get; set; }
    }

    // ── Computed totals / summary rows (derived in the back-end, not hardcoded) ──

    /// <summary>
    /// Footer counts for the Group Workflow table: how many rows are shown vs. the
    /// real group total (drives "N more groups · Showing X of Y"). The control derives
    /// "shown"/"remaining" from the live row count; only the real Total is carried here.
    /// </summary>
    public class PPMGroupWorkflowTotals
    {
        public int Total { get; set; }
    }

    /// <summary>Totals row for the GL reconciliation table.</summary>
    public class PPMReconciliationTotals
    {
        public string SapBalance { get; set; }
        public string FinhubBalance { get; set; }
        public string Variance { get; set; }
        public string VarianceStyle { get; set; }
        public PPMBadge VarianceBadge { get; set; }
    }

    /// <summary>Totals row for the prepayment report table.</summary>
    public class PPMReportTotals
    {
        public string Recognised { get; set; }
        public string Amortised { get; set; }
        public string Outstanding { get; set; }
    }

    /// <summary>Computed figures for the drill-down progress bar + schedule total.</summary>
    public class PPMDrilldownTotals
    {
        public string ScheduleTotal { get; set; }
        /// <summary>e.g. "66.8%" — width of the progress bar.</summary>
        public string PercentAmortised { get; set; }
        public string AmortisedLabel { get; set; }   // "$198,333 amortised"
        public string RemainingLabel { get; set; }    // "$141,667 remaining"
    }

}
