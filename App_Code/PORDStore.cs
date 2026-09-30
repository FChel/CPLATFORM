using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CPlatform.PORD
{
    public enum PordTokenKind { None = 0, AsFin = 1, Poc = 2 }

    /// <summary>
    /// Data contract for the PO Review module. Pages and handlers depend on
    /// this interface only. Two implementations are planned:
    ///
    ///   PordDemoStore  — in-memory, seeded, shared across the app domain.
    ///                    Lets the module be demonstrated on WARATAH with no
    ///                    schema change (SQL digest gate untouched).
    ///   PordSqlStore   — tblPORD_* tables via LPPIHelper-style OLE DB
    ///                    helpers. Built once the extract layout is agreed.
    /// </summary>
    public interface IPordStore
    {
        IList<PordPackage>     GetPackages(bool activeOnly);
        PordPackage            GetPackage(int packageId);
        IList<PordPo>          GetPos(int packageId);
        IList<PordPo>          GetCurrentCyclePos();
        IList<PordPackagePoc>  GetPocs(int packageId);
        PordTokenKind          ResolveToken(string token, out int packageId, out string pocEmail);

        IList<PordAsFinGroup>  GetAsFinGroups();
        bool                   SaveAsFinGroup(string program, string email, string displayName, out string error);
        IList<PordReasonCode>  GetReasonCodes(bool activeOnly);
        PordReasonCode         GetReasonCode(string code);
        IList<PordLoadBatch>   GetBatches();
        IList<PordExclusion>   GetExclusions();
        void                   RevokeExclusion(string poNumber, string user);
        IList<PordResolved>    GetResolved();
        IList<PordBpIssue>     GetBpIssues();
        void                   MarkBpNotified(IList<string> bpNumbers);

        IList<PordSaveResult>  Save(int packageId, string pocEmail, IList<PordSaveRow> rows, string user, out string packageStatus);
        bool                   Finalise(int packageId, string user, out int autoApplied, out string error);
        bool                   Unfinalise(int packageId, string user, out string error);
        int                    MarkSent(IList<int> packageIds, DateTime dueDate, string user);
        int                    CloseFinalised(string user);
        void                   Reset();
    }

    /// <summary>
    /// In-memory demo implementation. Seeded deterministically relative to
    /// today so due dates and "overdue / due soon" states always look live.
    /// State is shared by every viewer and resets on app-pool recycle or via
    /// the dashboard's Reset demo data button.
    /// </summary>
    public sealed class PordDemoStore : IPordStore
    {
        private static readonly PordDemoStore _instance = new PordDemoStore();
        public static PordDemoStore Instance { get { return _instance; } }

        private readonly object _sync = new object();
        private List<PordPackage>    _packages;
        private List<PordPo>         _pos;
        private List<PordPackagePoc> _pocs;
        private List<PordAsFinGroup> _groups;
        private List<PordReasonCode> _reasons;
        private List<PordLoadBatch>  _batches;
        private List<PordExclusion>  _exclusions;
        private List<PordResolved>   _resolved;
        private Dictionary<string, string> _bpStatus;

        private PordDemoStore() { Seed(); }

        public void Reset() { lock (_sync) { Seed(); } }

        // ==================================================================
        // Reads
        // ==================================================================

        public IList<PordPackage> GetPackages(bool activeOnly)
        {
            lock (_sync)
            {
                return _packages
                    .Where(p => !activeOnly || (p.Status != PordStatus.Closed && p.Status != PordStatus.Cancelled))
                    .OrderBy(p => p.DmProgram).ToList();
            }
        }

        public PordPackage GetPackage(int packageId)
        {
            lock (_sync) { return _packages.FirstOrDefault(p => p.PackageID == packageId); }
        }

        public IList<PordPo> GetPos(int packageId)
        {
            lock (_sync)
            {
                return _pos.Where(p => p.PackageID == packageId)
                           .OrderByDescending(p => p.ReviewNbr)
                           .ThenByDescending(p => p.StillToDeliver).ToList();
            }
        }

        public IList<PordPo> GetCurrentCyclePos()
        {
            lock (_sync)
            {
                var ids = new HashSet<int>(_packages.Where(p => p.Status != PordStatus.Closed && p.Status != PordStatus.Cancelled)
                                                    .Select(p => p.PackageID));
                return _pos.Where(p => ids.Contains(p.PackageID)).ToList();
            }
        }

        public IList<PordPackagePoc> GetPocs(int packageId)
        {
            lock (_sync) { return _pocs.Where(p => p.PackageID == packageId).OrderBy(p => p.PocName).ToList(); }
        }

        public PordTokenKind ResolveToken(string token, out int packageId, out string pocEmail)
        {
            packageId = 0; pocEmail = null;
            if (string.IsNullOrWhiteSpace(token)) return PordTokenKind.None;
            string t = token.Trim();
            lock (_sync)
            {
                var pkg = _packages.FirstOrDefault(p => p.Token == t);
                if (pkg != null) { packageId = pkg.PackageID; return PordTokenKind.AsFin; }
                var poc = _pocs.FirstOrDefault(p => p.Token == t);
                if (poc != null) { packageId = poc.PackageID; pocEmail = poc.PocEmail; return PordTokenKind.Poc; }
            }
            return PordTokenKind.None;
        }

        public IList<PordAsFinGroup> GetAsFinGroups()
        {
            lock (_sync) { return _groups.OrderBy(g => g.DmProgram).ToList(); }
        }

        public bool SaveAsFinGroup(string program, string email, string displayName, out string error)
        {
            error = null;
            email = (email ?? "").Trim().ToLowerInvariant();
            displayName = (displayName ?? "").Trim();
            if (email.Length > 0 && !(email.EndsWith("@defence.gov.au") || email.EndsWith(".defence.gov.au")))
            {
                error = "Use a Defence mailbox (@defence.gov.au).";
                return false;
            }
            if (email.Length > 0 && displayName.Length == 0)
            {
                error = "Add a display name so recipients recognise the sender.";
                return false;
            }
            lock (_sync)
            {
                var g = _groups.FirstOrDefault(x => x.DmProgram == program);
                if (g == null) { error = "Unknown program."; return false; }
                g.Email = email.Length == 0 ? null : email;
                g.DisplayName = displayName.Length == 0 ? null : displayName;
            }
            return true;
        }

        public IList<PordReasonCode> GetReasonCodes(bool activeOnly)
        {
            lock (_sync) { return _reasons.Where(r => !activeOnly || r.IsActive).OrderBy(r => r.DisplayOrder).ToList(); }
        }

        public PordReasonCode GetReasonCode(string code)
        {
            lock (_sync) { return _reasons.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)); }
        }

        public IList<PordLoadBatch> GetBatches()
        {
            lock (_sync) { return _batches.OrderByDescending(b => b.LoadedDate).ToList(); }
        }

        public IList<PordExclusion> GetExclusions()
        {
            lock (_sync) { return _exclusions.OrderBy(e => e.IsRevoked).ThenBy(e => e.ExpiryDate).ToList(); }
        }

        public void RevokeExclusion(string poNumber, string user)
        {
            lock (_sync)
            {
                foreach (var e in _exclusions.Where(x => x.PoNumber == poNumber)) e.IsRevoked = true;
            }
        }

        public IList<PordResolved> GetResolved()
        {
            lock (_sync) { return _resolved.OrderByDescending(r => r.VerifiedDate).ToList(); }
        }

        public IList<PordBpIssue> GetBpIssues()
        {
            lock (_sync)
            {
                var list = _pos.Where(p => !PORDRules.IsForeignCurrency(p.Currency)
                                        && !PORDRules.IsStandardAud(p.BpTermDays))
                    .GroupBy(p => p.BpNumber)
                    .Select(g =>
                    {
                        var f = g.First();
                        string st;
                        if (!_bpStatus.TryGetValue(g.Key, out st)) st = "New";
                        return new PordBpIssue
                        {
                            BpNumber = g.Key, BpName = f.BpName, BpTermKey = f.BpTermKey, BpTermDays = f.BpTermDays,
                            OpenPoCount = g.Count(), StillToDeliver = g.Sum(x => x.StillToDeliver), Status = st
                        };
                    })
                    .OrderByDescending(b => b.StillToDeliver).ToList();
                return list;
            }
        }

        public void MarkBpNotified(IList<string> bpNumbers)
        {
            lock (_sync) { foreach (var bp in bpNumbers) _bpStatus[bp] = "Notified"; }
        }

        // ==================================================================
        // Writes
        // ==================================================================

        private static bool IsEditable(string status)
        {
            return status == PordStatus.NotSent || status == PordStatus.Sent || status == PordStatus.InReview;
        }

        public IList<PordSaveResult> Save(int packageId, string pocEmail, IList<PordSaveRow> rows, string user, out string packageStatus)
        {
            var results = new List<PordSaveResult>();
            lock (_sync)
            {
                var pkg = _packages.FirstOrDefault(p => p.PackageID == packageId);
                packageStatus = pkg == null ? "" : pkg.Status;
                if (pkg == null || !IsEditable(pkg.Status))
                {
                    foreach (var r in rows)
                        results.Add(new PordSaveResult { PoID = r.PoID, Ok = false, ErrorCode = "locked",
                            Message = "This package is no longer open for changes." });
                    return results;
                }

                bool anySaved = false;
                foreach (var r in rows)
                {
                    var res = new PordSaveResult { PoID = r.PoID };
                    results.Add(res);
                    var po = _pos.FirstOrDefault(p => p.PoID == r.PoID && p.PackageID == packageId);
                    if (po == null || (pocEmail != null && !string.Equals(po.PocEmail, pocEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        res.ErrorCode = "notInPackage"; res.Message = "This PO is not in your review list."; continue;
                    }
                    if ((r.Version ?? "") != PORDHelper.VersionOf(po.ReviewedDate))
                    {
                        res.ErrorCode = "stale";
                        res.Message = "Someone else updated this PO since you opened the page. Reload to see their changes.";
                        res.Version = PORDHelper.VersionOf(po.ReviewedDate);
                        continue;
                    }

                    string response = (r.Response ?? "").Trim();
                    string reason   = response == PordResponse.Reason ? (r.ReasonCode ?? "").Trim() : null;
                    string comments = (r.Comments ?? "").Trim();
                    string evidence = response == PordResponse.Reason ? (r.EvidenceRef ?? "").Trim() : null;
                    DateTime? target = null;

                    string err = Validate(response, reason, comments, evidence, r.TargetDate, out target);
                    if (err != null) { res.ErrorCode = "validation"; res.Message = err; continue; }

                    bool same = (po.Response ?? "") == response
                             && (po.ReasonCode ?? "") == (reason ?? "")
                             && (po.Comments ?? "") == comments
                             && (po.EvidenceRef ?? "") == (evidence ?? "")
                             && po.TargetDate == target;
                    if (same) { res.Ok = true; res.ErrorCode = "noChange"; res.Version = PORDHelper.VersionOf(po.ReviewedDate); continue; }

                    po.Response    = response.Length == 0 ? null : response;
                    po.ReasonCode  = string.IsNullOrEmpty(reason) ? null : reason;
                    po.Comments    = comments.Length == 0 ? null : comments;
                    po.EvidenceRef = string.IsNullOrEmpty(evidence) ? null : evidence;
                    po.TargetDate  = target;
                    po.ReviewedBy  = user;
                    po.ReviewedDate = DateTime.Now;
                    res.Ok = true;
                    res.Version = PORDHelper.VersionOf(po.ReviewedDate);
                    anySaved = true;
                }

                if (anySaved && pkg.Status == PordStatus.Sent) pkg.Status = PordStatus.InReview;
                packageStatus = pkg.Status;
            }
            return results;
        }

        /// <summary>Server-side validation — the authoritative copy of the rules pord.js shows inline.</summary>
        private string Validate(string response, string reason, string comments, string evidence, string targetRaw, out DateTime? target)
        {
            target = null;
            switch (response)
            {
                case "":
                    return null;
                case PordResponse.Amend:
                    DateTime d;
                    if (!DateTime.TryParseExact((targetRaw ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                        return "Enter the date you expect the PO terms to be amended by.";
                    if (d.Date < DateTime.Today) return "The amendment date cannot be in the past.";
                    target = d.Date;
                    return null;
                case PordResponse.Reason:
                    if (string.IsNullOrEmpty(reason)) return "Choose the reason that applies.";
                    var rc = _reasons.FirstOrDefault(x => x.Code == reason && x.IsActive);
                    if (rc == null) return "That reason is not available.";
                    if (rc.RequiresComments && comments.Length == 0) return "Comments are required for this reason.";
                    if (rc.RequiresEvidence && string.IsNullOrEmpty(evidence)) return "Add the Objective reference for your evidence.";
                    return null;
                case PordResponse.Reassign:
                    if (comments.Length == 0) return "Tell us who owns this PO (name or email) in Comments.";
                    return null;
                default:
                    return "Unknown response.";
            }
        }

        public bool Finalise(int packageId, string user, out int autoApplied, out string error)
        {
            autoApplied = 0; error = null;
            lock (_sync)
            {
                var pkg = _packages.FirstOrDefault(p => p.PackageID == packageId);
                if (pkg == null) { error = "Package not found."; return false; }
                if (pkg.Status != PordStatus.Sent && pkg.Status != PordStatus.InReview)
                {
                    error = "Only a sent package can be finalised."; return false;
                }
                foreach (var po in _pos.Where(p => p.PackageID == packageId))
                {
                    if (!po.IsReviewed)
                    {
                        po.Response = PordResponse.NoResponse;
                        po.ReviewedBy = "System (finalise)";
                        po.ReviewedDate = DateTime.Now;
                        autoApplied++;
                    }
                    else if (po.Response == PordResponse.Reason)
                    {
                        var rc = _reasons.FirstOrDefault(r => r.Code == po.ReasonCode);
                        if (rc != null && rc.CreatesExclusion && !_exclusions.Any(e => e.PoNumber == po.PoNumber && !e.IsRevoked))
                        {
                            _exclusions.Add(new PordExclusion
                            {
                                PoNumber = po.PoNumber, PoTermKey = po.PoTermKey, BpName = po.BpName, DmProgram = po.DmProgram,
                                ReasonCode = po.ReasonCode, EvidenceRef = po.EvidenceRef, GrantedDate = DateTime.Today,
                                ExpiryDate = DateTime.Today.AddMonths(PORDHelper.ExclusionMonths), GrantedBy = user
                            });
                        }
                    }
                }
                pkg.Status = PordStatus.Finalised;
                pkg.FinalisedDate = DateTime.Now;
                pkg.FinalisedBy = user;
            }
            return true;
        }

        public bool Unfinalise(int packageId, string user, out string error)
        {
            error = null;
            lock (_sync)
            {
                var pkg = _packages.FirstOrDefault(p => p.PackageID == packageId);
                if (pkg == null || pkg.Status != PordStatus.Finalised) { error = "Only a finalised package can be reopened."; return false; }
                var pos = _pos.Where(p => p.PackageID == packageId).ToList();
                foreach (var po in pos.Where(p => p.Response == PordResponse.NoResponse))
                {
                    po.Response = null; po.ReviewedBy = null; po.ReviewedDate = null;
                }
                var poNums = new HashSet<string>(pos.Select(p => p.PoNumber));
                _exclusions.RemoveAll(e => poNums.Contains(e.PoNumber) && e.GrantedDate == DateTime.Today);
                pkg.Status = PordStatus.InReview;
                pkg.FinalisedDate = null; pkg.FinalisedBy = null;
            }
            return true;
        }

        public int MarkSent(IList<int> packageIds, DateTime dueDate, string user)
        {
            int n = 0;
            lock (_sync)
            {
                foreach (var pkg in _packages.Where(p => packageIds.Contains(p.PackageID)))
                {
                    if (pkg.Status == PordStatus.NotSent)
                    {
                        pkg.Status = PordStatus.Sent; pkg.SentDate = DateTime.Now; pkg.DueDate = dueDate.Date;
                    }
                    if (pkg.Status == PordStatus.Sent || pkg.Status == PordStatus.InReview)
                    {
                        pkg.LastEmailDate = DateTime.Now; n++;
                    }
                }
            }
            return n;
        }

        public int CloseFinalised(string user)
        {
            int n = 0;
            lock (_sync)
            {
                foreach (var pkg in _packages.Where(p => p.Status == PordStatus.Finalised)) { pkg.Status = PordStatus.Closed; n++; }
            }
            return n;
        }

        // ==================================================================
        // Seed
        // ==================================================================

        private sealed class Bp { public string No, Name, Term; public Bp(string n, string nm, string t) { No = n; Name = nm; Term = t; } }

        private void Seed()
        {
            var rnd = new Random(20260930);
            DateTime today = DateTime.Today;
            DateTime cycleLoad = today.AddDays(-9);

            _reasons = new List<PordReasonCode>
            {
                new PordReasonCode { Code = "VR01", Description = "Demonstrated clear and direct benefit to Defence",
                                     RequiresComments = true, RequiresEvidence = true, CreatesExclusion = true, IsActive = true, DisplayOrder = 1 },
                new PordReasonCode { Code = "VR02", Description = "Legislative requirement",
                                     RequiresComments = true, RequiresEvidence = true, CreatesExclusion = true, IsActive = true, DisplayOrder = 2 },
                new PordReasonCode { Code = "VR03", Description = "Contract entered prior to 1 July 2022 (RMG-417)",
                                     RequiresComments = false, RequiresEvidence = true, CreatesExclusion = true, IsActive = true, DisplayOrder = 3 },
                new PordReasonCode { Code = "VR99", Description = "Other (explain in comments)",
                                     RequiresComments = true, RequiresEvidence = true, CreatesExclusion = false, IsActive = true, DisplayOrder = 9 }
            };

            // Delivery Manager programs → AS Fin (demo mailboxes)
            var programs = new[]
            {
                new { P = "ARMY",      Cc = "D1101", Dm = "Land Capability Delivery",       Mail = "asfin.army",  Name = "AS Fin Army" },
                new { P = "NAVY",      Cc = "D2204", Dm = "Maritime Sustainment",           Mail = "asfin.navy",  Name = "AS Fin Navy" },
                new { P = "AIR FORCE", Cc = "D3310", Dm = "Air Systems Support",            Mail = "asfin.raaf",  Name = "AS Fin Air Force" },
                new { P = "CASG",      Cc = "D4102", Dm = "Commercial & Contracting",       Mail = "asfin.casg",  Name = "AS Fin CASG" },
                new { P = "CIOG",      Cc = "D5020", Dm = "ICT Operations",                 Mail = "asfin.ciog",  Name = "AS Fin CIOG" },
                new { P = "JCG",       Cc = "D6115", Dm = "Joint Logistics",                Mail = "asfin.jcg",   Name = "AS Fin JCG" },
                new { P = "DSTG",      Cc = "D7003", Dm = "Science & Technology Programs",  Mail = (string)null,  Name = (string)null }
            };

            _groups = programs.Select(p => new PordAsFinGroup
            {
                DmProgram = p.P,
                Email = p.Mail == null ? null : p.Mail + "@defence.gov.au",
                DisplayName = p.Name, IsActive = true
            }).ToList();

            var first = new[] { "Alex", "Sam", "Jordan", "Priya", "Liam", "Mei", "Tom", "Aisha", "Hamish", "Grace", "Nikhil", "Chloe", "Ravi", "Zoe", "Ben", "Olivia", "Kai", "Sophie", "Dev", "Isla" };
            var last  = new[] { "Nguyen", "Smith", "Patel", "O'Brien", "Walker", "Chen", "Kelly", "Rahman", "Fraser", "Taylor", "Singh", "Murray", "Iyer", "Brooks", "Hughes", "Lee", "Wright", "Ward", "Das", "Moore" };

            var bps = new List<Bp>
            {
                new Bp("1000210045", "Southern Cross Logistics Pty Ltd", "Z020"),
                new Bp("1000210112", "Harbourline Marine Services",       "Z020"),
                new Bp("1000210387", "Redgum Engineering Group",          "Z030"),
                new Bp("1000210401", "Blue Ridge Aerotech",               "Z020"),
                new Bp("1000210533", "Kestrel Systems Integration",       "Z005"),
                new Bp("1000210678", "Coastal Fuel Distributors",         "Z014"),
                new Bp("1000210702", "Wattle Facilities Management",      "Z020"),
                new Bp("1000210819", "Tasman Defence Training",           "Z045"),
                new Bp("1000210926", "Ironbark Construction Services",    "Z020"),
                new Bp("1000211004", "Pinnacle Cyber Assurance",          "Z005"),
                new Bp("1000211150", "Outback Vehicle Parts Co",          "Z030"),
                new Bp("1000211276", "Meridian Medical Supplies",         "Z020"),
                new Bp("1000211391", "Brindabella Consulting",            "Z020"),
                new Bp("1000211405", "Nullarbor Freight Lines",           "Z007"),
                new Bp("1000211588", "Ember Electronics Australia",       "Z020"),
                new Bp("1000211623", "Sapphire Coast Catering",           "Z010"),
                new Bp("1000211747", "Granite Peak Survey & Mapping",     "Z020"),
                new Bp("1000211872", "Lighthouse Software Services",      "Z005"),
                new Bp("1000211990", "Kookaburra Uniform Supply",         "Z060"),
                new Bp("1000212014", "Stirling Range Munitions Handling", "Z020")
            };
            var fxBps = new List<Bp>
            {
                new Bp("1000300118", "Atlantic Avionics Inc (US)",       "Z030"),
                new Bp("1000300245", "Nordic Sonar AB (SE)",              "Z030"),
                new Bp("1000300372", "Thames Precision Optics Ltd (UK)",  "Z045"),
                new Bp("1000300419", "Aoraki Marine NZ Ltd",              "Z020")
            };
            var fxCcy = new[] { "USD", "EUR", "GBP", "NZD" };

            _packages = new List<PordPackage>();
            _pocs = new List<PordPackagePoc>();
            _pos = new List<PordPo>();
            int pkgId = 3100, poId = 1;
            long poSeq = 4500712000L;

            // Package lifecycle states for the demo cycle, keyed by program.
            var states = new Dictionary<string, string>
            {
                { "ARMY", PordStatus.InReview }, { "NAVY", PordStatus.InReview }, { "AIR FORCE", PordStatus.Sent },
                { "CASG", PordStatus.Finalised }, { "CIOG", PordStatus.Sent }, { "JCG", PordStatus.InReview }, { "DSTG", PordStatus.NotSent }
            };
            var counts = new Dictionary<string, int>
            {
                { "ARMY", 38 }, { "NAVY", 31 }, { "AIR FORCE", 27 }, { "CASG", 22 }, { "CIOG", 14 }, { "JCG", 12 }, { "DSTG", 9 }
            };

            foreach (var prog in programs)
            {
                pkgId++;
                string status = states[prog.P];
                var pkg = new PordPackage
                {
                    PackageID = pkgId, DmProgram = prog.P,
                    Token = "demo-" + prog.P.Replace(" ", "").ToLowerInvariant() + "-asfin",
                    Status = status, CreatedDate = cycleLoad,
                    DueDate = status == PordStatus.NotSent ? today.AddDays(PORDHelper.DefaultDueDays)
                            : prog.P == "AIR FORCE" ? today.AddDays(2)
                            : prog.P == "CIOG" ? today.AddDays(-1)
                            : today.AddDays(5),
                    SentDate = status == PordStatus.NotSent ? (DateTime?)null : cycleLoad.AddDays(1).AddHours(9),
                    LastEmailDate = status == PordStatus.NotSent ? (DateTime?)null : cycleLoad.AddDays(1).AddHours(9)
                };
                if (status == PordStatus.Finalised) { pkg.FinalisedDate = today.AddDays(-2).AddHours(15); pkg.FinalisedBy = "AS Fin CASG"; }
                _packages.Add(pkg);

                // 3–5 POCs per program
                int nPoc = 3 + rnd.Next(3);
                var pocList = new List<PordPackagePoc>();
                for (int i = 0; i < nPoc; i++)
                {
                    string fn = first[rnd.Next(first.Length)], ln = last[rnd.Next(last.Length)];
                    string email = (fn + "." + ln.Replace("'", "")).ToLowerInvariant() + "@defence.gov.au";
                    if (pocList.Any(x => x.PocEmail == email)) { i--; continue; }
                    pocList.Add(new PordPackagePoc
                    {
                        PackageID = pkgId, PocEmail = email, PocName = fn + " " + ln,
                        Token = "demo-" + prog.P.Replace(" ", "").ToLowerInvariant() + "-poc" + (i + 1)
                    });
                }
                _pocs.AddRange(pocList);

                int target = counts[prog.P];
                int made = 0, guard = 0;
                while (made < target && guard++ < 1000)
                {
                    bool fx = rnd.NextDouble() < 0.12;
                    Bp bp = fx ? fxBps[rnd.Next(fxBps.Count)] : bps[rnd.Next(bps.Count)];
                    string ccy = fx ? fxCcy[rnd.Next(fxCcy.Length)] : "AUD";

                    string poTerm;
                    double roll = rnd.NextDouble();
                    if (fx)                 poTerm = new[] { "Z005", "Z007", "Z010" }[rnd.Next(3)];
                    else if (roll < 0.30)   poTerm = bp.Term;                                             // follows master (flags only if master is non-standard)
                    else if (roll < 0.55)   poTerm = bp.Term == "Z020" ? "Z005" : "Z020";                 // standard but overridden
                    else                    poTerm = new[] { "Z030", "Z045", "Z007", "Z014", "Z060", "Z000", "Z010" }[rnd.Next(7)];

                    var cat = PORDRules.Categorise(ccy, poTerm, bp.Term);
                    if (cat == PordCategory.None) continue;

                    var poc = pocList[rnd.Next(pocList.Count)];
                    decimal ordered = Math.Round((decimal)(Math.Pow(rnd.NextDouble(), 2.2) * 1800000 + 4000), 2);
                    decimal delivered = Math.Round(ordered * (decimal)(rnd.NextDouble() * 0.85), 2);
                    decimal invoiced = Math.Round(delivered * (decimal)(0.6 + rnd.NextDouble() * 0.4), 2);
                    double rnRoll = rnd.NextDouble();
                    int reviewNbr = rnRoll < 0.72 ? 1 : rnRoll < 0.92 ? 2 : 3;
                    bool oldContract = rnd.NextDouble() < 0.18;

                    var po = new PordPo
                    {
                        PoID = poId++, BatchID = 3, PackageID = pkgId, CheckType = PordCheckType.PaymentTerms,
                        CompanyCode = "1000", PoNumber = (poSeq += 7 + rnd.Next(400)).ToString(CultureInfo.InvariantCulture),
                        PoCreatedDate = today.AddDays(-rnd.Next(20, 900)),
                        PoTermKey = poTerm, PoTermDays = PORDRules.TermDays(poTerm),
                        BpNumber = bp.No, BpName = bp.Name, BpTermKey = bp.Term, BpTermDays = PORDRules.TermDays(bp.Term),
                        Currency = ccy, Ordered = ordered, Delivered = delivered, StillToDeliver = ordered - delivered, Invoiced = invoiced,
                        PurchasingGroup = "P" + (10 + rnd.Next(40)).ToString(CultureInfo.InvariantCulture),
                        DeliveryDate = today.AddDays(rnd.Next(-30, 240)),
                        PoCreator = poc.PocEmail.Split('@')[0].Replace(".", "").ToUpperInvariant(),
                        PocEmail = poc.PocEmail, PocName = poc.PocName,
                        DeliveryManager = prog.Cc, DeliveryManagerName = prog.Dm, DmProgram = prog.P,
                        ContractNumber = "CN" + (3000000 + rnd.Next(999999)).ToString(CultureInfo.InvariantCulture),
                        ContractDate = oldContract ? new DateTime(2019 + rnd.Next(3), 1 + rnd.Next(12), 1 + rnd.Next(27))
                                                   : today.AddDays(-rnd.Next(60, 1100)),
                        ReviewNbr = reviewNbr, Category = cat
                    };
                    _pos.Add(po);
                    made++;
                }

                // Pre-populate some responses so the demo shows a cycle in flight.
                double share = status == PordStatus.InReview ? 0.55 : status == PordStatus.Finalised ? 1.0 : 0.0;
                foreach (var po in _pos.Where(p => p.PackageID == pkgId))
                {
                    if (rnd.NextDouble() >= share) continue;
                    double r = rnd.NextDouble();
                    if (status == PordStatus.Finalised && r < 0.12)
                    {
                        po.Response = PordResponse.NoResponse; po.ReviewedBy = "System (finalise)";
                    }
                    else if (po.ContractDate.HasValue && po.ContractDate.Value < new DateTime(2022, 7, 1) && r < 0.8)
                    {
                        po.Response = PordResponse.Reason; po.ReasonCode = "VR03";
                        po.EvidenceRef = "BN" + (18000000 + rnd.Next(999999)).ToString(CultureInfo.InvariantCulture);
                        po.Comments = "Contract " + po.ContractNumber + " signed " + PORDHelper.Date(po.ContractDate) + ".";
                        po.ReviewedBy = po.PocName;
                    }
                    else if (r < 0.62)
                    {
                        po.Response = PordResponse.Amend; po.TargetDate = today.AddDays(7 + rnd.Next(21));
                        po.Comments = r < 0.3 ? "Change request raised with purchasing officer." : null;
                        po.ReviewedBy = po.PocName;
                    }
                    else if (r < 0.85)
                    {
                        po.Response = PordResponse.Reason; po.ReasonCode = r < 0.75 ? "VR01" : "VR02";
                        po.EvidenceRef = "BN" + (18000000 + rnd.Next(999999)).ToString(CultureInfo.InvariantCulture);
                        po.Comments = po.ReasonCode == "VR01"
                            ? "Supplier discount for early payment agreed at contract; value-for-money minute on file."
                            : "Terms mandated under the relevant Commonwealth fuel excise arrangement.";
                        po.ReviewedBy = po.PocName;
                    }
                    else
                    {
                        po.Response = PordResponse.Reassign; po.Comments = "Owned by " + first[rnd.Next(first.Length)] + " "
                            + last[rnd.Next(last.Length)] + " in Contracting Services.";
                        po.ReviewedBy = po.PocName;
                    }
                    po.ReviewedDate = cycleLoad.AddDays(2 + rnd.Next(5)).AddMinutes(rnd.Next(600));
                }
            }

            _batches = new List<PordLoadBatch>
            {
                new PordLoadBatch { BatchID = 1, FileName = "PO_NSPT_REVIEW_" + today.AddMonths(-2).ToString("yyyyMM") + "12.csv",
                    LoadedDate = today.AddMonths(-2).AddDays(-9).AddHours(10), LoadedBy = "Shauna Lodding", RowsInFile = 212, Flagged = 212, Excluded = 0, Repeat = 0, Resolved = 0 },
                new PordLoadBatch { BatchID = 2, FileName = "PO_NSPT_REVIEW_" + today.AddMonths(-1).ToString("yyyyMM") + "11.csv",
                    LoadedDate = today.AddMonths(-1).AddDays(-9).AddHours(10), LoadedBy = "Shauna Lodding", RowsInFile = 197, Flagged = 176, Excluded = 21, Repeat = 49, Resolved = 58 },
                new PordLoadBatch { BatchID = 3, FileName = "PO_NSPT_REVIEW_" + cycleLoad.ToString("yyyyMMdd") + ".csv",
                    LoadedDate = cycleLoad.AddHours(10), LoadedBy = "Kate", RowsInFile = _pos.Count + 34,
                    Flagged = _pos.Count, Excluded = 34, Repeat = _pos.Count(p => p.ReviewNbr > 1), Resolved = 41 }
            };

            _exclusions = new List<PordExclusion>();
            var exBps = bps.Take(9).ToList();
            for (int i = 0; i < 9; i++)
            {
                var b = exBps[i];
                _exclusions.Add(new PordExclusion
                {
                    PoNumber = (4500655000 + i * 377).ToString(CultureInfo.InvariantCulture), PoTermKey = i % 3 == 0 ? "Z030" : "Z045",
                    BpName = b.Name, DmProgram = programs[i % programs.Length].P,
                    ReasonCode = i % 3 == 0 ? "VR03" : i % 3 == 1 ? "VR01" : "VR02",
                    EvidenceRef = "BN" + (17500000 + i * 4211).ToString(CultureInfo.InvariantCulture),
                    GrantedDate = today.AddMonths(-1 - (i % 2)).AddDays(-i),
                    ExpiryDate = i == 4 ? today.AddDays(-20) : i == 7 ? today.AddDays(12) : today.AddMonths(11 - (i % 2)).AddDays(-i),
                    GrantedBy = programs[i % programs.Length].Name ?? "AS Fin", IsRevoked = false
                });
            }

            _resolved = new List<PordResolved>();
            for (int i = 0; i < 12; i++)
            {
                var b = bps[(i * 7) % bps.Count];
                _resolved.Add(new PordResolved
                {
                    PoNumber = (4500690000 + i * 913).ToString(CultureInfo.InvariantCulture), BpName = b.Name,
                    DmProgram = programs[i % programs.Length].P, OldTermKey = i % 2 == 0 ? "Z030" : "Z007",
                    CommittedDate = today.AddMonths(-1).AddDays(-5 + i % 4), VerifiedDate = cycleLoad
                });
            }

            _bpStatus = new Dictionary<string, string> { { "1000210387", "Notified" } };
        }
    }
}
