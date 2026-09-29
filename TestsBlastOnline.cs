using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;

namespace RpaDesigner
{
    public static class BlastOnlineSelfTests
    {
        private const string Dna = "ACGTTGCAACGTTGCAACGT";
        public static int Run(string reportPath)
        {
            var report = new List<string>(); int passed = 0, failed = 0;
            report.Add("NCBI BLAST transport and XML2 offline fixture tests; no remote request is made.");
            report.Add("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Action<string, Action> test = delegate(string name, Action body)
            {
                try { body(); passed++; report.Add("PASS  " + name); }
                catch (Exception ex) { failed++; report.Add("FAIL  " + name + ": " + ex); }
            };
            test("Only anonymous IDs and binding sequences enter FASTA/form; fixed short-query options", delegate
            {
                BlastQuerySet queries = Queries(2); queries.Queries[0].Label = "patient/sample/private.fasta";
                queries.CandidateLabel = "private candidate";
                BlastSettings settings = Settings(); settings.Email = "research+test@example.org"; settings.ExpectedAccessions = "PRIVATE_EXPECTED_ACCESSION";
                string fasta = BlastOnline.BuildFasta(queries);
                Require(fasta == ">Q001\n" + Dna + "\n>Q002\n" + Dna + "\n", "FASTA is not anonymous or exact.");
                // Check actual encoded values, not substring matching decoded labels.
                string body = BlastOnline.BuildSubmission(queries, settings);
                Dictionary<string, string> form = DecodeForm(body);
                Require(form["QUERY"] == fasta && form["email"] == settings.Email && form["tool"] == "PrimerDesigner", "Form encoding/contact fields changed.");
                Require(body.Contains("%2B") && !body.Contains("patient") && !body.Contains("private") && !body.Contains("PRIVATE_EXPECTED_ACCESSION"), "Metadata leaked into request.");
                Require(form["PROGRAM"] == "blastn" && form["WORD_SIZE"] == "7" && form["EXPECT"] == "1000" && form["NUCL_REWARD"] == "1" && form["NUCL_PENALTY"] == "-3" && form["GAPCOSTS"] == "5 2", "Short-query scoring parameters differ.");
                Require(form["FILTER"] == "F" && form["SHORT_QUERY_ADJUST"] == "false" && form["FORMAT_TYPE"] == "XML2_S" && form["HITLIST_SIZE"] == "100", "Result/filter settings differ.");
                Require(!form.ContainsKey("ENTREZ_QUERY") && !form.ContainsKey("MEGABLAST"), "Undocumented filters or megablast were enabled.");
            });
            test("Input and settings validation reject unsafe IDs, RNA, short/oversized batches and invalid settings", delegate
            {
                ExpectArgument(delegate { BlastOnline.Validate(null, Settings()); });
                BlastQuerySet queries = Queries(1); queries.Queries[0].Sequence = "ACGTAC";
                ExpectArgument(delegate { BlastOnline.Validate(queries, Settings()); });
                queries.Queries[0].Sequence = "ACGTUACGT"; ExpectArgument(delegate { BlastOnline.Validate(queries, Settings()); });
                queries = Queries(1); queries.Queries[0].Sequence = new string('A', 1001); ExpectArgument(delegate { BlastOnline.Validate(queries, Settings()); });
                queries.Queries[0].Sequence = new string('A', 1000); BlastOnline.Validate(queries, Settings());
                queries = Queries(2); queries.Queries[1].Id = "Q001"; ExpectArgument(delegate { BlastOnline.Validate(queries, Settings()); });
                queries = Queries(1); queries.Queries[0].Id = "patient\nACGT"; ExpectArgument(delegate { BlastOnline.Validate(queries, Settings()); });
                queries = Queries(1); BlastSettings settings = Settings(); settings.Email = ""; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
                settings = Settings(); settings.Email = "Name <name@example.org>"; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
                settings = Settings(); settings.Database = "refseq_reference_genomes"; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
                settings = Settings(); settings.HitListSize = 501; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
                settings = Settings(); settings.MinCoverage = Double.NaN; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
                settings = Settings(); settings.MinIdentity = Double.PositiveInfinity; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
                settings = Settings(); settings.MaxLocusSpan = 0; ExpectArgument(delegate { BlastOnline.Validate(queries, settings); });
            });
            test("XML2 namespace/type wrappers preserve all query reports, all HSPs and subject aliases", delegate
            {
                string hit = Hit("NC_TEST.1", Hsp(Dna, Dna, 1, 20, 101, 120) + Hsp(Dna, Dna, 1, 20, 520, 501), true);
                string xml = Xml(Search("Q001", hit) + Search("Q002", ""));
                BlastResult result = BlastOnline.ParseXml(xml, Queries(2));
                Require(result.CompletedQueryIds.Count == 2 && result.Hits.Count == 4, "Some query/HSP/aliases were lost.");
                Require(result.Database == "refseq_representative_genomes" && result.RawXml == xml && result.CompletedUtc != default(DateTime), "Report provenance missing.");
                Require(result.Hits[0].Accession == "NC_TEST.1" && result.Hits[1].Accession == "NC_ALIAS.1", "Aliases not retained.");
                Require(!result.Hits[0].Reverse && result.Hits[2].Reverse && result.Hits[2].SubjectFrom == 520 && result.Hits[2].SubjectTo == 501, "Subject orientation is wrong.");
                Require(result.Hits[0].Coverage == 100 && result.Hits[0].Identity == 100 && result.Hits[0].ThreePrimeMismatches == 0, "Perfect hit metrics differ.");
                Require(result.Notes.Count == 1 && result.Notes[0].Contains("不能证明特异性"), "No-hit query was not qualified.");
            });
            test("Partial coverage does not pretend to observe an absent 3-prime end", delegate
            {
                string first = Hsp(Dna.Substring(0, 15), Dna.Substring(0, 15), 1, 15, 1, 15);
                string last = Hsp(Dna.Substring(10), Dna.Substring(10), 11, 20, 11, 20);
                BlastResult result = BlastOnline.ParseXml(Xml(Search("Q001", Hit("X.1", first + last, false))), Queries(1));
                Require(result.Hits[0].Coverage == 75 && result.Hits[0].ThreePrimeMismatches == -1, "Uncovered 3-prime bases counted as matches.");
                Require(result.Hits[1].Coverage == 50 && result.Hits[1].ThreePrimeMismatches == 0, "Observed terminal five-base interval was lost.");
            });
            test("Accession version is recovered only from an exactly matching sequence ID", delegate
            {
                string xml = Xml(Search("Q001", Hit("NC_TEST", Hsp(Dna, Dna, 1, 20, 1, 20), false)));
                string same = xml.Replace("<id>ref|NC_TEST|</id>", "<id>gi|123|ref|NC_TEST.11|</id>");
                Require(BlastOnline.ParseXml(same, Queries(1)).Hits[0].Accession == "NC_TEST.11", "Exact accession version was lost.");
                string different = xml.Replace("<id>ref|NC_TEST|</id>", "<id>ref|NC_OTHER.11|</id>").Replace("Synthetic test sequence", "NC_TEST.22 mentioned only in title");
                Require(BlastOnline.ParseXml(different, Queries(1)).Hits[0].Accession == "NC_TEST", "Unrelated ID or title supplied a false version.");
                string conflicting = xml.Replace("<id>ref|NC_TEST|</id>", "<id>ref|NC_TEST.11|ref|NC_TEST.12|</id>");
                Require(BlastOnline.ParseXml(conflicting, Queries(1)).Hits[0].Accession == "NC_TEST", "Conflicting versions were guessed.");
                string alreadyVersioned = Xml(Search("Q001", Hit("NC_TEST.10", Hsp(Dna, Dna, 1, 20, 1, 20), false))).Replace("<id>ref|NC_TEST.10|</id>", "<id>ref|NC_TEST.11|</id>");
                Require(BlastOnline.ParseXml(alreadyVersioned, Queries(1)).Hits[0].Accession == "NC_TEST.10", "Explicit accession version was overwritten.");
            });
            test("Terminal mismatches and insertions/deletions are counted in canonical query coordinates", delegate
            {
                string subject = Dna.Substring(0, 17) + "-" + (Dna[18] == 'A' ? "C" : "A") + Dna.Substring(19);
                string gappedQuery = Dna.Substring(0, 17) + "-" + Dna.Substring(17);
                string insertedSubject = Dna.Substring(0, 17) + "A" + Dna.Substring(17);
                string both = Hsp(Dna, subject, 1, 20, 101, 119) + Hsp(gappedQuery, insertedSubject, 1, 20, 201, 221);
                BlastResult result = BlastOnline.ParseXml(Xml(Search("Q001", Hit("X.1", both, false))), Queries(1));
                Require(result.Hits[0].ThreePrimeMismatches == 2 && result.Hits[0].Gaps == 1 && result.Hits[0].Identity == 90, "Terminal deletion/mismatch metrics wrong.");
                Require(result.Hits[1].ThreePrimeMismatches == 1 && result.Hits[1].Coverage == 100 && Math.Abs(result.Hits[1].Identity - 2000.0 / 21) < 0.00001, "Insertion metrics wrong.");
            });
            test("Reverse-query HSPs normalize alignments, coordinates and three-prime metrics", delegate
            {
                string reverse = ReverseComplement(Dna), subject = "T" + reverse.Substring(1);
                if (subject[0] == reverse[0]) subject = "A" + reverse.Substring(1);
                string hsp = Hsp(reverse, subject, 20, 1, 101, 120);
                BlastResult result = BlastOnline.ParseXml(Xml(Search("Q001", Hit("X.1", hsp, false))), Queries(1));
                BlastHit hit = result.Hits[0];
                Require(hit.QueryFrom == 1 && hit.QueryTo == 20 && hit.QueryAligned == Dna && hit.Reverse && hit.SubjectFrom == 120 && hit.SubjectTo == 101, "Minus-query orientation not canonical.");
                Require(hit.ThreePrimeMismatches == 1, "Minus-query original first mismatch should map to 3-prime tail.");
            });
            test("Terminal IUPAC ambiguity is unknown rather than a proven mismatch", delegate
            {
                string terminalN = Dna.Substring(0, 19) + "N";
                string terminalR = Dna.Substring(0, 17) + "R" + Dna.Substring(18);
                string upstreamN = Dna.Substring(0, 10) + "N" + Dna.Substring(11);
                string hsps = Hsp(Dna, terminalN, 1, 20, 1, 20) + Hsp(Dna, terminalR, 1, 20, 31, 50) + Hsp(Dna, upstreamN, 1, 20, 61, 80);
                BlastResult result = BlastOnline.ParseXml(Xml(Search("Q001", Hit("X.1", hsps, false))), Queries(1));
                Require(result.Hits[0].ThreePrimeMismatches == -1, "Terminal N was counted as a definite mismatch.");
                Require(result.Hits[1].ThreePrimeMismatches == -1, "Terminal R was counted as a definite mismatch.");
                Require(result.Hits[2].ThreePrimeMismatches == 0, "Non-terminal ambiguity hid an observed perfect terminal interval.");
            });
            test("Single-output and schema_alt XML reports, BOM and exponent values are accepted", delegate
            {
                string xml = Xml(Search("Q001", Hit("X.1", Hsp(Dna, Dna, 1, 20, 1, 20), false)));
                foreach (string type in new string[] { "Report", "Target", "Results", "Search", "Statistics" })
                    xml = xml.Replace("<" + type + ">", "").Replace("</" + type + ">", "");
                xml = xml.Replace("<BlastXML2 xmlns=\"http://www.ncbi.nlm.nih.gov\">", "").Replace("</BlastXML2>", "");
                BlastResult result = BlastOnline.ParseXml("\uFEFF" + xml, Queries(1));
                Require(result.Hits.Count == 1 && result.Hits[0].EValue == 1e-8, "Single schema_alt report failed.");
            });
            test("No-hit reports are complete searches, never positive specificity evidence", delegate
            {
                BlastResult result = BlastOnline.ParseXml(Xml(Search("Q001", "")), Queries(1));
                Require(result.Hits.Count == 0 && result.CompletedQueryIds.Count == 1 && result.Notes.Count == 1, "Valid no-hit report failed.");
                string missingStats = Xml(Search("Q001", "")).Replace(Stats(), "");
                ExpectInvalid(delegate { BlastOnline.ParseXml(missingStats, Queries(1)); });
            });
            test("Hit-limit warning counts subject hits instead of HSPs or alternate descriptions", delegate
            {
                string xml = Xml(Search("Q001", Hit("X.1", Hsp(Dna, Dna, 1, 20, 1, 20) + Hsp(Dna, Dna, 1, 20, 31, 50), true)));
                BlastResult below = BlastOnline.ParseXmlCore(xml, Queries(1), 2);
                Require(below.Notes.Count == 0, "Aliases/HSPs falsely counted toward hit limit.");
                BlastResult capped = BlastOnline.ParseXmlCore(xml, Queries(1), 1);
                Require(capped.Notes.Count == 1 && capped.Notes[0].Contains("截断"), "Hit-limit truncation not reported.");
            });
            test("HTML, malformed XML, server errors, incomplete/duplicate/unknown queries fail closed", delegate
            {
                ExpectInvalid(delegate { BlastOnline.ParseXml("<html><body>No hits found</body></html>", Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml("<BlastXML2>", Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml("<BlastXML2/>", Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml("<BlastOutput2><error><Err><code>1</code></Err></error></BlastOutput2>", Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml(Xml(Search("Q001", "")), Queries(2)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml(Xml(Search("Q001", "") + Search("Q001", "")), Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml(Xml(Search("Q002", "")), Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml(Xml(Search("Q001", "")).Replace("<query-len>20</query-len>", "<query-len>19</query-len>"), Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml(Xml(Search("Q001", "")).Replace("<hits>", "<message>Query error</message><hits>"), Queries(1)); });
            });
            test("Unsafe XML entities and external include are never resolved", delegate
            {
                ExpectInvalid(delegate { BlastOnline.ParseXml("<!DOCTYPE x [<!ENTITY data SYSTEM 'file:///C:/Windows/win.ini'>]><BlastXML2>&data;</BlastXML2>", Queries(1)); });
                ExpectInvalid(delegate { BlastOnline.ParseXml("<BlastXML2 xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='https://example.invalid/query.xml'/></BlastXML2>", Queries(1)); });
            });
            test("Corrupt alignment metrics and conflicting scalar fields are rejected", delegate
            {
                string xml = Xml(Search("Q001", Hit("X.1", Hsp(Dna, Dna, 1, 20, 1, 20), false)));
                foreach (string changed in new string[] {
                    xml.Replace("<identity>20</identity>", "<identity>19</identity>"),
                    xml.Replace("<query-to>20</query-to>", "<query-to>19</query-to>"),
                    xml.Replace("<hit-to>20</hit-to>", "<hit-to>9999</hit-to>"),
                    xml.Replace("<evalue>1e-8</evalue>", "<evalue>NaN</evalue>"),
                    xml.Replace("<query-len>20</query-len>", "<query-len>20</query-len><query-len>20</query-len>"),
                    xml.Replace("<qseq>" + Dna + "</qseq>", "<qseq>" + new string('A', 20) + "</qseq>") })
                    ExpectInvalid(delegate { BlastOnline.ParseXml(changed, Queries(1)); });
            });
            test("WAITING, FAILED, UNKNOWN and READY status pages remain distinguishable", delegate
            {
                foreach (string state in new string[] { "WAITING", "FAILED", "UNKNOWN", "READY" })
                    Require(BlastOnline.ResponseState("<html>\nStatus=" + state + "\n</html>") == state, "Lost status: " + state);
                Require(BlastOnline.ResponseState("<?xml version='1.0'?><BlastXML2><title>Status=FAILED</title></BlastXML2>") == "", "XML content was misclassified as a status page.");
                Require(BlastOnline.ResponseState("<html>unexpected service error</html>") == "", "Unexpected response silently became no hits.");
            });
            test("First BLAST check uses RTOE with a ten-second floor and safe fallback", delegate
            {
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = 5\n") == 10000, "A short RTOE should permit the first Get after ten seconds.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = 0\n") == 10000, "Zero RTOE bypassed the request rate floor.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\r\nRTOE = 10\r\n") == 10000, "Ten-second boundary changed.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = 42\n") == 42000, "A valid RTOE was ignored.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = 1800\n") == 1800000, "Thirty-minute boundary changed.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = 99999999999999999\n") == 1800000, "Large valid RTOE was not clamped before conversion.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\n") == 60000, "Missing RTOE should use the conservative fallback.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = -1\n") == 60000, "Negative RTOE was accepted.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = 12.5\n") == 60000, "Malformed decimal RTOE was partially parsed.");
                Require(BlastOnline.FirstCheckDelayMs("RID = ABCDE123\nRTOE = invalid\n") == 60000, "Invalid RTOE should use the conservative fallback.");
                Require(BlastOnline.RequestIntervalMs == 10000 && BlastOnline.PollIntervalMs == 60000,
                    "The global request or subsequent same-RID polling interval changed.");
            });
            test("A pre-cancelled Run performs no upload, and polling waits are cancellable", delegate
            {
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel(); bool caught = false;
                    try { BlastOnline.Run(Queries(1), Settings(), null, cancelled.Token); }
                    catch (OperationCanceledException) { caught = true; }
                    Require(caught, "Pre-cancelled request did not cancel.");
                }
                using (var cancellation = new CancellationTokenSource())
                using (var timer = new Timer(delegate { cancellation.Cancel(); }, null, 80, Timeout.Infinite))
                {
                    MethodInfo wait = typeof(BlastOnline).GetMethod("Wait", BindingFlags.NonPublic | BindingFlags.Static);
                    var clock = Stopwatch.StartNew(); bool caught = false;
                    try { wait.Invoke(null, new object[] { (long)60000, clock, cancellation.Token }); }
                    catch (TargetInvocationException ex) { if (ex.InnerException is OperationCanceledException) caught = true; else throw; }
                    Require(caught && clock.ElapsedMilliseconds < 2000, "A polling delay did not cancel promptly.");
                }
            });
            report.Add("RESULT: " + passed + " passed, " + failed + " failed.");
            File.WriteAllLines(reportPath, report.ToArray(), new UTF8Encoding(true));
            return failed == 0 ? 0 : 1;
        }

        private static BlastSettings Settings() { return new BlastSettings { Database = "refseq_representative_genomes", Email = "test@example.org" }; }
        private static BlastQuerySet Queries(int count)
        {
            var queries = new BlastQuerySet();
            for (int i = 1; i <= count; i++) queries.Queries.Add(new BlastQuery { Id = "Q" + i.ToString("000"), Label = "local label " + i, Sequence = Dna });
            return queries;
        }
        private static string Xml(string output) { return "<BlastXML2 xmlns=\"http://www.ncbi.nlm.nih.gov\">" + output + "</BlastXML2>"; }
        private static string Stats() { return "<stat><Statistics><hsp-len>5</hsp-len><eff-space>9.1e10</eff-space></Statistics></stat>"; }
        private static string Search(string id, string hits)
        {
            return "<BlastOutput2><report><Report><program>blastn</program><search-target><Target><db>refseq_representative_genomes</db></Target></search-target><results><Results><search><Search>" +
                "<query-id>Query_1</query-id><query-title>" + id + "</query-title><query-len>20</query-len><hits>" + hits + "</hits>" + Stats() +
                "</Search></search></Results></results></Report></report></BlastOutput2>";
        }
        private static string Hit(string accession, string hsps, bool alias)
        {
            return "<Hit><description><HitDescr><id>ref|" + accession + "|</id><accession>" + accession + "</accession><title>Synthetic test sequence</title></HitDescr>" +
                (alias ? "<HitDescr><id>ref|NC_ALIAS.1|</id><accession>NC_ALIAS.1</accession><title>Another record</title></HitDescr>" : "") +
                "</description><len>1000</len><hsps>" + hsps + "</hsps></Hit>";
        }
        private static string Hsp(string query, string subject, int qFrom, int qTo, int sFrom, int sTo)
        {
            int identities = 0, gaps = 0;
            for (int i = 0; i < query.Length; i++)
            {
                if (query[i] == '-' || subject[i] == '-') gaps++;
                else if (query[i] == subject[i]) identities++;
            }
            return "<Hsp><bit-score>40.5</bit-score><evalue>1e-8</evalue><identity>" + identities + "</identity>" +
                "<query-from>" + qFrom + "</query-from><query-to>" + qTo + "</query-to><hit-from>" + sFrom + "</hit-from><hit-to>" + sTo + "</hit-to>" +
                "<align-len>" + query.Length + "</align-len><gaps>" + gaps + "</gaps><qseq>" + query + "</qseq><hseq>" + subject + "</hseq></Hsp>";
        }
        private static string ReverseComplement(string value)
        {
            char[] result = new char[value.Length];
            for (int i = 0; i < value.Length; i++) result[result.Length - i - 1] = "TGCA"["ACGT".IndexOf(value[i])];
            return new string(result);
        }
        private static Dictionary<string, string> DecodeForm(string form)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string pair in form.Split('&'))
            {
                int equal = pair.IndexOf('='); result.Add(Uri.UnescapeDataString(pair.Substring(0, equal)), Uri.UnescapeDataString(pair.Substring(equal + 1)));
            }
            return result;
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void ExpectArgument(Action action)
        {
            try { action(); } catch (ArgumentException) { return; }
            throw new InvalidOperationException("Expected invalid argument to be rejected.");
        }
        private static void ExpectInvalid(Action action)
        {
            try { action(); } catch (InvalidDataException) { return; } catch (XmlException) { return; }
            throw new InvalidOperationException("Expected invalid/incomplete XML response to fail.");
        }
    }
}
