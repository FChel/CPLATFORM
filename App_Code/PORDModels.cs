using System;
using System.Collections.Generic;

namespace CPlatform.PORD
{
    // =========================================================================
    // PO Review (PORD) — domain model
    //
    // PORD is built as a *family* of PO compliance checks. Tranche 1 is
    // Non-Standard Payment Terms (NSPT). Every flagged PO carries a CheckType
    // so later tranches (currency / bank mismatch, direct payment threshold,
    // purpose-of-payment codes …) can reuse the same load → package → review
    // → finalise → close-out loop without a new module.
    //
    // C# 5 compatible (App_Code is compiled on the fly by ASP.NET 4.8):
    // no string interpolation, no ?. operator, no expression-bodied members.
    // =========================================================================

    public static class PordCheckType
    {
        public const string PaymentTerms = "NSPT";
    }

    /// <summary>NSPT exception category, per the business requirement.</summary>
    public enum PordCategory
    {
        None             = 0,
        NonStandard      = 1,   // AUD PO term is not 5 or 20 days
        Override         = 2,   // PO term differs from the BP master term
        NonStandardAndOverride = 3,
        ForeignCurrency  = 4    // FX PO with fewer than 14 days' terms
    }

    /// <summary>The reviewer's response to a flagged PO.</summary>
    public static class PordResponse
    {
        public const string Amend    = "Amend";     // commit to correcting the PO terms
        public const string Reason   = "Reason";    // a valid reason exists (reason code + evidence)
        public const string Reassign = "Reassign";  // not my PO — tell us who
        public const string NoResponse = "NoResponse"; // system: applied at finalise
    }

    public static class PordStatus
    {
        public const string NotSent   = "NotSent";
        public const string Sent      = "Sent";
        public const string InReview  = "InReview";
        public const string Finalised = "Finalised";
        public const string Closed    = "Closed";     // outcomes issued; terminal
        public const string Cancelled = "Cancelled";
    }

    /// <summary>One flagged purchase order (header-level: payment terms live on EKKO).</summary>
    public class PordPo
    {
        public int      PoID;
        public int      BatchID;
        public int      PackageID;
        public string   CheckType;
        public string   CompanyCode;
        public string   PoNumber;
        public DateTime? PoCreatedDate;
        public string   PoTermKey;
        public int?     PoTermDays;
        public string   BpNumber;
        public string   BpName;
        public string   BpTermKey;
        public int?     BpTermDays;
        public string   Currency;
        public decimal  Ordered;
        public decimal  Delivered;
        public decimal  StillToDeliver;
        public decimal  Invoiced;
        public string   PurchasingGroup;
        public DateTime? DeliveryDate;
        public string   PoCreator;
        public string   PocEmail;
        public string   PocName;
        public string   DeliveryManager;       // cost centre
        public string   DeliveryManagerName;
        public string   DmProgram;             // grouping key → AS Fin
        public string   ContractNumber;
        public DateTime? ContractDate;
        public int      ReviewNbr;             // how many rounds this PO has appeared in
        public PordCategory Category;

        // Latest review state (one row per PO)
        public string   Response;
        public string   ReasonCode;
        public DateTime? TargetDate;
        public string   EvidenceRef;
        public string   Comments;
        public string   ReviewedBy;
        public DateTime? ReviewedDate;

        public bool IsReviewed { get { return !string.IsNullOrEmpty(Response); } }
        public bool TermsMatch
        {
            get { return string.Equals(PoTermKey ?? "", BpTermKey ?? "", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public class PordPackage
    {
        public int      PackageID;
        public string   DmProgram;
        public string   Token;
        public string   Status;
        public DateTime CreatedDate;
        public DateTime DueDate;
        public DateTime? SentDate;
        public DateTime? LastEmailDate;
        public DateTime? FinalisedDate;
        public string   FinalisedBy;
    }

    public class PordPackagePoc
    {
        public int    PackageID;
        public string PocEmail;
        public string PocName;
        public string Token;
    }

    public class PordAsFinGroup
    {
        public string DmProgram;
        public string Email;
        public string DisplayName;
        public bool   IsActive;
        public bool IsConfigured
        {
            get { return !string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(DisplayName); }
        }
    }

    public class PordReasonCode
    {
        public string Code;
        public string Description;
        public bool   RequiresComments;
        public bool   RequiresEvidence;
        public bool   CreatesExclusion;
        public bool   IsActive;
        public int    DisplayOrder;
    }

    public class PordLoadBatch
    {
        public int      BatchID;
        public string   FileName;
        public DateTime LoadedDate;
        public string   LoadedBy;
        public int      RowsInFile;
        public int      Flagged;
        public int      Excluded;
        public int      Repeat;
        public int      Resolved;
    }

    /// <summary>
    /// A PO excluded from future rounds because a valid reason was accepted
    /// at finalise. Keyed on PO + payment-term key: if the PO's terms change
    /// the exclusion no longer matches and the PO is re-evaluated.
    /// </summary>
    public class PordExclusion
    {
        public string   PoNumber;
        public string   PoTermKey;
        public string   BpName;
        public string   DmProgram;
        public string   ReasonCode;
        public string   EvidenceRef;
        public DateTime GrantedDate;
        public DateTime ExpiryDate;
        public string   GrantedBy;
        public bool     IsRevoked;
    }

    /// <summary>A PO flagged "will amend" last round that no longer appears — verified fixed.</summary>
    public class PordResolved
    {
        public string   PoNumber;
        public string   BpName;
        public string   DmProgram;
        public string   OldTermKey;
        public DateTime CommittedDate;
        public DateTime VerifiedDate;
    }

    /// <summary>A business partner whose master-data terms are themselves non-standard (for DFIM).</summary>
    public class PordBpIssue
    {
        public string BpNumber;
        public string BpName;
        public string BpTermKey;
        public int?   BpTermDays;
        public int    OpenPoCount;
        public decimal StillToDeliver;
        public string Status;   // "New" | "Notified" | "Updated"
    }

    public class PordSaveRow
    {
        public int     PoID;
        public string  Response;
        public string  ReasonCode;
        public string  TargetDate;
        public string  EvidenceRef;
        public string  Comments;
        public string  Version;
    }

    public class PordSaveResult
    {
        public int    PoID;
        public bool   Ok;
        public string ErrorCode;
        public string Message;
        public string Version;
    }
}
