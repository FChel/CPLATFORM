using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace Prepayment.Web.Services.ExcelImport
{
    /// <summary>
    /// Parses the real Excel workbook (Prepayment Dashboard_2026.xlsx) into an <see cref="PPMImportDataset"/>.
    /// This is the C# port of ReferenceFile/_gen_seed.py — same sheets, same mapping, same masters.
    /// Sheets: "PO Commitment (Aligned)", "Invoice (Aligned)", "GL_Listing".
    /// </summary>
    public static class PPMExcelParser
    {
        private const string ReconPeriod = "2026/06";
        private const int ReconFy = 2026, ReconFp = 6;

        // TODO: EPPlus 5+ requires a LicenseContext to be set before any ExcelPackage is created,
        // or it throws LicenseException in debug mode. Whether this deployment qualifies for the
        // free PolyForm Noncommercial license or requires a purchased commercial license is a
        // licensing decision, not a code decision — do not hardcode LicenseContext here once that
        // decision is made. Configure it externally via the "EPPlus:ExcelPackage.LicenseContext"
        // appSettings key or the EPPlusLicenseContext environment variable instead.

        private class GlMasterEntry
        {
            public string Acct;
            public string Desc;
            public GlMasterEntry(string acct, string desc) { Acct = acct; Desc = desc; }
        }

        // The fixed prepayment GL master (514xxx), descriptions per the real feed.
        private static readonly GlMasterEntry[] GlMaster =
        {
            new GlMasterEntry("514004", "Prepayments - Current / Operational"),
            new GlMasterEntry("514007", "Prepayments - Non-current / Operational"),
            new GlMasterEntry("514008", "Prepayments - Non-current / Capital"),
            new GlMasterEntry("514100", "Prepayments - Current / Operational"),
            new GlMasterEntry("514101", "Prepayments - Current / Capital"),
            new GlMasterEntry("514102", "Prepayments - Lease / Current Portion"),
            new GlMasterEntry("514103", "Prepayments - Lease / Current"),
            new GlMasterEntry("514104", "Prepayments - Lease / Non-current"),
            new GlMasterEntry("514107", "Prepayments - Lease / Non-current Portion"),
            new GlMasterEntry("514109", "Prepayments - Lease / Pre-commencement"),
            new GlMasterEntry("514110", "Prepayments - Non FMS / Non-current"),
        };

        public static PPMImportDataset Parse(Stream xlsxStream)
        {
            var ds = new PPMImportDataset();
            using (var wb = new ExcelPackage(xlsxStream))
            {
                var po = ReadSheet(wb, "PO Commitment (Aligned)");
                var inv = ReadSheet(wb, "Invoice (Aligned)");
                var gl = ReadSheet(wb, "GL_Listing");

                BuildGls(ds);
                var groupProgram = BuildGroups(ds, po, inv);   // also seeds groups + users
                BuildManagers(ds, po, inv);
                BuildVendors(ds, po, inv);
                BuildPurchaseOrders(ds, po, inv);
                BuildLines(ds, po, inv);
                BuildInvoices(ds, inv);
                BuildGlBalances(ds, gl, groupProgram);
            }
            return ds;
        }

        // ── sheet reading ────────────────────────────────────────────────────────
        private static List<Dictionary<string, object>> ReadSheet(ExcelPackage wb, string name)
        {
            var ws = wb.Workbook.Worksheets.FirstOrDefault(w => w.Name == name);
            if (ws == null) throw new ApplicationException("Sheet not found: '" + name + "'. Is this the correct workbook?");

            var rows = new List<Dictionary<string, object>>();
            var used = ws.Dimension;
            if (used == null) return rows;

            int startRow = used.Start.Row, endRow = used.End.Row;
            int startCol = used.Start.Column, endCol = used.End.Column;

            var headers = new List<string>();
            for (int col = startCol; col <= endCol; col++)
            {
                object headerVal = ws.Cells[startRow, col].Value;
                headers.Add((headerVal != null ? headerVal.ToString() : "").Trim());
            }

            for (int r = startRow + 1; r <= endRow; r++)
            {
                var rowValues = new object[endCol - startCol + 1];
                bool allEmpty = true;
                for (int col = startCol; col <= endCol; col++)
                {
                    var v = ws.Cells[r, col].Value;
                    if (v != null) allEmpty = false;
                    rowValues[col - startCol] = v;
                }
                if (allEmpty) continue;

                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count && i < rowValues.Length; i++)
                {
                    var v = rowValues[i];
                    object val;
                    if (v == null) val = null;
                    else if (v is DateTime) val = (DateTime)v;
                    else if (v is double) val = (double)v;
                    else val = v.ToString().Trim();
                    if (!dict.ContainsKey(headers[i])) dict[headers[i]] = val;
                }
                rows.Add(dict);
            }
            return rows;
        }

        // ── helpers (mirror the Python generator) ─────────────────────────────────
        private static string Str(object v)
        {
            if (v == null) return null;
            if (v is DateTime) return ((DateTime)v).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (v is double) { double d = (double)v; return (d == Math.Floor(d) ? ((long)d).ToString() : d.ToString(CultureInfo.InvariantCulture)); }
            var s = v.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        // Normalise any non-ASCII (mojibake dashes etc.) to '-'.
        private static string Clean(object v)
        {
            var s = Str(v);
            if (s == null) return null;
            var chars = s.Select(ch => ch < 128 ? ch : '-').ToArray();
            var t = new string(chars);
            while (t.Contains("- -")) t = t.Replace("- -", "-");
            return t;
        }

        private static long? IntId(object v)
        {
            var s = Str(v);
            if (s == null) return null;
            long n;
            return long.TryParse(s.Split('.')[0], NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : (long?)null;
        }

        private static string IntIdStr(object v)
        {
            long? id = IntId(v);
            return id.HasValue ? id.Value.ToString() : null;
        }

        private static decimal? Num(object v)
        {
            if (v == null) return null;
            if (v is double) return (decimal)(double)v;
            decimal n2;
            return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n2) ? n2 : (decimal?)null;
        }

        private static string DateStr(object v)
        {
            var s = Str(v);
            if (s == null) return null;
            s = s.Length >= 10 ? s.Substring(0, 10) : s;
            return s.Replace(".", "-");
        }

        private static string ShortProg(string prog)
        {
            if (string.IsNullOrWhiteSpace(prog)) return "Defence";
            var ascii = new string(prog.Select(ch => ch < 128 ? ch : '-').ToArray()).Trim();
            var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(ascii.ToLowerInvariant());
            return title.Length > 60 ? title.Substring(0, 60) : title;
        }

        // ── GL master + spec-aligned classification ───────────────────────────────
        private static void BuildGls(PPMImportDataset ds)
        {
            foreach (var entry in GlMaster)
            {
                var acct = entry.Acct;
                var desc = entry.Desc;
                var classification = Classify(desc);
                ds.Gls.Add(new PPMGlRow { GlAccount = acct, GlDescription = desc, AssetClassification = classification.Asset, ExpenditureType = classification.Exp, AasbReference = classification.Aasb });
            }
        }

        private class ClassifyResult
        {
            public string Asset;
            public string Exp;
            public string Aasb;
        }

        /// <summary>Spec 2.5 / Table 4 accounting rules: Lease→AASB 16 (pre-commencement→16 para 24, Non-current); Capital→101/116; Operational→101.</summary>
        private static ClassifyResult Classify(string desc)
        {
            var d = desc.ToLowerInvariant();
            string asset = d.Contains("non-current") ? "Non-current" : "Current";
            string exp, aasb;
            if (d.Contains("lease") || d.Contains("pre-commencement"))
            {
                exp = "Lease";
                if (d.Contains("pre-commencement")) { asset = "Non-current"; aasb = "AASB 16 para 24"; }
                else aasb = "AASB 16";
            }
            else if (d.Contains("capital")) { exp = "Capital"; aasb = "AASB 101 / AASB 116"; }
            else { exp = "Operational"; aasb = "AASB 101"; }
            return new ClassifyResult { Asset = asset, Exp = exp, Aasb = aasb };
        }

        // ── groups (+ per-program preparer/approver users) ────────────────────────
        private static Dictionary<string, string> BuildGroups(
            PPMImportDataset ds, List<Dictionary<string, object>> po, List<Dictionary<string, object>> inv)
        {
            // group code -> name
            var groups = new Dictionary<string, string>();
            foreach (var r in po) AddGroup(groups, Str(r.GetValueOrDefault("DELIVERY_GROUP_CODE")), Clean(r.GetValueOrDefault("DELIVERY_GROUP_NAME")));
            foreach (var r in inv) AddGroup(groups, Str(r.GetValueOrDefault("DELIVERY_GROUP_CODE")), Clean(r.GetValueOrDefault("DELIVERY_GROUP_NAME")));

            // group code -> representative program (for a per-group preparer/approver)
            var groupProgram = new Dictionary<string, string>();
            foreach (var r in po)
            {
                var gc = Str(r.GetValueOrDefault("DELIVERY_GROUP_CODE"));
                var prog = Clean(r.GetValueOrDefault("DELIVERY_MGR_PROGRAM")) ?? Clean(r.GetValueOrDefault("CAPABILITY_MGR_PROGRAM"));
                if (gc != null && prog != null && !groupProgram.ContainsKey(gc)) groupProgram[gc] = prog;
            }
            foreach (var r in inv)
            {
                var gc = Str(r.GetValueOrDefault("DELIVERY_GROUP_CODE"));
                var prog = Clean(r.GetValueOrDefault("DELIVERY_MANAGER_PROGRAM")) ?? Clean(r.GetValueOrDefault("CAPABILITY_MANAGER_PROGRAM"));
                if (gc != null && prog != null && !groupProgram.ContainsKey(gc)) groupProgram[gc] = prog;
            }

            // user 1 = admin; then a Preparer + Approver user per distinct program
            ds.Users.Add(new PPMUserRow { Id = 1, WindowsAccount = @"ADVITIYA\nihal.mali", DisplayName = "J. Harrison", RoleName = "Admin" });
            var progPreparerId = new Dictionary<string, int>();
            var progApproverId = new Dictionary<string, int>();
            int uid = 2;
            foreach (var prog in groupProgram.Values.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                var nm = ShortProg(prog);
                int pid = uid, aid = uid + 1;
                progPreparerId[prog] = pid;
                progApproverId[prog] = aid;
                ds.Users.Add(new PPMUserRow { Id = pid, WindowsAccount = @"ADVITIYA\prep" + pid, DisplayName = nm + " Preparer", RoleName = "FinancePreparer" });
                ds.Users.Add(new PPMUserRow { Id = aid, WindowsAccount = @"ADVITIYA\appr" + aid, DisplayName = nm + " Approver", RoleName = "FinanceApprover" });
                uid += 2;
            }

            foreach (var kv in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int prep = 1, appr = 1;
                string prog;
                int pv, av;
                if (groupProgram.TryGetValue(kv.Key, out prog) && progPreparerId.TryGetValue(prog, out pv) && progApproverId.TryGetValue(prog, out av)) { prep = pv; appr = av; }
                ds.Groups.Add(new PPMGroupRow { Code = kv.Key, Name = kv.Value, PreparerUserId = prep, ApproverUserId = appr });
            }
            return groupProgram;
        }

        private static void AddGroup(Dictionary<string, string> g, string code, string name)
        {
            if (!string.IsNullOrWhiteSpace(code) && !g.ContainsKey(code)) g[code] = name;
        }

        private class ManagerDescProg
        {
            public string Desc;
            public string Prog;
        }

        private static void AddManager(Dictionary<long, ManagerDescProg> m, object id, object desc, object prog)
        {
            var mid = IntId(id);
            if (mid.HasValue && !m.ContainsKey(mid.Value)) m[mid.Value] = new ManagerDescProg { Desc = Clean(desc), Prog = Clean(prog) };
        }

        // ── managers (PO + invoice; dedupe by int id) ─────────────────────────────
        private static void BuildManagers(PPMImportDataset ds, List<Dictionary<string, object>> po, List<Dictionary<string, object>> inv)
        {
            var m = new Dictionary<long, ManagerDescProg>();
            foreach (var r in po)
            {
                AddManager(m, r.GetValueOrDefault("CAPABILITY_MGR_ID"), r.GetValueOrDefault("CAPABILITY_MGR_DESC"), r.GetValueOrDefault("CAPABILITY_MGR_PROGRAM"));
                AddManager(m, r.GetValueOrDefault("DELIVERY_MGR_ID"), r.GetValueOrDefault("DELIVERY_MGR_NAME"), r.GetValueOrDefault("DELIVERY_MGR_PROGRAM"));
            }
            foreach (var r in inv)
            {
                AddManager(m, r.GetValueOrDefault("CAPABILITY_MANAGER"), r.GetValueOrDefault("CAPABILITY_MANAGER_DESC"), r.GetValueOrDefault("CAPABILITY_MANAGER_PROGRAM"));
                AddManager(m, r.GetValueOrDefault("DELIVERY_MANAGER"), r.GetValueOrDefault("DELIVERY_MANAGER_DESC"), r.GetValueOrDefault("DELIVERY_MANAGER_PROGRAM"));
            }
            foreach (var kv in m.OrderBy(x => x.Key))
                ds.Managers.Add(new PPMManagerRow { Id = kv.Key, ManagerDesc = kv.Value.Desc, Program = kv.Value.Prog });
        }

        // ── vendors ───────────────────────────────────────────────────────────────
        private static void BuildVendors(PPMImportDataset ds, List<Dictionary<string, object>> po, List<Dictionary<string, object>> inv)
        {
            var v = new Dictionary<string, string>();
            foreach (var r in po) { var id = IntId(r.GetValueOrDefault("VENDOR_ID")); if (id.HasValue && !v.ContainsKey(id.Value.ToString())) v[id.Value.ToString()] = Clean(r.GetValueOrDefault("VENDOR_NAME")); }
            foreach (var r in inv) { var id = IntId(r.GetValueOrDefault("VEND_NO")); if (id.HasValue && !v.ContainsKey(id.Value.ToString())) v[id.Value.ToString()] = Clean(r.GetValueOrDefault("VEND_NAME")); }
            foreach (var kv in v.OrderBy(x => long.Parse(x.Key)))
                ds.Vendors.Add(new PPMVendorRow { VendorCode = kv.Key, VendorName = kv.Value });
        }

        // ── purchase orders (PO-sheet headers + invoice-only PO stubs) ────────────
        private static readonly HashSet<string> _existingPo = new HashSet<string>();

        private static void BuildPurchaseOrders(PPMImportDataset ds, List<Dictionary<string, object>> po, List<Dictionary<string, object>> inv)
        {
            _existingPo.Clear();
            // PO-sheet: one header per distinct DOC_NUMBER
            var seen = new HashSet<string>();
            foreach (var r in po)
            {
                var pn = Str(r.GetValueOrDefault("DOC_NUMBER"));
                if (pn == null || !seen.Add(pn)) continue;
                _existingPo.Add(pn);
                ds.PurchaseOrders.Add(new PPMPoRow
                {
                    PoNumber = pn,
                    VendorCode = IntIdStr(r.GetValueOrDefault("VENDOR_ID")),
                    GroupCode = Str(r.GetValueOrDefault("DELIVERY_GROUP_CODE")),
                    Wbs = Clean(r.GetValueOrDefault("WBS_ELEMENT")),
                    TotalCommitment = Num(r.GetValueOrDefault("TOTAL_COMMITMENT")),
                    CurrentCommitment = Num(r.GetValueOrDefault("CURRENT_COMMITMENT")),
                    CapexOpex = Str(r.GetValueOrDefault("CAPEX_OPEX")),
                    CapabilityMgrId = IntId(r.GetValueOrDefault("CAPABILITY_MGR_ID")),
                    DeliveryMgrId = IntId(r.GetValueOrDefault("DELIVERY_MGR_ID")),
                    GrIndicator = Str(r.GetValueOrDefault("GR_INDICATOR")),
                    IrIndicator = Str(r.GetValueOrDefault("IR_INDICATOR")),
                    ProcessControl = Clean(r.GetValueOrDefault("PROCESS_CONTROL")),
                    SourceSystem = Str(r.GetValueOrDefault("SOURCE_SYSTEM")),
                    PoDate = DateStr(r.GetValueOrDefault("EINDT")),
                });
            }
            // invoice-only PO stubs
            var stubSeen = new HashSet<string>();
            foreach (var r in inv)
            {
                var pn = Str(r.GetValueOrDefault("PO_NO"));
                if (pn == null || pn == "0" || _existingPo.Contains(pn) || !stubSeen.Add(pn)) continue;
                ds.PurchaseOrders.Add(new PPMPoRow
                {
                    PoNumber = pn,
                    VendorCode = IntIdStr(r.GetValueOrDefault("VEND_NO")),
                    GroupCode = Str(r.GetValueOrDefault("DELIVERY_GROUP_CODE")),
                    Wbs = Clean(r.GetValueOrDefault("WBS_ELEMENT")),
                    TotalCommitment = 0,
                    CapexOpex = Str(r.GetValueOrDefault("CAPEX_OPEX")),
                    CapabilityMgrId = IntId(r.GetValueOrDefault("CAPABILITY_MANAGER")),
                    DeliveryMgrId = IntId(r.GetValueOrDefault("DELIVERY_MANAGER")),
                    SourceSystem = Str(r.GetValueOrDefault("SOURCE_SYSTEM")),
                    PoDate = DateStr(r.GetValueOrDefault("POST_DATE")),
                });
            }
        }

        // ── delivery lines (PO-sheet lines + flagged stubs for invoice-only POs) ──
        private static void BuildLines(PPMImportDataset ds, List<Dictionary<string, object>> po, List<Dictionary<string, object>> inv)
        {
            var seen = new HashSet<string>();
            foreach (var r in po)
            {
                var pn = Str(r.GetValueOrDefault("DOC_NUMBER"));
                if (pn == null) continue;
                int item = (int)(IntId(r.GetValueOrDefault("DOC_ITEM")) ?? 0);
                int acct = (int)(IntId(r.GetValueOrDefault("ACCT_ASSIGN_NUMBER")) ?? 1);
                if (!seen.Add(pn + "|" + item + "|" + acct)) continue;
                var hasPrepay = string.Equals(Str(r.GetValueOrDefault("HAS_PREPAYMENT")), "Yes", StringComparison.OrdinalIgnoreCase);
                var desc = Clean(r.GetValueOrDefault("ERP_GL_DESCRIPTION"));
                ds.Lines.Add(new PPMLineRow
                {
                    PoNumber = pn, LineNumber = item, AcctAssignNumber = acct,
                    Description = desc, GlAccount = IntIdStr(r.GetValueOrDefault("ERP_GL_ACCOUNT")),
                    GlDescription = desc, Wbs = Clean(r.GetValueOrDefault("WBS_ELEMENT")),
                    WbsDescription = Clean(r.GetValueOrDefault("WBS_DESCRIPTION")),
                    CapexOpex = Str(r.GetValueOrDefault("CAPEX_OPEX")), ScheduledDate = DateStr(r.GetValueOrDefault("EINDT")),
                    Quantity = Num(r.GetValueOrDefault("SCHEDULE_QUANTITY")), OpenQuantity = Num(r.GetValueOrDefault("OPEN_QUANTITY")),
                    LineValue = Num(r.GetValueOrDefault("TOTAL_COMMITMENT")), Flag = hasPrepay ? "Prepayment" : "Pending",
                });
            }
            // stub lines for invoice-only POs (flagged Prepayment so they surface on Tab 2)
            var lineStub = new HashSet<string>();
            foreach (var r in inv)
            {
                var pn = Str(r.GetValueOrDefault("PO_NO"));
                if (pn == null || pn == "0" || _existingPo.Contains(pn)) continue;
                int item = (int)(IntId(r.GetValueOrDefault("PO_ITEM")) ?? 10);
                if (!lineStub.Add(pn + "|" + item)) continue;
                var desc = Clean(r.GetValueOrDefault("WBS_DESCRIPTION")) ?? Clean(r.GetValueOrDefault("CASH_GL_DESCRIPTION"));
                ds.Lines.Add(new PPMLineRow
                {
                    PoNumber = pn, LineNumber = item, AcctAssignNumber = 1,
                    Description = desc, GlAccount = IntIdStr(r.GetValueOrDefault("CASH_GL_ACCOUNT")),
                    GlDescription = Clean(r.GetValueOrDefault("CASH_GL_DESCRIPTION")), Wbs = Clean(r.GetValueOrDefault("WBS_ELEMENT")),
                    WbsDescription = Clean(r.GetValueOrDefault("WBS_DESCRIPTION")), CapexOpex = Str(r.GetValueOrDefault("CAPEX_OPEX")),
                    ScheduledDate = DateStr(r.GetValueOrDefault("POST_DATE")), Flag = "Prepayment",
                });
            }
        }

        // ── invoices (dedupe by DOC_NO; FX when LC<>DC) ───────────────────────────
        private static void BuildInvoices(PPMImportDataset ds, List<Dictionary<string, object>> inv)
        {
            var seen = new HashSet<string>();
            foreach (var r in inv)
            {
                var no = Str(r.GetValueOrDefault("DOC_NO"));
                if (no == null || !seen.Add(no)) continue;
                var pn = Str(r.GetValueOrDefault("PO_NO"));
                bool hasPo = pn != null && pn != "0";
                var lc = Num(r.GetValueOrDefault("GL_AMOUNT_LC"));
                var dc = Num(r.GetValueOrDefault("GL_AMOUNT_DC"));
                bool isFx = lc.HasValue && dc.HasValue && lc.Value != dc.Value && dc.Value != 0;
                ds.Invoices.Add(new PPMInvoiceRow
                {
                    InvoiceNo = no,
                    PoNumber = hasPo ? pn : null,
                    LineNumber = (int?)IntId(r.GetValueOrDefault("PO_ITEM")),
                    VendorCode = IntIdStr(r.GetValueOrDefault("VEND_NO")),
                    GlAccount = IntIdStr(r.GetValueOrDefault("PREPAYMENT_GL_ACCOUNT")),
                    PrepaymentGlDesc = Clean(r.GetValueOrDefault("PREPAYMENT_GL_DESCRIPTION")),
                    CashGlAccount = IntIdStr(r.GetValueOrDefault("CASH_GL_ACCOUNT")),
                    CashGlDescription = Clean(r.GetValueOrDefault("CASH_GL_DESCRIPTION")),
                    ProfitCentre = IntIdStr(r.GetValueOrDefault("PROFIT_CENTRE")),
                    ProfitCentreDesc = Clean(r.GetValueOrDefault("PROFIT_CENTRE_DESCRIPTION")),
                    WbsElement = Clean(r.GetValueOrDefault("WBS_ELEMENT")),
                    WbsDescription = Clean(r.GetValueOrDefault("WBS_DESCRIPTION")),
                    CapexOpex = Str(r.GetValueOrDefault("CAPEX_OPEX")),
                    InvoiceDate = DateStr(r.GetValueOrDefault("POST_DATE")),
                    PostFiscalYear = (int?)IntId(r.GetValueOrDefault("POST_FY")),
                    PostFiscalPeriod = (int?)IntId(r.GetValueOrDefault("POST_FP")),
                    PaymentRunDate = DateStr(r.GetValueOrDefault("PAYMENT_RUN_DATE")),
                    Amount = lc ?? 0,
                    AmountDoc = dc,
                    FxRate = isFx ? Math.Round(lc.Value / dc.Value, 8) : (decimal?)null,
                    ForeignCurrency = isFx ? "FX" : null,
                    Description = Clean(r.GetValueOrDefault("WBS_DESCRIPTION")),
                    SetupStatus = hasPo ? "AmortisationNeeded" : "PendingClassification",
                    SourceSystem = Str(r.GetValueOrDefault("SOURCE_SYSTEM")),
                });
            }
        }

        // ── GL balances for reconciliation (aggregate GL_Listing 2026/06) ─────────
        private static readonly Dictionary<string, string> GlAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DEFENCE DIGITAL GROUP", "DDG" }, { "DEFENCE INTELLIGENCE GROUP", "DIG" }, { "AIR FORCE", "Air Force" },
            { "ARMY", "ARMY" }, { "DEFENCE CORPORATE", "DIG" }, { "JOINT CAPABILITIES", "JCG" },
            { "NAVAL SHIPBUILDING AND SUSTAINMENT GROUP", "NSSG" }, { "CAPABILITY ACQUISITION & SUSTAINMENT", "Air Force" },
            { "GUIDED WEAPONS & EXPLOSIVE ORD", "GEWO" }, { "DEFENCE PEOPLE GROUP- PEOPLE STRATEGY", "DPG - PSO" },
            { "DEFENCE PEOPLE GROUP- MILITARY PERSONNEL", "DPG - MPO" }, { "ASSOCIATE SECRETARY GROUP", "ASG" },
            { "STRATEGY POLICY & INDUSTRY GROUP", "SP&I" }, { "ADFHQ", "ADFHQ" },
        };

        private struct BalCell
        {
            public decimal Closing, Deb, Cred;
        }

        private static void BuildGlBalances(PPMImportDataset ds, List<Dictionary<string, object>> gl, Dictionary<string, string> groupProgram)
        {
            // inverse: program(upper) -> group code
            var progToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in groupProgram)
                if (!string.IsNullOrWhiteSpace(kv.Value) && !progToGroup.ContainsKey(kv.Value.Trim().ToUpperInvariant()))
                    progToGroup[kv.Value.Trim().ToUpperInvariant()] = kv.Key;

            var groupCodes = new HashSet<string>(ds.Groups.Select(g => g.Code));
            var glSet = new HashSet<string>(GlMaster.Select(x => x.Acct));
            Func<string, string> resolve = delegate(string prog)
            {
                if (string.IsNullOrWhiteSpace(prog)) return null;
                var key = prog.Trim().ToUpperInvariant();
                string c;
                if (GlAliases.TryGetValue(key, out c)) return c;
                string c2;
                return progToGroup.TryGetValue(key, out c2) ? c2 : null;
            };

            var bal = new Dictionary<string, BalCell>();
            foreach (var r in gl)
            {
                if ((int?)IntId(r.GetValueOrDefault("FISCAL_YEAR")) != ReconFy || (int?)IntId(r.GetValueOrDefault("FISCL_PERIOD")) != ReconFp) continue;
                var glAcctId = IntId(r.GetValueOrDefault("GL_ACCOUNT"));
                var glAcct = glAcctId.HasValue ? glAcctId.Value.ToString() : null;
                if (glAcct == null || !glSet.Contains(glAcct)) continue;
                var grp = resolve(Str(r.GetValueOrDefault("DELIVERY_MANAGER")));
                if (grp == null || !groupCodes.Contains(grp)) continue;
                var amt = Num(r.GetValueOrDefault("AMOUNT_COMPANY_CODE_CURRENCY")) ?? 0;
                var dc = Str(r.GetValueOrDefault("DEBIT_CREDIT"));
                var key = grp + "|" + glAcct;
                BalCell cell;
                bal.TryGetValue(key, out cell);
                if (dc == "S") { cell.Closing += amt; cell.Deb += amt; }
                else { cell.Closing -= amt; cell.Cred += amt; }
                bal[key] = cell;
            }
            foreach (var kv in bal.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var parts = kv.Key.Split('|');
                ds.GlBalances.Add(new PPMGlBalanceRow
                {
                    GroupCode = parts[0], GlAccount = parts[1], FiscalYear = ReconFy, FiscalPeriod = ReconFp,
                    Closing = Math.Round(kv.Value.Closing, 2), Debit = Math.Round(kv.Value.Deb, 2), Credit = Math.Round(kv.Value.Cred, 2),
                });
            }
        }
    }
}
