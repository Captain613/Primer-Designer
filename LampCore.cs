using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace RpaDesigner
{
    public sealed class LampTmRange
    {
        public double Min, Max;
        public bool MinUnlimited, MaxUnlimited;
        public LampTmRange(double minimum, double maximum) { Min = minimum; Max = maximum; }
    }

    public sealed class LampDesignSettings
    {
        // Broad candidate-search bounds. Ren 2019's TP53 example includes
        // a 17-nt B3 and F2 reference Tm above 65 C in this model.
        public int RegionMin = 17, RegionMax = 30;
        public int SpanMin = 110, SpanMax = 350, MaxSets = 10;
        public double GcMin = 30, GcMax = 75;
        public double AnnealTmMin = 52, AnnealTmMax = 68, InnerTmMin = 58, InnerTmMax = 72;
        public LampTmRange F3Tm, B3Tm, F2Tm, B2Tm, F1cTm, B1cTm, LFTm, LBTm;
        public int CoreSpanMin = 100, CoreSpanMax = 220;
        public bool CoreSpanMinUnlimited, CoreSpanMaxUnlimited;
        public bool RegionMinUnlimited, RegionMaxUnlimited, SpanMinUnlimited, SpanMaxUnlimited;
        public bool GcMinUnlimited, GcMaxUnlimited, AnnealTmMinUnlimited, AnnealTmMaxUnlimited;
        public bool InnerTmMinUnlimited, InnerTmMaxUnlimited;
        public bool IncludeLoops = true;
        public string SnpMethod = "AS-LAMP";
        public string SnpOrientation = "Auto";
        public int ExtraMismatchFromThreePrime = 0;
        // Du et al. supplement compares RNA + 4/5/6/7-nt DNA tails;
        // the 5-nt BIP-b construction is the initial engineering choice.
        public int PaTailLength = 5;
        public double MonovalentMilliMolar = 50, OligoNanoMolar = 100, MagnesiumMilliMolar = 4;

        public LampTmRange GetTm(string role)
        {
            LampTmRange value; bool inner;
            switch (role)
            {
                case "F3": value = F3Tm; inner = false; break;
                case "B3": value = B3Tm; inner = false; break;
                case "F2": value = F2Tm; inner = false; break;
                case "B2": value = B2Tm; inner = false; break;
                case "F1c": value = F1cTm; inner = true; break;
                case "B1c": value = B1cTm; inner = true; break;
                case "LF": value = LFTm; inner = true; break;
                case "LB": value = LBTm; inner = true; break;
                default: throw new ArgumentException("未知 LAMP 结合区域：" + role);
            }
            return value ?? (inner ? new LampTmRange(InnerTmMin, InnerTmMax) { MinUnlimited = InnerTmMinUnlimited, MaxUnlimited = InnerTmMaxUnlimited }
                : new LampTmRange(AnnealTmMin, AnnealTmMax) { MinUnlimited = AnnealTmMinUnlimited, MaxUnlimited = AnnealTmMaxUnlimited });
        }

        public static LampDesignSettings MLampDefaults()
        {
            // Ren et al. compare FIP0/FIP2/FIP3 and select FIP3. The paper
            // uses four core primers; optional loops are an extension.
            return new LampDesignSettings { SnpMethod = "mLAMP", SnpOrientation = "FIP",
                ExtraMismatchFromThreePrime = 3, IncludeLoops = false };
        }
    }

    // All coordinates refer to the input plus strand, one-based and inclusive.
    // Region.Sequence is always the actual synthesized 5'-to-3' segment.
    public sealed class LampRegion
    {
        public string Name, Sequence;
        public int Start, End;
        public bool Reverse;
        public double Tm, Gc;
    }

    public sealed class LampOligo
    {
        public string Name, Sequence;
        public Primer Metrics;
        public List<LampRegion> Regions = new List<LampRegion>();
        // Offset in the actual oligo, not in its binding interval. -1 means none.
        public int SnpIndex = -1;
        // PA-LAMP uses Sequence only as the blocked precursor's DNA-equivalent
        // text. OrderingSequence is the chemically annotated synthesis request.
        public int RnaIndex = -1, RnaTemplatePosition, ActivationTailLength;
        public string ThreePrimeBlock = "";
        public string ActivatedSequence;
        public Primer ActivatedMetrics;
        public List<LampRegion> ActivatedRegions = new List<LampRegion>();
        public int TailMismatchPosition;
        public char TailMismatchTemplateBase, TailMismatchOriginalBase, TailMismatchBase;
        public string OrderingSequence
        {
            get
            {
                if (RnaIndex < 0) return Sequence;
                char rna = Sequence[RnaIndex] == 'T' ? 'U' : Sequence[RnaIndex];
                return Sequence.Substring(0, RnaIndex) + "[r" + rna + "]" + Sequence.Substring(RnaIndex + 1)
                    + (String.IsNullOrEmpty(ThreePrimeBlock) ? "" : "[" + ThreePrimeBlock + "]");
            }
        }
        public int OrderingSnpIndex { get { return RnaIndex < 0 ? SnpIndex : RnaIndex + 2; } }
    }

    public sealed class LampPrimerSet
    {
        public int Rank;
        public double Score;
        public LampOligo F3, B3, FIP, BIP, LF, LB, AlternateInner;
        public string SpecificInner = "";
        public int SpanStart, SpanEnd, SpanLength;
        public int TemplateStart { get { return SpanStart; } }
        public int TemplateEnd { get { return SpanEnd; } }
        public string ReferenceTemplate, AlternateTemplate;
        public int ExtraMismatchPosition;
        public char ExtraMismatchTemplateBase, ExtraMismatchPrimerBase;
        public int TailMismatchPosition;
        public char TailMismatchTemplateBase, TailMismatchOriginalBase, TailMismatchBase;
        public List<string> Notes = new List<string>();
    }

    public sealed class LampDesignResult
    {
        public ParsedSequence Input;
        public SnpInput Snp;
        public LampDesignSettings Settings;
        public List<LampPrimerSet> Sets = new List<LampPrimerSet>();
        public List<string> Notes = new List<string>();
        public bool SearchTruncated;
    }

    public static class LampDesignEngine
    {
        private const int LeftAnchorLimit = 1600, FinalLayoutLimit = 320;
        private const int InnerWindowsPerStart = 4, OtherWindowsPerStart = 2;
        private const int InnerProbeLimit = 12, InnerArmLimit = 4, OuterArmLimit = 2, LayoutsPerBucket = 8;
        private const int StructureProbeMaxLength = 256;
        private static readonly string[] TmRoles = { "F3", "B3", "F2", "B2", "F1c", "B1c", "LF", "LB" };

        private sealed class Window
        {
            public int Start, End;
            public string Template;
            private string sequence;
            public string Sequence { get { return sequence ?? (sequence = Template.Substring(Start - 1, End - Start + 1)); } set { sequence = value; } }
            public double Gc, Tm, Penalty;
        }

        private sealed class RoleCatalog
        {
            public List<Window>[] Start, End;
            public List<Window> All = new List<Window>();
            public double IdealTm;
        }

        private sealed class Catalog
        {
            public Dictionary<string, RoleCatalog> Roles = new Dictionary<string, RoleCatalog>(StringComparer.Ordinal);
            public CancellationToken Cancellation;
            public Dictionary<string, double> StructureCache = new Dictionary<string, double>(StringComparer.Ordinal);
            public long StructureProbeBudget = 100000000;
            public bool StructureProbeSkipped;
            public RoleCatalog For(string role) { return Roles[role]; }
        }

        // Prefix sums keep unrestricted length sampling linear in the number of
        // sampled windows. Sequences are materialized only for retained layouts.
        private sealed class WindowScanner
        {
            private readonly string sequence;
            private readonly int[] gc, invalid, longRun, symmetry;
            private readonly double[] enthalpy, entropy;
            public WindowScanner(string sequence, CancellationToken cancellation)
            {
                this.sequence = sequence; int n = sequence.Length;
                gc = new int[n + 1]; invalid = new int[n + 1]; longRun = new int[n + 1]; symmetry = new int[n];
                enthalpy = new double[n]; entropy = new double[n];
                int run = 0; char previous = '\0';
                for (int i = 0; i < n; i++)
                {
                    if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    char value = sequence[i];
                    gc[i + 1] = gc[i] + (value == 'G' || value == 'C' ? 1 : 0);
                    invalid[i + 1] = invalid[i] + ("ACGT".IndexOf(value) < 0 ? 1 : 0);
                    run = value == previous ? run + 1 : 1; previous = value;
                    longRun[i + 1] = longRun[i] + (run >= 6 ? 1 : 0);
                    if (i > 0)
                    {
                        double h = enthalpy[i - 1], s = entropy[i - 1];
                        LampThermodynamics.AddNearestNeighbor(sequence[i - 1], value, ref h, ref s);
                        enthalpy[i] = h; entropy[i] = s;
                    }
                }
                // Even reverse-complement palindromes (DNA has no self-pairing
                // middle base). The Manacher mirror rule also holds for this
                // involutive complement relation.
                for (int i = 0, left = 0, right = -1; i < n; i++)
                {
                    if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    int radius = i > right ? 0 : Math.Min(symmetry[left + right - i + 1], right - i + 1);
                    while (i + radius < n && i - radius - 1 >= 0
                        && Complements(sequence[i + radius], sequence[i - radius - 1])) radius++;
                    symmetry[i] = radius;
                    if (i + radius - 1 > right) { left = i - radius; right = i + radius - 1; }
                }
            }
            private static bool Complements(char a, char b)
            { return a == 'A' && b == 'T' || a == 'T' && b == 'A' || a == 'C' && b == 'G' || a == 'G' && b == 'C'; }
            public Window Make(int start, int length, LampDesignSettings settings)
            {
                int first = start - 1, after = first + length;
                if (length < 2 || first < 0 || after > sequence.Length || invalid[after] != invalid[first]
                    || (length >= 6 && longRun[after] != longRun[first + 5])) return null;
                double percent = 100.0 * (gc[after] - gc[first]) / length;
                if (percent < settings.GcMin || percent > settings.GcMax) return null;
                bool symmetric = length % 2 == 0 && symmetry[first + length / 2] >= length / 2;
                double tm = LampThermodynamics.FromStackSums(sequence[first], sequence[after - 1], length, symmetric,
                    enthalpy[after - 1] - enthalpy[first], entropy[after - 1] - entropy[first],
                    settings.MonovalentMilliMolar, settings.OligoNanoMolar, settings.MagnesiumMilliMolar);
                return new Window { Start = start, End = start + length - 1, Template = sequence, Gc = percent, Tm = tm,
                    Penalty = Math.Abs(percent - 50) * 0.12 + Math.Abs(length - 22) * 0.2 };
            }
        }

        private sealed class Arm
        {
            public Window Outer, Anneal, Inner;
            public double Penalty, TmPenalty, NativeStructurePenalty;
        }

        private sealed class Layout
        {
            public Arm Left, Right;
            public string Specific;
            public double Penalty, LocalPenalty;
        }

        private sealed class Evaluated
        {
            public LampPrimerSet Set;
            public double Penalty;
        }

        public static LampDesignResult Design(ParsedSequence input, LampDesignSettings settings,
            Action<int, string> progress, CancellationToken cancellation)
        {
            if (input == null) throw new ArgumentNullException("input");
            ParsedSequence normalized = Normalize(input);
            return Run(normalized, null, settings, progress, cancellation);
        }

        public static LampDesignResult DesignSnp(SnpInput input, LampDesignSettings settings,
            Action<int, string> progress, CancellationToken cancellation)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (input.Reference == null || input.Alternate == null) throw new ArgumentException("SNP 模板不完整。");
            ParsedSequence reference = Normalize(input.Reference), alternate = Normalize(input.Alternate);
            if (reference.Sequence.Length != alternate.Sequence.Length || input.Position < 1 || input.Position > reference.Sequence.Length
                || "ACGT".IndexOf(input.ReferenceAllele) < 0 || "ACGT".IndexOf(input.AlternateAllele) < 0
                || input.ReferenceAllele == input.AlternateAllele
                || reference.Sequence[input.Position - 1] != input.ReferenceAllele || alternate.Sequence[input.Position - 1] != input.AlternateAllele)
                throw new ArgumentException("SNP 坐标、等位碱基或模板序列不一致。");
            for (int i = 0; i < reference.Sequence.Length; i++)
                if (i != input.Position - 1 && reference.Sequence[i] != alternate.Sequence[i])
                    throw new ArgumentException("本模式要求两个模板仅在指定 SNP 处不同。");
            SnpInput normalized = new SnpInput { Reference = reference, Alternate = alternate, Position = input.Position,
                ReferenceAllele = input.ReferenceAllele, AlternateAllele = input.AlternateAllele, AnnotatedSequence = input.AnnotatedSequence };
            return Run(reference, normalized, settings, progress, cancellation);
        }

        private static ParsedSequence Normalize(ParsedSequence input)
        {
            ParsedSequence normalized = SequenceParser.Parse(input.Sequence);
            normalized.Name = String.IsNullOrWhiteSpace(input.Name) ? "输入序列" : input.Name;
            if (input.Warnings != null) foreach (string note in input.Warnings)
                if (!normalized.Warnings.Contains(note)) normalized.Warnings.Add(note);
            return normalized;
        }

        private static LampDesignResult Run(ParsedSequence input, SnpInput snp, LampDesignSettings settings,
            Action<int, string> progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            LampDesignSettings options = Validate(settings, input.Sequence.Length);
            if (snp == null && (options.SnpMethod == "PA-LAMP" || options.SnpMethod == "mLAMP"))
                throw new ArgumentException(options.SnpMethod + " 需要一个 SNP 标记，请使用 SNP 设计模式。");
            LampDesignResult result = new LampDesignResult { Input = input, Snp = snp, Settings = options };
            result.Notes.AddRange(input.Warnings);
            AddMethodNotes(result);
            if (options.RegionMinUnlimited || options.RegionMaxUnlimited)
                result.Notes.Add("区域长度无限制仅移除对应用户界限：最短仍为最近邻 Tm 所需的 2 nt，最长不超过模板。若某起点可用长度超过 128 种，则逐一探索至 60 nt 的长度，并在更长区间均匀抽取至多 32 种长度（包含区间两端）；原有窗口、布局和结构核验限额仍适用，不是穷举。");
            if (snp != null && options.SnpMethod == "PA-LAMP")
            {
                bool tailAvailable = snp.Position > options.PaTailLength;
                for (int p = snp.Position - options.PaTailLength; tailAvailable && p < snp.Position; p++)
                    if ("ACGT".IndexOf(input.Sequence[p - 1]) < 0) tailAvailable = false;
                if (!tailAvailable)
                {
                    result.Notes.Add("SNP 上游不足 " + options.PaTailLength + " 个确定碱基，无法构建 BIP 方向的 RNA 后 DNA 尾和 C3 阻断前体。");
                    Report(progress, 100, "PA-LAMP 阻断尾没有足够的确定模板序列。");
                    return result;
                }
            }
            Report(progress, 3, "扫描 LAMP 结合区域并计算参考 Tm…");
            Catalog catalog = BuildCatalog(input.Sequence, options, cancellation);
            Report(progress, 23, "组合 F3、F2、F1、B1、B2、B3 六区域布局…");
            Dictionary<string, List<Layout>> buckets = new Dictionary<string, List<Layout>>();
            Dictionary<string, List<Arm>> leftCache = new Dictionary<string, List<Arm>>(), rightCache = new Dictionary<string, List<Arm>>();
            if (snp == null)
            {
                List<Window> anchors = PruneAnchors(catalog.For("F2").All, LeftAnchorLimit, catalog.For("F2").IdealTm);
                Search(anchors, null, "", catalog, leftCache, rightCache, options, buckets, progress, cancellation);
            }
            else
            {
                if (options.SnpMethod != "PA-LAMP" && (options.SnpOrientation == "Auto" || options.SnpOrientation == "FIP"))
                {
                    List<Window> anchored = Anchored(input.Sequence, snp.Position, false, options, cancellation);
                    Search(anchored, null, "FIP", catalog, leftCache, rightCache, options, buckets, progress, cancellation);
                }
                if (options.SnpOrientation == "Auto" || options.SnpOrientation == "BIP")
                {
                    // H2 cuts immediately 5' to the RNA nucleotide. The active
                    // reverse primer therefore ends at plus-strand SNP + 1;
                    // the RNA and the blocked downstream tail are discarded.
                    int anchor = snp.Position + (options.SnpMethod == "PA-LAMP" ? 1 : 0);
                    List<Window> anchored = Anchored(input.Sequence, anchor, true, options, cancellation);
                    int low = input.Sequence.Length + 1, high = 0;
                    foreach (Window right in anchored)
                    {
                        low = Math.Min(low, right.End - options.CoreSpanMax + 1);
                        high = Math.Max(high, right.End - options.CoreSpanMin + 1);
                    }
                    List<Window> left = Range(catalog.For("F2").Start, low, high, 0);
                    Search(left, anchored, "BIP", catalog, leftCache, rightCache, options, buckets, progress, cancellation);
                }
            }
            List<Layout> layouts = new List<Layout>();
            foreach (List<Layout> group in buckets.Values) layouts.AddRange(group);
            TrimLayouts(layouts, FinalLayoutLimit);
            result.SearchTruncated = true;
            result.Notes.Add("采用有限搜索：各区域按当前 Tm 范围筛选；每个起点的 F1c/B1c 各保留至多 4 个窗口，其余区域各 2 个。每个固定 F2/B2 从局部前 12 个内区段中保留局部前 2 个，并按拼接内引物的参考结构与 Tm 配平补充至多 4 个；外区段各 2 个，每侧至多 8 个布局。六区域布局同时保留局部排序与参考完整引物结构排序的候选。普通模式最多 1,600 个分散起点；按普通起点的 20 nt 分箱或 SNP 方向及 F2/B2 长度分组，每组保留至多 8 个布局，裁剪时预留 4 个局部优选名额，再按结构排序补足；最终至多评估 320 个六区域布局，裁剪时同样预留一半局部优选名额。环引物各核验至多 6 个窗口。预筛结构使用未人工改写的模板匹配内引物，PA 使用切后 DNA；实际等位、错配和修饰前体在最终阶段核验。可能遗漏合适组合。");
            if (catalog.StructureProbeSkipped)
                result.Notes.Add("早期结构预筛只核验不超过 256 nt 的参考完整引物，并使用 100,000,000 单位预算（每条按 4×长度平方估计，重复序列复用缓存）；超长或预算不足时回退局部指标，不视为已通过结构检查。最终仍按实际引物进行有限结构核验。");
            if (layouts.Count == 0)
            {
                result.Notes.Add("未找到满足六区域方向、间距、长度、GC、参考 Tm 和模板跨度约束的布局。可检查 SNP 两侧是否有足够序列，或调整 LAMP 参数。");
                Report(progress, 100, "未找到符合条件的 LAMP 候选。");
                return result;
            }
            List<Evaluated> evaluated = new List<Evaluated>();
            long structureBudget = 250000000;
            bool structureSkipped = false;
            for (int i = 0; i < layouts.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if ((i & 7) == 0) Report(progress, 50 + 45 * i / layouts.Count, "核验 LAMP 内引物、环引物及各反应内的互补风险…");
                char[] variants = snp != null && (options.ExtraMismatchFromThreePrime > 0 || options.SnpMethod == "PA-LAMP")
                    ? new char[] { 'A', 'C', 'G', 'T' } : new char[] { '\0' };
                foreach (char mismatch in variants)
                {
                    if (options.RegionMaxUnlimited)
                    {
                        Layout layout = layouts[i];
                        long totalLength = WindowLength(layout.Left.Outer) + WindowLength(layout.Right.Outer)
                            + WindowLength(layout.Left.Inner) + WindowLength(layout.Left.Anneal)
                            + WindowLength(layout.Right.Inner) + WindowLength(layout.Right.Anneal) + 120;
                        if (snp != null) totalLength += 180;
                        long cost = 4 * totalLength * totalLength;
                        if (cost > structureBudget) { structureSkipped = true; continue; }
                        structureBudget -= cost;
                    }
                    Evaluated value = Materialize(layouts[i], catalog, result, mismatch, cancellation);
                    if (value != null) evaluated.Add(value);
                }
            }
            evaluated.Sort(delegate(Evaluated a, Evaluated b) { int c = a.Penalty.CompareTo(b.Penalty); return c != 0 ? c : StringComparer.Ordinal.Compare(Signature(a.Set), Signature(b.Set)); });
            if (structureSkipped) result.Notes.Add("已解除区域最长限制；本次为保证有限且可取消的计算，按引物总长度平方估计结构核验工作量，使用 250,000,000 单位预算，跳过超出剩余预算的布局/错配组合。可能遗漏较长引物，结果不是无限穷举或全部合格方案。");
            Select(evaluated, result);
            if (result.Sets.Count == 0) result.Notes.Add("六区域布局存在，但拼接后的内引物或两等位引物未通过 GC、参考 Tm、同聚物等检查。");
            Report(progress, 100, "LAMP 设计完成，得到 " + result.Sets.Count + " 组候选。");
            return result;
        }

        public static LampDesignSettings EffectiveSettings(LampDesignSettings settings, int templateLength)
        {
            if (templateLength < 1 || templateLength > 20000) throw new ArgumentException("LAMP 模板长度须为 1–20,000 nt。");
            return Validate(settings, templateLength);
        }

        private static LampDesignSettings Validate(LampDesignSettings s, int templateLength)
        {
            if (s == null) throw new ArgumentNullException("settings");
            if ((!s.RegionMinUnlimited && (s.RegionMin < 15 || s.RegionMin > 35))
                || (!s.RegionMaxUnlimited && (s.RegionMax < 15 || s.RegionMax > 35))
                || (!s.RegionMinUnlimited && !s.RegionMaxUnlimited && s.RegionMax < s.RegionMin))
                throw new ArgumentException("LAMP 启用的单个结合区域长度界限须在 15–35 nt 内，且最短不大于最长。");
            if ((!s.SpanMinUnlimited && (s.SpanMin < 100 || s.SpanMin > 600))
                || (!s.SpanMaxUnlimited && (s.SpanMax < 100 || s.SpanMax > 600))
                || (!s.SpanMinUnlimited && !s.SpanMaxUnlimited && s.SpanMax < s.SpanMin))
                throw new ArgumentException("LAMP 启用的外侧模板跨度界限须在 100–600 bp 内，且最短不大于最长。");
            if ((!s.CoreSpanMinUnlimited && (s.CoreSpanMin < 1 || s.CoreSpanMin > 20000))
                || (!s.CoreSpanMaxUnlimited && (s.CoreSpanMax < 1 || s.CoreSpanMax > 20000))
                || (!s.CoreSpanMinUnlimited && !s.CoreSpanMaxUnlimited && s.CoreSpanMin > s.CoreSpanMax))
                throw new ArgumentException("LAMP 启用的 F2 至 B2 外缘跨度界限须在 1–20,000 nt 内，且最短不大于最长。");
            if (s.MaxSets < 1 || s.MaxSets > 50) throw new ArgumentException("LAMP 输出候选组数须在 1–50 之间。");
            if ((!s.GcMinUnlimited && (!LampThermodynamics.Finite(s.GcMin) || s.GcMin < 0 || s.GcMin > 100))
                || (!s.GcMaxUnlimited && (!LampThermodynamics.Finite(s.GcMax) || s.GcMax < 0 || s.GcMax > 100))
                || (!s.GcMinUnlimited && !s.GcMaxUnlimited && s.GcMin > s.GcMax))
                throw new ArgumentException("LAMP GC 范围无效。");
            foreach (string role in TmRoles)
            {
                LampTmRange range = s.GetTm(role);
                if (!ValidTm(range.Min, range.Max, range.MinUnlimited, range.MaxUnlimited))
                    throw new ArgumentException(role + " 启用的参考 Tm 界限须在 35–90 °C 内，且最低不大于最高。");
            }
            if (s.SnpOrientation != "Auto" && s.SnpOrientation != "FIP" && s.SnpOrientation != "BIP")
                throw new ArgumentException("LAMP SNP 方向须为 Auto、FIP 或 BIP。");
            if (s.SnpMethod != "AS-LAMP" && s.SnpMethod != "PA-LAMP" && s.SnpMethod != "mLAMP")
                throw new ArgumentException("LAMP SNP 方法须为 AS-LAMP、PA-LAMP 或 mLAMP。");
            if (s.SnpMethod == "mLAMP" && s.SnpOrientation == "BIP")
                throw new ArgumentException("Ren 2019 mLAMP 使用 FIP 的 F2 末端识别 SNP；BIP 方向请使用 AS-LAMP 探索模式。");
            if (s.SnpMethod == "PA-LAMP" && s.SnpOrientation == "FIP")
                throw new ArgumentException("当前 PA-LAMP 仅支持 BIP 激活引物，不能选择 FIP 方向。");
            if (s.SnpMethod == "PA-LAMP" && s.ExtraMismatchFromThreePrime != 0)
                throw new ArgumentException("PA-LAMP 通过 RNA / C3 阻断及 RNase H2 激活区分等位基因，不叠加 AS-LAMP 的人为错配。");
            if (s.SnpMethod == "PA-LAMP" && (s.PaTailLength < 4 || s.PaTailLength > 7))
                throw new ArgumentException("PA-LAMP RNA 后的 DNA 尾长度须为 4–7 nt（包括最后一个非互补碱基）；默认 5 nt 参考原文补充材料，不代表通用最优值。");
            if (s.ExtraMismatchFromThreePrime != 0 && s.ExtraMismatchFromThreePrime != 2 && s.ExtraMismatchFromThreePrime != 3)
                throw new ArgumentException("附加错配位置只能为不添加、3′ 倒数第 2 位或倒数第 3 位。");
            LampThermodynamics.MeltingTemperature("ACGTACGT", s.MonovalentMilliMolar, s.OligoNanoMolar, s.MagnesiumMilliMolar);
            return new LampDesignSettings { RegionMin = s.RegionMinUnlimited ? 2 : s.RegionMin, RegionMax = s.RegionMaxUnlimited ? templateLength : s.RegionMax,
                SpanMin = s.SpanMinUnlimited ? 1 : s.SpanMin, SpanMax = s.SpanMaxUnlimited ? templateLength : s.SpanMax,
                CoreSpanMin = s.CoreSpanMinUnlimited ? 1 : s.CoreSpanMin, CoreSpanMax = s.CoreSpanMaxUnlimited ? templateLength : s.CoreSpanMax,
                CoreSpanMinUnlimited = s.CoreSpanMinUnlimited, CoreSpanMaxUnlimited = s.CoreSpanMaxUnlimited,
                MaxSets = s.MaxSets, GcMin = s.GcMinUnlimited ? 0 : s.GcMin, GcMax = s.GcMaxUnlimited ? 100 : s.GcMax,
                AnnealTmMin = s.AnnealTmMinUnlimited ? Double.NegativeInfinity : s.AnnealTmMin,
                AnnealTmMax = s.AnnealTmMaxUnlimited ? Double.PositiveInfinity : s.AnnealTmMax,
                InnerTmMin = s.InnerTmMinUnlimited ? Double.NegativeInfinity : s.InnerTmMin,
                InnerTmMax = s.InnerTmMaxUnlimited ? Double.PositiveInfinity : s.InnerTmMax, IncludeLoops = s.IncludeLoops,
                RegionMinUnlimited = s.RegionMinUnlimited, RegionMaxUnlimited = s.RegionMaxUnlimited,
                SpanMinUnlimited = s.SpanMinUnlimited, SpanMaxUnlimited = s.SpanMaxUnlimited,
                GcMinUnlimited = s.GcMinUnlimited, GcMaxUnlimited = s.GcMaxUnlimited,
                AnnealTmMinUnlimited = s.AnnealTmMinUnlimited, AnnealTmMaxUnlimited = s.AnnealTmMaxUnlimited,
                InnerTmMinUnlimited = s.InnerTmMinUnlimited, InnerTmMaxUnlimited = s.InnerTmMaxUnlimited,
                F3Tm = ResolvedTm(s.GetTm("F3")), B3Tm = ResolvedTm(s.GetTm("B3")),
                F2Tm = ResolvedTm(s.GetTm("F2")), B2Tm = ResolvedTm(s.GetTm("B2")),
                F1cTm = ResolvedTm(s.GetTm("F1c")), B1cTm = ResolvedTm(s.GetTm("B1c")),
                LFTm = ResolvedTm(s.GetTm("LF")), LBTm = ResolvedTm(s.GetTm("LB")),
                SnpOrientation = s.SnpMethod == "mLAMP" ? "FIP" : s.SnpOrientation,
                SnpMethod = s.SnpMethod, PaTailLength = s.PaTailLength, ExtraMismatchFromThreePrime = s.ExtraMismatchFromThreePrime,
                MonovalentMilliMolar = s.MonovalentMilliMolar, OligoNanoMolar = s.OligoNanoMolar, MagnesiumMilliMolar = s.MagnesiumMilliMolar };
        }

        private static LampTmRange ResolvedTm(LampTmRange range)
        {
            return new LampTmRange(range.MinUnlimited ? Double.NegativeInfinity : range.Min,
                range.MaxUnlimited ? Double.PositiveInfinity : range.Max) { MinUnlimited = range.MinUnlimited, MaxUnlimited = range.MaxUnlimited };
        }

        private static bool FitsTm(double value, LampTmRange range)
        { return (range.MinUnlimited || value >= range.Min) && (range.MaxUnlimited || value <= range.Max); }

        private static double TargetTm(LampDesignSettings settings, string role)
        {
            LampTmRange range = settings.GetTm(role);
            if (!range.MinUnlimited && !range.MaxUnlimited && LampThermodynamics.Finite(range.Min) && LampThermodynamics.Finite(range.Max))
                return (range.Min + range.Max) / 2;
            return role == "F1c" || role == "B1c" || role == "LF" || role == "LB" ? 65 : 60;
        }

        private static bool ValidTm(double minimum, double maximum, bool minimumUnlimited, bool maximumUnlimited)
        {
            return (minimumUnlimited || (LampThermodynamics.Finite(minimum) && minimum >= 35 && minimum <= 90))
                && (maximumUnlimited || (LampThermodynamics.Finite(maximum) && maximum >= 35 && maximum <= 90))
                && (minimumUnlimited || maximumUnlimited || minimum <= maximum);
        }

        private static Catalog BuildCatalog(string sequence, LampDesignSettings s, CancellationToken cancellation)
        {
            int size = sequence.Length + 1;
            Catalog catalog = new Catalog { Cancellation = cancellation };
            foreach (string role in TmRoles) catalog.Roles.Add(role, new RoleCatalog { Start = new List<Window>[size], End = new List<Window>[size], IdealTm = TargetTm(s, role) });
            WindowScanner scanner = s.RegionMaxUnlimited ? new WindowScanner(sequence, cancellation) : null;
            for (int start = 1; start <= sequence.Length - s.RegionMin + 1; start++)
            {
                if ((start & 63) == 0) cancellation.ThrowIfCancellationRequested();
                var retained = new Dictionary<string, List<Window>>(StringComparer.Ordinal);
                foreach (string role in TmRoles) retained.Add(role, new List<Window>());
                bool sampled;
                int[] lengths = DesignEngine.SamplePrimerLengths(s.RegionMin, Math.Min(s.RegionMax, sequence.Length - start + 1), out sampled);
                foreach (int length in lengths)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Window value = scanner == null ? MakeWindow(sequence.Substring(start - 1, length), start, s) : scanner.Make(start, length, s);
                    if (value == null) continue;
                    foreach (string role in TmRoles)
                        if (FitsTm(value.Tm, s.GetTm(role))) KeepWindow(retained[role], value, catalog.For(role).IdealTm,
                            role == "F1c" || role == "B1c" ? InnerWindowsPerStart : OtherWindowsPerStart);
                }
                foreach (string role in TmRoles)
                    foreach (Window w in retained[role])
                    {
                        RoleCatalog section = catalog.For(role);
                        Add(section.Start, w.Start, w); Add(section.End, w.End, w); section.All.Add(w);
                    }
            }
            return catalog;
        }

        private static void KeepWindow(List<Window> values, Window value, double idealTm, int limit)
        {
            values.Add(value); values.Sort(delegate(Window a, Window b) { return CompareWindow(a, b, idealTm); });
            if (values.Count > limit) values.RemoveAt(values.Count - 1);
        }

        private static void Add(List<Window>[] index, int position, Window window)
        { if (index[position] == null) index[position] = new List<Window>(); index[position].Add(window); }

        private static Window MakeWindow(string sequence, int start, LampDesignSettings s)
        {
            int gc = 0, run = 0, longest = 0; char previous = '\0';
            foreach (char b in sequence)
            {
                if ("ACGT".IndexOf(b) < 0) return null;
                if (b == 'G' || b == 'C') gc++;
                run = b == previous ? run + 1 : 1; longest = Math.Max(longest, run); previous = b;
            }
            if (longest > 5) return null;
            double percent = 100.0 * gc / sequence.Length;
            if (percent < s.GcMin || percent > s.GcMax) return null;
            return new Window { Start = start, End = start + sequence.Length - 1, Sequence = sequence, Gc = percent,
                Tm = LampThermodynamics.MeltingTemperature(sequence, s.MonovalentMilliMolar, s.OligoNanoMolar, s.MagnesiumMilliMolar),
                Penalty = Math.Abs(percent - 50) * 0.12 + Math.Abs(sequence.Length - 22) * 0.2 };
        }

        private static int CompareWindow(Window a, Window b, double idealTm)
        {
            int c = (a.Penalty + 0.8 * Math.Abs(a.Tm - idealTm)).CompareTo(b.Penalty + 0.8 * Math.Abs(b.Tm - idealTm));
            if (c == 0) c = a.Start.CompareTo(b.Start); if (c == 0) c = a.End.CompareTo(b.End); return c;
        }
        private static int WindowLength(Window window) { return window.End - window.Start + 1; }

        private static List<Window> Range(List<Window>[] index, int minimum, int maximum, int maximumCount)
        {
            List<Window> values = new List<Window>();
            for (int p = Math.Max(1, minimum); p <= Math.Min(maximum, index.Length - 1); p++)
                if (index[p] != null) values.AddRange(index[p]);
            if (maximumCount > 0)
            {
                values.Sort(delegate(Window a, Window b) { return CompareWindow(a, b, 60); });
                if (values.Count > maximumCount) values.RemoveRange(maximumCount, values.Count - maximumCount);
            }
            return values;
        }

        private static List<Window> PruneAnchors(List<Window> input, int limit, double idealTm)
        {
            if (input.Count <= limit) return input;
            // A round-robin over spatial bins preserves access to distant targets.
            SortedDictionary<int, List<Window>> bins = new SortedDictionary<int, List<Window>>();
            foreach (Window w in input)
            {
                int key = w.Start / 40; List<Window> bin;
                if (!bins.TryGetValue(key, out bin)) { bin = new List<Window>(); bins.Add(key, bin); } bin.Add(w);
            }
            foreach (List<Window> bin in bins.Values) bin.Sort(delegate(Window a, Window b) { return CompareWindow(a, b, idealTm); });
            List<Window> output = new List<Window>();
            for (int round = 0; output.Count < limit; round++)
            {
                bool added = false;
                foreach (List<Window> bin in bins.Values)
                {
                    if (round < bin.Count) { output.Add(bin[round]); added = true; }
                    if (output.Count == limit) break;
                }
                if (!added) break;
            }
            output.Sort(delegate(Window a, Window b) { int c = a.Start.CompareTo(b.Start); return c == 0 ? a.End.CompareTo(b.End) : c; });
            return output;
        }

        private static List<Window> Anchored(string sequence, int position, bool reverse, LampDesignSettings s, CancellationToken cancellation)
        {
            List<Window> values = new List<Window>();
            bool sampled;
            int maximum = Math.Min(s.RegionMax, reverse ? sequence.Length - position + 1 : position);
            int[] lengths = DesignEngine.SamplePrimerLengths(Math.Max(s.RegionMin, s.ExtraMismatchFromThreePrime), maximum, out sampled);
            WindowScanner scanner = s.RegionMaxUnlimited ? new WindowScanner(sequence, cancellation) : null;
            foreach (int length in lengths)
            {
                cancellation.ThrowIfCancellationRequested();
                int start = reverse ? position : position - length + 1;
                if (start < 1 || start + length - 1 > sequence.Length) continue;
                Window w = scanner == null ? MakeWindow(sequence.Substring(start - 1, length), start, s) : scanner.Make(start, length, s);
                if (w != null && FitsTm(w.Tm, s.GetTm(reverse ? "B2" : "F2"))) values.Add(w);
            }
            return values;
        }

        private static List<Arm> Arms(Window anneal, bool reverse, Catalog catalog, Dictionary<string, List<Arm>> cache)
        {
            string key = anneal.Start + ":" + anneal.End;
            List<Arm> found;
            if (cache.TryGetValue(key, out found)) return found;
            RoleCatalog innerRole = catalog.For(reverse ? "B1c" : "F1c"), outerRole = catalog.For(reverse ? "B3" : "F3");
            RoleCatalog annealRole = catalog.For(reverse ? "B2" : "F2");
            List<Window> inners = reverse ? Range(innerRole.End, anneal.End - 60, anneal.End - 40, 0)
                : Range(innerRole.Start, anneal.Start + 40, anneal.Start + 60, 0);
            List<Window> outers = reverse ? Range(outerRole.Start, anneal.End + 1, anneal.End + 61, 0)
                : Range(outerRole.End, anneal.Start - 61, anneal.Start - 1, 0);
            inners.RemoveAll(delegate(Window w) { return reverse ? w.End >= anneal.Start : w.Start <= anneal.End; });
            inners.Sort(delegate(Window a, Window b) { return CompareWindow(a, b, innerRole.IdealTm); });
            outers.Sort(delegate(Window a, Window b) { return CompareWindow(a, b, outerRole.IdealTm); });
            if (inners.Count > InnerProbeLimit) inners.RemoveRange(InnerProbeLimit, inners.Count - InnerProbeLimit);
            // Keep local winners as well as structure-aware choices. Native F2/B2
            // structure is only a preview: final SNP/mismatch variants can differ.
            var native = new Dictionary<Window, double>();
            foreach (Window inner in inners)
            {
                catalog.Cancellation.ThrowIfCancellationRequested();
                native.Add(inner, NativeInnerStructure(anneal, inner, reverse, catalog));
            }
            var selected = new List<Window>();
            for (int i = 0; i < Math.Min(2, inners.Count); i++) selected.Add(inners[i]);
            inners.Sort(delegate(Window a, Window b)
            {
                double pa = RegionPenalty(a, innerRole.IdealTm) / 6.0 + native[a] / 4.0
                    + 0.8 * Math.Max(0, anneal.Tm + 1 - a.Tm);
                double pb = RegionPenalty(b, innerRole.IdealTm) / 6.0 + native[b] / 4.0
                    + 0.8 * Math.Max(0, anneal.Tm + 1 - b.Tm);
                int c = pa.CompareTo(pb); return c != 0 ? c : CompareWindow(a, b, innerRole.IdealTm);
            });
            foreach (Window inner in inners)
            {
                if (selected.Count >= InnerArmLimit) break;
                if (!selected.Contains(inner)) selected.Add(inner);
            }
            if (outers.Count > OuterArmLimit) outers.RemoveRange(OuterArmLimit, outers.Count - OuterArmLimit);
            found = new List<Arm>();
            foreach (Window inner in selected) foreach (Window outer in outers)
            {
                double penalty = RegionPenalty(anneal, annealRole.IdealTm) + RegionPenalty(inner, innerRole.IdealTm) + RegionPenalty(outer, outerRole.IdealTm);
                double outerStructure = ProbeStructure(reverse ? DesignEngine.ReverseComplement(outer.Sequence) : outer.Sequence, catalog);
                found.Add(new Arm { Anneal = anneal, Inner = inner, Outer = outer, Penalty = penalty,
                    TmPenalty = 0.8 * Math.Max(0, anneal.Tm + 1 - inner.Tm), NativeStructurePenalty = native[inner] + outerStructure });
            }
            cache.Add(key, found); return found;
        }

        private static double RegionPenalty(Window w, double tm) { return w.Penalty + 0.8 * Math.Abs(w.Tm - tm); }

        private static double NativeInnerStructure(Window anneal, Window inner, bool reverse, Catalog catalog)
        {
            if (WindowLength(anneal) + WindowLength(inner) > StructureProbeMaxLength)
            { catalog.StructureProbeSkipped = true; return 0; }
            string sequence = reverse ? inner.Sequence + DesignEngine.ReverseComplement(anneal.Sequence)
                : DesignEngine.ReverseComplement(inner.Sequence) + anneal.Sequence;
            return ProbeStructure(sequence, catalog);
        }

        private static double ProbeStructure(string sequence, Catalog catalog)
        {
            catalog.Cancellation.ThrowIfCancellationRequested();
            double penalty;
            if (catalog.StructureCache.TryGetValue(sequence, out penalty)) return penalty;
            long cost = 4L * sequence.Length * sequence.Length;
            if (sequence.Length > StructureProbeMaxLength || cost > catalog.StructureProbeBudget)
            {
                catalog.StructureProbeSkipped = true;
                // Zero means "no preview contribution", not a passed structure
                // check. Keep the locally selected branch and disclose fallback.
                catalog.StructureCache.Add(sequence, 0); return 0;
            }
            catalog.StructureProbeBudget -= cost;
            // Do not apply composition filters here: artificial mismatches can
            // change them. Hard filters still apply to actual final oligos.
            var candidate = new DesignEngine.Candidate { Primer = new Primer { Sequence = sequence } };
            DesignEngine.AddStructures(new List<DesignEngine.Candidate> { candidate }, catalog.Cancellation);
            penalty = StructurePenalty(candidate.Primer);
            catalog.StructureCache.Add(sequence, penalty); return penalty;
        }

        private static void Search(List<Window> leftAnchors, List<Window> fixedRight, string specific, Catalog catalog,
            Dictionary<string, List<Arm>> leftCache, Dictionary<string, List<Arm>> rightCache, LampDesignSettings s,
            Dictionary<string, List<Layout>> buckets, Action<int, string> progress, CancellationToken cancellation)
        {
            for (int i = 0; i < leftAnchors.Count; i++)
            {
                if ((i & 15) == 0) { cancellation.ThrowIfCancellationRequested(); Report(progress, 25 + 23 * i / Math.Max(1, leftAnchors.Count), "筛选 LAMP 六区域布局…"); }
                Window f2 = leftAnchors[i];
                List<Arm> left = Arms(f2, false, catalog, leftCache);
                if (left.Count == 0) continue;
                List<Window> rights = fixedRight ?? Range(catalog.For("B2").End, f2.Start + s.CoreSpanMin - 1, f2.Start + s.CoreSpanMax - 1, 0);
                foreach (Window b2 in rights)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int coreSpan = b2.End - f2.Start + 1;
                    if (coreSpan < s.CoreSpanMin || coreSpan > s.CoreSpanMax) continue;
                    List<Arm> right = Arms(b2, true, catalog, rightCache);
                    foreach (Arm l in left) foreach (Arm r in right)
                    {
                        if (l.Inner.End >= r.Inner.Start) continue;
                        if (s.SnpMethod == "PA-LAMP" && r.Inner.End >= b2.Start - s.PaTailLength - 1) continue;
                        int span = r.Outer.End - l.Outer.Start + 1;
                        if (span < s.SpanMin || span > s.SpanMax) continue;
                        double penalty = (l.Penalty + r.Penalty) / 6.0 + (l.NativeStructurePenalty + r.NativeStructurePenalty) / 4.0
                            + l.TmPenalty + r.TmPenalty + 0.8 * Math.Abs(f2.Tm - b2.Tm)
                            + 0.05 * Math.Abs(coreSpan - 140) + 0.01 * Math.Abs(span - 200);
                        double localPenalty = (l.Penalty + r.Penalty + l.TmPenalty + r.TmPenalty) / 6.0 + 0.8 * Math.Abs(f2.Tm - b2.Tm)
                            + 0.05 * Math.Abs(coreSpan - 140) + 0.01 * Math.Abs(span - 200);
                        string bucketKey = specific.Length == 0 ? "normal:" + (f2.Start / 20)
                            : specific + ":" + (f2.End - f2.Start + 1) + ":" + (b2.End - b2.Start + 1);
                        List<Layout> bucket;
                        if (!buckets.TryGetValue(bucketKey, out bucket)) { bucket = new List<Layout>(); buckets.Add(bucketKey, bucket); }
                        if (bucket.Count == LayoutsPerBucket && penalty > bucket[bucket.Count - 1].Penalty)
                        {
                            // Skip only when neither retained ordering can admit
                            // this layout. A worse structure preview must not
                            // exclude a candidate in the reserved local half.
                            int betterLocal = 0;
                            foreach (Layout kept in bucket) if (kept.LocalPenalty < localPenalty) betterLocal++;
                            if (betterLocal >= LayoutsPerBucket / 2) continue;
                        }
                        bucket.Add(new Layout { Left = l, Right = r, Specific = specific, Penalty = penalty, LocalPenalty = localPenalty });
                        TrimLayouts(bucket, LayoutsPerBucket);
                    }
                }
            }
        }

        private static void TrimLayouts(List<Layout> layouts, int limit)
        {
            if (layouts.Count > limit)
            {
                var local = new List<Layout>(layouts);
                local.Sort(delegate(Layout a, Layout b)
                { int c = a.LocalPenalty.CompareTo(b.LocalPenalty); return c != 0 ? c : CompareLayout(a, b); });
                var selected = local.GetRange(0, limit / 2);
                layouts.Sort(CompareLayout);
                foreach (Layout layout in layouts)
                {
                    if (selected.Count >= limit) break;
                    if (!selected.Contains(layout)) selected.Add(layout);
                }
                layouts.Clear(); layouts.AddRange(selected);
            }
            layouts.Sort(CompareLayout);
        }

        private static int CompareLayout(Layout a, Layout b)
        {
            int c = a.Penalty.CompareTo(b.Penalty);
            if (c == 0) c = a.Left.Outer.Start.CompareTo(b.Left.Outer.Start);
            if (c == 0) c = a.Right.Outer.End.CompareTo(b.Right.Outer.End);
            if (c == 0) c = a.Left.Anneal.Start.CompareTo(b.Left.Anneal.Start);
            if (c == 0) c = a.Right.Anneal.End.CompareTo(b.Right.Anneal.End);
            return c;
        }

        private static Evaluated Materialize(Layout layout, Catalog catalog, LampDesignResult result, char mismatch, CancellationToken cancellation)
        {
            if (result.Snp != null && result.Settings.SnpMethod == "PA-LAMP")
                return MaterializePa(layout, catalog, result, mismatch, cancellation);
            LampDesignSettings s = result.Settings;
            SnpInput snp = result.Snp;
            Arm left = layout.Left, right = layout.Right;
            LampRegion f2 = Region("F2", left.Anneal, false), f1 = Region("F1c", left.Inner, true);
            LampRegion b1 = Region("B1c", right.Inner, false), b2 = Region("B2", right.Anneal, true);
            LampRegion alternateRegion = null;
            int mismatchPosition = 0; char templateBase = '\0';
            if (snp != null)
            {
                LampRegion target = layout.Specific == "FIP" ? f2 : b2;
                char[] actual = target.Sequence.ToCharArray();
                char[] alternate = target.Sequence.ToCharArray();
                alternate[alternate.Length - 1] = target.Reverse ? Complement(snp.AlternateAllele) : snp.AlternateAllele;
                if (s.ExtraMismatchFromThreePrime > 0)
                {
                    int index = actual.Length - s.ExtraMismatchFromThreePrime;
                    if (index < 0) return null;
                    if (mismatch == actual[index]) return null;
                    mismatchPosition = target.Reverse ? target.End - index : target.Start + index;
                    templateBase = result.Input.Sequence[mismatchPosition - 1];
                    actual[index] = mismatch; alternate[index] = mismatch;
                }
                LampRegion changed = ModifiedRegion(target, new String(actual), s), alt = ModifiedRegion(target, new String(alternate), s);
                if (changed == null || alt == null) return null;
                if (layout.Specific == "FIP") f2 = changed; else b2 = changed;
                alternateRegion = alt;
            }
            LampPrimerSet set = new LampPrimerSet { SpecificInner = layout.Specific, SpanStart = left.Outer.Start,
                SpanEnd = right.Outer.End, SpanLength = right.Outer.End - left.Outer.Start + 1,
                ExtraMismatchPosition = mismatchPosition, ExtraMismatchTemplateBase = templateBase, ExtraMismatchPrimerBase = mismatch };
            set.ReferenceTemplate = result.Input.Sequence.Substring(set.SpanStart - 1, set.SpanLength);
            set.AlternateTemplate = snp == null ? null : snp.Alternate.Sequence.Substring(set.SpanStart - 1, set.SpanLength);
            set.F3 = Oligo("F3", s, cancellation, Region("F3", left.Outer, false));
            set.B3 = Oligo("B3", s, cancellation, Region("B3", right.Outer, true));
            set.FIP = Oligo("FIP", s, cancellation, f1, f2);
            set.BIP = Oligo("BIP", s, cancellation, b1, b2);
            if (set.F3 == null || set.B3 == null || set.FIP == null || set.BIP == null) return null;
            if (snp != null)
            {
                LampOligo reference = layout.Specific == "FIP" ? set.FIP : set.BIP;
                reference.SnpIndex = reference.Sequence.Length - 1;
                set.AlternateInner = layout.Specific == "FIP" ? Oligo("FIP_alt", s, cancellation, f1, alternateRegion)
                    : Oligo("BIP_alt", s, cancellation, b1, alternateRegion);
                if (set.AlternateInner == null) return null;
                set.AlternateInner.SnpIndex = set.AlternateInner.Sequence.Length - 1;
                set.Notes.Add("参考等位与替代等位分别使用独立反应：各自的 " + layout.Specific + " + 共用其余引物。不要把两条等位内引物混入同一管来判型。");
                set.Notes.Add("SNP 位于 " + layout.Specific + " 的 3′ 末端；" + (layout.Specific == "BIP"
                    ? "BIP 末端为输入正链等位碱基的互补碱基。" : "FIP 的 F2 末端对应输入正链等位碱基。") + "红色只标记 SNP 本身。");
                if (mismatchPosition > 0)
                    set.Notes.Add("探索性附加错配：模板正链坐标 " + mismatchPosition + "，模板正链碱基 " + templateBase
                        + "；按引物方向由 " + (layout.Specific == "BIP" ? Complement(templateBase) : templateBase) + " 改为 " + mismatch
                        + "。两个等位内引物使用相同附加碱基。参考 Tm 未建模该错配与模板配对的热力学影响。");
            }
            if (s.IncludeLoops)
            {
                set.LF = FindLoop("LF", left.Anneal.End + 1, left.Inner.Start - 1, true, catalog, s, cancellation);
                set.LB = FindLoop("LB", right.Inner.End + 1, right.Anneal.Start - 1, false, catalog, s, cancellation);
                if (set.LF == null || set.LB == null)
                    set.Notes.Add("未能为 " + (set.LF == null && set.LB == null ? "LF 和 LB" : set.LF == null ? "LF" : "LB") + " 找到符合当前长度、GC 和对应区域参考 Tm 的候选；四条核心引物仍构成基本 LAMP 设计。");
            }
            double penalty = ReactionPenalty(set, false, s, cancellation);
            if (set.AlternateInner != null) penalty = (penalty + ReactionPenalty(set, true, s, cancellation)) / 2.0;
            int coreLength = b2.End - f2.Start + 1;
            penalty += 0.05 * Math.Abs(coreLength - 140) + 0.01 * Math.Abs(set.SpanLength - 200);
            if (s.IncludeLoops) { if (set.LF == null) penalty += 2; if (set.LB == null) penalty += 2; }
            set.Score = DesignEngine.ScoreFromPenalty(penalty);
            if (coreLength < 120 || coreLength > 160) set.Notes.Add("F2 至 B2 外缘跨度为 " + coreLength + " nt，超出 PrimerExplorer 官方推荐范围 120–160 nt；当前按本次筛选范围探索。");
            if (f1.Tm < f2.Tm + 1 || b1.Tm < b2.Tm + 1) set.Notes.Add("至少一侧 F1c/B1c 参考 Tm 未比对应 F2/B2 高 1 °C；已降低排序，需实验核验。");
            AddCrossNotes(set, cancellation);
            return new Evaluated { Set = set, Penalty = penalty };
        }

        private static Evaluated MaterializePa(Layout layout, Catalog catalog, LampDesignResult result, char tailMismatch, CancellationToken cancellation)
        {
            LampDesignSettings s = result.Settings;
            SnpInput snp = result.Snp;
            Arm left = layout.Left, right = layout.Right;
            int precursorStart = snp.Position - s.PaTailLength;
            if (layout.Specific != "BIP" || right.Anneal.Start != snp.Position + 1 || precursorStart < 1
                || right.Inner.End >= precursorStart) return null;
            char originalTailBase = Complement(result.Input.Sequence[precursorStart - 1]);
            if (tailMismatch == originalTailBase || "ACGT".IndexOf(tailMismatch) < 0) return null;

            LampRegion f2 = Region("F2", left.Anneal, false), f1 = Region("F1c", left.Inner, true);
            LampRegion b1 = Region("B1c", right.Inner, false), activeB2 = Region("B2", right.Anneal, true);
            LampPrimerSet set = new LampPrimerSet { SpecificInner = "BIP", SpanStart = left.Outer.Start,
                SpanEnd = right.Outer.End, SpanLength = right.Outer.End - left.Outer.Start + 1,
                TailMismatchPosition = precursorStart, TailMismatchTemplateBase = result.Input.Sequence[precursorStart - 1],
                TailMismatchOriginalBase = originalTailBase, TailMismatchBase = tailMismatch };
            set.ReferenceTemplate = result.Input.Sequence.Substring(set.SpanStart - 1, set.SpanLength);
            set.AlternateTemplate = snp.Alternate.Sequence.Substring(set.SpanStart - 1, set.SpanLength);
            set.F3 = Oligo("F3", s, cancellation, Region("F3", left.Outer, false));
            set.B3 = Oligo("B3", s, cancellation, Region("B3", right.Outer, true));
            set.FIP = Oligo("FIP", s, cancellation, f1, f2);
            LampOligo activeBip = Oligo("BIP_active", s, cancellation, b1, activeB2);
            if (set.F3 == null || set.B3 == null || set.FIP == null || activeBip == null) return null;
            set.BIP = PaPrecursor("BIP", result.Input.Sequence, precursorStart, snp.Position, activeBip, tailMismatch, s, cancellation);
            set.AlternateInner = PaPrecursor("BIP_alt", snp.Alternate.Sequence, precursorStart, snp.Position, activeBip, tailMismatch, s, cancellation);
            if (set.BIP == null || set.AlternateInner == null) return null;
            if (s.IncludeLoops)
            {
                set.LF = FindLoop("LF", left.Anneal.End + 1, left.Inner.Start - 1, true, catalog, s, cancellation);
                // The common LB must avoid the entire blocked B2 precursor,
                // including the polymorphic RNA and all downstream DNA bases.
                set.LB = FindLoop("LB", right.Inner.End + 1, precursorStart - 1, false, catalog, s, cancellation);
                if (set.LF == null || set.LB == null)
                    set.Notes.Add("未能为 " + (set.LF == null && set.LB == null ? "LF 和 LB" : set.LF == null ? "LF" : "LB")
                        + " 找到合格且避开 PA-LAMP 前体区域的共用环引物；四条核心引物仍是基本候选配置。");
            }
            LampPrimerSet activated = new LampPrimerSet { F3 = set.F3, B3 = set.B3, FIP = set.FIP, BIP = activeBip, LF = set.LF, LB = set.LB };
            double activeRisk = StructureRisk(Reaction(activated, false), cancellation);
            double precursorRisk = (Math.Max(0, StructureRisk(Reaction(set, false), cancellation) - activeRisk)
                + Math.Max(0, StructureRisk(Reaction(set, true), cancellation) - activeRisk)) / 2.0;
            double penalty = ReactionPenalty(activated, false, s, cancellation) + precursorRisk;
            int coreLength = activeB2.End - f2.Start + 1;
            penalty += 0.05 * Math.Abs(coreLength - 140) + 0.01 * Math.Abs(set.SpanLength - 200);
            if (s.IncludeLoops) { if (set.LF == null) penalty += 2; if (set.LB == null) penalty += 2; }
            set.Score = DesignEngine.ScoreFromPenalty(penalty);
            set.Notes.Add("扩增方式：PA-LAMP（引物可激活 LAMP）。两条等位 BIP 前体各含一个与 SNP 配对的 RNA 碱基、其后 "
                + s.PaTailLength + " nt DNA 尾及 3′ C3 阻断。尾中前 " + (s.PaTailLength - 1)
                + " nt 配对，最后 1 nt 非互补；两个等位使用相同的尾末非互补碱基。参考等位与替代等位分别反应，共用 FIP、F3、B3 及已列出的环引物。");
            set.Notes.Add("前体尾末位：正链坐标 " + set.TailMismatchPosition + "，正链碱基 " + set.TailMismatchTemplateBase
                + "；按引物方向由 " + set.TailMismatchOriginalBase + " 改为 " + set.TailMismatchBase
                + "。此非互补位点位于会被移除的 3′ 阻断片段，不是 AS-LAMP 在有效引物 3′ 端引入的额外错配，也不标红。");
            set.Notes.Add("订购应使用标注 [rA]/[rC]/[rG]/[rU] 与 [C3] 的修饰序列，并与合成供应商确认其格式。无修饰的 DNA 等效字符串不能替代实际前体。RNA 配对输入正链 SNP "
                + snp.Position + "；若输入等位为 A，反向 RNA 应为 rU。");
            set.Notes.Add("RNase H2 在 RNA 的 5′ 侧切割：移除 RNA + DNA 尾 + C3 的 3′ 片段，留下可延伸 3′-OH。两种等位前体切后的活性 BIP 序列相同，活性 B2 在输入正链的边界为 "
                + activeB2.Start + "–" + activeB2.End + "；SNP 不再位于活性引物内。");
            set.Notes.Add("PA-LAMP 不是 AS-LAMP 的 3′ 末端 SNP / 人为错配策略，实际选择性依赖阻断完整性及 RNase H2 对匹配、错配底物的差异激活；本程序不预测酶切速率、激活效率、检出限或等位选择性。");
            set.Notes.Add("PA 评分先评估切后的有效 DNA 引物组，再增加阻断前体 DNA 等效结构风险高于活性组的部分（两个等位平均）。RNA、C3 及其对 Tm/结构/酶切的真实影响均未建模。");
            if (coreLength < 120 || coreLength > 160) set.Notes.Add("切后有效 F2 至 B2 外缘跨度为 " + coreLength + " nt，超出 PrimerExplorer 官方推荐范围 120–160 nt；当前按本次筛选范围探索。");
            if (f1.Tm < f2.Tm + 1 || b1.Tm < activeB2.Tm + 1)
                set.Notes.Add("至少一侧 F1c/B1c 参考 Tm 未比对应切后有效 F2/B2 高 1 °C，已作启发式降分。");
            AddCrossNotes(set, cancellation);
            return new Evaluated { Set = set, Penalty = penalty };
        }

        private static LampOligo PaPrecursor(string name, string template, int precursorStart, int snpPosition,
            LampOligo activated, char tailMismatch, LampDesignSettings settings, CancellationToken cancellation)
        {
            LampRegion activeB2 = activated.Regions[1];
            string sequence = DesignEngine.ReverseComplement(template.Substring(precursorStart - 1, activeB2.End - precursorStart + 1));
            char originalTailBase = sequence[sequence.Length - 1];
            sequence = sequence.Substring(0, sequence.Length - 1) + tailMismatch;
            // This check is intentionally DNA-equivalent. There is no RNA/DNA
            // mismatch thermodynamic model or C3 chemistry in these metrics.
            Window equivalent = MakeWindow(sequence, precursorStart, settings);
            if (equivalent == null) return null;
            LampRegion precursorB2 = new LampRegion { Name = "B2", Sequence = sequence, Start = precursorStart,
                End = activeB2.End, Reverse = true, Gc = equivalent.Gc, Tm = equivalent.Tm };
            LampOligo precursor = Oligo(name, settings, cancellation, activated.Regions[0], precursorB2);
            if (precursor == null) return null;
            precursor.SnpIndex = activated.Sequence.Length;
            precursor.RnaIndex = precursor.SnpIndex;
            precursor.RnaTemplatePosition = snpPosition;
            precursor.ActivationTailLength = settings.PaTailLength;
            precursor.ThreePrimeBlock = "C3";
            precursor.ActivatedSequence = activated.Sequence;
            precursor.ActivatedRegions.AddRange(activated.Regions);
            precursor.ActivatedMetrics = activated.Metrics;
            precursor.TailMismatchPosition = precursorStart;
            precursor.TailMismatchTemplateBase = template[precursorStart - 1];
            precursor.TailMismatchOriginalBase = originalTailBase;
            precursor.TailMismatchBase = tailMismatch;
            return precursor;
        }

        private static double StructureRisk(List<LampOligo> primers, CancellationToken cancellation)
        {
            double structure = 0, cross = 0; int pairs = 0;
            foreach (LampOligo p in primers) structure += StructurePenalty(p.Metrics);
            for (int i = 0; i < primers.Count; i++) for (int j = i + 1; j < primers.Count; j++)
            {
                int complement, threePrime;
                cross += DesignEngine.PairStructurePenalty(primers[i].Metrics, primers[j].Metrics, out complement, out threePrime, cancellation); pairs++;
            }
            return structure / primers.Count + cross / pairs;
        }

        private static LampRegion Region(string name, Window value, bool reverse)
        {
            return new LampRegion { Name = name, Sequence = reverse ? DesignEngine.ReverseComplement(value.Sequence) : value.Sequence,
                Start = value.Start, End = value.End, Reverse = reverse, Tm = value.Tm, Gc = value.Gc };
        }

        private static LampRegion ModifiedRegion(LampRegion original, string sequence, LampDesignSettings s)
        {
            Window actual = MakeWindow(sequence, original.Start, s);
            if (actual == null || !FitsTm(actual.Tm, s.GetTm(original.Name))) return null;
            return new LampRegion { Name = original.Name, Sequence = sequence, Start = original.Start, End = original.End,
                Reverse = original.Reverse, Tm = actual.Tm, Gc = actual.Gc };
        }

        private static LampOligo Oligo(string name, LampDesignSettings settings, CancellationToken cancellation, params LampRegion[] regions)
        {
            string sequence = "";
            foreach (LampRegion r in regions) sequence += r.Sequence;
            DesignSettings composition = new DesignSettings { PrimerMin = 1, PrimerMax = sequence.Length, GcMin = settings.GcMin, GcMax = settings.GcMax };
            DesignEngine.Candidate candidate = DesignEngine.CompositionCandidate(sequence, 1, composition);
            if (candidate == null) return null;
            DesignEngine.AddStructures(new List<DesignEngine.Candidate> { candidate }, cancellation);
            // A composite inner primer binds two separate regions; a single full-
            // oligo Tm against one contiguous target would be misleading.
            candidate.Primer.Tm = regions.Length == 1 ? regions[0].Tm : Double.NaN;
            candidate.Primer.Start = 0; candidate.Primer.End = 0;
            LampOligo value = new LampOligo { Name = name, Sequence = sequence, Metrics = candidate.Primer };
            value.Regions.AddRange(regions); return value;
        }

        private static LampOligo FindLoop(string name, int start, int end, bool reverse, Catalog catalog, LampDesignSettings settings, CancellationToken cancellation)
        {
            RoleCatalog section = catalog.For(name);
            List<Window> options = Range(section.Start, start, end, 0);
            options.RemoveAll(delegate(Window w) { return w.End > end; });
            options.Sort(delegate(Window a, Window b) { return CompareWindow(a, b, section.IdealTm); });
            LampOligo best = null; double bestPenalty = Double.MaxValue;
            for (int i = 0; i < Math.Min(6, options.Count); i++)
            {
                LampOligo value = Oligo(name, settings, cancellation, Region(name, options[i], reverse));
                if (value == null) continue;
                double penalty = RegionPenalty(options[i], section.IdealTm) + StructurePenalty(value.Metrics);
                if (penalty < bestPenalty) { best = value; bestPenalty = penalty; }
            }
            return best;
        }

        private static List<LampOligo> Reaction(LampPrimerSet set, bool alternate)
        {
            List<LampOligo> primers = new List<LampOligo> { set.F3, set.B3,
                alternate && set.SpecificInner == "FIP" ? set.AlternateInner : set.FIP,
                alternate && set.SpecificInner == "BIP" ? set.AlternateInner : set.BIP };
            if (set.LF != null) primers.Add(set.LF); if (set.LB != null) primers.Add(set.LB); return primers;
        }

        private static double StructurePenalty(Primer p)
        {
            return 0.6 * Math.Max(0, p.SelfComplement - 3) + 1.8 * Math.Max(0, p.SelfThreePrime - 2)
                + 1.2 * Math.Max(0, p.Hairpin - 3) + 0.5 * Math.Max(0, p.TandemRepeat - 8);
        }

        private static double ReactionPenalty(LampPrimerSet set, bool alternate, LampDesignSettings settings, CancellationToken cancellation)
        {
            List<LampOligo> primers = Reaction(set, alternate);
            double regionPenalty = 0, structure = 0, cross = 0; int regions = 0, pairs = 0;
            foreach (LampOligo primer in primers)
            {
                structure += StructurePenalty(primer.Metrics);
                foreach (LampRegion r in primer.Regions)
                {
                    double tm = TargetTm(settings, r.Name);
                    regionPenalty += 0.12 * Math.Abs(r.Gc - 50) + 0.2 * Math.Abs(r.Sequence.Length - 22) + 0.8 * Math.Abs(r.Tm - tm); regions++;
                }
            }
            for (int a = 0; a < primers.Count; a++) for (int b = a + 1; b < primers.Count; b++)
            { int complement, threePrime; cross += DesignEngine.PairStructurePenalty(primers[a].Metrics, primers[b].Metrics, out complement, out threePrime, cancellation); pairs++; }
            LampOligo fip = alternate && set.SpecificInner == "FIP" ? set.AlternateInner : set.FIP;
            LampOligo bip = alternate && set.SpecificInner == "BIP" ? set.AlternateInner : set.BIP;
            double tmPenalty = 0.8 * (Math.Abs(fip.Regions[1].Tm - bip.Regions[1].Tm)
                + Math.Max(0, fip.Regions[1].Tm + 1 - fip.Regions[0].Tm) + Math.Max(0, bip.Regions[1].Tm + 1 - bip.Regions[0].Tm));
            return regionPenalty / regions + structure / primers.Count + cross / pairs + tmPenalty;
        }

        private static void AddCrossNotes(LampPrimerSet set, CancellationToken cancellation)
        {
            for (int reaction = 0; reaction < (set.AlternateInner == null ? 1 : 2); reaction++)
            {
                List<LampOligo> primers = Reaction(set, reaction == 1);
                int maximum = 0, three = 0; string pair = "";
                for (int i = 0; i < primers.Count; i++) for (int j = i + 1; j < primers.Count; j++)
                {
                    int c, t; DesignEngine.PairStructurePenalty(primers[i].Metrics, primers[j].Metrics, out c, out t, cancellation);
                    maximum = Math.Max(maximum, c);
                    if (t > three) { three = t; pair = primers[i].Name + " / " + primers[j].Name; }
                }
                if (maximum >= 8 || three >= 4)
                    set.Notes.Add((set.AlternateInner == null ? "本组" : reaction == 0 ? "参考等位反应" : "替代等位反应")
                        + "：任意两条引物最长连续互补 " + maximum + " nt，涉及 3′ 端的最长连续互补 " + three + " nt（" + pair + "）。需进一步核验二聚体。");
            }
        }

        private static void Select(List<Evaluated> evaluated, LampDesignResult result)
        {
            HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
            List<Evaluated> kept = new List<Evaluated>();
            if (result.Snp != null && result.Settings.SnpOrientation == "Auto" && result.Settings.MaxSets >= 2)
                foreach (string direction in new string[] { "FIP", "BIP" })
                    foreach (Evaluated value in evaluated) if (value.Set.SpecificInner == direction)
                    { kept.Add(value); selected.Add(Signature(value.Set)); break; }
            // First spread ordinary candidates over the template; then fill with
            // the best remaining distinct primer sequences if space remains.
            for (int pass = 0; pass < 2 && kept.Count < result.Settings.MaxSets; pass++)
                foreach (Evaluated value in evaluated)
                {
                    if (kept.Count == result.Settings.MaxSets) break;
                    string signature = Signature(value.Set);
                    if (selected.Contains(signature)) continue;
                    bool near = false;
                    if (pass == 0 && result.Snp == null) foreach (Evaluated previous in kept)
                        if (Math.Abs(previous.Set.FIP.Regions[1].Start - value.Set.FIP.Regions[1].Start) < 12) { near = true; break; }
                    if (near) continue;
                    kept.Add(value); selected.Add(signature);
                }
            kept.Sort(delegate(Evaluated a, Evaluated b) { return a.Penalty.CompareTo(b.Penalty); });
            foreach (Evaluated value in kept) { value.Set.Rank = result.Sets.Count + 1; result.Sets.Add(value.Set); }
        }

        private static string Signature(LampPrimerSet s)
        {
            return s.SpecificInner + ":" + s.F3.Sequence + ":" + s.B3.Sequence + ":" + s.FIP.Sequence + ":" + s.BIP.Sequence
                + ":" + (s.AlternateInner == null ? "" : s.AlternateInner.Sequence);
        }

        private static char Complement(char value) { return DesignEngine.ReverseComplement(value.ToString())[0]; }
        private static void Report(Action<int, string> callback, int percent, string message) { if (callback != null) callback(percent, message); }

        private static void AddMethodNotes(LampDesignResult result)
        {
            bool pa = result.Snp != null && result.Settings.SnpMethod == "PA-LAMP";
            bool mlamp = result.Snp != null && result.Settings.SnpMethod == "mLAMP";
            result.Notes.Add("具体扩增方式：" + (result.Snp == null ? "常规 LAMP" : pa ? "PA-LAMP（引物可激活 LAMP，RNase H2 / RNA / C3）"
                : mlamp ? "mLAMP（Ren 2019，FIP 人工错配型）" : "AS-LAMP（等位基因特异性 LAMP，内引物 3′ 末端判别）") + "。");
            result.Notes.Add("LAMP 四条核心引物为 F3、B3、FIP=F1c+F2、BIP=B1c+B2；可附加 LF/LB 环引物。所有订购序列均为实际 5′→3′ 序列，无连接符、无额外 linker。");
            result.Notes.Add("六个核心区域沿输入正链为 F3、F2、F1、B1c、B2c、B3c，互不重叠。F2→F1 的对应 5′ 端距离及反向一侧均为 40–60 nt，外引物与相邻内区间隙为 0–60 nt。F2 至 B2 外缘跨度包含两端，独立使用设置的最短/最长界限；软件预设 100–220 nt，PrimerExplorer 官方推荐 120–160 nt。F3 至 B3 外缘跨度另行设置。");
            result.Notes.Add("显示的序列是 F3 至 B3 的原始模板片段，不是完整 LAMP 扩增产物。LAMP 可生成不同长度的茎环、串联重复产物，不能用一条固定线性序列表示。");
            result.Notes.Add("参考 Tm：SantaLucia 1998 DNA 最近邻参数，单价盐 " + result.Settings.MonovalentMilliMolar.ToString("0.###", CultureInfo.InvariantCulture)
                + " mM、总寡核苷酸浓度 " + result.Settings.OligoNanoMolar.ToString("0.###", CultureInfo.InvariantCulture)
                + " nM、Mg²⁺ " + result.Settings.MagnesiumMilliMolar.ToString("0.###", CultureInfo.InvariantCulture)
                + " mM；以 [Na⁺]eq=[Na⁺]+4√[Mg²⁺]（浓度以 M 计）换算后，仅采用熵盐校正 0.368×(长度−1)×ln[Na⁺]eq。Mg²⁺ 为近似等效盐处理，未计算游离 Mg²⁺、dNTP 螯合、添加剂或错配双链热力学；不是反应温度建议，也不等同于 PrimerExplorer 的完整实现。FIP/BIP 分区域显示 Tm。");
            result.Notes.Add("GC、Tm、长度及间距用于生成待验证候选。" + (pa ? "切后 DNA 引物及阻断前体的 DNA 等效字符串" : "完整合成引物") + "会检查连续同聚物、自互补、发卡及同一反应内所有引物对的连续互补；这些是简化序列指标，不是热力学 ΔG 或全基因组特异性筛查。");
            result.Notes.Add("分数是本程序自定义的候选排序，未获权威机构认证，不能解释为扩增成功率或 SNP 选择性。有限搜索和候选多样性处理可能跳过更高分方案。");
            result.Notes.Add("评分公式：Score=100/(1+P/25)。每个反应的 P 为区域惩罚均值 + 完整引物结构惩罚均值 + 反应内引物对互补惩罚均值 + Tm 配平惩罚。区域惩罚=0.12×|GC%−50|+0.2×|区域长度−22|+0.8×|Tm−目标Tm|；各区域 Tm 目标为所设有效范围的中点；任意一端无限制时，F1c/B1c/LF/LB 回退为65°C，其余为60°C。此目标统一用于窗口、布局和最终候选排序。结构惩罚=0.6×max(自互补−3,0)+1.8×max(3′自互补−2,0)+1.2×max(发卡茎−3,0)+0.5×max(串联重复跨度−8,0)；引物对惩罚=0.8×max(连续互补−3,0)+2.5×max(3′连续互补−2,0)。");
            result.Notes.Add("Tm 配平惩罚=0.8×[|Tm(F2)−Tm(B2)|+max(Tm(F2)+1−Tm(F1c),0)+max(Tm(B2)+1−Tm(B1c),0)]。AS-LAMP / mLAMP 先平均两个独立等位反应的 P；然后各模式均加 0.05×|有效F2..B2跨度−140|+0.01×|F3..B3跨度−200|；请求环引物时每缺少一条再加2。这些权重和偏好值均为程序自定义，不是文献验证的预测模型。");
            result.Notes.Add("方法参考：Eiken PrimerExplorer V5 手册 https://primerexplorer.jp/e/v5_manual/pdf/PrimerExplorerV5_Manual_1.pdf ；SantaLucia 1998 最近邻模型 https://pmc.ncbi.nlm.nih.gov/articles/PMC19045/ 。PrimerExplorer 官方推荐 GC40–65%、F3/B3/F2/B2 Tm59–61°C、F1c/B1c/LF/LB Tm64–66°C、F2 至 B2 跨度120–160 nt；本软件为增加候选搜索空间，初始预设单区域长度17–30 nt、GC30–75%、对应 Tm52–68°C 与58–72°C、F2 至 B2 跨度100–220 nt、F3 至 B3 跨度110–350 nt。这些允许范围是程序的候选探索设置，Ren 2019 未报告通用 Tm 或 ΔG 阈值。实际筛选始终按当前设置执行，不会自动放宽用户手动设置；搜索、结构评价及 Mg²⁺ 近似处理为本程序实现，不等同于 PrimerExplorer。");
            if (pa)
            {
                result.Notes.Add("PA-LAMP 仅提供 BIP 激活方向。先在 SNP 正链坐标+1 处结束切后有效 B2 的 3′ 端，再在前体中依次添加与 SNP 配对的一个 RNA、" + result.Settings.PaTailLength
                    + " nt DNA 尾及 3′ C3 阻断。DNA 尾前 " + (result.Settings.PaTailLength - 1)
                    + " nt 配对、最后 1 nt 非互补；不添加 AS-LAMP 的有效引物 3′ 端额外错配。SNP 位于前体内部 RNA，两个等位前体切后活性序列相同。");
                result.Notes.Add("PA 模式的区域长度与各对应区段 Tm 参数约束切后有效 DNA 区域（B2 指切后有效片段）。RNA 后 DNA 尾及 SNP 附加在前体中，因此前体 B2 区域会比有效 B2 长；其 Tm/GC 仅按实际前体的 DNA 等效字符串计算，其中 Tm 假设完全匹配互补链，未计算 RNA/DNA 杂交、尾端错配双链、C3 或 RNase H2 作用。");
                result.Notes.Add("PA 评分以相同切后有效引物组的 DNA 基础 P 为起点。令 R=完整引物结构惩罚均值+同管引物对互补惩罚均值，再加两个等位前体各自 max(0,R前体DNA等效−R活性DNA) 的平均值；最后加上述跨度和缺失环引物惩罚。不对前体 RNA、C3、激活效率或 SNP 选择性作预测。");
                result.Notes.Add("PA 附加搜索限制：RNA 后 DNA 尾须有足够的 A/C/G/T 模板；前体完整 B2 不可覆盖 B1c，共用 LB 只能位于 B1c 与该前体之间，全部共用引物避开 SNP。每个入围布局枚举尾末位其余 3 种非互补碱基进行 DNA 等效结构排序，不以酶切选择性排序。可用共用环引物数量可能因此减少。原有有限布局搜索仍适用，未穷尽所有 PA 构型。");
                result.Notes.Add("PA-LAMP 方法依据：Du 等，Analytica Chimica Acta，DOI 10.1016/j.aca.2018.10.068，https://doi.org/10.1016/j.aca.2018.10.068 。本程序使用其 RNA / 3′ C3 / RNase H2 激活机制构建待验证候选，不等同于复现某个已验证实验体系。");
                result.Notes.Add("DNA 尾默认 5 nt、可探索 4–7 nt（均包括末位非互补碱基），依据 Du 原文补充材料 Table S1 的 BIP-a/b/c/d 及 Figure S5 中 BIP-b 的比较结果；末位非互补来自补充材料序列与模板的比对，非原文明示的普适必需规则。将该构型和三种末位替换推广到其他 SNP 是本程序的工程探索，不代表通用最优值。原始补充材料：https://ars.els-cdn.com/content/image/1-s2.0-S0003267018313242-mmc1.doc 。");
            }
            else if (mlamp)
            {
                int position = result.Settings.ExtraMismatchFromThreePrime;
                result.Notes.Add("mLAMP 固定 FIP：F1c + F2，F2 的 3′ 最末位对应 SNP。两种等位 FIP 分别与共用 BIP、F3、B3 组成独立反应；均为普通 DNA 引物。");
                result.Notes.Add("当前错配方案：" + (position == 0 ? "FIP₀，不加人工错配（论文对照）。"
                    : "FIP" + (position == 2 ? "₂" : "₃") + "，在 3′ 倒数第 " + position + " 位加入人工错配（" + (position == 3 ? "文献默认" : "比较方案") + "）。"));
                result.Notes.Add(position == 0 ? "此对照仅依赖 SNP 末端差异；Bst 对末端单错配仍可能延伸，不能保证区分等位基因。"
                    : "目标模板与 F2 保留一处人工错配；非目标模板另有 3′ 末端 SNP 错配，共两处。该策略旨在抑制和延迟非目标延伸，不保证其始终不扩增。");
                result.Notes.Add("Ren 等比较无人工错配、倒数第 2 位及第 3 位方案，在所测序列及条件下选择第 3 位；不代表所有 SNP 的最优位置。本程序枚举该位置其余三种碱基并按序列结构排序，是待验证的候选扩展，未预测错配选择性、时间差或检测限。");
                result.Notes.Add(result.Settings.IncludeLoops ? "已请求 LF/LB 环引物：这是相对于原文四引物体系的扩展，实际找到的环引物以候选清单为准。"
                    : "采用原文四条核心引物的配置，不附加 LF/LB 环引物。");
                result.Notes.Add("mLAMP 方法依据：Ren 等，ChemistrySelect 2019, 4, 1423–1427，Scheme 1 / Figure 1，https://doi.org/10.1002/slct.201802693 。具体错配碱基、搜索参数和新位点性能需独立验证；本程序不复现补充材料中的具体引物序列。");
            }
            else if (result.Snp != null)
            {
                result.Notes.Add("AS-LAMP 将等位差异放在 FIP 的 F2 或 BIP 的 B2 的 3′ 末端，生成两个独立等位反应所需内引物和共用引物。仅 3′ 单碱基差异并不保证选择性，应比较两个等位模板及阴性对照。");
                result.Notes.Add("Badolo 等研究过 BIP 末端 SNP 与附加错配；Ren 等 mLAMP 研究过 FIP 末端 SNP 与倒数第 2/3 位人工错配（https://doi.org/10.1002/slct.201802693）。这些结果来自特定位点，本程序对新位点及错配碱基的候选搜索未预测或保证等位识别能力。");
                result.Notes.Add("AS-LAMP 参考：Badolo 等，2012，https://link.springer.com/article/10.1186/1475-2875-11-227 。该研究不构成本软件或所有 SNP 位点的性能认证。");
            }
        }
    }
}
