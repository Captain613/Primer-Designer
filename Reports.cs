using System;
using System.Globalization;
using System.Text;

namespace RpaDesigner
{
    public static class ReportWriter
    {
        public const string ManualUrl = "https://www.globalpointofcare.abbott/content/dam/ardx/globalpointofcare/lp/2025/twistdx/support/docs/manuals/INASDM%20v1.0%20TwistAmp%20DNA%20Amplification%20Kits%20-%20Assay%20Design%20Manual%20INASDM.pdf";
        public const string Method = "RPA 候选引物，需实验筛选验证。所有引物按 5′→3′ 输出；坐标为输入正链上的 1-based 闭区间。反向引物已取反向互补，可直接作为订购序列参考。\r\n本版采用序列启发式排序，分数不是成功率；未进行全基因组/物种特异性检索，也未计算热力学 ΔG。互补指标为无错配、无缺口的连续互补长度（nt），不能代替完整结构分析。\r\nTm 仅为经验估算：64.9 + 41 × (G+C 数量 − 16.4) / 长度；未校正盐和引物浓度，不是 RPA 反应温度，不参与排序。\r\n适用于线性模板上的单对 RPA 候选设计；不含 exo/nfo 探针、简并引物设计、SNP 等位基因判别、甲基化转化模型、多重扩增及环形跨原点设计。";
        public const string Scoring = "评分方法（工程启发式，未经实验标定）：\r\n单引物惩罚 = 0.12*|GC%-50| + 0.3*|长度-clamp(32,最短,最长)| + 1.5*max(0,同聚物-3) + 0.5*max(0,短串联重复跨度-8) + 0.6*max(0,自互补-3) + 1.8*max(0,自身3′互补-2) + 1.2*max(0,发卡茎-3)。\r\n配对惩罚 = 两条单引物惩罚之和 + 8*|产物长度-偏好|/max(1,最长产物-最短产物) + 0.8*max(0,引物间互补-3) + 2.5*max(0,引物间3′互补-2)。\r\n偏好产物设为无限制时，产物长度偏好惩罚为 0；单引物长度及其他惩罚仍计入。\r\n显示分数 = 100/(1+总惩罚/25)，越高越优先筛选。短串联重复指 2–4 nt 模体至少连续 3 次完整重复，跨度按 nt 计；结构指标的单位均为 nt。";

