using System;
using System.Threading;

namespace RpaDesigner
{
    internal static class LampEndStabilityTests
    {
        public static void Register(Action<string, Action> test)
        {
            test("End DG37 fixed reference values include terminal and symmetry terms", delegate
            {
                Equal(-8.67, LampThermodynamics.EndDeltaG37("GCGCGC"));
                Equal(-1.31, LampThermodynamics.EndDeltaG37("ATATAT"));
                Equal(-2.94, LampThermodynamics.EndDeltaG37("aaaaaa"));
                Equal(-5.58, LampThermodynamics.EndDeltaG37("AGGCTT"));
                foreach (string invalid in new string[] { null, "", "ACGTA", "ACGTACG", "ACGTAN", "ACGTAU", "[rA]GC" })
                {
                    bool rejected = false;
                    try { LampThermodynamics.EndDeltaG37(invalid); } catch (ArgumentException) { rejected = true; }
                    Require(rejected, "Invalid DNA must not receive a free energy");
                }
                bool boundaryFound = false;
                for (int n = 0; n < 4096; n++)
                {
                    char[] bases = new char[6]; int bits = n;
                    for (int i = 0; i < 6; i++) { bases[i] = "ACGT"[bits & 3]; bits >>= 2; }
                    string dna = new String(bases); double dg = LampThermodynamics.EndDeltaG37(dna);
                    Equal(dg, LampThermodynamics.EndDeltaG37(DesignEngine.ReverseComplement(dna)));
                    if (dg == -5.0)
                    {
                        boundaryFound = true;
                        Require(new LampEndStabilityResult { DeltaG37 = dg }.Status.Contains("达到参考标准"), "Inclusive -5 threshold");
                    }
                }
                Require(boundaryFound, "An exact -5 fixture was exercised");
                Require(new LampEndStabilityResult { DeltaG37 = -4.99 }.Status.Contains("末端偏弱"), "Above threshold");
                Require(new LampEndStabilityResult { DeltaG37 = -8, HasArtificialMismatch = true }.Status.Contains("未评估"), "Mismatch cannot pass");
            });
            test("End selection uses synthesis direction and never crosses segment boundaries", delegate
            {
                var set = new LampPrimerSet { ExtraMismatchPosition = 102 };
                var oligo = new LampOligo { SnpIndex = 13, Sequence = "AGGCTTAAGCGCGC" };
                oligo.Regions.Add(new LampRegion { Name = "B1c", Sequence = "AGGCTTAA", Start = 80, End = 87 });
                oligo.Regions.Add(new LampRegion { Name = "B2", Sequence = "GCGCGC", Start = 100, End = 105, Reverse = true });
                var values = LampEndStability.Evaluate(oligo, set);
                Require(values.Count == 2 && values[0].Sequence == "AGGCTT" && values[0].End == "5′", "F1c/B1c use first six");
                Require(values[1].Sequence == "GCGCGC" && values[1].End == "3′" && values[1].HasArtificialMismatch, "Reverse mismatch mapping");
                Require(!values[0].HasArtificialMismatch, "Mismatch must be local to the inspected window");
                oligo.Regions[1].Sequence = "AAA";
                Require(!LampEndStability.Evaluate(oligo, set)[1].DeltaG37.HasValue, "Short region must not borrow from F1c/B1c");
                oligo.Regions[1].Sequence = "ACGTAN";
                Require(!LampEndStability.Evaluate(oligo, set)[1].DeltaG37.HasValue, "Ambiguous region unevaluated");
                oligo.RnaIndex = 14; oligo.ActivatedSequence = "ATATAT";
                oligo.ActivatedRegions.Add(new LampRegion { Name = "B2", Sequence = "ATATAT", Start = 110, End = 115, Reverse = true });
                values = LampEndStability.Evaluate(oligo, set);
                Require(values.Count == 1 && values[0].Sequence == "ATATAT" && !values[0].HasArtificialMismatch
                    && values[0].Scope == "切后有效 DNA", "PA uses activated regions exclusively");
            });
            test("LAMP, reverse AS-LAMP, mLAMP and PA-LAMP export end results without changing candidates", delegate
            {
                var ordinary = LampDesignEngine.Design(SequenceParser.Parse(LampReportWriter.ExampleFasta()),
                    new LampDesignSettings { MaxSets = 1 }, null, CancellationToken.None);
                CheckReports(ordinary);
                foreach (string mode in new string[] { "AS-LAMP", "mLAMP", "PA-LAMP" })
                {
                    var settings = mode == "mLAMP" ? LampDesignSettings.MLampDefaults() : new LampDesignSettings();
                    settings.MaxSets = 1; settings.SnpMethod = mode;
                    if (mode == "AS-LAMP") { settings.SnpOrientation = "BIP"; settings.ExtraMismatchFromThreePrime = 2; }
                    var result = LampDesignEngine.DesignSnp(SnpParser.Parse(LampReportWriter.ExampleSnpFasta()), settings, null, CancellationToken.None);
                    CheckReports(result);
                    var set = result.Sets[0];
                    foreach (LampOligo p in new LampOligo[] { set.SpecificInner == "FIP" ? set.FIP : set.BIP, set.AlternateInner })
                    {
                        var values = LampEndStability.Evaluate(p, set);
                        var terminal = values.Find(delegate(LampEndStabilityResult v) { return v.End == "3′"; });
                        Require(terminal != null, "Specific inner 3prime result");
                        if (mode == "PA-LAMP") Require(terminal.Sequence == p.ActivatedSequence.Substring(p.ActivatedSequence.Length - 6)
                            && terminal.Scope == "切后有效 DNA", "PA uses activated end, not precursor tail");
                        else Require(terminal.HasArtificialMismatch && terminal.Status.Contains("实际错配结合未评估"), "Both allele mismatches must remain unevaluated");
                    }
                }
            });
        }
        private static void CheckReports(LampDesignResult result)
        {
            Require(result.Sets.Count > 0, "Fixture must generate candidates");
            var set = result.Sets[0]; double score = set.Score;
            string ordering = LampReportWriter.OrderingText(set, result), fasta = LampReportWriter.Fasta(result);
            foreach (string report in new string[] { LampReportWriter.TextReport(result), LampReportWriter.Csv(result), LampReportWriter.Html(result) })
            {
                Require(report.Contains("ΔG°37") && report.Contains("1 M Na⁺") && report.Contains("kcal/mol"), "All exports include energy and conditions");
                Require(report.Contains("不改变排序") && report.Contains("非目标等位模板结合未评估"), "All exports explain limits");
                foreach (LampOligo p in LampReportWriter.Oligos(set))
                    foreach (LampEndStabilityResult value in LampEndStability.Evaluate(p, set))
                        Require(value.DeltaG37.HasValue && report.Contains(value.ToString()), "Every compact result exported");
                Require(report.IndexOf("以下 ΔG°37", StringComparison.Ordinal) == report.LastIndexOf("以下 ΔG°37", StringComparison.Ordinal), "Explanation appears once per report");
                Require(!report.Contains("；达到参考标准") && !report.Contains("；仅完全互补参考") && !report.Contains("；末端偏弱"), "No explanations appended to individual energies");
            }
            Equal(score, set.Score);
            Require(ordering == LampReportWriter.OrderingText(set, result) && fasta == LampReportWriter.Fasta(result), "Assessment must not alter ordering sequences");
        }
        private static void Equal(double expected, double actual)
        { Require(Math.Abs(expected - actual) < 1e-10, "Expected " + expected + ", actual " + actual); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
