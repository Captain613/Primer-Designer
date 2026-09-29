using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RpaDesigner
{
    // Artificial DNA only; these tests verify sequence/model/export invariants,
    // not RNase H2 activity, allele selectivity, or amplification performance.
    public static class PaLampSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("PA-LAMP self-test report");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            lines.Add("Deterministic artificial DNA. Software checks do not establish enzyme activity or SNP selectivity.");
            lines.Add("");
            Action<string, Action> test = delegate(string title, Action action)
            {
                var watch = Stopwatch.StartNew();
                try { action(); passed++; lines.Add("PASS  " + title + " (" + watch.ElapsedMilliseconds + " ms)"); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + title + " (" + watch.ElapsedMilliseconds + " ms): " + ex.Message); lines.Add(ex.StackTrace ?? ""); }
            };
            string template = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
            LampDesignResult fixture = null;

            test("All twelve directed substitutions retain PA RNA alleles and common active BIP", delegate
            {
                foreach (char reference in "ACGT") foreach (char alternate in "ACGT")
                {
                    if (reference == alternate) continue;
                    LampDesignResult result = Design(template, 333, reference, alternate, Broad());
                    Require(result.Sets.Count > 0, "No artificial PA candidate for " + reference + ">" + alternate + ".");
                    Validate(result);
                    if (reference == 'A' && alternate == 'C') fixture = result;
                }
            });
            test("Four through seven nucleotide tails map upstream with one terminal mismatch", delegate
            {
                foreach (int length in new int[] { 4, 5, 6, 7 })
                {
                    LampDesignSettings settings = Broad(); settings.PaTailLength = length;
                    LampDesignResult result = Design(template, 333, 'T', 'G', settings);
                    Require(result.Sets.Count > 0, "Tail " + length + " produced no artificial candidate.");
                    Validate(result);
                    foreach (LampPrimerSet set in result.Sets)
                        Require(set.BIP.ActivationTailLength == length && set.AlternateInner.ActivationTailLength == length, "Requested tail length was silently changed.");
                }
            });
            test("Auto and explicit BIP use activation instead of an AS terminal mismatch", delegate
            {
                foreach (string orientation in new string[] { "Auto", "BIP" })
                {
                    LampDesignSettings settings = Broad(); settings.SnpOrientation = orientation;
                    LampDesignResult result = Design(template, 333, 'C', 'A', settings);
                    Require(result.Sets.Count > 0, "Supported PA orientation produced no candidate."); Validate(result);
                    foreach (LampPrimerSet set in result.Sets)
                        Require(set.ExtraMismatchPosition == 0, "PA-LAMP acquired an AS-LAMP artificial mismatch.");
                }
            });
            test("Optional loop primers do not overlap the blocked BIP extension", delegate
            {
                LampDesignSettings settings = Broad(); settings.IncludeLoops = true;
                LampDesignResult result = Design(template, 333, 'G', 'T', settings);
                Require(result.Sets.Count > 0, "Loop fixture produced no PA candidate."); Validate(result);
                bool exercised = false;
                foreach (LampPrimerSet set in result.Sets)
                {
                    if (set.LB != null)
                    {
                        exercised = true;
                        Require(set.LB.Regions[0].End < set.BIP.Regions[1].Start, "LB overlaps the precursor RNA/tail region.");
                    }
                    exercised |= set.LF != null;
                }
                Require(exercised, "Loop fixture did not exercise an actual loop primer.");
            });
            test("Boundary SNPs cannot manufacture missing activation or LAMP flanks", delegate
            {
                foreach (int position in new int[] { 1, 4, 5, template.Length - 1, template.Length })
                {
                    LampDesignResult result = Design(template, position, 'A', 'C', Broad());
                    Require(result.Sets.Count == 0, "Boundary SNP " + position + " fabricated a usable PA layout.");
                }
            });
            test("Ambiguity in the required activation tail is not silently removed", delegate
            {
                string ambiguous = template.Substring(0, 331) + "N" + template.Substring(332);
                LampDesignResult result = Design(ambiguous, 333, 'A', 'C', Broad());
                Require(result.Input.Sequence.Length == template.Length && result.Input.Sequence[331] == 'N', "Tail ambiguity changed coordinates.");
                Require(result.Sets.Count == 0, "A required ambiguous tail base became an orderable precursor.");
                result = Design(new string('N', 500), 250, 'A', 'C', Broad());
                Require(result.Sets.Count == 0, "Ambiguous template fabricated PA candidates.");
            });
            test("Ambiguity outside the selected template does not invalidate clean candidates", delegate
            {
                string ambiguous = "N" + template.Substring(1);
                LampDesignResult result = Design(ambiguous, 333, 'A', 'G', Broad());
                Require(result.Sets.Count > 0, "An unrelated ambiguous base prevented clean PA candidates."); Validate(result);
            });
            test("Invalid PA controls are rejected without silently borrowing AS behavior", delegate
            {
                LampDesignSettings settings = Broad(); settings.SnpOrientation = "FIP";
                ExpectArgument(delegate { Design(template, 333, 'A', 'C', settings); }, "Unsupported FIP activation");
                foreach (int offset in new int[] { 2, 3 })
                {
                    settings = Broad(); settings.ExtraMismatchFromThreePrime = offset;
                    ExpectArgument(delegate { Design(template, 333, 'A', 'C', settings); }, "AS extra mismatch");
                }
                foreach (int tail in new int[] { 0, 3, 8 })
                {
                    settings = Broad(); settings.PaTailLength = tail;
                    ExpectArgument(delegate { Design(template, 333, 'A', 'C', settings); }, "Invalid tail");
                }
                settings = Broad(); settings.SnpMethod = "unknown";
                ExpectArgument(delegate { Design(template, 333, 'A', 'C', settings); }, "Unknown SNP method");
                ExpectArgument(delegate { LampDesignEngine.Design(SequenceParser.Parse(template), Broad(), null, CancellationToken.None); }, "PA requires a SNP input");
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(Input(template, 333, 'A', 'C'), null, null, CancellationToken.None); }, "Null settings");
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(null, Broad(), null, CancellationToken.None); }, "Null SNP input");
            });
            test("Forged SNP coordinates or additional allele changes are rejected", delegate
            {
                SnpInput malformed = Input(template, 333, 'A', 'C'); malformed.Position = 0;
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(malformed, Broad(), null, CancellationToken.None); }, "Invalid SNP coordinate");
                malformed = Input(template, 333, 'A', 'C');
                char[] alternate = malformed.Alternate.Sequence.ToCharArray(); alternate[20] = alternate[20] == 'A' ? 'C' : 'A';
                malformed.Alternate.Sequence = new string(alternate);
                ExpectArgument(delegate { LampDesignEngine.DesignSnp(malformed, Broad(), null, CancellationToken.None); }, "Second hidden substitution");
            });
            test("Both pre-cancelled and actively cancelled PA searches stop", delegate
            {
                foreach (bool preCancelled in new bool[] { true, false }) using (var cancellation = new CancellationTokenSource())
                {
                    if (preCancelled) cancellation.Cancel();
                    bool caught = false; int callbacks = 0;
                    try
                    {
                        LampDesignEngine.DesignSnp(Input(template, 333, 'A', 'C'), Broad(), delegate(int percent, string text)
                        { callbacks++; cancellation.Cancel(); }, cancellation.Token);
                    }
                    catch (OperationCanceledException) { caught = true; }
                    Require(caught && (preCancelled || callbacks > 0), "PA cancellation was ignored.");
                }
            });
            test("Ordering copy text retains RNA, C3 and only the two real SNP highlights", delegate
            {
                Require(fixture != null, "Substitution fixture failed.");
                foreach (LampPrimerSet set in fixture.Sets)
                {
                    HighlightedReport report = LampReportWriter.HighlightedOrderingText(set, fixture);
                    List<LampOligo> oligos = LampReportWriter.Oligos(set);
                    string[] rows = report.Text.Split('\n'); Require(rows.Length == oligos.Count, "Ordering copy changed oligo count.");
                    var expected = new HashSet<int>(); int rowStart = 0;
                    for (int i = 0; i < rows.Length; i++)
                    {
                        int tab = rows[i].IndexOf('\t'); Require(tab >= 0, "Ordering copy lacks name/sequence separator.");
                        Equal(oligos[i].OrderingSequence, rows[i].Substring(tab + 1), "Copied chemical ordering sequence");
                        if (oligos[i].OrderingSnpIndex >= 0) expected.Add(rowStart + tab + 1 + oligos[i].OrderingSnpIndex);
                        rowStart += rows[i].Length + 1;
                    }
                    Require(expected.Count == 2, "Two blocked allele primers require exactly two RNA SNP highlights.");
                    ExactMarks(report, expected);
                    Equal(report.Text, LampReportWriter.OrderingText(set, fixture).Replace("\r\n", "\n"), "Plain ordering copy");
                }
            });
            test("TXT and HTML distinguish PA activation and preserve modification-aware highlights", delegate
            {
                Require(fixture != null, "Substitution fixture failed.");
                string text = LampReportWriter.TextReport(fixture), html = LampReportWriter.Html(fixture);
                Require(text.Contains("PA-LAMP") && html.Contains("PA-LAMP"), "PA method identity was lost.");
                Require(text.Replace(" ", "").Contains("RNaseH2") && text.Contains("C3"), "Activation enzyme or block not explained.");
                foreach (LampPrimerSet set in fixture.Sets)
                {
                    HighlightedReport report = LampReportWriter.HighlightedSet(set, fixture);
                    var expected = new HashSet<int>(); int lineStart = 0;
                    foreach (string row in report.Text.Split('\n'))
                    {
                        foreach (LampOligo oligo in LampReportWriter.Oligos(set))
                            if (row == oligo.OrderingSequence && oligo.OrderingSnpIndex >= 0) expected.Add(lineStart + oligo.OrderingSnpIndex);
                        if (row == set.ReferenceTemplate || row == set.AlternateTemplate) expected.Add(lineStart + fixture.Snp.Position - set.SpanStart);
                        lineStart += row.Length + 1;
                    }
                    Require(expected.Count == 4, "Set report must mark two RNA bases and two original-template SNP bases."); ExactMarks(report, expected);
                    foreach (LampOligo oligo in LampReportWriter.Oligos(set))
                    {
                        Require(text.Contains(oligo.OrderingSequence), "TXT lost the complete chemical ordering sequence.");
                        string marked = oligo.OrderingSequence; int index = oligo.OrderingSnpIndex;
                        if (index >= 0) marked = marked.Substring(0, index) + "<span class=\"snp\">" + marked[index] + "</span>" + marked.Substring(index + 1);
                        Require(html.Contains("<pre class=\"dna\">" + marked + "</pre>"), "HTML lost modified oligo text or marked the wrong RNA character.");
                    }
                }
            });
            test("CSV keeps chemical order strings and safely escapes FASTA metadata", delegate
            {
                Require(fixture != null, "Substitution fixture failed."); string originalName = fixture.Input.Name;
                try
                {
                    fixture.Input.Name = "=SUM(1,2) \"PA\"\r\nsynthetic";
                    List<string[]> rows = ParseCsv(LampReportWriter.Csv(fixture));
                    int rowIndex = 1;
                    foreach (LampPrimerSet set in fixture.Sets) foreach (LampOligo oligo in LampReportWriter.Oligos(set))
                    {
                        Require(rowIndex < rows.Count, "CSV lost an oligo row."); string[] row = rows[rowIndex++];
                        Require(row.Length == rows[0].Length, "CSV quoting changed column count.");
                        Equal("'" + fixture.Input.Name, row[0], "CSV formula protection");
                        Require(Array.IndexOf(row, oligo.OrderingSequence) >= 0, "CSV replaced an orderable modified sequence with plain DNA.");
                    }
                    Require(rowIndex == rows.Count, "CSV emitted unexpected oligo rows.");
                }
                finally { fixture.Input.Name = originalName; }
            });
            test("Standard FASTA retains DNA-equivalent lines and explicit non-ordering modification metadata", delegate
            {
                Require(fixture != null, "Substitution fixture failed.");
                var oligos = new List<LampOligo>(); foreach (LampPrimerSet set in fixture.Sets) oligos.AddRange(LampReportWriter.Oligos(set));
                string[] linesOfFasta = LampReportWriter.Fasta(fixture).Replace("\r", "").TrimEnd('\n').Split('\n');
                Require(linesOfFasta.Length == oligos.Count * 2, "FASTA oligo count changed.");
                for (int i = 0; i < oligos.Count; i++)
                {
                    LampOligo oligo = oligos[i]; string header = linesOfFasta[i * 2];
                    Require(header.StartsWith(">", StringComparison.Ordinal), "FASTA header missing.");
                    Equal(oligo.Sequence, linesOfFasta[i * 2 + 1], "FASTA DNA-equivalent sequence");
                    Require(Regex.IsMatch(linesOfFasta[i * 2 + 1], "^[ACGT]+$"), "Chemical annotations corrupted a standard FASTA sequence line.");
                    if (oligo.RnaIndex >= 0)
                    {
                        Require(header.Contains("method=PA-LAMP") && header.Contains("DNA_equivalent_only") && header.Contains("NOT_FOR_ORDERING"), "FASTA fails to distinguish analytical DNA from ordering chemistry.");
                        Require(header.Contains("RNA_position=" + (oligo.RnaIndex + 1)) && header.Contains("RNA_base=" + oligo.OrderingSequence[oligo.OrderingSnpIndex]), "FASTA lost RNA position/base metadata.");
                        Require(header.Contains("block_3prime=C3") && header.Contains("ordering_sequence=" + oligo.OrderingSequence), "FASTA lost RNA/C3 ordering metadata.");
                        Require(header.Contains("activated_sequence=" + oligo.ActivatedSequence), "FASTA lost post-cleavage oligo metadata.");
                    }
                }
            });
            test("Ordinary and AS-LAMP results remain unmodified after PA calls", delegate
            {
                LampDesignSettings settings = Broad(); settings.SnpMethod = "AS-LAMP"; settings.SnpOrientation = "BIP";
                LampDesignResult asLamp = Design(template, 333, 'A', 'C', settings);
                Require(asLamp.Sets.Count > 0 && !LampReportWriter.IsPa(asLamp), "AS regression fixture failed.");
                settings.SnpOrientation = "Auto";
                LampDesignResult ordinary = LampDesignEngine.Design(SequenceParser.Parse(template), settings, null, CancellationToken.None);
                Require(ordinary.Sets.Count > 0 && !LampReportWriter.IsPa(ordinary), "Ordinary regression fixture failed.");
                foreach (LampDesignResult result in new LampDesignResult[] { asLamp, ordinary }) foreach (LampPrimerSet set in result.Sets)
                    foreach (LampOligo oligo in LampReportWriter.Oligos(set))
                    {
                        Equal(oligo.Sequence, oligo.OrderingSequence, "Unmodified order sequence fallback");
                        Require(oligo.OrderingSnpIndex == oligo.SnpIndex && oligo.RnaIndex < 0 && String.IsNullOrEmpty(oligo.ThreePrimeBlock), "Non-PA design acquired chemical metadata.");
                    }
            });

            lines.Add(""); lines.Add("Passed: " + passed); lines.Add("Failed: " + failed); lines.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath)); if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }

        private static LampDesignSettings Broad()
        {
            return new LampDesignSettings { SnpMethod = "PA-LAMP", RegionMin = 20, RegionMax = 20, SpanMin = 120, SpanMax = 300,
                MaxSets = 2, GcMin = 20, GcMax = 80, AnnealTmMin = 35, AnnealTmMax = 85, InnerTmMin = 35, InnerTmMax = 85,
                IncludeLoops = false, ExtraMismatchFromThreePrime = 0, PaTailLength = 5 };
        }
        private static SnpInput Input(string source, int position, char reference, char alternate)
        {
            return SnpParser.Parse(source.Substring(0, position - 1) + "[" + reference + ">" + alternate + "]" + source.Substring(position));
        }
        private static LampDesignResult Design(string source, int position, char reference, char alternate, LampDesignSettings settings)
        {
            SnpInput input = Input(source, position, reference, alternate);
            string referenceBefore = input.Reference.Sequence, alternateBefore = input.Alternate.Sequence;
            LampDesignResult result = LampDesignEngine.DesignSnp(input, settings, null, CancellationToken.None);
            Equal(referenceBefore, input.Reference.Sequence, "Caller reference template must not be mutated");
            Equal(alternateBefore, input.Alternate.Sequence, "Caller alternate template must not be mutated");
            Equal(referenceBefore, result.Input.Sequence, "Returned original reference template");
            Equal(referenceBefore, result.Snp.Reference.Sequence, "Returned SNP reference template");
            Equal(alternateBefore, result.Snp.Alternate.Sequence, "Returned original alternate template");
            return result;
        }
        private static void Validate(LampDesignResult result)
        {
            Require(result.Snp != null && result.Settings.SnpMethod == "PA-LAMP" && LampReportWriter.IsPa(result), "PA result identity or SNP input missing.");
            Require(result.Sets.Count <= result.Settings.MaxSets, "Candidate cap exceeded.");
            int rank = 0;
            foreach (LampPrimerSet set in result.Sets)
            {
                Require(set.Rank == ++rank && !Double.IsNaN(set.Score) && set.Score >= 0 && set.Score <= 100, "Invalid rank or score.");
                Require(set.SpecificInner == "BIP" && set.BIP != null && set.AlternateInner != null, "PA needs two blocked BIP allele versions.");
                Require(set.ExtraMismatchPosition == 0, "PA acquired an artificial AS mismatch.");
                Require(set.TailMismatchPosition == result.Snp.Position - result.Settings.PaTailLength &&
                    set.TailMismatchBase == set.BIP.TailMismatchBase && set.TailMismatchBase == set.AlternateInner.TailMismatchBase,
                    "PA tail-terminal mismatch metadata is inconsistent across the two allele precursors.");
                ValidateModified(set.BIP, result.Snp.Reference, result.Snp.ReferenceAllele, result);
                ValidateModified(set.AlternateInner, result.Snp.Alternate, result.Snp.AlternateAllele, result);
                Equal(set.BIP.ActivatedSequence, set.AlternateInner.ActivatedSequence, "Both RNA allele variants release the same active DNA BIP");
                Equal(set.BIP.Sequence.Remove(set.BIP.RnaIndex, 1), set.AlternateInner.Sequence.Remove(set.AlternateInner.RnaIndex, 1), "Only RNA allele identity may differ between precursors");
                LampRegion f3 = set.F3.Regions[0], f2 = set.FIP.Regions[1], f1 = set.FIP.Regions[0];
                LampRegion b1 = set.BIP.Regions[0], precursorB2 = set.BIP.Regions[1], activeB2 = set.BIP.ActivatedRegions[1], b3 = set.B3.Regions[0];
                Require(f3.End < f2.Start && f2.End < f1.Start && f1.End < b1.Start && b1.End < precursorB2.Start && precursorB2.End < b3.Start, "PA six-region precursor geometry overlaps or changes order.");
                Require(f1.Start - f2.Start >= 40 && f1.Start - f2.Start <= 60, "Forward loop span invalid.");
                Require(activeB2.End - b1.End >= 40 && activeB2.End - b1.End <= 60, "Activated backward loop span invalid.");
                Require(activeB2.End - f2.Start + 1 >= result.Settings.CoreSpanMin && activeB2.End - f2.Start + 1 <= result.Settings.CoreSpanMax, "Activated F2-B2 span invalid.");
                Require(set.SpanStart == f3.Start && set.SpanEnd == b3.End && set.SpanLength == set.SpanEnd - set.SpanStart + 1, "Template-span coordinates differ.");
                Equal(result.Snp.Reference.Sequence.Substring(set.SpanStart - 1, set.SpanLength), set.ReferenceTemplate, "Original reference target template");
                Equal(result.Snp.Alternate.Sequence.Substring(set.SpanStart - 1, set.SpanLength), set.AlternateTemplate, "Original alternate target template");
                foreach (LampOligo oligo in LampReportWriter.Oligos(set))
                {
                    if (Object.ReferenceEquals(oligo, set.BIP) || Object.ReferenceEquals(oligo, set.AlternateInner)) continue;
                    Require(oligo.RnaIndex < 0 && String.IsNullOrEmpty(oligo.ThreePrimeBlock) && oligo.SnpIndex < 0, "A common oligo acquired allele-specific chemistry.");
                    string expected = "";
                    foreach (LampRegion region in oligo.Regions)
                    {
                        Require(result.Snp.Position < region.Start || result.Snp.Position > region.End, "Common oligo overlaps the SNP.");
                        ValidateRegion(region, result.Snp.Reference.Sequence); expected += region.Sequence;
                    }
                    Equal(expected, oligo.Sequence, "Common oligo joins actual strand-oriented regions");
                    Equal(oligo.Sequence, oligo.OrderingSequence, "Common oligo ordering fallback");
                }
                ValidatePaScore(set, result.Settings);
            }
        }
        private static void ValidateModified(LampOligo oligo, ParsedSequence template, char allele, LampDesignResult result)
        {
            Require(oligo.Regions.Count == 2 && oligo.ActivatedRegions != null && oligo.ActivatedRegions.Count == 2, "Precursor/active BIP requires B1c and B2 regions.");
            int position = result.Snp.Position, tail = result.Settings.PaTailLength;
            LampRegion b1 = oligo.Regions[0], fullB2 = oligo.Regions[1], activeB1 = oligo.ActivatedRegions[0], activeB2 = oligo.ActivatedRegions[1];
            Require(!b1.Reverse && fullB2.Reverse && !activeB1.Reverse && activeB2.Reverse, "BIP strand orientation invalid.");
            ValidateRegion(b1, template.Sequence);
            foreach (LampRegion region in oligo.ActivatedRegions) ValidateRegion(region, template.Sequence);
            Require(activeB2.Start == position + 1 && fullB2.Start == position - tail && fullB2.End == activeB2.End, "RNA cleavage/tail coordinates do not map in BIP's reverse direction.");
            Equal(b1.Sequence, activeB1.Sequence, "Cleavage must retain B1c");
            string expectedActive = b1.Sequence + Rc(template.Sequence.Substring(position, activeB2.End - position));
            string expectedTail = Rc(template.Sequence.Substring(position - tail - 1, tail));
            char originalTailBase = expectedTail[expectedTail.Length - 1];
            Require("ACGT".IndexOf(oligo.TailMismatchBase) >= 0 && oligo.TailMismatchBase != originalTailBase, "PA tail must end with exactly one non-SNP mismatch.");
            Require(oligo.TailMismatchPosition == position - tail && oligo.TailMismatchTemplateBase == template.Sequence[position - tail - 1] &&
                oligo.TailMismatchOriginalBase == originalTailBase, "Tail mismatch coordinate or original strand-specific base is wrong.");
            expectedTail = expectedTail.Substring(0, expectedTail.Length - 1) + oligo.TailMismatchBase;
            char complement = Complement(allele); char rna = complement == 'T' ? 'U' : complement;
            string expectedPrecursor = expectedActive + complement + expectedTail;
            Equal(expectedActive, oligo.ActivatedSequence, "Cut immediately 5-prime of RNA releases upstream DNA fragment");
            Equal(expectedPrecursor, oligo.Sequence, "DNA-equivalent blocked precursor");
            Equal(expectedPrecursor.Substring(b1.Sequence.Length), fullB2.Sequence, "Full precursor B2 retains its actual tail-terminal mismatch");
            Equal(expectedActive + "[r" + rna + "]" + expectedTail + "[C3]", oligo.OrderingSequence, "Chemical order with reverse-complemented RNA allele");
            Require(oligo.RnaIndex == expectedActive.Length && oligo.SnpIndex == oligo.RnaIndex && oligo.RnaTemplatePosition == position, "RNA index/template coordinate mismatch.");
            Require(oligo.OrderingSnpIndex == expectedActive.Length + 2 && oligo.OrderingSequence[oligo.OrderingSnpIndex] == rna, "Ordering SNP index must address RNA letter, not bracket or r prefix.");
            Require(oligo.ActivationTailLength == tail && oligo.ThreePrimeBlock == "C3", "Tail or C3 metadata missing.");
            Require(oligo.Sequence.Length == oligo.ActivatedSequence.Length + 1 + tail &&
                fullB2.Sequence.Length == activeB2.Sequence.Length + 1 + tail, "Precursor length must add exactly one RNA nucleotide and the requested DNA tail.");
            Require(oligo.RnaIndex < oligo.Sequence.Length - 1, "PA RNA site must be internal to the blocked precursor.");
            Equal(oligo.Sequence.Substring(0, oligo.RnaIndex), oligo.ActivatedSequence, "Cleaved RNA and tail are absent from active fragment");
            Require(oligo.Metrics != null && oligo.ActivatedMetrics != null, "Both precursor and active DNA proxy metrics are required.");
            Equal(oligo.Sequence, oligo.Metrics.Sequence, "Precursor metrics DNA proxy");
            Equal(oligo.ActivatedSequence, oligo.ActivatedMetrics.Sequence, "Active oligo metrics");
            Require(Double.IsNaN(oligo.Metrics.Tm) && Double.IsNaN(oligo.ActivatedMetrics.Tm), "Joined BIP must not claim one binding Tm.");
            foreach (LampRegion region in oligo.ActivatedRegions)
                Require(region.Sequence.Length >= result.Settings.RegionMin && region.Sequence.Length <= result.Settings.RegionMax, "Active region violates requested length bounds.");
        }
        private static void ValidateRegion(LampRegion region, string template)
        {
            Require(region.Start >= 1 && region.End >= region.Start && region.End <= template.Length, "Region coordinates are out of range.");
            string expected = template.Substring(region.Start - 1, region.End - region.Start + 1);
            if (region.Reverse) expected = Rc(expected);
            Equal(expected, region.Sequence, "Exact strand-oriented region sequence");
            Require(Regex.IsMatch(region.Sequence, "^[ACGT]+$"), "A synthesized region includes ambiguity.");
        }
        private static void ValidatePaScore(LampPrimerSet set, LampDesignSettings settings)
        {
            // Audit the documented score using the returned physical oligos.
            // Pair complementarity below uses direct alignments, not engine helpers.
            var activeBip = new LampOligo { Sequence = set.BIP.ActivatedSequence,
                Regions = set.BIP.ActivatedRegions, Metrics = set.BIP.ActivatedMetrics };
            var active = new List<LampOligo> { set.F3, set.B3, set.FIP, activeBip };
            if (set.LF != null) active.Add(set.LF); if (set.LB != null) active.Add(set.LB);
            double regionTerm = 0; int regionCount = 0;
            foreach (LampOligo oligo in active) foreach (LampRegion region in oligo.Regions)
            {
                LampTmRange range = settings.GetTm(region.Name);
                double ideal = !range.MinUnlimited && !range.MaxUnlimited ? (range.Min + range.Max) / 2
                    : region.Name == "F1c" || region.Name == "B1c" || region.Name == "LF" || region.Name == "LB" ? 65 : 60;
                regionTerm += 0.12 * Math.Abs(region.Gc - 50) + 0.2 * Math.Abs(region.Sequence.Length - 22) + 0.8 * Math.Abs(region.Tm - ideal); regionCount++;
            }
            double activeRisk = DirectStructureRisk(active);
            var precursor = new List<LampOligo>(active); precursor[3] = set.BIP;
            double referenceExcess = Math.Max(0, DirectStructureRisk(precursor) - activeRisk);
            precursor[3] = set.AlternateInner;
            double alternateExcess = Math.Max(0, DirectStructureRisk(precursor) - activeRisk);
            LampRegion f2 = set.FIP.Regions[1], f1 = set.FIP.Regions[0], b2 = activeBip.Regions[1], b1 = activeBip.Regions[0];
            double tmBalance = 0.8 * (Math.Abs(f2.Tm - b2.Tm) + Math.Max(0, f2.Tm + 1 - f1.Tm) + Math.Max(0, b2.Tm + 1 - b1.Tm));
            double penalty = regionTerm / regionCount + activeRisk + tmBalance + (referenceExcess + alternateExcess) / 2;
            penalty += 0.05 * Math.Abs(b2.End - f2.Start + 1 - 140) + 0.01 * Math.Abs(set.SpanLength - 200);
            if (settings.IncludeLoops) { if (set.LF == null) penalty += 2; if (set.LB == null) penalty += 2; }
            double expected = Math.Round(100.0 / (1.0 + penalty / 25.0), 2);
            Require(Math.Abs(expected - set.Score) < 0.001, "PA score must use active-region Tm/length plus average positive precursor structure excess; expected " + expected + ", actual " + set.Score + ".");
        }
        private static double DirectStructureRisk(List<LampOligo> oligos)
        {
            double single = 0, cross = 0; int pairs = 0;
            foreach (LampOligo oligo in oligos)
            {
                Primer p = oligo.Metrics;
                single += 0.6 * Math.Max(0, p.SelfComplement - 3) + 1.8 * Math.Max(0, p.SelfThreePrime - 2)
                    + 1.2 * Math.Max(0, p.Hairpin - 3) + 0.5 * Math.Max(0, p.TandemRepeat - 8);
            }
            for (int i = 0; i < oligos.Count; i++) for (int j = i + 1; j < oligos.Count; j++)
            {
                string a = oligos[i].Sequence, b = Rc(oligos[j].Sequence); int longest = 0, endMatch = 0;
                for (int shift = -b.Length + 1; shift < a.Length; shift++)
                {
                    int run = 0;
                    for (int x = Math.Max(0, shift); x < Math.Min(a.Length, shift + b.Length); x++)
                    {
                        int y = x - shift; run = a[x] == b[y] ? run + 1 : 0;
                        longest = Math.Max(longest, run);
                        if (x == a.Length - 1 || y - run + 1 == 0) endMatch = Math.Max(endMatch, run);
                    }
                }
                cross += 0.8 * Math.Max(0, longest - 3) + 2.5 * Math.Max(0, endMatch - 2); pairs++;
            }
            return single / oligos.Count + cross / pairs;
        }
        private static void ExactMarks(HighlightedReport report, HashSet<int> expected)
        {
            var actual = new HashSet<int>();
            foreach (ReportHighlight mark in report.SnpHighlights)
            {
                Require(mark.Length == 1 && mark.Start >= 0 && mark.Start < report.Text.Length, "Each red mark must be one real base.");
                Require(actual.Add(mark.Start), "Duplicate red mark.");
            }
            Require(actual.SetEquals(expected), "Modification-aware SNP highlights differ from expected character positions.");
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
        private static string Rc(string value) { char[] result = new char[value.Length]; for (int i = 0; i < value.Length; i++) result[value.Length - i - 1] = Complement(value[i]); return new string(result); }
        private static char Complement(char value) { switch (value) { case 'A': return 'T'; case 'C': return 'G'; case 'G': return 'C'; case 'T': return 'A'; default: throw new InvalidOperationException("Non-DNA base in artificial fixture."); } }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Equal(string expected, string actual, string message) { Require(expected == actual, message + ": expected " + expected + "; actual " + actual); }
        private static void ExpectArgument(Action action, string message) { bool caught = false; try { action(); } catch (ArgumentException) { caught = true; } Require(caught, "Invalid input was accepted: " + message); }
    }
}
