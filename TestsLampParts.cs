using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RpaDesigner
{
    // Hand-built short DNA fixtures exercise report direction and chemistry
    // without rerunning candidate search or claiming experimental performance.
    public static class LampPartsSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("LAMP component-sequence self-test report");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Action<string, Action> test = delegate(string title, Action action)
            {
                try { action(); passed++; lines.Add("PASS  " + title); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + title + ": " + ex.Message); lines.Add(ex.StackTrace ?? ""); }
            };
            test("Ordinary LAMP lists actual synthesized forward/reverse segments in oligo order", delegate
            {
                LampPrimerSet set = Fixture("LAMP").Sets[0];
                Part(set.FIP, "F1c", "GACT", 0); Part(set.FIP, "F2", "TCCGA", 4);
                Part(set.BIP, "B1c", "GCCAT", 0); Part(set.BIP, "B2", "GGACT", 5);
                Part(set.F3, "F3", "AACGG", 0); Part(set.B3, "B3", "CTAGG", 0);
                Part(set.LF, "LF", "CTGT", 0); Part(set.LB, "LB", "TGAG", 0);
                foreach (LampOligo p in LampReportWriter.Oligos(set)) ValidateParts(p);
            });
            test("mLAMP F2 segments retain the actual mismatch and distinguish reference/alternate alleles", delegate
            {
                LampDesignResult r = Fixture("mLAMP"); LampPrimerSet set = r.Sets[0];
                Part(set.FIP, "F2", "TCAGA", 4); Part(set.AlternateInner, "F2", "TCAGC", 4);
                Equal("TCCGA", r.Input.Sequence.Substring(7, 5), "Original template is not rewritten by the synthetic mismatch");
                foreach (LampOligo p in LampReportWriter.Oligos(set)) ValidateParts(p);
            });
            test("AS-LAMP BIP parts preserve reverse-complemented allele identity", delegate
            {
                LampDesignResult r = Fixture("AS-BIP"); LampPrimerSet set = r.Sets[0];
                Part(set.BIP, "B2", "GGACT", 5); Part(set.AlternateInner, "B2", "GGACG", 5);
                Equal("A", r.Snp.ReferenceAllele.ToString(), "Plus-strand reference allele");
                Equal("C", r.Snp.AlternateAllele.ToString(), "Plus-strand alternate allele");
                foreach (LampOligo p in LampReportWriter.Oligos(set)) ValidateParts(p);
            });
            test("PA-LAMP separates active B2, RNA U/G, mismatched tail DNA and non-nucleotide C3", delegate
            {
                LampPrimerSet set = Fixture("PA-LAMP").Sets[0];
                foreach (LampOligo p in new[] { set.BIP, set.AlternateInner })
                {
                    Require(LampReportWriter.OligoParts(p).Count == 5, "Blocked BIP must have five chemical components.");
                    Part(p, "B1c", "GCCAT", 0); Part(p, "有效 B2", "GGAC", 5);
                    Part(p, "RNA", Object.ReferenceEquals(p, set.BIP) ? "[rU]" : "[rG]", 9);
                    Part(p, "尾部 DNA", "TGACC", 13); Part(p, "3′ C3", "[C3]", 18);
                    ValidateParts(p);
                    Require(LampReportWriter.PartIndex(FindPart(p, "RNA"), p.OrderingSnpIndex) == 2,
                        "RNA highlight must address its base letter, not '[' or 'r'.");
                    Equal("GCCATGGAC", p.ActivatedSequence, "Active primer contains only B1c and effective B2");
                    Require(FindPart(p, "尾部 DNA").Sequence.EndsWith("C", StringComparison.Ordinal), "Tail lost actual final mismatch.");
                }
            });
            test("TXT and HTML list every part under the correct full primer", delegate
            {
                foreach (string mode in new[] { "LAMP", "AS-FIP", "AS-BIP", "mLAMP", "PA-LAMP" })
                {
                    LampDesignResult r = Fixture(mode); LampPrimerSet set = r.Sets[0];
                    string text = LampReportWriter.SetText(set, r).Replace("\r\n", "\n");
                    string html = LampReportWriter.Html(r); int cursor = 0;
                    foreach (LampOligo p in LampReportWriter.Oligos(set))
                    {
                        string name = LampReportWriter.OligoName(p, set, r);
                        int start = text.IndexOf(name + " 5′→3′\n", cursor, StringComparison.Ordinal);
                        Require(start >= cursor, "TXT lost primer " + name + ".");
                        int end = text.IndexOf("\n\n", start, StringComparison.Ordinal); if (end < 0) end = text.Length;
                        string block = text.Substring(start, end - start); cursor = end;
                        int htmlStart = html.IndexOf("<h3>" + name + " · 5′→3′</h3>", StringComparison.Ordinal);
                        Require(htmlStart >= 0, "HTML lost primer " + name + ".");
                        int htmlEnd = html.IndexOf("</article>", htmlStart, StringComparison.Ordinal);
                        string htmlBlock = html.Substring(htmlStart, htmlEnd - htmlStart);
                        if (p.Regions.Count == 1)
                        {
                            Require(block.Contains("\n" + p.OrderingSequence + "\n"), "TXT lost complete single-region primer.");
                            Require(htmlBlock.Contains("<pre class=\"dna\">" + p.OrderingSequence + "</pre>"), "HTML lost complete single-region primer.");
                            continue;
                        }
                        Require(block.Contains("组成部分（按完整引物 5′→3′ 拼接顺序）"), "TXT component direction/order missing.");
                        Require(htmlBlock.Contains("class=\"parts\""), "HTML lacks component group.");
                        foreach (LampOligoPart part in LampReportWriter.OligoParts(p))
                        {
                            Require(block.Contains(part.Name + " 5′→3′\n" + part.Sequence + "\n"), "TXT misplaced component " + name + "/" + part.Name + ".");
                            Match match = Regex.Match(htmlBlock, "<h4>" + Regex.Escape(part.Name) + " · 5′→3′</h4>.*?<pre class=\"dna\">(.*?)</pre>", RegexOptions.Singleline);
                            Require(match.Success, "HTML lost labelled component " + name + "/" + part.Name + ".");
                            Equal(part.Sequence, Regex.Replace(match.Groups[1].Value, "<[^>]+>", ""), "HTML labelled component sequence");
                            if (mode == "mLAMP" && part.Name == "F2")
                                Equal("TC<span class=\"mismatch\">A</span>G<span class=\"snp\">" + (Object.ReferenceEquals(p, set.FIP) ? "A" : "C") + "</span>", match.Groups[1].Value, "HTML F2 mismatch/SNP colors");
                            if (mode == "PA-LAMP" && part.Name == "RNA")
                                Equal("[r<span class=\"snp\">" + (Object.ReferenceEquals(p, set.BIP) ? "U" : "G") + "</span>]", match.Groups[1].Value, "HTML RNA color excludes chemical punctuation");
                            if (part.Name == "F1c" || part.Name == "B1c" || part.Name == "尾部 DNA" || part.Name == "3′ C3")
                                Require(!match.Groups[1].Value.Contains("<span"), "Common/chemical component acquired SNP or mLAMP mismatch color.");
                        }
                    }
                }
            });
            test("CSV appends independently readable component columns without splitting multiline records", delegate
            {
                foreach (string mode in new[] { "LAMP", "AS-FIP", "AS-BIP", "mLAMP", "PA-LAMP" })
                {
                    LampDesignResult r = Fixture(mode); List<LampOligo> oligos = LampReportWriter.Oligos(r.Sets[0]);
                    List<List<string>> rows = ParseCsv(LampReportWriter.Csv(r));
                    Require(rows.Count == oligos.Count + 1, "CSV multiline cells changed oligo row count.");
                    string[] added = { "各组成部分序列_5to3", "F1c_5to3", "F2_5to3", "B1c_5to3", "B2_5to3", "RNA_5to3", "尾部DNA_5to3", "3prime封闭" };
                    Require(rows[0].Count == 37, "CSV must retain 27 original columns, eight component columns and two end-stability columns.");
                    for (int i = 0; i < added.Length; i++) Equal(added[i], rows[0][27 + i], "CSV appended header");
                    Equal("末端6nt稳定性_序列及DeltaG37", rows[0][35], "CSV end-stability header");
                    Equal("末端稳定性模型与限制", rows[0][36], "CSV end-stability explanation header");
                    for (int i = 0; i < oligos.Count; i++)
                    {
                        LampOligo p = oligos[i]; List<string> row = rows[i + 1];
                        Require(row.Count == rows[0].Count, "CSV column count differs across rows.");
                        Equal(p.OrderingSequence, row[4], "CSV full synthesis sequence unchanged");
                        Equal(LampReportWriter.EndStabilityText(p, r.Sets[0]), row[35], "CSV end results coexist with component columns");
                        Equal(i == 0 ? LampEndStability.Explanation : "", row[36], "CSV explanation appears on its first oligo row");
                        Equal(LampReportWriter.PartsText(p), row[27], "CSV complete labelled component list");
                        string[] names = { "F1c", "F2", "B1c", p.RnaIndex >= 0 ? "有效 B2" : "B2", "RNA", "尾部 DNA", "3′ C3" };
                        for (int j = 0; j < names.Length; j++)
                        {
                            LampOligoPart part = FindPart(p, names[j]);
                            Equal(part == null ? "" : part.Sequence, row[28 + j], "CSV per-component value for " + names[j]);
                        }
                    }
                }
            });
            test("Component details preserve red SNP and blue mLAMP mismatch positions", delegate
            {
                foreach (string mode in new[] { "LAMP", "AS-FIP", "AS-BIP", "mLAMP", "PA-LAMP" }) ValidateHighlights(Fixture(mode));
            });
            test("Group copy lists each full primer followed by its own 5-prime to 3-prime components with chemistry and colors", delegate
            {
                foreach (string mode in new[] { "LAMP", "AS-FIP", "AS-BIP", "mLAMP", "PA-LAMP" }) ValidateGroupCopy(Fixture(mode));
            });
            test("Ordering list and FASTA retain original full-primer record count, order and sequence", delegate
            {
                foreach (string mode in new[] { "LAMP", "AS-FIP", "AS-BIP", "mLAMP", "PA-LAMP" })
                {
                    LampDesignResult r = Fixture(mode); LampPrimerSet set = r.Sets[0]; List<LampOligo> oligos = LampReportWriter.Oligos(set);
                    string[] copy = LampReportWriter.OrderingText(set, r).Replace("\r\n", "\n").Split('\n');
                    string[] fasta = LampReportWriter.Fasta(r).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                    Require(copy.Length == oligos.Count && fasta.Length == oligos.Count * 2, "Parts must not become separate ordering/FASTA records.");
                    for (int i = 0; i < oligos.Count; i++)
                    {
                        LampOligo p = oligos[i]; int tab = copy[i].IndexOf('\t');
                        Require(tab >= 0, "Ordering copy lost name separator."); Equal(p.OrderingSequence, copy[i].Substring(tab + 1), "Whole-primer ordering copy");
                        Equal(p.Sequence, fasta[i * 2 + 1], "FASTA analytical DNA body unchanged");
                        foreach (LampOligoPart part in LampReportWriter.OligoParts(p))
                        {
                            string field = part.Name == "有效 B2" ? "B2_active" : part.Name == "尾部 DNA" ? "tail_DNA" : part.Name == "3′ C3" ? "block_3prime" : part.Name;
                            Require(fasta[i * 2].Contains(" segment_" + field + "=" + part.Sequence), "FASTA lost component metadata " + part.Name + ".");
                        }
                        if (p.RnaIndex >= 0) Require(fasta[i * 2].Contains("NOT_FOR_ORDERING"), "PA FASTA must retain chemistry warning.");
                    }
                }
            });
            lines.Add(""); lines.Add("Passed: " + passed); lines.Add("Failed: " + failed); lines.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath)); if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }

        private static LampDesignResult Fixture(string mode)
        {
            char[] template = new string('A', 64).ToCharArray();
            Put(template, 1, "AACGG"); Put(template, 8, "TCCGA"); Put(template, 20, "AGTC"); Put(template, 24, "ACAG");
            Put(template, 30, "GCCAT"); Put(template, 35, "TGAG"); Put(template, 37, "AGTCA"); Put(template, 42, "AGTCC"); Put(template, 53, "CCTAG");
            string sequence = new string(template);
            var r = new LampDesignResult { Input = new ParsedSequence { Name = "parts_fixture", Sequence = sequence }, Settings = new LampDesignSettings() };
            var set = new LampPrimerSet { Rank = 1, Score = 80, SpanStart = 1, SpanEnd = 57, SpanLength = 57, ReferenceTemplate = sequence.Substring(0, 57) };
            set.F3 = Oligo("F3", Region("F3", sequence, 1, 5, false)); set.B3 = Oligo("B3", Region("B3", sequence, 53, 57, true));
            set.FIP = Oligo("FIP", Region("F1c", sequence, 20, 23, true), Region("F2", sequence, 8, 12, false));
            set.BIP = Oligo("BIP", Region("B1c", sequence, 30, 34, false), Region("B2", sequence, 42, 46, true));
            set.LF = Oligo("LF", Region("LF", sequence, 24, 27, true)); set.LB = Oligo("LB", Region("LB", sequence, 35, 38, false));
            r.Sets.Add(set); if (mode == "LAMP") return r;
            int snpPosition = mode == "AS-BIP" || mode == "PA-LAMP" ? 42 : 12;
            char[] alternate = sequence.ToCharArray(); alternate[snpPosition - 1] = 'C';
            r.Snp = new SnpInput { Position = snpPosition, ReferenceAllele = 'A', AlternateAllele = 'C', Reference = r.Input,
                Alternate = new ParsedSequence { Name = "parts_fixture_alt", Sequence = new string(alternate) } };
            set.AlternateTemplate = r.Snp.Alternate.Sequence.Substring(0, 57);
            set.SpecificInner = snpPosition == 42 ? "BIP" : "FIP"; r.Settings.SnpMethod = mode == "AS-FIP" || mode == "AS-BIP" ? "AS-LAMP" : mode;
            if (mode == "PA-LAMP")
            {
                set.LB = null; set.BIP = PaOligo(sequence, 'T'); set.AlternateInner = PaOligo(r.Snp.Alternate.Sequence, 'G');
                set.TailMismatchPosition = 37; set.TailMismatchTemplateBase = 'A'; set.TailMismatchOriginalBase = 'T'; set.TailMismatchBase = 'C'; return r;
            }
            if (set.SpecificInner == "BIP") set.AlternateInner = Oligo("BIP_alt", Region("B1c", sequence, 30, 34, false), Region("B2", r.Snp.Alternate.Sequence, 42, 46, true));
            else
            {
                set.AlternateInner = Oligo("FIP_alt", Region("F1c", sequence, 20, 23, true), Region("F2", r.Snp.Alternate.Sequence, 8, 12, false));
                if (mode == "mLAMP")
                {
                    r.Settings.ExtraMismatchFromThreePrime = 3; set.ExtraMismatchPosition = 10; set.ExtraMismatchTemplateBase = 'C'; set.ExtraMismatchPrimerBase = 'A';
                    set.FIP.Regions[1].Sequence = "TCAGA"; set.AlternateInner.Regions[1].Sequence = "TCAGC";
                    set.FIP.Sequence = "GACTTCAGA"; set.AlternateInner.Sequence = "GACTTCAGC";
                }
            }
            LampOligo reference = set.SpecificInner == "FIP" ? set.FIP : set.BIP;
            reference.SnpIndex = reference.Sequence.Length - 1; set.AlternateInner.SnpIndex = set.AlternateInner.Sequence.Length - 1;
            return r;
        }
        private static LampOligo PaOligo(string template, char alleleBase)
        {
            LampRegion b1 = Region("B1c", template, 30, 34, false), active = Region("B2", template, 43, 46, true), precursor = Region("B2", template, 37, 46, true);
            precursor.Sequence = "GGAC" + alleleBase + "TGACC";
            LampOligo p = Oligo("BIP", b1, precursor); p.RnaIndex = p.SnpIndex = 9; p.RnaTemplatePosition = 42; p.ActivationTailLength = 5;
            p.ThreePrimeBlock = "C3"; p.ActivatedSequence = "GCCATGGAC"; p.ActivatedRegions.Add(b1); p.ActivatedRegions.Add(active);
            p.TailMismatchPosition = 37; p.TailMismatchTemplateBase = 'A'; p.TailMismatchOriginalBase = 'T'; p.TailMismatchBase = 'C'; return p;
        }
        private static void Put(char[] destination, int start, string sequence) { sequence.CopyTo(0, destination, start - 1, sequence.Length); }
        private static LampRegion Region(string name, string template, int start, int end, bool reverse)
        {
            string dna = template.Substring(start - 1, end - start + 1); if (reverse) dna = DesignEngine.ReverseComplement(dna);
            return new LampRegion { Name = name, Sequence = dna, Start = start, End = end, Reverse = reverse, Tm = 60, Gc = 50 };
        }
        private static LampOligo Oligo(string name, params LampRegion[] regions)
        {
            var p = new LampOligo { Name = name, Sequence = "" }; foreach (LampRegion region in regions) { p.Regions.Add(region); p.Sequence += region.Sequence; } return p;
        }
        private static LampOligoPart FindPart(LampOligo p, string name)
        { foreach (LampOligoPart part in LampReportWriter.OligoParts(p)) if (part.Name == name) return part; return null; }
        private static void Part(LampOligo p, string name, string sequence, int offset)
        {
            LampOligoPart part = FindPart(p, name); Require(part != null, "Missing component " + name + ".");
            Equal(sequence, part.Sequence, "Actual synthesized component " + name); Require(part.OrderingOffset == offset, "Incorrect synthesis-text offset for " + name + ".");
        }
        private static void ValidateParts(LampOligo p)
        {
            List<LampOligoPart> parts = LampReportWriter.OligoParts(p); int offset = 0; string joined = "";
            foreach (LampOligoPart part in parts)
            {
                Require(part.OrderingOffset == offset, "Components are not ordered by synthesis offset.");
                Require(!String.IsNullOrEmpty(part.Metadata), "Component metadata is empty.");
                for (int index = -1; index <= p.OrderingSequence.Length; index++)
                    Require(LampReportWriter.PartIndex(part, index) == (index >= offset && index < offset + part.Sequence.Length ? index - offset : -1), "Component relative highlight index is incorrect.");
                joined += part.Sequence; offset += part.Sequence.Length;
            }
            Equal(p.OrderingSequence, joined, "Joining listed components recreates full ordering chemistry");
        }
        private static void ValidateHighlights(LampDesignResult r)
        {
            LampPrimerSet set = r.Sets[0]; HighlightedReport report = LampReportWriter.HighlightedSet(set, r);
            var red = new HashSet<int>(); var blue = new HashSet<int>(); int cursor = 0;
            foreach (LampOligo p in LampReportWriter.Oligos(set))
            {
                string title = LampReportWriter.OligoName(p, set, r) + " 5′→3′\n";
                int titleStart = report.Text.IndexOf(title, cursor, StringComparison.Ordinal); Require(titleStart >= cursor, "Missing full-primer heading.");
                int sequenceStart = titleStart + title.Length; Equal(p.OrderingSequence, report.Text.Substring(sequenceStart, p.OrderingSequence.Length), "Full-primer highlighted sequence");
                AddMark(red, sequenceStart, p.OrderingSnpIndex); int mismatch = LampReportWriter.MismatchIndex(p, set, r); AddMark(blue, sequenceStart, mismatch);
                cursor = sequenceStart + p.OrderingSequence.Length;
                if (p.Regions.Count == 1) continue;
                foreach (LampOligoPart part in LampReportWriter.OligoParts(p))
                {
                    string heading = part.Name + " 5′→3′\n"; int partStart = report.Text.IndexOf(heading, cursor, StringComparison.Ordinal); Require(partStart >= cursor, "Missing component heading.");
                    sequenceStart = partStart + heading.Length; Equal(part.Sequence, report.Text.Substring(sequenceStart, part.Sequence.Length), "Component highlighted sequence");
                    AddMark(red, sequenceStart, LampReportWriter.PartIndex(part, p.OrderingSnpIndex)); AddMark(blue, sequenceStart, LampReportWriter.PartIndex(part, mismatch));
                    cursor = sequenceStart + part.Sequence.Length;
                }
            }
            if (r.Snp != null)
            {
                string referenceTitle = "靶区模板 / 参考等位基因 A（原始正链 5′→3′）\n", alternateTitle = "靶区模板 / 替代等位基因 C（原始正链 5′→3′）\n";
                AddMark(red, report.Text.IndexOf(referenceTitle, StringComparison.Ordinal) + referenceTitle.Length, r.Snp.Position - set.SpanStart);
                AddMark(red, report.Text.IndexOf(alternateTitle, StringComparison.Ordinal) + alternateTitle.Length, r.Snp.Position - set.SpanStart);
            }
            if (LampReportWriter.IsMLamp(r)) Require(blue.Count == 4, "mLAMP must mark full oligos and F2 parts for both alleles.");
            ExactMarks(report.SnpHighlights, red, report.Text, "SNP"); ExactMarks(report.MismatchHighlights, blue, report.Text, "Artificial mismatch");
            if (LampReportWriter.IsPa(r)) foreach (ReportHighlight mark in report.SnpHighlights) Require("ACGTU".IndexOf(report.Text[mark.Start]) >= 0, "PA SNP highlight marks chemical punctuation.");
        }
        private static void ValidateGroupCopy(LampDesignResult r)
        {
            LampPrimerSet set = r.Sets[0]; set.Rank = 17;
            HighlightedReport report = LampReportWriter.HighlightedGroupCopyText(set, r);
            Equal(report.Text.Replace("\n", "\r\n"), LampReportWriter.GroupCopyText(set, r), "Plain group copy preserves highlighted text with Windows line endings");
            string[] rows = report.Text.TrimEnd('\n').Split('\n');
            Equal("此组 LAMP 引物 · 全部序列 5′→3′", rows[0], "Group-copy direction heading");
            Require(rows.Length > 2 && rows[1].Contains("组成") && rows[1].Contains("引物"), "Group copy lacks explanation separating complete primers from components.");
            string prefix = r.Snp == null ? "LAMP_" : LampReportWriter.IsPa(r) ? "PA-LAMP_" : LampReportWriter.IsMLamp(r) ? "mLAMP_" : "AS-LAMP_";
            int rowIndex = 2, sequenceStart = rows[0].Length + rows[1].Length + 2;
            var red = new HashSet<int>(); var blue = new HashSet<int>(); var names = new HashSet<string>();
            foreach (LampOligo p in LampReportWriter.Oligos(set))
            {
                string name = prefix + set.Rank + "_" + LampReportWriter.OligoName(p, set, r);
                Require(names.Add(name), "Reference and alternate primers acquired the same group-copy name.");
                string heading = name + "\t"; Require(rowIndex < rows.Length, "Missing copied full primer " + name + ".");
                Equal(heading + p.OrderingSequence, rows[rowIndex], "Copied full primer keeps synthesis sequence and chemical notation");
                int mismatch = LampReportWriter.MismatchIndex(p, set, r);
                AddMark(red, sequenceStart + heading.Length, p.OrderingSnpIndex); AddMark(blue, sequenceStart + heading.Length, mismatch);
                sequenceStart += rows[rowIndex++].Length + 1;
                if (p.Regions.Count == 1) continue;
                string joined = "";
                foreach (LampOligoPart part in LampReportWriter.OligoParts(p))
                {
                    heading = name + "_组成_" + part.Name + " (5′→3′)\t";
                    Require(rowIndex < rows.Length, "Missing copied component " + name + "/" + part.Name + ".");
                    Equal(heading + part.Sequence, rows[rowIndex], "Component remains associated with its full primer and in synthesis order");
                    string copiedSequence = rows[rowIndex].Substring(heading.Length); joined += copiedSequence;
                    int snp = p.OrderingSnpIndex - part.OrderingOffset, localMismatch = mismatch - part.OrderingOffset;
                    if (snp >= 0 && snp < copiedSequence.Length) AddMark(red, sequenceStart + heading.Length, snp);
                    if (localMismatch >= 0 && localMismatch < copiedSequence.Length) AddMark(blue, sequenceStart + heading.Length, localMismatch);
                    if (LampReportWriter.IsMLamp(r) && part.Name == "F2")
                    {
                        Equal(Object.ReferenceEquals(p, set.FIP) ? "TCAGA" : "TCAGC", copiedSequence, "Copied mLAMP F2 retains actual reference/alternate SNP and artificial mismatch");
                        Require(snp == 4 && localMismatch == 2, "Copied F2 SNP/mismatch positions changed.");
                    }
                    if (LampReportWriter.IsPa(r) && part.Name == "RNA")
                    {
                        Equal(Object.ReferenceEquals(p, set.BIP) ? "[rU]" : "[rG]", copiedSequence, "Copied PA RNA keeps chemistry and allele identity");
                        Require(snp == 2 && "UG".IndexOf(copiedSequence[snp]) >= 0, "Copied PA SNP color must address the RNA base.");
                    }
                    sequenceStart += rows[rowIndex++].Length + 1;
                }
                Equal(p.OrderingSequence, joined, "Copied components reconstruct the complete synthesis oligo including RNA and C3");
            }
            Require(rowIndex == rows.Length, "Group copy has extra, blank, missing or incorrectly ordered primer/component rows.");
            ExactMarks(report.SnpHighlights, red, report.Text, "Group-copy SNP"); ExactMarks(report.MismatchHighlights, blue, report.Text, "Group-copy artificial mismatch");
            if (LampReportWriter.IsMLamp(r)) Require(report.MismatchHighlights.Count == 4, "mLAMP group copy must color each full oligo and its F2 for both alleles.");
        }
        private static void AddMark(HashSet<int> marks, int start, int index) { if (index >= 0) marks.Add(start + index); }
        private static void ExactMarks(List<ReportHighlight> actual, HashSet<int> expected, string text, string kind)
        {
            Require(actual.Count == expected.Count, kind + " highlight count differs.");
            foreach (ReportHighlight mark in actual) Require(mark.Length == 1 && mark.Start >= 0 && mark.Start < text.Length && expected.Remove(mark.Start), kind + " highlight is missing, duplicated or outside its component.");
            Require(expected.Count == 0, kind + " expected highlights missing.");
        }
        private static List<List<string>> ParseCsv(string csv)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char value = csv[i];
                if (value == '"') { if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
                else if (!quoted && value == ',') { row.Add(cell.ToString()); cell.Length = 0; }
                else if (!quoted && (value == '\r' || value == '\n'))
                { if (value == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; row.Add(cell.ToString()); cell.Length = 0; rows.Add(row); row = new List<string>(); }
                else cell.Append(value);
            }
            Require(!quoted, "CSV has an unclosed quoted cell."); if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); } return rows;
        }
        private static void Equal(string expected, string actual, string title) { Require(String.Equals(expected, actual, StringComparison.Ordinal), title + ": expected " + expected + ", got " + actual + "."); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
