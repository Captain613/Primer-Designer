using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Batch = LampBatchReview;

namespace RpaDesigner
{
    public static class BlastManualSelfTests
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string> { "BLAST manual workflow tests: synthetic DNA, no network requests." };
            int failed = 0, passed = 0;
            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)), "manual-fixtures"); Directory.CreateDirectory(folder);
            Action<string, Action> test = delegate(string name, Action body) { try { body(); passed++; lines.Add("PASS " + name); } catch (Exception ex) { failed++; lines.Add("FAIL " + name + ": " + ex); } };
            test("NCBI webpage bookmark preserves short-query defaults without submitting a query", delegate
            {
                var url = new Uri(BlastWebPreset.Create(BlastWebPreset.DefaultOrganism));
                Require(url.Scheme == "https" && url.Host == "blast.ncbi.nlm.nih.gov" && url.AbsolutePath == "/Blast.cgi", "Official HTTPS search form");
                var fields = url.Query.Substring(1).Split('&').ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Substring(p.IndexOf('=') + 1)));
                Require(fields["USER_FORMAT_DEFAULTS"] == "on" && fields["SET_SAVED_SEARCH"] == "true" && fields["PROG_DEFAULTS"] == "on", "Bookmark flags prevent webpage scripts resetting the database and masks");
                Require(fields["DATABASE"] == "refseq_genomes" && fields["EQ_MENU"] == BlastWebPreset.DefaultOrganism && fields["BLAST_PROGRAMS"] == "blastn", "Human genomic short-query search");
                Require(fields["WORD_SIZE"] == "7" && fields["EXPECT"] == "1000" && fields["MATCH_SCORES"] == "1,-3" && fields["GAPCOSTS"] == "5 2", "Consistent supported short-query scoring");
                Require(fields["MAX_NUM_SEQ"] == "1000" && fields["HSP_RANGE_MAX"] == "0" && fields["FILTER"] == "F" && fields["SHORT_QUERY_ADJUST"] == "on", "Retains broad short-query reporting");
                Require(!fields.ContainsKey("QUERY") && !fields.ContainsKey("CMD") && !fields.ContainsKey("SUBJECT") && !fields.ContainsKey("RID"), "Form navigation cannot submit local primer data or reuse a result");
            });
            test("Custom webpage organism is escaped and does not change search settings", delegate
            {
                const string organism = "Mus musculus (taxid:10090)&FILTER=L#QUERY=PRIVATE";
                var url = new Uri(BlastWebPreset.Create(" " + organism + " "));
                var fields = url.Query.Substring(1).Split('&').ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Substring(p.IndexOf('=') + 1)));
                Require(fields["EQ_MENU"] == organism && fields["FILTER"] == "F" && url.Fragment == "" && !fields.ContainsKey("QUERY"), "Organism cannot inject another parameter or fragment");
                Require(!new Uri(BlastWebPreset.Create("")).Query.Contains("EQ_MENU"), "Empty organism leaves selection to the webpage");
                bool rejected = false; try { BlastWebPreset.Create("Homo\nsapiens"); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Multiline organism rejected");
            });
            test("Bookmark carries complete formatter defaults so submission cannot fall back to text", delegate
            {
                var url = new Uri(BlastWebPreset.Create(BlastWebPreset.DefaultOrganism));
                var fields = url.Query.Substring(1).Split('&').ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Substring(p.IndexOf('=') + 1)));
                Require(fields["FORMAT_TYPE"] == "HTML" && fields["FORMAT_OBJECT"] == "Alignment" && fields["NEW_VIEW"] == "true" && fields["ALIGNMENT_VIEW"] == "Pairwise", "Explicit valid output type and alignment view");
                foreach (string name in new[] { "SHOW_OVERVIEW", "SHOW_LINKOUT", "GET_SEQUENCE" }) Require(fields[name] == "true", "Enabled result view " + name);
                foreach (string name in new[] { "NCBI_GI", "SHOW_CDS_FEATURE" }) Require(fields[name] == "false", "Explicit optional result view " + name);
                foreach (string name in new[] { "DESCRIPTIONS", "ALIGNMENTS", "NUM_OVERVIEW" }) Require(fields[name] == fields["MAX_NUM_SEQ"], "Formatting does not restrict target records: " + name);
                foreach (string name in new[] { "MASK_CHAR", "MASK_COLOR", "LINE_LENGTH" }) Require(!String.IsNullOrEmpty(fields[name]), "Formatter has no empty reset default: " + name);
            });
            test("RPA and SNP RPA export/import preserve real independent reactions", delegate
            {
                var pair = new PrimerPair { Rank = 2, Forward = DnaPrimer("ACGTAGTCGATCGTACGTCA"), Reverse = DnaPrimer("TGCATCGATGCTAGCTACGA") };
                var simple = ReadTemplate(folder, "rpa", BlastQueryBuilder.ForRpa(pair));
                Require(Batch.Engine.BuildProfiles(simple).Single().Primers.Select(p => p.Role).SequenceEqual(new[] { "F", "R" }), "F/R order");
                var snp = new SnpPrimerSet { Rank = 3, ReferenceForward = pair.Forward, AlternateForward = DnaPrimer("ACGTAGTCGATCGTACGTCC"), CommonReverse = pair.Reverse, ControlForward = DnaPrimer("GTCACTAGCTAGTCGATGCA") };
                var snpPrimers = ReadTemplate(folder, "snp", BlastQueryBuilder.ForSnp(snp));
                var profiles = Batch.Engine.BuildProfiles(snpPrimers);
                Require(snpPrimers.Count == 4 && profiles.Count == 3, "Three RPA reactions, four records");
                Require(profiles.Single(p => p.Name == "alt").Primers[0].Dna == snp.AlternateForward.Sequence, "Alternate sequence preserved");
                Require(profiles.Single(p => p.Name == "control").Primers[0].Dna == snp.ControlForward.Sequence, "Control remains separate");
            });
            test("LAMP export has named six-region queries and preserves artificial mismatch", delegate
            {
                LampPrimerSet lamp = Fixture(false, false); lamp.Rank = 7;
                var query = BlastQueryBuilder.ForLamp(lamp, "mLAMP"); string text = BlastManualTemplate.Create(query);
                var primers = ReadTemplate(folder, "mlamp", query); var profiles = Batch.Engine.BuildProfiles(primers);
                Require(primers.Count == 7 && profiles.Count == 2 && text.Contains(">mLAMP_007_F2_ref reactions=ref"), "Readable, reusable headers");
                Require(!primers.Any(p => p.Dna == lamp.FIP.Sequence || p.Dna == lamp.BIP.Sequence), "Inner primers split");
                Require(profiles.Single(p => p.Name == "ref").Primers[1].Dna == lamp.FIP.Regions[1].Sequence, "Modified reference F2 preserved");
                Require(profiles.Single(p => p.Name == "alt").Primers[1].Dna == lamp.AlternateInner.Regions[1].Sequence, "Modified alternate F2 preserved");
                Require(!text.Contains("PRIVATE_TEMPLATE"), "Original template metadata excluded");
            });
            test("BIP variants retain correlated B1c/B2 pairs without mixing alleles", delegate
            {
                var primers = ReadTemplate(folder, "bip", BlastQueryBuilder.ForLamp(Fixture(true, false), "AS-LAMP"));
                var profiles = Batch.Engine.BuildProfiles(primers);
                Require(profiles.Count == 2, "Exactly actual reactions");
                Require(profiles[0].Primers[3].Dna != profiles[1].Primers[3].Dna && profiles[0].Primers[4].Dna != profiles[1].Primers[4].Dna, "Both variant components preserved");
                foreach (Batch.Primer p in primers) p.Reactions = null;
                bool rejected = false; try { Batch.Engine.BuildProfiles(primers); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "Ambiguous multi-region combinations rejected");
            });
            test("PA-LAMP active regions and optional loops are explicitly scoped", delegate
            {
                LampPrimerSet set = Fixture(true, true); set.LF = new LampOligo { Sequence = set.F3.Sequence }; set.LB = new LampOligo { Sequence = "CATGACGATCTAGCTAGTCA" };
                var primers = ReadTemplate(folder, "pa", BlastQueryBuilder.ForLamp(set, "PA-LAMP"));
                var profiles = Batch.Engine.BuildProfiles(primers);
                Require(profiles.Count == 2 && profiles.All(p => p.Primers.Length == 6), "Loops do not change core requirements");
                Require(primers.Any(p => p.Role == "LF") && primers.Any(p => p.Role == "LB"), "Loops preserved even when DNA equals core");
                Require(profiles.All(p => p.Primers[4].Dna == set.BIP.ActivatedRegions[1].Sequence), "Uses active B2");
                Require(primers.All(p => p.Dna.All(c => "ACGT".Contains(c))), "Only DNA bases exported");
            });
            test("XML/XML2 use complete query validation and versioned reference IDs", delegate
            {
                foreach (bool xml2 in new[] { true, false })
                {
                    var settings = SyntheticFixture(folder, xml2 ? "xml2" : "xml1", xml2, false);
                    var parsed = Batch.Engine.Read(settings);
                    Require(parsed.Windows.Count == 1 && parsed.Seeds.Values.SelectMany(x => x).All(x => x.Accession == "NC_SYNTH.1"), "Versioned candidate layout");
                    var doc = XDocument.Load(settings.Xml); doc.Descendants().First(x => x.Name.LocalName == (xml2 ? "qseq" : "Hsp_qseq")).Value = "AAAAAAAAAAAAAAAAAAAA"; doc.Save(settings.Xml);
                    bool rejected = false; try { Batch.Engine.Read(settings); } catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "Different FASTA and XML cannot be assessed");
                }
            });
            test("Full reference extension restores truncated F2 termini on both strands", delegate
            {
                foreach (bool reverse in new[] { false, true })
                {
                    var settings = SyntheticFixture(folder, reverse ? "reverse" : "forward", true, reverse);
                    var result = Batch.Engine.Run(settings, delegate { }, CancellationToken.None);
                    Require(result.ReviewedWindows == 1 && result.FailedWindows == 0 && result.Combinations.Count(c => c.Expected) == 2, "Two expected reactions");
                    var reference = result.Combinations.Single(c => c.Reaction == "ref"); var alternate = result.Combinations.Single(c => c.Reaction == "alt");
                    Require(reference.Matches[1].Mismatches.SequenceEqual(new[] { 17 }), "Artificial mismatch position retained");
                    Require(alternate.Matches[1].Mismatches.SequenceEqual(new[] { 17, 20 }), "Terminal allele mismatch retained");
                    Require(reference.Reverse == reverse, "Whole-layout orientation");
                    string html = File.ReadAllText(Batch.Engine.WriteReport(settings, result));
                    Require(html.Contains("不能判定实际扩增") && html.Contains("未评估完整引物") && html.Contains("无插入缺失模型"), "Report states calculation scope");
                }
            });
            test("Missing records and uncached reference remain incomplete, never zero-evidence pass", delegate
            {
                var settings = SyntheticFixture(folder, "incomplete", true, false);
                var document = XDocument.Load(settings.Xml); document.Descendants("Search").First().Remove(); document.Save(settings.Xml);
                Require(Batch.Engine.Read(settings).MissingQueries, "Missing query detected");
                settings = SyntheticFixture(folder, "uncached", true, false); var candidate = Batch.Engine.Read(settings).Windows.Single(); File.Delete(Batch.Engine.CachePath(settings, candidate));
                var result = Batch.Engine.Run(settings, delegate { }, CancellationToken.None);
                Require(result.FailedWindows == 1 && result.ReviewedWindows == 0 && result.Combinations.Count == 0, "Offline missing cache is a failed region");
                Require(Batch.Engine.TextReport(settings, result).Contains("未完成 1 个"), "Failure visible in text report");
            });
            test("RPA geometry, selected B2 terminal coordinate, N bases and cancellation", delegate
            {
                var f = new Batch.Primer { Id = "X_F", Role = "F", Group = "X", Dna = "ACGTAGTCGATCGTACGTCA" };
                var r = new Batch.Primer { Id = "X_R", Role = "R", Group = "X", Dna = "TGCATCGATGCTAGCTACGA" };
                var hits = new Dictionary<string, List<Batch.Match>> { { f.Id, Batch.Engine.FindMatches(f, f.Dna, 100, false, 0) }, { r.Id, Batch.Engine.FindMatches(r, Batch.Engine.ReverseComplement(r.Dna), 200, false, 0) } };
                bool truncated = false;
                var paired = Batch.Engine.Join(new Batch.Window { Group = "X", Accession = "NC_SYNTH.1" }, new[] { f, r }, hits, new Batch.Settings { ExpectedAccession = "NC_SYNTH.1", ExpectedRole = "R", ExpectedSite = 200, MaxSpan = 300 }, ref truncated);
                Require(paired.Count == 1 && paired[0].Expected, "Selected reverse-primer 3prime coordinate");
                hits[r.Id][0].Start = 110; hits[r.Id][0].End = 129;
                Require(Batch.Engine.Join(new Batch.Window { Group = "X" }, new[] { f, r }, hits, new Batch.Settings(), ref truncated).Count == 0, "Overlapping primers rejected");
                Require(Batch.Engine.FindMatches(f, "ACGTAGTCGATCGTACGTNA", 1, false, 4).Count == 0, "N sites not assigned deterministic matches");
                var settings = SyntheticFixture(folder, "cancel", true, false); bool cancelled = false;
                try { Batch.Engine.Run(settings, delegate { }, new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
                Require(cancelled, "Cancellation honored");
                var b2 = new Batch.Primer { Id = "Y_B2", Group = "Y", Role = "B2", Dna = r.Dna };
                hits.Remove(r.Id); hits[b2.Id] = Batch.Engine.FindMatches(b2, Batch.Engine.ReverseComplement(b2.Dna), 200, false, 0);
                paired = Batch.Engine.Join(new Batch.Window { Group = "X", Accession = "NC_SYNTH.1" }, new[] { f, b2 }, hits, new Batch.Settings { ExpectedAccession = "NC_SYNTH.1", ExpectedRole = "B2", ExpectedSite = 200 }, ref truncated);
                Require(paired.Single().Expected, "Target classification is not hard-coded to F2");
            });
            test("Imported reference identifiers cannot escape the cache directory", delegate
            {
                foreach (string accession in new[] { "../outside", "C:\\outside", "NC_TEST.1/child" })
                {
                    bool rejected = false;
                    try { Batch.Engine.CachePath(new Batch.Settings { Output = folder }, new Batch.Window { Accession = accession, Start = 1, End = 100 }); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "Invalid reference identifier rejected before file access");
                }
            });
            lines.Add("Passed=" + passed + "; Failed=" + failed); lines.Add(failed == 0 ? "RESULT: PASS" : "RESULT: FAIL"); File.WriteAllLines(reportPath, lines, new UTF8Encoding(true)); return failed == 0 ? 0 : 1;
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static Primer DnaPrimer(string dna) { return new Primer { Sequence = dna }; }
        static LampRegion Region(string role, string dna, bool reverse) { return new LampRegion { Name = role, Sequence = dna, Reverse = reverse }; }
        static LampOligo Oligo(params LampRegion[] regions) { var p = new LampOligo { Sequence = String.Concat(regions.Select(r => r.Sequence)) }; p.Regions.AddRange(regions); return p; }
        static LampPrimerSet Fixture(bool bipVariant, bool pa)
        {
            var f1 = Region("F1c", "GTCAGCTAGCTACGATCGTAA", true); var f2 = Region("F2", "ACGTAGTCGATCGTACGTCA", false);
            var b1 = Region("B1c", "AGCATGCTAGTCGACTAGCAA", false); var b2 = Region("B2", "TGCATCGATGCTAGCTACGA", true);
            var set = new LampPrimerSet { Rank = 1, SpecificInner = bipVariant ? "BIP" : "FIP", F3 = Oligo(Region("F3", "GTCACTAGCTAGTCGATGCA", false)), B3 = Oligo(Region("B3", "CGATGCATGACTGCATAGCT", true)), FIP = Oligo(f1, f2), BIP = Oligo(b1, b2) };
            set.AlternateInner = bipVariant ? Oligo(Region("B1c", "AGCATGCTAGTCGACTAGCAC", false), Region("B2", "TGCATCGATGCTAGCTACGC", true)) : Oligo(f1, Region("F2", "ACGTAGTCGATCGTACGTCC", false));
            if (pa) foreach (LampOligo p in new[] { set.BIP, set.AlternateInner }) { p.RnaIndex = p.Sequence.Length; p.ActivatedSequence = set.BIP.Sequence; p.ActivatedRegions.AddRange(new[] { b1, b2 }); }
            return set;
        }
        static List<Batch.Primer> ReadTemplate(string folder, string name, BlastQuerySet query)
        { string path = Path.Combine(folder, name + ".fasta"); File.WriteAllText(path, BlastManualTemplate.Create(query)); return Batch.Engine.ReadFasta(path); }
        public static Batch.Settings SyntheticFixture(string folder, string name, bool xml2, bool reverse)
        {
            Directory.CreateDirectory(folder); LampPrimerSet lamp = Fixture(false, false);
            string modified = "ACGTAGTCGATCGTACTTCA";
            lamp.FIP.Regions[1].Sequence = modified; lamp.FIP.Sequence = lamp.FIP.Regions[0].Sequence + modified;
            lamp.AlternateInner.Regions[1].Sequence = modified.Substring(0, 19) + "C"; lamp.AlternateInner.Sequence = lamp.AlternateInner.Regions[0].Sequence + lamp.AlternateInner.Regions[1].Sequence;
            string fasta = Path.Combine(folder, name + ".fasta"), xml = Path.Combine(folder, name + ".xml");
            File.WriteAllText(fasta, BlastManualTemplate.Create(BlastQueryBuilder.ForLamp(lamp, "mLAMP")));
            var primers = Batch.Engine.ReadFasta(fasta); var profiles = Batch.Engine.BuildProfiles(primers); char[] reference = new string('A', 1000).ToCharArray();
            var starts = new Dictionary<string, int>();
            foreach (Batch.Primer p in profiles.First().Primers)
            {
                int index = Array.IndexOf(Batch.Engine.Roles, p.Role); int start = reverse ? 1000 - index * 50 : 100 + index * 50;
                string dna = p.Role == "F2" ? "ACGTAGTCGATCGTACGTCA" : p.Dna;
                if (p.Reverse != reverse) dna = Batch.Engine.ReverseComplement(dna);
                int windowStart = reverse ? profiles.First().Primers[0].Dna.Length : 100;
                Array.Copy(dna.ToCharArray(), 0, reference, start - windowStart, dna.Length); starts[p.Role] = start;
            }
            var root = new XElement(xml2 ? "BlastXML2" : "BlastOutput");
            foreach (Batch.Primer p in primers)
            {
                int start = starts[p.Role], length = p.Dna.Length, qto = p.Role == "F2" ? 16 : length;
                bool strand = p.Reverse != reverse;
                var q = new XElement(xml2 ? "Search" : "Iteration", new XElement(xml2 ? "query-title" : "Iteration_query-def", p.Id), new XElement(xml2 ? "query-len" : "Iteration_query-len", length));
                var hit = new XElement("Hit", new XElement(xml2 ? "len" : "Hit_len", 2000)); XElement descr = xml2 ? new XElement("HitDescr") : hit;
                if (xml2) hit.Add(descr); descr.Add(new XElement(xml2 ? "accession" : "Hit_accession", "NC_SYNTH"), new XElement(xml2 ? "id" : "Hit_id", "ref|NC_SYNTH.1|"));
                string prefix = xml2 ? "" : "Hsp_";
                hit.Add(new XElement("Hsp", new XElement(prefix + "query-from", 1), new XElement(prefix + "query-to", qto), new XElement(prefix + "hit-from", strand ? start + length - 1 : start), new XElement(prefix + "hit-to", strand ? start + length - qto : start + qto - 1), new XElement(prefix + "qseq", p.Dna.Substring(0, qto)), new XElement(prefix + "hseq", p.Dna.Substring(0, qto))));
                q.Add(hit); root.Add(q);
            }
            new XDocument(root).Save(xml);
            var settings = new Batch.Settings { Fasta = fasta, Xml = xml, Network = false, ExpectedAccession = "NC_SYNTH.1", ExpectedRole = "F2", ExpectedSite = reverse ? starts["F2"] : starts["F2"] + 19, Output = Path.Combine(folder, name + "-result"), MaxMismatches = 4 };
            Batch.Window window = Batch.Engine.Read(settings).Windows.Single(); string cache = Batch.Engine.CachePath(settings, window); Directory.CreateDirectory(Path.GetDirectoryName(cache));
            File.WriteAllText(cache, ">NC_SYNTH.1:" + window.Start + "-" + window.End + "\n" + new string(reference)); return settings;
        }
    }
}
