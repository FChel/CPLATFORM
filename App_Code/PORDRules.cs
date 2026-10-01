using System;
using System.Collections.Generic;
using System.Globalization;

namespace CPlatform.PORD
{
    /// <summary>
    /// Non-Standard Payment Terms rule engine. Pure functions, no database —
    /// the same logic classifies rows in the Load preview, the demo data and
    /// (later) the committed load, so the three can never disagree.
    ///
    /// Rule (as clarified in the spec review):
    ///   flag when (AUD and term not in {5, 20})
    ///          or (PO term key differs from the BP purchasing-org term key)
    ///          or (foreign currency and net days &lt; 14)
    ///
    /// Defence standard terms are 20 days, or 5 days for suppliers invoicing
    /// through PEPPOL e-invoicing. BP master records carry these standard terms,
    /// so in practice an exception is a PO whose terms were changed from the BP
    /// master. "Non-standard" on its own (PO matches a non-standard BP master)
    /// should therefore not occur; when it does, the BP master itself is wrong
    /// and goes to DFIM.
    ///
    /// SAP payment terms are keys, not day counts. TermDays resolves a key to
    /// net days from the configured map (PORD.TermKeyDays) with a fallback
    /// that reads trailing digits (e.g. "Z020" → 20). The real key list must
    /// be confirmed with the ERP team before go-live.
    /// </summary>
    public static class PORDRules
    {
        public static readonly int[] StandardAudDays = new[] { 5, 20 };
        public const int ForeignCurrencyMinDays = 14;
        public const string LocalCurrency = "AUD";

        /// <summary>GL accounts excluded from the extract (BODS applies these; the loader re-checks).</summary>
        public static readonly string[] ExcludedGlAccounts = new[] { "211455", "212208" };

        private static Dictionary<string, int> _termMap;
        private static readonly object _lock = new object();

        private static Dictionary<string, int> TermMap
        {
            get
            {
                if (_termMap != null) return _termMap;
                lock (_lock)
                {
                    if (_termMap != null) return _termMap;
                    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    // Demo defaults. Override with PORD.TermKeyDays = "Z005:5;Z020:20;..."
                    string raw = PORDHelper.Setting("PORD.TermKeyDays",
                        "Z000:0;Z005:5;Z007:7;Z010:10;Z014:14;Z020:20;Z030:30;Z045:45;Z060:60");
                    foreach (string pair in raw.Split(';'))
                    {
                        string[] kv = pair.Split(':');
                        int d;
                        if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out d))
                            map[kv[0].Trim()] = d;
                    }
                    _termMap = map;
                }
                return _termMap;
            }
        }

        public static int? TermDays(string termKey)
        {
            if (string.IsNullOrWhiteSpace(termKey)) return null;
            int d;
            if (TermMap.TryGetValue(termKey.Trim(), out d)) return d;

            // Fallback: trailing digits of the key.
            string k = termKey.Trim();
            int i = k.Length;
            while (i > 0 && char.IsDigit(k[i - 1])) i--;
            if (i < k.Length && int.TryParse(k.Substring(i), out d)) return d;
            return null;
        }

        public static bool IsForeignCurrency(string currency)
        {
            return !string.IsNullOrWhiteSpace(currency)
                && !string.Equals(currency.Trim(), LocalCurrency, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsStandardAud(int? days)
        {
            if (!days.HasValue) return false;
            return Array.IndexOf(StandardAudDays, days.Value) >= 0;
        }

        /// <summary>Classify one PO. Returns None when the PO is compliant.</summary>
        public static PordCategory Categorise(string currency, string poTermKey, string bpTermKey)
        {
            int? poDays = TermDays(poTermKey);

            if (IsForeignCurrency(currency))
            {
                return (poDays.HasValue && poDays.Value < ForeignCurrencyMinDays)
                    ? PordCategory.ForeignCurrency
                    : PordCategory.None;
            }

            bool nonStandard = !IsStandardAud(poDays);
            bool overridden  = !string.Equals((poTermKey ?? "").Trim(), (bpTermKey ?? "").Trim(),
                                              StringComparison.OrdinalIgnoreCase);

            if (nonStandard && overridden) return PordCategory.NonStandardAndOverride;
            if (nonStandard)               return PordCategory.NonStandard;
            if (overridden)                return PordCategory.Override;
            return PordCategory.None;
        }

        public static string CategoryLabel(PordCategory c)
        {
            switch (c)
            {
                case PordCategory.NonStandard:            return "Non-standard";
                case PordCategory.Override:               return "Override";
                case PordCategory.NonStandardAndOverride: return "Non-standard + override";
                case PordCategory.ForeignCurrency:        return "Foreign currency < 14 days";
                default:                                  return "Compliant";
            }
        }

        public static string CategoryDescription(PordCategory c)
        {
            switch (c)
            {
                case PordCategory.NonStandard:
                    return "PO terms are not 5 or 20 days, and the BP master has the same non-standard terms. The BP master record needs correcting.";
                case PordCategory.Override:
                    return "PO terms are 5 or 20 days but differ from the BP master, for example 5 days on a supplier that is not PEPPOL-enabled.";
                case PordCategory.NonStandardAndOverride:
                    return "PO terms are not 5 or 20 days, so they differ from the BP master's standard terms.";
                case PordCategory.ForeignCurrency:
                    return "Foreign-currency PO with payment terms shorter than 14 days. DFG cannot reliably pay FX within 5 days.";
                default:
                    return "";
            }
        }

        public static string CategoryCss(PordCategory c)
        {
            switch (c)
            {
                case PordCategory.NonStandard:            return "cat-ns";
                case PordCategory.Override:               return "cat-ov";
                case PordCategory.NonStandardAndOverride: return "cat-both";
                case PordCategory.ForeignCurrency:        return "cat-fx";
                default:                                  return "";
            }
        }

        public static string CategoryKey(PordCategory c)
        {
            switch (c)
            {
                case PordCategory.NonStandard:            return "ns";
                case PordCategory.Override:               return "ov";
                case PordCategory.NonStandardAndOverride: return "both";
                case PordCategory.ForeignCurrency:        return "fx";
                default:                                  return "";
            }
        }

        /// <summary>5-day terms are standard only for PEPPOL e-invoicing suppliers.</summary>
        public static bool IsPeppolTerm(int? days)
        {
            return days.HasValue && days.Value == 5;
        }

        public static string TermLabel(string key, int? days)
        {
            if (string.IsNullOrWhiteSpace(key)) return "—";
            return days.HasValue
                ? key + " · " + days.Value.ToString(CultureInfo.InvariantCulture) + "d"
                : key;
        }

        public static string ResponseLabel(string response)
        {
            switch (response ?? "")
            {
                case PordResponse.Amend:      return "Will amend PO terms";
                case PordResponse.Reason:     return "Valid reason";
                case PordResponse.Reassign:   return "Not mine – reassign";
                case PordResponse.NoResponse: return "No response";
                default:                      return "Awaiting response";
            }
        }
    }
}
