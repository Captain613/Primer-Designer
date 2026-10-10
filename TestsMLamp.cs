using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RpaDesigner
{
    // Synthetic DNA, published TP53 and a user-supplied CYP2C9 fixture verify software contracts.
    // These checks cannot predict amplification timing or allele discrimination.
    public static class MLampSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("mLAMP self-test report");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            lines.Add("Synthetic DNA, Ren 2019 TP53 and user-supplied CYP2C9 rs1057910 sequences; no enzyme kinetics, allele selectivity or clinical performance validation.");
            Action<string, Action> test = delegate(string title, Action action)
            {
                var watch = Stopwatch.StartNew();
                try { action(); passed++; lines.Add("PASS  " + title + " (" + watch.ElapsedMilliseconds + " ms)"); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + title + ": " + ex.Message); lines.Add(ex.StackTrace ?? ""); }
            };
            string source = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
            LampDesignResult fixture = null;

            test("Paper preset uses FIP, third-from-end mismatch and four core primers", delegate
            {
                LampDesignSettings s = LampDesignSettings.MLampDefaults();
                Require(s.SnpMethod == "mLAMP" && s.SnpOrientation == "FIP", "Preset must identify mLAMP and its FIP direction.");
                Require(s.ExtraMismatchFromThreePrime == 3 && !s.IncludeLoops, "Preset must use the paper's initial third-position/four-primer choice.");
                s.SnpOrientation = "BIP";
                Require(LampDesignSettings.MLampDefaults().SnpOrientation == "FIP", "Preset instances must not share mutable state.");
            });
            test("Widened bounds admit published TP53 regions and both edited F2 alleles", delegate
            {
                // Ren 2019 SI Table S1. The wild-type target has C at position 38.
                const string target = "CTTTGAGGTGCGTGTTTGTGCCTGTCCTGGGAGAGACCGGCGCACAGAGGAAGAGAATCTCCGCAAGAAAGGGGAGC"
                    + "CTCACCACGAGCTGCCCCCAGGGAGCACTAAGCGAGGTAAGCAAGCAGGACAAGAAGCGGTGGAGGAGACCAAGGGT"
                    + "GCAGTTATGCCTCAGATTCACTTTTATCACCTTTCCTTGCCTCTTT";
                LampDesignSettings s = LampDesignSettings.MLampDefaults();
                LampRegion f3 = PaperRegion("F3", "CTTTGAGGTGCGTGTTTG", s), b3 = PaperRegion("B3", "AGAGGCAAGGAAAGGTG", s);
                LampRegion f1 = PaperRegion("F1c", "CTCCCCTTTCTTGCGGAG", s), b1 = PaperRegion("B1c", "CCAGGGAGCACTAAGCGA", s);
                LampRegion b2 = PaperRegion("B2", "TAACTGCACCCTTGGTCT", s);
                LampRegion wtF2 = PaperRegion("F2", "TGCCTGTCCTGGGAGAGTCC", s), mutF2 = PaperRegion("F2", "TGCCTGTCCTGGGAGAGTCT", s);
                IList anchors = (IList)InvokeEngine("Anchored", target, 38, false, s, CancellationToken.None);
                bool nativeFound = false;
                foreach (object anchor in anchors)
                    nativeFound |= (int)anchor.GetType().GetField("Start").GetValue(anchor) == 19
                        && (int)anchor.GetType().GetField("End").GetValue(anchor) == 38;
                Require(nativeFound, "The unmodified 20-nt F2 was rejected before artificial-mismatch processing.");
                foreach (LampRegion f2 in new[] { wtF2, mutF2 })
                {
                    var oligo = (LampOligo)InvokeEngine("Oligo", "FIP", s, CancellationToken.None, new[] { f1, f2 });
                    Require(oligo != null && oligo.Sequence == f1.Sequence + f2.Sequence && oligo.Sequence.Length == 38,
                        "Published concatenated FIP was excluded or changed.");
                }
                Require(InvokeEngine("Oligo", "BIP", s, CancellationToken.None, new[] { b1, b2 }) != null,
                    "Published concatenated BIP was excluded.");
                Require(f3.Sequence.Length == 18 && b3.Sequence.Length == 17, "Paper outer-primer fixture changed.");
                // A user's old, tighter bound must still reject these F2 sequences.
                s.F2Tm = new LampTmRange(55, 65);
                Require(InvokeEngine("ModifiedRegion", wtF2, wtF2.Sequence, s) == null
                    && InvokeEngine("ModifiedRegion", mutF2, mutF2.Sequence, s) == null,
                    "Wider defaults silently bypassed an explicit F2 Tm limit.");
            });
            test("Widened CYP2C9 search retains or improves the former complete-candidate score", delegate
            {
                SnpInput input = Cyp2c9Input();
                Require(input.Reference.Sequence.Length == 401 && input.Position == 201
                    && input.ReferenceAllele == 'A' && input.AlternateAllele == 'C', "CYP2C9 regression fixture changed.");
                LampDesignResult narrow = LampDesignEngine.DesignSnp(input, FormerMLampBounds(), null, CancellationToken.None);
                LampDesignResult wide = LampDesignEngine.DesignSnp(input, LampDesignSettings.MLampDefaults(), null, CancellationToken.None);
                Require(narrow.Sets.Count > 0 && wide.Sets.Count > 0, "CYP2C9 fixture lost all mLAMP candidates.");
                Require(wide.Sets[0].Score >= 82.59 && wide.Sets[0].Score >= narrow.Sets[0].Score,
                    "Wider bounds again discarded a better complete candidate: narrow=" + narrow.Sets[0].Score.ToString("F2", CultureInfo.InvariantCulture)
                    + ", wide=" + wide.Sets[0].Score.ToString("F2", CultureInfo.InvariantCulture) + ".");
                Require(wide.Sets.Exists(delegate(LampPrimerSet set)
                {
                    return set.FIP.Sequence == "AATGTCACAGGTCACTGCATGGCACGAGGTCCAGAGATTCA"
                        && set.AlternateInner.Sequence == "AATGTCACAGGTCACTGCATGGCACGAGGTCCAGAGATTCC"
                        && set.BIP.Sequence == "TCAGAAACTATCTCATTCCCAAGGTGGACTTCGAAAACATGGAGT"
                        && set.F3.Sequence == "ATGCCCTACACAGATGCT" && set.B3.Sequence == "GTTATGCACTTCTCTCACCC"
                        && set.F3.Regions[0].Start == 159 && set.F3.Regions[0].End == 176
                        && set.B3.Regions[0].Start == 360 && set.B3.Regions[0].End == 379
                        && set.BIP.Regions[0].Start == 253 && set.BIP.Regions[0].End == 277
                        && set.BIP.Regions[1].Start == 301 && set.BIP.Regions[1].End == 320;
                }), "The former complete CYP2C9 candidate did not survive through final widened search output.");
                lines.Add("      CYP2C9 rs1057910 software regression: former bounds " + narrow.Sets[0].Score.ToString("F2", CultureInfo.InvariantCulture)
                    + "; widened bounds " + wide.Sets[0].Score.ToString("F2", CultureInfo.InvariantCulture) + ".");
                Validate(wide, 3);
            });
            test("CYP2C9 former 25-nt B1c reaches arm selection with its original 20-nt B2", delegate
            {
                SnpInput input = Cyp2c9Input();
                LampDesignSettings settings = (LampDesignSettings)InvokeEngine("Validate", LampDesignSettings.MLampDefaults(), input.Reference.Sequence.Length);
                object catalog = InvokeEngine("BuildCatalog", input.Reference.Sequence, settings, CancellationToken.None);
                object role = catalog.GetType().GetMethod("For").Invoke(catalog, new object[] { "B2" });
                object anneal = null;
                foreach (object w in (IEnumerable)role.GetType().GetField("All").GetValue(role))
                    if (Coordinate(w, "Start") == 301 && Coordinate(w, "End") == 320) { anneal = w; break; }
                Require(anneal != null, "CYP2C9 original B2 window was excluded before arm selection.");
                MethodInfo arms = typeof(LampDesignEngine).GetMethod("Arms", BindingFlags.Static | BindingFlags.NonPublic);
                object cache = Activator.CreateInstance(arms.GetParameters()[3].ParameterType);
                IEnumerable values = (IEnumerable)InvokeEngine("Arms", anneal, true, catalog, cache);
                bool retained = false;
                foreach (object arm in values)
                {
                    object inner = arm.GetType().GetField("Inner").GetValue(arm);
                    object actualAnneal = arm.GetType().GetField("Anneal").GetValue(arm);
                    retained |= Coordinate(inner, "Start") == 253 && Coordinate(inner, "End") == 277
                        && Coordinate(actualAnneal, "Start") == 301 && Coordinate(actualAnneal, "End") == 320;
                }
                Require(retained, "The former B1c 253-277 / B2 301-320 pair was prematurely pruned under widened bounds.");
            });
            test("CYP2C9 original complete candidate still scores 82.59 under widened settings", delegate
            {
                LampDesignSettings settings = LampDesignSettings.MLampDefaults();
                LampPrimerSet set = FormerCyp2c9Candidate(Cyp2c9Input(), settings);
                double reference = (double)InvokeEngine("ReactionPenalty", set, false, settings, CancellationToken.None);
                double alternate = (double)InvokeEngine("ReactionPenalty", set, true, settings, CancellationToken.None);
                double penalty = (reference + alternate) / 2.0
                    + 0.05 * Math.Abs(set.BIP.Regions[1].End - set.FIP.Regions[1].Start + 1 - 140)
                    + 0.01 * Math.Abs(set.SpanLength - 200);
                Require(Math.Abs(penalty - 5.27000099) < 0.000001 && DesignEngine.ScoreFromPenalty(penalty) == 82.59,
                    "Search improvement changed the original complete candidate's final scoring weights or sequence metrics.");
            });
            test("Structure preview cache reuses results and exhausted budget records fallback", delegate
            {
                LampDesignSettings settings = LampDesignSettings.MLampDefaults();
                object catalog = InvokeEngine("BuildCatalog", Cyp2c9Input().Reference.Sequence, settings, CancellationToken.None);
                Type type = catalog.GetType(); FieldInfo budget = type.GetField("StructureProbeBudget"), skipped = type.GetField("StructureProbeSkipped");
                const string dna = "TCAGAAACTATCTCATTCCCAAGGTGGACTTCGAAAACATGGAGT";
                long before = (long)budget.GetValue(catalog);
                double first = (double)InvokeEngine("ProbeStructure", dna, catalog);
                long after = (long)budget.GetValue(catalog);
                Require(after == before - 4L * dna.Length * dna.Length && !(bool)skipped.GetValue(catalog),
                    "An affordable structure preview must debit its work budget without recording fallback.");
                double cached = (double)InvokeEngine("ProbeStructure", dna, catalog);
                Require(cached == first && (long)budget.GetValue(catalog) == after,
                    "Cached structure previews must reuse the same result without charging work twice.");
                budget.SetValue(catalog, 0L);
                const string unpreviewed = "AATGTCACAGGTCACTGCATGGCACGAGGTCCAGAGATTCA";
                var cache = (IDictionary)type.GetField("StructureCache").GetValue(catalog);
                Require(!cache.Contains(unpreviewed), "Exhausted-budget fixture was already previewed.");
                Require((double)InvokeEngine("ProbeStructure", unpreviewed, catalog) == 0
                    && (long)budget.GetValue(catalog) == 0 && (bool)skipped.GetValue(catalog),
                    "An exhausted preview budget must record its unevaluated fallback without going negative.");
            });
            test("Structure preview observes cancellation even for a cached sequence", delegate
            {
                object catalog = InvokeEngine("BuildCatalog", Cyp2c9Input().Reference.Sequence, LampDesignSettings.MLampDefaults(), CancellationToken.None);
                const string dna = "TCAGAAACTATCTCATTCCCAAGGTGGACTTCGAAAACATGGAGT";
                InvokeEngine("ProbeStructure", dna, catalog);
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel(); catalog.GetType().GetField("Cancellation").SetValue(catalog, cancellation.Token);
                    bool rejected = false;
                    try { InvokeEngine("ProbeStructure", dna, catalog); } catch (OperationCanceledException) { rejected = true; }
                    Require(rejected, "A cached preview bypassed the user's cancellation request.");
                }
            });
            test("Zero, second and third-position designs retain exact allele and mismatch coordinates", delegate
            {
                foreach (int offset in new int[] { 0, 2, 3 })
                {
                    SnpInput input = Input(source); string reference = input.Reference.Sequence, alternate = input.Alternate.Sequence;
                    LampDesignResult r = LampDesignEngine.DesignSnp(input, Broad(offset), null, CancellationToken.None);
                    Require(r.Sets.Count > 0, "Artificial fixture yielded no candidate for offset " + offset + ".");
                    Validate(r, offset);
                    ValidateHighlights(r, offset);
                    Equal(reference, input.Reference.Sequence, "Caller reference remains unchanged");
                    Equal(alternate, input.Alternate.Sequence, "Caller alternate remains unchanged");
                    if (offset == 3) fixture = r;
                }
            });
            test("Automatic direction resolves only to FIP and preserves caller settings", delegate
            {
                LampDesignSettings s = Broad(3); s.SnpOrientation = "Auto";
                LampDesignResult r = LampDesignEngine.DesignSnp(Input(source), s, null, CancellationToken.None);
                Require(r.Sets.Count > 0 && r.Settings.SnpOrientation == "FIP", "mLAMP Auto must resolve to FIP.");
                Require(s.SnpOrientation == "Auto" && s.SnpMethod == "mLAMP" && s.ExtraMismatchFromThreePrime == 3, "Design mutated caller settings.");
                Validate(r, 3);
            });
            test("Unsupported reverse, absent SNP and invalid offsets fail explicitly", delegate
            {
                LampDesignSettings s = Broad(3); s.SnpOrientation = "BIP";
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(Input(source), s, null, CancellationToken.None); }, "mLAMP BIP");
                ExpectArgument(delegate { LampDesignEngine.Design(SequenceParser.Parse(source), Broad(3), null, CancellationToken.None); }, "mLAMP without SNP");
                foreach (int offset in new int[] { -1, 1, 4 })
                {
                    LampDesignSettings invalid = Broad(offset);
                    ExpectArgument(delegate { LampDesignEngine.DesignSnp(Input(source), invalid, null, CancellationToken.None); }, "Unsupported offset " + offset);
                }
            });
            test("Copy, text, CSV, HTML and FASTA retain mLAMP identity and plain DNA", delegate
            {
                Require(fixture != null, "Third-position fixture unavailable.");
                Require(LampReportWriter.IsMLamp(fixture) && !LampReportWriter.IsPa(fixture), "mLAMP was classified as another method.");
                Require(LampReportWriter.ModeName(fixture).Contains("mLAMP"), "Mode name loses mLAMP identity.");
                string txt = LampReportWriter.TextReport(fixture), csv = LampReportWriter.Csv(fixture), html = LampReportWriter.Html(fixture);
                Require(txt.Contains("mLAMP") && csv.Contains("mLAMP") && html.Contains("mLAMP"), "An export loses mLAMP identity.");
                Require(txt.Contains(LampReportWriter.MLampEvidenceUrl) && html.Contains(LampReportWriter.MLampEvidenceUrl), "Report loses original-paper attribution.");
                Require(txt.Contains("独立反应") && txt.Contains("人为错配"), "Report must explain separate reactions and artificial mismatch.");
                foreach (LampPrimerSet set in fixture.Sets)
                {
                    HighlightedReport copy = LampReportWriter.HighlightedOrderingText(set, fixture);
                    var expected = new HashSet<int>(); int cursor = 0;
                    string[] rows = copy.Text.Split('\n'); List<LampOligo> oligos = LampReportWriter.Oligos(set);
                    Require(rows.Length == oligos.Count, "Copy must have one row per oligo.");
                    for (int i = 0; i < rows.Length; i++)
                    {
                        int tab = rows[i].IndexOf('\t'); Require(tab >= 0 && rows[i].StartsWith("mLAMP_", StringComparison.Ordinal), "Copy name must carry mLAMP identity.");
                        Equal(oligos[i].Sequence, rows[i].Substring(tab + 1), "Copied DNA order sequence");
                        if (oligos[i].SnpIndex >= 0) expected.Add(cursor + tab + 1 + oligos[i].SnpIndex);
                        cursor += rows[i].Length + 1;
                        Require(txt.Contains(oligos[i].Sequence) && csv.Contains(oligos[i].Sequence), "Export lost an order sequence.");
                        int blue = oligos[i].SnpIndex < 0 ? -1 : oligos[i].Sequence.Length - 3;
                        string marked = MarkedSequence(oligos[i].Sequence, oligos[i].SnpIndex, blue);
                        Require(html.Contains("<pre class=\"dna\">" + marked + "</pre>"), "HTML must distinguish the true SNP and artificial mismatch without changing DNA.");
                    }
                    ExactMarks(copy, expected);
                    Equal(copy.Text, LampReportWriter.OrderingText(set, fixture).Replace("\r\n", "\n"), "Copy plain-text fallback");
                    HighlightedReport detail = LampReportWriter.HighlightedSet(set, fixture);
                    HashSet<int> detailRed = DetailMarks(detail, set, fixture, false);
                    Require(detailRed.Count == 6, "Detail report must mark SNP in complete FIPs, F2 components and native templates.");
                    ExactSnpMarks(detail, detailRed);
                }
                var all = new List<LampOligo>(); foreach (LampPrimerSet set in fixture.Sets) all.AddRange(LampReportWriter.Oligos(set));
                string[] fasta = LampReportWriter.Fasta(fixture).Replace("\r", "").TrimEnd('\n').Split('\n');
                Require(fasta.Length == all.Count * 2, "FASTA must contain one header/sequence pair per oligo.");
                for (int i = 0; i < all.Count; i++)
                {
                    Require(fasta[i * 2].StartsWith(">mLAMP_", StringComparison.Ordinal) && fasta[i * 2].Contains("method=mLAMP"), "FASTA method identity is wrong.");
                    Equal(all[i].Sequence, fasta[i * 2 + 1], "FASTA plain DNA");
                    Require(Regex.IsMatch(fasta[i * 2 + 1], "^[ACGT]+$"), "FASTA DNA includes chemical annotations.");
                }
            });
            test("mLAMP preset does not change existing AS-LAMP search semantics", delegate
            {
                Require(fixture != null, "Third-position fixture unavailable.");
                LampDesignSettings s = Broad(3); s.SnpMethod = "AS-LAMP";
                LampDesignResult r = LampDesignEngine.DesignSnp(Input(source), s, null, CancellationToken.None);
                Require(!LampReportWriter.IsMLamp(r) && r.Sets.Count == fixture.Sets.Count, "AS-LAMP method or result count changed.");
                for (int i = 0; i < r.Sets.Count; i++)
                {
                    Equal(fixture.Sets[i].FIP.Sequence, r.Sets[i].FIP.Sequence, "Existing FIP search is reused");
                    Equal(fixture.Sets[i].AlternateInner.Sequence, r.Sets[i].AlternateInner.Sequence, "Existing alternate FIP search is reused");
                    Require(fixture.Sets[i].Score == r.Sets[i].Score, "Method label must not manufacture allele discrimination scores.");
                    Require(LampReportWriter.HighlightedOrderingText(r.Sets[i], r).MismatchHighlights.Count == 0
                        && LampReportWriter.HighlightedSet(r.Sets[i], r).MismatchHighlights.Count == 0, "AS-LAMP must not acquire mLAMP-only blue marks.");
                    foreach (LampOligo p in LampReportWriter.Oligos(r.Sets[i]))
                        Require(LampReportWriter.MismatchIndex(p, r.Sets[i], r) == -1, "AS-LAMP must not expose a mLAMP blue index.");
                }
                Require(LampReportWriter.HighlightedTextReport(r).MismatchHighlights.Count == 0
                    && !LampReportWriter.Html(r).Contains("<span class=\"mismatch\">"), "AS-LAMP report must retain its existing highlighting.");
            });
            lines.Add(""); lines.Add("Passed: " + passed); lines.Add("Failed: " + failed); lines.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }

        private static LampDesignSettings Broad(int offset)
        {
            LampDesignSettings s = LampDesignSettings.MLampDefaults();
            s.RegionMin = s.RegionMax = 20; s.MaxSets = 2; s.GcMin = 20; s.GcMax = 80;
            s.AnnealTmMin = s.InnerTmMin = 35; s.AnnealTmMax = s.InnerTmMax = 85;
            s.ExtraMismatchFromThreePrime = offset; return s;
        }
        private static SnpInput Cyp2c9Input()
        {
            // User-provided CYP2C93 rs1057910 400.fasta; GRCh38.p14 coordinates
            // NC_000010.11:94981096-94981496. This tests search/ranking software,
            // not amplification, allele discrimination or clinical performance.
            return SnpParser.Parse(">CYP2C9_rs1057910_user_supplied_software_regression_GRCh38_p14\n"
                + "ACCTTCATGATTCATATACCCCTGAATTGCTACAACAAATGTGCCATTTTTCTCCTTTTCCATCAGTTTT"
                + "TACTTGTGTCTTATCAGCTAAAGTCCAGGAAGAGATTGAACGTGTGATTGGCAGAAACCGGAGCCCCTGC"
                + "ATGCAAGACAGGAGCCACATGCCCTACACAGATGCTGTGGTGCACGAGGTCCAGAGATAC[A>C]TTGACCTTC"
                + "TCCCCACCAGCCTGCCCCATGCAGTGACCTGTGACATTAAATTCAGAAACTATCTCATTCCCAAGGTAAG"
                + "TTTGTTTCTCCTACACTGCAACTCCATGTTTTCGAAGTCCCCAAATTCATAGTATCATTTTTAAACCTCT"
                + "ACCATCACCGGGTGAGAGAAGTGCATAACTCATATGTATGGCAGTTTAACT");
        }
        private static LampDesignSettings FormerMLampBounds()
        {
            LampDesignSettings s = LampDesignSettings.MLampDefaults();
            s.RegionMin = 18; s.RegionMax = 27; s.GcMin = 35; s.GcMax = 70;
            s.AnnealTmMin = 55; s.AnnealTmMax = 65; s.InnerTmMin = 60; s.InnerTmMax = 70;
            s.CoreSpanMin = 110; s.CoreSpanMax = 190; s.SpanMin = 120; s.SpanMax = 300;
            return s;
        }
        private static int Coordinate(object value, string field)
        { return (int)value.GetType().GetField(field).GetValue(value); }
        private static LampPrimerSet FormerCyp2c9Candidate(SnpInput input, LampDesignSettings settings)
        {
            // First group in the user's mLAMP_20260929_v0.19.html report.
            // Rebuild the fixed candidate independently of today's search order.
            string sequence = input.Reference.Sequence;
            LampRegion f1 = FixtureRegion("F1c", sequence, 227, 248, true, settings);
            LampRegion f2 = FixtureRegion("F2", sequence, 183, 201, false, settings);
            char[] reference = f2.Sequence.ToCharArray(), alternate = f2.Sequence.ToCharArray();
            reference[reference.Length - 3] = alternate[alternate.Length - 3] = 'T';
            alternate[alternate.Length - 1] = input.AlternateAllele;
            LampRegion edited = (LampRegion)InvokeEngine("ModifiedRegion", f2, new string(reference), settings);
            LampRegion editedAlternate = (LampRegion)InvokeEngine("ModifiedRegion", f2, new string(alternate), settings);
            Require(edited != null && editedAlternate != null, "Original edited F2 alleles no longer meet the widened bounds.");
            var set = new LampPrimerSet { SpecificInner = "FIP", SpanStart = 159, SpanEnd = 379, SpanLength = 221,
                ExtraMismatchPosition = 199, ExtraMismatchTemplateBase = 'A', ExtraMismatchPrimerBase = 'T' };
            set.F3 = (LampOligo)InvokeEngine("Oligo", "F3", settings, CancellationToken.None,
                new[] { FixtureRegion("F3", sequence, 159, 176, false, settings) });
            set.B3 = (LampOligo)InvokeEngine("Oligo", "B3", settings, CancellationToken.None,
                new[] { FixtureRegion("B3", sequence, 360, 379, true, settings) });
            set.FIP = (LampOligo)InvokeEngine("Oligo", "FIP", settings, CancellationToken.None, new[] { f1, edited });
            set.AlternateInner = (LampOligo)InvokeEngine("Oligo", "FIP_alt", settings, CancellationToken.None, new[] { f1, editedAlternate });
            set.BIP = (LampOligo)InvokeEngine("Oligo", "BIP", settings, CancellationToken.None, new[] {
                FixtureRegion("B1c", sequence, 253, 277, false, settings), FixtureRegion("B2", sequence, 301, 320, true, settings) });
            Require(set.F3 != null && set.B3 != null && set.FIP != null && set.BIP != null && set.AlternateInner != null,
                "Original CYP2C9 complete candidate could not be reconstructed.");
            set.FIP.SnpIndex = set.FIP.Sequence.Length - 1; set.AlternateInner.SnpIndex = set.AlternateInner.Sequence.Length - 1;
            Equal("TCAGAAACTATCTCATTCCCAAGGTGGACTTCGAAAACATGGAGT", set.BIP.Sequence, "Original 45-nt CYP2C9 BIP");
            return set;
        }
        private static LampRegion FixtureRegion(string role, string sequence, int start, int end, bool reverse, LampDesignSettings settings)
        {
            string dna = sequence.Substring(start - 1, end - start + 1);
            if (reverse) dna = DesignEngine.ReverseComplement(dna);
            var original = new LampRegion { Name = role, Sequence = dna, Start = start, End = end, Reverse = reverse };
            var actual = (LampRegion)InvokeEngine("ModifiedRegion", original, dna, settings);
            Require(actual != null, "CYP2C9 fixture region excluded: " + role);
            return actual;
        }
        private static LampRegion PaperRegion(string role, string dna, LampDesignSettings settings)
        {
            Require(dna.Length >= settings.RegionMin && dna.Length <= settings.RegionMax, "Paper length excluded: " + role);
            var original = new LampRegion { Name = role, Sequence = dna, Start = 1, End = dna.Length };
            var actual = (LampRegion)InvokeEngine("ModifiedRegion", original, dna, settings);
            Require(actual != null, "Paper GC or reference Tm excluded: " + role);
            return actual;
        }
        private static object InvokeEngine(string name, params object[] args)
        {
            try { return typeof(LampDesignEngine).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        private static SnpInput Input(string source)
        {
            return SnpParser.Parse(">synthetic_mLAMP_nonbiological\n" + source.Substring(0, 332) + "[T>C]" + source.Substring(333));
        }
        private static void Validate(LampDesignResult r, int offset)
        {
            Require(r.Settings.SnpMethod == "mLAMP" && r.Settings.SnpOrientation == "FIP", "mLAMP method/direction metadata is inconsistent.");
            foreach (LampPrimerSet set in r.Sets)
            {
                Require(set.SpecificInner == "FIP" && set.LF == null && set.LB == null, "Four-primer mLAMP default acquired a reverse or loop design.");
                Require(LampReportWriter.Oligos(set).Count == 5, "Two FIP alleles must share the other three core oligos.");
                LampRegion f2 = set.FIP.Regions[1], alt = set.AlternateInner.Regions[1];
                Require(f2.Name == "F2" && !f2.Reverse && !alt.Reverse && f2.End == r.Snp.Position && alt.End == r.Snp.Position, "SNP must be F2's forward 3-prime terminus.");
                Require(f2.Start == alt.Start && set.FIP.SnpIndex == set.FIP.Sequence.Length - 1 && set.AlternateInner.SnpIndex == set.AlternateInner.Sequence.Length - 1, "Both FIPs must share a binding window and terminal SNP.");
                Equal(set.FIP.Regions[0].Sequence + f2.Sequence, set.FIP.Sequence, "F1c plus F2 ordering architecture");
                Equal(set.AlternateInner.Regions[0].Sequence + alt.Sequence, set.AlternateInner.Sequence, "Alternate F1c plus F2 architecture");
                string nativeRef = r.Input.Sequence.Substring(f2.Start - 1, f2.Sequence.Length);
                string nativeAlt = r.Snp.Alternate.Sequence.Substring(f2.Start - 1, f2.Sequence.Length);
                int deliberate = offset == 0 ? 0 : 1;
                Require(Differences(f2.Sequence, nativeRef) == deliberate && Differences(alt.Sequence, nativeAlt) == deliberate, "Each target pair must retain only the requested artificial mismatch.");
                Require(Differences(f2.Sequence, nativeAlt) == deliberate + 1 && Differences(alt.Sequence, nativeRef) == deliberate + 1, "Opposite allele must add exactly one terminal SNP mismatch.");
                Require(Differences(set.FIP.Sequence, set.AlternateInner.Sequence) == 1, "FIP alleles must differ only at their SNP, sharing the artificial mismatch.");
                Require(set.ExtraMismatchPosition == (offset == 0 ? 0 : r.Snp.Position - offset + 1), "Mismatch template coordinate is off by one.");
                if (offset > 0)
                {
                    int i = f2.Sequence.Length - offset;
                    Require(f2.Sequence[i] == alt.Sequence[i] && f2.Sequence[i] == set.ExtraMismatchPrimerBase && f2.Sequence[i] != nativeRef[i], "Both allele primers must carry the same nonnative mismatch base.");
                    Require(set.ExtraMismatchTemplateBase == nativeRef[i], "Recorded original mismatch base must be unchanged template DNA.");
                }
                Equal(r.Input.Sequence.Substring(set.SpanStart - 1, set.SpanLength), set.ReferenceTemplate, "Native reference template");
                Equal(r.Snp.Alternate.Sequence.Substring(set.SpanStart - 1, set.SpanLength), set.AlternateTemplate, "Native alternate template");
                foreach (LampOligo p in LampReportWriter.Oligos(set))
                {
                    Require(Regex.IsMatch(p.Sequence, "^[ACGT]+$") && p.RnaIndex < 0 && String.IsNullOrEmpty(p.ThreePrimeBlock), "mLAMP oligo acquired RNA/C3 chemistry.");
                    Equal(p.Sequence, p.OrderingSequence, "Unmodified DNA ordering sequence");
                }
            }
        }
        private static void ExactMarks(HighlightedReport report, HashSet<int> expected)
        {
            Require(expected.Count == 2, "Only two true allele SNP bases should be highlighted in copy text.");
            ExactSnpMarks(report, expected);
        }
        private static void ExactSnpMarks(HighlightedReport report, HashSet<int> expected)
        {
            Require(report.SnpHighlights.Count == expected.Count, "Wrong number of SNP highlights.");
            foreach (ReportHighlight mark in report.SnpHighlights)
                Require(mark.Length == 1 && expected.Remove(mark.Start), "Wrong or duplicate SNP highlight.");
            Require(expected.Count == 0, "Missing SNP highlight.");
        }
        private static void ValidateHighlights(LampDesignResult r, int offset)
        {
            string html = LampReportWriter.Html(r);
            MatchCollection dnaBlocks = Regex.Matches(html, "<pre class=\"dna\">(.*?)</pre>", RegexOptions.Singleline);
            int htmlCursor = 0, perSet = offset == 0 ? 0 : 4;
            HighlightedReport full = LampReportWriter.HighlightedTextReport(r);
            var fullBlue = new HashSet<int>(); var fullRed = new HashSet<int>(); int fullCursor = 0;
            Require(full.MismatchHighlights.Count == perSet * r.Sets.Count, "Combined report lost or duplicated artificial-mismatch highlights.");
            Require(full.SnpHighlights.Count == r.Sets.Count * 6 + 2, "Combined report must preserve all complete-primer, component and template SNP highlights.");
            foreach (LampPrimerSet set in r.Sets)
            {
                HighlightedReport copy = LampReportWriter.HighlightedOrderingText(set, r);
                HighlightedReport detail = LampReportWriter.HighlightedSet(set, r);
                var red = new HashSet<int>(); var blue = new HashSet<int>(); int cursor = 0;
                string[] rows = copy.Text.Split('\n'); List<LampOligo> oligos = LampReportWriter.Oligos(set);
                for (int i = 0; i < oligos.Count; i++)
                {
                    LampOligo p = oligos[i]; int tab = rows[i].IndexOf('\t');
                    int mismatchIndex = p.SnpIndex >= 0 && offset > 0 ? p.Sequence.Length - offset : -1;
                    Equal(p.Sequence, rows[i].Substring(tab + 1), "Highlighting preserves copied nucleotide text");
                    Require(LampReportWriter.MismatchIndex(p, set, r) == mismatchIndex, "Blue index must follow the selected offset on each allele FIP only.");
                    if (p.SnpIndex >= 0) red.Add(cursor + tab + 1 + p.SnpIndex);
                    if (mismatchIndex >= 0)
                    {
                        blue.Add(cursor + tab + 1 + mismatchIndex);
                    }
                    ExpectedHtmlSequence(dnaBlocks, ref htmlCursor, p.OrderingSequence, p.OrderingSnpIndex, mismatchIndex);
                    if (p.Regions.Count > 1) foreach (LampOligoPart part in LampReportWriter.OligoParts(p))
                    {
                        int partRed = RelativeIndex(part, p.OrderingSnpIndex), partBlue = RelativeIndex(part, mismatchIndex);
                        ExpectedHtmlSequence(dnaBlocks, ref htmlCursor, part.Sequence, partRed, partBlue);
                    }
                    cursor += rows[i].Length + 1;
                }
                ExactMarks(copy, red);
                ExactMismatchMarks(copy, blue);
                int detailStart = full.Text.IndexOf(detail.Text, StringComparison.Ordinal);
                Require(detailStart >= 0, "Combined report lost a candidate's detailed report.");
                HashSet<int> detailBlue = DetailMarks(detail, set, r, true), detailRed = DetailMarks(detail, set, r, false);
                foreach (int position in detailBlue) fullBlue.Add(detailStart + position);
                foreach (int position in detailRed) fullRed.Add(detailStart + position);
                fullCursor = detailStart + detail.Text.Length;
                ExactMismatchMarks(detail, detailBlue);
                Require(detailRed.Count == 6, "Detail must retain red SNP in complete FIPs, F2 components and native templates."); ExactSnpMarks(detail, detailRed);
                int templateIndex = r.Snp.Position - set.SpanStart;
                ExpectedHtmlSequence(dnaBlocks, ref htmlCursor, set.ReferenceTemplate, templateIndex, -1);
                ExpectedHtmlSequence(dnaBlocks, ref htmlCursor, set.AlternateTemplate, templateIndex, -1);
            }
            fullRed.Add(SequenceLineStart(full.Text, r.Input.Sequence, ref fullCursor) + r.Snp.Position - 1);
            fullRed.Add(SequenceLineStart(full.Text, r.Snp.Alternate.Sequence, ref fullCursor) + r.Snp.Position - 1);
            ExactSnpMarks(full, fullRed);
            ExactMismatchMarks(full, fullBlue);
            ExpectedHtmlSequence(dnaBlocks, ref htmlCursor, r.Input.Sequence, r.Snp.Position - 1, -1);
            ExpectedHtmlSequence(dnaBlocks, ref htmlCursor, r.Snp.Alternate.Sequence, r.Snp.Position - 1, -1);
            Require(htmlCursor == dnaBlocks.Count, "HTML emitted unexpected sequence blocks.");
        }
        private static HashSet<int> DetailMarks(HighlightedReport report, LampPrimerSet set, LampDesignResult r, bool mismatch)
        {
            var expected = new HashSet<int>(); int cursor = 0;
            foreach (LampOligo p in LampReportWriter.Oligos(set))
            {
                int index = mismatch ? (p.SnpIndex >= 0 && r.Settings.ExtraMismatchFromThreePrime > 0 ? p.Sequence.Length - r.Settings.ExtraMismatchFromThreePrime : -1) : p.OrderingSnpIndex;
                int sequenceStart = SequenceLineStart(report.Text, p.OrderingSequence, ref cursor);
                if (index >= 0) expected.Add(sequenceStart + index);
                if (p.Regions.Count > 1) foreach (LampOligoPart part in LampReportWriter.OligoParts(p))
                {
                    sequenceStart = SequenceLineStart(report.Text, part.Sequence, ref cursor);
                    int relative = RelativeIndex(part, index); if (relative >= 0) expected.Add(sequenceStart + relative);
                }
            }
            int templateStart = SequenceLineStart(report.Text, set.ReferenceTemplate, ref cursor);
            if (!mismatch) expected.Add(templateStart + r.Snp.Position - set.SpanStart);
            templateStart = SequenceLineStart(report.Text, set.AlternateTemplate, ref cursor);
            if (!mismatch) expected.Add(templateStart + r.Snp.Position - set.SpanStart);
            return expected;
        }
        private static int RelativeIndex(LampOligoPart part, int index)
        {
            int relative = index - part.OrderingOffset;
            return index >= 0 && relative >= 0 && relative < part.Sequence.Length ? relative : -1;
        }
        private static int SequenceLineStart(string text, string sequence, ref int cursor)
        {
            while (cursor < text.Length)
            {
                int lineStart = cursor, lineEnd = text.IndexOf('\n', cursor); if (lineEnd < 0) lineEnd = text.Length;
                cursor = lineEnd + 1;
                if (text.Substring(lineStart, lineEnd - lineStart) == sequence) return lineStart;
            }
            throw new InvalidOperationException("Detailed report lost or reordered sequence: " + sequence);
        }
        private static void ExpectedHtmlSequence(MatchCollection blocks, ref int cursor, string sequence, int snpIndex, int mismatchIndex)
        {
            Require(cursor < blocks.Count, "HTML lost a sequence block.");
            Equal(MarkedSequence(sequence, snpIndex, mismatchIndex), blocks[cursor++].Groups[1].Value,
                "HTML ordered sequence and SNP/mismatch coloring");
        }
        private static void ExactMismatchMarks(HighlightedReport report, HashSet<int> expected)
        {
            Require(report.MismatchHighlights.Count == expected.Count, "Wrong number of artificial-mismatch highlights.");
            foreach (ReportHighlight mark in report.MismatchHighlights)
                Require(mark.Length == 1 && expected.Remove(mark.Start), "Wrong or duplicate artificial-mismatch highlight.");
            Require(expected.Count == 0, "Missing artificial-mismatch highlight.");
        }
        private static string MarkedSequence(string sequence, int snpIndex, int mismatchIndex)
        {
            var b = new StringBuilder();
            for (int i = 0; i < sequence.Length; i++)
            {
                string cls = i == snpIndex ? "snp" : i == mismatchIndex ? "mismatch" : null;
                if (cls != null) b.Append("<span class=\"").Append(cls).Append("\">");
                b.Append(sequence[i]);
                if (cls != null) b.Append("</span>");
            }
            return b.ToString();
        }
        private static int Differences(string a, string b)
        {
            Require(a.Length == b.Length, "Compared sequences differ in length."); int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++; return n;
        }
        private static void ExpectArgument(Action action, string label)
        {
            bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
            Require(rejected, label + " must throw ArgumentException.");
        }
        private static void Equal(string expected, string actual, string label) { Require(expected == actual, label + " differs."); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
