using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RpaDesigner
{
    // Rendering regression fixtures are deterministic artificial DNA only.
    // They intentionally place the SNP far from primer/amplicon local indices.
    public static class HighlightSelfTests
    {
        public static int Run(string reportPath)
        {
            var report = new List<string>();
            int passed = 0, failed = 0;
            report.Add("RPA Designer SNP highlighting self-test report");
            report.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            report.Add("Rendering-only checks use deterministic artificial DNA; no GUI is started.");
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

            test("Every displayed baseline primer and product SNP is red at its own sequence coordinate", delegate
            {
                SnpDesignResult result = Fixture(0);
                foreach (SnpPrimerSet set in result.Sets) ValidateSet(set, result.Input);
                Require(result.Sets[0].ReferenceForward.Start != result.Sets[1].ReferenceForward.Start,
                    "Fixture must cover different local primer and product SNP coordinates.");
                Require(result.Input.Position != result.Sets[0].ReferenceForward.Sequence.Length,
                    "Fixture must distinguish genomic SNP coordinates from primer-local offsets.");
            });

            test("Second- and third-from-end deliberate mismatches are blue only in allele primers and predicted products", delegate
            {
                foreach (int offset in new int[] { 0, 2, 3 })
                {
                    SnpDesignResult result = Fixture(offset);
                    foreach (SnpPrimerSet set in result.Sets)
                    {
                        HighlightedReport document = ValidateSet(set, result.Input);
                        bool[] colored = ColoredCharacters(document);
                        bool[] blue = ColoredCharacters(document, document.MismatchHighlights);
                        Primer[] primers = { set.ReferenceForward, set.AlternateForward };
                        foreach (Primer primer in primers)
                        {
                            Require(SnpReportWriter.MismatchIndex(primer, set) == (offset == 0 ? -1 : primer.Sequence.Length - offset),
                                "Primer-local artificial mismatch offset is incorrect.");
                            if (offset == 0) continue;
                            int local = set.ExtraMismatchPosition - primer.Start;
                            Require(primer.Sequence[local] != result.Input.Reference.Sequence[set.ExtraMismatchPosition - 1],
                                "Fixture must include a genuine artificial mismatch.");
                            foreach (int start in SequenceLines(document.Text, primer.Sequence))
                            {
                                Require(!colored[start + local], "Artificial primer mismatch was mislabeled as SNP.");
                                Require(blue[start + local], "Artificial primer mismatch lost its blue highlight.");
                            }
                        }
                        foreach (PrimerPair pair in new PrimerPair[] { set.ReferencePair, set.AlternatePair })
                        {
                            Require(SnpReportWriter.ProductMismatchIndex(pair, set) == (offset == 0 ? -1 : set.ExtraMismatchPosition - pair.AmpliconStart),
                                "Product-local artificial mismatch offset is incorrect.");
                            if (offset == 0) continue;
                            foreach (int start in SequenceLines(document.Text, pair.AmpliconSequence))
                            {
                                Require(!colored[start + set.ExtraMismatchPosition - pair.AmpliconStart],
                                    "Artificial product mismatch was mislabeled as SNP.");
                                Require(blue[start + set.ExtraMismatchPosition - pair.AmpliconStart],
                                    "Artificial product mismatch lost its blue highlight.");
                            }
                        }
                        Require(SnpReportWriter.MismatchIndex(set.CommonReverse, set) == -1
                            && SnpReportWriter.MismatchIndex(set.ControlForward, set) == -1
                            && SnpReportWriter.ProductMismatchIndex(set.ControlPair, set) == -1,
                            "An unmodified common/control sequence was assigned an artificial mismatch.");
                        if (offset > 0) Require(set.ExtraMismatchPosition >= set.ControlForward.Start
                            && set.ExtraMismatchPosition <= set.ControlForward.End,
                            "Control fixture must overlap the mismatch coordinate without containing a synthetic mismatch.");
                    }
                }
            });

            test("Ordering view preserves four exact oligos and separate red SNP / blue mismatch coordinates", delegate
            {
                foreach (int mismatch in new int[] { 0, 2, 3 })
                {
                    SnpDesignResult result = Fixture(mismatch);
                    foreach (SnpPrimerSet set in result.Sets)
                    {
                        HighlightedReport document = SnpReportWriter.HighlightedOrderingText(set, result.Input);
                        Equal(Lf(SnpReportWriter.OrderingText(set, result.Input)), document.Text, "Ordering plain text");
                        string[] lines = document.Text.Split('\n');
                        Require(lines.Length == 4, "Ordering output must contain exactly four oligo rows.");
                        Primer[] primers = { set.ReferenceForward, set.AlternateForward, set.CommonReverse, set.ControlForward };
                        var expected = new HashSet<int>();
                        var expectedBlue = new HashSet<int>();
                        int lineStart = 0;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            int separator = lines[i].IndexOf('\t');
                            Require(separator >= 0, "Ordering row needs name and literal oligo fields.");
                            Equal(primers[i].Sequence, lines[i].Substring(separator + 1), "Literal order sequence");
                            if (i < 2) expected.Add(lineStart + lines[i].Length - 1);
                            if (i < 2 && mismatch > 0) expectedBlue.Add(lineStart + lines[i].Length - mismatch);
                            lineStart += lines[i].Length + 1;
                        }
                        ExactHighlights(document, expected);
                        ExactMismatchHighlights(document, expectedBlue);
                    }
                }
            });

            test("Full report rebases every set and marks only the four source-template SNP bases", delegate
            {
                SnpDesignResult result = Fixture(3);
                result.Input.Reference.Name = "artificial \uD83E\uDDEC <header> & metadata";
                HighlightedReport document = SnpReportWriter.HighlightedTextReport(result);
                Equal(WithoutTimestamp(Lf(SnpReportWriter.TextReport(result))), WithoutTimestamp(document.Text), "Full plain report");
                var expected = new HashSet<int>();
                var expectedBlue = new HashSet<int>();
                foreach (SnpPrimerSet set in result.Sets)
                {
                    HighlightedReport local = ValidateSet(set, result.Input);
                    int setStart = document.Text.IndexOf(local.Text, StringComparison.Ordinal);
                    Require(setStart >= 0, "Full report lost a complete candidate section.");
                    foreach (ReportHighlight span in local.SnpHighlights) expected.Add(setStart + span.Start);
                    foreach (ReportHighlight span in local.MismatchHighlights) expectedBlue.Add(setStart + span.Start);
                }
                AddTemplateHighlights(document.Text, result.Input, expected);
                Require(expected.Count == result.Sets.Count * 8 + 4, "Expected fixture highlight count is inconsistent.");
                ExactHighlights(document, expected);
                ExactMismatchHighlights(document, expectedBlue);
            });

            test("HTML safely escapes metadata and retains exact report text, red SNP and blue mismatch spans", delegate
            {
                SnpDesignResult result = Fixture(3);
                result.Input.Reference.Name = "artificial <script>alert(\"x\")</script> & ' > \uD83E\uDDEC";
                result.Notes.Add("<img src=x onerror=alert(1)> & SNP [A>C]");
                HighlightedReport document = SnpReportWriter.HighlightedTextReport(result);
                string html = SnpReportWriter.Html(result);
                Require(html.IndexOf("<script", StringComparison.OrdinalIgnoreCase) < 0,
                    "Untrusted FASTA title created script markup.");
                Require(html.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0,
                    "Untrusted report note created image markup.");
                Match pre = Regex.Match(html, @"<pre\b[^>]*>([\s\S]*?)</pre>", RegexOptions.IgnoreCase);
                Require(pre.Success, "HTML report must contain its readable report text.");
                MatchCollection spans = Regex.Matches(pre.Groups[1].Value, "<span class=\"snp\">([ACGT])</span>");
                Require(spans.Count == document.SnpHighlights.Count, "HTML lost or added SNP spans.");
                MatchCollection blueSpans = Regex.Matches(pre.Groups[1].Value, "<span class=\"mismatch\">([ACGT])</span>");
                Require(blueSpans.Count == document.MismatchHighlights.Count && blueSpans.Count == 12,
                    "HTML lost or added artificial mismatch spans.");
                Require(Regex.IsMatch(html, @"\.mismatch\s*\{[^}]*color\s*:\s*#2563eb\b", RegexOptions.IgnoreCase),
                    "HTML artificial-mismatch style must use the same blue as the UI.");
                string body = Regex.Replace(pre.Groups[1].Value, @"</?span\b[^>]*>", "", RegexOptions.IgnoreCase);
                Require(body.IndexOf('<') < 0 && body.IndexOf('>') < 0, "Report body contains unescaped markup.");
                Equal(WithoutTimestamp(document.Text), WithoutTimestamp(Lf(WebUtility.HtmlDecode(body))),
                    "HTML decoded report text");
                for (int i = 0; i < spans.Count; i++)
                    Require(spans[i].Groups[1].Value[0] == document.Text[document.SnpHighlights[i].Start],
                        "HTML span allele differs from report allele.");
                for (int i = 0; i < blueSpans.Count; i++)
                    Require(blueSpans[i].Groups[1].Value[0] == document.Text[document.MismatchHighlights[i].Start],
                        "HTML span mismatch differs from its synthetic nucleotide.");
                ValidateHtmlOffsets(pre.Groups[1].Value, document);
            });

            test("Plain text, CSV and FASTA preserve synthetic DNA without color markup", delegate
            {
                foreach (int offset in new int[] { 0, 2, 3 })
                {
                    SnpDesignResult result = Fixture(offset);
                    string fasta = SnpReportWriter.Fasta(result), csv = SnpReportWriter.Csv(result);
                    string[] lines = Lf(fasta).TrimEnd('\n').Split('\n');
                    Require(lines.Length == result.Sets.Count * 8, "FASTA changed the number of ordered oligos.");
                    int row = 0;
                    foreach (SnpPrimerSet set in result.Sets)
                        foreach (Primer primer in new Primer[] { set.ReferenceForward, set.AlternateForward, set.CommonReverse, set.ControlForward })
                        {
                            Require(lines[row++].StartsWith(">", StringComparison.Ordinal), "FASTA oligo header is missing.");
                            Equal(primer.Sequence, lines[row++], "FASTA literal DNA");
                            Require(csv.Contains("\"" + primer.Sequence + "\""), "CSV altered an oligo sequence.");
                        }
                    foreach (string text in new string[] { fasta, csv, SnpReportWriter.TextReport(result) })
                        Require(!text.Contains("<span") && !text.Contains("\\cf") && !text.Contains("#2563eb"),
                            "Plain format leaked color markup into export data.");
                }
            });

            test("Empty result and 20000-base boundary templates retain valid SNP highlight positions", delegate
            {
                foreach (int position in new int[] { 1, SequenceParser.MaximumLength })
                {
                    string template = ArtificialSequence(SequenceParser.MaximumLength);
                    SnpInput input = SnpParser.Parse(template.Substring(0, position - 1) + "[A>C]" + template.Substring(position));
                    var result = new SnpDesignResult { Input = input, Settings = new SnpDesignSettings() };
                    result.Notes.Add("Artificial empty candidate result.");
                    HighlightedReport document = SnpReportWriter.HighlightedTextReport(result);
                    var expected = new HashSet<int>();
                    AddTemplateHighlights(document.Text, input, expected);
                    Require(expected.Count == 4, "Empty report should mark only source-template SNP alleles.");
                    ExactHighlights(document, expected);
                    ExactMismatchHighlights(document, new HashSet<int>());
                    Require(Regex.Matches(SnpReportWriter.Html(result), "<span class=\"snp\">").Count == 4,
                        "Boundary report HTML lost source-template SNPs.");
                }
            });

            report.Add("");
            report.Add("TOTAL: " + passed + " passed; " + failed + " failed.");
            string fullPath = Path.GetFullPath(reportPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(fullPath, report.ToArray(), new UTF8Encoding(true));
            return failed == 0 ? 0 : 1;
        }

        private static HighlightedReport ValidateSet(SnpPrimerSet set, SnpInput input)
        {
            HighlightedReport document = SnpReportWriter.HighlightedSet(set, input);
            Equal(Lf(SnpReportWriter.SetText(set, input)), document.Text, "Candidate plain text");
            var expected = new HashSet<int>();
            AddSequenceHighlights(document.Text, set.ReferenceForward.Sequence, input.Position - set.ReferenceForward.Start, 2, expected);
            AddSequenceHighlights(document.Text, set.AlternateForward.Sequence, input.Position - set.AlternateForward.Start, 2, expected);
            AddSequenceHighlights(document.Text, set.ReferencePair.AmpliconSequence, input.Position - set.ReferencePair.AmpliconStart, 1, expected);
            AddSequenceHighlights(document.Text, set.AlternatePair.AmpliconSequence, input.Position - set.AlternatePair.AmpliconStart, 1, expected);
            AddSequenceHighlights(document.Text, set.ControlPair.AmpliconSequence, input.Position - set.ControlPair.AmpliconStart, 1, expected);
            AddSequenceHighlights(document.Text, set.AlternateControlAmpliconSequence, input.Position - set.ControlPair.AmpliconStart, 1, expected);
            Require(expected.Count == 8, "Candidate fixture should contain eight SNP base occurrences.");
            ExactHighlights(document, expected);
            var expectedBlue = new HashSet<int>();
            if (set.ExtraMismatchPosition > 0)
            {
                AddMismatchHighlights(document.Text, set.ReferenceForward.Sequence, set.ExtraMismatchPosition - set.ReferenceForward.Start, 2, expectedBlue);
                AddMismatchHighlights(document.Text, set.AlternateForward.Sequence, set.ExtraMismatchPosition - set.AlternateForward.Start, 2, expectedBlue);
                AddMismatchHighlights(document.Text, set.ReferencePair.AmpliconSequence, set.ExtraMismatchPosition - set.ReferencePair.AmpliconStart, 1, expectedBlue);
                AddMismatchHighlights(document.Text, set.AlternatePair.AmpliconSequence, set.ExtraMismatchPosition - set.AlternatePair.AmpliconStart, 1, expectedBlue);
            }
            ExactMismatchHighlights(document, expectedBlue);
            bool[] colored = ColoredCharacters(document);
            bool[] blue = ColoredCharacters(document, document.MismatchHighlights);
            foreach (Primer primer in new Primer[] { set.CommonReverse, set.ControlForward })
            {
                List<int> lines = SequenceLines(document.Text, primer.Sequence);
                Require(lines.Count == 1, "Shared primer sequence must be visible once.");
                foreach (int start in lines)
                    for (int i = 0; i < primer.Sequence.Length; i++)
                        Require(!colored[start + i] && !blue[start + i], "Shared/control primer that avoids SNP was colored.");
            }
            return document;
        }

        private static void AddTemplateHighlights(string text, SnpInput input, HashSet<int> expected)
        {
            AddSequenceHighlights(text, input.Reference.Sequence, input.Position - 1, 1, expected);
            AddSequenceHighlights(text, input.Alternate.Sequence, input.Position - 1, 1, expected);
            AddSequenceHighlights(text, input.AnnotatedSequence, input.Position, 1, expected);
            AddSequenceHighlights(text, input.AnnotatedSequence, input.Position + 2, 1, expected);
        }

        private static void AddSequenceHighlights(string text, string sequence, int localIndex, int occurrences, HashSet<int> expected)
        {
            Require(localIndex >= 0 && localIndex < sequence.Length, "Fixture local SNP offset is invalid.");
            Require(sequence[localIndex] == 'A' || sequence[localIndex] == 'C', "Fixture SNP allele is not A/C.");
            List<int> starts = SequenceLines(text, sequence);
            Require(starts.Count == occurrences, "Expected " + occurrences + " whole sequence lines, saw " + starts.Count + ".");
            foreach (int start in starts) expected.Add(start + localIndex);
        }

        private static void AddMismatchHighlights(string text, string sequence, int localIndex, int occurrences, HashSet<int> expected)
        {
            Require(localIndex >= 0 && localIndex < sequence.Length, "Fixture local mismatch offset is invalid.");
            List<int> starts = SequenceLines(text, sequence);
            Require(starts.Count == occurrences, "Wrong count of exact sequence lines containing synthetic DNA.");
            foreach (int start in starts) expected.Add(start + localIndex);
        }

        private static void ValidateHtmlOffsets(string html, HighlightedReport document)
        {
            var red = new HashSet<int>(); var blue = new HashSet<int>();
            var decoded = new StringBuilder(); int cursor = 0;
            foreach (Match span in Regex.Matches(html, "<span class=\"(snp|mismatch)\">([ACGT])</span>"))
            {
                decoded.Append(WebUtility.HtmlDecode(html.Substring(cursor, span.Index - cursor)));
                (span.Groups[1].Value == "snp" ? red : blue).Add(decoded.Length);
                decoded.Append(span.Groups[2].Value);
                cursor = span.Index + span.Length;
            }
            decoded.Append(WebUtility.HtmlDecode(html.Substring(cursor)));
            Equal(WithoutTimestamp(document.Text), WithoutTimestamp(decoded.ToString()), "HTML reconstructed literal text");
            ExactHighlights(document, red); ExactMismatchHighlights(document, blue);
        }

        // Whole-line equality distinguishes a displayed oligo from the same motif
        // occurring as part of an amplicon, template, metadata or another primer.
        private static List<int> SequenceLines(string text, string sequence)
        {
            var starts = new List<int>();
            int start = 0;
            foreach (string line in text.Split('\n'))
            {
                if (String.Equals(sequence, line, StringComparison.Ordinal)) starts.Add(start);
                start += line.Length + 1;
            }
            return starts;
        }

        private static void ExactHighlights(HighlightedReport document, HashSet<int> expected)
        {
            bool[] actual = ColoredCharacters(document);
            Require(document.SnpHighlights.Count == expected.Count, "Wrong number of SNP spans.");
            for (int i = 0; i < actual.Length; i++)
                Require(actual[i] == expected.Contains(i), "Incorrect red highlight at report offset " + i + ".");
        }

        private static bool[] ColoredCharacters(HighlightedReport document)
        {
            return ColoredCharacters(document, document.SnpHighlights);
        }

        private static void ExactMismatchHighlights(HighlightedReport document, HashSet<int> expected)
        {
            bool[] actual = ColoredCharacters(document, document.MismatchHighlights);
            bool[] red = ColoredCharacters(document);
            Require(document.MismatchHighlights.Count == expected.Count, "Wrong number of artificial-mismatch spans.");
            for (int i = 0; i < actual.Length; i++)
            {
                Require(actual[i] == expected.Contains(i), "Incorrect blue highlight at report offset " + i + ".");
                Require(!actual[i] || !red[i], "SNP and artificial-mismatch highlights overlap.");
            }
        }

        private static bool[] ColoredCharacters(HighlightedReport document, List<ReportHighlight> marks)
        {
            Require(document.Text.IndexOf('\r') < 0, "Rich report needs LF-only offsets.");
            var actual = new bool[document.Text.Length];
            int prior = -1;
            foreach (ReportHighlight span in marks)
            {
                Require(span.Start > prior && span.Start >= 0 && span.Length == 1 && span.Start < actual.Length,
                    "Highlight must be a sorted, unique, in-bounds single base.");
                Require("ACGT".IndexOf(document.Text[span.Start]) >= 0, "Highlight covers a non-base character.");
                actual[span.Start] = true;
                prior = span.Start;
            }
            return actual;
        }

        private static SnpDesignResult Fixture(int mismatchOffset)
        {
            const int snpPosition = 90;
            string template = ArtificialSequence(240);
            SnpInput input = SnpParser.Parse(template.Substring(0, snpPosition - 1) + "[A>C]" + template.Substring(snpPosition));
            var result = new SnpDesignResult { Input = input,
                Settings = new SnpDesignSettings { ExtraMismatchFromThreePrime = mismatchOffset } };
            for (int i = 0; i < 2; i++)
            {
                int length = 30 + i * 5, reverseStart = 135 + i * 6, controlStart = snpPosition - 30;
                Primer reference = Forward(input.Reference.Sequence, snpPosition - length + 1, length);
                Primer alternate = Forward(input.Alternate.Sequence, snpPosition - length + 1, length);
                Primer reverse = Forward(input.Reference.Sequence, reverseStart, 30);
                reverse.Sequence = DesignEngine.ReverseComplement(reverse.Sequence);
                Primer control = Forward(input.Reference.Sequence, controlStart, 30);
                var set = new SnpPrimerSet { Rank = i + 1, ReferenceForward = reference,
                    AlternateForward = alternate, CommonReverse = reverse, ControlForward = control, Score = 80 - i };
                if (mismatchOffset != 0)
                {
                    int local = length - mismatchOffset;
                    char original = reference.Sequence[local], replacement = original == 'T' ? 'G' : 'T';
                    reference.Sequence = ChangeBase(reference.Sequence, local, replacement);
                    alternate.Sequence = ChangeBase(alternate.Sequence, local, replacement);
                    set.ExtraMismatchPosition = snpPosition - mismatchOffset + 1;
                    set.ExtraMismatchTemplateBase = original;
                    set.ExtraMismatchPrimerBase = replacement;
                }
                set.ReferencePair = Pair(input.Reference.Sequence, reference, reverse, set.Rank);
                set.AlternatePair = Pair(input.Alternate.Sequence, alternate, reverse, set.Rank);
                set.ControlPair = Pair(input.Reference.Sequence, control, reverse, set.Rank);
                set.AlternateControlAmpliconSequence = input.Alternate.Sequence.Substring(control.Start - 1, set.ControlPair.AmpliconLength);
                result.Sets.Add(set);
            }
            return result;
        }

        private static Primer Forward(string template, int start, int length)
        {
            return new Primer { Sequence = template.Substring(start - 1, length), Start = start, End = start + length - 1 };
        }

        private static PrimerPair Pair(string template, Primer forward, Primer reverse, int rank)
        {
            return new PrimerPair { Rank = rank, Forward = forward, Reverse = reverse,
                AmpliconStart = forward.Start, AmpliconEnd = reverse.End,
                AmpliconLength = reverse.End - forward.Start + 1,
                AmpliconSequence = forward.Sequence + template.Substring(forward.End, reverse.End - forward.End) };
        }

        private static string ChangeBase(string sequence, int index, char value)
        {
            char[] bases = sequence.ToCharArray(); bases[index] = value; return new string(bases);
        }

        private static string ArtificialSequence(int length)
        {
            var random = new Random(93027);
            var b = new StringBuilder(length);
            for (int i = 0; i < length; i++) b.Append("ACGT"[random.Next(4)]);
            return b.ToString();
        }

        private static string Lf(string text) { return text.Replace("\r\n", "\n").Replace('\r', '\n'); }
        private static string WithoutTimestamp(string text)
        {
            return Regex.Replace(text, @"(?m)^导出时间：[^\n]*", "导出时间：<dynamic>");
        }
        private static void Equal(string expected, string actual, string label)
        {
            Require(String.Equals(expected, actual, StringComparison.Ordinal), label + " changed.");
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
