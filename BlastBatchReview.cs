using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace LampBatchReview
{
    public sealed class Primer
    {
        public string Id, Group, Role, Dna;
        public string[] Reactions;
        public bool Auxiliary { get { return Role == "LF" || Role == "LB"; } }
        public bool Reverse { get { return Role == "F1c" || Role == "B2" || Role == "B3" || Role == "R"; } }
    }
    public sealed class ReactionProfile
    {
        public string Group, Name;
        public Primer[] Primers;
    }
    public sealed class Seed
    {
        public string Accession;
        public int Start, End, ReferenceLength;
        public bool Reverse;
    }
    public sealed class Window
    {
        public string Group, Accession;
        public int Start, End;
        public bool Reverse;
    }
    public sealed class Match
    {
        public Primer Primer;
        public int Start, End;
        public bool Reverse;
        public string Reference;
        public List<int> Mismatches = new List<int>();
        public int KeyMismatches { get { return Mismatches.Count(p => Primer.Role == "F1c" || Primer.Role == "B1c" ? p <= 5 : p > Primer.Dna.Length - 5); } }
        public int PStart(bool reverse) { return reverse ? -End : Start; }
        public int PEnd(bool reverse) { return reverse ? -Start : End; }
    }
    public sealed class Combination
    {
        public string Group, Accession, Reaction;
        public bool Reverse, Expected;
        public List<Match> Matches;
        public int Start { get { return Matches.Min(x => x.Start); } }
        public int End { get { return Matches.Max(x => x.End); } }
        public int Mismatches { get { return Matches.Sum(x => x.Mismatches.Count); } }
    }
    public sealed class Settings
    {
        public string Xml, Fasta, Output, CacheDirectory, ExpectedAccession = "NC_000010.11", ExpectedRole = "F2";
        public int ExpectedSite = 94981296, MaxSpan = 1000, MaxMismatches = 4;
        public bool Network = true, Plan;
    }
    public sealed class Analysis
    {
        public List<Primer> Primers;
        public List<ReactionProfile> Profiles;
        public Dictionary<string, List<Seed>> Seeds = new Dictionary<string, List<Seed>>();
        public List<Window> Windows;
        public List<Combination> Combinations = new List<Combination>();
        public List<string> Notes = new List<string>();
        public Dictionary<string, int> HspCounts = new Dictionary<string, int>();
        public int ReviewedWindows, FailedWindows;
        public bool Truncated, MissingQueries;
    }
    public static class Engine
    {
        public static readonly string[] Roles = { "F3", "F2", "F1c", "B1c", "B2", "B3" };
        public static readonly string[] PairRoles = { "F", "R" };
        static string Value(XElement e, string name) { XElement c = e.Elements().FirstOrDefault(x => x.Name.LocalName == name); return c == null ? "" : c.Value; }
        static int Number(XElement e, string name) { int n; if (!Int32.TryParse(Value(e, name), out n)) throw new InvalidDataException("BLAST 数值字段不完整：" + name); return n; }
        static string Token(string s) { return Regex.Split(s.Trim(), @"\s+")[0]; }
        public static string ReverseComplement(string dna)
        {
            return new string(dna.Reverse().Select(c => c == 'A' ? 'T' : c == 'T' ? 'A' : c == 'C' ? 'G' : c == 'G' ? 'C' : 'N').ToArray());
        }
        public static List<Primer> ReadFasta(string path)
        {
            var result = new List<Primer>(); string id = null, header = null; var dna = new StringBuilder();
            Action flush = delegate
            {
                if (id == null) return;
                string sequence = dna.ToString().ToUpperInvariant();
                if (!Regex.IsMatch(sequence, "^[ACGT]+$") || sequence.Length < 7 || sequence.Length > 1000) throw new InvalidDataException("查询须为 7–1000 nt 的明确 DNA 序列：" + id);
                var m = Regex.Match(id, @"^(?<group>.+)_(?<role>F1c|B1c|F3|B3|F2|B2|LF|LB|F|R)(?:_|$)");
                if (!m.Success) throw new InvalidDataException("FASTA 标题须包含组名和区段，例如 Set01_F2_ref_A；LAMP 使用拆分后的六区段，RPA 使用 F/R：" + id);
                if (result.Any(p => p.Id == id)) throw new InvalidDataException("FASTA 标题重复：" + id);
                var tags = Regex.Match(header, @"(?:^|\s)reactions=([^\s]+)");
                string[] reactions = tags.Success ? tags.Groups[1].Value.Split(',') : null;
                if (reactions != null && reactions.Any(r => !Regex.IsMatch(r, @"^[A-Za-z0-9_-]+$"))) throw new InvalidDataException("reactions 标记仅允许字母、数字、下划线和连字符：" + id);
                result.Add(new Primer { Id = id, Group = m.Groups["group"].Value, Role = m.Groups["role"].Value, Dna = sequence, Reactions = reactions });
            };
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                if (line.StartsWith(">")) { flush(); header = line.Substring(1); id = Token(header); dna.Clear(); }
                else { if (id == null) throw new InvalidDataException("FASTA 序列前缺少 >标题。"); dna.Append(line); }
            }
            flush(); if (result.Count == 0) throw new InvalidDataException("FASTA 中没有引物。");
            BuildProfiles(result);
            return result;
        }
        public static List<ReactionProfile> BuildProfiles(List<Primer> primers)
        {
            var profiles = new List<ReactionProfile>();
            foreach (var g in primers.GroupBy(p => p.Group))
            {
                var core = g.Where(p => !p.Auxiliary).ToList();
                bool pair = core.Any(p => p.Role == "F" || p.Role == "R");
                string[] roles = pair ? PairRoles : Roles;
                if (core.Any(p => !roles.Contains(p.Role))) throw new InvalidDataException(g.Key + " 的 RPA 与 LAMP 区段不能混为同一组。");
                if (core.Any(p => p.Reactions != null))
                {
                    if (core.Any(p => p.Reactions == null)) throw new InvalidDataException(g.Key + " 的反应标记不完整，请保留程序导出的 reactions 信息。");
                    foreach (string name in core.SelectMany(p => p.Reactions).Distinct())
                    {
                        var selected = core.Where(p => p.Reactions.Contains(name)).ToList();
                        foreach (string role in roles)
                            if (selected.Count(p => p.Role == role) != 1) throw new InvalidDataException(g.Key + "/" + name + " 缺失或重复区段：" + role);
                        profiles.Add(new ReactionProfile { Group = g.Key, Name = name, Primers = roles.Select(r => selected.Single(p => p.Role == r)).ToArray() });
                    }
                }
                else
                {
                    foreach (string role in roles)
                        if (!core.Any(p => p.Role == role)) throw new InvalidDataException(g.Key + " 缺少区段：" + role);
                    string[] variable = roles.Where(r => core.Count(p => p.Role == r) > 1).ToArray();
                    if (variable.Length > 1) throw new InvalidDataException(g.Key + " 有多个区段的多版本，请用 reactions 标记指定真实反应组合，避免混配。");
                    if (variable.Length == 0) profiles.Add(new ReactionProfile { Group = g.Key, Name = "common", Primers = roles.Select(r => core.Single(p => p.Role == r)).ToArray() });
                    else foreach (Primer variant in core.Where(p => p.Role == variable[0]))
                        profiles.Add(new ReactionProfile { Group = g.Key, Name = variant.Id, Primers = roles.Select(r => r == variable[0] ? variant : core.Single(p => p.Role == r)).ToArray() });
                }
            }
            return profiles;
        }
        public static Analysis Read(Settings settings)
        {
            var a = new Analysis { Primers = ReadFasta(settings.Fasta) };
            a.Profiles = BuildProfiles(a.Primers);
            if (a.Primers.Any(p => p.Auxiliary)) a.Notes.Add("LF/LB 仅列出局部查询命中，不作为核心六区段组合的必要条件，未单独证明环引物特异性。");
            foreach (Primer p in a.Primers) { a.Seeds[p.Id] = new List<Seed>(); a.HspCounts[p.Id] = 0; }
            var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            XDocument document; using (var reader = XmlReader.Create(settings.Xml, readerSettings)) document = XDocument.Load(reader);
            bool xml2 = document.Root.Name.LocalName == "BlastXML2";
            if (!xml2 && document.Root.Name.LocalName != "BlastOutput") throw new InvalidDataException("请使用 NCBI BLAST 的完整 XML/XML2 文本文件；ZIP 文件需要先解压。");
            var queries = document.Descendants().Where(x => x.Name.LocalName == (xml2 ? "Search" : "Iteration"));
            var seen = new HashSet<string>();
            foreach (XElement query in queries)
            {
                string id = Token(Value(query, xml2 ? "query-title" : "Iteration_query-def"));
                Primer primer = a.Primers.FirstOrDefault(p => p.Id == id);
                if (primer == null) { a.Notes.Add("XML 中的查询未对应 FASTA，已跳过：" + id); continue; }
                if (!seen.Add(id)) throw new InvalidDataException("XML 中查询标题重复：" + id);
                int length = Number(query, xml2 ? "query-len" : "Iteration_query-len");
                if (length != primer.Dna.Length) throw new InvalidDataException("XML 与 FASTA 序列长度不同：" + id);
                foreach (XElement hit in query.Descendants().Where(x => x.Name.LocalName == "Hit"))
                {
                    XElement descr = xml2 ? hit.Descendants().FirstOrDefault(x => x.Name.LocalName == "HitDescr") : hit;
                    string acc = Value(descr, xml2 ? "accession" : "Hit_accession");
                    string hitId = Value(descr, xml2 ? "id" : "Hit_id");
                    var accessionMatch = Regex.Match(hitId, @"(?:ref|gb|emb|dbj|tpg|tpe|tpd)\|([^|]+)\|");
                    if (accessionMatch.Success) acc = accessionMatch.Groups[1].Value;
                    int refLength = Number(hit, xml2 ? "len" : "Hit_len");
                    foreach (XElement h in hit.Descendants().Where(x => x.Name.LocalName == "Hsp"))
                    {
                        string pre = xml2 ? "" : "Hsp_";
                        int qf = Number(h, pre + (xml2 ? "query-from" : "query-from"));
                        int qt = Number(h, pre + "query-to"), hf = Number(h, pre + "hit-from"), ht = Number(h, pre + "hit-to");
                        string qseq = Value(h, pre + "qseq").ToUpperInvariant(), hseq = Value(h, pre + "hseq").ToUpperInvariant();
                        if (qf < 1 || qt < 1 || qf > length || qt > length || qseq.Length != hseq.Length || qseq.Length == 0 || hf < 1 || ht < 1 || hf > refLength || ht > refLength)
                            throw new InvalidDataException("BLAST 比对坐标或序列不完整：" + id);
                        string ungapped = qseq.Replace("-", "");
                        string expected = primer.Dna.Substring(Math.Min(qf, qt) - 1, Math.Abs(qt - qf) + 1);
                        if (qf > qt) expected = ReverseComplement(expected);
                        if (ungapped != expected) throw new InvalidDataException("XML 与 FASTA 的碱基不一致，不能复核：" + id);
                        a.HspCounts[id]++;
                        // Approximate a full-primer footprint only for seeding a window;
                        // all combinations are later rebuilt from the reference sequence.
                        bool reverse = (hf > ht) != (qf > qt);
                        int minQ = Math.Min(qf, qt), maxQ = Math.Max(qf, qt);
                        int low = Math.Min(hf, ht), high = Math.Max(hf, ht);
                        int start = reverse ? low - (length - maxQ) : low - (minQ - 1);
                        int end = reverse ? high + (minQ - 1) : high + (length - maxQ);
                        if (start < 1 || end > refLength) continue;
                        a.Seeds[id].Add(new Seed { Accession = acc, Start = start, End = end, Reverse = reverse, ReferenceLength = refLength });
                    }
                }
            }
            foreach (Primer p in a.Primers)
                if (!seen.Contains(p.Id)) { a.MissingQueries = true; a.Notes.Add("XML 缺少查询的完成记录，不能按零命中解释：" + p.Id); }
                else if (a.HspCounts[p.Id] == 0) a.Notes.Add("本次查询没有返回命中，不代表特异性通过：" + p.Id);
            foreach (string key in a.Seeds.Keys.ToArray())
                a.Seeds[key] = a.Seeds[key].GroupBy(s => s.Accession + ":" + s.Start + ":" + s.End + ":" + s.Reverse).Select(g => g.First()).ToList();
            a.Windows = SeedWindows(a, settings.MaxSpan);
            return a;
        }
        public static List<Window> SeedWindows(Analysis a, int span)
        {
            var windows = new List<Window>();
            foreach (var profile in a.Profiles ?? BuildProfiles(a.Primers))
            {
                Primer f3 = profile.Primers[0];
                foreach (Seed anchor in a.Seeds[f3.Id])
                {
                    bool reverse = anchor.Reverse;
                    int pStart = reverse ? -anchor.End : anchor.Start;
                    int pEnd = reverse ? -anchor.Start : anchor.End;
                    bool compatible = true;
                    foreach (Primer p in profile.Primers.Skip(1))
                    {
                        var found = a.Seeds[p.Id].Where(s => s.Accession == anchor.Accession && s.Reverse == (p.Reverse != reverse))
                            .Select(s => new { Start = reverse ? -s.End : s.Start, End = reverse ? -s.Start : s.End })
                            .Where(s => s.Start > pEnd && s.End - pStart + 1 <= span).OrderBy(s => s.End).FirstOrDefault();
                        if (found == null) { compatible = false; break; }
                        pEnd = found.End;
                    }
                    if (!compatible) continue;
                    windows.Add(new Window { Group = profile.Group, Accession = anchor.Accession, Reverse = reverse,
                        Start = reverse ? Math.Max(1, anchor.End - span + 1) : anchor.Start,
                        End = reverse ? anchor.End : Math.Min(anchor.ReferenceLength, anchor.Start + span - 1) });
                }
            }
            var merged = new List<Window>();
            foreach (var key in windows.GroupBy(w => w.Group + "|" + w.Accession + "|" + w.Reverse))
            {
                Window current = null;
                foreach (Window w in key.OrderBy(w => w.Start))
                {
                    if (current != null && w.Start <= current.End) current.End = Math.Max(current.End, w.End);
                    else { current = w; merged.Add(w); }
                }
            }
            return merged;
        }
        public static List<Match> FindMatches(Primer p, string reference, int start, bool overallReverse, int maxMismatches)
        {
            var result = new List<Match>(); bool reverse = p.Reverse != overallReverse;
            for (int offset = 0; offset <= reference.Length - p.Dna.Length; offset++)
            {
                string sequence = reference.Substring(offset, p.Dna.Length);
                if (sequence.Any(c => "ACGT".IndexOf(c) < 0)) continue;
                if (reverse) sequence = ReverseComplement(sequence);
                var mismatch = new List<int>();
                for (int i = 0; i < p.Dna.Length; i++)
                    if (p.Dna[i] != sequence[i]) { mismatch.Add(i + 1); if (mismatch.Count > maxMismatches) break; }
                if (mismatch.Count <= maxMismatches) result.Add(new Match { Primer = p, Start = start + offset, End = start + offset + p.Dna.Length - 1, Reverse = reverse, Reference = sequence, Mismatches = mismatch });
            }
            return result.OrderBy(m => m.PStart(overallReverse)).ToList();
        }
        public static List<Combination> Join(Window window, Primer[] primers, Dictionary<string, List<Match>> hits, Settings settings, ref bool truncated)
        {
            var combinations = new List<Combination>(); var chain = new List<Match>(); int steps = 0; bool cut = false;
            Action<int> visit = null;
            visit = delegate(int index)
            {
                if (++steps > 200000 || combinations.Count >= 200) { cut = true; return; }
                if (index == primers.Length)
                {
                    Match selected = chain.FirstOrDefault(m => m.Primer.Role == settings.ExpectedRole);
                    int site = selected == null ? -1 : selected.Reverse ? selected.Start : selected.End;
                    combinations.Add(new Combination { Group = window.Group, Accession = window.Accession, Reverse = window.Reverse,
                        Expected = settings.ExpectedSite > 0 && window.Accession == settings.ExpectedAccession && site == settings.ExpectedSite, Matches = new List<Match>(chain) });
                    return;
                }
                foreach (Match m in hits[primers[index].Id])
                {
                    if (chain.Count > 0 && (m.PStart(window.Reverse) <= chain[chain.Count - 1].PEnd(window.Reverse) || m.PEnd(window.Reverse) - chain[0].PStart(window.Reverse) + 1 > settings.MaxSpan)) continue;
                    chain.Add(m); visit(index + 1); chain.RemoveAt(chain.Count - 1); if (cut) break;
                }
            };
            visit(0); truncated |= cut; return combinations;
        }
        public static string CachePath(Settings s, Window w)
        {
            if (!Regex.IsMatch(w.Accession ?? "", @"^[A-Za-z0-9_]+(?:\.\d+)?$")) throw new InvalidDataException("参考登录号格式无效：" + w.Accession);
            return Path.Combine(String.IsNullOrEmpty(s.CacheDirectory) ? Path.Combine(s.Output, "参考缓存") : s.CacheDirectory, w.Accession + "_" + w.Start + "_" + w.End + ".fasta");
        }
        public static string ReadReference(string fasta, int expectedLength)
        {
            string dna = String.Join("", fasta.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith(">"))).ToUpperInvariant();
            if (dna.Length != expectedLength || !Regex.IsMatch(dna, "^[ACGTN]+$")) throw new InvalidDataException("参考区段长度或 DNA 字符不符合预期。");
            return dna;
        }
        public static string Reference(Settings s, Window w, CancellationToken token)
        {
            string path = CachePath(s, w); int length = w.End - w.Start + 1;
            if (File.Exists(path)) return ReadReference(File.ReadAllText(path), length);
            if (!s.Network) throw new IOException("本地参考缓存缺失，需联网补齐。");
            if (!Regex.IsMatch(w.Accession, @"^[A-Za-z0-9_]+\.\d+$")) throw new InvalidDataException("缺少明确的参考序列版本，未自动联网：" + w.Accession);
            if (length > 50000) throw new InvalidDataException("合并参考区段超过 50,000 bp，需单独复核。");
            string url = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi?db=nuccore&id=" + Uri.EscapeDataString(w.Accession)
                + "&rettype=fasta&retmode=text&seq_start=" + w.Start + "&seq_stop=" + w.End + "&strand=1&tool=LampBatchReview";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested(); Thread.Sleep(attempt == 0 ? 1000 : 2500);
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url); request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                    request.UserAgent = "LampBatchReview/1.0";
                    string fasta; using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) fasta = reader.ReadToEnd();
                    string dna = ReadReference(fasta, length);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, fasta, new UTF8Encoding(false)); return dna;
                }
                catch (WebException) { if (attempt == 2) throw; }
            }
            throw new IOException("参考序列获取失败。");
        }
        public static Analysis Run(Settings s, Action<string> progress, CancellationToken token)
        {
            if (s.MaxMismatches < 0 || s.MaxMismatches > 10 || s.MaxSpan < 100 || s.MaxSpan > 10000) throw new ArgumentException("每区段错配数须为 0–10，跨度须为 100–10000 bp。");
            Directory.CreateDirectory(s.Output); progress("读取 FASTA 和完整 BLAST XML…"); Analysis a = Read(s);
            progress("找到 " + a.Windows.Count + " 个有相容区段布局的候选区域。");
            if (a.Windows.Count > 200) { a.Notes.Add("候选区域超过 200 个，仅复核前 200 个；其余未评估。"); a.Truncated = true; }
            if (s.Plan) return a;
            foreach (Window w in a.Windows.Take(200))
            {
                token.ThrowIfCancellationRequested(); progress("补齐参考区段并比较 " + w.Accession + ":" + w.Start + "–" + w.End);
                try
                {
                    string reference = Reference(s, w, token);
                    if (reference.Contains('N')) a.Notes.Add(w.Accession + ":" + w.Start + "–" + w.End + " 含 N；覆盖 N 的结合位点未能评估。");
                    var group = a.Primers.Where(p => p.Group == w.Group && !p.Auxiliary).ToList();
                    var hits = group.ToDictionary(p => p.Id, p => FindMatches(p, reference, w.Start, w.Reverse, s.MaxMismatches));
                    foreach (ReactionProfile profile in a.Profiles.Where(p => p.Group == w.Group))
                    {
                        var joined = Join(w, profile.Primers, hits, s, ref a.Truncated);
                        foreach (Combination c in joined) c.Reaction = profile.Name;
                        a.Combinations.AddRange(joined);
                    }
                    a.ReviewedWindows++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { a.FailedWindows++; a.Notes.Add("区段未完成复核：" + w.Accession + ":" + w.Start + "–" + w.End + "；" + ex.Message); }
            }
            a.Combinations = a.Combinations.GroupBy(c => c.Accession + "|" + c.Group + "|" + c.Reaction + "|" + String.Join("|", c.Matches.Select(m => m.Primer.Id + ":" + m.Start + ":" + m.End + ":" + m.Reverse)))
                .Select(g => g.First()).OrderByDescending(c => c.Expected).ThenBy(c => c.Mismatches).ThenBy(c => c.Accession).ThenBy(c => c.Start).ToList();
            return a;
        }
        static string Html(string s) { return WebUtility.HtmlEncode(s ?? ""); }
        public static string TextReport(Settings s, Analysis a)
        {
            var t = new StringBuilder();
            t.AppendLine("BLAST 手动批量复核 · 计算初筛");
            t.AppendLine("XML：" + Path.GetFullPath(s.Xml)); t.AppendLine("区段 FASTA：" + Path.GetFullPath(s.Fasta));
            t.AppendLine("读取查询 " + a.Primers.Count + " 条；反应组合定义 " + a.Profiles.Count + " 套。");
            t.AppendLine("候选区域 " + a.Windows.Count + " 个；已完成 " + a.ReviewedWindows + " 个；未完成 " + a.FailedWindows + " 个。");
            t.AppendLine("预期位点组合 " + a.Combinations.Count(c => c.Expected) + " 个；其他需复核组合 " + a.Combinations.Count(c => !c.Expected) + " 个。");
            t.AppendLine("预期：" + (String.IsNullOrWhiteSpace(s.ExpectedAccession) || s.ExpectedSite == 0 ? "未指定完整预期位置，全部组合待确认" : s.ExpectedAccession + " / " + s.ExpectedRole + " 末位 " + s.ExpectedSite));
            t.AppendLine("每区段最多 " + s.MaxMismatches + " 处错配；跨度上限 " + s.MaxSpan + " bp；按完整参考序列无插入缺失比较。");
            t.AppendLine("仅复核本次 BLAST 返回的候选。未检出其他组合不等于特异性通过；未评估完整引物的二聚体、发卡、自扩增或等位区分。");
            if (a.Truncated) t.AppendLine("搜索达到区域或组合上限，结果存在截断。");
            foreach (string note in a.Notes.Distinct()) t.AppendLine("提示：" + note);
            foreach (Combination c in a.Combinations)
            {
                t.AppendLine(); t.AppendLine((c.Expected ? "预期位点" : "待复核") + " · " + c.Group + " / " + c.Reaction + " · " + c.Accession + ":" + c.Start + "–" + c.End + " · " + (c.Reverse ? "反向" : "正向") + " · " + (c.End - c.Start + 1) + " bp");
                foreach (Match m in c.Matches) t.AppendLine(m.Primer.Role + "  " + m.Start + "–" + m.End + " " + (m.Reverse ? "−" : "+") + "  全长错配位置：" + (m.Mismatches.Count == 0 ? "无" : String.Join(",", m.Mismatches)));
            }
            return t.ToString();
        }
        public static string WriteReport(Settings s, Analysis a)
        {
            bool expectedSpecified = !String.IsNullOrWhiteSpace(s.ExpectedAccession) && s.ExpectedSite > 0;
            var h = new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>BLAST 批量复核</title><style>body{font:16px/1.7 'Microsoft YaHei',sans-serif;max-width:1200px;margin:32px auto;padding:0 20px;color:#18333c;background:#fafcfc}h1,h2{color:#12666a}section{background:white;border:1px solid #d8e4e5;padding:18px;margin:18px 0;border-radius:10px}table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:8px;border:1px solid #d8e4e5;text-align:left}th{background:#eaf4f4}.note{background:#fff2d7;padding:12px}code{font-family:Consolas,monospace;word-break:break-all}b{color:#a5322d}small{color:#587076}</style><h1>BLAST 批量复核</h1>");
            h.Append("<p>生成时间：" + DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss") + "（北京时间）</p>");
            h.Append("<p><small>XML：" + Html(Path.GetFullPath(s.Xml)) + "<br>区段 FASTA：" + Html(Path.GetFullPath(s.Fasta)) + "</small></p>");
            h.Append("<p>已读取 " + a.Primers.Count + " 条查询；种子候选区域 " + a.Windows.Count + " 个；完成参考序列复核 " + a.ReviewedWindows + " 个；未完成 " + a.FailedWindows + " 个。</p>");
            h.Append("<p>覆盖预期 " + Html(s.ExpectedRole) + " 末位的组合 " + a.Combinations.Count(c => c.Expected) + " 个；其他需复核组合 " + a.Combinations.Count(c => !c.Expected) + " 个。组合数量包括不同反应、等位版本与不同结合位置。</p>");
            h.Append("<div class='note'>这是计算初筛，不能判定实际扩增、等位区分效果或特异性通过。只分析本次 BLAST 返回的种子候选区域。完整参考比较采用无插入缺失模型，每区段最多 " + s.MaxMismatches + " 处错配；LAMP 核心六区段或 RPA 的 F/R 不重叠，顺序与方向相容，外侧跨度不超过 " + s.MaxSpan + " bp。这些是可调整的工程阈值，不是实验判据。实际合成碱基（包括人为错配）均保留。此报告未评估完整引物的二聚体、发卡或无模板自扩增。</div>");
            h.Append("<p>预期参考序列：" + Html(s.ExpectedAccession) + "；" + Html(s.ExpectedRole) + " 最末位坐标：" + s.ExpectedSite + "。登录号与坐标共同匹配时才标注预期位点；未填写两项时，所有组合均列为待确认。</p>");
            h.Append("<p><small>BLAST 局部检索、返回上限、数据库覆盖及参考序列多态性都可能造成漏检。未发现完整六区段组合，也不能排除其他非特异扩增机制。查询标题、长度及返回的查询碱基已与 FASTA 核对。</small></p>");
            if (a.Notes.Count > 0 || a.Truncated)
            {
                h.Append("<section class='note'><h2>需要注意的未完成项</h2><ul>");
                foreach (string note in a.Notes.Distinct()) h.Append("<li>" + Html(note) + "</li>");
                if (a.Truncated) h.Append("<li>区域或组合搜索达到上限，报告不完整；未列出的组合未能排除。</li>");
                h.Append("</ul></section>");
            }
            h.Append("<section><h2>查询与局部命中</h2><table><tr><th>名称</th><th>长度</th><th>返回的局部比对片段数</th><th>5′→3′序列</th></tr>");
            foreach (Primer p in a.Primers) h.Append("<tr><td>" + Html(p.Id) + "</td><td>" + p.Dna.Length + "</td><td>" + a.HspCounts[p.Id] + "</td><td><code>" + p.Dna + "</code></td></tr>");
            h.Append("</table><p>命中片段数不是全长完全匹配位点数，也不是扩增产物数。</p></section>");
            if (a.Combinations.Count == 0) h.Append("<section><h2>本次未列出满足上述条件的组合</h2><p>请结合未完成项和筛查范围判断；不能作为无脱靶证明。</p></section>");
            int n = 0;
            foreach (Combination c in a.Combinations)
            {
                h.Append("<section><h2>" + (++n) + ". " + (c.Expected ? "覆盖预期 " + Html(s.ExpectedRole) + " 末位" : expectedSpecified ? "需复核的非目标组合" : "待确认位置的组合") + "</h2><p>" + Html(c.Group) + " · " + Html(c.Accession) + ":" + c.Start + "–" + c.End + " · 整体" + (c.Reverse ? "反向" : "正向") + " · 跨度 " + (c.End - c.Start + 1) + " bp · 反应：" + Html(c.Reaction) + "</p>");
                h.Append("<table><tr><th>区段</th><th>坐标 / 方向</th><th>全长错配位置<br>按引物5′端起算</th><th>关键末端5nt错配</th><th>引物与对应参考序列<br>均按引物5′→3′方向</th></tr>");
                foreach (Match m in c.Matches)
                {
                    string reference = String.Concat(m.Reference.Select((b, i) => m.Mismatches.Contains(i + 1) ? "<b>" + b + "</b>" : b.ToString()));
                    string keyEnd = m.Primer.Role == "F1c" || m.Primer.Role == "B1c" ? "5′" : "3′";
                    h.Append("<tr><td>" + Html(m.Primer.Role) + "</td><td>" + m.Start + "–" + m.End + " / " + (m.Reverse ? "−" : "+") + "</td><td>" + (m.Mismatches.Count == 0 ? "无" : String.Join(", ", m.Mismatches)) + "</td><td>" + keyEnd + "：" + m.KeyMismatches + "</td><td><code>引物：" + m.Primer.Dna + "<br>参考：" + reference + "</code></td></tr>");
                }
                h.Append("</table><p><small>参考序列只代表本次数据库中的等位状态。等位引物对另一种参考等位状态的末位错配不能单独作为淘汰依据。F1c/B1c 的末端信息用于成环区段复核。PA-LAMP 输入切后有效 DNA 时，此报告不评价 RNA/C3、RNase H2 激活或等位选择性。</small></p></section>");
            }
            h.Append("<section><h2>候选参考区域</h2><table><tr><th>候选组</th><th>参考区段</th><th>整体方向</th></tr>");
            foreach (Window w in a.Windows) h.Append("<tr><td>" + Html(w.Group) + "</td><td>" + Html(w.Accession) + ":" + w.Start + "–" + w.End + "</td><td>" + (w.Reverse ? "−" : "+") + "</td></tr>");
            h.Append("</table><p>联网只读取这些公开参考区段；不重新提交引物。</p></section></html>");
            string path = Path.Combine(s.Output, "BLAST批量复核报告.html"); File.WriteAllText(path, h.ToString(), new UTF8Encoding(false)); return path;
        }
    }
}
