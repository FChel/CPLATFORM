using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CPlatform.PORD
{
    /// <summary>
    /// Parser for the monthly BODS Non-Standard Payment Terms CSV.
    ///
    /// Columns are matched by normalised header name (case, spaces and
    /// punctuation ignored), so "PO Payment Term", "PO_PAYMENT_TERM" and
    /// "po payment term" all resolve. Required columns are the business
    /// requirement's report output; "Proposed" columns are the additions the
    /// spec review asked for (grouping, POC routing, FX rule). Missing
    /// proposed columns are warnings, not errors, so a spec-v1 file still
    /// previews.
    ///
    /// The parser never writes anything. Categorisation uses PORDRules so the
    /// preview shows exactly what a commit would flag.
    /// </summary>
    public static class PORDCsvParser
    {
        public sealed class Column
        {
            public string Key, Label; public bool Required; public string[] Aliases;
            public Column(string key, string label, bool required, params string[] aliases)
            { Key = key; Label = label; Required = required; Aliases = aliases; }
        }

        public static readonly Column[] Columns = new[]
        {
            new Column("CoCode",        "Co Code",           true,  "companycode", "bukrs"),
            new Column("PoNumber",      "PO Number",         true,  "ponumber", "purchaseorder", "ebeln"),
            new Column("PoTerm",        "PO Payment Term",   true,  "popaymentterms", "zterm"),
            new Column("BpNumber",      "BP Number",         true,  "businesspartner", "vendor", "lifnr"),
            new Column("BpName",        "BP Name",           true,  "vendorname", "suppliername"),
            new Column("BpTerm",        "BP Payment Term",   true,  "bppaymentterms", "vendorpaymentterm"),
            new Column("Ordered",       "Ordered",           true,  "orderedvalue"),
            new Column("Delivered",     "Delivered",         true,  "deliveredvalue"),
            new Column("StillToDeliver","Still to Deliver",  true,  "stilltobedelivered", "openvalue"),
            new Column("Invoiced",      "Invoiced",          true,  "invoicedvalue"),
            new Column("PurchGroup",    "Purchasing Group",  true,  "purchgroup", "ekgrp"),
            new Column("DeliveryDate",  "Delivery Date",     true,  "deliverydate", "eindt"),
            new Column("PoCreator",     "PO Creator",        true,  "createdby", "ernam"),
            // Proposed additions from the spec review
            new Column("Currency",      "Currency",          false, "pocurrency", "waers"),
            new Column("DmProgram",     "DM Program",        false, "deliverymanagerprogram"),
            new Column("DeliveryManager","Delivery Manager", false, "costcentre", "dmcostcentre"),
            new Column("PocEmail",      "POC Email",         false, "creatoremail", "pocemail")
        };

        public sealed class ParsedRow
        {
            public int LineNo;
            public Dictionary<string, string> Values = new Dictionary<string, string>();
            public PordCategory Category;
            public string Error;
            public string Get(string key) { string v; return Values.TryGetValue(key, out v) ? v : null; }
        }

        public sealed class Result
        {
            public string FileName;
            public string Error;
            public List<string> Headers = new List<string>();
            public List<Column> Found = new List<Column>();
            public List<Column> MissingRequired = new List<Column>();
            public List<Column> MissingProposed = new List<Column>();
            public List<ParsedRow> Rows = new List<ParsedRow>();
            public int Compliant, Failed;
            public Dictionary<PordCategory, int> ByCategory = new Dictionary<PordCategory, int>();
            public bool HeaderOk { get { return Error == null && MissingRequired.Count == 0; } }
            public int Flagged { get { return ByCategory.Values.Sum(); } }
        }

        public static Result Parse(Stream stream, string fileName)
        {
            var res = new Result { FileName = fileName };
            string text;
            using (var sr = new StreamReader(stream, Encoding.UTF8, true)) text = sr.ReadToEnd();

            var records = SplitCsv(text);
            if (records.Count == 0) { res.Error = "The file is empty."; return res; }

            res.Headers = records[0].Select(h => h.Trim()).ToList();
            var index = new Dictionary<string, int>();
            for (int i = 0; i < res.Headers.Count; i++)
            {
                string n = Norm(res.Headers[i]);
                foreach (var c in Columns)
                {
                    if (index.ContainsKey(c.Key)) continue;
                    if (n == Norm(c.Label) || n == Norm(c.Key) || c.Aliases.Any(a => n == Norm(a))) { index[c.Key] = i; break; }
                }
            }
            foreach (var c in Columns)
            {
                if (index.ContainsKey(c.Key)) res.Found.Add(c);
                else if (c.Required) res.MissingRequired.Add(c);
                else res.MissingProposed.Add(c);
            }
            if (res.MissingRequired.Count > 0) return res;

            for (int r = 1; r < records.Count; r++)
            {
                var rec = records[r];
                if (rec.Count == 1 && string.IsNullOrWhiteSpace(rec[0])) continue;
                var row = new ParsedRow { LineNo = r + 1 };
                foreach (var kv in index)
                    row.Values[kv.Key] = kv.Value < rec.Count ? rec[kv.Value].Trim() : "";

                if (string.IsNullOrEmpty(row.Get("PoNumber"))) row.Error = "PO Number is blank.";
                else if (string.IsNullOrEmpty(row.Get("PoTerm"))) row.Error = "PO Payment Term is blank.";

                if (row.Error != null) { res.Failed++; res.Rows.Add(row); continue; }

                string ccy = row.Get("Currency");
                if (string.IsNullOrEmpty(ccy)) ccy = PORDRules.LocalCurrency;
                row.Category = PORDRules.Categorise(ccy, row.Get("PoTerm"), row.Get("BpTerm"));
                if (row.Category == PordCategory.None) res.Compliant++;
                else
                {
                    int n; res.ByCategory.TryGetValue(row.Category, out n); res.ByCategory[row.Category] = n + 1;
                }
                res.Rows.Add(row);
            }
            return res;
        }

        private static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>RFC 4180 splitter: quoted fields, doubled quotes, embedded newlines; comma or tab delimited.</summary>
        public static List<List<string>> SplitCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            int firstLineEnd = text.IndexOf('\n');
            string firstLine = firstLineEnd < 0 ? text : text.Substring(0, firstLineEnd);
            char delim = firstLine.Count(c => c == '\t') > firstLine.Count(c => c == ',') ? '\t' : ',';

            var row = new List<string>();
            var field = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQ)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQ = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"') inQ = true;
                else if (c == delim) { row.Add(field.ToString()); field.Length = 0; }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(field.ToString()); field.Length = 0; rows.Add(row); row = new List<string>(); }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        /// <summary>A sample extract in the proposed layout, for the Load page's "Download template".</summary>
        public static string SampleCsv(IList<PordPo> pos)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Columns.Select(c => c.Label)));
            foreach (var p in pos)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    p.CompanyCode, p.PoNumber, p.PoTermKey, p.BpNumber, Q(p.BpName), p.BpTermKey,
                    p.Ordered.ToString("0.00", CultureInfo.InvariantCulture), p.Delivered.ToString("0.00", CultureInfo.InvariantCulture),
                    p.StillToDeliver.ToString("0.00", CultureInfo.InvariantCulture), p.Invoiced.ToString("0.00", CultureInfo.InvariantCulture),
                    p.PurchasingGroup, PORDHelper.Date(p.DeliveryDate), p.PoCreator, p.Currency, Q(p.DmProgram), p.DeliveryManager, p.PocEmail
                }));
            }
            return sb.ToString();
        }

        private static string Q(string s)
        {
            s = s ?? "";
            return (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
