using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RpaDesigner
{
    // Artificial DNA only. These checks establish software invariants, not assay performance.
    public static class LampSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("RPA / LAMP Designer LAMP self-test report");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            lines.Add("Deterministic artificial DNA. Software checks do not establish experimental SNP selectivity.");
            lines.Add("");
            Action<string, Action> test = delegate(string title, Action action)
            {
                var timer = Stopwatch.StartNew();
                try { action(); passed++; lines.Add("PASS  " + title + " (" + timer.ElapsedMilliseconds + " ms)"); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + title + " (" + timer.ElapsedMilliseconds + " ms): " + ex.Message); lines.Add(ex.StackTrace ?? ""); }
            };
            string template = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
            LampDesignResult ordinary = null, forward = null, backward = null;

            test("Nearest-neighbor values independently derived from SantaLucia 1998 Table 2", delegate
            {
                // Non-self: H=-166.4 kcal/mol, S=-450.1 cal/(K mol), 19 NN steps.
                Near(57.0043324725905, LampThermodynamics.MeltingTemperature("CGTTGACGTTGACGTTGACG", 50, 250), 0.001, "Non-self complementary Tm");
                // Self: H=-161.2, S=-438.4 including symmetry; concentration divisor is 1.
                Near(56.1305499377993, LampThermodynamics.MeltingTemperature("ACGTACGTACGTACGTACGT", 50, 250), 0.001, "Self-complementary Tm");
                Near(LampThermodynamics.MeltingTemperature("CGTTGACGTTGACGTTGACG", 50, 250),
                    LampThermodynamics.MeltingTemperature(Rc("CGTTGACGTTGACGTTGACG"), 50, 250), 0.00001, "Reverse-complement Tm invariance");
            });
            test("Default ordinary LAMP returns a valid six-region, four-primer core", delegate
            {
                var settings = new LampDesignSettings(); settings.MaxSets = 2;
                ordinary = LampDesignEngine.Design(SequenceParser.Parse(template), settings, null, CancellationToken.None);
                Require(ordinary.Sets.Count > 0, "Default artificial demo produced no candidates."); Validate(ordinary);
            });
            test("Optional loop primers map to the correct intervening strands", delegate
            {
                var settings = Broad(); settings.IncludeLoops = true;
                var result = LampDesignEngine.Design(SequenceParser.Parse(template), settings, null, CancellationToken.None);
                Require(result.Sets.Count > 0, "Loop fixture has no core candidate."); Validate(result);
                bool hasLoop = false;
                foreach (LampPrimerSet set in result.Sets) hasLoop |= set.LF != null || set.LB != null;
                Require(hasLoop, "Broad artificial fixture must exercise at least one actual loop primer.");
            });
            test("All 12 directed SNP substitutions retain both alleles with FIP and BIP orientation", delegate
            {
                foreach (string side in new string[] { "FIP", "BIP" })
                foreach (char reference in "ACGT") foreach (char alternate in "ACGT")
                {
                    if (reference == alternate) continue;
                    var settings = Broad(); settings.SnpOrientation = side; settings.MaxSets = 1;
                    var result = DesignSnp(template, 300, reference, alternate, settings);
                    Require(result.Sets.Count > 0, side + " " + reference + ">" + alternate + " yielded no synthetic candidate.");
                    Validate(result);
                    foreach (LampPrimerSet set in result.Sets) Equal(side, set.SpecificInner, "Requested SNP orientation");
                    if (reference == 'A' && alternate == 'C') { if (side == "FIP") forward = result; else backward = result; }
                }
            });
            test("Second- and third-from-end mismatches alter actual oligos without altering template", delegate
            {
                foreach (string side in new string[] { "FIP", "BIP" }) foreach (int offset in new int[] { 2, 3 })
                {
                    var settings = Broad(); settings.SnpOrientation = side; settings.ExtraMismatchFromThreePrime = offset;
                    var result = DesignSnp(template, 300, 'G', 'T', settings);
                    Require(result.Sets.Count > 0, "Mismatch fixture has no candidate: " + side + "/" + offset); Validate(result);
                    ValidateReports(result);
                }
            });
            test("Automatic SNP direction returns supported, correctly mapped candidates", delegate
            {
                var result = DesignSnp(template, 300, 'C', 'G', Broad());
                Require(result.Sets.Count > 0, "Auto mode produced no candidate."); Validate(result);
            });
            test("No-loop mode, count cap, explicit GC/Tm and target span bounds are enforced", delegate
            {
                var settings = Broad(); settings.GcMin = 40; settings.GcMax = 65; settings.AnnealTmMin = 45; settings.AnnealTmMax = 70;
                settings.InnerTmMin = 45; settings.InnerTmMax = 70; settings.SpanMin = 180; settings.SpanMax = 230; settings.MaxSets = 1;
                var result = LampDesignEngine.Design(SequenceParser.Parse(template), settings, null, CancellationToken.None);
                Require(result.Sets.Count > 0, "Constrained synthetic fixture has no candidate."); Validate(result);
                Require(result.Sets[0].LF == null && result.Sets[0].LB == null, "Disabled loop option still emitted a loop primer.");
            });
            test("SNPs at either input boundary produce no fabricated flank", delegate
            {
                foreach (int position in new int[] { 1, template.Length })
                {
                    var result = DesignSnp(template, position, 'A', 'C', Broad());
                    Require(result.Sets.Count == 0, "Boundary SNP produced a geometrically impossible candidate.");
                }
            });
            test("Ambiguous or homopolymer templates give no candidates", delegate
            {
                foreach (char symbol in new char[] { 'N', 'A' })
                {
                    var result = LampDesignEngine.Design(SequenceParser.Parse(new string(symbol, 400)), Broad(), null, CancellationToken.None);
                    Require(result.Sets.Count == 0, "Unsupported low-information sequence yielded primers.");
                }
            });
            test("Invalid settings are rejected instead of silently changed", delegate
            {
                Action<LampDesignSettings> run = delegate(LampDesignSettings options) { LampDesignEngine.Design(SequenceParser.Parse(template), options, null, CancellationToken.None); };
                var settings = Broad(); settings.RegionMin = 25; settings.RegionMax = 18; ExpectArgument(delegate { run(settings); });
                settings = Broad(); settings.SpanMin = 300; settings.SpanMax = 120; ExpectArgument(delegate { run(settings); });
                settings = Broad(); settings.GcMin = 70; settings.GcMax = 30; ExpectArgument(delegate { run(settings); });
                settings = Broad(); settings.AnnealTmMin = 70; settings.AnnealTmMax = 50; ExpectArgument(delegate { run(settings); });
                settings = Broad(); settings.MaxSets = 0; ExpectArgument(delegate { run(settings); });
                settings = Broad(); settings.MonovalentMilliMolar = 0; ExpectArgument(delegate { run(settings); });
                settings = Broad(); settings.SnpOrientation = "sideways"; ExpectArgument(delegate { DesignSnp(template, 300, 'A', 'C', settings); });
                settings = Broad(); settings.ExtraMismatchFromThreePrime = 1; ExpectArgument(delegate { DesignSnp(template, 300, 'A', 'C', settings); });
            });
            test("Pre-cancelled search exits through cancellation", delegate
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel(); bool caught = false;
                    try { LampDesignEngine.Design(SequenceParser.Parse(template), Broad(), null, cancellation.Token); }
                    catch (OperationCanceledException) { caught = true; }
                    Require(caught, "Cancellation was ignored.");
                }
            });
            test("Cancellation during a reported design stage exits promptly", delegate
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    bool caught = false; int callbacks = 0;
                    try
                    {
                        LampDesignEngine.Design(SequenceParser.Parse(template), Broad(), delegate(int percent, string text)
                        { callbacks++; cancellation.Cancel(); }, cancellation.Token);
                    }
                    catch (OperationCanceledException) { caught = true; }
                    Require(callbacks > 0 && caught, "In-progress cancellation was ignored.");
                }
            });
            test("Forged SNP models cannot bypass allele and coordinate consistency", delegate
            {
                SnpInput malformed = SnpParser.Parse(template.Substring(0, 299) + "[A>C]" + template.Substring(300));
                malformed.Position = 0;
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(malformed, Broad(), null, CancellationToken.None); });
                malformed = SnpParser.Parse(template.Substring(0, 299) + "[A>C]" + template.Substring(300));
                var bases = malformed.Alternate.Sequence.ToCharArray(); bases[10] = bases[10] == 'A' ? 'C' : 'A';
                malformed.Alternate.Sequence = new string(bases);
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(malformed, Broad(), null, CancellationToken.None); });
                ExpectArgument(delegate { LampDesignEngine.Design(null, Broad(), null, CancellationToken.None); });
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(null, Broad(), null, CancellationToken.None); });
            });
            test("Nonfinite thermodynamic settings and ambiguous NN sequences are rejected", delegate
            {
                foreach (double value in new double[] { 0, -1, Double.NaN, Double.PositiveInfinity })
                {
                    double current = value;
                    ExpectArgument(delegate { LampThermodynamics.MeltingTemperature("ACGT", current, 250); });
                    ExpectArgument(delegate { LampThermodynamics.MeltingTemperature("ACGT", 50, current); });
                }
                ExpectArgument(delegate { LampThermodynamics.MeltingTemperature("ACNT", 50, 250); });
                ExpectArgument(delegate { LampThermodynamics.MeltingTemperature("A", 50, 250); });
                var settings = Broad(); settings.GcMin = Double.NaN;
                ExpectArgument(delegate { LampDesignEngine.Design(SequenceParser.Parse(template), settings, null, CancellationToken.None); });
                settings = Broad(); settings.AnnealTmMax = Double.PositiveInfinity;
                ExpectArgument(delegate { LampDesignEngine.Design(SequenceParser.Parse(template), settings, null, CancellationToken.None); });
            });
            test("SNP annotation parser rejects malformed or multiple markers", delegate
            {
                foreach (string value in new string[] { "ACG[A>A]CG", "ACG[AT>C]CG", "ACG[A>N]CG", "ACG[A>C]CG[G>T]", "ACG[A>C", "ACGT" })
                { string current = value; ExpectArgument(delegate { SnpParser.Parse(current); }); }
            });
            test("SNP ordering, HTML and target templates mark only real SNP bases", delegate
            {
                Require(forward != null && backward != null, "Previous SNP fixtures did not complete."); ValidateReports(forward); ValidateReports(backward);
            });
            test("Ordinary report never acquires stale SNP colors", delegate
            {
                Require(ordinary != null && ordinary.Sets.Count > 0, "Ordinary fixture unavailable."); ValidateReports(ordinary);
                Require(LampReportWriter.HighlightedTextReport(ordinary).SnpHighlights.Count == 0, "Ordinary report has SNP marks.");
                Require(!LampReportWriter.Html(ordinary).Contains("<span class=\"snp\">"), "Ordinary HTML has highlighted bases.");
            });
            test("HTML escapes hostile metadata and CSV protects spreadsheet formula prefixes", delegate
            {
                Require(backward != null, "SNP fixture unavailable."); string original = backward.Input.Name;
                try
                {
                    backward.Input.Name = "<script>alert(1)</script> & \"quoted\"";
                    string html = LampReportWriter.Html(backward);
                    Require(!html.Contains("<script>"), "Raw FASTA title became executable HTML.");
                    Require(html.Contains("&lt;script&gt;alert(1)&lt;/script&gt;"), "Escaped FASTA title missing.");
                    foreach (string title in new string[] { "=1+1", " +SUM(1,2)", "@test", "-2", "\t=1" })
                    {
                        backward.Input.Name = title; List<string[]> rows = ParseCsv(LampReportWriter.Csv(backward));
                        Require(rows.Count > 1, "CSV missing primer rows.");
                        Require(rows[1][0] == "'" + title, "CSV formula guard missing for " + title);
                        foreach (string[] row in rows) Require(row.Length == rows[0].Length, "CSV quoting produced a different column count.");
                    }
                }
                finally { backward.Input.Name = original; }
            });
            test("FASTA contains exact separate 5-to-3 ordering sequences", delegate
            {
                Require(forward != null, "SNP fixture unavailable."); var expected = new List<LampOligo>();
                foreach (LampPrimerSet set in forward.Sets) expected.AddRange(LampReportWriter.Oligos(set));
                string[] rows = LampReportWriter.Fasta(forward).Replace("\r", "").TrimEnd('\n').Split('\n');
                Require(rows.Length == expected.Count * 2, "FASTA primer count differs.");
                for (int i = 0; i < expected.Count; i++) { Require(rows[2 * i].StartsWith(">"), "FASTA header missing."); Equal(expected[i].Sequence, rows[2 * i + 1], "FASTA oligo"); }
            });
            lines.Add(""); lines.Add("Passed: " + passed); lines.Add("Failed: " + failed);
            string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath)); if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }

        private static LampDesignSettings Broad()
        {
            return new LampDesignSettings { RegionMin = 20, RegionMax = 20, SpanMin = 120, SpanMax = 300, MaxSets = 2,
                GcMin = 20, GcMax = 80, AnnealTmMin = 35, AnnealTmMax = 85, InnerTmMin = 35, InnerTmMax = 85, IncludeLoops = false };
        }
        private static LampDesignResult DesignSnp(string source, int position, char reference, char alternate, LampDesignSettings settings)
        {
            return LampDesignEngine.DesignSnp(SnpParser.Parse(source.Substring(0, position - 1) + "[" + reference + ">" + alternate + "]" + source.Substring(position)), settings, null, CancellationToken.None);
        }
        private static void Validate(LampDesignResult result)
        {
            Require(result.Sets.Count <= result.Settings.MaxSets, "Candidate count exceeds requested maximum."); int rank = 0;
            foreach (LampPrimerSet set in result.Sets)
            {
                Require(set.Rank == ++rank, "Ranks must be consecutive."); Require(set.Score >= 0 && set.Score <= 100 && !Double.IsNaN(set.Score), "Invalid score.");
                Require(set.F3 != null && set.B3 != null && set.FIP != null && set.BIP != null, "Core primer missing.");
                Require(set.FIP.Regions.Count == 2 && set.BIP.Regions.Count == 2, "Inner oligos must have two distinct regions.");
                var f3 = set.F3.Regions[0]; var f1 = set.FIP.Regions[0]; var f2 = set.FIP.Regions[1];
                var b1 = set.BIP.Regions[0]; var b2 = set.BIP.Regions[1]; var b3 = set.B3.Regions[0];
                Require(!f3.Reverse && f1.Reverse && !f2.Reverse && !b1.Reverse && b2.Reverse && b3.Reverse, "Core strand orientation is wrong.");
                Require(f3.End < f2.Start && f2.End < f1.Start && f1.End < b1.Start && b1.End < b2.Start && b2.End < b3.Start, "Six core regions overlap or are out of order.");
                Require(f1.Start - f2.Start >= 40 && f1.Start - f2.Start <= 60, "Forward loop span must include F2 but exclude F1.");
                Require(b2.End - b1.End >= 40 && b2.End - b1.End <= 60, "Backward loop span must include B2 but exclude B1.");
                Require(b2.End - f2.Start + 1 >= result.Settings.CoreSpanMin && b2.End - f2.Start + 1 <= result.Settings.CoreSpanMax, "F2-B2 inclusive span invalid.");
                Require(f2.Start - f3.End - 1 >= 0 && f2.Start - f3.End - 1 <= 60, "F3-F2 gap invalid.");
                Require(b3.Start - b2.End - 1 >= 0 && b3.Start - b2.End - 1 <= 60, "B2-B3 gap invalid.");
                Require(set.SpanStart == f3.Start && set.SpanEnd == b3.End && set.SpanLength == set.SpanEnd - set.SpanStart + 1, "Outer span coordinates disagree.");
                Require(set.SpanLength >= result.Settings.SpanMin && set.SpanLength <= result.Settings.SpanMax, "Outer span violates requested bounds.");
                Equal(result.Input.Sequence.Substring(set.SpanStart - 1, set.SpanLength), set.ReferenceTemplate, "Original target template");
                if (set.LF != null) { var region = set.LF.Regions[0]; Require(region.Reverse && region.Start > f2.End && region.End < f1.Start, "LF maps outside the forward loop or wrong strand."); }
                if (set.LB != null) { var region = set.LB.Regions[0]; Require(!region.Reverse && region.Start > b1.End && region.End < b2.Start, "LB maps outside the backward loop or wrong strand."); }
                foreach (LampOligo oligo in LampReportWriter.Oligos(set)) ValidateOligo(oligo, set, result);
                if (result.Snp == null)
                { Require(set.AlternateInner == null, "Ordinary design contains an alternative allele oligo."); continue; }
                Require(set.SpecificInner == "FIP" || set.SpecificInner == "BIP", "Unknown SNP inner primer direction.");
                Require(set.AlternateInner != null, "Alternate allele inner primer missing.");
                LampOligo specific = set.SpecificInner == "FIP" ? set.FIP : set.BIP;
                bool reverse = set.SpecificInner == "BIP";
                Require(specific.SnpIndex == specific.Sequence.Length - 1 && set.AlternateInner.SnpIndex == set.AlternateInner.Sequence.Length - 1, "SNP is not the 3-prime terminal oligo base.");
                Require(specific.Sequence[specific.SnpIndex] == (reverse ? Complement(result.Snp.ReferenceAllele) : result.Snp.ReferenceAllele), "Reference terminal allele incorrect.");
                Require(set.AlternateInner.Sequence[set.AlternateInner.SnpIndex] == (reverse ? Complement(result.Snp.AlternateAllele) : result.Snp.AlternateAllele), "Alternate terminal allele incorrect.");
                Equal(specific.Sequence.Substring(0, specific.Sequence.Length - 1), set.AlternateInner.Sequence.Substring(0, set.AlternateInner.Sequence.Length - 1), "Allele oligos must differ only at SNP");
                Equal(result.Snp.Alternate.Sequence.Substring(set.SpanStart - 1, set.SpanLength), set.AlternateTemplate, "Original alternate target template");
                int offset = result.Settings.ExtraMismatchFromThreePrime;
                if (offset == 0) Require(set.ExtraMismatchPosition == 0, "Baseline mode reports an artificial mismatch.");
                else
                {
                    int coordinate = result.Snp.Position + (reverse ? offset - 1 : 1 - offset);
                    Require(set.ExtraMismatchPosition == coordinate, "Mismatch mapped to wrong input coordinate.");
                    char templateBase = result.Input.Sequence[coordinate - 1]; if (reverse) templateBase = Complement(templateBase);
                    Require(set.ExtraMismatchTemplateBase == result.Input.Sequence[coordinate - 1], "Reported template base is not in original plus-strand orientation.");
                    Require(specific.Sequence[specific.Sequence.Length - offset] != templateBase, "Requested artificial mismatch was not introduced.");
                    Require(set.ExtraMismatchPrimerBase == specific.Sequence[specific.Sequence.Length - offset], "Reported mismatch base differs from oligo.");
                }
            }
        }
        private static void ValidateOligo(LampOligo oligo, LampPrimerSet set, LampDesignResult result)
        {
            string actualTemplate = Object.ReferenceEquals(oligo, set.AlternateInner) ? result.Snp.Alternate.Sequence : result.Input.Sequence;
            var expectedFull = new StringBuilder(); var actualFull = new StringBuilder(); bool specific = oligo.SnpIndex >= 0;
            foreach (LampRegion region in oligo.Regions)
            {
                Require(region.Start >= 1 && region.End <= actualTemplate.Length && region.End >= region.Start, "Region outside input.");
                int length = region.End - region.Start + 1; Require(length >= result.Settings.RegionMin && length <= result.Settings.RegionMax, "Region length outside setting.");
                string expected = actualTemplate.Substring(region.Start - 1, length); if (region.Reverse) expected = Rc(expected);
                if (specific && set.ExtraMismatchPosition >= region.Start && set.ExtraMismatchPosition <= region.End)
                {
                    int index = region.Reverse ? region.End - set.ExtraMismatchPosition : set.ExtraMismatchPosition - region.Start;
                    var chars = expected.ToCharArray(); chars[index] = set.ExtraMismatchPrimerBase; expected = new string(chars);
                }
                Equal(expected, region.Sequence, "Binding-region sequence mapping"); expectedFull.Append(expected); actualFull.Append(region.Sequence);
                double gc = Gc(region.Sequence); Near(gc, region.Gc, 0.02, "Region GC");
                Require(gc + 0.001 >= result.Settings.GcMin && gc - 0.001 <= result.Settings.GcMax, "GC limit not enforced.");
                double tm = LampThermodynamics.MeltingTemperature(region.Sequence, result.Settings.MonovalentMilliMolar, result.Settings.OligoNanoMolar, result.Settings.MagnesiumMilliMolar);
                Near(tm, region.Tm, 0.02, "Actual oligo region Tm");
                LampTmRange range = result.Settings.GetTm(region.Name);
                double min = range.Min, max = range.Max;
                Require(tm + 0.02 >= min && tm - 0.02 <= max, "Segment Tm outside settings: " + region.Name);
                if (result.Snp != null && !specific) Require(!(region.Start <= result.Snp.Position && region.End >= result.Snp.Position), "A claimed shared primer overlaps the SNP.");
            }
            Equal(expectedFull.ToString(), oligo.Sequence, "Full composite sequence orientation/order");
            Equal(actualFull.ToString(), oligo.Sequence, "Concatenated region sequences");
            Require(Regex.IsMatch(oligo.Sequence, "^[ACGT]+$"), "Order sequence includes separators or ambiguous symbols.");
            Require(oligo.Metrics != null, "Full oligo structure metrics missing."); Equal(oligo.Sequence, oligo.Metrics.Sequence, "Structure analysis must use actual complete oligo");
            if (oligo.Regions.Count > 1) Require(Double.IsNaN(oligo.Metrics.Tm), "Joined inner oligo must not claim a single binding Tm.");
        }
        private static void ValidateReports(LampDesignResult result)
        {
            foreach (LampPrimerSet set in result.Sets)
            {
                HighlightedReport ordering = LampReportWriter.HighlightedOrderingText(set, result);
                var expected = new HashSet<int>(); int start = 0; string[] rows = ordering.Text.Split('\n'); var oligos = LampReportWriter.Oligos(set);
                Require(rows.Length == oligos.Count, "Ordering view changed primer count.");
                for (int i = 0; i < rows.Length; i++)
                {
                    int separator = rows[i].IndexOf('\t'); Require(separator >= 0, "Ordering row lacks tab separator.");
                    Equal(oligos[i].Sequence, rows[i].Substring(separator + 1), "Ordering sequence altered.");
                    if (oligos[i].SnpIndex >= 0) expected.Add(start + separator + 1 + oligos[i].SnpIndex);
                    start += rows[i].Length + 1;
                }
                ExactMarks(ordering, expected);
                HighlightedReport details = LampReportWriter.HighlightedSet(set, result); expected.Clear(); start = 0;
                foreach (string row in details.Text.Split('\n'))
                {
                    foreach (LampOligo oligo in oligos) if (row == oligo.Sequence && oligo.SnpIndex >= 0) expected.Add(start + oligo.SnpIndex);
                    if (result.Snp != null && (row == set.ReferenceTemplate || row == set.AlternateTemplate)) expected.Add(start + result.Snp.Position - set.SpanStart);
                    start += row.Length + 1;
                }
                ExactMarks(details, expected); Require(expected.Count == (result.Snp == null ? 0 : 4), "Expected exact primer/template SNP marks.");
            }
            string html = LampReportWriter.Html(result); var dnaBlocks = Regex.Matches(html, "<pre class=\"dna\">(.*?)</pre>", RegexOptions.Singleline);
            var sequences = new List<string>(); var indices = new List<int>();
            foreach (LampPrimerSet set in result.Sets)
            {
                foreach (LampOligo oligo in LampReportWriter.Oligos(set)) { sequences.Add(oligo.Sequence); indices.Add(oligo.SnpIndex); }
                sequences.Add(set.ReferenceTemplate); indices.Add(result.Snp == null ? -1 : result.Snp.Position - set.SpanStart);
                if (result.Snp != null) { sequences.Add(set.AlternateTemplate); indices.Add(result.Snp.Position - set.SpanStart); }
            }
            sequences.Add(result.Input.Sequence); indices.Add(result.Snp == null ? -1 : result.Snp.Position - 1);
            if (result.Snp != null) { sequences.Add(result.Snp.Alternate.Sequence); indices.Add(result.Snp.Position - 1); }
            Require(dnaBlocks.Count == sequences.Count, "HTML sequence block count differs.");
            for (int i = 0; i < sequences.Count; i++)
            {
                string expected = sequences[i]; int index = indices[i];
                if (index >= 0) expected = expected.Substring(0, index) + "<span class=\"snp\">" + expected[index] + "</span>" + expected.Substring(index + 1);
                Equal(expected, dnaBlocks[i].Groups[1].Value, "Exact HTML sequence and SNP coloring");
            }
            Require(html.Contains("color:#d32f2f"), "HTML SNP red style missing.");
        }
        private static void ExactMarks(HighlightedReport value, HashSet<int> expected)
        {
            var actual = new HashSet<int>(); foreach (ReportHighlight mark in value.SnpHighlights)
            { Require(mark.Length == 1 && mark.Start >= 0 && mark.Start < value.Text.Length, "Highlight must be one actual sequence base."); Require(actual.Add(mark.Start), "Duplicate highlight."); }
            Require(actual.SetEquals(expected), "SNP character offsets differ from sequence-derived positions.");
        }
        private static List<string[]> ParseCsv(string value)
        {
            var rows = new List<string[]>(); var row = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"') { if (quoted && i + 1 < value.Length && value[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
                else if (!quoted && c == ',') { row.Add(cell.ToString()); cell.Length = 0; }
                else if (!quoted && (c == '\r' || c == '\n'))
                { if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n') i++; row.Add(cell.ToString()); cell.Length = 0; rows.Add(row.ToArray()); row.Clear(); }
                else cell.Append(c);
            }
            Require(!quoted, "Unterminated CSV field."); if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); } return rows;
        }
        private static string Rc(string value) { var chars = new char[value.Length]; for (int i = 0; i < value.Length; i++) chars[value.Length - i - 1] = Complement(value[i]); return new string(chars); }
        private static char Complement(char value) { switch (value) { case 'A': return 'T'; case 'C': return 'G'; case 'G': return 'C'; case 'T': return 'A'; default: throw new InvalidOperationException("Invalid DNA base in test."); } }
        private static double Gc(string value) { int gc = 0; foreach (char c in value) if (c == 'G' || c == 'C') gc++; return 100.0 * gc / value.Length; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Equal(string expected, string actual, string message) { Require(expected == actual, message + ": expected " + expected + "; actual " + actual); }
        private static void Near(double expected, double actual, double tolerance, string message) { Require(!Double.IsNaN(actual) && Math.Abs(expected - actual) <= tolerance, message + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + "; actual " + actual.ToString("R", CultureInfo.InvariantCulture)); }
        private static void ExpectArgument(Action action) { bool caught = false; try { action(); } catch (ArgumentException) { caught = true; } Require(caught, "Expected invalid-input exception."); }
    }
}
