using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CPlatform.PORD
{
    // =========================================================================
    // PO Review — check registry
    //
    // The module is a PO compliance dashboard; each compliance rule is a
    // CHECK plugged into the same cycle:
    //
    //   BODS extract (one per check) → Load → exceptions (PO × check)
    //   → one package per Delivery Manager program per cycle, covering ALL
    //     checks → one AS Fin email + one POC email, one review page with a
    //     tab per check → finalise → outcomes / exclusions / next-load verify.
    //
    // What varies per check lives here: name, issue categories, the issue
    // cell on the review page, "why flagged" text, extract columns and the
    // fix wording. Reason codes are tagged with their check (PordReasonCode.
    // CheckType). The response model (fix / valid reason / reassign), the
    // lifecycle, emails, exclusions and reporting are shared.
    //
    // Adding a check:
    //   1. Register a PordCheckDef below (Status = "Live").
    //   2. Add its reason codes and extract columns.
    //   3. Implement its Classify rule (PORDRules-style pure function).
    //   4. BODS supplies a new extract; Load file gains the extract type.
    // No new pages, handlers or tables are needed.
    // =========================================================================

    public sealed class PordIssueDef
    {
        public string Key, Label, Description, Css;
        public PordIssueDef(string key, string label, string css, string description)
        { Key = key; Label = label; Css = css; Description = description; }
    }

    public sealed class PordCheckDef
    {
        public string Key;           // stored on every exception (CheckType)
        public string Name;          // "Non-standard payment terms"
        public string ShortName;     // tab label: "Payment terms"
        public string Description;
        public string Status;        // Live | Example | Planned
        public string FilePattern;   // BODS extract name
        public string IssueHeader;   // review-table header for the issue cell
        public string FixLabel;      // response option for "I will fix it"
        public string FixDateLabel;  // detail label for the fix date
        public string PolicyText;
        public string[] ExtractColumns = new string[0];   // for display on Load file (NSPT uses PORDCsvParser.Columns)
        public List<PordIssueDef> Issues = new List<PordIssueDef>();
        public Func<PordPo, string> IssueCell;   // HTML for the issue cell (without the pill)
        public Func<PordPo, string> Why;         // plain text, why flagged

        public bool IsActive { get { return Status == "Live" || Status == "Example"; } }

        public PordIssueDef Issue(string key)
        {
            return Issues.FirstOrDefault(i => i.Key == key) ?? new PordIssueDef(key ?? "", key ?? "", "", "");
        }
    }

    public static class PORDChecks
    {
        public const string Nspt    = "NSPT";
        public const string CcyBank = "CCYBANK";

        private static readonly List<PordCheckDef> _all = Build();

        public static IList<PordCheckDef> All    { get { return _all; } }
        public static IList<PordCheckDef> Active { get { return _all.Where(c => c.IsActive).ToList(); } }

        public static PordCheckDef Get(string key)
        {
            return _all.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) ?? _all[0];
        }

        public static PordIssueDef IssueOf(PordPo p)   { return Get(p.CheckType).Issue(p.IssueKey); }

        public static string IssuePill(PordPo p)       { return IssuePill(p.CheckType, p.IssueKey); }

        public static string IssuePill(string check, string issueKey)
        {
            var i = Get(check).Issue(issueKey);
            return "<span class=\"pord-cat " + i.Css + "\" title=\"" + PORDHelper.Attr(i.Description) + "\">" + PORDHelper.Enc(i.Label) + "</span>";
        }

        public static string CheckBadge(PordCheckDef c)
        {
            string cls = c.Status == "Live" ? "finalised" : c.Status == "Example" ? "duesoon" : "closed";
            return "<span class=\"pill " + cls + "\">" + PORDHelper.Enc(c.Status) + "</span>";
        }

        private static List<PordCheckDef> Build()
        {
            var list = new List<PordCheckDef>();

            // ---------------------------------------------------------- NSPT
            var nspt = new PordCheckDef
            {
                Key = Nspt, Name = "Non-standard payment terms", ShortName = "Payment terms", Status = "Live",
                Description = "Open POs whose payment terms are not the Defence standard (20 days, or 5 days for PEPPOL suppliers) or differ from the supplier master. Foreign-currency POs need at least 14 days.",
                FilePattern = "PO_NSPT_REVIEW_*.csv", IssueHeader = "Terms (PO vs BP) & issue",
                FixLabel = "Amend PO terms", FixDateLabel = "Amend by",
                PolicyText = "RMG-417 Supplier Pay On-Time or Pay Interest"
            };
            foreach (var c in new[] { PordCategory.NonStandard, PordCategory.Override, PordCategory.NonStandardAndOverride, PordCategory.ForeignCurrency })
                nspt.Issues.Add(new PordIssueDef(PORDRules.CategoryKey(c), PORDRules.CategoryLabel(c), PORDRules.CategoryCss(c), PORDRules.CategoryDescription(c)));
            nspt.IssueCell = p => PORD_TermsHtml(p);
            nspt.Why = p => PORDRules.CategoryDescription(p.Category) + " PO terms " + PORDRules.TermLabel(p.PoTermKey, p.PoTermDays)
                          + ", BP master " + PORDRules.TermLabel(p.BpTermKey, p.BpTermDays) + ".";
            list.Add(nspt);

            // ---------------------------------------------------------- Currency vs bank (example)
            var bank = new PordCheckDef
            {
                Key = CcyBank, Name = "Currency vs bank mismatch", ShortName = "Currency vs bank", Status = "Example",
                Description = "POs whose currency does not match the supplier's bank account currency or country. Payments fail or incur conversion costs. Example check showing how further checks plug in.",
                FilePattern = "PO_CCYBANK_REVIEW_*.csv", IssueHeader = "PO vs bank & issue",
                FixLabel = "Correct PO / bank details", FixDateLabel = "Fix by",
                PolicyText = "Decision Brief — compliance monitoring",
                ExtractColumns = new[] { "Co Code", "PO Number", "PO Currency", "BP Number", "BP Name", "Bank Account (masked)", "Bank Currency",
                                         "Bank Country", "Still to Deliver", "DM Program", "Delivery Manager", "POC Email" }
            };
            bank.Issues.Add(new PordIssueDef("ccy",  "Currency mismatch", "cat-ccy",  "PO currency differs from the currency of the supplier's nominated bank account."));
            bank.Issues.Add(new PordIssueDef("ctry", "Bank country mismatch", "cat-ctry", "AUD PO paying to an overseas bank account, or FX PO paying to an Australian account."));
            bank.IssueCell = p =>
                "<span class=\"pord-terms\"><span class=\"pord-term bad\">" + PORDHelper.Enc(p.Currency) + "</span><span class=\"arrow\">vs</span>"
                + "<span class=\"pord-term\">" + PORDHelper.Enc(Attr(p, "BankCurrency")) + "<span class=\"d\">" + PORDHelper.Enc(Attr(p, "BankCountry")) + "</span></span></span>";
            bank.Why = p => bank.Issue(p.IssueKey).Description + " PO in " + p.Currency + "; bank account " + Attr(p, "BankAccount")
                          + " is " + Attr(p, "BankCurrency") + " (" + Attr(p, "BankCountry") + ").";
            list.Add(bank);

            // ---------------------------------------------------------- Planned (Decision Brief)
            list.Add(Planned("DPT", "Direct payment threshold",    "Payments made outside a PO above the direct-payment threshold."));
            list.Add(Planned("POP", "Purpose of payment codes",    "Missing or invalid purpose-of-payment codes on overseas payments."));
            list.Add(Planned("NPP", "Non-procurement payment type", "Non-procurement payment types used where a procurement should exist."));
            return list;
        }

        private static PordCheckDef Planned(string key, string name, string desc)
        {
            return new PordCheckDef { Key = key, Name = name, ShortName = name, Status = "Planned", Description = desc + " Scope to be confirmed." };
        }

        public static string Attr(PordPo p, string key)
        {
            string v;
            return p.Attr != null && p.Attr.TryGetValue(key, out v) ? v : "";
        }

        /// <summary>NSPT issue cell: PO term chip vs BP term chip (+ PEPPOL / FX markers).</summary>
        public static string PORD_TermsHtml(PordPo p)
        {
            bool fx = PORDRules.IsForeignCurrency(p.Currency);
            bool poBad = fx ? (p.PoTermDays.HasValue && p.PoTermDays.Value < PORDRules.ForeignCurrencyMinDays)
                            : !PORDRules.IsStandardAud(p.PoTermDays) || !p.TermsMatch;
            var sb = new StringBuilder("<span class=\"pord-terms\">");
            sb.Append(Chip(p.PoTermKey, p.PoTermDays, poBad));
            sb.Append("<span class=\"arrow\" title=\"PO terms vs BP master terms\">vs</span>");
            sb.Append(Chip(p.BpTermKey, p.BpTermDays, false));
            if (PORDRules.IsPeppolTerm(p.BpTermDays))
                sb.Append("<span class=\"pord-peppol\" title=\"PEPPOL e-invoicing supplier: 5-day terms are standard\">PEPPOL</span>");
            if (fx) sb.Append("<span class=\"pord-ccy fx\">").Append(PORDHelper.Enc(p.Currency)).Append("</span>");
            sb.Append("</span>");
            return sb.ToString();
        }

        private static string Chip(string key, int? days, bool bad)
        {
            return "<span class=\"pord-term" + (bad ? " bad" : "") + "\">" + PORDHelper.Enc(key)
                 + (days.HasValue ? "<span class=\"d\">" + days.Value + "d</span>" : "") + "</span>";
        }
    }
}