        public static string ExampleFasta()
        {
            var random = new Random(20260920);
            var b = new StringBuilder(">synthetic_demo 非生物来源的随机演示序列\r\n");
            for (int i = 0; i < 800; i++) { b.Append("ACGT"[random.Next(4)]); if ((i+1)%80 == 0) b.AppendLine(); }
            return b.ToString();
        }
        private static string N(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string Bound(int value, bool unlimited) { return unlimited ? "无限制" : value.ToString(CultureInfo.InvariantCulture); }
        private static string Bound(double value, bool unlimited) { return unlimited ? "无限制" : N(value); }
        public static string Params(DesignSettings s, bool snp)
        {
            if (s == null) throw new ArgumentNullException("s");
            var b = new StringBuilder();
            b.Append(snp ? "所有引物" : "引物").Append("：最短 ").Append(Bound(s.PrimerMin, s.PrimerMinUnlimited))
                .Append(" / 最长 ").Append(Bound(s.PrimerMax, s.PrimerMaxUnlimited)).Append(" nt；GC：最低 ")
                .Append(Bound(s.GcMin, s.GcMinUnlimited)).Append(" / 最高 ").Append(Bound(s.GcMax, s.GcMaxUnlimited)).Append("%；")
                .Append(snp ? "所有反应产物" : "产物").Append("：最短 ").Append(Bound(s.AmpliconMin, s.AmpliconMinUnlimited))
                .Append(" / 最长 ").Append(Bound(s.AmpliconMax, s.AmpliconMaxUnlimited)).Append(" bp；")
                .Append(snp ? "等位产物偏好 " : "偏好产物 ")
                .Append(s.PreferredAmpliconUnlimited ? "无限制（不按产物长度偏好评分）" : s.PreferredAmplicon.ToString(CultureInfo.InvariantCulture) + " bp")
                .Append(snp ? "（不评价对照产物长度）" : "").Append("；请求 ").Append(s.MaxPairs).Append(snp ? " 组。" : " 对。");
            if (!snp) b.Append("必须包围的靶区：").Append(s.TargetStart == 0 ? "未指定" : s.TargetStart + "–" + s.TargetEnd).Append("。");
            if (s.PrimerMinUnlimited || s.PrimerMaxUnlimited || s.GcMinUnlimited || s.GcMaxUnlimited || s.AmpliconMinUnlimited || s.AmpliconMaxUnlimited)
                b.Append("\r\n无限制表示关闭对应的用户筛选边界；模板长度、引物不重叠等可行性条件及有限候选搜索仍保留，不表示穷举所有组合。");
            if (s.PrimerMinUnlimited || s.PrimerMaxUnlimited)
                b.Append("引物长度范围较宽时采用有限长度采样，裁剪情况见搜索裁剪及设计说明。");
            return b.ToString();
        }
        public static string PrimerText(string name, Primer p)
        {
            var b = new StringBuilder();
            b.AppendLine(name + "  5′→3′"); b.AppendLine(p.Sequence);
            b.AppendLine("坐标 " + p.Start + "–" + p.End + " | 长度 " + p.Sequence.Length + " nt | GC " + N(p.Gc) + "% | Tm 估算 " + N(p.Tm) + " °C");
            b.AppendLine("发卡茎 / 自互补 / 自身 3′ 互补：" + p.Hairpin + " / " + p.SelfComplement + " / " + p.SelfThreePrime + " nt");
            b.AppendLine("短串联重复最长跨度：" + p.TandemRepeat + " nt");
            foreach (string w in p.Warnings) b.AppendLine("提示：" + w);
            return b.ToString();
        }
        public static string PairText(PrimerPair p)
        {
            var b = new StringBuilder();
            b.AppendLine("候选 #" + p.Rank + "   排序分数 " + N(p.Score) + "（非成功率）"); b.AppendLine();
            b.AppendLine(PrimerText("正向引物 F", p.Forward));
            b.AppendLine(PrimerText("反向引物 R", p.Reverse));
            b.AppendLine("引物间连续互补 / 3′ 互补：" + p.CrossComplement + " / " + p.CrossThreePrime + " nt");
            foreach (string w in p.Warnings) b.AppendLine("提示：" + w);
            b.AppendLine(); b.AppendLine("扩增片段：" + p.AmpliconStart + "–" + p.AmpliconEnd + "，" + p.AmpliconLength + " bp（含两端引物结合区）");
            b.AppendLine(p.AmpliconSequence);
            return b.ToString();
        }
        public static string TextReport(DesignResult r)
        {
            var b = new StringBuilder();
            b.AppendLine("RPA 引物设计助手 v0.19 — 普通扩增候选报告");
            b.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("序列：" + r.Input.Name + " | " + r.Input.Sequence.Length + " nt");
            var s = r.Settings;
            b.AppendLine("参数：" + Params(s, false));
            b.AppendLine("候选数量 F/R：" + r.ForwardCandidateCount + "/" + r.ReverseCandidateCount + "；结果 " + r.Pairs.Count + " 对；搜索裁剪：" + (r.SearchTruncated ? "是" : "否"));
            b.AppendLine(); b.AppendLine(Method); b.AppendLine(); b.AppendLine(Scoring); b.AppendLine();
            foreach (string w in r.Input.Warnings) b.AppendLine("输入提示：" + w);
            foreach (string w in r.Notes) b.AppendLine("设计说明：" + w);
            b.AppendLine();
            foreach (var p in r.Pairs) { b.AppendLine(new string('=', 64)); b.AppendLine(PairText(p)); }
            b.AppendLine("输入序列（规范化后）"); b.AppendLine(r.Input.Sequence);
            b.AppendLine(); b.AppendLine("默认规则参考：TwistAmp Assay Design Manual，INASDM Rev 1，§2。软件的评分权重和裁剪规则是工程实现选择。"); b.AppendLine(ManualUrl);
            return b.ToString();
        }
        private static string Cell(string v)
        {
            if (!String.IsNullOrEmpty(v) && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
            return "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        }
        public static string Csv(DesignResult r)
        {
            var b = new StringBuilder("序列名称,候选序号,方向,引物序列_5to3,起点_1based,终点_含,长度_nt,GC百分比,Tm经验估算_C,扩增起点,扩增终点,扩增长度_bp,排序分数_非成功率,发卡茎_nt,自互补_nt,自身3prime互补_nt,引物间互补_nt,引物间3prime互补_nt,提示,扩增序列,方法限制,设计参数,搜索裁剪,短串联重复跨度_nt\r\n");
            foreach (var pair in r.Pairs)
            {
                var primers = new Primer[] { pair.Forward, pair.Reverse };
                for (int i=0; i<2; i++)
                {
                    var p = primers[i];
                    var values = new string[] {r.Input.Name,pair.Rank.ToString(),i==0?"F":"R",p.Sequence,p.Start.ToString(),p.End.ToString(),p.Sequence.Length.ToString(),N(p.Gc),N(p.Tm),pair.AmpliconStart.ToString(),pair.AmpliconEnd.ToString(),pair.AmpliconLength.ToString(),N(pair.Score),p.Hairpin.ToString(),p.SelfComplement.ToString(),p.SelfThreePrime.ToString(),pair.CrossComplement.ToString(),pair.CrossThreePrime.ToString(),String.Join("；",p.Warnings.ToArray()) + "；" + String.Join("；",pair.Warnings.ToArray()),pair.AmpliconSequence,"候选需验证；Tm非反应温度；未做基因组特异性检索；互补为启发式", Params(r.Settings, false), r.SearchTruncated?"是":"否"};
                    for (int j=0;j<values.Length;j++) { if(j>0)b.Append(','); b.Append(Cell(values[j])); } b.Append(','); b.Append(Cell(p.TandemRepeat.ToString(CultureInfo.InvariantCulture))); b.AppendLine();
                }
            }
            return b.ToString();
        }
        public static string Fasta(DesignResult r)
        {
            var b = new StringBuilder();
            foreach (var p in r.Pairs) { b.AppendLine(">candidate_"+p.Rank+"_F 5to3 pos="+p.Forward.Start+"-"+p.Forward.End); b.AppendLine(p.Forward.Sequence); b.AppendLine(">candidate_"+p.Rank+"_R 5to3 pos="+p.Reverse.Start+"-"+p.Reverse.End); b.AppendLine(p.Reverse.Sequence); }
            return b.ToString();
        }
    }
}
