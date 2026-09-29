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
    // Dependency-free integration tests; all template DNA below is synthetic.
    public static class SelfTests
    {
        public static int Run(string reportPath)
        {
            var report = new List<string>();
            int passed = 0;
            int failed = 0;
            string template = SyntheticSequence(480, 20260920);
            DesignResult defaultResult = null;
            report.Add("RPA Designer self-test report");
            report.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            report.Add("Templates are deterministic, artificial sequences without a biological source.");
            report.Add("");

            Action<string, Action> test = delegate(string name, Action body)
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    body();
                    passed++;
                    report.Add("PASS  " + name + " (" + timer.ElapsedMilliseconds + " ms)");
                }
                catch (Exception ex)
                {
                    failed++;
                    report.Add("FAIL  " + name + " (" + timer.ElapsedMilliseconds + " ms)");
                    report.Add("      " + ex.GetType().Name + ": " + ex.Message);
                    report.Add(ex.StackTrace ?? "");
                }
            };

            test("Reverse primer orientation", delegate
            {
                Equal("CGACTT", DesignEngine.ReverseComplement("AAGTCG"), "Known reverse complement");
                Equal(template, DesignEngine.ReverseComplement(DesignEngine.ReverseComplement(template)), "Double reverse complement");
            });

            test("Single FASTA record and lowercase input", delegate
            {
                ParsedSequence parsed = SequenceParser.Parse(">synthetic_reference description\r\nacgtacgt\r\n\r\nTTGC\r\n");
                Equal("ACGTACGTTTGC", parsed.Sequence, "FASTA sequence");
                Require(!String.IsNullOrWhiteSpace(parsed.Name), "FASTA name must be preserved.");
                Require(parsed.Name.IndexOf("synthetic_reference", StringComparison.Ordinal) >= 0, "FASTA identifier was lost.");
            });

            test("Numbered raw sequence keeps bases and reports cleanup", delegate
            {
                ParsedSequence parsed = SequenceParser.Parse("1 acgt acgt 8\r\n9 TTGC 12\r\n");
                Equal("ACGTACGTTTGC", parsed.Sequence, "Numbered sequence");
                Require(parsed.Warnings != null && parsed.Warnings.Count > 0, "Removed numbers must be reported.");
            });

            test("IUPAC ambiguity preserves every coordinate", delegate
            {
                const string ambiguous = "ACGTRYSWKMBDHVNACGT";
                ParsedSequence parsed = SequenceParser.Parse(ambiguous.ToLowerInvariant());
                Equal(ambiguous, parsed.Sequence, "Ambiguity must not be silently removed");
                Require(parsed.Sequence.Length == ambiguous.Length, "Ambiguous bases changed sequence coordinates.");
            });

            test("RNA U is converted to T with a warning", delegate
            {
                ParsedSequence parsed = SequenceParser.Parse("auGcUu");
                Equal("ATGCTT", parsed.Sequence, "U conversion");
                Require(parsed.Warnings != null && parsed.Warnings.Count > 0, "U conversion must be reported.");
            });

            test("Malformed and empty sequence input is rejected", delegate
            {
                ExpectArgument(delegate { SequenceParser.Parse(""); }, "Empty input");
                ExpectArgument(delegate { SequenceParser.Parse("  \r\n  "); }, "Whitespace-only input");
                ExpectArgument(delegate { SequenceParser.Parse(">only_a_header\r\n"); }, "Header without bases");
                ExpectArgument(delegate { SequenceParser.Parse("ACGT$ACGT"); }, "Invalid punctuation");
                ExpectArgument(delegate { SequenceParser.Parse("ACGTXACGT"); }, "Invalid base");
            });

            test("Multiple FASTA records cannot be concatenated", delegate
            {
                ExpectArgument(delegate { SequenceParser.Parse(">one\nACGTACGT\n>two\nTGCATGCA\n"); }, "Multiple records");
            });

            test("Sequence length limit accepts 20,000 and rejects 20,001 bases", delegate
            {
                ParsedSequence boundary = SequenceParser.Parse(new string('A', 20000));
                Require(boundary.Sequence.Length == 20000, "The documented boundary must be accepted.");
                ExpectArgument(delegate { SequenceParser.Parse(new string('A', 20001)); }, "Sequence over the supported limit");
                ExpectArgument(delegate { SequenceParser.Parse(new string(' ', 1000000) + "A"); }, "Raw input over one million characters");
            });

            test("Short tandem repeats are measured across complete repeat units", delegate
            {
                CheckTandem("ATATAT", 6);
                CheckTandem("ATATATATATAT", 12);
                CheckTandem("ACGACGACG", 9);
                CheckTandem("ACGTACGTACGT", 12);
                CheckTandem("ACGACG", 0);
            });

            test("Known antiparallel structures and minimum hairpin loop", delegate
            {
                CheckComplement("AAAA", "TTTT", 4, 4);
                CheckComplement("AAAA", "CCCC", 0, 0);
                // The AAA/TTT tract is internal to both oligos: neither 3-prime end participates.
                CheckComplement("CAAAC", "CTTTC", 3, 0);
                // Only one 3-prime end participates. This must still count as end complementarity.
                CheckComplement("CCCAAA", "CTTTC", 3, 3);
                CheckComplement("CTTTC", "CCCAAA", 3, 3);
                CheckHairpin("GCGAAACGC", 3);
                CheckHairpin("GCAAAGC", 2);
                CheckHairpin("GCAAGC", 1); // Two-base loop cannot support a two-base stem.
                CheckHairpin("AAAAAAA", 0);
            });

            test("Default design returns valid complete amplicons", delegate
            {
                DesignSettings settings = StandardSettings();
                DesignResult result = Design(template, settings);
                defaultResult = result;
                Require(result.Pairs.Count > 0, "Synthetic default template produced no candidates.");
                ValidateResult(result, template, settings);
                Require(result.ForwardCandidateCount > 0 && result.ReverseCandidateCount > 0, "Candidate statistics must include both directions.");
            });

            test("Requested target is strictly flanked on both sides", delegate
            {
                DesignSettings settings = StandardSettings();
                settings.TargetStart = 200;
                settings.TargetEnd = 225;
                DesignResult result = Design(template, settings);
                Require(result.Pairs.Count > 0, "Synthetic target region produced no candidates.");
                ValidateResult(result, template, settings);
                foreach (PrimerPair pair in result.Pairs)
                {
                    Require(pair.Forward.End < settings.TargetStart, "Forward primer overlaps the protected target.");
                    Require(pair.Reverse.Start > settings.TargetEnd, "Reverse primer overlaps the protected target.");
                }
            });

            test("Ambiguous windows are skipped without shifting coordinates", delegate
            {
                string sequence = template.Substring(0, 137) + "NNRYSWKMBDHVN" + template.Substring(150);
                DesignSettings settings = StandardSettings();
                DesignResult result = Design(sequence, settings);
                Require(result.Pairs.Count > 0, "Ambiguity-free regions should still yield candidates.");
                Equal(sequence, result.Input.Sequence, "Input coordinates must be retained");
                ValidateResult(result, sequence, settings);
                foreach (PrimerPair pair in result.Pairs)
                {
                    Require(OnlyAcgt(pair.Forward.Sequence) && OnlyAcgt(pair.Reverse.Sequence), "A primer includes an ambiguous base.");
                }
            });

            test("Settings reject inconsistent bounds and target coordinates", delegate
            {
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.PrimerMin = 36; s.PrimerMax = 30; }, "Primer range");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.AmpliconMin = 201; s.AmpliconMax = 100; }, "Product range");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.GcMin = 80; s.GcMax = 20; }, "GC range");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.GcMin = -1; }, "Negative GC percentage");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.GcMax = 101; }, "GC percentage over 100");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.MaxPairs = 0; }, "Zero result count");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.TargetStart = 0; s.TargetEnd = 100; }, "Half-specified target");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.TargetStart = 220; s.TargetEnd = 200; }, "Reversed target");
                CheckInvalidSettings(template, delegate(DesignSettings s) { s.TargetStart = 450; s.TargetEnd = 481; }, "Target outside sequence");
            });

            test("No-result sequences return explanations", delegate
            {
                foreach (char basis in new char[] { 'N', 'A' })
                {
                    DesignResult result = Design(new string(basis, 300), StandardSettings());
                    Require(result.Pairs != null && result.Pairs.Count == 0, "All-" + basis + " template must not yield usable primers.");
                    Require(result.Notes != null && result.Notes.Count > 0, "No-result design needs an explanation.");
                }
            });

            test("Repeated binding sites are reported", delegate
            {
                string block = SyntheticSequence(180, 3107);
                string repeated = block + block + block;
                DesignSettings settings = StandardSettings();
                DesignResult result = Design(repeated, settings);
                ValidateResult(result, repeated, settings);
                Require(result.Pairs.Count > 0, "Repeated synthetic template must exercise returned-primer warnings.");
                foreach (PrimerPair pair in result.Pairs)
                {
                    Require(HasRepeatMessage(pair.Forward.Warnings), "Repeated forward-primer binding was not reported.");
                    Require(HasRepeatMessage(pair.Reverse.Warnings), "Repeated reverse-primer binding was not reported.");
                }
            });

            test("Pre-cancelled computation stops before returning results", delegate
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    bool cancelled = false;
                    try { DesignEngine.Design(SequenceParser.Parse(template), StandardSettings(), null, cancellation.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "The design engine ignored a cancelled token.");
                }
            });

            test("Cancellation after progress stops an active calculation", delegate
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    bool reached = false;
                    bool completed = false;
                    bool cancelled = false;
                    Action<int, string> progress = delegate(int percent, string message)
                    {
                        Require(percent >= 0 && percent <= 100, "Progress is outside 0-100.");
                        if (percent == 100) completed = true;
                        if (percent >= 55) { reached = true; cancellation.Cancel(); }
                    };
                    try { DesignEngine.Design(SequenceParser.Parse(template), StandardSettings(), progress, cancellation.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(reached && cancelled && !completed, "Cancellation during calculation must stop before completion.");
                }
            });

            test("CSV round-trip escapes quotes, commas, line breaks and formulas", delegate
            {
                DesignResult result = defaultResult ?? Design(template, StandardSettings());
                string originalName = result.Input.Name;
                CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
                const string unusualName = "=SUM(1,2) \"quoted\"\r\nsecond line";
                try
                {
                    result.Input.Name = unusualName;
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                    List<List<string>> rows = ParseCsv(ReportWriter.Csv(result));
                    Require(rows.Count == result.Pairs.Count * 2 + 1, "CSV must contain exactly two records per primer pair.");
                    Require(rows[0].Count == 24, "CSV header must have the documented 24 columns.");
                    for (int i = 0; i < result.Pairs.Count; i++)
                    {
                        for (int direction = 0; direction < 2; direction++)
                        {
                            List<string> row = rows[1 + i * 2 + direction];
                            Primer primer = direction == 0 ? result.Pairs[i].Forward : result.Pairs[i].Reverse;
                            Require(row.Count == rows[0].Count, "CSV row was broken by an embedded comma or newline.");
                            Equal("'" + unusualName, row[0], "Untrusted formula-like FASTA name must be neutralized");
                            Equal(direction == 0 ? "F" : "R", row[2], "CSV primer direction");
                            Equal(primer.Sequence, row[3], "CSV synthesized primer sequence");
                            Equal(primer.Gc.ToString("0.0", CultureInfo.InvariantCulture), row[7], "CSV decimal formatting");
                            Equal(result.Pairs[i].AmpliconSequence, row[19], "CSV full amplicon");
                            Require(row[20].Contains("需验证") && row[20].Contains("未做基因组特异性检索"), "CSV must retain interpretation limits.");
                            Equal(primer.TandemRepeat.ToString(CultureInfo.InvariantCulture), row[23], "CSV tandem repeat span");
                        }
                    }
                }
                finally
                {
                    result.Input.Name = originalName;
                    Thread.CurrentThread.CurrentCulture = originalCulture;
                }
            });

            test("Text and FASTA exports retain sequences and interpretation limits", delegate
            {
                DesignResult result = defaultResult ?? Design(template, StandardSettings());
                string text = ReportWriter.TextReport(result);
                Require(text.Contains("分数不是成功率") && text.Contains("未进行全基因组") && text.Contains("未计算热力学"), "Text report must disclose score, specificity and structure limitations.");
                Require(text.Contains("不是 RPA 反应温度") && text.Contains("不含 exo/nfo"), "Text report must explain Tm and probe limitations.");
                Require(text.Contains(result.Input.Sequence), "Text report lost the complete normalized input.");
                string[] fasta = ReportWriter.Fasta(result).Replace("\r", "").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Require(fasta.Length == result.Pairs.Count * 4, "Each exported pair needs two FASTA records.");
                for (int i = 0; i < result.Pairs.Count; i++)
                {
                    Require(fasta[i * 4].StartsWith(">candidate_" + (i + 1) + "_F ", StringComparison.Ordinal), "Forward FASTA record is mislabeled.");
                    Equal(result.Pairs[i].Forward.Sequence, fasta[i * 4 + 1], "Forward FASTA oligo");
                    Require(fasta[i * 4 + 2].StartsWith(">candidate_" + (i + 1) + "_R ", StringComparison.Ordinal), "Reverse FASTA record is mislabeled.");
                    Equal(result.Pairs[i].Reverse.Sequence, fasta[i * 4 + 3], "Reverse FASTA oligo");
                }
            });

            report.Add("");
            report.Add("Passed: " + passed.ToString(CultureInfo.InvariantCulture));
            report.Add("Failed: " + failed.ToString(CultureInfo.InvariantCulture));
            report.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            try
            {
                string absolutePath = Path.GetFullPath(reportPath);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(absolutePath, report.ToArray(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not write self-test report: " + ex.Message);
                return 1;
            }
            return failed == 0 ? 0 : 1;
        }

        private static DesignSettings StandardSettings()
        {
            return new DesignSettings { MaxPairs = 6 };
        }

        private static DesignResult Design(string sequence, DesignSettings settings)
        {
            return DesignEngine.Design(SequenceParser.Parse(">artificial_self_test\n" + sequence), settings, null, CancellationToken.None);
        }

        private static void ValidateResult(DesignResult result, string template, DesignSettings settings)
        {
            Require(result != null && result.Pairs != null, "Result and pair collection must be initialized.");
            Require(result.Pairs.Count <= settings.MaxPairs, "Requested result limit was exceeded.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.Pairs.Count; i++)
            {
                PrimerPair pair = result.Pairs[i];
                Require(pair.Rank == i + 1, "Ranks must be consecutive and 1-based.");
                ValidatePrimer(pair.Forward, template, settings, false);
                ValidatePrimer(pair.Reverse, template, settings, true);
                Require(pair.Forward.End < pair.Reverse.Start, "Primer binding regions overlap or have the wrong order.");
                Require(pair.AmpliconStart == pair.Forward.Start, "Product must start at forward primer's 5-prime coordinate.");
                Require(pair.AmpliconEnd == pair.Reverse.End, "Product must end at reverse primer's 5-prime coordinate.");
                Require(pair.AmpliconLength == pair.AmpliconEnd - pair.AmpliconStart + 1, "Product length is not inclusive.");
                Require(pair.AmpliconLength >= settings.AmpliconMin && pair.AmpliconLength <= settings.AmpliconMax, "Product is outside the requested length range.");
                Equal(template.Substring(pair.AmpliconStart - 1, pair.AmpliconLength), pair.AmpliconSequence, "Exact product sequence");
                Require(pair.AmpliconSequence.StartsWith(pair.Forward.Sequence, StringComparison.Ordinal), "Product does not begin with forward primer.");
                Require(pair.AmpliconSequence.EndsWith(DesignEngine.ReverseComplement(pair.Reverse.Sequence), StringComparison.Ordinal), "Product does not end with reverse primer binding sequence.");
                Require(!Double.IsNaN(pair.Score) && !Double.IsInfinity(pair.Score), "Pair score must be finite.");
                Require(pair.CrossComplement >= 0 && pair.CrossThreePrime >= 0, "Complementarity counts cannot be negative.");
                string key = pair.Forward.Start + ":" + pair.Forward.End + ":" + pair.Reverse.Start + ":" + pair.Reverse.End;
                Require(unique.Add(key), "An identical primer pair was emitted more than once.");
            }
        }

        private static void ValidatePrimer(Primer primer, string template, DesignSettings settings, bool reverse)
        {
            Require(primer != null, "Primer must be initialized.");
            Require(primer.Start >= 1 && primer.End >= primer.Start && primer.End <= template.Length, "Primer coordinates must be 1-based, inclusive, and in range.");
            int length = primer.End - primer.Start + 1;
            Require(length == primer.Sequence.Length, "Primer sequence length and coordinates disagree.");
            Require(length >= settings.PrimerMin && length <= settings.PrimerMax, "Primer length violates settings.");
            string expected = template.Substring(primer.Start - 1, length);
            if (reverse) expected = DesignEngine.ReverseComplement(expected);
            Equal(expected, primer.Sequence, reverse ? "Reverse primer 5-prime to 3-prime sequence" : "Forward primer 5-prime to 3-prime sequence");
            int gc = 0;
            foreach (char basis in primer.Sequence) if (basis == 'G' || basis == 'C') gc++;
            double actualGc = 100.0 * gc / length;
            Require(Math.Abs(primer.Gc - actualGc) < 0.11, "Reported GC percentage is inconsistent with primer bases.");
            Require(actualGc + 0.000001 >= settings.GcMin && actualGc - 0.000001 <= settings.GcMax, "Primer GC content violates settings.");
            Require(!Double.IsNaN(primer.Tm) && !Double.IsInfinity(primer.Tm), "Primer Tm must be finite.");
            Require(primer.Hairpin >= 0 && primer.SelfComplement >= 0 && primer.SelfThreePrime >= 0, "Structure metrics cannot be negative.");
        }

        private static void CheckInvalidSettings(string template, Action<DesignSettings> change, string label)
        {
            DesignSettings settings = StandardSettings();
            change(settings);
            ExpectArgument(delegate { Design(template, settings); }, label);
        }

        private static void CheckComplement(string a, string b, int expectedLongest, int expectedThreePrime)
        {
            MethodInfo method = typeof(DesignEngine).GetMethod("ComplementMetrics", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method != null, "Complementarity metric entry point was not found.");
            object[] arguments = new object[] { a, b, 0, 0 };
            method.Invoke(null, arguments);
            Require((int)arguments[2] == expectedLongest && (int)arguments[3] == expectedThreePrime,
                "Known oligo pair " + a + "/" + b + " expected " + expectedLongest + "/" + expectedThreePrime +
                " but returned " + arguments[2] + "/" + arguments[3] + ".");
        }

        private static void CheckHairpin(string sequence, int expected)
        {
            MethodInfo method = typeof(DesignEngine).GetMethod("HairpinStem", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method != null, "Hairpin metric entry point was not found.");
            int actual = (int)method.Invoke(null, new object[] { sequence });
            Require(actual == expected, "Known hairpin " + sequence + " expected stem " + expected + " but returned " + actual + ".");
        }

        private static void CheckTandem(string sequence, int expected)
        {
            MethodInfo method = typeof(DesignEngine).GetMethod("TandemRepeatSpan", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method != null, "Tandem repeat metric entry point was not found.");
            int actual = (int)method.Invoke(null, new object[] { sequence });
            Require(actual == expected, "Known tandem repeat " + sequence + " expected span " + expected + " but returned " + actual + ".");
        }

        // Small independent RFC 4180 reader verifies actual exported record boundaries.
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Length = 0;
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(c);
            }
            Require(!quoted, "CSV contains an unterminated quoted field.");
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        private static void ExpectArgument(Action body, string label)
        {
            bool rejected = false;
            try { body(); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, label + " must throw ArgumentException.");
        }

        private static bool OnlyAcgt(string sequence)
        {
            foreach (char basis in sequence) if ("ACGT".IndexOf(basis) < 0) return false;
            return true;
        }

        private static bool HasRepeatMessage(List<string> messages)
        {
            if (messages == null) return false;
            foreach (string message in messages)
            {
                string text = (message ?? "").ToLowerInvariant();
                if (text.Contains("重复") || text.Contains("多重") || text.Contains("多处") ||
                    text.Contains("多个") || text.Contains("repeat") || text.Contains("multiple")) return true;
            }
            return false;
        }

        private static string SyntheticSequence(int length, int seed)
        {
            var random = new Random(seed);
            var sequence = new StringBuilder(length);
            const string alphabet = "ACGT";
            for (int i = 0; i < length; i++) sequence.Append(alphabet[random.Next(alphabet.Length)]);
            return sequence.ToString();
        }

        private static void Equal(string expected, string actual, string label)
        {
            Require(String.Equals(expected, actual, StringComparison.Ordinal), label + " differs. Expected: " + expected + "; actual: " + actual);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
