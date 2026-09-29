using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    public sealed class DesignSettings
    {
        public int PrimerMin = 30, PrimerMax = 35;
        public int AmpliconMin = 100, AmpliconMax = 200, PreferredAmplicon = 150;
        public int MaxPairs = 10, TargetStart = 0, TargetEnd = 0;
        public double GcMin = 30, GcMax = 70;
        public bool PrimerMinUnlimited, PrimerMaxUnlimited, GcMinUnlimited, GcMaxUnlimited;
        public bool AmpliconMinUnlimited, AmpliconMaxUnlimited, PreferredAmpliconUnlimited;
    }

    public sealed class ParsedSequence
    {
        public string Name = "输入序列", Sequence = "";
        public List<string> Warnings = new List<string>();
    }

    public sealed class Primer
    {
        public string Sequence;
        // Coordinates are one-based, inclusive, on the supplied template's plus strand.
        // Reverse-primer sequence is always reported as the synthesized 5'-to-3' oligo.
        public int Start, End;
        public double Gc, Tm;
        public int Hairpin, SelfComplement, SelfThreePrime, TandemRepeat;
        public List<string> Warnings = new List<string>();
    }

    public sealed class PrimerPair
    {
        public int Rank;
        public Primer Forward, Reverse;
        public int AmpliconStart, AmpliconEnd, AmpliconLength;
        public string AmpliconSequence;
        public double Score;
        public int CrossComplement, CrossThreePrime;
        public List<string> Warnings = new List<string>();
    }

    public sealed class DesignResult
    {
        public ParsedSequence Input;
        public DesignSettings Settings;
        public List<PrimerPair> Pairs = new List<PrimerPair>();
        public List<string> Notes = new List<string>();
        public int ForwardCandidateCount, ReverseCandidateCount;
        public bool SearchTruncated;
    }

    public static class SequenceParser
    {
        public const int MaximumLength = 20000;

        public static ParsedSequence Parse(string input)
        {
            if (String.IsNullOrWhiteSpace(input))
                throw new ArgumentException("请输入 DNA 序列，或导入单条记录的 FASTA 文件。");
            if (input.Length > 1000000)
                throw new ArgumentException("原始输入不得超过 1,000,000 个字符；请只保留单条模板序列。本版本最多处理 20,000 nt。");
            ParsedSequence result = new ParsedSequence();
            StringBuilder sequence = new StringBuilder();
            string[] lines = input.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool sawHeader = false, sawContent = false, convertedU = false, removedNumbers = false;
            int ambiguous = 0;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '>')
                {
                    if (sawHeader) throw new ArgumentException("检测到多条 FASTA 记录。请一次只输入一条模板序列。");
                    if (sawContent) throw new ArgumentException("FASTA 标题必须位于序列之前，不能混入序列中。");
                    sawHeader = true;
                    result.Name = line.Substring(1).Trim();
                    if (result.Name.Length == 0) result.Name = "未命名 FASTA 序列";
                    continue;
                }
                sawContent = true;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = Char.ToUpperInvariant(line[i]);
                    if (Char.IsWhiteSpace(c)) continue;
                    if (c >= '0' && c <= '9') { removedNumbers = true; continue; }
                    if (c == 'U') { c = 'T'; convertedU = true; }
                    if ("ACGTRYSWKMBDHVN".IndexOf(c) < 0)
                        throw new ArgumentException("序列包含不支持的字符“" + line[i] + "”。仅接受 A/C/G/T/U、IUPAC 歧义碱基、空白和行号数字。");
                    if (c != 'A' && c != 'C' && c != 'G' && c != 'T') ambiguous++;
                    sequence.Append(c);
                    if (sequence.Length > MaximumLength)
                        throw new ArgumentException("本版本每次最多处理 20,000 nt；请截取包含靶区的模板片段。");
                }
            }
            if (sequence.Length == 0) throw new ArgumentException("没有读到碱基序列。FASTA 记录必须包含非空序列。");
            result.Sequence = sequence.ToString();
            if (convertedU) result.Warnings.Add("输入中的 U 已转换为 T。输出为 DNA 引物；本程序不判断是否需要反转录步骤。");
            if (removedNumbers) result.Warnings.Add("已去除序列中的行号数字；所有坐标均基于清理后的模板序列。");
            if (ambiguous > 0) result.Warnings.Add("模板含 " + ambiguous + " 个 IUPAC 歧义碱基，坐标保持不变；含这些位置的引物结合窗口会被跳过。");
            return result;
        }
    }

    public static class DesignEngine
    {
        private const int CandidateLimit = 4000;
        private const int PairEvaluationLimit = 30000;
        private const int PositionBin = 20;

        internal sealed class Candidate
        {
            public Primer Primer;
            public double Penalty;
            public int Homopolymer;
        }

        private sealed class PairSeed
        {
            public Candidate Forward, Reverse;
            public double Penalty;
        }

        private sealed class StructureMetrics
        {
            public int Hairpin, Complement, ThreePrime, TandemRepeat;
        }

        private sealed class EvaluatedPair
        {
            public PairSeed Seed;
            public double Penalty;
            public int Complement, ThreePrime;
        }

        public static string ReverseComplement(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            char[] result = new char[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char c = Char.ToUpperInvariant(value[value.Length - i - 1]);
                switch (c)
                {
                    case 'A': result[i] = 'T'; break;
                    case 'T': case 'U': result[i] = 'A'; break;
                    case 'C': result[i] = 'G'; break;
                    case 'G': result[i] = 'C'; break;
                    case 'R': result[i] = 'Y'; break;
                    case 'Y': result[i] = 'R'; break;
                    case 'S': result[i] = 'S'; break;
                    case 'W': result[i] = 'W'; break;
                    case 'K': result[i] = 'M'; break;
                    case 'M': result[i] = 'K'; break;
                    case 'B': result[i] = 'V'; break;
                    case 'V': result[i] = 'B'; break;
                    case 'D': result[i] = 'H'; break;
                    case 'H': result[i] = 'D'; break;
                    case 'N': result[i] = 'N'; break;
                    default: throw new ArgumentException("反向互补序列包含不支持的碱基。");
                }
            }
            return new string(result);
        }

        public static DesignResult Design(ParsedSequence input, DesignSettings settings,
            Action<int, string> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (input == null) throw new ArgumentNullException("input");
            if (settings == null) throw new ArgumentNullException("settings");
            ParsedSequence normalized = SequenceParser.Parse(input.Sequence);
            normalized.Name = String.IsNullOrWhiteSpace(input.Name) ? "输入序列" : input.Name;
            if (input.Warnings != null)
                foreach (string warning in input.Warnings)
                    if (!normalized.Warnings.Contains(warning)) normalized.Warnings.Add(warning);
            DesignSettings options = CopyAndValidate(settings, normalized.Sequence.Length);
            DesignResult result = new DesignResult { Input = normalized, Settings = options };
            result.Notes.AddRange(normalized.Warnings);
            result.Notes.Add("候选引物均按 5′→3′ 输出；坐标为输入模板正链的 1-based 闭区间，反向引物已取反向互补。");
            result.Notes.Add("默认 RPA 范围为引物 30–35 nt、GC 30–70%、产物 100–200 bp；优先产物长度仅用于排序。连续同一碱基超过 5 nt 的引物已排除。");
            result.Notes.Add("评分仅为可解释的序列启发式排序，不是扩增成功率。连续互补与发卡指标不含错配、鼓包或热力学自由能计算，需另做二级结构核验和实验筛选。");
            result.Notes.Add("简单串联重复按 2–4 nt 单元连续重复至少 3 次的最长完整跨度计算；每条引物增加 0.5 × max(0, 重复跨度 − 8) 的启发式惩罚，跨度 ≥12 nt 时提示，不作为硬排除条件。");
            result.Notes.Add("Tm 使用经验公式 64.9 + 41 × (GC数 − 16.4) / 长度，仅作参考；不用于 RPA 候选筛选或评分，也不是反应温度建议。");
            result.Notes.Add("特异性检查仅提示入选引物在当前输入模板内的重复完全匹配；未进行全基因组、近似匹配或数据库特异性筛查。");
            if (options.PrimerMin < 30 || options.PrimerMax > 35)
                result.Notes.Add("当前引物长度范围超出默认 30–35 nt，属于自定义探索范围。");
            if (options.AmpliconMax > 500)
                result.Notes.Add("当前允许产物超过 500 bp，超出常用 RPA 设计范围；长产物是否适用需要单独验证。");
            if (options.TargetStart > 0)
                result.Notes.Add("已要求引物严格位于靶区 " + options.TargetStart + "–" + options.TargetEnd + " 的两侧，两个引物结合区均不与靶区重叠。");
            Report(progress, 3, "解析模板并扫描候选引物…");
            List<Candidate> forward = GenerateCandidates(normalized.Sequence, options, false, result, cancellation);
            List<Candidate> reverse = GenerateCandidates(normalized.Sequence, options, true, result, cancellation);
            int rawForward = forward.Count, rawReverse = reverse.Count;
            forward = PruneCandidates(forward, false);
            reverse = PruneCandidates(reverse, true);
            result.ForwardCandidateCount = forward.Count;
            result.ReverseCandidateCount = reverse.Count;
            if (rawForward > forward.Count || rawReverse > reverse.Count)
            {
                result.SearchTruncated = true;
                result.Notes.Add("候选过多，采用每 20 nt 位置分箱、按初筛分数分配保留名额：正向 " + rawForward + "→" + forward.Count + "，反向 " + rawReverse + "→" + reverse.Count + "。未穷尽全部候选，可能遗漏更优组合。");
            }
            if (options.PrimerMaxUnlimited && options.PrimerMax > 128)
            {
                int removedForward, removedReverse;
                forward = LimitStructureCandidates(forward, 100000000L, out removedForward);
                reverse = LimitStructureCandidates(reverse, 100000000L, out removedReverse);
                if (removedForward + removedReverse > 0)
                {
                    result.SearchTruncated = true;
                    result.Notes.Add("无引物长度上限时，单方向结构评估按初筛排序采用 1 亿平方长度运算预算（至少评估一条），另舍弃正向 " + removedForward + "、反向 " + removedReverse + " 条；这是计算预算，不是长度限制，可能遗漏有效组合。");
                }
                result.ForwardCandidateCount = forward.Count;
                result.ReverseCandidateCount = reverse.Count;
            }
            if (forward.Count == 0 || reverse.Count == 0)
            {
                result.Notes.Add("没有足够的正反向候选。请检查模板长度、歧义碱基、GC 范围、长同聚物或靶区两侧是否留有足够序列。");
                Report(progress, 100, "未找到符合条件的引物对。");
                return result;
            }
            Report(progress, 15, "评估候选引物的自互补与发卡…");
            Dictionary<string, StructureMetrics> cache = new Dictionary<string, StructureMetrics>(StringComparer.Ordinal);
            AddStructures(forward, cache, cancellation);
            AddStructures(reverse, cache, cancellation);
            forward.Sort(delegate(Candidate a, Candidate b) { return ComparePosition(a, b, false); });
            reverse.Sort(delegate(Candidate a, Candidate b) { return ComparePosition(a, b, true); });
            Report(progress, 35, "按产物长度与靶区筛选候选组合…");
            List<PairSeed> shortlist = BuildShortlist(forward, reverse, options, result, progress, cancellation);
            if (shortlist.Count == 0)
            {
                result.Notes.Add("存在单条候选引物，但没有满足产物长度、方向和靶区侧翼条件的引物对。");
                Report(progress, 100, "未找到符合条件的引物对。");
                return result;
            }
            Report(progress, 55, "计算引物对交叉互补并排序…");
            List<EvaluatedPair> evaluated = new List<EvaluatedPair>(shortlist.Count);
            bool budgetedStructures = options.PrimerMaxUnlimited && options.PrimerMax > 128;
            if (budgetedStructures) shortlist.Sort(CompareSeed);
            long pairWork = 0;
            for (int i = 0; i < shortlist.Count; i++)
            {
                if ((i & 127) == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Report(progress, 55 + i * 35 / shortlist.Count, "评估引物对 " + i + " / " + shortlist.Count + "…");
                }
                PairSeed seed = shortlist[i];
                long nextWork = (long)seed.Forward.Primer.Sequence.Length * seed.Reverse.Primer.Sequence.Length;
                if (budgetedStructures && i > 0 && pairWork + nextWork > 150000000L)
                {
                    result.SearchTruncated = true;
                    result.Notes.Add("无引物长度上限时，交叉互补评估按初筛排序采用 1.5 亿长度乘积运算预算（至少评估一对）；本次评估 " + i + " / " + shortlist.Count + " 对，可能遗漏有效组合。");
                    break;
                }
                pairWork += nextWork;
                int complement, threePrime;
                double penalty = seed.Penalty + PairStructurePenalty(seed.Forward.Primer, seed.Reverse.Primer, out complement, out threePrime, cancellation);
                evaluated.Add(new EvaluatedPair { Seed = seed, Penalty = penalty, Complement = complement, ThreePrime = threePrime });
            }
            evaluated.Sort(CompareEvaluated);
            HashSet<string> sequencePairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (EvaluatedPair value in evaluated)
            {
                cancellation.ThrowIfCancellationRequested();
                Primer f = value.Seed.Forward.Primer, r = value.Seed.Reverse.Primer;
                string key = f.Sequence + "/" + r.Sequence;
                if (sequencePairs.Contains(key)) continue;
                bool nearDuplicate = false;
                foreach (PrimerPair accepted in result.Pairs)
                {
                    if (Math.Abs(accepted.Forward.Start - f.Start) < 6 && Math.Abs(accepted.Reverse.End - r.End) < 6)
                    { nearDuplicate = true; break; }
                }
                if (nearDuplicate) continue;
                sequencePairs.Add(key);
                PrimerPair pair = new PrimerPair();
                pair.Rank = result.Pairs.Count + 1;
                pair.Forward = f;
                pair.Reverse = r;
                pair.AmpliconStart = f.Start;
                pair.AmpliconEnd = r.End;
                pair.AmpliconLength = r.End - f.Start + 1;
                pair.AmpliconSequence = normalized.Sequence.Substring(f.Start - 1, pair.AmpliconLength);
                pair.Score = ScoreFromPenalty(value.Penalty);
                pair.CrossComplement = value.Complement;
                pair.CrossThreePrime = value.ThreePrime;
                if (value.Complement >= 8) pair.Warnings.Add("正反引物存在 " + value.Complement + " nt 连续互补（启发式），建议核验引物二聚体。");
                if (value.ThreePrime >= 4) pair.Warnings.Add("正反引物涉及 3′ 端的连续互补达 " + value.ThreePrime + " nt（启发式）。");
                AddRepeatedBindingWarning(f, normalized.Sequence);
                AddRepeatedBindingWarning(r, normalized.Sequence);
                foreach (string warning in f.Warnings) pair.Warnings.Add("正向：" + warning);
                foreach (string warning in r.Warnings) pair.Warnings.Add("反向：" + warning);
                if (ContainsAmbiguous(pair.AmpliconSequence)) pair.Warnings.Add("扩增片段内部含歧义碱基；两条引物结合区均为确定的 A/C/G/T。");
                double productGc = UnambiguousGc(pair.AmpliconSequence);
                if (!Double.IsNaN(productGc) && (productGc < 40 || productGc > 60))
                    pair.Warnings.Add("产物确定碱基的 GC 为 " + productGc.ToString("0.0", CultureInfo.InvariantCulture) + "%，偏离常用优选区间 40–60%。");
                result.Pairs.Add(pair);
                if (result.Pairs.Count >= options.MaxPairs) break;
            }
            result.Notes.Add("已优先保留结合位置不同的方案：若两对引物的产物起点和终点差均小于 6 nt，则视为近似重复；相同引物序列对仅保留一次。");
            if (result.Pairs.Count < options.MaxPairs)
                result.Notes.Add("当前筛选和位置去重后仅得到 " + result.Pairs.Count + " 对候选，少于请求的 " + options.MaxPairs + " 对。");
            bool repeated = false;
            foreach (PrimerPair pair in result.Pairs)
                foreach (string warning in pair.Warnings)
                    if (warning.IndexOf("重复", StringComparison.Ordinal) >= 0) repeated = true;
            if (repeated) result.Notes.Add("部分入选引物在输入模板内存在重复完全匹配位置，可能出现额外结合或扩增；请查看各对警告并继续验证特异性。");
            Report(progress, 100, "设计完成：" + result.Pairs.Count + " 对候选引物。");
            return result;
        }

        internal static DesignSettings CopyAndValidate(DesignSettings source, int templateLength)
        {
            DesignSettings s = new DesignSettings
            {
                PrimerMin = source.PrimerMin, PrimerMax = source.PrimerMax,
                AmpliconMin = source.AmpliconMin, AmpliconMax = source.AmpliconMax,
                PreferredAmplicon = source.PreferredAmplicon, MaxPairs = source.MaxPairs,
                TargetStart = source.TargetStart, TargetEnd = source.TargetEnd,
                GcMin = source.GcMin, GcMax = source.GcMax,
                PrimerMinUnlimited = source.PrimerMinUnlimited, PrimerMaxUnlimited = source.PrimerMaxUnlimited,
                GcMinUnlimited = source.GcMinUnlimited, GcMaxUnlimited = source.GcMaxUnlimited,
                AmpliconMinUnlimited = source.AmpliconMinUnlimited, AmpliconMaxUnlimited = source.AmpliconMaxUnlimited,
                PreferredAmpliconUnlimited = source.PreferredAmpliconUnlimited
            };
            if ((!s.PrimerMinUnlimited && (s.PrimerMin < 20 || s.PrimerMin > 60)) ||
                (!s.PrimerMaxUnlimited && (s.PrimerMax < 20 || s.PrimerMax > 60)))
                throw new ArgumentException("引物长度须在 20–60 nt 内，且最小值不得大于最大值。RPA 默认 30–35 nt。");
            if (s.PrimerMinUnlimited) s.PrimerMin = 1;
            if (s.PrimerMaxUnlimited) s.PrimerMax = templateLength;
            if (s.PrimerMin > s.PrimerMax)
                throw new ArgumentException("有效引物最小长度不得大于有效最大长度；无限制上限仍受模板长度约束。");
            if ((!s.AmpliconMinUnlimited && (s.AmpliconMin < s.PrimerMin * 2 || s.AmpliconMin > 5000)) ||
                (!s.AmpliconMaxUnlimited && (s.AmpliconMax < 1 || s.AmpliconMax > 5000)))
                throw new ArgumentException("产物最小长度须至少为最短引物长度的两倍；产物最大长度不得超过 5,000 bp，且最小值不得大于最大值。");
            if (s.AmpliconMinUnlimited) s.AmpliconMin = 1;
            if (s.AmpliconMaxUnlimited) s.AmpliconMax = templateLength;
            if (s.AmpliconMin > s.AmpliconMax)
                throw new ArgumentException("有效产物最小长度不得大于有效最大长度；无限制上限仍受模板长度约束。");
            if (!s.PreferredAmpliconUnlimited && (s.PreferredAmplicon < s.AmpliconMin || s.PreferredAmplicon > s.AmpliconMax))
                throw new ArgumentException("优先产物长度必须位于所设产物长度区间内。");
            if (s.PreferredAmpliconUnlimited) s.PreferredAmplicon = 0;
            if (s.GcMinUnlimited) s.GcMin = 0;
            if (s.GcMaxUnlimited) s.GcMax = 100;
            if (Double.IsNaN(s.GcMin) || Double.IsNaN(s.GcMax) || Double.IsInfinity(s.GcMin) || Double.IsInfinity(s.GcMax) || s.GcMin < 0 || s.GcMax > 100 || s.GcMin > s.GcMax)
                throw new ArgumentException("GC 范围须在 0–100% 内，且最小值不得大于最大值。");
            if (s.MaxPairs < 1 || s.MaxPairs > 50) throw new ArgumentException("输出引物对数量须为 1–50。");
            if (s.TargetStart != 0 || s.TargetEnd != 0)
            {
                if (s.TargetStart < 1 || s.TargetEnd < s.TargetStart || s.TargetEnd > templateLength)
                    throw new ArgumentException("靶区必须使用模板内的 1-based 闭区间，或将起止位置同时设为 0。");
            }
            return s;
        }

        private static List<Candidate> GenerateCandidates(string template, DesignSettings s, bool reverse, DesignResult result, CancellationToken cancellation)
        {
            List<Candidate> output = new List<Candidate>();
            bool sampled;
            int[] lengths = SamplePrimerLengths(s.PrimerMin, Math.Min(s.PrimerMax, template.Length), out sampled);
            bool[] includeLength = new bool[Math.Min(s.PrimerMax, template.Length) + 1];
            foreach (int length in lengths) includeLength[length] = true;
            int[] starts = SamplePositions(0, template.Length - s.PrimerMin, sampled ? 512 : Int32.MaxValue);
            int pruned = 0;
            foreach (int start in starts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!reverse && start + s.AmpliconMin > template.Length) continue;
                if (s.TargetStart > 0 && reverse && start + 1 <= s.TargetEnd) continue;
                int gc = 0, run = 0, longest = 0;
                char previous = '\0';
                for (int length = 1; length <= s.PrimerMax && start + length <= template.Length; length++)
                {
                    if ((length & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    char c = template[start + length - 1];
                    if (c != 'A' && c != 'C' && c != 'G' && c != 'T') break;
                    if (c == 'G' || c == 'C') gc++;
                    run = c == previous ? run + 1 : 1;
                    previous = c;
                    longest = Math.Max(longest, run);
                    if (longest > 5) break;
                    if (length < s.PrimerMin || !includeLength[length]) continue;
                    int end = start + length;
                    if (reverse && end < s.AmpliconMin) continue;
                    if (s.TargetStart > 0 && !reverse && end >= s.TargetStart) continue;
                    double percent = 100.0 * gc / length;
                    if (percent < s.GcMin || percent > s.GcMax) continue;
                    string sequence = template.Substring(start, length);
                    if (reverse) sequence = ReverseComplement(sequence);
                    output.Add(CompositionCandidate(sequence, start + 1, s));
                    if (sampled && output.Count >= CandidateLimit * 2)
                    {
                        int before = output.Count;
                        output = PruneCandidates(output, reverse);
                        pruned += before - output.Count;
                    }
                }
            }
            if (sampled)
            {
                result.SearchTruncated = true;
                result.Notes.Add((reverse ? "反向" : "正向") + "引物长度范围较宽：在有效区间保留 60 nt 及以下全部长度，并均匀抽样更长的 32 个长度（含端点），本次 " + lengths.Length + " 种长度；沿全模板均匀抽样至多 512 个起点（含端点）。扫描中按既有位置分箱初评另舍弃 " + pruned + " 条。未穷尽长度与位置组合，可能遗漏有效引物。");
            }
            return output;
        }

        // Bounded search samples are computational policy, never a hidden primer
        // length filter. Callers must disclose sampling and set SearchTruncated.
        internal static int[] SamplePrimerLengths(int minimum, int maximum, out bool sampled)
        {
            sampled = maximum - minimum + 1 > 128;
            if (maximum < minimum) return new int[0];
            if (!sampled) return SamplePositions(minimum, maximum, Int32.MaxValue);
            SortedSet<int> lengths = new SortedSet<int>();
            for (int length = minimum; length <= Math.Min(maximum, 60); length++) lengths.Add(length);
            foreach (int length in SamplePositions(Math.Max(minimum, 61), maximum, 32)) lengths.Add(length);
            int[] result = new int[lengths.Count]; lengths.CopyTo(result); return result;
        }

        internal static int[] SamplePositions(int minimum, int maximum, int limit)
        {
            if (maximum < minimum) return new int[0];
            int count = Math.Min(maximum - minimum + 1, limit);
            int[] values = new int[count];
            for (int i = 0; i < count; i++) values[i] = count == 1 ? minimum : minimum + (int)((long)(maximum - minimum) * i / (count - 1));
            return values;
        }

        internal static List<Candidate> LimitStructureCandidates(List<Candidate> source, long budget, out int removed)
        {
            List<Candidate> sorted = new List<Candidate>(source); sorted.Sort(CompareCandidate);
            List<Candidate> kept = new List<Candidate>(); long work = 0;
            foreach (Candidate candidate in sorted)
            {
                long cost = 2L * candidate.Primer.Sequence.Length * candidate.Primer.Sequence.Length;
                if (kept.Count > 0 && work + cost > budget) continue;
                kept.Add(candidate); work += cost;
            }
            removed = source.Count - kept.Count; return kept;
        }

        private static List<Candidate> PruneCandidates(List<Candidate> source, bool reverse)
        {
            if (source.Count <= CandidateLimit) return source;
            SortedDictionary<int, List<Candidate>> bins = new SortedDictionary<int, List<Candidate>>();
            foreach (Candidate candidate in source)
            {
                int key = ((reverse ? candidate.Primer.End : candidate.Primer.Start) - 1) / PositionBin;
                List<Candidate> bin;
                if (!bins.TryGetValue(key, out bin)) { bin = new List<Candidate>(); bins.Add(key, bin); }
                bin.Add(candidate);
            }
            int quota = Math.Max(1, CandidateLimit / bins.Count);
            List<Candidate> output = new List<Candidate>();
            foreach (List<Candidate> bin in bins.Values)
            {
                bin.Sort(CompareCandidate);
                int count = Math.Min(quota, bin.Count);
                for (int i = 0; i < count; i++) output.Add(bin[i]);
            }
            return output;
        }

        // Shared by ordinary and SNP design. Always evaluate the actual synthesized
        // oligo, including any deliberately introduced mismatch.
        internal static Candidate CompositionCandidate(string sequence, int start, DesignSettings s)
        {
            if (sequence.Length < s.PrimerMin || sequence.Length > s.PrimerMax) return null;
            int gc = 0, run = 0, longest = 0;
            char previous = '\0';
            foreach (char c in sequence)
            {
                if (c != 'A' && c != 'C' && c != 'G' && c != 'T') return null;
                if (c == 'G' || c == 'C') gc++;
                run = c == previous ? run + 1 : 1;
                longest = Math.Max(longest, run);
                previous = c;
            }
            if (longest > 5) return null;
            double percent = 100.0 * gc / sequence.Length;
            if (percent < s.GcMin || percent > s.GcMax) return null;
            Primer primer = new Primer { Start = start, End = start + sequence.Length - 1,
                Sequence = sequence, Gc = percent, Tm = 64.9 + 41.0 * (gc - 16.4) / sequence.Length };
            double preferredLength = Math.Max(s.PrimerMin, Math.Min(s.PrimerMax, 32));
            double penalty = Math.Abs(percent - 50.0) * 0.12 + Math.Abs(sequence.Length - preferredLength) * 0.3 + Math.Max(0, longest - 3) * 1.5;
            return new Candidate { Primer = primer, Penalty = penalty, Homopolymer = longest };
        }

        internal static void AddStructures(List<Candidate> candidates, CancellationToken cancellation)
        {
            AddStructures(candidates, new Dictionary<string, StructureMetrics>(StringComparer.Ordinal), cancellation);
        }

        internal static double PairLengthPenalty(int length, DesignSettings s)
        {
            if (s.PreferredAmpliconUnlimited) return 0;
            return Math.Abs(length - s.PreferredAmplicon) * 8.0 / Math.Max(1, s.AmpliconMax - s.AmpliconMin);
        }

        internal static double PairStructurePenalty(Primer forward, Primer reverse, out int complement, out int threePrime)
        {
            return PairStructurePenalty(forward, reverse, out complement, out threePrime, CancellationToken.None);
        }

        internal static double PairStructurePenalty(Primer forward, Primer reverse, out int complement, out int threePrime, CancellationToken cancellation)
        {
            ComplementMetricsCancellable(forward.Sequence, reverse.Sequence, out complement, out threePrime, cancellation);
            return Math.Max(0, complement - 3) * 0.8 + Math.Max(0, threePrime - 2) * 2.5;
        }

        internal static double ScoreFromPenalty(double penalty)
        {
            return Math.Round(100.0 / (1.0 + penalty / 25.0), 2);
        }

        internal static PrimerPair MaterializePair(Candidate forward, Candidate reverse, string template,
            DesignSettings settings, int rank, bool incorporatePrimers, bool includeLengthPenalty = true, CancellationToken cancellation = default(CancellationToken))
        {
            Primer f = forward.Primer, r = reverse.Primer;
            int complement, threePrime;
            double penalty = forward.Penalty + reverse.Penalty + (includeLengthPenalty ? PairLengthPenalty(r.End - f.Start + 1, settings) : 0.0)
                + PairStructurePenalty(f, r, out complement, out threePrime, cancellation);
            int length = r.End - f.Start + 1;
            string product = template.Substring(f.Start - 1, length);
            if (incorporatePrimers)
            {
                char[] manufactured = product.ToCharArray();
                f.Sequence.CopyTo(0, manufactured, 0, f.Sequence.Length);
                string reverseProduct = ReverseComplement(r.Sequence);
                reverseProduct.CopyTo(0, manufactured, manufactured.Length - reverseProduct.Length, reverseProduct.Length);
                product = new string(manufactured);
            }
            AddRepeatedBindingWarning(f, template);
            AddRepeatedBindingWarning(r, template);
            PrimerPair pair = new PrimerPair { Rank = rank, Forward = f, Reverse = r,
                AmpliconStart = f.Start, AmpliconEnd = r.End, AmpliconLength = length,
                AmpliconSequence = product, Score = ScoreFromPenalty(penalty),
                CrossComplement = complement, CrossThreePrime = threePrime };
            if (complement >= 8) pair.Warnings.Add("正反引物存在 " + complement + " nt 连续互补（启发式），建议核验引物二聚体。");
            if (threePrime >= 4) pair.Warnings.Add("正反引物涉及 3′ 端的连续互补达 " + threePrime + " nt（启发式）。");
            foreach (string warning in f.Warnings) pair.Warnings.Add("正向：" + warning);
            foreach (string warning in r.Warnings) pair.Warnings.Add("反向：" + warning);
            if (ContainsAmbiguous(product)) pair.Warnings.Add("扩增片段内部含歧义碱基；两条引物结合区均为确定的 A/C/G/T。");
            double productGc = UnambiguousGc(product);
            if (!Double.IsNaN(productGc) && (productGc < 40 || productGc > 60))
                pair.Warnings.Add("产物确定碱基的 GC 为 " + productGc.ToString("0.0", CultureInfo.InvariantCulture) + "%，偏离常用优选区间 40–60%。");
            return pair;
        }

        private static void AddStructures(List<Candidate> candidates, Dictionary<string, StructureMetrics> cache, CancellationToken cancellation)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if ((i & 63) == 0) cancellation.ThrowIfCancellationRequested();
                Candidate candidate = candidates[i];
                Primer p = candidate.Primer;
                StructureMetrics metrics;
                if (!cache.TryGetValue(p.Sequence, out metrics))
                {
                    metrics = new StructureMetrics();
                    ComplementMetricsCancellable(p.Sequence, p.Sequence, out metrics.Complement, out metrics.ThreePrime, cancellation);
                    metrics.Hairpin = HairpinStemCancellable(p.Sequence, cancellation);
                    metrics.TandemRepeat = TandemRepeatSpan(p.Sequence);
                    cache.Add(p.Sequence, metrics);
                }
                p.Hairpin = metrics.Hairpin;
                p.SelfComplement = metrics.Complement;
                p.SelfThreePrime = metrics.ThreePrime;
                p.TandemRepeat = metrics.TandemRepeat;
                candidate.Penalty += Math.Max(0, metrics.Complement - 3) * 0.6 + Math.Max(0, metrics.ThreePrime - 2) * 1.8 + Math.Max(0, metrics.Hairpin - 3) * 1.2 + Math.Max(0, metrics.TandemRepeat - 8) * 0.5;
                if (metrics.Hairpin >= 5) p.Warnings.Add("可形成最长 " + metrics.Hairpin + " bp 连续发卡茎（至少 3 nt 环，启发式）。");
                if (metrics.Complement >= 8) p.Warnings.Add("自互补最长 " + metrics.Complement + " nt（启发式），建议核验自二聚体。");
                if (metrics.ThreePrime >= 4) p.Warnings.Add("涉及 3′ 端的自互补最长 " + metrics.ThreePrime + " nt（启发式）。");
                if (candidate.Homopolymer == 5) p.Warnings.Add("存在 5 nt 同聚物，建议在实验筛选时留意。");
                if (metrics.TandemRepeat >= 12) p.Warnings.Add("存在跨度 " + metrics.TandemRepeat + " nt 的简单串联重复（2–4 nt 单元，至少重复 3 次），已作启发式降分。");
            }
        }

        private static int TandemRepeatSpan(string sequence)
        {
            int maximum = 0;
            for (int period = 2; period <= 4; period++)
            {
                int run = period;
                for (int i = period; i < sequence.Length; i++)
                {
                    run = sequence[i] == sequence[i - period] ? run + 1 : period;
                    int fullUnits = run / period;
                    if (fullUnits >= 3) maximum = Math.Max(maximum, fullUnits * period);
                }
            }
            return maximum;
        }

        // Align two 5'-to-3' oligos antiparallel. The metric is a longest *contiguous*
        // Watson-Crick match, not a thermodynamic dimer prediction. ThreePrime is
        // the longest such run that contains the terminal base of either oligo.
        private static void ComplementMetrics(string a, string b, out int longest, out int threePrime)
        {
            ComplementMetricsCancellable(a, b, out longest, out threePrime, CancellationToken.None);
        }

        private static void ComplementMetricsCancellable(string a, string b, out int longest, out int threePrime, CancellationToken cancellation)
        {
            longest = 0; threePrime = 0;
            int[] prior = new int[b.Length + 1];
            int[] current = new int[b.Length + 1];
            for (int i = 0; i < a.Length; i++)
            {
                if ((i & 31) == 0) cancellation.ThrowIfCancellationRequested();
                for (int j = b.Length - 1; j >= 0; j--)
                {
                    int run = IsComplement(a[i], b[j]) ? prior[j + 1] + 1 : 0;
                    current[j] = run;
                    if (run > longest) longest = run;
                    if (run > threePrime && (i == a.Length - 1 || j + run == b.Length)) threePrime = run;
                }
                int[] swap = prior; prior = current; current = swap;
            }
        }

        private static int HairpinStem(string sequence)
        {
            return HairpinStemCancellable(sequence, CancellationToken.None);
        }

        private static int HairpinStemCancellable(string sequence, CancellationToken cancellation)
        {
            int n = sequence.Length, maximum = 0;
            int[] prior = new int[n], current = new int[n];
            for (int left = n - 1; left >= 0; left--)
            {
                if ((left & 31) == 0) cancellation.ThrowIfCancellationRequested();
                for (int right = left + 4; right < n; right++)
                {
                    current[right] = 0;
                    if (!IsComplement(sequence[left], sequence[right])) continue;
                    int length = 1;
                    if (right - left >= 6) length += prior[right - 1];
                    current[right] = length;
                    maximum = Math.Max(maximum, length);
                }
                int[] swap = prior; prior = current; current = swap;
            }
            return maximum;
        }

        private static bool IsComplement(char a, char b)
        {
            return (a == 'A' && b == 'T') || (a == 'T' && b == 'A') || (a == 'C' && b == 'G') || (a == 'G' && b == 'C');
        }

        private static List<PairSeed> BuildShortlist(List<Candidate> forward, List<Candidate> reverse, DesignSettings s,
            DesignResult result, Action<int, string> progress, CancellationToken cancellation)
        {
            List<PairSeed> heap = new List<PairSeed>(PairEvaluationLimit);
            long feasible = 0;
            for (int i = 0; i < forward.Count; i++)
            {
                if ((i & 31) == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Report(progress, 35 + i * 18 / forward.Count, "配对候选 " + i + " / " + forward.Count + "…");
                }
                Candidate f = forward[i];
                int firstEnd = f.Primer.Start + s.AmpliconMin - 1;
                int lastEnd = f.Primer.Start + s.AmpliconMax - 1;
                int low = 0, high = reverse.Count;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (reverse[middle].Primer.End < firstEnd) low = middle + 1; else high = middle;
                }
                for (int j = low; j < reverse.Count && reverse[j].Primer.End <= lastEnd; j++)
                {
                    Candidate r = reverse[j];
                    if (r.Primer.Start <= f.Primer.End) continue;
                    feasible++;
                    if ((feasible & 8191) == 0) cancellation.ThrowIfCancellationRequested();
                    int length = r.Primer.End - f.Primer.Start + 1;
                    double penalty = f.Penalty + r.Penalty + PairLengthPenalty(length, s);
                    if (heap.Count == PairEvaluationLimit && penalty > heap[0].Penalty) continue;
                    PairSeed seed = new PairSeed { Forward = f, Reverse = r, Penalty = penalty };
                    HeapKeepBest(heap, seed);
                }
            }
            if (feasible > PairEvaluationLimit)
            {
                result.SearchTruncated = true;
                result.Notes.Add("符合位置与长度条件的候选组合共 " + feasible + " 对；按单引物结构与产物长度初评，仅对前 " + PairEvaluationLimit + " 对进一步计算交叉互补。此为有限搜索，未保证全局最优。");
            }
            return heap;
        }

        private static void HeapKeepBest(List<PairSeed> heap, PairSeed seed)
        {
            if (heap.Count < PairEvaluationLimit)
            {
                heap.Add(seed);
                int child = heap.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (CompareSeed(heap[parent], heap[child]) >= 0) break;
                    PairSeed swap = heap[parent]; heap[parent] = heap[child]; heap[child] = swap;
                    child = parent;
                }
                return;
            }
            if (CompareSeed(seed, heap[0]) >= 0) return;
            heap[0] = seed;
            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= heap.Count) break;
                int right = left + 1, worse = left;
                if (right < heap.Count && CompareSeed(heap[right], heap[left]) > 0) worse = right;
                if (CompareSeed(heap[index], heap[worse]) >= 0) break;
                PairSeed swap = heap[index]; heap[index] = heap[worse]; heap[worse] = swap;
                index = worse;
            }
        }

        private static int CompareCandidate(Candidate a, Candidate b)
        {
            int score = a.Penalty.CompareTo(b.Penalty);
            if (score != 0) return score;
            int start = a.Primer.Start.CompareTo(b.Primer.Start);
            if (start != 0) return start;
            return a.Primer.End.CompareTo(b.Primer.End);
        }

        private static int ComparePosition(Candidate a, Candidate b, bool reverse)
        {
            int anchor = (reverse ? a.Primer.End : a.Primer.Start).CompareTo(reverse ? b.Primer.End : b.Primer.Start);
            return anchor != 0 ? anchor : CompareCandidate(a, b);
        }

        private static int CompareSeed(PairSeed a, PairSeed b)
        {
            int score = a.Penalty.CompareTo(b.Penalty);
            if (score != 0) return score;
            int forwardStart = a.Forward.Primer.Start.CompareTo(b.Forward.Primer.Start);
            if (forwardStart != 0) return forwardStart;
            int reverseEnd = a.Reverse.Primer.End.CompareTo(b.Reverse.Primer.End);
            if (reverseEnd != 0) return reverseEnd;
            int forwardEnd = a.Forward.Primer.End.CompareTo(b.Forward.Primer.End);
            if (forwardEnd != 0) return forwardEnd;
            return a.Reverse.Primer.Start.CompareTo(b.Reverse.Primer.Start);
        }

        private static int CompareEvaluated(EvaluatedPair a, EvaluatedPair b)
        {
            int score = a.Penalty.CompareTo(b.Penalty);
            return score != 0 ? score : CompareSeed(a.Seed, b.Seed);
        }

        private static void AddRepeatedBindingWarning(Primer primer, string template)
        {
            string opposite = ReverseComplement(primer.Sequence);
            int occurrences = CountOccurrences(template, primer.Sequence);
            if (opposite != primer.Sequence) occurrences += CountOccurrences(template, opposite);
            if (occurrences > 1)
            {
                string warning = "引物及其反向互补序列在输入模板内共有 " + occurrences + " 个完全匹配位置，存在重复结合风险；需继续验证特异性。";
                if (!primer.Warnings.Contains(warning)) primer.Warnings.Add(warning);
            }
        }

        private static int CountOccurrences(string template, string value)
        {
            int count = 0, start = 0;
            while (start <= template.Length - value.Length)
            {
                int found = template.IndexOf(value, start, StringComparison.Ordinal);
                if (found < 0) break;
                count++;
                start = found + 1;
            }
            return count;
        }

        private static bool ContainsAmbiguous(string sequence)
        {
            foreach (char c in sequence) if (c != 'A' && c != 'C' && c != 'G' && c != 'T') return true;
            return false;
        }

        private static double UnambiguousGc(string sequence)
        {
            int gc = 0, count = 0;
            foreach (char c in sequence)
            {
                if (c == 'G' || c == 'C') { gc++; count++; }
                else if (c == 'A' || c == 'T') count++;
            }
            return count == 0 ? Double.NaN : 100.0 * gc / count;
        }

        private static void Report(Action<int, string> progress, int percent, string message)
        {
            if (progress != null) progress(percent, message);
        }
    }
}
