using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    public static class UnlimitedRpaSelfTests
    {
        public static int Run(string reportPath)
        {
            var report = new List<string>(); int passed = 0, failed = 0;
            report.Add("RPA unlimited-bound regression tests (synthetic sequences only)");
            report.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Action<string, Action> test = delegate(string name, Action body)
            {
                var timer = Stopwatch.StartNew();
                try { body(); passed++; report.Add("PASS  " + name + " (" + timer.ElapsedMilliseconds + " ms)"); }
                catch (Exception ex) { failed++; report.Add("FAIL  " + name + ": " + ex); }
            };

            test("Disabled stored values are ignored, resolved copies preserve flags, caller is untouched", delegate
            {
                DesignSettings source = Unlimited();
                source.PrimerMin = -9; source.PrimerMax = -8; source.AmpliconMin = -7; source.AmpliconMax = -6;
                source.GcMin = Double.NaN; source.GcMax = Double.PositiveInfinity; source.PreferredAmplicon = -5;
                DesignSettings effective = DesignEngine.CopyAndValidate(source, 700);
                Require(effective.PrimerMin == 1 && effective.PrimerMax == 700, "Disabled length bounds must reach physical/template limits.");
                Require(effective.GcMin == 0 && effective.GcMax == 100 && effective.AmpliconMin == 1 && effective.AmpliconMax == 700, "Disabled composition/product bounds were not removed.");
                Require(effective.PrimerMinUnlimited && effective.PrimerMaxUnlimited && effective.GcMinUnlimited && effective.GcMaxUnlimited && effective.AmpliconMinUnlimited && effective.AmpliconMaxUnlimited && effective.PreferredAmpliconUnlimited, "Resolved flags were lost.");
                Require(source.PrimerMin == -9 && source.PrimerMax == -8 && source.AmpliconMin == -7 && source.AmpliconMax == -6 && Double.IsNaN(source.GcMin) && Double.IsPositiveInfinity(source.GcMax) && source.PreferredAmplicon == -5, "Caller settings mutated.");
            });

            test("One-sided limits accept short/long primers without disabling the other bound", delegate
            {
                DesignSettings lower = new DesignSettings { PrimerMinUnlimited = true, GcMinUnlimited = true, AmpliconMinUnlimited = true, PreferredAmpliconUnlimited = true };
                DesignSettings a = DesignEngine.CopyAndValidate(lower, 300);
                Require(a.PrimerMin == 1 && a.PrimerMax == 35 && a.GcMin == 0 && a.GcMax == 70, "Independent lower bounds were combined incorrectly.");
                Require(DesignEngine.CompositionCandidate("ACGTACGT", 1, a) != null, "Disabled lower length bound still clamps to 20.");
                Require(DesignEngine.CompositionCandidate(Repeat("ACGT", 10), 1, a) == null, "Enabled upper length bound was ignored.");
                DesignSettings upper = new DesignSettings { PrimerMaxUnlimited = true, GcMaxUnlimited = true, AmpliconMaxUnlimited = true };
                DesignSettings b = DesignEngine.CopyAndValidate(upper, 700);
                Require(b.PrimerMin == 30 && b.PrimerMax == 700 && b.GcMin == 30 && b.GcMax == 100, "Independent upper bounds were combined incorrectly.");
                Require(DesignEngine.CompositionCandidate(Repeat("ACGT", 20), 1, b) != null, "Disabled upper length bound still clamps to 60.");
                Require(DesignEngine.CompositionCandidate(Repeat("AT", 20), 1, b) == null, "Enabled lower GC bound was ignored.");
                Require(DesignEngine.CompositionCandidate(Repeat("GC", 20), 1, b) != null, "Disabled upper GC bound still rejects 100 percent.");
                Require(DesignEngine.CompositionCandidate("ACGTACGT", 1, b) == null, "Enabled lower length bound was ignored.");
            });

            test("Enabled validation and finite output count remain active", delegate
            {
                ExpectArgument(delegate { DesignEngine.CopyAndValidate(new DesignSettings { PrimerMin = 19, PrimerMaxUnlimited = true }, 300); });
                ExpectArgument(delegate { DesignEngine.CopyAndValidate(new DesignSettings { GcMin = Double.NaN, GcMaxUnlimited = true }, 300); });
                ExpectArgument(delegate { DesignEngine.CopyAndValidate(new DesignSettings { GcMin = 80, GcMax = 70 }, 300); });
                ExpectArgument(delegate { DesignEngine.CopyAndValidate(new DesignSettings { AmpliconMax = 6000, AmpliconMinUnlimited = true }, 7000); });
                ExpectArgument(delegate { DesignEngine.CopyAndValidate(new DesignSettings { PreferredAmplicon = 300 }, 400); });
                DesignSettings count = Unlimited(); count.MaxPairs = 51;
                ExpectArgument(delegate { DesignEngine.CopyAndValidate(count, 400); });
            });

            test("No length preference gives exact zero and reports preserve asymmetric infinity", delegate
            {
                DesignSettings settings = new DesignSettings { PrimerMinUnlimited = true, GcMaxUnlimited = true, PreferredAmpliconUnlimited = true, PreferredAmplicon = Int32.MaxValue, MaxPairs = 4 };
                DesignSettings effective = DesignEngine.CopyAndValidate(settings, 300);
                Require(DesignEngine.PairLengthPenalty(100, effective) == 0 && DesignEngine.PairLengthPenalty(200, effective) == 0, "No-preference mode still penalizes product length.");
                string text = ReportWriter.Params(effective, false);
                Require(text.Contains("最短 无限制 / 最长 35") && text.Contains("最低 30.0 / 最高 无限制"), "Asymmetric bounds lost in report.");
                Require(text.Contains("偏好产物 无限制") && text.Contains("请求 4 对"), "Preference or finite output count missing from report.");
            });

            test("Broad sampling reaches range endpoints and does not retain the old 60 nt cap", delegate
            {
                bool sampled; int[] values = DesignEngine.SamplePrimerLengths(1, 20000, out sampled);
                Require(sampled && values.Length <= 128 && values[0] == 1 && values[values.Length - 1] == 20000, "Wide length sampling misses endpoints or is unbounded.");
                for (int length = 1; length <= 61; length++) Require(Array.IndexOf(values, length) >= 0, "Dense normal neighborhood lost.");
                values = DesignEngine.SamplePrimerLengths(20, 60, out sampled);
                Require(!sampled && values.Length == 41 && values[0] == 20 && values[40] == 60, "Existing ordinary ranges were sampled.");
            });

            test("All-unlimited ordinary design remains finite, valid, and explicitly truncated", delegate
            {
                string dna = Synthetic(280, 42017); DesignSettings settings = Unlimited(); settings.MaxPairs = 3;
                DesignResult result = DesignEngine.Design(SequenceParser.Parse(dna), settings, null, CancellationToken.None);
                Require(result.Pairs.Count > 0 && result.Pairs.Count <= 3, "Finite requested result count was lost.");
                Require(result.SearchTruncated && String.Join("\n", result.Notes).Contains("抽样"), "Wide finite search not disclosed.");
                Require(settings.PrimerMin == 30 && settings.PrimerMax == 35, "Design changed stored numeric settings.");
                foreach (PrimerPair pair in result.Pairs)
                {
                    Require(pair.Forward.End < pair.Reverse.Start, "Unlimited range allowed overlapping primers.");
                    Require(pair.AmpliconStart >= 1 && pair.AmpliconEnd <= dna.Length && pair.AmpliconLength == pair.AmpliconSequence.Length, "Product exceeds physical template.");
                }
            });

            test("SNP artificial mismatch skips unavailable short positions and keeps finite output", delegate
            {
                string dna = Synthetic(170, 9917); int snp = 70;
                string annotation = dna.Substring(0, snp - 1) + "[A>C]" + dna.Substring(snp);
                DesignSettings settings = Unlimited(); settings.PrimerMaxUnlimited = false; settings.PrimerMax = 20; settings.MaxPairs = 3;
                var options = new SnpDesignSettings { Base = settings, ExtraMismatchFromThreePrime = 3 };
                SnpDesignResult result = SnpDesignEngine.Design(SnpParser.Parse(annotation), options, null, CancellationToken.None);
                Require(result.Sets.Count > 0 && result.Sets.Count <= 3, "SNP design failed with disabled lower length bound.");
                foreach (SnpPrimerSet set in result.Sets)
                {
                    Require(set.ReferenceForward.Sequence.Length >= 3 && set.ExtraMismatchPosition == snp - 2, "Mismatch index is outside short primer.");
                    Require(set.ReferenceForward.End == snp && set.CommonReverse.Start > snp && set.ControlForward.End < snp, "SNP topology changed.");
                }
                settings.PrimerMaxUnlimited = true;
                SnpDesignResult broad = SnpDesignEngine.Design(SnpParser.Parse(annotation), options, null, CancellationToken.None);
                Require(broad.Sets.Count > 0 && broad.Sets.Count <= 3 && broad.SearchTruncated, "All-unlimited SNP search did not remain finite/disclosed.");
            });

            test("Linear-memory hairpin metric agrees with independent stem enumeration", delegate
            {
                MethodInfo hairpin = typeof(DesignEngine).GetMethod("HairpinStem", BindingFlags.Static | BindingFlags.NonPublic);
                Require(hairpin != null, "Hairpin entry point missing.");
                for (int seed = 0; seed < 100; seed++)
                {
                    string dna = Synthetic(1 + seed, 713 + seed);
                    int actual = (int)hairpin.Invoke(null, new object[] { dna });
                    Require(actual == BruteHairpin(dna), "Hairpin recurrence changed at seed " + seed + ".");
                }
            });

            test("Broad searches and long structure evaluation can be cancelled", delegate
            {
                using (var cancel = new CancellationTokenSource())
                {
                    bool cancelled = false;
                    Action<int, string> progress = delegate(int percent, string message) { cancel.Cancel(); };
                    try { DesignEngine.Design(SequenceParser.Parse(Synthetic(20000, 32)), Unlimited(), progress, cancel.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "Broad search ignored cancellation.");
                }
                using (var cancel = new CancellationTokenSource())
                {
                    bool cancelled = false;
                    var primer = new Primer { Sequence = Repeat("ACGT", 5000) };
                    cancel.CancelAfter(15);
                    try { int a, b; DesignEngine.PairStructurePenalty(primer, primer, out a, out b, cancel.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "Long dynamic-programming loop ignored cancellation.");
                }
            });

            report.Add(""); report.Add("Passed: " + passed); report.Add("Failed: " + failed);
            string folder = Path.GetDirectoryName(Path.GetFullPath(reportPath)); Directory.CreateDirectory(folder);
            File.WriteAllLines(reportPath, report.ToArray(), new UTF8Encoding(true));
            return failed == 0 ? 0 : 1;
        }

        private static DesignSettings Unlimited()
        {
            return new DesignSettings { PrimerMinUnlimited = true, PrimerMaxUnlimited = true, GcMinUnlimited = true,
                GcMaxUnlimited = true, AmpliconMinUnlimited = true, AmpliconMaxUnlimited = true, PreferredAmpliconUnlimited = true };
        }
        private static string Synthetic(int length, int seed)
        {
            var random = new Random(seed); var text = new StringBuilder(length);
            for (int i = 0; i < length; i++) text.Append("ACGT"[random.Next(4)]);
            return text.ToString();
        }
        private static string Repeat(string unit, int count)
        {
            var text = new StringBuilder(unit.Length * count);
            for (int i = 0; i < count; i++) text.Append(unit);
            return text.ToString();
        }
        private static int BruteHairpin(string dna)
        {
            int best = 0;
            for (int left = 0; left < dna.Length; left++)
                for (int right = left + 4; right < dna.Length; right++)
                {
                    int length = 0;
                    while (right - left - 2 * length >= 4 && Complement(dna[left + length], dna[right - length])) length++;
                    best = Math.Max(best, length);
                }
            return best;
        }
        private static bool Complement(char a, char b)
        {
            return (a == 'A' && b == 'T') || (a == 'T' && b == 'A') || (a == 'G' && b == 'C') || (a == 'C' && b == 'G');
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void ExpectArgument(Action action)
        {
            try { action(); } catch (ArgumentException) { return; }
            throw new InvalidOperationException("Expected invalid enabled option to be rejected.");
        }
    }
}
