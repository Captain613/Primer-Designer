using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    // Artificial fixtures only. No request is sent to NCBI by these tests.
    public static class BlastAnalysisSelfTests
    {
        public static int Run(string reportPath)
        {
            List<string> lines = new List<string>(); int passed = 0, failed = 0;
            lines.Add("BLAST query / interpretation offline self-tests");
            lines.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            lines.Add("Software contract checks only; no biological specificity validation or network traffic.");
            Action<string, Action> test = delegate(string name, Action action)
            {
                try { action(); passed++; lines.Add("PASS  " + name); }
                catch (Exception ex) { failed++; lines.Add("FAIL  " + name + ": " + ex.Message); lines.Add(ex.StackTrace ?? ""); }
            };

            test("RPA snapshot uses actual primers, anonymous IDs, deduplication and independent copy", delegate
            {
                PrimerPair pair = Pair(); pair.Rank = 7; pair.AmpliconSequence = "PRIVATE_TEMPLATE";
                BlastQuerySet query = BlastQueryBuilder.ForRpa(pair);
                Require(query.Queries.Count == 2 && query.Reactions.Count == 1 && query.CandidateLabel.Contains("#7"), "RPA snapshot count/rank");
                Require(query.Queries[0].Id == "Q001" && query.Queries[1].Id == "Q002", "Anonymous IDs");
                Require(!query.Reactions[0].Bindings[0].Reverse && query.Reactions[0].Bindings[1].Reverse, "RPA direction");
                string original = pair.Forward.Sequence; pair.Forward.Sequence = "AAAA";
                Require(query.Queries[0].Sequence == original, "Snapshot leaked mutable source");
                pair.Reverse.Sequence = pair.Forward.Sequence;
                BlastQuerySet same = BlastQueryBuilder.ForRpa(pair);
                Require(same.Queries.Count == 1 && same.Reactions[0].Bindings[0].QueryId == same.Reactions[0].Bindings[1].QueryId, "Identical actual sequences must deduplicate");
            });
            test("SNP query builder preserves artificial mismatch and separates three reactions", delegate
            {
                SnpDesignSettings settings = new SnpDesignSettings { ExtraMismatchFromThreePrime = 3 };
                settings.Base.MaxPairs = 1;
                SnpDesignResult actual = SnpDesignEngine.Design(SnpParser.Parse(SnpReportWriter.ExampleFasta()), settings, null, CancellationToken.None);
                Require(actual.Sets.Count > 0, "SNP demo must design a candidate");
                SnpPrimerSet primers = actual.Sets[0]; BlastQuerySet query = BlastQueryBuilder.ForSnp(primers);
                Require(query.Queries.Count == 4 && query.Reactions.Count == 3, "RPA SNP queries/reactions");
                Require(QueryFor(query, query.Reactions[0].Bindings[0]).Sequence == primers.ReferenceForward.Sequence, "Lost reference mismatch");
                Require(QueryFor(query, query.Reactions[1].Bindings[0]).Sequence == primers.AlternateForward.Sequence, "Lost alternate mismatch");
                int mismatch = primers.ExtraMismatchPosition - primers.ReferenceForward.Start;
                Require(mismatch >= 0 && query.Queries[0].Sequence[mismatch] == primers.ExtraMismatchPrimerBase, "Mismatch restored to template");
                Require(query.Reactions[0].Bindings[1].QueryId == query.Reactions[2].Bindings[1].QueryId, "Common reverse not reused");
            });
            test("LAMP/mLAMP use core binding segments including actual modified F2 and auxiliary loops", delegate
            {
                string source = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
                LampDesignSettings settings = Broad("mLAMP"); settings.SnpOrientation = "FIP"; settings.ExtraMismatchFromThreePrime = 3;
                LampDesignResult actual = LampDesignEngine.DesignSnp(SnpParser.Parse(source.Substring(0, 332) + "[T>C]" + source.Substring(333)), settings, null, CancellationToken.None);
                Require(actual.Sets.Count > 0, "mLAMP demo must design a candidate");
                LampPrimerSet primers = actual.Sets[0]; BlastQuerySet query = BlastQueryBuilder.ForLamp(primers, "mLAMP");
                Require(query.Reactions.Count == 2 && query.Queries.Count == 7, "Expected six core regions plus alternate F2");
                string[] roles = { "F3", "F2", "F1c", "B1c", "B2", "B3" }; bool[] direction = { false, false, true, false, true, true };
                for (int i = 0; i < roles.Length; i++) Require(query.Reactions[0].Bindings[i].Role == roles[i] && query.Reactions[0].Bindings[i].Reverse == direction[i], "Core order/direction");
                Require(QueryFor(query, query.Reactions[0].Bindings[1]).Sequence == primers.FIP.Regions[1].Sequence, "Actual modified F2 lost");
                Require(QueryFor(query, query.Reactions[1].Bindings[1]).Sequence == primers.AlternateInner.Regions[1].Sequence, "Actual alternate modified F2 lost");
                foreach (BlastQuery item in query.Queries) Require(item.Sequence != primers.FIP.Sequence && item.Sequence != primers.BIP.Sequence, "Submitted full concatenated inner primer");
                primers.LF = new LampOligo { Sequence = "ACGTTACGTACGATCTTGCA", Name = "LF" };
                BlastQuerySet loops = BlastQueryBuilder.ForLamp(primers, "mLAMP");
                Require(loops.Queries.Count == 8 && loops.Reactions[0].Bindings.Count == 6, "Auxiliary loop changed required core");
            });
            test("PA-LAMP uses active B2; precursor RNA and removable mismatch tail are not silently tested", delegate
            {
                string source = SequenceParser.Parse(LampReportWriter.ExampleFasta()).Sequence;
                LampDesignSettings settings = Broad("PA-LAMP");
                LampDesignResult actual = LampDesignEngine.DesignSnp(SnpParser.Parse(source.Substring(0, 332) + "[A>C]" + source.Substring(333)), settings, null, CancellationToken.None);
                Require(actual.Sets.Count > 0, "PA demo must design a candidate");
                LampPrimerSet primers = actual.Sets[0]; BlastQuerySet query = BlastQueryBuilder.ForLamp(primers, "PA-LAMP");
                Require(query.Queries.Count == 6 && query.Reactions.Count == 2, "PA identical active alleles deduplication");
                string active = QueryFor(query, query.Reactions[0].Bindings[4]).Sequence;
                Require(active == primers.BIP.ActivatedRegions[1].Sequence && active != primers.BIP.Regions[1].Sequence, "PA must use active B2");
                Require(String.Join(" ", query.Notes.ToArray()).Contains("未评估 RNase H2"), "PA limitation missing");
            });
            test("RPA geometry accepts inward plus/reverse loci but rejects wrong strands, overlap and distance", delegate
            {
                BlastQuerySet query = BlastQueryBuilder.ForRpa(Pair()); BlastSettings settings = new BlastSettings { MaxLocusSpan = 150 };
                BlastResult result = Empty(query); AddLocus(query, result, false, 100, "NC_TEST.1"); AddLocus(query, result, true, 500, "NC_TEST.1");
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 2, "Both orientations must work");
                result.Hits[1].Reverse = false;
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 1, "Wrong reverse must fail");
                result.Hits.Clear(); AddLocus(query, result, false, 100, "NC_TEST.1"); result.Hits[1].SubjectFrom = 125; result.Hits[1].SubjectTo = 106;
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 0, "Overlapping regions must fail");
                result.Hits.Clear(); AddLocus(query, result, false, 100, "NC_TEST.1"); result.Hits[1].SubjectFrom += 500; result.Hits[1].SubjectTo += 500;
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 0, "Distant pair must fail");
            });
            test("LAMP six-region order handles overall reverse strand and rejects rearrangements and split chromosomes", delegate
            {
                BlastQuerySet query = SyntheticLamp(); BlastSettings settings = new BlastSettings { MaxLocusSpan = 400 };
                BlastResult result = Empty(query); AddLocus(query, result, false, 100, "NC_TEST.1"); AddLocus(query, result, true, 1000, "NC_TEST.1");
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 2, "Six-region forward/reverse loci");
                result.Hits.RemoveRange(6, 6);
                int from = result.Hits[1].SubjectFrom, to = result.Hits[1].SubjectTo;
                result.Hits[1].SubjectFrom = result.Hits[3].SubjectFrom; result.Hits[1].SubjectTo = result.Hits[3].SubjectTo;
                result.Hits[3].SubjectFrom = from; result.Hits[3].SubjectTo = to;
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 0, "Out-of-order same-strand regions must fail");
                result.Hits.Clear(); AddLocus(query, result, false, 100, "NC_TEST.1"); result.Hits[5].Accession = "NC_OTHER.1";
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 0, "Do not mix chromosome records");
            });
            test("HSPs cannot combine coverage or mix far-away copies into one core locus", delegate
            {
                BlastQuerySet query = SyntheticLamp(); BlastSettings settings = new BlastSettings { MaxLocusSpan = 300 };
                BlastResult result = Empty(query); AddLocus(query, result, false, 100, "NC_TEST.1");
                result.Hits[2].Coverage = 50; result.Hits[2].QueryTo = 10;
                BlastHit other = Hit(query.Queries[2], 200, true, "NC_TEST.1"); other.Coverage = 50; other.QueryFrom = 11;
                result.Hits.Add(other);
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 0, "Separate half-HSPs must not meet coverage");
                result.Hits.Clear(); AddLocus(query, result, false, 100, "NC_TEST.1"); result.Hits[2].SubjectFrom += 2000; result.Hits[2].SubjectTo += 2000;
                Require(BlastAssessment.Evaluate(query, settings, result).Loci.Count == 0, "Far locus mixing must fail");
            });
            test("Expected accessions match versions conservatively without gene-title guessing", delegate
            {
                Require(!BlastAssessment.Expected("NM_001.2", "NM_001.1"), "Versioned accession cannot match a different version");
                Require(BlastAssessment.Expected("NM_001.2", "NC_999.1; NM_001,AB_002"), "Unversioned expected accession may match versions");
                Require(!BlastAssessment.Expected("NM_0010.2", "NM_001") && !BlastAssessment.Expected("NM_001.2", ""), "Prefix/empty must not guess target");
                BlastQuerySet query = BlastQueryBuilder.ForRpa(Pair()); BlastResult result = Empty(query); AddLocus(query, result, false, 100, "NC_TEST.1");
                string report = BlastAssessment.Text(query, new BlastSettings { ExpectedAccessions = "NC_TEST" }, result);
                Require(report.Contains("仍需核对坐标") && report.Contains("同一染色体可含多个非目标位点") && !report.Contains("RESULT: PASS"), "Expected chromosome cannot become specificity pass");
            });
            test("Zero hits, missing query, partial HSP, unknown terminal and hit cap are disclosed", delegate
            {
                BlastQuerySet query = BlastQueryBuilder.ForRpa(Pair()); BlastResult result = Empty(query);
                string empty = BlastAssessment.Text(query, new BlastSettings(), result);
                Require(empty.Contains("本次未发现返回命中") && empty.Contains("不能证明特异性"), "Zero-hit limitation");
                result.CompletedQueryIds.Remove(query.Queries[1].Id);
                BlastHit hit = Hit(query.Queries[0], 100, false, "NC_TEST.1"); hit.Coverage = 95; hit.QueryFrom = 2; hit.ThreePrimeMismatches = -1; result.Hits.Add(hit);
                string report = BlastAssessment.Text(query, new BlastSettings { HitListSize = 1 }, result);
                Require(report.Contains("缺少该查询") && report.Contains("仅覆盖部分查询") && report.Contains("未知（未完整覆盖或含歧义碱基）") && report.Contains("达到返回上限"), "Incomplete result warnings");
                Require(report.Contains(query.Queries[0].Sequence) && report.Contains("请求数据库") && report.Contains("NCBI 返回数据库") && report.Contains("E="), "Report audit fields");
            });
            test("Repeated hits have bounded combination search with explicit truncation", delegate
            {
                BlastQuerySet query = BlastQueryBuilder.ForRpa(Pair()); BlastResult result = Empty(query);
                for (int i = 0; i < 25; i++) result.Hits.Add(Hit(query.Queries[0], 100 + i, false, "NC_TEST.1"));
                for (int i = 0; i < 25; i++) result.Hits.Add(Hit(query.Queries[1], 300 + i, true, "NC_TEST.1"));
                BlastAssessment.Evaluation evaluation = BlastAssessment.Evaluate(query, new BlastSettings(), result);
                Require(evaluation.Truncated && evaluation.Loci.Count <= 200, "Unbounded combinations");
                Require(BlastAssessment.Text(query, new BlastSettings(), result).Contains("已截断"), "Search truncation not disclosed");
            });
            test("Text and CSV snapshots include real DNA, coordinates, thresholds and injection-safe titles", delegate
            {
                BlastQuerySet query = BlastQueryBuilder.ForRpa(Pair()); BlastResult result = Empty(query); AddLocus(query, result, false, 100, "NC_TEST.1");
                result.Hits[0].Title = "=HYPERLINK(\"https://example.invalid\")\nquote,comma";
                string csv = BlastAssessment.Csv(query, new BlastSettings(), result);
                Require(csv.Contains("potential_combination") && csv.Contains(query.Queries[0].Sequence) && csv.Contains("terminal_5nt_mismatches"), "CSV dropped information");
                Require(csv.Contains("\"'=HYPERLINK") && !csv.Contains("\"=HYPERLINK"), "CSV formula injection");
                List<int> columns = CsvColumnCounts(csv);
                foreach (int count in columns) Require(count == 22, "CSV column count " + count + " expected 22");
                string text = BlastAssessment.Text(query, new BlastSettings(), result);
                Require(!text.Contains("PRIVATE_TEMPLATE") && text.Contains("100→119") && text.Contains("5′→3′"), "Text snapshot fields/privacy");
            });
            test("Empty/chemically annotated sequence and invalid threshold fail explicitly", delegate
            {
                PrimerPair pair = Pair(); pair.Forward.Sequence = "ACG[rA]TC[C3]";
                ExpectArgument(delegate { BlastQueryBuilder.ForRpa(pair); });
                pair.Forward.Sequence = ""; ExpectArgument(delegate { BlastQueryBuilder.ForRpa(pair); });
                BlastQuerySet query = BlastQueryBuilder.ForRpa(Pair());
                ExpectArgument(delegate { BlastAssessment.Text(query, new BlastSettings { MinCoverage = Double.NaN }, Empty(query)); });
            });

            lines.Add(""); lines.Add("Passed: " + passed); lines.Add("Failed: " + failed); lines.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }

        private static PrimerPair Pair()
        { return new PrimerPair { Rank = 1, Forward = new Primer { Sequence = "ATGCTAGCTCGATCGATCGA" }, Reverse = new Primer { Sequence = "CGATGCTAGCTTAGCTAGCG" } }; }
        private static LampDesignSettings Broad(string method)
        { return new LampDesignSettings { SnpMethod = method, RegionMin = 20, RegionMax = 20, MaxSets = 1, GcMin = 20, GcMax = 80, AnnealTmMin = 35, AnnealTmMax = 85, InnerTmMin = 35, InnerTmMax = 85, IncludeLoops = false }; }
        private static BlastQuery QueryFor(BlastQuerySet set, BlastBinding binding)
        { foreach (BlastQuery query in set.Queries) if (query.Id == binding.QueryId) return query; throw new InvalidOperationException("Missing query"); }
        private static BlastQuerySet SyntheticLamp()
        {
            BlastQuerySet set = new BlastQuerySet { Mode = "LAMP", CandidateLabel = "LAMP synthetic #1" };
            BlastReaction reaction = new BlastReaction { Name = "LAMP 反应" }; string[] roles = { "F3", "F2", "F1c", "B1c", "B2", "B3" };
            bool[] direction = { false, false, true, false, true, true };
            for (int i = 0; i < 6; i++)
            {
                string id = "Q" + (i + 1).ToString("000", CultureInfo.InvariantCulture);
                set.Queries.Add(new BlastQuery { Id = id, Label = roles[i], Sequence = "ACGATCGATCGTAGCTAGCT" + "ACGTAC"[i] });
                reaction.Bindings.Add(new BlastBinding { QueryId = id, Role = roles[i], Reverse = direction[i] });
            }
            set.Reactions.Add(reaction); return set;
        }
        private static BlastResult Empty(BlastQuerySet set)
        {
            BlastResult result = new BlastResult { Rid = "SYNTHETIC_RID", Database = "synthetic_db", CompletedUtc = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc) };
            foreach (BlastQuery query in set.Queries) result.CompletedQueryIds.Add(query.Id); return result;
        }
        private static BlastHit Hit(BlastQuery query, int start, bool reverse, string accession)
        {
            int end = start + query.Sequence.Length - 1;
            return new BlastHit { QueryId = query.Id, Accession = accession, Title = "synthetic nonbiological fixture", QueryLength = query.Sequence.Length,
                QueryFrom = 1, QueryTo = query.Sequence.Length, SubjectFrom = reverse ? end : start, SubjectTo = reverse ? start : end,
                QueryAligned = query.Sequence, SubjectAligned = query.Sequence, AlignLength = query.Sequence.Length, Identities = query.Sequence.Length,
                Identity = 100, Coverage = 100, Reverse = reverse, ThreePrimeMismatches = 0, EValue = 0.001, BitScore = 30 };
        }
        private static void AddLocus(BlastQuerySet set, BlastResult result, bool reverse, int start, string accession)
        {
            BlastReaction reaction = set.Reactions[0];
            for (int i = 0; i < reaction.Bindings.Count; i++)
            {
                BlastBinding binding = reaction.Bindings[i]; int offset = reverse ? (reaction.Bindings.Count - 1 - i) * 40 : i * 40;
                result.Hits.Add(Hit(QueryFor(set, binding), start + offset, binding.Reverse ^ reverse, accession));
            }
        }
        private static List<int> CsvColumnCounts(string csv)
        {
            List<int> result = new List<int>(); bool quote = false; int count = 1;
            for (int i = 0; i < csv.Length; i++)
            {
                char value = csv[i];
                if (value == '"') { if (quote && i + 1 < csv.Length && csv[i + 1] == '"') i++; else quote = !quote; }
                else if (!quote && value == ',') count++;
                else if (!quote && value == '\n') { result.Add(count); count = 1; }
            }
            return result;
        }
        private static void ExpectArgument(Action action)
        { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected ArgumentException"); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
