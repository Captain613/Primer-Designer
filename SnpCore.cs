using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace RpaDesigner
{
    public sealed class SnpInput
    {
        public ParsedSequence Reference, Alternate;
        public string AnnotatedSequence;
        public int Position;
        public char ReferenceAllele, AlternateAllele;
    }

    public static class SnpParser
    {
        public static SnpInput Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入含一个 [A>C] 格式 SNP 标记的 DNA 序列。");
            if (text.Length > 1000000) throw new ArgumentException("原始输入不得超过 1,000,000 个字符。");
            StringBuilder reference = new StringBuilder(), alternate = new StringBuilder();
            int markerCount = 0, position = 0, bases = 0;
            char refAllele = '\0', altAllele = '\0';
            string[] lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith(">", StringComparison.Ordinal))
                {
                    // FASTA metadata is not sequence and must never supply a SNP.
                    reference.AppendLine(line); alternate.AppendLine(line); continue;
                }
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '[')
                    {
                        if (i + 4 >= line.Length || line[i + 2] != '>' || line[i + 4] != ']')
                            throw new ArgumentException("SNP 标记须严格使用 [A>C] 格式，仅支持单碱基替换，标记内不加空格或换行。");
                        char a = Char.ToUpperInvariant(line[i + 1]), b = Char.ToUpperInvariant(line[i + 3]);
                        if ("ACGT".IndexOf(a) < 0 || "ACGT".IndexOf(b) < 0 || a == b)
                            throw new ArgumentException("SNP 两个等位碱基必须是不同的 A/C/G/T，例如 [A>C]；不支持相同碱基、歧义碱基或插入缺失。");
                        if (++markerCount > 1) throw new ArgumentException("SNP 模式一次只支持一个 [A>C] 格式位点。");
                        refAllele = a; altAllele = b; position = bases + 1;
                        reference.Append(a); alternate.Append(b); bases++; i += 4;
                    }
                    else
                    {
                        if (c == ']' || c == '>') throw new ArgumentException("检测到不完整或无效的 SNP 标记，请使用 [A>C] 格式。");
                        reference.Append(c); alternate.Append(c);
                        if (!Char.IsWhiteSpace(c) && !(c >= '0' && c <= '9')) bases++;
                    }
                }
                reference.AppendLine(); alternate.AppendLine();
            }
            if (markerCount != 1) throw new ArgumentException("序列正文必须含且仅含一个 [A>C] 格式的 SNP 标记；FASTA 标题中的标记不计入。");
            ParsedSequence r = SequenceParser.Parse(reference.ToString()), aseq = SequenceParser.Parse(alternate.ToString());
            return new SnpInput { Reference = r, Alternate = aseq, Position = position,
                ReferenceAllele = refAllele, AlternateAllele = altAllele,
                AnnotatedSequence = r.Sequence.Substring(0, position - 1) + "[" + refAllele + ">" + altAllele + "]" + r.Sequence.Substring(position) };
        }
    }

    public sealed class SnpDesignSettings
    {
        public DesignSettings Base = new DesignSettings();
        // Terminal SNP is position 1 from the 3' end; 0 means no added mismatch.
        public int ExtraMismatchFromThreePrime = 0;
    }

    public sealed class SnpPrimerSet
    {
        public int Rank;
        public Primer ReferenceForward, AlternateForward, CommonReverse, ControlForward;
        public PrimerPair ReferencePair, AlternatePair, ControlPair;
        public string AlternateControlAmpliconSequence;
        public double Score;
        // One-based template coordinate, or zero in the unmodified baseline.
        public int ExtraMismatchPosition;
        public char ExtraMismatchTemplateBase, ExtraMismatchPrimerBase;
        public List<string> Notes = new List<string>();
    }

    public sealed class SnpDesignResult
    {
        public SnpInput Input;
        public SnpDesignSettings Settings;
        public List<SnpPrimerSet> Sets = new List<SnpPrimerSet>();
        public List<string> Notes = new List<string>();
        public bool SearchTruncated;
    }

    public static class SnpDesignEngine
    {
        private const int FlankCandidateLimit = 1000, SetEvaluationLimit = 12000, ControlsPerSeed = 6;

        private sealed class AlleleChoice
        {
            public DesignEngine.Candidate Reference, Alternate;
            public char EngineeredBase;
        }

        private sealed class ControlChoice
        {
            public DesignEngine.Candidate Candidate;
            public double Penalty;
            public double? EvaluatedPenalty;
        }

        private sealed class ReverseChoice
        {
            public DesignEngine.Candidate Candidate;
            public List<ControlChoice> Controls = new List<ControlChoice>();
        }

        private sealed class SetSeed
        {
            public AlleleChoice Alleles;
            public ReverseChoice Reverse;
            public ControlChoice Control;
            public double Penalty;
        }

        public static SnpDesignResult Design(SnpInput input, SnpDesignSettings settings,
            Action<int, string> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (input == null) throw new ArgumentNullException("input");
            if (settings == null || settings.Base == null) throw new ArgumentNullException("settings");
            SnpInput normalized = Normalize(input);
            DesignSettings s = DesignEngine.CopyAndValidate(settings.Base, normalized.Reference.Sequence.Length);
            if (s.TargetStart != 0 || s.TargetEnd != 0)
                throw new ArgumentException("SNP 模式使用 [A>C] 标记指定位置，请将普通扩增靶区起止坐标设为 0。");
            int offset = settings.ExtraMismatchFromThreePrime;
            if (offset != 0 && offset != 2 && offset != 3)
                throw new ArgumentException("额外错配仅支持无额外错配（0）、3′ 倒数第 2 或第 3 位；SNP 末端本身计为第 1 位。");
            SnpDesignResult result = new SnpDesignResult { Input = normalized,
                Settings = new SnpDesignSettings { Base = s, ExtraMismatchFromThreePrime = offset } };
            result.Notes.AddRange(normalized.Reference.Warnings);
            result.Notes.Add("每套含参考等位正向、替代等位正向、共用反向和对照正向引物。两条等位正向的 3′ 末端均落在 SNP；对照正向完全位于 SNP 上游，共用反向完全位于下游。");
            result.Notes.Add("四条引物用于三个分开的反应：参考等位 F + 共用 R、替代等位 F + 共用 R、对照 F + 共用 R。本程序未设计四条引物同管的多重反应。");
            result.Notes.Add("3′ 单碱基错配不能保证 RPA 的等位选择性；这些是实验筛选候选。评分仅按序列组成、连续互补和长度启发式排序，不是 SNP 特异性概率或扩增成功率，不输出基因型判定。");
            result.Notes.Add("所有引物按 5′→3′ 输出，坐标为清理后的模板正链 1-based 闭区间。[A>C] 在坐标中占一个碱基。对照引物对两种等位模板的结合序列相同。");
            result.Notes.Add("每条引物均执行所设长度与 GC 范围和同聚物不超过 5 nt 的筛选；三种产物均须满足所设长度范围。单引物结构和各 F/R 二聚体指标复用普通 RPA 模式算法。");
            result.Notes.Add("产物长度评分仅依据两种等位扩增产物，与偏好长度的偏差不用于对照产物评分或优选。对照产物仍执行最短/最长硬筛选，对照引物的组成与结构指标仍参与评分。");
            result.Notes.Add("连续互补、发卡和简单串联重复仅为序列启发式，不包含自由能、鼓包或热力学错配模型。Tm 采用 64.9 + 41 × (GC数 − 16.4) / 长度，仅作参考，不参与筛选或排序。");
            result.Notes.Add("未执行全基因组、近似匹配或数据库特异性筛查；模板内重复完全匹配提示也不能证明等位选择性。");
            if(s.PrimerMin<30 || s.PrimerMax>35)result.Notes.Add("当前引物长度超出默认 30–35 nt，属于自定义探索范围。");
            if(s.AmpliconMax>500)result.Notes.Add("当前允许产物超过 500 bp，超出常用 RPA 设计范围，需单独验证长产物是否适用。");
            result.Notes.Add(offset == 0 ? "当前为末端 SNP 匹配基线：不人为改变 SNP 上游碱基。"
                : "当前探索 3′ 倒数第 " + offset + " 位额外错配；穷举该位置另外三种 A/C/G/T，重新计算实际合成引物的全部指标。这些人为错配可能降低目标扩增，未预设某一种替换有效。");
            result.Notes.Add("产物序列表示由所列引物形成的最终扩增产物；人工错配会写入等位产物。对照分别报告参考与替代等位模板产物。");
            Report(progress, 3, "解析 SNP 并生成末端等位引物…");
            List<AlleleChoice> alleles = GenerateAlleles(normalized, s, offset, result, cancellation);
            bool budgetedStructures = s.PrimerMaxUnlimited && s.PrimerMax > 128;
            if (budgetedStructures) alleles = LimitAlleleStructures(alleles, result);
            if (alleles.Count == 0)
            {
                result.Notes.Add("没有满足长度、GC、歧义碱基和同聚物条件的成对 SNP 正向引物。请补充 SNP 上游序列或调整筛选范围。");
                Report(progress, 100, "未找到 SNP 候选。"); return result;
            }
            string template = normalized.Reference.Sequence;
            int p = normalized.Position, earliest = Int32.MaxValue, latest = 0;
            foreach (AlleleChoice a in alleles) { earliest = Math.Min(earliest, a.Reference.Primer.Start); latest = Math.Max(latest, a.Reference.Primer.Start); }
            int reverseEndMin = Math.Max(p + s.PrimerMin, earliest + s.AmpliconMin - 1);
            int reverseEndMax = Math.Min(template.Length, latest + s.AmpliconMax - 1);
            bool sampledLengths;
            int[] lengths = DesignEngine.SamplePrimerLengths(s.PrimerMin, Math.Min(s.PrimerMax, template.Length), out sampledLengths);
            int reversePruned = 0, controlPruned = 0;
            List<DesignEngine.Candidate> reverse = new List<DesignEngine.Candidate>();
            foreach (int end in DesignEngine.SamplePositions(reverseEndMin, reverseEndMax, sampledLengths ? 512 : Int32.MaxValue))
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (int len in lengths)
                {
                    int start = end - len + 1;
                    if (start <= p) break;
                    DesignEngine.Candidate candidate = DesignEngine.CompositionCandidate(DesignEngine.ReverseComplement(template.Substring(start - 1, len)), start, s);
                    if (candidate != null) reverse.Add(candidate);
                    if (sampledLengths && reverse.Count >= FlankCandidateLimit * 2)
                    {
                        int before = reverse.Count;
                        reverse = Prune(reverse, true, null);
                        reversePruned += before - reverse.Count;
                    }
                }
            }
            List<DesignEngine.Candidate> controls = new List<DesignEngine.Candidate>();
            int firstControl = Math.Max(1, reverseEndMin - s.AmpliconMax + 1);
            foreach (int start in DesignEngine.SamplePositions(firstControl, latest - 1, sampledLengths ? 512 : Int32.MaxValue))
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (int len in lengths)
                {
                    if (start + len - 1 >= p) break;
                    DesignEngine.Candidate candidate = DesignEngine.CompositionCandidate(template.Substring(start - 1, len), start, s);
                    if (candidate != null) controls.Add(candidate);
                    if (sampledLengths && controls.Count >= FlankCandidateLimit * 2)
                    {
                        int before = controls.Count;
                        controls = Prune(controls, false, null);
                        controlPruned += before - controls.Count;
                    }
                }
            }
            if (sampledLengths)
            {
                result.SearchTruncated = true;
                result.Notes.Add("侧翼引物长度范围较宽：在有效区间保留 60 nt 及以下全部长度，并均匀抽样更长的 32 个长度（含端点），共 " + lengths.Length + " 种；沿各侧翼范围均匀抽样至多 512 个锚点（含端点）。扫描中按既有位置分箱初评另舍弃共用反向 " + reversePruned + "、对照正向 " + controlPruned + " 条。未穷尽组合，可能遗漏有效引物。");
            }
            reverse = Prune(reverse, true, result); controls = Prune(controls, false, result);
            if (budgetedStructures)
            {
                int removedReverse, removedControls;
                reverse = DesignEngine.LimitStructureCandidates(reverse, 100000000L, out removedReverse);
                controls = DesignEngine.LimitStructureCandidates(controls, 100000000L, out removedControls);
                if (removedReverse + removedControls > 0)
                {
                    result.SearchTruncated = true;
                    result.Notes.Add("无引物长度上限时，共用反向与对照正向各按初筛排序采用 1 亿平方长度结构运算预算（每类至少评估一条），另舍弃 " + removedReverse + " / " + removedControls + " 条；这是计算预算，不是长度限制，可能遗漏有效组合。");
                }
            }
            List<DesignEngine.Candidate> all = new List<DesignEngine.Candidate>(reverse);
            all.AddRange(controls);
            foreach (AlleleChoice a in alleles) { all.Add(a.Reference); all.Add(a.Alternate); }
            Report(progress, 15, "计算实际合成引物的结构指标…");
            DesignEngine.AddStructures(all, cancellation);
            List<ReverseChoice> reverseChoices = BuildReverseChoices(reverse, controls, s, cancellation);
            List<SetSeed> seeds = new List<SetSeed>();
            long feasible = 0;
            foreach (AlleleChoice a in alleles)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (ReverseChoice r in reverseChoices)
                {
                    int length = r.Candidate.Primer.End - a.Reference.Primer.Start + 1;
                    if (length < s.AmpliconMin || length > s.AmpliconMax) continue;
                    ControlChoice control = null;
                    foreach (ControlChoice c in r.Controls)
                        if (c.Candidate.Primer.Start < a.Reference.Primer.Start) { control = c; break; }
                    if (control == null) continue;
                    feasible++;
                    // Average reaction quality separately from the allele-only length preference.
                    // Both allele products have the same coordinates and therefore the same length.
                    double penalty = (a.Reference.Penalty + a.Alternate.Penalty + 2 * r.Candidate.Penalty
                        + control.Penalty) / 3.0 + DesignEngine.PairLengthPenalty(length, s);
                    KeepSeed(seeds, new SetSeed { Alleles = a, Reverse = r, Control = control, Penalty = penalty });
                }
            }
            if (feasible > SetEvaluationLimit)
            {
                result.SearchTruncated = true;
                result.Notes.Add("可行等位引物/共用反向组合共 " + feasible + " 套；按单引物指标和等位产物长度初筛，仅对前 " + SetEvaluationLimit + " 套计算成对互补，可能遗漏更优方案。");
            }
            seeds.Sort(CompareSeed);
            if (budgetedStructures) LimitPairStructures(seeds, result);
            bool controlsLimited = false;
            for (int i = 0; i < seeds.Count; i++)
            {
                if ((i & 63) == 0) { cancellation.ThrowIfCancellationRequested(); Report(progress, 35 + i * 55 / Math.Max(1, seeds.Count), "评估 SNP 引物组合 " + i + " / " + seeds.Count + "…"); }
                SetSeed seed = seeds[i];
                int valid = 0, complement, threePrime;
                ControlChoice best = null;
                foreach (ControlChoice control in seed.Reverse.Controls)
                {
                    if (control.Candidate.Primer.Start >= seed.Alleles.Reference.Primer.Start) continue;
                    if (++valid > ControlsPerSeed) { controlsLimited = true; break; }
                    if (!control.EvaluatedPenalty.HasValue)
                        control.EvaluatedPenalty = control.Penalty + DesignEngine.PairStructurePenalty(control.Candidate.Primer, seed.Reverse.Candidate.Primer, out complement, out threePrime, cancellation);
                    if (best == null || control.EvaluatedPenalty.Value < best.EvaluatedPenalty.Value) best = control;
                }
                seed.Control = best;
                double lengthPenalty = DesignEngine.PairLengthPenalty(seed.Reverse.Candidate.Primer.End - seed.Alleles.Reference.Primer.Start + 1, s);
                double refPenalty = seed.Alleles.Reference.Penalty + seed.Reverse.Candidate.Penalty
                    + DesignEngine.PairStructurePenalty(seed.Alleles.Reference.Primer, seed.Reverse.Candidate.Primer, out complement, out threePrime, cancellation);
                double altPenalty = seed.Alleles.Alternate.Penalty + seed.Reverse.Candidate.Penalty
                    + DesignEngine.PairStructurePenalty(seed.Alleles.Alternate.Primer, seed.Reverse.Candidate.Primer, out complement, out threePrime, cancellation);
                seed.Penalty = (refPenalty + altPenalty + best.EvaluatedPenalty.Value) / 3.0 + lengthPenalty;
            }
            if (controlsLimited)
            {
                result.SearchTruncated = true;
                result.Notes.Add("每个等位正向/共用反向组合仅对按初评排列的前 " + ControlsPerSeed + " 条合格对照正向进一步计算二聚体，并保留其中评分最高的一条；未穷尽对照组合。");
            }
            seeds.Sort(CompareSeed);
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            // Prefer spatially distinct common reverses, then fill remaining slots
            // with other unique oligo sets instead of silently losing SNP lengths.
            for (int pass = 0; pass < 2 && result.Sets.Count < s.MaxPairs; pass++)
                foreach (SetSeed seed in seeds)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string key = seed.Alleles.Reference.Primer.Sequence + "/" + seed.Alleles.Alternate.Primer.Sequence + "/" + seed.Reverse.Candidate.Primer.Sequence + "/" + seed.Control.Candidate.Primer.Sequence;
                    if (used.Contains(key)) continue;
                    bool nearby = false;
                    if (pass == 0)
                        foreach (SnpPrimerSet prior in result.Sets)
                            if (Math.Abs(prior.CommonReverse.End - seed.Reverse.Candidate.Primer.End) < 6 && prior.ExtraMismatchPrimerBase == seed.Alleles.EngineeredBase) { nearby = true; break; }
                    if (nearby) continue;
                    used.Add(key);
                    result.Sets.Add(Materialize(seed, normalized, result.Settings, result.Sets.Count + 1, cancellation));
                    if (result.Sets.Count >= s.MaxPairs) break;
                }
            if (result.Sets.Count == 0) result.Notes.Add("未找到同时满足四条引物及三个产物约束的方案。SNP 两侧需有足够序列；对照 F 必须起始于等位 F 上游，且对照产物仍须落在长度范围内。");
            else if (result.Sets.Count < s.MaxPairs) result.Notes.Add("当前约束和去重后得到 " + result.Sets.Count + " 套候选，少于请求的 " + s.MaxPairs + " 套。");
            result.Notes.Add("优先保留共用反向末端相距至少 6 nt 的方案；不足请求数量时补充其他不同引物组合。组总惩罚为三个分开反应的非产物长度惩罚均值，加上两种等位产物的长度惩罚均值；对照产物长度不计入评分。");
            Report(progress, 100, "SNP 设计完成：" + result.Sets.Count + " 套候选。");
            return result;
        }

        private static SnpInput Normalize(SnpInput input)
        {
            if (input.Reference == null || input.Alternate == null) throw new ArgumentException("缺少 SNP 参考或替代模板，请重新解析输入。");
            ParsedSequence reference = SequenceParser.Parse(input.Reference.Sequence), alternate = SequenceParser.Parse(input.Alternate.Sequence);
            int p = input.Position;
            if (p < 1 || p > reference.Sequence.Length || reference.Sequence.Length != alternate.Sequence.Length
                || "ACGT".IndexOf(input.ReferenceAllele) < 0 || "ACGT".IndexOf(input.AlternateAllele) < 0 || input.ReferenceAllele == input.AlternateAllele
                || reference.Sequence[p - 1] != input.ReferenceAllele || alternate.Sequence[p - 1] != input.AlternateAllele)
                throw new ArgumentException("SNP 坐标、等位碱基或模板不一致，请重新解析 [A>C] 输入。");
            for (int i = 0; i < reference.Sequence.Length; i++)
                if (i != p - 1 && reference.Sequence[i] != alternate.Sequence[i]) throw new ArgumentException("两条等位模板除指定 SNP 外必须相同。");
            reference.Name = input.Reference.Name; alternate.Name = input.Alternate.Name;
            if (input.Reference.Warnings != null)
                foreach (string w in input.Reference.Warnings) if (!reference.Warnings.Contains(w)) reference.Warnings.Add(w);
            if (input.Alternate.Warnings != null)
                foreach (string w in input.Alternate.Warnings) if (!alternate.Warnings.Contains(w)) alternate.Warnings.Add(w);
            return new SnpInput { Reference = reference, Alternate = alternate, Position = p,
                ReferenceAllele = input.ReferenceAllele, AlternateAllele = input.AlternateAllele,
                AnnotatedSequence = reference.Sequence.Substring(0, p - 1) + "[" + input.ReferenceAllele + ">" + input.AlternateAllele + "]" + reference.Sequence.Substring(p) };
        }

        private static List<AlleleChoice> GenerateAlleles(SnpInput input, DesignSettings s, int offset, SnpDesignResult designResult, CancellationToken cancellation)
        {
            List<AlleleChoice> result = new List<AlleleChoice>();
            bool sampled;
            int[] lengths = DesignEngine.SamplePrimerLengths(Math.Max(s.PrimerMin, offset), Math.Min(s.PrimerMax, input.Position), out sampled);
            if (sampled)
            {
                designResult.SearchTruncated = true;
                designResult.Notes.Add("SNP 等位正向长度范围较宽：在有效区间保留 60 nt 及以下全部长度，并均匀抽样更长的 32 个长度（含端点），本次评估 " + lengths.Length + " 种长度；未穷尽全部长度，可能遗漏有效引物。");
            }
            foreach (int length in lengths)
            {
                cancellation.ThrowIfCancellationRequested();
                int start = input.Position - length + 1;
                string refOligo = input.Reference.Sequence.Substring(start - 1, length), altOligo = input.Alternate.Sequence.Substring(start - 1, length);
                // Unknown flanking bases cannot be rescued by inventing a mismatch.
                bool ambiguous = false;
                foreach (char c in refOligo) if ("ACGT".IndexOf(c) < 0) { ambiguous = true; break; }
                if (ambiguous) continue;
                string replacements = offset == 0 ? "-" : "ACGT";
                foreach (char replacement in replacements)
                {
                    if (offset != 0 && replacement == refOligo[length - offset]) continue;
                    char[] r = refOligo.ToCharArray(), a = altOligo.ToCharArray();
                    if (offset != 0) { r[length - offset] = replacement; a[length - offset] = replacement; }
                    DesignEngine.Candidate reference = DesignEngine.CompositionCandidate(new string(r), start, s);
                    DesignEngine.Candidate alternate = DesignEngine.CompositionCandidate(new string(a), start, s);
                    if (reference != null && alternate != null)
                        result.Add(new AlleleChoice { Reference = reference, Alternate = alternate, EngineeredBase = offset == 0 ? '\0' : replacement });
                }
            }
            return result;
        }

        private static List<AlleleChoice> LimitAlleleStructures(List<AlleleChoice> source, SnpDesignResult result)
        {
            source.Sort(delegate(AlleleChoice a, AlleleChoice b)
            {
                int order = (a.Reference.Penalty + a.Alternate.Penalty).CompareTo(b.Reference.Penalty + b.Alternate.Penalty);
                if (order != 0) return order;
                order = a.Reference.Primer.Start.CompareTo(b.Reference.Primer.Start);
                return order != 0 ? order : a.EngineeredBase.CompareTo(b.EngineeredBase);
            });
            List<AlleleChoice> kept = new List<AlleleChoice>(); long work = 0;
            foreach (AlleleChoice allele in source)
            {
                long cost = 4L * allele.Reference.Primer.Sequence.Length * allele.Reference.Primer.Sequence.Length;
                if (kept.Count > 0 && work + cost > 100000000L) continue;
                kept.Add(allele); work += cost;
            }
            if (kept.Count < source.Count)
            {
                result.SearchTruncated = true;
                result.Notes.Add("无引物长度上限时，等位正向按初筛排序采用 1 亿平方长度结构运算预算（至少评估一组等位引物），另舍弃 " + (source.Count - kept.Count) + " 组；这是计算预算，不是长度限制，可能遗漏有效组合。");
            }
            return kept;
        }

        private static void LimitPairStructures(List<SetSeed> seeds, SnpDesignResult result)
        {
            long work = 0;
            for (int i = 0; i < seeds.Count; i++)
            {
                SetSeed seed = seeds[i];
                long reverseLength = seed.Reverse.Candidate.Primer.Sequence.Length;
                long cost = reverseLength * (seed.Alleles.Reference.Primer.Sequence.Length + seed.Alleles.Alternate.Primer.Sequence.Length);
                int controls = 0;
                foreach (ControlChoice control in seed.Reverse.Controls)
                {
                    if (control.Candidate.Primer.Start >= seed.Alleles.Reference.Primer.Start) continue;
                    if (++controls > ControlsPerSeed) break;
                    cost += reverseLength * control.Candidate.Primer.Sequence.Length;
                }
                if (i > 0 && work + cost > 150000000L)
                {
                    result.SearchTruncated = true;
                    result.Notes.Add("无引物长度上限时，组合交叉互补按初筛排序采用 1.5 亿长度乘积估算运算预算（至少评估一套），本次评估 " + i + " / " + seeds.Count + " 套，可能遗漏有效组合。");
                    seeds.RemoveRange(i, seeds.Count - i); break;
                }
                work += cost;
            }
        }

        private static List<DesignEngine.Candidate> Prune(List<DesignEngine.Candidate> source, bool reverse, SnpDesignResult result)
        {
            if (source.Count <= FlankCandidateLimit) return source;
            SortedDictionary<int, List<DesignEngine.Candidate>> bins = new SortedDictionary<int, List<DesignEngine.Candidate>>();
            foreach (DesignEngine.Candidate c in source)
            {
                int key = ((reverse ? c.Primer.End : c.Primer.Start) - 1) / 20;
                List<DesignEngine.Candidate> bin;
                if (!bins.TryGetValue(key, out bin)) { bin = new List<DesignEngine.Candidate>(); bins.Add(key, bin); }
                bin.Add(c);
            }
            foreach (List<DesignEngine.Candidate> bin in bins.Values) bin.Sort(CompareCandidate);
            List<DesignEngine.Candidate> kept = new List<DesignEngine.Candidate>();
            for (int depth = 0; kept.Count < FlankCandidateLimit; depth++)
            {
                bool added = false;
                foreach (List<DesignEngine.Candidate> bin in bins.Values)
                    if (depth < bin.Count) { kept.Add(bin[depth]); added = true; if (kept.Count == FlankCandidateLimit) break; }
                if (!added) break;
            }
            if (result != null)
            {
                result.SearchTruncated = true;
                result.Notes.Add((reverse ? "共用反向" : "对照正向") + "候选 " + source.Count + "→" + kept.Count + "，按 20 nt 分箱轮流保留初评较优候选；属于有限搜索，可能遗漏其他有效组合。");
            }
            return kept;
        }

        private static List<ReverseChoice> BuildReverseChoices(List<DesignEngine.Candidate> reverse, List<DesignEngine.Candidate> controls,
            DesignSettings s, CancellationToken cancellation)
        {
            List<ReverseChoice> result = new List<ReverseChoice>();
            foreach (DesignEngine.Candidate r in reverse)
            {
                cancellation.ThrowIfCancellationRequested();
                ReverseChoice choice = new ReverseChoice { Candidate = r };
                foreach (DesignEngine.Candidate c in controls)
                {
                    int length = r.Primer.End - c.Primer.Start + 1;
                    if (length < s.AmpliconMin || length > s.AmpliconMax || c.Primer.End >= r.Primer.Start) continue;
                    choice.Controls.Add(new ControlChoice { Candidate = c,
                        Penalty = c.Penalty + r.Penalty });
                }
                choice.Controls.Sort(delegate(ControlChoice a, ControlChoice b)
                {
                    int score = a.Penalty.CompareTo(b.Penalty);
                    return score != 0 ? score : CompareCandidate(a.Candidate, b.Candidate);
                });
                if (choice.Controls.Count > 0) result.Add(choice);
            }
            return result;
        }

        private static int CompareCandidate(DesignEngine.Candidate a, DesignEngine.Candidate b)
        {
            int score = a.Penalty.CompareTo(b.Penalty);
            if (score != 0) return score;
            int start = a.Primer.Start.CompareTo(b.Primer.Start);
            return start != 0 ? start : a.Primer.End.CompareTo(b.Primer.End);
        }

        private static int CompareSeed(SetSeed a, SetSeed b)
        {
            int score = a.Penalty.CompareTo(b.Penalty);
            if (score != 0) return score;
            int start = a.Alleles.Reference.Primer.Start.CompareTo(b.Alleles.Reference.Primer.Start);
            if (start != 0) return start;
            int reverse = a.Reverse.Candidate.Primer.End.CompareTo(b.Reverse.Candidate.Primer.End);
            if (reverse != 0) return reverse;
            int sequence = String.CompareOrdinal(a.Alleles.Reference.Primer.Sequence, b.Alleles.Reference.Primer.Sequence);
            if (sequence != 0) return sequence;
            return a.Reverse.Candidate.Primer.Start.CompareTo(b.Reverse.Candidate.Primer.Start);
        }

        private static void KeepSeed(List<SetSeed> heap, SetSeed seed)
        {
            if (heap.Count < SetEvaluationLimit)
            {
                heap.Add(seed);
                int child = heap.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (CompareSeed(heap[parent], heap[child]) >= 0) break;
                    SetSeed swap = heap[parent]; heap[parent] = heap[child]; heap[child] = swap; child = parent;
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
                SetSeed swap = heap[index]; heap[index] = heap[worse]; heap[worse] = swap; index = worse;
            }
        }

        private static SnpPrimerSet Materialize(SetSeed seed, SnpInput input, SnpDesignSettings settings, int rank, CancellationToken cancellation)
        {
            SnpPrimerSet set = new SnpPrimerSet { Rank = rank,
                ReferenceForward = seed.Alleles.Reference.Primer, AlternateForward = seed.Alleles.Alternate.Primer,
                CommonReverse = seed.Reverse.Candidate.Primer, ControlForward = seed.Control.Candidate.Primer,
                Score = DesignEngine.ScoreFromPenalty(seed.Penalty) };
            set.ReferencePair = DesignEngine.MaterializePair(seed.Alleles.Reference, seed.Reverse.Candidate, input.Reference.Sequence, settings.Base, rank, true, true, cancellation);
            set.AlternatePair = DesignEngine.MaterializePair(seed.Alleles.Alternate, seed.Reverse.Candidate, input.Alternate.Sequence, settings.Base, rank, true, true, cancellation);
            set.ControlPair = DesignEngine.MaterializePair(seed.Control.Candidate, seed.Reverse.Candidate, input.Reference.Sequence, settings.Base, rank, true, false, cancellation);
            set.AlternateControlAmpliconSequence = input.Alternate.Sequence.Substring(set.ControlPair.AmpliconStart - 1, set.ControlPair.AmpliconLength);
            if (settings.ExtraMismatchFromThreePrime != 0)
            {
                set.ExtraMismatchPosition = input.Position - settings.ExtraMismatchFromThreePrime + 1;
                set.ExtraMismatchTemplateBase = input.Reference.Sequence[set.ExtraMismatchPosition - 1];
                set.ExtraMismatchPrimerBase = seed.Alleles.EngineeredBase;
                string note = "人为错配位点 " + set.ExtraMismatchPosition + "：模板 " + set.ExtraMismatchTemplateBase + " → 合成引物 " + set.ExtraMismatchPrimerBase + "；两条等位正向使用同一改变，最终等位扩增产物也包含该改变。";
                set.Notes.Add(note); set.ReferencePair.Warnings.Add(note); set.AlternatePair.Warnings.Add(note);
            }
            set.Notes.Add("对照引物不区分这一个 SNP 的两种等位；不表示覆盖未知的其他变异或所有样本背景。");
            return set;
        }

        private static void Report(Action<int, string> progress, int percent, string message)
        {
            if (progress != null) progress(percent, message);
        }
    }
}
