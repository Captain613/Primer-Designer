using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RpaDesigner
{
    public sealed class ReportHighlight
    {
        public int Start, Length;
    }

    public sealed class HighlightedReport
    {
        // LF is intentional: RichTextBox normalizes CRLF when assigning Text.
        public string Text = "";
        public List<ReportHighlight> SnpHighlights = new List<ReportHighlight>();
        public List<ReportHighlight> MismatchHighlights = new List<ReportHighlight>();
    }

    public static class SnpReportWriter
    {
        public const string EvidenceUrl = "https://pubmed.ncbi.nlm.nih.gov/39051493/";
        public const string Scoring = "SNP 评分方法（工程启发式，未经实验标定）：\r\n"+
            "单引物惩罚 = 0.12*|GC%-50| + 0.3*|长度-clamp(32,最短,最长)| + 1.5*max(0,同聚物-3) + 0.5*max(0,短串联重复跨度-8) + 0.6*max(0,自互补-3) + 1.8*max(0,自身3′互补-2) + 1.2*max(0,发卡茎-3)。\r\n"+
            "每个反应的非产物长度惩罚 N = 两条单引物惩罚之和 + 0.8*max(0,引物间互补-3) + 2.5*max(0,引物间3′互补-2)。单条引物自身的长度惩罚照常计入。\r\n"+
            "等位反应的产物长度惩罚 L = 8*|等位产物长度-偏好|/max(1,最长产物-最短产物)。总惩罚 P = (N参考+N替代+N对照)/3 + (L参考+L替代)/2；当前两种等位产物同长。等位产物偏好设为无限制时，L参考和L替代均为 0；单引物长度及其他惩罚仍计入。\r\n"+
            "组分数 = 100/(1+P/25)，不是三个反应分数的平均值。对照产物长度只须满足最短/最长范围，不参与候选初评、对照优选、组评分或对照反应分数；对照引物的组成、单引物长度及互补结构惩罚仍计入。\r\n"+
            "分数越高越优先筛选，不代表选择性或成功率。短串联重复指 2–4 nt 模体至少连续 3 次完整重复，跨度及结构指标单位均为 nt。候选还会优先保留不同共用反向位置，因此列表并非所有组合按分数穷举排列。";
        public const string Method = "SNP 模式输出等位基因选择性扩增候选，不能保证已获得等位基因特异性。RPA 可容忍错配；仅将 SNP 放在引物 3′ 端不一定阻断另一等位基因的扩增。附加错配为未标定的探索选项，也可能损失目标扩增。\r\n本版仅设计普通 DNA 引物，不含 LNA 等化学修饰，不预测判别比或检测限。分数只反映组成与结构启发式，不是选择性或成功率。需分别用两种已知等位基因模板及相应对照核验。\r\n三种组合按独立反应设计：F_ref + R_common、F_alt + R_common、F_control + R_common。不要把这四条引物直接当作已验证的单管分型体系。共用对照引物仅避开当前标注 SNP，不能证明所有样本或全基因组中的特异性。\r\n坐标为去除标记后的参考正链 1-based 闭区间；[A>C] 占一个碱基。所有订购序列均为 5′→3′；未进行全基因组/近似匹配检索或热力学 ΔG 计算。";

        private static string N(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        public static string ExampleFasta()
        {
            var original = SequenceParser.Parse(ReportWriter.ExampleFasta()).Sequence;
            const int index = 299;
            var sequence = original.Substring(0,index) + "[A>C]" + original.Substring(index+1);
            return ">synthetic_SNP_demo 非生物来源；第300位 A>C\r\n" + sequence + "\r\n";
        }
        public static string Strategy(int offset)
        {
            return offset == 0 ? "SNP 位于 3′ 末端；不加人为错配（基线）" : "SNP 位于 3′ 末端；倒数第 " + offset + " 位附加人为错配（探索）";
        }
        private static string Lf(string value) { return (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); }

        // Use the recorded candidate and synthesized base, not the current UI setting.
        // Common/control primers can overlap this coordinate without carrying a mismatch.
        public static int MismatchIndex(Primer p, SnpPrimerSet set)
        {
            if (p == null || set == null || set.ExtraMismatchPosition <= 0 || String.IsNullOrEmpty(p.Sequence)) return -1;
            if (!Object.ReferenceEquals(p, set.ReferenceForward) && !Object.ReferenceEquals(p, set.AlternateForward)) return -1;
            int index = set.ExtraMismatchPosition - p.Start;
            char mismatchBase = Char.ToUpperInvariant(set.ExtraMismatchPrimerBase);
            char templateBase = Char.ToUpperInvariant(set.ExtraMismatchTemplateBase);
            if (index < 0 || index >= p.Sequence.Length - 1 || set.ExtraMismatchPosition >= p.End ||
                "ACGT".IndexOf(mismatchBase) < 0 || "ACGT".IndexOf(templateBase) < 0 || mismatchBase == templateBase ||
                Char.ToUpperInvariant(p.Sequence[index]) != mismatchBase) return -1;
            return index;
        }

        public static int ProductMismatchIndex(PrimerPair pair, SnpPrimerSet set)
        {
            if (pair == null || set == null || String.IsNullOrEmpty(pair.AmpliconSequence)) return -1;
            Primer forward;
            if (Object.ReferenceEquals(pair, set.ReferencePair)) forward = set.ReferenceForward;
            else if (Object.ReferenceEquals(pair, set.AlternatePair)) forward = set.AlternateForward;
            else return -1;
            if (!Object.ReferenceEquals(pair.Forward, forward) || MismatchIndex(forward, set) < 0) return -1;
            int index = set.ExtraMismatchPosition - pair.AmpliconStart;
            if (index < 0 || index >= pair.AmpliconSequence.Length || set.ExtraMismatchPosition > pair.AmpliconEnd ||
                Char.ToUpperInvariant(pair.AmpliconSequence[index]) != Char.ToUpperInvariant(set.ExtraMismatchPrimerBase)) return -1;
            return index;
        }

        // Record offsets at the moment a known sequence is written. Searching the
        // assembled report would also match repeated DNA, headers, and metadata.
        private sealed class ReportBuilder
        {
            private readonly StringBuilder text = new StringBuilder();
            private readonly List<ReportHighlight> highlights = new List<ReportHighlight>();
            private readonly List<ReportHighlight> mismatchHighlights = new List<ReportHighlight>();

            public void Append(string value) { text.Append(Lf(value)); }
            public void AppendLine() { text.Append('\n'); }
            public void AppendLine(string value) { Append(value); AppendLine(); }

            public void Sequence(string sequence, params int[] snpIndices)
            {
                SequenceWithMismatch(sequence, -1, snpIndices);
            }

            public void SequenceWithMismatch(string sequence, int mismatchIndex, params int[] snpIndices)
            {
                if (sequence == null) throw new ArgumentNullException("sequence");
                if (sequence.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                    throw new ArgumentException("待标色序列必须为不含换行的单行序列。", "sequence");
                int previous = -1;
                foreach (int index in snpIndices)
                {
                    if (index < 0 || index >= sequence.Length || index <= previous)
                        throw new ArgumentOutOfRangeException("snpIndices", "SNP 标色坐标必须位于序列内并严格递增。");
                    previous = index;
                }
                foreach (int index in snpIndices)
                    highlights.Add(new ReportHighlight { Start = text.Length + index, Length = 1 });
                if (mismatchIndex < -1 || mismatchIndex >= sequence.Length)
                    throw new ArgumentOutOfRangeException("mismatchIndex", "人为错配标色坐标必须位于序列内。");
                if (mismatchIndex >= 0 && Array.IndexOf(snpIndices, mismatchIndex) < 0)
                    mismatchHighlights.Add(new ReportHighlight { Start = text.Length + mismatchIndex, Length = 1 });
                text.Append(sequence);
            }

            public void SequenceLine(string sequence, params int[] snpIndices)
            {
                Sequence(sequence, snpIndices); AppendLine();
            }

            public void SequenceLineWithMismatch(string sequence, int mismatchIndex, params int[] snpIndices)
            {
                SequenceWithMismatch(sequence, mismatchIndex, snpIndices); AppendLine();
            }

            public void AppendReport(HighlightedReport report)
            {
                int start = text.Length;
                foreach (ReportHighlight mark in report.SnpHighlights)
                    highlights.Add(new ReportHighlight { Start = start + mark.Start, Length = mark.Length });
                foreach (ReportHighlight mark in report.MismatchHighlights)
                    mismatchHighlights.Add(new ReportHighlight { Start = start + mark.Start, Length = mark.Length });
                // Every report made by this builder already uses LF.
                text.Append(report.Text);
            }

            public HighlightedReport Build()
            {
                return new HighlightedReport { Text = text.ToString(), SnpHighlights = new List<ReportHighlight>(highlights), MismatchHighlights = new List<ReportHighlight>(mismatchHighlights) };
            }
        }

        private static void ValidateArguments(SnpPrimerSet set, SnpInput input)
        {
            if (set == null) throw new ArgumentNullException("set");
            if (input == null) throw new ArgumentNullException("input");
        }

        public static HighlightedReport HighlightedOrderingText(SnpPrimerSet set, SnpInput input)
        {
            ValidateArguments(set, input);
            var b = new ReportBuilder();
            b.Append("SNP_" + set.Rank + "_F_ref_" + input.ReferenceAllele + "\t");
            b.SequenceLineWithMismatch(set.ReferenceForward.Sequence, MismatchIndex(set.ReferenceForward, set), input.Position - set.ReferenceForward.Start);
            b.Append("SNP_" + set.Rank + "_F_alt_" + input.AlternateAllele + "\t");
            b.SequenceLineWithMismatch(set.AlternateForward.Sequence, MismatchIndex(set.AlternateForward, set), input.Position - set.AlternateForward.Start);
            b.Append("SNP_" + set.Rank + "_R_common\t"); b.SequenceLine(set.CommonReverse.Sequence);
            b.Append("SNP_" + set.Rank + "_F_control\t"); b.Sequence(set.ControlForward.Sequence);
            return b.Build();
        }

        public static string OrderingText(SnpPrimerSet set, SnpInput input)
        {
            return HighlightedOrderingText(set, input).Text.Replace("\n", "\r\n");
        }

        private static void AppendPrimer(ReportBuilder b, string name, Primer p, int mismatchIndex, params int[] snpIndices)
        {
            b.AppendLine(name + "  5′→3′"); b.SequenceLineWithMismatch(p.Sequence, mismatchIndex, snpIndices);
            b.AppendLine("坐标 " + p.Start + "–" + p.End + " | 长度 " + p.Sequence.Length + " nt | GC " + N(p.Gc) + "% | Tm 估算 " + N(p.Tm) + " °C");
            b.AppendLine("发卡茎 / 自互补 / 自身 3′ 互补：" + p.Hairpin + " / " + p.SelfComplement + " / " + p.SelfThreePrime + " nt");
            b.AppendLine("短串联重复最长跨度：" + p.TandemRepeat + " nt");
            foreach (string warning in p.Warnings) b.AppendLine("提示：" + warning);
            b.AppendLine();
        }
        private static string Marks(Primer p, int mismatch)
        {
            char[] marks = new string(' ',p.Sequence.Length).ToCharArray();
            marks[marks.Length-1]='^';
            if(mismatch >= p.Start && mismatch < p.End)marks[mismatch-p.Start]='*';
            return new string(marks) + "  (^ = SNP；* = 人为错配)";
        }
        public static string SetText(SnpPrimerSet set, SnpInput input)
        {
            return HighlightedSet(set, input).Text.Replace("\n", "\r\n");
        }
        public static HighlightedReport HighlightedSet(SnpPrimerSet set, SnpInput input)
        {
            ValidateArguments(set, input);
            var b = new ReportBuilder();
            b.AppendLine("候选组 #"+set.Rank+"  |  结构排序分数 "+N(set.Score)+"（不代表选择性）");
            b.AppendLine("SNP："+input.Position+" ["+input.ReferenceAllele+">"+input.AlternateAllele+"]；两条等位基因候选正向引物均以此位点作为 3′ 最后一位。");
            if(set.ExtraMismatchPosition>0)b.AppendLine("人为错配：参考正链第 "+set.ExtraMismatchPosition+" 位 "+set.ExtraMismatchTemplateBase+" → 合成引物 "+set.ExtraMismatchPrimerBase+"；两条等位引物采用相同附加替换，合成引物及预期等位产物中的该碱基标为蓝色。");
            else b.AppendLine("人为错配：无。保留基线方案用于实验比较。");
            b.AppendLine();
            AppendPrimer(b,"F_ref（"+input.ReferenceAllele+" 等位基因候选）",set.ReferenceForward,MismatchIndex(set.ReferenceForward,set),input.Position-set.ReferenceForward.Start);
            b.SequenceLineWithMismatch(set.ReferenceForward.Sequence,MismatchIndex(set.ReferenceForward,set),input.Position-set.ReferenceForward.Start);b.AppendLine(Marks(set.ReferenceForward,set.ExtraMismatchPosition));b.AppendLine();
            AppendPrimer(b,"F_alt（"+input.AlternateAllele+" 等位基因候选）",set.AlternateForward,MismatchIndex(set.AlternateForward,set),input.Position-set.AlternateForward.Start);
            b.SequenceLineWithMismatch(set.AlternateForward.Sequence,MismatchIndex(set.AlternateForward,set),input.Position-set.AlternateForward.Start);b.AppendLine(Marks(set.AlternateForward,set.ExtraMismatchPosition));b.AppendLine();
            AppendPrimer(b,"R_common（以上三种反应共用反向）",set.CommonReverse,-1);
            AppendPrimer(b,"F_control（不区分当前 SNP 的对照正向）",set.ControlForward,-1);
            b.AppendLine("独立反应组合与片段长度");
            b.AppendLine("① F_ref + R_common："+set.ReferencePair.AmpliconLength+" bp；引物间连续 / 3′ 互补："+set.ReferencePair.CrossComplement+" / "+set.ReferencePair.CrossThreePrime+" nt");
            b.AppendLine("② F_alt + R_common："+set.AlternatePair.AmpliconLength+" bp；引物间连续 / 3′ 互补："+set.AlternatePair.CrossComplement+" / "+set.AlternatePair.CrossThreePrime+" nt");
            b.AppendLine("③ F_control + R_common："+set.ControlPair.AmpliconLength+" bp；引物间连续 / 3′ 互补："+set.ControlPair.CrossComplement+" / "+set.ControlPair.CrossThreePrime+" nt");
            b.AppendLine();
            foreach(string note in set.Notes)b.AppendLine("提示："+note);
            foreach(string note in set.ReferencePair.Warnings)b.AppendLine("参考反应提示："+note);
            foreach(string note in set.AlternatePair.Warnings)b.AppendLine("替代反应提示："+note);
            foreach(string note in set.ControlPair.Warnings)b.AppendLine("对照反应提示："+note);
            b.AppendLine();b.AppendLine("若对应反应发生扩增，预期产物正链如下（包含合成引物所引入的碱基，不是扩增是否发生的预测）：");
            b.AppendLine("参考等位反应产物：");b.SequenceLineWithMismatch(set.ReferencePair.AmpliconSequence,ProductMismatchIndex(set.ReferencePair,set),input.Position-set.ReferencePair.AmpliconStart);
            b.AppendLine("替代等位反应产物：");b.SequenceLineWithMismatch(set.AlternatePair.AmpliconSequence,ProductMismatchIndex(set.AlternatePair,set),input.Position-set.AlternatePair.AmpliconStart);
            b.AppendLine("共用对照产物 / 参考模板：");b.SequenceLine(set.ControlPair.AmpliconSequence,input.Position-set.ControlPair.AmpliconStart);
            b.AppendLine("共用对照产物 / 替代模板：");b.SequenceLine(set.AlternateControlAmpliconSequence,input.Position-set.ControlPair.AmpliconStart);
            return b.Build();
        }
        public static string TextReport(SnpDesignResult r)
        {
            return HighlightedTextReport(r).Text.Replace("\n", "\r\n");
        }
        public static HighlightedReport HighlightedTextReport(SnpDesignResult r)
        {
            if (r == null) throw new ArgumentNullException("r");
            var b = new ReportBuilder();var s = r.Settings.Base;
            b.AppendLine("RPA 引物设计助手 v0.19 — SNP 选择性扩增候选报告");
            b.AppendLine("导出时间："+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("序列："+r.Input.Reference.Name+" | "+r.Input.Reference.Sequence.Length+" nt | SNP："+r.Input.Position+" ["+r.Input.ReferenceAllele+">"+r.Input.AlternateAllele+"]");
            b.AppendLine("策略："+Strategy(r.Settings.ExtraMismatchFromThreePrime));
            b.AppendLine("参数：" + ReportWriter.Params(s, true));
            b.AppendLine("结果："+r.Sets.Count+" 组；搜索裁剪："+(r.SearchTruncated?"是":"否"));
            b.AppendLine("标色说明：界面与 HTML 报告中，红色碱基为 SNP 位点；蓝色碱基为合成引物及预期等位产物中的附加人为错配。原始模板和共用对照不标蓝。纯文本、CSV 与 FASTA 不保存颜色。");
            b.AppendLine();b.AppendLine(Method);b.AppendLine();
            b.AppendLine("Tm 沿用经验估算 64.9 + 41*(GC数-16.4)/长度，不参与评分，不是反应温度。结构指标为连续互补启发式。");
            b.AppendLine(Scoring);
            foreach(string note in r.Notes)b.AppendLine("设计说明："+note);
            foreach(string note in r.Input.Reference.Warnings)b.AppendLine("输入提示："+note);
            foreach(var set in r.Sets){b.AppendLine();b.AppendLine(new string('=',70));b.AppendReport(HighlightedSet(set,r.Input));b.AppendLine();}
            // [A>C] occupies one biological coordinate; highlight the two allele
            // characters only, leaving its brackets and separator uncolored.
            b.AppendLine();b.AppendLine("规范化标注序列：");b.SequenceLine(r.Input.AnnotatedSequence,r.Input.Position,r.Input.Position+2);
            b.AppendLine("参考模板：");b.SequenceLine(r.Input.Reference.Sequence,r.Input.Position-1);
            b.AppendLine("替代模板：");b.SequenceLine(r.Input.Alternate.Sequence,r.Input.Position-1);
            b.AppendLine();b.AppendLine("研究依据（不等于当前软件的实验验证）：2024 年 allele-specific RPA 研究。");b.AppendLine(EvidenceUrl);
            b.AppendLine("通用 RPA 长度、GC、产物范围依据：");b.AppendLine(ReportWriter.ManualUrl);
            return b.Build();
        }

        private static string HtmlEscape(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        public static string Html(SnpDesignResult r)
        {
            HighlightedReport report = HighlightedTextReport(r);
            var b = new StringBuilder();
            b.Append("<!doctype html>\n<html lang=\"zh-CN\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            b.Append("<title>RPA 引物设计助手 v0.19 — SNP 候选报告</title>\n<style>\n");
            b.Append("body { margin: 0; padding: 24px; color: #172635; background: #f5f7fa; }\n");
            b.Append("main { max-width: 1200px; margin: 0 auto; padding: 24px; border: 1px solid #dce2e8; border-radius: 10px; background: #fff; }\n");
            b.Append("pre { margin: 0; font-family: Consolas, 'Microsoft YaHei', monospace; font-size: 14px; line-height: 1.6; white-space: pre-wrap; overflow-wrap: anywhere; word-wrap: break-word; }\n");
            b.Append(".snp { color: #d32f2f; font-weight: 700; }\n.mismatch { color: #2563eb; font-weight: 700; }\n@media (max-width: 600px) { body { padding: 8px; } main { padding: 12px; } }\n");
            b.Append("@media print { body { padding: 0; background: #fff; } main { border: 0; padding: 0; } .snp, .mismatch { print-color-adjust: exact; -webkit-print-color-adjust: exact; } }\n</style>\n</head>\n<body><main><pre>");
            // Merge by character offset so earlier blue bases never displace later
            // red SNP marks. Red has priority if the two channels ever overlap.
            var colors = new SortedDictionary<int, string>();
            foreach (ReportHighlight mark in report.MismatchHighlights)
            {
                ValidateHtmlMark(mark, report.Text.Length);
                colors[mark.Start] = "mismatch";
            }
            foreach (ReportHighlight mark in report.SnpHighlights)
            {
                ValidateHtmlMark(mark, report.Text.Length);
                colors[mark.Start] = "snp";
            }
            int previousEnd = 0;
            foreach (KeyValuePair<int, string> mark in colors)
            {
                b.Append(HtmlEscape(report.Text.Substring(previousEnd, mark.Key - previousEnd)));
                b.Append("<span class=\"" + mark.Value + "\">"); b.Append(HtmlEscape(report.Text.Substring(mark.Key, 1))); b.Append("</span>");
                previousEnd = mark.Key + 1;
            }
            b.Append(HtmlEscape(report.Text.Substring(previousEnd)));
            b.Append("</pre></main></body>\n</html>\n");
            return b.ToString();
        }
        private static void ValidateHtmlMark(ReportHighlight mark, int textLength)
        {
            if (mark == null || mark.Length != 1 || mark.Start < 0 || mark.Start >= textLength)
                throw new InvalidOperationException("SNP 报告标色范围无效。");
        }
        private static string Cell(string value)
        {
            value=value??"";if(value.Length>0 && "=+-@\t\r\n".IndexOf(value[0])>=0)value="'"+value;
            return "\""+value.Replace("\"","\"\"")+"\"";
        }
        public static string Csv(SnpDesignResult r)
        {
            var b=new StringBuilder("序列名称,候选组,引物角色,订购序列_5to3,长度_nt,结合起点_1based,结合终点_含,GC百分比,Tm经验估算_C,SNP位置,参考等位基因,替代等位基因,附加错配距3prime端_末位为1,附加错配坐标,该位模板碱基,该位引物碱基,参考反应产物_bp,替代反应产物_bp,对照产物_bp,结构分数_非选择性,发卡茎_nt,自互补_nt,自身3prime互补_nt,短串联重复_nt,参考反应预期产物,替代反应预期产物,对照参考产物,对照替代产物,设计参数,搜索裁剪,提示与限制\r\n");
            foreach(var set in r.Sets)
            {
                var primers=new Primer[]{set.ReferenceForward,set.AlternateForward,set.CommonReverse,set.ControlForward};
                string[] roles={"F_ref_"+r.Input.ReferenceAllele,"F_alt_"+r.Input.AlternateAllele,"R_common","F_control"};
                var s=r.Settings.Base;
                for(int i=0;i<4;i++)
                {
                    var p=primers[i];
                    string[] row={r.Input.Reference.Name,set.Rank.ToString(),roles[i],p.Sequence,p.Sequence.Length.ToString(),p.Start.ToString(),p.End.ToString(),N(p.Gc),N(p.Tm),r.Input.Position.ToString(),r.Input.ReferenceAllele.ToString(),r.Input.AlternateAllele.ToString(),r.Settings.ExtraMismatchFromThreePrime.ToString(),set.ExtraMismatchPosition>0?set.ExtraMismatchPosition.ToString():"",set.ExtraMismatchPosition>0?set.ExtraMismatchTemplateBase.ToString():"",set.ExtraMismatchPosition>0?set.ExtraMismatchPrimerBase.ToString():"",set.ReferencePair.AmpliconLength.ToString(),set.AlternatePair.AmpliconLength.ToString(),set.ControlPair.AmpliconLength.ToString(),N(set.Score),p.Hairpin.ToString(),p.SelfComplement.ToString(),p.SelfThreePrime.ToString(),p.TandemRepeat.ToString(),set.ReferencePair.AmpliconSequence,set.AlternatePair.AmpliconSequence,set.ControlPair.AmpliconSequence,set.AlternateControlAmpliconSequence,ReportWriter.Params(s, true),r.SearchTruncated?"是":"否",String.Join("；",p.Warnings.ToArray())+"；"+String.Join("；",set.Notes.ToArray())+"；实验候选，未证明等位基因特异性；三种组合分开反应；未做基因组特异性检索；产物为扩增发生时的预期序列"};
                    if(i>=2){row[12]="0";row[13]="";row[14]="";row[15]="";}
                    row[30]+="；"+Scoring.Replace("\r\n","；");
                    row[30]+="；参考反应："+String.Join("；",set.ReferencePair.Warnings.ToArray())+"；替代反应："+String.Join("；",set.AlternatePair.Warnings.ToArray())+"；对照反应："+String.Join("；",set.ControlPair.Warnings.ToArray());
                    for(int j=0;j<row.Length;j++){if(j>0)b.Append(',');b.Append(Cell(row[j]));}b.AppendLine();
                }
            }
            return b.ToString();
        }
        public static string Fasta(SnpDesignResult r)
        {
            var b=new StringBuilder();
            foreach(var s in r.Sets)
            {
                string[] roles={"F_ref_"+r.Input.ReferenceAllele,"F_alt_"+r.Input.AlternateAllele,"R_common","F_control"};
                Primer[] p={s.ReferenceForward,s.AlternateForward,s.CommonReverse,s.ControlForward};
                for(int i=0;i<4;i++){b.AppendLine(">SNP_"+s.Rank+"_"+roles[i]+" 5to3 candidate_unvalidated SNP="+r.Input.Position+" extra_mismatch="+(i<2?s.ExtraMismatchPosition:0));b.AppendLine(p[i].Sequence);}
            }
            return b.ToString();
        }
    }
}
