using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    // Tests use only deterministic, artificial DNA; no reference genomes or network calls.
    public static class SnpSelfTests
    {
        public static int Run(string reportPath)
        {
            var report = new List<string>();
            int passed = 0, failed = 0;
            string template = SyntheticSequence(460, 20260920);
            string annotated = Annotate(template, 210, 'A', 'C');
            SnpDesignResult baseline = null;
            report.Add("RPA Designer SNP self-test report");
            report.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            report.Add("All DNA is deterministic and artificial. Passing tests does not establish experimental allele discrimination.");
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

            test("Marker is one nucleotide with exact one-based coordinates", delegate
            {
                SnpInput value = SnpParser.Parse("ACG[A>C]TGC");
                Require(value.Position == 4, "Marker must occupy one template coordinate.");
                Require(value.ReferenceAllele == 'A' && value.AlternateAllele == 'C', "Allele labels were lost.");
                Equal("ACGATGC", value.Reference.Sequence, "Reference allele template");
                Equal("ACGCTGC", value.Alternate.Sequence, "Alternate allele template");
                Equal("ACG[A>C]TGC", value.AnnotatedSequence, "Normalized annotated sequence");
            });

            test("Raw numbering and whitespace do not shift SNP coordinates", delegate
            {
                SnpInput value = SnpParser.Parse("1 acg 3\r\n4 [a>c] tgc 7\r\n");
                Require(value.Position == 4, "Removed line numbers must not change the SNP position.");
                Equal("ACGATGC", value.Reference.Sequence, "Numbered reference sequence");
                Equal("ACGCTGC", value.Alternate.Sequence, "Numbered alternate sequence");
                Require(value.Reference.Warnings.Count > 0, "Number cleanup should be disclosed.");
            });

            test("FASTA title punctuation is metadata, not an SNP", delegate
            {
                SnpInput value = SnpParser.Parse(">synthetic [G>T] score>2 description\r\nACG[A>C]TGC\r\n");
                Require(value.Position == 4, "FASTA metadata was parsed as sequence.");
                Equal("synthetic [G>T] score>2 description", value.Reference.Name, "Reference FASTA title");
                Equal(value.Reference.Name, value.Alternate.Name, "Consistent FASTA title");
                Equal("ACGATGC", value.Reference.Sequence, "FASTA reference sequence");
            });

            test("Ambiguous flanks preserve their coordinates", delegate
            {
                SnpInput value = SnpParser.Parse("ACGNRY[A>T]GTN");
                Require(value.Position == 7, "Ambiguous flank bases must retain their coordinates.");
                Equal("ACGNRYAGTN", value.Reference.Sequence, "Ambiguous reference sequence");
                Equal("ACGNRYTGTN", value.Alternate.Sequence, "Ambiguous alternate sequence");
                Require(value.Reference.Warnings.Count > 0, "Ambiguous flanks should be disclosed.");
            });

            test("Empty, missing, repeated and malformed SNP annotations are rejected", delegate
            {
                string[] invalid = {
                    "", "  \r\n ", "ACGTACGT", "ACG[A>A]TGC", "ACG[A>a]TGC",
                    "ACG[A>C]TG[G>T]C", "ACG[AT>C]TGC", "ACG[A>CT]TGC",
                    "ACG[A>-]TGC", "ACG[N>C]TGC", "ACG[A>N]TGC", "ACG[A/C]TGC",
                    "ACG[A>C", "ACGA>C]TGC", "ACG[[A>C]]TGC", "ACG[A>C]>TGC",
                    ">title [A>C]\nACGTACGT", ">one\nACG[A>C]TGC\n>two\nACGT",
                    "ACG[A>C]TGC\n>late_header\nACGT"
                };
                foreach (string input in invalid)
                {
                    string current = input;
                    ExpectArgument(delegate { SnpParser.Parse(current); }, "Invalid annotation " + current);
                }
            });

            test("Marker normalization respects maximum template length", delegate
            {
                SnpInput limit = SnpParser.Parse(new string('A', 9999) + "[A>C]" + new string('T', 10000));
                Require(limit.Reference.Sequence.Length == 20000 && limit.Position == 10000, "The marker must count as one base at the length boundary.");
                ExpectArgument(delegate { SnpParser.Parse(new string('A', 20000) + "[A>C]"); }, "20,001 nt template");
                ExpectArgument(delegate { SnpParser.Parse(new string(' ', 1000001) + "[A>C]"); }, "Excessive raw input");
            });

            test("Default SNP design returns four correctly oriented primers", delegate
            {
                baseline = Design(annotated, Settings(0));
                Require(baseline.Sets.Count > 0, "Artificial template produced no baseline sets.");
                ValidateResult(baseline);
                foreach (SnpPrimerSet set in baseline.Sets)
                {
                    Require(set.ExtraMismatchPosition == 0, "Baseline must not contain an additional deliberate mismatch.");
                    Require(Differences(set.ReferenceForward.Sequence, set.AlternateForward.Sequence) == 1,
                        "Baseline allele-specific forward primers must differ only at the SNP base.");
                    Require(set.ReferenceForward.Sequence[set.ReferenceForward.Sequence.Length - 1] == 'A', "Reference forward primer must terminate in A.");
                    Require(set.AlternateForward.Sequence[set.AlternateForward.Sequence.Length - 1] == 'C', "Alternate forward primer must terminate in C.");
                }
            });

            test("Shared control pair spans both SNP alleles without overlapping the site", delegate
            {
                SnpDesignResult value = baseline ?? Design(annotated, Settings(0));
                Require(value.Sets.Count > 0, "Control validation requires a complete set.");
                foreach (SnpPrimerSet set in value.Sets)
                {
                    Require(set.ControlForward.End < value.Input.Position, "Control forward primer must lie wholly upstream of the SNP.");
                    Require(set.CommonReverse.Start > value.Input.Position, "Common reverse primer must lie wholly downstream of the SNP.");
                    int index = value.Input.Position - set.ControlPair.AmpliconStart;
                    Require(index >= 0 && index < set.ControlPair.AmpliconLength, "Control amplicon does not span the SNP.");
                    Require(set.ControlPair.AmpliconSequence[index] == 'A', "Reference control product lost reference allele.");
                    Require(set.AlternateControlAmpliconSequence[index] == 'C', "Alternate control product lost alternate allele.");
                    Require(Differences(set.ControlPair.AmpliconSequence, set.AlternateControlAmpliconSequence) == 1,
                        "Control products must differ at exactly the annotated SNP.");
                }
            });

            test("Deliberate mismatch at second base from 3-prime is represented in oligos and products", delegate
            {
                SnpDesignResult value = Design(annotated, Settings(2));
                Require(value.Sets.Count > 0, "Artificial template produced no -2 mismatch sets.");
                ValidateResult(value);
                ValidateExtraMismatch(value, 2);
            });

            test("Deliberate mismatch at third base from 3-prime is represented in oligos and products", delegate
            {
                SnpDesignResult value = Design(annotated, Settings(3));
                Require(value.Sets.Count > 0, "Artificial template produced no -3 mismatch sets.");
                ValidateResult(value);
                ValidateExtraMismatch(value, 3);
            });

            test("Every directed canonical allele substitution keeps correct terminal bases", delegate
            {
                const string bases = "ACGT";
                foreach (char reference in bases)
                foreach (char alternate in bases)
                {
                    if (reference == alternate) continue;
                    SnpDesignSettings options = Settings(0);
                    options.Base.MaxPairs = 1;
                    SnpDesignResult value = Design(Annotate(template, 210, reference, alternate), options);
                    Require(value.Sets.Count > 0, "No set for artificial " + reference + ">" + alternate + " substitution.");
                    ValidateResult(value);
                    Primer refPrimer = value.Sets[0].ReferenceForward;
                    Primer altPrimer = value.Sets[0].AlternateForward;
                    Require(refPrimer.Sequence[refPrimer.Sequence.Length - 1] == reference, "Wrong reference terminal allele.");
                    Require(altPrimer.Sequence[altPrimer.Sequence.Length - 1] == alternate, "Wrong alternate terminal allele.");
                }
            });

            test("All primers and all three product lengths obey custom settings", delegate
            {
                SnpDesignSettings options = Settings(0);
                options.Base.PrimerMin = 31;
                options.Base.PrimerMax = 31;
                options.Base.AmpliconMin = 115;
                options.Base.AmpliconMax = 180;
                options.Base.PreferredAmplicon = 145;
                options.Base.GcMin = 35;
                options.Base.GcMax = 65;
                SnpDesignResult value = Design(annotated, options);
                Require(value.Sets.Count > 0, "Custom constraints produced no artificial candidate sets.");
                ValidateResult(value);
            });

            test("SNP group and control scores ignore control-product length for otherwise identical oligos", delegate
            {
                // N spacers allow exactly one 30-nt binding window in each block.
                // Moving the same control oligo changes only its product length (210 vs 170 bp).
                SnpPrimerSet longer = OnlySet(Design(LengthScoringTemplate(true, false), LengthScoringSettings(130)));
                SnpPrimerSet shorter = OnlySet(Design(LengthScoringTemplate(false, true), LengthScoringSettings(130)));
                Require(longer.ReferencePair.AmpliconLength == 130 && shorter.ReferencePair.AmpliconLength == 130,
                    "Controlled allele products must remain 130 bp.");
                Require(longer.ControlPair.AmpliconLength == 210 && shorter.ControlPair.AmpliconLength == 170,
                    "Controlled control products must differ by 40 bp.");
                Equal(longer.ControlForward.Sequence, shorter.ControlForward.Sequence, "Identical control oligos at different coordinates");
                Equal(longer.ReferenceForward.Sequence, shorter.ReferenceForward.Sequence, "Fixed reference allele oligo");
                Equal(longer.CommonReverse.Sequence, shorter.CommonReverse.Sequence, "Fixed common reverse oligo");
                Near(longer.Score, shorter.Score, 0.000001, "SNP group score must ignore control-product length");
                Near(longer.ControlPair.Score, shorter.ControlPair.Score, 0.000001, "Control-pair score must ignore its product length");
            });

            test("SNP preferred-product length affects only allele products and cannot prefer a different identical control", delegate
            {
                SnpPrimerSet preferredAllele = OnlySet(Design(LengthScoringTemplate(true, true), LengthScoringSettings(130)));
                foreach (int preferred in new int[] { 170, 210 })
                {
                    SnpPrimerSet changed = OnlySet(Design(LengthScoringTemplate(true, true), LengthScoringSettings(preferred)));
                    Require(preferredAllele.ControlForward.Start == 1 && changed.ControlForward.Start == 1,
                        "Identical controls must retain the coordinate tie-break even when the preferred length matches the shorter control product.");
                    Require(changed.ReferenceForward.Start == preferredAllele.ReferenceForward.Start
                        && changed.CommonReverse.End == preferredAllele.CommonReverse.End,
                        "The controlled fixture must compare the same allele candidate after changing the preference.");
                    double addedLengthPenalty = 8.0 * Math.Abs(130 - preferred) / 120.0;
                    // Invert the rounded public score; 0.02 covers its 0.01-point rounding only.
                    double expected = ScoreAfterAddedPenalty(preferredAllele.Score, addedLengthPenalty);
                    Near(expected, changed.Score, 0.02, "Only the full allele-length penalty may change the SNP group score");
                    Require(changed.Score < preferredAllele.Score - 1.0, "Moving away from the allele length must have a visible scoring effect.");
                    Near(preferredAllele.ControlPair.Score, changed.ControlPair.Score, 0.000001,
                        "Changing the preferred length must not change the same control-pair score");
                }
            });

            test("SNP control-product maximum remains a hard constraint after removing its scoring preference", delegate
            {
                SnpDesignSettings bounded = LengthScoringSettings(130);
                bounded.Base.AmpliconMax = 200;
                SnpDesignResult value = Design(LengthScoringTemplate(true, true), bounded);
                SnpPrimerSet set = OnlySet(value);
                Require(set.ControlForward.Start == 41 && set.ControlPair.AmpliconLength == 170,
                    "The 210-bp control must be excluded even though its length is no longer penalized.");
                ValidateResult(value);
                bounded.Base.AmpliconMax = 169;
                Require(Design(LengthScoringTemplate(true, true), bounded).Sets.Count == 0,
                    "No complete set may survive when both control products exceed the maximum, despite the 130-bp allele product fitting.");
            });

            test("Ordinary RPA keeps product-length preference in its score", delegate
            {
                ParsedSequence sequence = SnpParser.Parse(LengthScoringTemplate(false, false)).Reference;
                DesignSettings bestSettings = LengthScoringSettings(130).Base;
                bestSettings.TargetStart = 120; bestSettings.TargetEnd = 170;
                DesignResult best = DesignEngine.Design(sequence, bestSettings, null, CancellationToken.None);
                DesignSettings changedSettings = LengthScoringSettings(170).Base;
                changedSettings.TargetStart = 120; changedSettings.TargetEnd = 170;
                DesignResult changed = DesignEngine.Design(sequence, changedSettings, null, CancellationToken.None);
                Require(best.Pairs.Count == 1 && changed.Pairs.Count == 1, "Ordinary RPA fixture must isolate one primer pair.");
                Equal(best.Pairs[0].Forward.Sequence, changed.Pairs[0].Forward.Sequence, "Ordinary RPA fixed forward oligo");
                Equal(best.Pairs[0].Reverse.Sequence, changed.Pairs[0].Reverse.Sequence, "Ordinary RPA fixed reverse oligo");
                Require(best.Pairs[0].AmpliconLength == 130, "Ordinary RPA fixture must produce a 130-bp product.");
                Near(ScoreAfterAddedPenalty(best.Pairs[0].Score, 8.0 * 40.0 / 120.0), changed.Pairs[0].Score, 0.02,
                    "Ordinary RPA must retain its full product-length penalty");
                Require(changed.Pairs[0].Score < best.Pairs[0].Score - 1.0, "Ordinary product-length preference unexpectedly stopped affecting ranking.");
            });

            test("Insufficient flanks return no partial primer sets", delegate
            {
                string[] shortFlanks = { "[A>C]" + template, template + "[A>C]", "ACGT[A>C]TGCA" };
                foreach (string input in shortFlanks)
                {
                    SnpDesignResult value = Design(input, Settings(0));
                    Require(value.Sets.Count == 0, "Insufficient flanks must not produce a partial or incorrectly anchored set.");
                    Require(value.Notes != null && value.Notes.Count > 0, "No-result output should explain its limitations.");
                }
            });

            test("Ambiguous bases at anchored forward-primer site prevent false complete sets", delegate
            {
                string input = template.Substring(0, 209 - 35) + new string('N', 35) + "[A>C]" + template.Substring(210);
                SnpDesignResult value = Design(input, Settings(0));
                Require(value.Sets.Count == 0, "An allele-specific primer cannot skip an ambiguous base immediately before the SNP.");
            });

            test("Invalid SNP and ordinary-target settings are rejected explicitly", delegate
            {
                SnpInput input = SnpParser.Parse(annotated);
                ExpectArgument(delegate { SnpDesignEngine.Design(input, null, null, CancellationToken.None); }, "Null SNP settings");
                ExpectArgument(delegate { SnpDesignEngine.Design(null, Settings(0), null, CancellationToken.None); }, "Null SNP input");
                int[] invalidOffsets = { -1, 1, 4 };
                foreach (int offset in invalidOffsets)
                {
                    int current = offset;
                    ExpectArgument(delegate { Design(annotated, Settings(current)); }, "Invalid mismatch offset");
                }
                SnpDesignSettings ordinaryTarget = Settings(0);
                ordinaryTarget.Base.TargetStart = 180;
                ordinaryTarget.Base.TargetEnd = 215;
                ExpectArgument(delegate { Design(annotated, ordinaryTarget); }, "Ordinary protected-target constraints in SNP mode");
                SnpDesignSettings invalidLength = Settings(0);
                invalidLength.Base.PrimerMin = 36;
                invalidLength.Base.PrimerMax = 30;
                ExpectArgument(delegate { Design(annotated, invalidLength); }, "Reversed primer length range");
                SnpDesignSettings invalidGc = Settings(0);
                invalidGc.Base.GcMin = Double.NaN;
                ExpectArgument(delegate { Design(annotated, invalidGc); }, "Non-finite GC bound");
            });

            test("Cancellation before work propagates without partial results", delegate
            {
                using (var source = new CancellationTokenSource())
                {
                    source.Cancel();
                    bool cancelled = false;
                    try { SnpDesignEngine.Design(SnpParser.Parse(annotated), Settings(0), null, source.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "A pre-cancelled operation must throw OperationCanceledException.");
                }
            });

            test("Progress-triggered cancellation is observed during design", delegate
            {
                using (var source = new CancellationTokenSource())
                {
                    bool cancelled = false, notified = false;
                    Action<int, string> progress = delegate(int percent, string message)
                    {
                        Require(percent >= 0 && percent <= 100, "Progress percentage is out of range.");
                        notified = true;
                        source.Cancel();
                    };
                    try { SnpDesignEngine.Design(SnpParser.Parse(annotated), Settings(3), progress, source.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(notified && cancelled, "Cancellation requested through progress must be honored.");
                }
            });

            test("Repeated designs are deterministic and do not mutate caller-owned input or settings", delegate
            {
                SnpInput input = SnpParser.Parse(">artificial immutability test\n" + annotated);
                input.Reference.Warnings.Add("caller-owned warning");
                string refBefore = input.Reference.Sequence, altBefore = input.Alternate.Sequence;
                string annotationBefore = input.AnnotatedSequence;
                int warningsBefore = input.Reference.Warnings.Count;
                SnpDesignSettings options = Settings(0);
                string settingsBefore = SettingsFingerprint(options);
                SnpDesignResult first = SnpDesignEngine.Design(input, options, null, CancellationToken.None);
                SnpDesignResult second = SnpDesignEngine.Design(input, options, null, CancellationToken.None);
                Require(first.Sets.Count > 0, "Determinism test requires complete candidates.");
                Equal(ResultFingerprint(first), ResultFingerprint(second), "Deterministic candidate order and score");
                Equal(refBefore, input.Reference.Sequence, "Caller reference template");
                Equal(altBefore, input.Alternate.Sequence, "Caller alternate template");
                Equal(annotationBefore, input.AnnotatedSequence, "Caller annotated sequence");
                Equal(settingsBefore, SettingsFingerprint(options), "Caller options");
                Require(input.Reference.Warnings.Count == warningsBefore, "Caller warnings were mutated.");
                Require(!Object.ReferenceEquals(options, first.Settings) && !Object.ReferenceEquals(options.Base, first.Settings.Base), "Result settings must be a snapshot.");
                Require(!Object.ReferenceEquals(input, first.Input) && !Object.ReferenceEquals(input.Reference, first.Input.Reference), "Result templates must be a snapshot.");
                options.Base.GcMin = 0;
                input.Reference.Sequence = "ACGT";
                Require(first.Settings.Base.GcMin == 30 && first.Input.Reference.Sequence.Length == template.Length, "Later caller changes altered an existing result.");
            });

            test("Built-in SNP example parses and produces complete A-to-C candidate sets", delegate
            {
                SnpInput demo = SnpParser.Parse(SnpReportWriter.ExampleFasta());
                Require(demo.Position == 300 && demo.ReferenceAllele == 'A' && demo.AlternateAllele == 'C', "Built-in demonstration has the wrong SNP annotation.");
                Require(demo.Reference.Sequence.Length == demo.Alternate.Sequence.Length, "Demo allele templates differ in length.");
                Require(Differences(demo.Reference.Sequence, demo.Alternate.Sequence) == 1, "Demo alleles must differ at exactly one base.");
                SnpDesignResult value = SnpDesignEngine.Design(demo, Settings(0), null, CancellationToken.None);
                Require(value.Sets.Count > 0, "Built-in demonstration must generate usable candidate sets.");
                ValidateResult(value);
            });

            test("CSV preserves four literal oligos and 31 fields with safe names and invariant decimals", delegate
            {
                SnpDesignResult value = baseline ?? Design(annotated, Settings(0));
                Require(value.Sets.Count > 0, "CSV regression requires complete candidate sets.");
                CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
                string originalName = value.Input.Reference.Name;
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                    foreach (char prefix in "=+-@\t\r\n")
                    {
                        string unusualName = prefix + "source, \"quoted\"\r\nsecond line";
                        value.Input.Reference.Name = unusualName;
                        List<List<string>> rows = ParseCsv(SnpReportWriter.Csv(value));
                        Require(rows.Count == 1 + value.Sets.Count * 4, "CSV must contain exactly four oligo rows per complete set.");
                        Require(rows[0].Count == 31, "CSV header must expose all 31 documented fields.");
                        for (int i = 0; i < value.Sets.Count; i++)
                        {
                            SnpPrimerSet set = value.Sets[i];
                            Primer[] primers = { set.ReferenceForward, set.AlternateForward, set.CommonReverse, set.ControlForward };
                            string[] roles = { "F_ref_A", "F_alt_C", "R_common", "F_control" };
                            for (int j = 0; j < primers.Length; j++)
                            {
                                List<string> row = rows[1 + 4 * i + j];
                                Require(row.Count == 31, "CSV row was split by embedded commas, quotes or newlines.");
                                Equal("'" + unusualName, row[0], "CSV formula-like source name neutralization");
                                Equal(roles[j], row[2], "CSV primer role");
                                Equal(primers[j].Sequence, row[3], "CSV literal synthesized oligo");
                                Equal(primers[j].Gc.ToString("0.0", CultureInfo.InvariantCulture), row[7], "CSV invariant GC decimal");
                                Equal(set.ReferencePair.AmpliconSequence, row[24], "CSV reference engineered product");
                                Equal(set.AlternatePair.AmpliconSequence, row[25], "CSV alternate engineered product");
                                Equal(set.ControlPair.AmpliconSequence, row[26], "CSV reference control product");
                                Equal(set.AlternateControlAmpliconSequence, row[27], "CSV alternate control product");
                                Require(row[30].Contains("未证明等位基因特异性") && row[30].Contains("分开反应") && row[30].Contains("未做基因组特异性检索"), "CSV must retain interpretation limits and independent-reaction instructions.");
                            }
                        }
                    }
                }
                finally
                {
                    value.Input.Reference.Name = originalName;
                    Thread.CurrentThread.CurrentCulture = originalCulture;
                }
            });

            test("CSV additional mismatch metadata applies only to allele-specific forward oligos", delegate
            {
                foreach (int offset in new int[] { 0, 2, 3 })
                {
                    SnpDesignResult value = Design(annotated, Settings(offset));
                    Require(value.Sets.Count > 0, "Mismatch export regression requires complete sets.");
                    List<List<string>> rows = ParseCsv(SnpReportWriter.Csv(value));
                    for (int i = 0; i < value.Sets.Count; i++)
                    {
                        SnpPrimerSet set = value.Sets[i];
                        for (int j = 0; j < 4; j++)
                        {
                            List<string> row = rows[1 + 4 * i + j];
                            bool hasMismatch = j < 2 && offset > 0;
                            Equal(hasMismatch ? offset.ToString(CultureInfo.InvariantCulture) : "0", row[12], "Per-oligo mismatch offset");
                            Equal(hasMismatch ? set.ExtraMismatchPosition.ToString(CultureInfo.InvariantCulture) : "", row[13], "Per-oligo mismatch coordinate");
                            Equal(hasMismatch ? set.ExtraMismatchTemplateBase.ToString() : "", row[14], "Per-oligo mismatch native base");
                            Equal(hasMismatch ? set.ExtraMismatchPrimerBase.ToString() : "", row[15], "Per-oligo mismatch synthesized base");
                        }
                    }
                }
            });

            test("FASTA and ordering exports contain exactly four unannotated synthesized oligos per set", delegate
            {
                SnpDesignResult value = Design(annotated, Settings(3));
                Require(value.Sets.Count > 0, "FASTA export regression requires complete candidate sets.");
                string[] fasta = SnpReportWriter.Fasta(value).Replace("\r", "").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Require(fasta.Length == value.Sets.Count * 8, "FASTA must contain exactly four two-line records per set.");
                for (int i = 0; i < value.Sets.Count; i++)
                {
                    SnpPrimerSet set = value.Sets[i];
                    Primer[] primers = { set.ReferenceForward, set.AlternateForward, set.CommonReverse, set.ControlForward };
                    string[] roles = { "F_ref_A", "F_alt_C", "R_common", "F_control" };
                    string[] ordering = SnpReportWriter.OrderingText(set, value.Input).Replace("\r", "").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    Require(ordering.Length == 4, "An ordering export must have exactly four oligos.");
                    for (int j = 0; j < 4; j++)
                    {
                        string expectedName = "SNP_" + set.Rank + "_" + roles[j];
                        string header = fasta[i * 8 + j * 2];
                        Require(header.StartsWith(">" + expectedName + " ", StringComparison.Ordinal), "FASTA record has the wrong set or oligo role.");
                        Require(header.Contains("candidate_unvalidated"), "FASTA header must disclose candidate status.");
                        Require(header.Contains("extra_mismatch=" + (j < 2 ? set.ExtraMismatchPosition : 0)), "FASTA mismatch coordinates were attached to the wrong oligo.");
                        Equal(primers[j].Sequence, fasta[i * 8 + j * 2 + 1], "FASTA literal synthesized oligo");
                        string[] columns = ordering[j].Split('\t');
                        Require(columns.Length == 2, "Ordering export must separate name and sequence with one tab.");
                        Equal(expectedName, columns[0], "Ordering oligo label");
                        Equal(primers[j].Sequence, columns[1], "Ordering literal synthesized oligo");
                    }
                }
            });

            test("Text export retains all primers, independent reactions, engineered products and interpretation limits", delegate
            {
                SnpDesignResult value = Design(annotated, Settings(2));
                Require(value.Sets.Count > 0, "Text export regression requires complete candidate sets.");
                string full = SnpReportWriter.TextReport(value);
                Require(full.Contains("不是选择性或成功率") && full.Contains("RPA 可容忍错配") && full.Contains("未进行全基因组/近似匹配检索"), "Text report must disclose score and allele-discrimination limitations.");
                Require(full.Contains("热力学") && full.Contains("不是反应温度") && full.Contains("不含 LNA"), "Text report must disclose structure, Tm and oligo chemistry limits.");
                Require(full.Contains(value.Input.AnnotatedSequence) && full.Contains(value.Input.Reference.Sequence) && full.Contains(value.Input.Alternate.Sequence), "Text report must preserve the normalized annotation and both templates.");
                foreach (SnpPrimerSet set in value.Sets)
                {
                    string text = SnpReportWriter.SetText(set, value.Input);
                    foreach (Primer primer in new Primer[] { set.ReferenceForward, set.AlternateForward, set.CommonReverse, set.ControlForward })
                        Require(text.Contains(primer.Sequence), "Set report omitted a synthesized primer.");
                    Require(text.Contains("F_ref + R_common") && text.Contains("F_alt + R_common") && text.Contains("F_control + R_common") && text.Contains("独立反应"), "Set report must describe all three independent reaction combinations.");
                    Require(text.Contains("不代表选择性") && text.Contains("不是扩增是否发生的预测"), "Set report must not imply experimentally established discrimination or amplification.");
                    Require(text.Contains(set.ReferencePair.AmpliconSequence) && text.Contains(set.AlternatePair.AmpliconSequence) && text.Contains(set.ControlPair.AmpliconSequence) && text.Contains(set.AlternateControlAmpliconSequence), "Set report must retain all four allele-dependent product sequences.");
                    Require(text.Contains("人为错配") && text.Contains(set.ExtraMismatchPosition.ToString(CultureInfo.InvariantCulture)), "Set report lost the deliberate mismatch coordinate.");
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
                Console.Error.WriteLine("Could not write SNP self-test report: " + ex.Message);
                return 1;
            }
            return failed == 0 ? 0 : 1;
        }

        private static SnpDesignSettings Settings(int offset)
        {
            return new SnpDesignSettings { Base = new DesignSettings { MaxPairs = 3 }, ExtraMismatchFromThreePrime = offset };
        }

        private static SnpDesignSettings LengthScoringSettings(int preferred)
        {
            return new SnpDesignSettings { Base = new DesignSettings {
                PrimerMin = 30, PrimerMax = 30, AmpliconMin = 100, AmpliconMax = 220,
                PreferredAmplicon = preferred, MaxPairs = 10 } };
        }

        private static string LengthScoringTemplate(bool firstControl, bool secondControl)
        {
            const string control = "AGTCCGATGCTAGACTGACCTAGTCGATCA";
            const string allele = "TCAGACTCGATGTAGCTACGACTGCTAGCA";
            const string reverseBinding = "GCTAGTCAGATCGTACCTGACTAGCTACGA";
            Require(control.Length == 30 && allele.Length == 30 && reverseBinding.Length == 30,
                "Length-scoring fixture binding windows must be 30 nt.");
            string sequence = (firstControl ? control : new string('N', 30)) + new string('N', 10)
                + (secondControl ? control : new string('N', 30)) + new string('N', 10)
                + allele + new string('N', 70) + reverseBinding;
            return Annotate(sequence, 110, 'A', 'C');
        }

        private static SnpPrimerSet OnlySet(SnpDesignResult result)
        {
            Require(result.Sets.Count == 1, "Controlled SNP fixture must yield exactly one unique complete oligo set; got " + result.Sets.Count + ".");
            return result.Sets[0];
        }

        private static double ScoreAfterAddedPenalty(double originalScore, double addedPenalty)
        {
            double originalPenalty = 25.0 * (100.0 / originalScore - 1.0);
            return Math.Round(100.0 / (1.0 + (originalPenalty + addedPenalty) / 25.0), 2);
        }

        private static void Near(double expected, double actual, double tolerance, string label)
        {
            Require(Math.Abs(expected - actual) <= tolerance,
                label + ". Expected: " + expected.ToString("R", CultureInfo.InvariantCulture)
                + "; actual: " + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        private static SnpDesignResult Design(string annotation, SnpDesignSettings options)
        {
            return SnpDesignEngine.Design(SnpParser.Parse(annotation), options, null, CancellationToken.None);
        }

        private static void ValidateResult(SnpDesignResult result)
        {
            Require(result != null && result.Sets != null, "SNP result must be initialized.");
            SnpInput input = result.Input;
            DesignSettings settings = result.Settings.Base;
            Require(result.Sets.Count <= settings.MaxPairs, "Requested maximum number of sets was exceeded.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < result.Sets.Count; index++)
            {
                SnpPrimerSet set = result.Sets[index];
                Require(set.Rank == index + 1, "Set ranks must be sequential and one-based.");
                Require(set.ReferenceForward.End == input.Position && set.AlternateForward.End == input.Position,
                    "Both allele-specific forward primers must end exactly at the SNP.");
                Require(set.ReferenceForward.Start == set.AlternateForward.Start, "Allele-specific forward primers must share the same binding window.");
                Require(set.ControlForward.End < input.Position && set.CommonReverse.Start > input.Position,
                    "Control forward and common reverse must not overlap the SNP.");
                ValidatePrimer(set.ReferenceForward, input.Reference.Sequence, settings, false, set.ExtraMismatchPosition, set.ExtraMismatchPrimerBase);
                ValidatePrimer(set.AlternateForward, input.Alternate.Sequence, settings, false, set.ExtraMismatchPosition, set.ExtraMismatchPrimerBase);
                ValidatePrimer(set.CommonReverse, input.Reference.Sequence, settings, true, 0, '\0');
                ValidatePrimer(set.CommonReverse, input.Alternate.Sequence, settings, true, 0, '\0');
                ValidatePrimer(set.ControlForward, input.Reference.Sequence, settings, false, 0, '\0');
                ValidatePrimer(set.ControlForward, input.Alternate.Sequence, settings, false, 0, '\0');
                ValidatePair(set.ReferencePair, set.ReferenceForward, set.CommonReverse, input.Reference.Sequence, settings);
                ValidatePair(set.AlternatePair, set.AlternateForward, set.CommonReverse, input.Alternate.Sequence, settings);
                ValidatePair(set.ControlPair, set.ControlForward, set.CommonReverse, input.Reference.Sequence, settings);
                Equal(input.Alternate.Sequence.Substring(set.ControlPair.AmpliconStart - 1, set.ControlPair.AmpliconLength),
                    set.AlternateControlAmpliconSequence, "Alternate control product");
                Require(Finite(set.Score), "SNP set score must be finite.");
                string key = set.ReferenceForward.Sequence + ":" + set.AlternateForward.Sequence + ":" + set.CommonReverse.Sequence + ":" + set.ControlForward.Sequence;
                Require(unique.Add(key), "Duplicate complete oligo set was emitted.");
            }
        }

        private static void ValidatePrimer(Primer primer, string template, DesignSettings settings, bool reverse, int mismatch, char mismatchBase)
        {
            Require(primer != null && primer.Sequence != null, "Primer must be initialized.");
            Require(primer.Start >= 1 && primer.End >= primer.Start && primer.End <= template.Length, "Primer coordinates are invalid.");
            int length = primer.End - primer.Start + 1;
            Require(length == primer.Sequence.Length && length >= settings.PrimerMin && length <= settings.PrimerMax, "Primer length violates its coordinates or settings.");
            string expected = template.Substring(primer.Start - 1, length);
            if (mismatch > 0)
            {
                Require(!reverse && mismatch >= primer.Start && mismatch <= primer.End, "Deliberate mismatch coordinate is outside its forward primer.");
                char[] bases = expected.ToCharArray();
                bases[mismatch - primer.Start] = mismatchBase;
                expected = new string(bases);
            }
            if (reverse) expected = ReverseComplement(expected);
            Equal(expected, primer.Sequence, "Synthesized primer sequence and orientation");
            int gc = 0, repeat = 0, longest = 0;
            char previous = '\0';
            foreach (char basis in primer.Sequence)
            {
                Require("ACGT".IndexOf(basis) >= 0, "A synthesized primer contains a noncanonical base.");
                if (basis == 'C' || basis == 'G') gc++;
                repeat = basis == previous ? repeat + 1 : 1;
                previous = basis;
                longest = Math.Max(longest, repeat);
            }
            double expectedGc = 100.0 * gc / length;
            Require(Math.Abs(primer.Gc - expectedGc) < 0.11, "Primer GC metric does not describe the synthesized oligo.");
            Require(expectedGc >= settings.GcMin - 0.000001 && expectedGc <= settings.GcMax + 0.000001, "Synthesized primer GC violates requested bounds.");
            Require(longest <= 5, "Primer includes a homopolymer excluded by the design rules.");
            Require(Finite(primer.Tm), "Primer Tm must be finite.");
            Require(primer.Hairpin >= 0 && primer.SelfComplement >= 0 && primer.SelfThreePrime >= 0, "Primer structure counts must be nonnegative.");
        }

        private static void ValidatePair(PrimerPair pair, Primer forward, Primer reverse, string template, DesignSettings settings)
        {
            Require(pair != null, "Each complete set must include three pair records.");
            Equal(forward.Sequence, pair.Forward.Sequence, "Pair forward primer");
            Equal(reverse.Sequence, pair.Reverse.Sequence, "Pair shared reverse primer");
            Require(pair.AmpliconStart == forward.Start && pair.AmpliconEnd == reverse.End, "Pair product boundaries are inconsistent with its oligos.");
            Require(forward.End < reverse.Start, "Forward and reverse binding windows must not overlap.");
            Require(pair.AmpliconLength == reverse.End - forward.Start + 1, "Product length must use inclusive template coordinates.");
            Require(pair.AmpliconLength >= settings.AmpliconMin && pair.AmpliconLength <= settings.AmpliconMax, "A product length violates the requested range.");
            char[] product = template.Substring(pair.AmpliconStart - 1, pair.AmpliconLength).ToCharArray();
            forward.Sequence.CopyTo(0, product, 0, forward.Sequence.Length);
            string reverseBinding = ReverseComplement(reverse.Sequence);
            reverseBinding.CopyTo(0, product, product.Length - reverseBinding.Length, reverseBinding.Length);
            Equal(new string(product), pair.AmpliconSequence, "Product must incorporate synthesized primer bases");
            Require(Finite(pair.Score), "Pair score must be finite.");
        }

        private static void ValidateExtraMismatch(SnpDesignResult result, int offset)
        {
            foreach (SnpPrimerSet set in result.Sets)
            {
                int expectedPosition = result.Input.Position - offset + 1;
                Require(set.ExtraMismatchPosition == expectedPosition, "Extra mismatch is not at the requested base from the 3-prime end.");
                char original = result.Input.Reference.Sequence[expectedPosition - 1];
                Require(set.ExtraMismatchTemplateBase == original, "Recorded mismatch template base is wrong.");
                Require("ACGT".IndexOf(set.ExtraMismatchPrimerBase) >= 0 && set.ExtraMismatchPrimerBase != original, "The deliberate mismatch must be a different canonical base.");
                string nativeRef = result.Input.Reference.Sequence.Substring(set.ReferenceForward.Start - 1, set.ReferenceForward.Sequence.Length);
                string nativeAlt = result.Input.Alternate.Sequence.Substring(set.AlternateForward.Start - 1, set.AlternateForward.Sequence.Length);
                Require(Differences(nativeRef, set.ReferenceForward.Sequence) == 1, "Reference forward primer must contain exactly one deliberate additional mismatch.");
                Require(Differences(nativeAlt, set.AlternateForward.Sequence) == 1, "Alternate forward primer must contain exactly one deliberate additional mismatch.");
                Require(Differences(set.ReferenceForward.Sequence, set.AlternateForward.Sequence) == 1, "Allele-specific oligos must use the same additional mismatch.");
                Require(set.ReferencePair.AmpliconSequence[expectedPosition - set.ReferencePair.AmpliconStart] == set.ExtraMismatchPrimerBase, "Reference product omits synthesized mismatch.");
                Require(set.AlternatePair.AmpliconSequence[expectedPosition - set.AlternatePair.AmpliconStart] == set.ExtraMismatchPrimerBase, "Alternate product omits synthesized mismatch.");
                Require(set.ControlPair.AmpliconSequence[expectedPosition - set.ControlPair.AmpliconStart] == original, "Control product must retain its native sequence.");
            }
        }

        private static string ResultFingerprint(SnpDesignResult result)
        {
            var text = new StringBuilder();
            foreach (SnpPrimerSet set in result.Sets)
            {
                text.Append(set.Rank).Append('|').Append(set.Score.ToString("R", CultureInfo.InvariantCulture)).Append('|');
                foreach (Primer p in new Primer[] { set.ReferenceForward, set.AlternateForward, set.CommonReverse, set.ControlForward })
                    text.Append(p.Start).Append(':').Append(p.End).Append(':').Append(p.Sequence).Append('|');
            }
            return text.ToString();
        }

        private static string SettingsFingerprint(SnpDesignSettings value)
        {
            DesignSettings s = value.Base;
            return String.Join("|", new string[] { s.PrimerMin.ToString(), s.PrimerMax.ToString(), s.AmpliconMin.ToString(),
                s.AmpliconMax.ToString(), s.PreferredAmplicon.ToString(), s.MaxPairs.ToString(), s.TargetStart.ToString(),
                s.TargetEnd.ToString(), s.GcMin.ToString("R", CultureInfo.InvariantCulture), s.GcMax.ToString("R", CultureInfo.InvariantCulture),
                value.ExtraMismatchFromThreePrime.ToString() });
        }

        private static string ReverseComplement(string sequence)
        {
            var result = new char[sequence.Length];
            for (int i = 0; i < sequence.Length; i++)
            {
                int index = "ACGT".IndexOf(sequence[sequence.Length - i - 1]);
                Require(index >= 0, "Independent reverse-complement check requires canonical DNA.");
                result[i] = "TGCA"[index];
            }
            return new string(result);
        }

        // Independent RFC 4180 reader checks real record boundaries, including quoted CR/LF.
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

        private static int Differences(string first, string second)
        {
            Require(first.Length == second.Length, "Difference count requires equal sequence lengths.");
            int differences = 0;
            for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) differences++;
            return differences;
        }

        private static string Annotate(string sequence, int position, char reference, char alternate)
        {
            return sequence.Substring(0, position - 1) + "[" + reference + ">" + alternate + "]" + sequence.Substring(position);
        }

        private static string SyntheticSequence(int length, int seed)
        {
            var random = new Random(seed);
            var sequence = new StringBuilder(length);
            const string bases = "ACGT";
            for (int i = 0; i < length; i++) sequence.Append(bases[random.Next(bases.Length)]);
            return sequence.ToString();
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static void Equal(string expected, string actual, string label)
        {
            Require(String.Equals(expected, actual, StringComparison.Ordinal), label + " differs. Expected: " + expected + "; actual: " + actual);
        }

        private static void ExpectArgument(Action body, string label)
        {
            bool rejected = false;
            try { body(); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, label + " must throw ArgumentException.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
