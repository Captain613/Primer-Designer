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
    // Deterministic artificial DNA verifies software contracts only. These
    // checks cannot predict amplification timing or allele discrimination.
    public static class MLampSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("mLAMP self-test report");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            lines.Add("Artificial DNA only; no enzyme kinetics, allele selectivity or clinical performance validation.");
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
                    Require(detail.SnpHighlights.Count == 4, "Detail report must mark two FIP termini and two native template SNP bases.");
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
            Require(expected.Count == 2 && report.SnpHighlights.Count == expected.Count, "Only two true allele SNP bases should be highlighted in copy text.");
            foreach (ReportHighlight mark in report.SnpHighlights)
                Require(mark.Length == 1 && expected.Remove(mark.Start), "Wrong or duplicate SNP highlight.");
            Require(expected.Count == 0, "Missing SNP highlight.");
        }
        private static void ValidateHighlights(LampDesignResult r, int offset)
        {
            string html = LampReportWriter.Html(r);
            int perSet = offset == 0 ? 0 : 2;
            int htmlBlue = 0;
            foreach (Match dna in Regex.Matches(html, "<pre class=\"dna\">(.*?)</pre>", RegexOptions.Singleline))
                htmlBlue += Regex.Matches(dna.Groups[1].Value, "<span class=\"mismatch\">").Count;
            Require(htmlBlue == perSet * r.Sets.Count,
                "HTML must color only the artificial base of both FIP alleles, never native templates.");
            HighlightedReport full = LampReportWriter.HighlightedTextReport(r);
            var fullBlue = new HashSet<int>();
            Require(full.MismatchHighlights.Count == perSet * r.Sets.Count, "Combined report lost or duplicated artificial-mismatch highlights.");
            Require(full.SnpHighlights.Count == r.Sets.Count * 4 + 2, "Combined report must preserve all primer and native-template SNP highlights.");
            foreach (LampPrimerSet set in r.Sets)
            {
                HighlightedReport copy = LampReportWriter.HighlightedOrderingText(set, r);
                HighlightedReport detail = LampReportWriter.HighlightedSet(set, r);
                var red = new HashSet<int>(); var blue = new HashSet<int>(); var detailBlue = new HashSet<int>(); int cursor = 0;
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
                        string prefix = LampReportWriter.OligoName(p, set, r) + " 5′→3′\n";
                        int start = detail.Text.IndexOf(prefix, StringComparison.Ordinal);
                        Require(start >= 0, "Detail report lost an allele FIP sequence heading.");
                        detailBlue.Add(start + prefix.Length + mismatchIndex);
                    }
                    Require(html.Contains("<pre class=\"dna\">" + MarkedSequence(p.Sequence, p.SnpIndex, mismatchIndex) + "</pre>"),
                        "HTML colored sequence disagrees with the selected mismatch offset.");
                    cursor += rows[i].Length + 1;
                }
                ExactMarks(copy, red);
                ExactMismatchMarks(copy, blue);
                int detailStart = full.Text.IndexOf(detail.Text, StringComparison.Ordinal);
                Require(detailStart >= 0, "Combined report lost a candidate's detailed report.");
                foreach (int position in detailBlue) fullBlue.Add(detailStart + position);
                ExactMismatchMarks(detail, detailBlue);
                Require(detail.SnpHighlights.Count == 4, "Detail must retain SNP red in both FIPs and both native templates.");
                int templateIndex = r.Snp.Position - set.SpanStart;
                Require(html.Contains("<pre class=\"dna\">" + MarkedSequence(set.ReferenceTemplate, templateIndex, -1) + "</pre>")
                    && html.Contains("<pre class=\"dna\">" + MarkedSequence(set.AlternateTemplate, templateIndex, -1) + "</pre>"),
                    "Native template HTML must not acquire artificial bases or blue marks.");
            }
            ExactMismatchMarks(full, fullBlue);
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
