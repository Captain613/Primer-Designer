using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RpaDesigner
{
    public static class LampReportWriter
    {
        public const string ManualUrl = "https://www.primerexplorer.jp/e/v5_manual/02.html";
        public const string SnpManualUrl = "https://primerexplorer.jp/e/v5_manual/pdf/PrimerExplorerV5_Manual_3.pdf";
        public const string TmUrl = "https://www.primerexplorer.jp/e/v5_manual/03.html";
        public const string EvidenceUrl = "https://pmc.ncbi.nlm.nih.gov/articles/PMC3407793/";
        public const string PaEvidenceUrl = "https://doi.org/10.1016/j.aca.2018.10.068";
        public const string PaSupplementUrl = "https://ars.els-cdn.com/content/image/1-s2.0-S0003267018313242-mmc1.doc";
        public const string MLampEvidenceUrl = "https://doi.org/10.1002/slct.201802693";
        public const string MLampMethod = "mLAMP（Ren 等，ChemistrySelect 2019）：保留 FIP、BIP、F3、B3 四引物六区段框架，固定由 FIP 的 F2 区段识别 SNP，SNP 对应 FIP 的 3′ 最末位。FIP = F1c + F2，BIP = B1c + B2；订购序列为普通 DNA，无 RNA 或 C3 修饰。\n默认在 FIP 倒数第 3 位加入人工错配；也可选择不加错配（论文 Mut-FIP0 对照）或倒数第 2 位（Mut-FIP2 比较方案）。末端碱基计作第 1 位，倒数第 3 位方案对应 Mut-FIP3。\n加入人工错配时，目标模板与 FIP 保留一处人工错配；非目标模板另有 3′ 末端 SNP 错配，共两处错配。其思路是抑制非目标初始延伸、延迟背景扩增，不保证非目标模板绝不扩增。无人工错配的对照不具有这种双错配结构。\n参考和替代等位基因分别使用对应 FIP，与共用 BIP/F3/B3 组成两个独立反应；每个反应只加入对应的一条等位 FIP，未自动设计通用扩增对照。参考/替代标签不自动表示野生型/突变型。\n本模式默认不使用环引物，符合主文四引物框架；启用 LF/LB 属于本程序扩展，需另行验证。程序枚举错配位置另外三种 DNA 碱基并按结构筛选，是新位点的候选探索，不是原论文给出的通用最优碱基规则。\n靶区模板保留输入的原始序列，人为错配只写入订购引物；坐标从 1 开始，订购序列为 5′→3′。F3 至 B3 区间不是固定长度的最终扩增产物。未做全基因组特异性检索或湿实验验证。";
        public const string MLampScoring = "mLAMP 排序分数依据区段组成、Tm、间距与连续互补等启发式指标，不预测 SNP 选择性、聚合酶错配延伸效率、背景延迟或扩增成功率。Score = 100/(1+P/25)；两种独立反应的惩罚先求平均，再加入间距和请求但未获得环引物的惩罚。\n区段 Tm 使用 SantaLucia 1998 最近邻参数及 Mg²⁺ 等效单价盐熵校正，按合成序列的完全匹配互补链估算；没有计算人工错配与实际模板的双链 Tm，不为整条拼接 FIP/BIP 提供单一结合 Tm。软件搜索默认值不是原论文反应条件或 PrimerExplorer 官方默认条件，显示 Tm 不是建议反应温度。\nRen 等主文第 1424–1425 页 Scheme 1 / Figure 1 比较无人工错配、倒数第 2 位及第 3 位，并在所测靶点及条件下选择第 3 位方案。本程序将该思路用于用户输入的新位点，具体错配碱基、候选排序和可选环引物不构成原文性能复现；须比较已知两种等位模板及实验对照。";
        public const string PaMethod = "PA-LAMP（Primer-activatable LAMP，引物激活型环介导等温扩增）：本版使用 BIP 作为可激活内引物。订购序列结构为 B1c + 有效 B2 DNA + SNP 对应的单个 RNA + 尾部 DNA（最后一位人为错配）+ 3′ C3 spacer。\n[rA]、[rC]、[rG]、[rU] 表示一个 RNA 核苷酸；[C3] 表示 3′ 端 C3 spacer 封闭修饰，不是 DNA 碱基。该标记为通用说明写法，订购时需按供应商修饰格式确认。\n需要支持该反应的 RNase H2 与链置换 DNA 聚合酶。RNase H2 在 RNA 的 5′ 侧切割，释放 RNA、尾部及封闭基团，留下具有 3′-OH 的有效 BIP；切后活性引物不含 SNP。选择性来自切割激活过程，不能由普通 DNA 引物末端错配评分预测。\n两个等位基因分别使用对应的修饰 BIP，与共用 FIP/F3/B3 及可选环引物组成两个独立反应；本版未设计通用扩增对照。\n尾部默认 5 nt（4 个匹配碱基 + 1 个末位人为错配），可选 4–7 nt，参考 Du 等原始论文补充材料；错配碱基由本程序枚举筛选，并非原文对任意位点的通用规则。通用位点、参数及候选没有湿实验验证，不保证非目标等位基因完全不扩增。\n靶区模板为输入正链的 F3 至 B3 原始区间，不是固定长度的 LAMP 最终产物。坐标从 1 开始，订购序列为 5′→3′；未进行全基因组特异性检索。";
        public const string PaScoring = "PA-LAMP 分数仅用于候选排序，不代表 RNase H2 切割效率、SNP 选择性或扩增成功率。有效 B2 的长度与 Tm 按切割后保留的 DNA 片段筛选；修饰前体的序列结构仅按 DNA 等效序列近似检查。RNA/DNA 杂交、C3 封闭、切割动力学及切后引物的实际扩增效率未建模。\n尾部长度参考原始研究的 4–7 nt 结构，默认 5 nt；尾部最后一位为人为错配，具体碱基及新位点仍需验证。请结合已知两种等位模板和实验对照筛选候选。";
        public static bool IsPa(LampDesignResult r) { return r.Snp != null && r.Settings.SnpMethod == "PA-LAMP"; }
        public static bool IsMLamp(LampDesignResult r) { return r.Snp != null && r.Settings.SnpMethod == "mLAMP"; }
        public static string ModeName(LampDesignResult r) { return r.Snp == null ? "普通 LAMP" : IsPa(r) ? "PA-LAMP · 引物激活型" : IsMLamp(r) ? "mLAMP · Ren 2019 人工错配型" : "AS-LAMP · 等位基因特异性"; }
        public static string MethodFor(LampDesignResult r) { return IsPa(r) ? PaMethod : IsMLamp(r) ? MLampMethod : Method; }
        public static string ScoringFor(LampDesignResult r) { return IsPa(r) ? PaScoring : IsMLamp(r) ? MLampScoring : Scoring; }
        public const string Method = "LAMP 核心为 F3、B3、FIP、BIP 四条引物识别六个区段；FIP 按 5′→3′ 为 F1c + F2，BIP 为 B1c + B2。LF/LB 为可选环引物；未找到的环引物不表示核心组无效。\n本报告的“靶区模板”是输入正链上 F3 至 B3 的原始区间，不是固定长度的最终扩增产物；LAMP 可形成茎环及串联、分支产物。附加人为错配只存在于订购引物中，不改写这里的原始模板。\nAS-LAMP（等位基因特异性 LAMP）把位点放在 FIP 的 F2 或 BIP 的 B2 区段 3′ 端；参考、替代等位基因的两条内引物分别与其余共用引物组成两个独立反应。没有自动生成通用扩增对照，不能把两条等位内引物同时加入同管作为已验证分型体系。\n末端 SNP 和可选附加错配均为待验证设计策略，不能保证另一等位基因不扩增，也不预测判别比或检测限。需用已知两种等位模板及实验对照验证。\n坐标为输入参考正链 1-based 闭区间；订购序列全部为 5′→3′。设计阶段不进行数据库检索；可另行启用在线 BLAST。未进行完整发卡/二聚体自由能计算。";
        public const string Scoring = "分数用于比较有限搜索中得到的候选，基于区段组成、Tm、间距与连续互补等启发式指标；不是扩增成功率或 SNP 选择性。Score = 100/(1+P/25)，P 为组成、结构及间距惩罚；SNP 模式先对两种独立反应的惩罚求平均，之后加入间距和请求但未获得环引物的惩罚，不是把两个显示分数求平均。\nTm 使用 SantaLucia 1998 最近邻参数及 Mg²⁺ 等效单价盐熵校正，每个结合区段分别计算，表示该合成序列与完全匹配互补链的估算值；包含人为错配的引物与实际模板之间的错配双链 Tm 未计算。FIP/BIP 是两段拼接的寡核苷酸，不为整条拼接引物给出单一结合 Tm。实际 LAMP 反应受盐、镁、dNTP、酶及体系影响；显示的区段 Tm 不是建议反应温度。\n默认区段 Tm、GC 与 F2..B2 跨度参考 PrimerExplorer V5 常规序列建议；参考条件为 Na⁺ 50 mM、Mg²⁺ 4 mM、寡核苷酸 100 nM。当前仍采用 SantaLucia 1998 参数及熵盐校正，不等同于 PrimerExplorer 引用的 1996 参数和 16.6×log[Na⁺] 公式，不保证与其得到相同的数值或候选。PrimerExplorer V5 手册第 6.2 节支持在 F2/B2 的 3′ 端安排差异以探索区分等位基因；实际选择性需验证。Badolo 等 AS-LAMP 研究验证了特定位点的 BIP 附加错配方案；Ren 等 2019 mLAMP 研究验证了特定条件下 FIP 倒数第 3 位方案。上述证据均不证明任意新位点或错配碱基普遍有效，程序生成的候选仍属待验证探索。";

        private static string N(double value) { return value.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string Bound(int value, bool unlimited) { return unlimited ? "无限制" : value.ToString(CultureInfo.InvariantCulture); }
        private static string Bound(double value, bool unlimited) { return unlimited ? "无限制" : N(value); }
        private static string Lf(string text) { return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); }
        private static string E(string value) { return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;"); }

        public static string ExampleFasta() { return ReportWriter.ExampleFasta(); }
        public static string ExampleSnpFasta()
        {
            string sequence = SequenceParser.Parse(ExampleFasta()).Sequence;
            // This artificial SNP gives examples for all supported LAMP SNP
            // modes under the reference defaults; user sequences are untouched.
            const int index = 290;
            return ">synthetic_LAMP_SNP_demo 非生物来源；第291位 T>A\r\n" + sequence.Substring(0, index) + "[T>A]" + sequence.Substring(index + 1) + "\r\n";
        }
        public static string Params(LampDesignSettings s)
        {
            if (s == null) throw new ArgumentNullException("s");
            string summary = (s.SnpMethod == "PA-LAMP" ? "PA-LAMP：BIP 激活；RNA 后尾部 DNA " + s.PaTailLength + " nt；3′ C3 封闭；下列 B2 长度/Tm 指切后有效片段。\n" : s.SnpMethod == "mLAMP" ? "mLAMP（Ren 2019）：FIP 的 F2 末端识别 SNP；普通 DNA 引物；" + (s.ExtraMismatchFromThreePrime == 0 ? "无人工错配（Mut-FIP0 对照）" : "倒数第 " + s.ExtraMismatchFromThreePrime + " 位人工错配（Mut-FIP" + s.ExtraMismatchFromThreePrime + "）") + "；" + (s.IncludeLoops ? "已启用环引物（程序扩展）" : "四引物框架，不加环引物") + "。\n" : "") +
                "结合区段：最短 " + Bound(s.RegionMin, s.RegionMinUnlimited) + " / 最长 " + Bound(s.RegionMax, s.RegionMaxUnlimited) + " nt；GC：最低 " + Bound(s.GcMin, s.GcMinUnlimited) + " / 最高 " + Bound(s.GcMax, s.GcMaxUnlimited) + "%；F3..B3 靶区跨度：最短 " + Bound(s.SpanMin, s.SpanMinUnlimited) + " / 最长 " + Bound(s.SpanMax, s.SpanMaxUnlimited) + " nt；请求 " + s.MaxSets + " 组。\n" +
                "F2..B2 跨度（含两端）：最短 " + Bound(s.CoreSpanMin, s.CoreSpanMinUnlimited) + " / 最长 " + Bound(s.CoreSpanMax, s.CoreSpanMaxUnlimited) + " nt。\n";
            bool tmUnlimited = false;
            foreach (string role in new string[] { "F3", "B3", "F2", "B2", "F1c", "B1c", "LF", "LB" })
            {
                LampTmRange range = s.GetTm(role);
                summary += role + " Tm：最低 " + Bound(range.Min, range.MinUnlimited) + " / 最高 " + Bound(range.Max, range.MaxUnlimited) + " °C" + ((role == "LF" || role == "LB") && !s.IncludeLoops ? "（本次未请求）" : "") + "。\n";
                tmUnlimited |= range.MinUnlimited || range.MaxUnlimited;
            }
            summary += "Tm 参考条件：单价盐 " + N(s.MonovalentMilliMolar) + " mM；Mg²⁺ " + N(s.MagnesiumMilliMolar) + " mM；寡核苷酸 " + N(s.OligoNanoMolar) + " nM。采用 SantaLucia 1998 最近邻模型及 Mg²⁺ 等效单价盐熵校正，不是实验配方，也不等同于 PrimerExplorer 的 Tm 算法。F2c 是 F2 的互补区，同一完全匹配模型下共用 F2 Tm。\n尝试环引物：" + (s.IncludeLoops ? "是" : "否") + "；SNP 方向：" + s.SnpOrientation + "；附加错配：" + (s.ExtraMismatchFromThreePrime == 0 ? "无" : "距 3′ 端第 " + s.ExtraMismatchFromThreePrime + " 位") + "。";
            if (s.RegionMinUnlimited || s.RegionMaxUnlimited || s.SpanMinUnlimited || s.SpanMaxUnlimited || s.CoreSpanMinUnlimited || s.CoreSpanMaxUnlimited || s.GcMinUnlimited || s.GcMaxUnlimited || tmUnlimited)
                summary += "\n无限制表示关闭对应的用户筛选边界；模板长度、LAMP 区段顺序和间距等可行性条件及有限候选搜索仍保留，不表示穷举所有组合。";
            if (s.RegionMinUnlimited || s.RegionMaxUnlimited)
                summary += "区段长度范围较宽时采用有限长度采样，裁剪情况见搜索裁剪及设计说明。";
            return summary;
        }

        public static List<LampOligo> Oligos(LampPrimerSet set)
        {
            if (set == null) throw new ArgumentNullException("set");
            var result = new List<LampOligo>();
            if (set.FIP != null) result.Add(set.FIP);
            if (set.SpecificInner == "FIP" && set.AlternateInner != null) result.Add(set.AlternateInner);
            if (set.BIP != null) result.Add(set.BIP);
            if (set.SpecificInner == "BIP" && set.AlternateInner != null) result.Add(set.AlternateInner);
            if (set.F3 != null) result.Add(set.F3);
            if (set.B3 != null) result.Add(set.B3);
            if (set.LF != null) result.Add(set.LF);
            if (set.LB != null) result.Add(set.LB);
            return result;
        }
        public static string OligoName(LampOligo p, LampPrimerSet set, LampDesignResult r)
        {
            string role = Object.ReferenceEquals(p, set.FIP) ? "FIP" : Object.ReferenceEquals(p, set.BIP) ? "BIP" :
                Object.ReferenceEquals(p, set.F3) ? "F3" : Object.ReferenceEquals(p, set.B3) ? "B3" :
                Object.ReferenceEquals(p, set.LF) ? "LF" : Object.ReferenceEquals(p, set.LB) ? "LB" : set.SpecificInner;
            if (r.Snp == null) return role;
            if (Object.ReferenceEquals(p, set.AlternateInner)) return role + "_alt_" + r.Snp.AlternateAllele;
            if (role == set.SpecificInner) return role + "_ref_" + r.Snp.ReferenceAllele;
            return role + "_common";
        }
        public static string OligoNote(LampOligo p, LampPrimerSet set, LampDesignResult r)
        {
            if (IsPa(r) && p.RnaIndex >= 0)
                return "PA-LAMP 修饰 BIP；红色为与输入 SNP 互补的 RNA 碱基（U 为 RNA 碱基）。[C3] 为必需的 3′ 封闭修饰。\n" + PaTailMismatchText(set) + "\nRNase H2 在 RNA 的 5′ 侧切割；切后活性 BIP：" + p.ActivatedSequence + "（3′-OH，不含 SNP）。\n需订购上方完整修饰序列，不能将 DNA 等效序列或切后序列替代为订购引物。";
            string note;
            if (r.Snp != null && p.SnpIndex >= 0)
            {
                char allele = Object.ReferenceEquals(p, set.AlternateInner) ? r.Snp.AlternateAllele : r.Snp.ReferenceAllele;
                note = "对应输入正链等位基因 " + allele + "；红色为 SNP 结合碱基。";
                if (set.SpecificInner == "BIP") note += " BIP 的末端为正链 SNP 的互补碱基。";
                if (set.ExtraMismatchPosition > 0) note += " 附加人为错配位于正链坐标 " + set.ExtraMismatchPosition + (IsMLamp(r) ? "（蓝色）。" : "（不标红）。");
                if (IsMLamp(r)) note += set.ExtraMismatchPosition > 0 ? " mLAMP FIP：目标模板保留人工错配；非目标模板另有末端 SNP 错配。抑制表现需实验验证。" : " mLAMP FIP：无人工错配对照，仅由 3′ 末端 SNP 区分。";
            }
            else note = r.Snp == null ? "用于此组 LAMP 反应。" : "两种独立等位反应共用；避开当前 SNP。";
            if (p.Regions.Count > 1) note += " 订购序列已按 " + p.Regions[0].Name + " + " + p.Regions[1].Name + " 直接拼接，不含连接符。";
            return note;
        }
        public static string MismatchText(LampPrimerSet set)
        {
            return MismatchText(set, null);
        }
        public static string MismatchText(LampPrimerSet set, LampDesignResult r)
        {
            if (set.ExtraMismatchPosition <= 0) return "未加入人为错配。";
            char original = set.ExtraMismatchTemplateBase;
            if (set.SpecificInner == "BIP") original = DesignEngine.ReverseComplement(original.ToString())[0];
            return "附加人为错配：原始正链第 " + set.ExtraMismatchPosition + " 位为 " + set.ExtraMismatchTemplateBase + "；按订购引物方向，原碱基 " + original + " → 合成碱基 " + set.ExtraMismatchPrimerBase + (r != null && IsMLamp(r) ? "。订购引物中以蓝色标记；SNP 仍为红色；原始靶区模板保持不变。" : "。此处不是 SNP，因此不标红；原始靶区模板保持不变。");
        }
        public static int MismatchIndex(LampOligo p, LampPrimerSet set, LampDesignResult r)
        {
            if (p == null || set == null || r == null || !IsMLamp(r) || set.SpecificInner != "FIP"
                || set.ExtraMismatchPosition <= 0 || p.RnaIndex >= 0 || p.SnpIndex < 0
                || (!Object.ReferenceEquals(p, set.FIP) && !Object.ReferenceEquals(p, set.AlternateInner))) return -1;
            int offset = 0;
            foreach (LampRegion region in p.Regions)
            {
                if (region.Name == "F2" && set.ExtraMismatchPosition >= region.Start && set.ExtraMismatchPosition <= region.End)
                {
                    int index = offset + (region.Reverse ? region.End - set.ExtraMismatchPosition : set.ExtraMismatchPosition - region.Start);
                    if (index >= 0 && index < p.OrderingSequence.Length && index != p.OrderingSnpIndex
                        && p.OrderingSequence[index] == set.ExtraMismatchPrimerBase) return index;
                    return -1;
                }
                offset += region.Sequence.Length;
            }
            return -1;
        }
        public static string PaTailMismatchText(LampPrimerSet set)
        {
            return "尾部末位人为错配：正链第 " + set.TailMismatchPosition + " 位；按订购引物方向 " + set.TailMismatchOriginalBase + " → " + set.TailMismatchBase + "（C3 前最后一个 DNA 碱基）。此处不是 SNP，不标红，切割后随尾部移除。";
        }
        public static string RegionText(LampRegion region)
        {
            return region.Name + "：" + region.Start + "–" + region.End + "（" + (region.Reverse ? "反向互补" : "正向") + "），" + region.Sequence.Length + " nt，GC " + N(region.Gc) + "%，Tm ≈ " + N(region.Tm) + " °C";
        }
        public static string OligoMetadata(LampOligo p)
        {
            var parts = new List<string>();
            parts.Add("完整订购序列 " + p.Sequence.Length + " nt" + (p.RnaIndex >= 0 ? "（含 1 个 RNA，另有 3′ C3）；切后 " + p.ActivatedSequence.Length + " nt" : "") + "；5′→3′");
            if (p.RnaIndex >= 0) parts.Add("RNA：引物第 " + (p.RnaIndex + 1) + " 位，对应正链第 " + p.RnaTemplatePosition + " 位；RNA 后尾部 " + p.ActivationTailLength + " nt。以下 Tm 为 DNA 等效估算。");
            int offset = 1;
            foreach (LampRegion region in p.Regions)
            {
                string value = RegionText(region);
                if (p.Regions.Count > 1) value += "；位于拼接引物第 " + offset + "–" + (offset + region.Sequence.Length - 1) + " 位";
                parts.Add(value); offset += region.Sequence.Length;
            }
            if (p.RnaIndex >= 0)
                foreach (LampRegion region in p.ActivatedRegions) parts.Add("切后有效 " + RegionText(region));
            return String.Join("\n", parts.ToArray());
        }
        public static string Reactions(LampPrimerSet set, LampDesignResult r)
        {
            var common = new List<string>();
            foreach (LampOligo p in Oligos(set)) if (r.Snp == null || p.SnpIndex < 0) common.Add(OligoName(p, set, r));
            if (r.Snp == null) return String.Join(" + ", common.ToArray());
            string tail = String.Join(" + ", common.ToArray());
            return ModeName(r) + "\n参考等位反应：" + set.SpecificInner + "_ref_" + r.Snp.ReferenceAllele + " + " + tail + "\n" +
                "替代等位反应：" + set.SpecificInner + "_alt_" + r.Snp.AlternateAllele + " + " + tail + "\n两种反应分开配制；未自动设计通用扩增对照。";
        }
        public static string LayoutText(LampPrimerSet set)
        {
            var regions = new List<LampRegion>();
            foreach (LampOligo p in new LampOligo[] { set.F3, set.FIP, set.BIP, set.B3, set.LF, set.LB })
                if (p != null) regions.AddRange(p.Regions);
            regions.Sort(delegate(LampRegion a, LampRegion b) { return a.Start.CompareTo(b.Start); });
            var text = new StringBuilder("参考正链 5′→3′ 区段顺序（按坐标排列）：\n");
            foreach (LampRegion region in regions) text.AppendLine(RegionText(region));
            text.Append(set.BIP.RnaIndex >= 0 ? "PA-LAMP：BIP 为 B1c + 有效 B2 + RNA + 尾部 DNA + 3′ C3。前体与切后有效区段见引物卡片。" : "FIP = F1c + F2；BIP = B1c + B2。以上方向指订购区段相对于输入正链的方向。");
            return Lf(text.ToString());
        }

        private sealed class Builder
        {
            private readonly StringBuilder text = new StringBuilder();
            private readonly List<ReportHighlight> marks = new List<ReportHighlight>();
            private readonly List<ReportHighlight> mismatchMarks = new List<ReportHighlight>();
            public void Add(string value) { text.Append(Lf(value)); }
            public void Line(string value) { Add(value); text.Append('\n'); }
            public void Line() { text.Append('\n'); }
            public void Sequence(string value, int index, int mismatchIndex = -1)
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value.IndexOfAny(new char[] { '\r', '\n' }) >= 0) throw new ArgumentException("序列不得含换行。", "value");
                if (index < -1 || index >= value.Length) throw new ArgumentOutOfRangeException("index", "SNP 坐标不在序列内。");
                if (mismatchIndex < -1 || mismatchIndex >= value.Length) throw new ArgumentOutOfRangeException("mismatchIndex", "人为错配坐标不在序列内。");
                if (index >= 0) marks.Add(new ReportHighlight { Start = text.Length + index, Length = 1 });
                if (mismatchIndex >= 0 && mismatchIndex != index) mismatchMarks.Add(new ReportHighlight { Start = text.Length + mismatchIndex, Length = 1 });
                text.Append(value);
            }
            public void AddReport(HighlightedReport r)
            {
                foreach (ReportHighlight h in r.SnpHighlights) marks.Add(new ReportHighlight { Start = text.Length + h.Start, Length = h.Length });
                foreach (ReportHighlight h in r.MismatchHighlights) mismatchMarks.Add(new ReportHighlight { Start = text.Length + h.Start, Length = h.Length });
                text.Append(r.Text);
            }
            public HighlightedReport Build() { return new HighlightedReport { Text = text.ToString(), SnpHighlights = new List<ReportHighlight>(marks), MismatchHighlights = new List<ReportHighlight>(mismatchMarks) }; }
        }
        public static HighlightedReport HighlightedOrderingText(LampPrimerSet set, LampDesignResult r)
        {
            var b = new Builder(); bool first = true;
            foreach (LampOligo p in Oligos(set))
            {
                if (!first) b.Line(); first = false;
                b.Add((IsPa(r) ? "PA-LAMP_" : IsMLamp(r) ? "mLAMP_" : r.Snp != null ? "AS-LAMP_" : "LAMP_") + set.Rank + "_" + OligoName(p, set, r) + "\t"); b.Sequence(p.OrderingSequence, p.OrderingSnpIndex, MismatchIndex(p, set, r));
            }
            return b.Build();
        }
        public static string OrderingText(LampPrimerSet set, LampDesignResult r) { return HighlightedOrderingText(set, r).Text.Replace("\n", "\r\n"); }
        public static HighlightedReport HighlightedSet(LampPrimerSet set, LampDesignResult r)
        {
            var b = new Builder();
            b.Line(ModeName(r) + " 候选组 #" + set.Rank + " | 排序分数 " + N(set.Score) + "（非成功率/选择性）");
            b.Line("F3..B3 靶区：" + set.SpanStart + "–" + set.SpanEnd + "，" + set.SpanLength + " nt");
            b.Line(LayoutText(set)); b.Line(); b.Line("独立反应组合"); b.Line(Reactions(set, r)); b.Line();
            if (set.ExtraMismatchPosition > 0) b.Line(MismatchText(set, r));
            foreach (LampOligo p in Oligos(set))
            {
                b.Line(OligoName(p, set, r) + " 5′→3′"); b.Sequence(p.OrderingSequence, p.OrderingSnpIndex, MismatchIndex(p, set, r)); b.Line();
                b.Line(OligoMetadata(p)); b.Line(OligoNote(p, set, r));
                if (p.Metrics != null)
                {
                    b.Line((IsPa(r) ? "DNA 等效结构启发式：发卡茎 " : "完整引物结构启发式：发卡茎 ") + p.Metrics.Hairpin + "；自互补 " + p.Metrics.SelfComplement + "；自身 3′ 互补 " + p.Metrics.SelfThreePrime + "；短串联重复 " + p.Metrics.TandemRepeat + " nt");
                    foreach (string note in p.Metrics.Warnings) b.Line("引物提示：" + note);
                }
                b.Line();
            }
            b.Line("靶区模板 / " + (r.Snp == null ? "输入模板" : "参考等位基因 " + r.Snp.ReferenceAllele) + "（原始正链 5′→3′）");
            b.Sequence(set.ReferenceTemplate, r.Snp == null ? -1 : r.Snp.Position - set.SpanStart); b.Line();
            if (r.Snp != null)
            {
                b.Line("靶区模板 / 替代等位基因 " + r.Snp.AlternateAllele + "（原始正链 5′→3′）");
                b.Sequence(set.AlternateTemplate, r.Snp.Position - set.SpanStart); b.Line();
            }
            b.Line("上述区间用于定位引物，不表示固定长度的 LAMP 最终扩增产物。");
            foreach (string note in set.Notes) b.Line("候选组提示：" + note);
            return b.Build();
        }
        public static string SetText(LampPrimerSet set, LampDesignResult r) { return HighlightedSet(set, r).Text.Replace("\n", "\r\n"); }
        public static HighlightedReport HighlightedTextReport(LampDesignResult r)
        {
            if (r == null) throw new ArgumentNullException("r");
            var b = new Builder();
            b.Line("RPA / LAMP 引物设计助手 v" + AppVersion.Display + " — " + ModeName(r) + " 候选报告");
            b.Line("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.Line("序列：" + r.Input.Name + " | " + r.Input.Sequence.Length + " nt");
            if (r.Snp != null) b.Line("SNP：正链第 " + r.Snp.Position + " 位 [" + r.Snp.ReferenceAllele + ">" + r.Snp.AlternateAllele + "]");
            b.Line(Params(r.Settings));
            b.Line("结果：" + r.Sets.Count + " 组；搜索裁剪：" + (r.SearchTruncated ? "是" : "否"));
            b.Line("标色：界面与 HTML 中的红色碱基表示 SNP；" + (IsMLamp(r) ? "蓝色碱基表示 mLAMP 订购引物中的人为错配；" : "") + "纯文本、CSV 和 FASTA 不保存颜色。");
            b.Line(); b.Line(MethodFor(r)); b.Line(); b.Line(ScoringFor(r)); b.Line();
            foreach (string note in r.Input.Warnings) b.Line("输入提示：" + note);
            foreach (string note in r.Notes) b.Line("设计说明：" + note);
            foreach (LampPrimerSet set in r.Sets) { b.Line(); b.Line(new string('=', 70)); b.AddReport(HighlightedSet(set, r)); }
            b.Line(); b.Line("完整输入模板 / 参考正链："); b.Sequence(r.Input.Sequence, r.Snp == null ? -1 : r.Snp.Position - 1); b.Line();
            if (r.Snp != null) { b.Line("完整替代模板 / 正链："); b.Sequence(r.Snp.Alternate.Sequence, r.Snp.Position - 1); b.Line(); }
            b.Line(); b.Line("设计原则参考（不等于本软件候选的实验验证）："); b.Line(ManualUrl); b.Line(TmUrl);
            if (r.Snp != null) { b.Line("PrimerExplorer V5 等位差异设计（第 6.2 节）："); b.Line(SnpManualUrl); }
            if (r.Snp != null && !IsPa(r) && !IsMLamp(r)) { b.Line("AS-LAMP 研究："); b.Line(EvidenceUrl); b.Line("FIP 附加错配的相关 mLAMP 研究："); b.Line(MLampEvidenceUrl); }
            if (IsPa(r)) { b.Line("PA-LAMP 原始研究："); b.Line(PaEvidenceUrl); b.Line("补充材料 Table S1 / Figure S5："); b.Line(PaSupplementUrl); }
            if (IsMLamp(r)) { b.Line("mLAMP 原始研究：Ren 等，ChemistrySelect 2019，第 1424–1425 页 Scheme 1 / Figure 1："); b.Line(MLampEvidenceUrl); }
            return b.Build();
        }
        public static string TextReport(LampDesignResult r) { return HighlightedTextReport(r).Text.Replace("\n", "\r\n"); }
        private static string CsvCell(string value)
        {
            value = value ?? ""; string check = value.TrimStart(' ', '\t', '\r', '\n');
            if ((check.Length > 0 && "=+-@".IndexOf(check[0]) >= 0) || (value.Length > 0 && "\t\r\n".IndexOf(value[0]) >= 0)) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        public static string Csv(LampDesignResult r)
        {
            var b = new StringBuilder("序列名称,候选组,模式,引物角色,订购序列_5to3,完整长度_nt,结合区段坐标与GC及Tm,本引物SNP位置_1based,SNP正链坐标,参考等位基因,替代等位基因,特异内引物方向,附加错配正链坐标,靶区起点,靶区终点,靶区跨度_nt,结构分数_非选择性,发卡茎_nt,自互补_nt,自身3prime互补_nt,短串联重复_nt,参考靶区模板,替代靶区模板,独立反应组合,本次参数,搜索裁剪,提示与限制\r\n");
            foreach (LampPrimerSet set in r.Sets)
            foreach (LampOligo p in Oligos(set))
            {
                var notes = new List<string>(set.Notes); if (p.Metrics != null) notes.AddRange(p.Metrics.Warnings); notes.Add(OligoNote(p, set, r));
                if (p.SnpIndex >= 0 && set.ExtraMismatchPosition > 0) notes.Add(MismatchText(set, r));
                notes.Add("靶区为原始模板，不是固定 LAMP 产物；候选需实验验证；未做全基因组特异性检索");
                if (IsMLamp(r)) { notes.Add(MLampMethod); notes.Add(MLampScoring); notes.Add("mLAMP 文献依据：" + MLampEvidenceUrl); }
                string[] cells = { r.Input.Name, set.Rank.ToString(), ModeName(r), OligoName(p, set, r), p.OrderingSequence, p.Sequence.Length.ToString(), OligoMetadata(p),
                    p.SnpIndex < 0 ? "" : (p.SnpIndex + 1).ToString(), r.Snp == null ? "" : r.Snp.Position.ToString(), r.Snp == null ? "" : r.Snp.ReferenceAllele.ToString(), r.Snp == null ? "" : r.Snp.AlternateAllele.ToString(), set.SpecificInner,
                    p.SnpIndex >= 0 && set.ExtraMismatchPosition > 0 ? set.ExtraMismatchPosition.ToString() : "", set.SpanStart.ToString(), set.SpanEnd.ToString(), set.SpanLength.ToString(), N(set.Score),
                    p.Metrics == null ? "" : p.Metrics.Hairpin.ToString(), p.Metrics == null ? "" : p.Metrics.SelfComplement.ToString(), p.Metrics == null ? "" : p.Metrics.SelfThreePrime.ToString(), p.Metrics == null ? "" : p.Metrics.TandemRepeat.ToString(),
                    set.ReferenceTemplate, r.Snp == null ? "" : set.AlternateTemplate, Reactions(set, r), Params(r.Settings), r.SearchTruncated ? "是" : "否", String.Join("；", notes.ToArray()) };
                for (int i = 0; i < cells.Length; i++) { if (i > 0) b.Append(','); b.Append(CsvCell(cells[i])); } b.Append("\r\n");
            }
            return b.ToString();
        }
        public static string Fasta(LampDesignResult r)
        {
            var b = new StringBuilder();
            foreach (LampPrimerSet set in r.Sets)
            foreach (LampOligo p in Oligos(set))
            {
                b.Append(IsPa(r) ? ">PA-LAMP_" : IsMLamp(r) ? ">mLAMP_" : r.Snp != null ? ">AS-LAMP_" : ">LAMP_").Append(set.Rank).Append('_').Append(OligoName(p, set, r));
                b.Append(" 5to3 candidate_unvalidated");
                b.Append(" method=").Append(r.Snp == null ? "LAMP" : r.Settings.SnpMethod);
                if (IsMLamp(r)) b.Append(" evidence=").Append(MLampEvidenceUrl).Append(" separate_allele_reactions extra_mismatch_from_3prime=").Append(r.Settings.ExtraMismatchFromThreePrime).Append(r.Settings.IncludeLoops ? " optional_loops_program_extension" : " four_primer_framework");
                if (p.SnpIndex >= 0) b.Append(" SNP_oligo_position=").Append(p.SnpIndex + 1);
                if (p.RnaIndex >= 0)
                    b.Append(" DNA_equivalent_only NOT_FOR_ORDERING RNA_position=").Append(p.RnaIndex + 1)
                        .Append(" RNA_base=").Append(p.Sequence[p.RnaIndex] == 'T' ? 'U' : p.Sequence[p.RnaIndex])
                        .Append(" block_3prime=C3 ordering_sequence=").Append(p.OrderingSequence)
                        .Append(" activated_sequence=").Append(p.ActivatedSequence);
                b.Append("\r\n").Append(p.Sequence).Append("\r\n");
            }
            return b.ToString();
        }
        private static string HtmlSequence(string text, int snpIndex, int mismatchIndex)
        {
            if (snpIndex < -1 || snpIndex >= text.Length) throw new ArgumentOutOfRangeException("snpIndex");
            if (mismatchIndex < -1 || mismatchIndex >= text.Length) throw new ArgumentOutOfRangeException("mismatchIndex");
            if (snpIndex < 0 && mismatchIndex < 0) return E(text);
            var b = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                string css = i == snpIndex ? "snp" : i == mismatchIndex ? "mismatch" : null;
                if (css != null) b.Append("<span class=\"").Append(css).Append("\">");
                b.Append(E(text[i].ToString()));
                if (css != null) b.Append("</span>");
            }
            return b.ToString();
        }
        private static void HtmlInfo(StringBuilder b, string title, string value)
        {
            b.Append("<article><h3>").Append(E(title)).Append("</h3><p class=\"meta\">").Append(E(Lf(value))).Append("</p></article>\n");
        }
        private static void HtmlDna(StringBuilder b, string title, string meta, string sequence, int snpIndex, string note, int mismatchIndex = -1)
        {
            b.Append("<article><h3>").Append(E(title)).Append("</h3><p class=\"meta\">").Append(E(Lf(meta))).Append("</p><pre class=\"dna\">");
            b.Append(HtmlSequence(sequence, snpIndex, mismatchIndex)).Append("</pre><p class=\"note\">").Append(E(note)).Append("</p></article>\n");
        }
        public static string Html(LampDesignResult r)
        {
            var b = new StringBuilder("<!doctype html>\n<html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            b.Append("<title>RPA / LAMP 引物设计助手 v" + AppVersion.Display + " — LAMP 候选报告</title><style>body{margin:0;background:#f3f6f8;color:#1a2b3e;font:15px/1.65 'Microsoft YaHei',sans-serif}main{max-width:1160px;margin:auto;padding:24px}h1{font-size:26px}h2{font-size:21px;margin:28px 0 12px}h3{margin:0 0 8px;font-size:17px;color:#007775}article{background:white;border:1px solid #dce5eb;border-radius:10px;padding:18px;margin:12px 0;break-inside:avoid}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,400px),1fr));gap:14px}.grid article{margin:0}.meta{white-space:pre-wrap;margin:0 0 12px;color:#52616c}.dna{white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-all;font:16px/1.8 Consolas,monospace;background:#f6f9fa;padding:12px}.note{color:#667582;font-size:13px;margin-bottom:0}.snp{color:#d32f2f;font-weight:700}.mismatch{color:#2563eb;font-weight:700}a{color:#007775}@media(max-width:600px){main{padding:12px}article{padding:12px}}@media print{body{background:white}main{padding:0}.snp,.mismatch{print-color-adjust:exact;-webkit-print-color-adjust:exact}}</style></head><body><main>\n");
            b.Append("<h1>").Append(ModeName(r)).Append(" 候选报告</h1>");
            HtmlInfo(b, "输入与结果", r.Input.Name + " | " + r.Input.Sequence.Length + " nt\n导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n候选：" + r.Sets.Count + " 组；搜索裁剪：" + (r.SearchTruncated ? "是" : "否") + (r.Snp == null ? "" : "\nSNP：" + r.Snp.Position + " [" + r.Snp.ReferenceAllele + ">" + r.Snp.AlternateAllele + "]；红色为 SNP 碱基" + (IsMLamp(r) ? "；蓝色为订购引物中的人为错配" : "")));
            HtmlInfo(b, "本次参数", Params(r.Settings));
            if (r.Input.Warnings.Count > 0) HtmlInfo(b, "输入提示", String.Join("\n", r.Input.Warnings.ToArray()));
            if (r.Notes.Count > 0) HtmlInfo(b, "搜索与筛选说明", String.Join("\n", r.Notes.ToArray()));
            foreach (LampPrimerSet set in r.Sets)
            {
                b.Append("<section><h2>候选组 #").Append(set.Rank).Append(" · 排序分数 ").Append(N(set.Score)).Append("（非成功率/选择性）</h2>\n");
                HtmlInfo(b, "六区段布局", LayoutText(set)); HtmlInfo(b, "反应组合", Reactions(set, r));
                if (set.ExtraMismatchPosition > 0) HtmlInfo(b, "附加人为错配", MismatchText(set, r));
                b.Append("<div class=\"grid\">\n");
                foreach (LampOligo p in Oligos(set)) HtmlDna(b, OligoName(p, set, r) + " · 5′→3′", OligoMetadata(p), p.OrderingSequence, p.OrderingSnpIndex, OligoNote(p, set, r), MismatchIndex(p, set, r));
                b.Append("</div>\n");
                string region = "原始正链 " + set.SpanStart + "–" + set.SpanEnd + "，" + set.SpanLength + " nt；5′→3′";
                const string templateNote = "此处显示原始靶区模板，不是固定长度的 LAMP 最终扩增产物。";
                HtmlDna(b, "靶区模板 · " + (r.Snp == null ? "输入" : "参考等位基因 " + r.Snp.ReferenceAllele), region, set.ReferenceTemplate, r.Snp == null ? -1 : r.Snp.Position - set.SpanStart, templateNote);
                if (r.Snp != null) HtmlDna(b, "靶区模板 · 替代等位基因 " + r.Snp.AlternateAllele, region, set.AlternateTemplate, r.Snp.Position - set.SpanStart, templateNote);
                var metrics = new StringBuilder();
                foreach (LampOligo p in Oligos(set)) if (p.Metrics != null)
                {
                    metrics.AppendLine(OligoName(p, set, r) + "：发卡茎 " + p.Metrics.Hairpin + " / 自互补 " + p.Metrics.SelfComplement + " / 自身 3′ 互补 " + p.Metrics.SelfThreePrime + " / 串联重复 " + p.Metrics.TandemRepeat + " nt");
                    foreach (string note in p.Metrics.Warnings) metrics.AppendLine("提示：" + note);
                }
                HtmlInfo(b, IsPa(r) ? "前体 DNA 等效结构指标" : "完整订购引物结构指标", metrics.ToString());
                if (set.Notes.Count > 0) HtmlInfo(b, "候选组提示", String.Join("\n", set.Notes.ToArray()));
                b.Append("</section>\n");
            }
            HtmlDna(b, "完整输入模板 · 参考正链", "5′→3′", r.Input.Sequence, r.Snp == null ? -1 : r.Snp.Position - 1, "原始输入模板");
            if (r.Snp != null) HtmlDna(b, "完整替代模板 · 正链", "5′→3′", r.Snp.Alternate.Sequence, r.Snp.Position - 1, "只替换用户标记的 SNP 碱基");
            HtmlInfo(b, "方法与限制", MethodFor(r)); HtmlInfo(b, "评价依据", ScoringFor(r));
            b.Append("<article><h3>参考资料</h3><p><a href=\"").Append(ManualUrl).Append("\">PrimerExplorer 设计说明</a> · <a href=\"").Append(TmUrl).Append("\">最近邻 Tm 说明</a>");
            if (r.Snp != null) b.Append(" · <a href=\"").Append(SnpManualUrl).Append("\">官方等位差异设计说明（第 6.2 节）</a>");
            if (r.Snp != null && !IsPa(r) && !IsMLamp(r)) b.Append(" · <a href=\"").Append(EvidenceUrl).Append("\">AS-LAMP 研究</a> · <a href=\"").Append(MLampEvidenceUrl).Append("\">FIP 附加错配的相关 mLAMP 研究</a>");
            if (IsPa(r)) b.Append(" · <a href=\"").Append(PaEvidenceUrl).Append("\">PA-LAMP 原始研究</a> · <a href=\"").Append(PaSupplementUrl).Append("\">PA-LAMP 补充材料</a>");
            if (IsMLamp(r)) b.Append(" · <a href=\"").Append(MLampEvidenceUrl).Append("\">Ren 等 mLAMP 原始研究（2019；Scheme 1 / Figure 1）</a>");
            b.Append("</p><p class=\"note\">参考资料不等于本软件候选已经获得实验验证。</p></article></main></body></html>\n");
            return b.ToString();
        }
    }
}
